# 发布手册（win-x64 便携包）

> M6 / PKG-01..03 的落地说明：一条命令怎么出包、两种包怎么选、每一步为什么这么定、出问题怎么回滚。
> 与 [design/packaging.md](../design/packaging.md) 分工：那篇讲设计决策与 manifest 字段定义，本篇是可照抄的操作步骤。
> 机器前置（\`external/\` 两个依赖、NuGet 本地镜像、上游多 TFM 竞态）见 [build-environment.md](build-environment.md)；代码与提交纪律见 [engineering-conventions.md](engineering-conventions.md)。
> 规格依据：[plan/09-migration-packaging.md](../plan/09-migration-packaging.md) §5/§11/§12、[plan/06-wbs-milestones.md](../plan/06-wbs-milestones.md) M6。

## 1. 一条命令出包

```bash
# 最常用：出两个包，工作树脏也继续（dirty 标记写进 manifest）
powershell.exe -NoProfile -ExecutionPolicy Bypass -File tools/release/release.ps1

# 发布前：干净树门禁 + 解压后真启动到主窗口的冒烟
powershell.exe -NoProfile -ExecutionPolicy Bypass -File tools/release/release.ps1 -SmokeTest -RequireClean

# 覆盖版本号（不写版本文件，只影响这一次产物）
powershell.exe -NoProfile -ExecutionPolicy Bypass -File tools/release/release.ps1 -Version 0.1.1
```

| 参数 | 默认 | 作用 |
| --- | --- | --- |
| `-Version` | 读 `src/CkanUI.App/Version.props` 的 `CkanUiVersion` | 覆盖版本号；格式 `x.y.z` 或 `x.y.z-后缀` |
| `-Runtime` | `win-x64` | 目标 RID；M6 只做这一档 |
| `-OutDir` | `<仓库根>/artifacts/release` | 产物目录（`artifacts/` 已在 .gitignore 内） |
| `-RequireClean` | 关 | 工作树脏就中止；默认只把 `source.dirty=true` 写进 manifest |
| `-SmokeTest` | 关 | 解压自包含 zip 到临时目录、真启动、等主窗口出现再关；结果进 manifest |
| `-NoInstaller` | 关 | 跳过原生单文件安装程序（CkanUISetup.exe）的编译构建 |
| `-KeepWork` | 关 | 保留中间目录 `_work`（publish 输出、发布日志、stage 内容） |
| `-Configuration` | `Release` | 构建配置；出包不该用 Debug |

脚本本体：[`tools/release/release.ps1`](../../tools/release/release.ps1)。要求 Windows PowerShell 5.1 及以上（CI 与本机都满足），**不依赖 pwsh 7**。

### 1.1 十二步流程

| 步 | 做什么 | 失败时的典型原因 |
| --- | --- | --- |
| 1 | 解析版本与源码信息（`git rev-list --count`、`git rev-parse --short`、`git status --porcelain`） | 版本文件缺失 / 版本号不合格式 |
| 2 | 环境自检（dotnet 版本、OS、`external/CKAN` 是否存在） | 缺 CKAN Core（见 build-environment.md） |
| 3 | 准备输出目录（清空 `_work`，删前校验目录名必须是 `_work`） | 产物目录被占用 |
| 4 | `dotnet publish` 自包含（`--self-contained true`） | NU1301 = NuGet 源不可用（**不改源，先修环境**） |
| 5 | 组装自包含便携目录（排除 `*.pdb`、注入 README/NOTICES/licenses） | stage 缺必需文件 |
| 6 | 打自包含 zip，并断言 9 个必需条目齐全、0 个 pdb | 内容断言失败 |
| 7 | `dotnet publish` 框架依赖（`--self-contained false`） | 同上 |
| 8 | 组装框架依赖便携目录 | 同上 |
| 9 | 打框架依赖 zip + 内容断言 | 同上 |
| 10 | 冒烟（仅 `-SmokeTest`）：解压自包含 zip → 启动 → 等主窗口 → 关闭 | 见 §6 |
| 11 | 写 `SHA256SUMS.txt`（ASCII、两空格分隔，兼容 `sha256sum -c`）与 `manifest.json` | 磁盘满 |
| 12 | 汇总（打印体积、哈希、产物目录） | — |

publish 传的参数固定为：`-c Release -r win-x64 -p:Version=<ver> -p:CkanUiVersion=<ver> -p:CkanUiCommitCount=<n> -p:CkanUiCommitSha=<sha> -p:PublishReadyToRun=true -p:PublishTrimmed=false -p:PublishSingleFile=false`。版本三元组由脚本显式传入，所以**即使不 `git tag`，产物版本也来自同一处**。

### 1.2 本机实测（2026-10-29，commit `4a888a5` / 计数 26 / 工作树 dirty）

> 下面的哈希只是某一次实际运行的样本：并行开发期工作树天天在变，**不要把本篇的哈希当校验值**，一律以产物目录里的 `SHA256SUMS.txt` 为准。体积与体积占比是稳定的。

```
[4] 发布 portable（自包含（自带运行时））
    发布输出 26 行，警告 0 条
