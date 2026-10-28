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

## 2. 两种包怎么选

| | 自包含（portable） | 框架依赖（framework-dependent） |
| --- | --- | --- |
| 文件名 | `ckan-ui-<ver>-win-x64-portable-unsigned.zip` | `ckan-ui-<ver>-win-x64-framework-dependent-unsigned.zip` |
| zip 体积 | 63.7 MB | 29.7 MB |
| 解压体积 | 150 MB / 280 条目 | 73 MB / 93 条目 |
| 前置依赖 | 无（自带 .NET 10 运行时） | 需 .NET 10 Desktop Runtime |
| 首次启动 | 0.81 s（R2R，stage 目录实测）；zip 解压后首启 1.08 s | 0.80–0.83 s（同开 R2R，实测） |
| 适用 | 内网 / 无外网 / 不想装运行时 / 一般用户 | 已装运行时、在意下载体积、CI 内部试用 |

默认推荐自包含版：M6 的验收口径是「干净机器能跑」，少一个前置条件是少一类工单。框架依赖版是体积敏感场景的补充，不是主推。

## 3. 发布参数为什么这么定

### 3.1 不裁剪（`PublishTrimmed=false`）

Avalonia 的 XAML 编译绑定按字符串找成员，FluentAvalonia 与 CKAN Core 大量用 `Newtonsoft.Json` 反射反序列化 `CkanModule`、`Repository`，插件加载靠 `Assembly.Load`。这些都不是 trim-safe 的：裁剪后编得过、运行时在冷门路径上抛 `MissingMethodException`。裁掉的收益（约 20–30 MB）无法用自动化验证，而 M6 的验收恰好是「干净机器能跑」——风险不可控，收益不可验证，所以不做。将来若要做，必须先有完整的冒烟用例集。

### 3.2 开 ReadyToRun（`PublishReadyToRun=true`）

实测冷启动（同一台机、同一次构建会话、每档跑 2 次取代表值）：

| 变体 | 冷启动 | 解压体积 | zip 体积 |
| --- | --- | --- | --- |
| 自包含 + R2R | 813 / 806 ms | 150 MB | 63.7 MB |
| 自包含（无 R2R） | 1601 / 1572 ms | 118 MB | 未测 |
| 框架依赖 + R2R | 830 / 803 ms | 73 MB | 29.7 MB |
| 框架依赖（无 R2R） | 1559 / 1561 ms | 40.9 MB | 15.4 MB |

R2R 把两种变体的冷启动都砍掉约 **49%**（1.58 s → 0.81 s），代价是解压体积 +32 MB 的预编译镜像。启动速度是用户每次开程序都能感知的，下载体积只感知一次，所以两种变体都开 R2R。09 篇 §11 的定稿也是 `-p:PublishReadyToRun=true`，与实测方向一致。

框架依赖版这里有个真实的取舍要知道：开 R2R 后它的 zip 从 15.4 MB 涨到 **29.7 MB**（+92%），而框架依赖版的卖点本来就是下载小。仍然选择开——14 MB 是一次性下载成本（几秒钟），0.75 s 是每天每次启动都要付的成本；何况 29.7 MB 相对自包含版的 63.7 MB 仍不到一半。如果将来出现「必须压到 20 MB 以内」的分发渠道，把 `release.ps1` 里框架依赖那一路的 `PublishReadyToRun` 改成 `false` 即可，其余链路不用动。

### 3.3 不做单文件（`PublishSingleFile=false`）

单文件包首次启动要把内嵌程序集解压到临时目录（`%TEMP%.net`），冷启动明显变慢，而且杀软对「解压式自解压」更敏感。09 篇 §11 已否决。

### 3.4 包里不带调试符号（排除 `*.pdb`）

publish 默认会带 pdb：自包含版 14 个 pdb 就占 **101 MB**（其中 `libSkiaSharp.pdb` 单文件 84 MB、`libHarfBuzzSharp.pdb` 20 MB）。这些是原生库的调试符号，对用户毫无用处。脚本在组装 stage 时按 `*.pdb` 排除，并在打包断言里检查 zip 内 pdb 数量必须为 0。

### 3.5 体积与规格预算的差额

09 篇 §11 给的是 **60–90 MB** 的磁盘预算，实测自包含解压 **150 MB**（zip 63.6 MB）。差额来源：`libSkiaSharp.dll`（约 12 MB）、`libHarfBuzzSharp.dll`、`av_libglesv2.dll` 等原生库，加上 R2R 镜像（约 +32 MB）。09 篇 §5 自己给的估算就是 90–110 MB，与实测 118 MB（无 R2R）在同一量级，所以更可能是 §11 的预算按「无 R2R」写的。按 §11 的规则「超预算须在发布说明里解释」，发布说明照写：体积主要来自 Skia/HarfBuzz 原生库与 R2R，去掉 R2R 可回到 118 MB 但冷启动翻倍。

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

解压后**根目录就是应用目录**（zip 内没有包裹目录，解压到哪，哪就是安装位置），直接双击 `CkanUI.App.exe`：

```
<你选定的目录>/
  CkanUI.App.exe              入口（程序集名是 CkanUI.App，故 exe 带 .App 后缀）
  CkanUI.*.dll                自研程序集（App/ViewModels/Services/Services.Abstractions/Controls/Core/Localization/ModText/Platform/Sfs）
  CKAN.dll CKAN.xml           上游 CKAN Core 及其 XML 文档
  Avalonia*.dll SkiaSharp.dll libSkiaSharp.dll HarfBuzzSharp.dll ...   渲染栈与原生库
  log4net.dll log4net.xml     日志配置（必须与 exe 同目录，程序启动时会切到程序目录）
  Assets/ckan.ico             应用图标（csproj 里以 Content 复制）
  i18n/packs/{en-US,zh-CN}.json   语言包
  README.txt                  面向用户的使用说明
  THIRD-PARTY-NOTICES.txt     第三方组件与许可证清单
  licenses/CKAN-LICENSE.md    上游 CKAN 的 MIT 许可证原文
  （自包含版另有 coreclr.dll / clrjit.dll / hostfxr.dll / hostpolicy.dll 等运行时）
```

