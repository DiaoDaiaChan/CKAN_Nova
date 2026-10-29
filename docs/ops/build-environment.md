# 构建环境手册（本机 / CI 差异与已知坑）

> 记录 2026-10-28 接手时修复的三处环境问题与对应操作。目的是让「下一个接手的人」不必重新趟一遍。
> 与 [engineering-conventions.md](engineering-conventions.md) 分工：那篇讲代码与提交纪律，本篇只讲「机器上怎么让构建跑起来」。

## 1. 两个 external/ 依赖的来历

本仓库不把上游源码入库，而是靠 `Directory.Build.props` 里的两个根变量找依赖：

| 变量 | 期望路径 | 用途 |
| --- | --- | --- |
| `CkanCoreRoot` → `CkanCoreProject` | `external/CKAN/Core/CKAN-core.csproj` | 上游 CKAN 核心（只读，不修改） |
| `UI18nCoreRoot` → `UI18nCoreProject` | `external/UniversalI18n/src/UI18n.Core/UI18n.Core.csproj` | 通用 i18n 运行时（`UI18n.Core`） |

两者都在 `.gitignore` 里，任何克隆都没有内容。缺失时的表现：

- 缺 `external/CKAN` → `error MSB3202: 未找到项目文件 ...external/CKAN/Core/CKAN-core.csproj`（构建 1 秒内失败）。
- 缺 `external/UniversalI18n` → `UI18nCoreProject` 退化成相对路径 `src/UI18n.Core/UI18n.Core.csproj`，ModText 项目引用不到。

本机接法（两者都是**目录联接**，与规格仓做法一致）：

```bash
git clone --depth 1 --branch master https://github.com/KSP-CKAN/CKAN external/CKAN   # CI 的兜底逻辑同此
cmd //c "mklink /J external\\UniversalI18n E:\\UniversalI18n"                    # 指向本机 UniversalI18n 克隆
```

> CI 的 `.github/workflows/ci.yml` 自带兜底：缺 `external/CKAN/Core/CKAN-core.csproj` 时自动 `git clone`。
> UniversalI18n 没有兜底，本地必须自己接。

## 2. NuGet 源：为什么本机走了本地镜像

### 症状

`dotnet restore` 卡住十几分钟，最后报：

```
未能从“https://api.nuget.org/v3-flatcontainer/avalonia/12.1.4/avalonia.12.1.4.nupkg”下载包“Avalonia.12.1.4”。
  Received an unexpected EOF or 0 bytes from the transport stream.
```

### 根因（两个叠加）

1. 本机开着系统代理（`HKCU\...\Internet Settings`：`ProxyEnable=1`、`ProxyServer=192.168.10.38:10808`），而 `.NET` 的 `HttpClient` 走系统代理，`curl` 不走 —— 所以「curl 能下、dotnet 下不了」。
2. `api.nuget.org` 在本网络下回 **302** 跳到 `nuget.azure.cn`，.NET 跟随后 TLS 流被截断（上面的 EOF）。

### 方案：本地 v3 源代理（不是改仓库）

- 起一个本地 v3 源，把包下载转发给可用镜像主机：`node artifacts/env/nuget-mirror.mjs`（监听 `http://127.0.0.1:18080/v3/index.json`）。
  它只提供两项能力：`PackageBaseAddress`（`/v3-flatcontainer/`）与 `RegistrationsBaseUrl`（`/v3/registration5-gz-semver2/`），全部流式转发到 `https://nuget.azure.cn`。
- 用户级 `%AppData%\NuGet\NuGet.Config` 指向它（HTTP 源必须显式 `allowInsecureConnections="true"`）：

```xml
<configuration>
  <packageSources>
    <clear />
    <add key="local-mirror" value="http://127.0.0.1:18080/v3/index.json" allowInsecureConnections="true" protocolVersion="3" />
  </packageSources>
</configuration>
```

- 让 .NET 别把本地源也塞进系统代理：`ProxyOverride` 加上本机回环（否则 .NET 会拿到代理回的 503）：

```bash
reg add 'HKCU\Software\Microsoft\Windows\CurrentVersion\Internet Settings' //v ProxyOverride //t REG_SZ //d '<local>;localhost;127.0.0.1;::1' //f
```

**注意**：这三步都是机器级配置，不在仓库里。换机 / CI 走官方源即可，不需要镜像。

## 3. 上游 CKAN 的多 TFM 竞态（MSB3073）

`external/CKAN/Core/CKAN-core.csproj` 的 `MakeAssemblyInfo` 目标在 `!Exists('../_build/meta/GlobalAssemblyVersionInfo.cs')` 时执行 `build.ps1`，
而该文件**所有 TFM 共享**：`netstandard2.0;net481;net10.0` 三个内部构建并行跑时，两个同时判定「文件不存在」→ 同时写 `_build/out/Build/VSCodeIDE/obj/.../Build.dll`：

```
CSC : error CS2012: The process cannot access the file '...\_build\out\Build\VSCodeIDE\obj\Debug\net10.0\Build.dll' because it is being used by another process.
error MSB3073: 命令“powershell.exe ... .\build.ps1 Generate-GlobalAssemblyVersionInfo”已退出，代码为 1。
```

处理：**先用单 TFM 生成一次**，文件存在后该目标自动跳过，之后可正常并行构建：

```bash
dotnet build external/CKAN/Core/CKAN-core.csproj -f net10.0 -m:1 --nologo -v q
```

兜底：整解决方案用 `dotnet build ckan-ui.sln -m:1` 串行构建（慢但不会踩竞态）。

**别删 `external/CKAN/_build/meta/`**，删了就得重新生成。

## 4. 日常命令

```bash
# 全量构建（0 警告是硬门禁）
dotnet build ckan-ui.sln --nologo -v q /nodeReuse:false

# 单条链路构建（并行开发时优先）
dotnet build src/CkanUI.Services/CkanUI.Services.csproj --nologo -v q /nodeReuse:false

# 测试：禁 dotnet test（沙箱拒绝 Testhost），走进程内 NUnitLite runner
dotnet build tests/CkanUI.ViewModels.Tests/CkanUI.ViewModels.Tests.csproj --nologo -v q
dotnet run --project tests/CkanUI.TestRunner --no-build -- ViewModels    # 参数是测试项目名/命名空间片段

# 仓库卫生门禁
dotnet run --project tools/verify-layout --no-build -- --all
```

runner 只发现 `tests/<proj>/bin/<cfg>/net10.0/<proj>.dll`，**不会自动重建**：改过测试源码必须先 build。

## 5. 本机基线（2026-10-28 修复后）

| 项 | 结果 |
| --- | --- |
| `dotnet build ckan-ui.sln` | 0 警告 0 错误（~2 分钟首次，之后增量） |
| TestRunner 全量 | 3110 用例，0 失败 0 跳过（App 163 / Architecture 21 / Core 3 / Localization 76 / ModText 19 / Services 1205 / Sfs 93 / ViewModels 1530） |
| `verify-layout --all` | 阻断 0 警告 0，结论绿 |