[5] 组装 portable 便携目录
    自包含版（self-contained）：277 个文件 / 150 MB（已排除 pdb）
[6] 打包 portable zip
    ckan-ui-0.1.0-win-x64-portable-unsigned.zip：64 MB（280 个条目），必需条目齐全、无 pdb
[7] 发布 framework-dependent（框架依赖（需 .NET 10 Desktop Runtime））
    发布输出 23 行，警告 0 条
[8] 组装 framework-dependent 便携目录
    框架依赖版（framework-dependent）：90 个文件 / 73 MB（已排除 pdb）
[9] 打包 framework-dependent zip
    ckan-ui-0.1.0-win-x64-framework-dependent-unsigned.zip：30 MB（93 个条目），必需条目齐全、无 pdb
[10] 冒烟：解压自包含 zip 并真启动到主窗口
    RESULT: window up in 1049 ms title=[CKAN UI] pid=11012
[12] 完成
    ckan-ui-0.1.0-win-x64-portable-unsigned.zip
      大小 63.7 MB，解压后 150 MB / 280 个文件
      sha256 32815b28825370471773065ec1d7b3398212c8730c75fe251263447a5594a495
    ckan-ui-0.1.0-win-x64-framework-dependent-unsigned.zip
      大小 29.7 MB，解压后 73 MB / 93 个文件
      sha256 881300aecf3886594caf61cf3613c519aa4c87ea5a05903fc631c954f7740016