zip 条目数 280 = stage 文件 277 + 后注入的 3 个（README、NOTICES、licenses）。配置与日志**不**在程序目录：配置走 `%LocalAppData%\CKAN\config.json`、日志走 `%LocalAppData%\CKAN\logs\`。程序目录只读（装在 Program Files、只读分卷、解压到受控目录）时，`Program.EnsureConfigFileWritable` 会把 `CKAN_CONFIG_FILE` 指到程序目录下的 `config.json` 并打一条警告日志——所以便携包在任何位置都能起来，只是配置落点会变。

## 6. 冒烟检查单

自动化部分（`-SmokeTest`，脚本 [`Test-PortableLaunch.ps1`](../../tools/release/Test-PortableLaunch.ps1) 完成）：

1. 解压自包含 zip 到临时目录（不动已安装的版本）。
2. 起进程，轮询主窗口（默认最多 90 s），标题必须是 `CKAN UI`。
3. 关闭并清理临时目录；结果写进 manifest 的 `smokeTest` 段。
4. 若本机已有 CkanUI 在跑，直接跳过（单实例守卫会让新进程转发参数后退出，误判为失败）→ 退出码 2。

本机实测：`window up in 1049 ms title=[CKAN UI]`（2026-10-29，自包含 zip 解压后首启；从 stage 目录直接启动是 0.81 s，差在首次解压与磁盘缓存）。

需要人眼的部分（CI 跑不了，发版前手动过一遍）：

- [ ] 主窗口起来了，左侧导航栏 16 项全在，无空白页。
- [ ] 关于对话框：版本显示 `v0.1.0 (26.g4a888a5)` 形式，CKAN Core 版本与 manifest 的版本一致。
- [ ] 语言切到英文再切回中文，界面文案跟着变（`i18n/packs` 是否真被打进包）。
- [ ] `%LocalAppData%\CKAN\logs\` 下有当次启动日志，内容无 `ERROR`/异常堆栈。
- [ ] 换一个**没有装过 .NET** 的干净 Win10 机器起自包含版（这是 PKG-01 的验收口径，本仓库沙箱无法替代）。

## 7. 未签名分发

本期包**没有代码签名**（manifest `signed=false`，文件名带 `-unsigned`）。因此：

1. 发布页必须同时贴出 `SHA256SUMS.txt` 的内容（09 篇 §12 / 19 篇 PKG-09 的要求）。
2. 用户第一次运行会被 SmartScreen 拦，需要「更多信息 → 仍要运行」；README.txt 里已写明。
3. 下载后自校：

```bash
certutil -hashfile ckan-ui-0.1.0-win-x64-portable-unsigned.zip SHA256
# 或
sha256sum -c SHA256SUMS.txt
```

签名属后续里程碑（PKG-09 预留 SignPath 接入位），接入后：文件名去掉 `-unsigned`、manifest `signed` 改 `true`、`release.ps1` 增加一个签名步骤即可，其余不动。

## 8. 回滚

产物是**完全自包含的目录树**，没有安装器、没有注册表写入、没有系统级依赖，所以回滚等价于「换回上一版 zip 解压」：

1. 每个版本的 `artifacts/release/` 产物连同 `manifest.json` 一起留档（不要只留 zip：manifest 是那一版到底用哪个 commit、什么构建参数的唯一证据）。
2. 回滚步骤：删掉/改名当前解压目录 → 解压上一版 zip → 校验其 `sha256` 与留档 manifest 一致 → 启动确认。
3. 用户配置与已装 Mod 不受影响（它们不在程序目录里），所以回滚不会丢数据。
4. 版本号**只前进不回退**：修好后再发一个更高的 patch（`0.1.2`），不要重发同号的包——重号会让 `SHA256SUMS.txt` 与用户手里的包对不上。

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
| 主程序名 | 产物是 `CkanUI.App.exe`（`AssemblyName=CkanUI.App`） | 09 篇 §2.1 写的是 `CkanUI.exe`；改名会牵动 `avares://CkanUI.App/` 资源 URI 与 `ViewLocator`，需单独评估 |
| 包体积 | 自包含解压 150 MB，超 §11 的 60–90 MB 预算 | 在发布说明里解释（§3.5）；若将来必须压回去，先做 R2R 取舍 |
| 项目许可证 | 关于页文案已改为双声明（CKAN UI 按 GPL-3.0、内置 CKAN 核心库按上游 MIT，见 `AboutDialogViewModel.LicenseText` 与 `AboutDialogTests.LicenseText_SeparatesUiAndUpstreamLicenses`）；仓库根仍没有 `LICENSE.md` | 需产品口径确认后再补根 `LICENSE.md` 承载 GPL-3.0 全文，并在发布包 `licenses/` 下同时放 MIT 与 GPL 两份 |
| 安装器 | 不做（09 篇 §5 明确） | 属后续里程碑 |
| macOS / Linux | 未冒烟（M6 不含，属 PKG-04） | 需要真机 |
| 图标各尺寸 | 用 `external/CKAN/assets` 的 6 档 PNG 合成 PNG-entry ICO | 若设计给出矢量图标，替换 `src/CkanUI.App/Assets/ckan.ico` |

## 修订记录

| 日期 | 变更 |
| --- | --- |
| 2026-10-29 | 首版：M6 / PKG-01..03 出包链路、参数 Rationale、冒烟检查单、未签名分发与回滚 |