```

> 上面是 v1.3 的样本（包内容还没有 `LICENSE.md` 与 `install/`）。v1.4 起包内新增 `LICENSE.md` 与 `install/` 三个脚本，自包含 stage 280 文件 / zip 284 条目、框架依赖 zip 97 条目，体积与冷启动的四形态实测见 §3.5。

`artifacts/release/` 实际内容（`ls -la`）：

```
SHA256SUMS.txt                                             231 B   （LF + ASCII）
manifest.json                                             2265 B
ckan-ui-0.1.0-win-x64-portable-unsigned.zip        66787377 B  (63.7 MB / 280 条目)
ckan-ui-0.1.0-win-x64-framework-dependent-unsigned.zip 31113332 B  (29.7 MB / 93 条目)
_work/                                              中间目录（publish/stage/日志）
```

校验和可直接用 GNU 工具回验（`sha256sum -c` 对 CRLF 会报「找不到文件」，所以 `SHA256SUMS.txt` 是 **LF + ASCII**）：

```bash
cd artifacts/release && sha256sum -c SHA256SUMS.txt
# ckan-ui-0.1.0-win-x64-portable-unsigned.zip: OK
# ckan-ui-0.1.0-win-x64-framework-dependent-unsigned.zip: OK
```

## 2. 三种发布形态怎么选

| | 原生安装向导（setup.exe） | 自包含便携包（portable.zip） | 框架依赖（framework-dependent.zip） |
| --- | --- | --- | --- |
| 文件名 | `ckan-ui-<ver>-win-x64-setup-unsigned.exe` | `ckan-ui-<ver>-win-x64-portable-unsigned.zip` | `ckan-ui-<ver>-win-x64-framework-dependent-unsigned.zip` |
| 文件体积 | ~85 MB（单文件内嵌全量包） | 49.7 ~ 63.7 MB | 15.7 ~ 29.7 MB |
| 解压/安装体积 | ~119 MB（装入 %LocalAppData%\CKAN UI） | 119 ~ 152 MB / 284 文件 | 42 ~ 75.5 MB / 97 文件 |
| 前置依赖 | 无（自包含独立执行） | 无（自带 .NET 10 运行时） | 需 .NET 10 Desktop Runtime |
| 安装与卸载 | 双击直接引导安装，自带控制面板卸载 | 手工解压即用，无系统快捷方式 | 手工解压即用 |
| 适用 | 普通最终用户首选、企业批量静默部署 | 尝鲜、U 盘随身带、无需安装的机器 | 已装运行时、在意下载体积 |

默认推荐原生安装向导或自包含便携版：验收口径是「干净机器能跑」，零前置依赖。

## 3. 发布参数为什么这么定

### 3.1 不裁剪（`PublishTrimmed=false`）

Avalonia 的 XAML 编译绑定按字符串找成员，FluentAvalonia 与 CKAN Core 大量用 `Newtonsoft.Json` 反射反序列化 `CkanModule`、`Repository`，插件加载靠 `Assembly.Load`。这些都不是 trim-safe 的：裁剪后编得过、运行时在冷门路径上抛 `MissingMethodException`。裁掉的收益（约 20–30 MB）无法用自动化验证，而 M6 的验收恰好是「干净机器能跑」——风险不可控，收益不可验证，所以不做。将来若要做，必须先有完整的冒烟用例集。

### 3.2 开 ReadyToRun（`PublishReadyToRun=true`）

实测冷启动（同一台机、同一次构建会话、每档跑 2 次取代表值）：

| 变体 | 冷启动 | 解压体积 | zip 体积 |
| --- | --- | --- | --- |
| 自包含 + R2R | 813 / 806 ms | 150 MB | 63.7 MB |
| 自包含（无 R2R，`-NoReadyToRun`） | 1601 / 1572 ms | 119 MB | 49.7 MB |
| 框架依赖 + R2R | 830 / 803 ms | 73 MB | 29.7 MB |
| 框架依赖（无 R2R，`-NoReadyToRun`） | 1559 / 1561 ms | 42 MB | 15.7 MB |

R2R 把两种变体的冷启动都砍掉约 **49%**（1.58 s → 0.81 s），代价是解压体积 +32 MB 的预编译镜像。启动速度是用户每次开程序都能感知的，下载体积只感知一次，所以两种变体都开 R2R。09 篇 §11 的定稿也是 `-p:PublishReadyToRun=true`，与实测方向一致。

两种变体的 R2R 都由 `release.ps1 -NoReadyToRun` 一键关掉：它把 `-p:PublishReadyToRun=false` 传给 publish，并把 `publishOptions.publishReadyToRun` 写进 manifest（可追溯哪个包是省体积版）。

框架依赖版这里有个真实的取舍要知道：开 R2R 后它的 zip 从 15.4 MB 涨到 **29.7 MB**（+92%），而框架依赖版的卖点本来就是下载小。仍然选择开——14 MB 是一次性下载成本（几秒钟），0.75 s 是每天每次启动都要付的成本；何况 29.7 MB 相对自包含版的 63.7 MB 仍不到一半。如果将来出现「必须压到 20 MB 以内」的分发渠道，把 `release.ps1` 里框架依赖那一路的 `PublishReadyToRun` 改成 `false` 即可，其余链路不用动。

### 3.3 不做单文件（`PublishSingleFile=false`）

单文件包首次启动要把内嵌程序集解压到临时目录（`%TEMP%.net`），冷启动明显变慢，而且杀软对「解压式自解压」更敏感。09 篇 §11 已否决。

### 3.4 包里不带调试符号（排除 `*.pdb`）

publish 默认会带 pdb：自包含版 14 个 pdb 就占 **101 MB**（其中 `libSkiaSharp.pdb` 单文件 84 MB、`libHarfBuzzSharp.pdb` 20 MB）。这些是原生库的调试符号，对用户毫无用处。脚本在组装 stage 时按 `*.pdb` 排除，并在打包断言里检查 zip 内 pdb 数量必须为 0。

### 3.5 体积与规格预算的差额

09 篇 §11 给的是 60–90 MB 预算，2026-10-29 四档实测如下（release.ps1 真实出包，体积已排除 pdb）：

| 形态 | 解压后 | zip | 与 60–90 MB 预算比 |
| --- | --- | --- | --- |
| 自包含（默认，R2R） | 152 MB / 277 文件 | 63.7 MB / 281 条目 | 下载在预算内，解压超 |
| 自包含（-NoReadyToRun） | 119 MB / 281 条目 | 49.7 MB | 下载在预算内，解压仍超 |
| 框架依赖（默认，R2R） | 75.5 MB / 90 文件 | 29.7 MB | 两者都在预算内 |
| 框架依赖（-NoReadyToRun） | 42 MB / 94 条目 | 15.7 MB | 两者都在预算内 |

口径修订（09 篇 §14 同步回填）：§11 的 60–90 MB 应按**下载包（zip）**读——默认自包含 63.7 MB 落在区间内。**解压体积另有地板**：.NET 自包含运行时约 95 MB（System.Private.CoreLib 15.3 MB、coreclr 4.4 MB、System.Private.Xml 7.4 MB 等）+ libSkiaSharp.dll 11.1 MB + av_libglesv2.dll 5.1 MB + 自研与 CKAN 程序集，任何自包含形态都做不到 60–90 MB 解压。要更小只有两条路：关 R2R（152 → 119 MB，冷启动 0.81 → 1.6 s）或改用框架依赖版（75.5 / 42 MB，需用户自装 .NET 10 Desktop Runtime）。

被 external 仓库挡死的两条路（不要再试）：-p:PublishSingleFile=true 与 -p:PublishTrimmed=true 都会给所有工程注入 Microsoft.NET.ILLink.Tasks，而 external/UniversalI18n 的 src/UI18n.Core/UI18n.Core.csproj 带一条「必须零第三方依赖」的硬门禁，实测直接报 error : UI18n.Core 必须零第三方依赖：禁止 PackageReference 'Microsoft.NET.ILLink.Tasks'，publish 退出码 1。要解只能改上游仓库，不在本仓库范围。

另有约 6.2 MB 可删的调试残留（mscordbi.dll 1.2 MB、Microsoft.DiaSymReader.Native.amd64.dll 2.3 MB、mscordaccore*.dll 各 1.3 MB、createdump.exe）：它们不进 zip 也无害，删了会破坏事后调试栈，收益小于风险，不删。

## 4. 版本号：单一真源

- **唯一手改点**：[`src/CkanUI.App/Version.props`](../../src/CkanUI.App/Version.props) 的 `<CkanUiVersion>`（默认 `0.1.0`）。全仓没有第二处写版本号：`Directory.Build.props` 不设 `Version`，`CkanUI.App.csproj` 只 `Import` 这个文件。
- 为什么放在项目目录而不是 `build/`：`tools/verify-layout` 的 H-01 只允许仓库根层出现白名单条目（`build/` 不在白名单里），而 `src/` 是白名单内的目录，且这个 props 只被 `CkanUI.App` 导入。
- 为什么不用 `Directory.Build.props`：那是共享文件（多人在改），且版本号属于「应用产物」的属性，不该给所有项目（含测试与工具）都盖一个 `Version`。

词法（`Version.props` 内容）：

| 属性 | 值 | 落到哪里 |
| --- | --- | --- |
| `Version` | `0.1.0` | 程序集版本 `AssemblyVersion`/文件版本基础 |
| `AssemblyVersion` | `0.1.0.0` | CLR 绑定用 |
| `FileVersion` | `0.1.0.<提交数>` → 实测 `0.1.0.26` | 资源管理器「详细信息」 |
| `InformationalVersion` | `0.1.0+26.g4a888a5` | 关于页显示的提交后缀 |
| `IncludeSourceRevisionInInformationalVersion` | `false` | 禁用 SDK 自动追加的 `+<全 sha>`，改由我们自己拼稳定词法 |

关于页（[`AboutInfoFactory.cs`](../../src/CkanUI.App/AboutInfoFactory.cs)）读 `AssemblyInformationalVersion`，取 `+` 之后的部分，拼成 `v0.1.0 (26.g4a888a5)`。提交数来自 `git rev-list --count HEAD`，短哈希来自 `git rev-parse --short HEAD`，由 `release.ps1` 以 MSBuild 属性传入——所以**不设 `git tag` 也能得到可追溯的版本词法**；本地开发直接 `dotnet build` 时没有这三个属性，关于页退化成 `v0.1.0`。

## 5. 便携包布局

解压后**根目录就是应用目录**（zip 内没有包裹目录，解压到哪，哪就是安装位置），直接双击 `CkanUI.exe`：

```
<你选定的目录>/
  CkanUI.exe              入口（程序集名 CkanUI，与 09 篇 §2.1 一致）
  CkanUI.*.dll                自研程序集（App/ViewModels/Services/Services.Abstractions/Controls/Core/Localization/ModText/Platform/Sfs）
  CKAN.dll CKAN.xml           上游 CKAN Core 及其 XML 文档
  Avalonia*.dll SkiaSharp.dll libSkiaSharp.dll HarfBuzzSharp.dll ...   渲染栈与原生库
  log4net.dll log4net.xml     日志配置（必须与 exe 同目录，程序启动时会切到程序目录）
  Assets/ckan.ico             应用图标（csproj 里以 Content 复制）
  i18n/packs/{en-US,zh-CN}.json   语言包
  README.txt                  面向用户的使用说明
  THIRD-PARTY-NOTICES.txt     第三方组件与许可证清单
  LICENSE.md                  本项目（CKAN UI）的 GPL-3.0 许可证全文
  licenses/CKAN-LICENSE.md    上游 CKAN 的 MIT 许可证原文
  install/                    per-user 安装脚本（install.ps1 / uninstall.ps1 / README.txt）
  （自包含版另有 coreclr.dll / clrjit.dll / hostfxr.dll / hostpolicy.dll 等运行时）
```

zip 条目数 284 = stage 文件 284（含后注入的 README、NOTICES、`LICENSE.md`、`licenses/` 与 `install/` 的三个脚本）。配置与日志**不**在程序目录：配置走 `%LocalAppData%\CKAN\config.json`、日志走 `%LocalAppData%\CKAN\logs\`。程序目录只读（装在 Program Files、只读分卷、解压到受控目录）时，`Program.EnsureConfigFileWritable` 会把 `CKAN_CONFIG_FILE` 指到程序目录下的 `config.json` 并打一条警告日志——所以便携包在任何位置都能起来，只是配置落点会变。

## 6. 冒烟检查单

自动化部分（`-SmokeTest`，脚本 [`Test-PortableLaunch.ps1`](../../tools/release/Test-PortableLaunch.ps1) 完成）：

1. 解压自包含 zip 到临时目录（不动已安装的版本）。
2. 起进程，轮询主窗口（默认最多 90 s），标题必须是 `CKAN UI`。
3. 关闭并清理临时目录；结果写进 manifest 的 `smokeTest` 段。
4. 若本机已有 CkanUI 在跑，直接跳过（单实例守卫会让新进程转发参数后退出，误判为失败）→ 退出码 2。

本机实测：`window up in 1049 ms title=[CKAN UI]`（2026-10-29，自包含 zip 解压后首启；从 stage 目录直接启动是 0.81 s，差在首次解压与磁盘缓存）。

需要人眼的部分（CI 跑不了，发版前手动过一遍）：

- [ ] 主窗口起来了，左侧导航栏 19 项全在（含「日志」「Mod 配置」），无空白页。
- [ ] 关于对话框：版本显示 `v0.1.0 (26.g4a888a5)` 形式，CKAN Core 版本与 manifest 的版本一致。
- [ ] 语言切到英文再切回中文，界面文案跟着变（`i18n/packs` 是否真被打进包）。
- [ ] `%LocalAppData%\CKAN\logs\` 下有当次启动日志，内容无 `ERROR`/异常堆栈。
- [ ] 换一个**没有装过 .NET** 的干净 Win10 机器起自包含版（这是 PKG-01 的验收口径，本仓库沙箱无法替代）。

## 7. 未签名分发与签名通道

本期包**没有代码签名**（manifest `signing.requested=false`、`signing.applied=false`、`signed=false`，文件名带 `-unsigned`），但签名通道已经接进出包链路，拿到证书即用。因此：

1. 发布页必须同时贴出 `SHA256SUMS.txt` 的内容（09 篇 §12 / 19 篇 PKG-09 的要求）。
2. 用户第一次运行会被 SmartScreen 拦，需要「更多信息 → 仍要运行」；README.txt 里已写明。
3. 下载后自校：

```bash
certutil -hashfile ckan-ui-0.1.0-win-x64-portable-unsigned.zip SHA256
# 或
sha256sum -c SHA256SUMS.txt
```

### 7.1 拿到证书后怎么签

不用改流程，只多传参数：

```powershell
# 预演：只打印 signtool 命令行（口令显示为 ***），不真签
.\tools\release\release.ps1 -SignDryRun

# 用 PFX 真签；或 -Thumbprint 取证书存储里的证书
.\tools\release\release.ps1 -PfxPath .\artifacts\certs\dev-code-signing.pfx -PfxPassword <口令>
```

`release.ps1` 在 stage 组装完成、打 zip **之前**调 `tools/release/Sign-Artifacts.ps1`（签名必须早于打包，签完再改文件即失效），失败直接中止出包；`manifest.signing` 记 `{requested, applied, method: none|thumbprint|pfx, timestampUrl}`。签名脚本要 `signtool.exe`（Windows SDK Signing Tools），缺失时打印三条可操作提示并 exit 1；时间戳默认 `http://timestamp.digicert.com`。自签测试证书用 `tools/release/New-DevCertificate.ps1` 生成（默认导到 `artifacts/certs/`，并立即从证书存储里移除）。

仍剩：文件名后缀与 `manifest.signed` 还是按未签名硬编码，正式签名后要一次性改掉；发布页、GitHub Release 与 SignPath 属 PKG-09 的人工流程（`docs/design/installer.md` 记了决策与限制）。

## 8. 回滚

产物是**完全自包含的目录树**；便携版没有系统级依赖，安装器 `install/install.ps1` 也只写 `HKCU`（卸载项 + `App Paths`），可用包内 `uninstall.ps1` 干净移除。所以回滚等价于「换回上一版 zip 解压」：

1. 每个版本的 `artifacts/release/` 产物连同 `manifest.json` 一起留档（不要只留 zip：manifest 是那一版到底用哪个 commit、什么构建参数的唯一证据）。
2. 回滚步骤：删掉/改名当前解压目录 → 解压上一版 zip → 校验其 `sha256` 与留档 manifest 一致 → 启动确认。
3. 用户配置与已装 Mod 不受影响（它们不在程序目录里），所以回滚不会丢数据。
4. 版本号**只前进不回退**：修好后再发一个更高的 patch（`0.1.2`），不要重发同号的包——重号会让 `SHA256SUMS.txt` 与用户手里的包对不上。
5. 用安装器装过的机器：先跑安装目录下的 `uninstall.ps1`（或「设置 → 应用 → CKAN UI → 卸载」），再解压上一版 zip 或重跑上一版的 `install.ps1 -SourceDir <解压目录>`。`%LocalAppData%\CKAN`（配置、日志、已装 Mod）装与卸都刻意保留，回滚不会丢数据。

## 9. CI：`release.yml`

[`.github/workflows/release.yml`](../../.github/workflows/release.yml) 在 windows-latest 上跑同一条命令（不加 `-SmokeTest`，CI 无交互桌面），然后：

1. `dotnet run --project tools/verify-layout -c Release -- --all` 先过布局门禁。
2. 跑 `release.ps1`；`push` 的 `v*` 标签会自动带 `-RequireClean`。
3. 回验校验和：把 `manifest.json` 里的 `sha256`、`SHA256SUMS.txt`、磁盘上 zip 的实际哈希三者对齐，任一不一致就失败。
4. 写 job summary（版本 / 体积 / 文件数 / SHA256 表），上传 `artifacts/release/*.zip` + `SHA256SUMS.txt` + `manifest.json`（30 天），失败时上传 `_work/*.log`（7 天）。

**刻意不建 GitHub Release**：包未签名，正式对外发布流程（发布页 + 校验清单 + 后续签名）属 PKG-09/E7。`release.yml` 只负责「出包并归档」，人工下载 artifact 后按 §7 分发。

## 10. 已知问题与待办

| 项 | 现状 | 待办 |
| --- | --- | --- |
| 主程序名 | 产物 `CkanUI.exe`（`AssemblyName=CkanUI`） | 已按 09 篇 §2.1 统一，2026-10-29 改名并冒烟通过 |
| 包体积 | 默认（R2R）自包含 zip 63.7 MB / 解压 150 MB；加 `-NoReadyToRun` 则 zip 49.7 MB / 解压 119 MB | 已加 `-NoReadyToRun` 开关并写进 §3.5 实测表；裁剪与单文件被 external/UniversalI18n 的零第三方依赖门禁挡死（ILLink.Tasks），解压地板约 119 MB |
| 项目许可证 | 仓库根 `LICENSE.md` 已是 GPL-3.0 全文，出包时复制到包根；关于页与 `THIRD-PARTY-NOTICES.txt` 的双声明（界面 GPL-3.0 / 内置 CKAN MIT）与之对齐 | 无（`release.ps1` 缺 `LICENSE.md` 时直接中止，防止再漏） |
| 安装器 | 随包 `install/{install.ps1,uninstall.ps1}`（每用户装/卸，已实测）+ Inno 6 模板 `install/CkanUISetup.iss`（本机无 Inno，未跑 ISCC） | M13 验收口径的 `CkanUISetup.exe` 待有 Inno 的机器产出；用法见 `docs/ops/install.md` |
| 代码签名 | 通道已接（`Sign-Artifacts.ps1` 在 stage 后、打包前调用；`-SignDryRun` 预演；`manifest.signing` 记录） | 缺证书；拿到后去掉文件名 `-unsigned` 并置 `signed=true` |
| macOS / Linux | 未冒烟（M6 不含，属 PKG-04） | 需要真机 |
| 图标各尺寸 | 用 `external/CKAN/assets` 的 6 档 PNG 合成 PNG-entry ICO | 若设计给出矢量图标，替换 `src/CkanUI.App/Assets/ckan.ico` |

## 修订记录

| 日期 | 变更 |
| --- | --- |
| 2026-10-29 | 首版：M6 / PKG-01..03 出包链路、参数 Rationale、冒烟检查单、未签名分发与回滚 |
| 2026-10-29 | v1.4 回填：程序名改 `CkanUI.exe`、包内新增 `LICENSE.md` 与 `install/`、签名通道与 `-SignDryRun`、`-NoReadyToRun` 与四形态体积实测（§3.5）、安装器与签名行 |
