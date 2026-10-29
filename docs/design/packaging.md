# 打包与版本设计（win-x64 便携包）

> M6 / PKG-01..03 的设计决策记录：为什么这样出包、为什么这样定版本词法、manifest 与 zip 的格式约束。
> 操作步骤见 [ops/release.md](../ops/release.md)；参数取舍的实测数据也在那篇 §3。
> 脚本实现：[`tools/release/release.ps1`](../../tools/release/release.ps1)、[`tools/release/Test-PortableLaunch.ps1`](../../tools/release/Test-PortableLaunch.ps1)。

## 1. 决策一览

| # | 决策 | 主要理由 | 被否决的替代 |
| --- | --- | --- | --- |
| D1 | 分发形态是「解压即用」的 zip，根目录即应用目录；安装器是**可选附加物**（包内 `install/` 每用户脚本，只写 `HKCU`） | 便携版无注册表、回滚=换回上一版 zip；09 篇 §5 明确不做 MSI 与 `HKLM` 写入 | MSI/系统级安装器、单文件 exe |
| D2 | 每次出两个包：自包含 + 框架依赖 | 06 篇 PKG-01 要求两种配置都可用；干净机器用自包含，体积敏感场景用框架依赖 | 只出一个自包含包 |
| D3 | 两种都开 `PublishReadyToRun=true` | 实测冷启动 1.57 s → 0.81 s（-49%），启动是高频可感知成本 | 都不开（体积小 32 MB）、只给自包含开 |
| D4 | `PublishTrimmed=false` | Avalonia XAML 绑定 + Newtonsoft.Json 反射 + 插件加载不是 trim-safe | 裁剪（省 20–30 MB，但验收「干净机器能跑」无法自动验证） |
| D5 | `PublishSingleFile=false` | 单文件首启要解压到临时目录，反而拖慢冷启动；09 篇 §11 已否决 | 单文件 exe |
| D6 | zip 里排除 `*.pdb` | 自包含版 pdb 占 101 MB（`libSkiaSharp.pdb` 单文件 84 MB），对用户无用 | 保留 pdb（排障价值 < 体积代价） |
| D7 | 版本号唯一手改点是 `src/CkanUI.App/Version.props` | 单一真源；不进 `Directory.Build.props`（共享文件、会被测试与工具继承） | 写进 csproj、写进 `Directory.Build.props`、只用 git tag |
| D8 | 包名带 `-unsigned`，`signed` 进 manifest | 09 篇 §12 / 19 篇 PKG-09：未签名必须显式标注 + 提供 SHA256 清单 | 不加后缀（用户误以为已签名） |
| D9 | zip 用固定时间戳 + 正斜杠条目名 | 同一份 stage 内容重复打包得到相同 sha256，便于比对「是不是同一份产物」 | 用系统时间（同内容每次哈希都变） |
| D10 | 出包脚本放 `tools/release/`，不放根层 `build/` | `verify-layout` 的 H-01 只允许根层白名单条目，`build/` 不在其中；`tools/` 是白名单目录 | 根层 `build/release.ps1`、`scripts/` |

## 2. 版本号：单一真源与词法

### 2.1 位置与导入方式

```xml
<!-- src/CkanUI.App/CkanUI.App.csproj 顶部 -->
<Import Project="$(MSBuildThisFileDirectory)Version.props" Condition="Exists('$(MSBuildThisFileDirectory)Version.props')" />
```

- 放在 `src/CkanUI.App/` 而不是仓库根：这个 props **只服务于应用产物**，给全仓项目（含测试、工具、上游 CKAN）都盖 `Version` 没有意义，而且 `Directory.Build.props` 是多人在改的共享文件。
- 用 `Condition="Exists(...)"`：文件万一缺失时退化成 SDK 默认版本而不是构建直接崩，错误信息更可读（脚本第 1 步会单独检查文件是否存在并给出明确提示）。
- 版本元素：

```xml
<CkanUiVersion Condition="'$(CkanUiVersion)' == ''">0.1.0</CkanUiVersion>
```

条件判断的作用是**允许命令行覆盖**：`release.ps1 -Version 0.1.1` 传 `-p:CkanUiVersion=0.1.1` 时以命令行值为准，文件不需要改。

### 2.2 词法

| 属性 | 词法 | 例 |
| --- | --- | --- |
| `Version` | `<CkanUiVersion>` | `0.1.0` |
| `AssemblyVersion` | SDK 从 `Version` 取前三段 | `0.1.0.0` |
| `FileVersion` | `<ver>.<提交数>` | `0.1.0.26` |
| `InformationalVersion` | `<ver>+<提交数>.g<短哈希>` | `0.1.0+26.g4a888a5` |

- `IncludeSourceRevisionInInformationalVersion=false`：SDK 默认会往 `InformationalVersion` 追加 `+<全 sha>`，且工作树脏时还会加 `.dirty`；那会让同一份源码的关于页文本随环境变化（且长到界面放不下）。关掉它，改由自己拼「提交数.短哈希」。
- 提交数 + 短哈希由 `release.ps1` 以 MSBuild 属性传入（`-p:CkanUiCommitCount=26 -p:CkanUiCommitSha=4a888a5`），props 里只在属性非空时才覆盖 `InformationalVersion`/`FileVersion`。所以直接 `dotnet build` 的开发者看到的是干净的 `0.1.0`，不需要本地装 git 钩子。
- **不用 git tag 做版本源**：tag 只是「什么时候发布」的记号，版本词法必须能从任意工作树推导（含 CI 与本地 dirty 树）。`manifest.json` 里同时记了 `commitCount`/`commitSha`/`dirty`，追溯靠它，不靠 tag。
- 关于页（[`AboutInfoFactory.cs`](../../src/CkanUI.App/AboutInfoFactory.cs)）读 `AssemblyInformationalVersionAttribute`，取 `+` 之后的部分拼成 `v0.1.0 (26.g4a888a5)`；没有后缀时就是 `v0.1.0`。UI 版本与 Core 版本分开显示（09 篇 §6：`CkanUi-<uiVer>`，不带 Core 版本）。

### 2.3 版本只前进

同号重发会让 `SHA256SUMS.txt` 与用户手里的包对不上（两个不同的 zip 都自称 `0.1.0`）。修 bug 一律发更高 patch。

## 3. 便携目录布局

- zip **没有**顶层包裹目录：解压到任意位置后，那个目录就是应用目录，`CkanUI.exe` 在根。理由：用户常把 zip 解压到 `D:\Games\CKAN UI\`，多一层 `ckan-ui-0.1.0-.../` 会让「程序在哪」变得不直观，也让快捷方式的相对路径更难写。
- `log4net.xml`、`i18n/packs/`、`Assets/` 必须与 exe 同目录：`LoggingSetup.Initialize()` 会先 `Directory.SetCurrentDirectory(AppContext.BaseDirectory)` 再按相对路径找 `log4net.xml` 与语言包。
- 配置与日志**不**进程序目录：配置在 `%LocalAppData%\CKAN\config.json`、日志在 `%LocalAppData%\CKAN\logs\`。程序目录常常是只读的（装在 Program Files 或只读分卷），写配置会失败；`Program.EnsureConfigFileWritable()` 兜底把 `CKAN_CONFIG_FILE` 指到程序目录。
- stage（自包含 277 文件）→ zip（281 条目）的差值固定为 4：脚本在 stage 之外再注入 `README.txt`、`THIRD-PARTY-NOTICES.txt`、`LICENSE.md`（本项目 GPL-3.0）、`licenses/CKAN-LICENSE.md`（上游 MIT）。**不要**把这些塞进 publish 输出目录——那是 MSBuild 的地盘，下次 publish 会被清掉。
- README/NOTICES 由模板生成（[`tools/release/package/`](../../tools/release/package/)），占位符 `{{VERSION}}`/`{{FLAVOR_NAME}}`/`{{RUNTIME_NOTE}}`/`{{SIZE_NOTE}}` 在打包时替换：两个包的说明必须写明「要不要装运行时」，靠模板只维护一份。

## 4. 确定性 zip

打包实现（`New-DeterministicZip`）做了三件事：

1. 条目名用 `/`（zip 规范要求，Windows 的 `\` 会让 Linux/macOS 的解压工具建出奇怪的文件名）。
2. 所有条目的 `LastWriteTime` 固定为 `1980-01-01`（zip 格式能表示的最早时间），不用文件的真实修改时间。
3. 用 `ZipArchive` 的 `Create` 模式按相对路径排序逐个写入**文件条目**（不写目录条目，解压时目录自动创建），不依赖文件系统的枚举顺序。

**边界**：确定性只覆盖「同一份 stage 内容 → 相同 zip 字节」。`dotnet publish` 的输出本身不是字节级可复现的（编译时间戳、R2R 镜像、文件顺序都会变），所以**不能**用「同一 commit 两次构建哈希相同」来验证；能做到的是「同一份 publish 输出重复打包哈希相同」——出包后如果只是想确认「这个 zip 是不是刚打的那个」，重打包一次比对即可。

## 5. manifest.json（schemaVersion 1）

每次出包在产物目录写一份，是「这一版到底是怎么来的」的唯一证据。字段：

| 字段 | 类型 | 说明 |
| --- | --- | --- |
| `schemaVersion` | int | 目前 `1`；破坏性改字段必须 +1 |
| `product` | string | `CKAN UI` |
| `version` | string | 同 `CkanUiVersion`（不含提交词法） |
| `runtime` | string | RID，本版恒 `win-x64` |
| `configuration` | string | `Release` |
| `signed` | bool | 本版恒 `false`；签名接入后改 `true` |
| `builtAtUtc` | string | ISO-8601 UTC |
| `versionSource` | string | 版本来源文件路径（相对仓库根），便于回答「版本写在哪」 |
| `source.commitCount` / `source.commitSha` / `source.dirty` | int/string/bool | 源码状态；`dirty=true` 说明产物不可从某个 commit 精确复现 |
| `toolchain.dotnetSdk` / `toolchain.os` / `toolchain.powershell` | string | 复现所需的最小环境信息 |
| `publishOptions.publishReadyToRun` / `publishTrimmed` / `publishSingleFile` / `excludedPatterns` | bool/bool/bool/string[] | 构建开关；`excludedPatterns` 让「包里为什么没有 pdb」有据可查 |
| `artifacts[]` | array | 每个 zip 一项：`file`、`selfContained`、`sizeBytes`、`sha256`、`fileCount`、`uncompressedBytes` |
| `smokeTest.performed` / `.result` / `.reason` / `.log` | bool/string/string/string | 冒烟是否跑了、结论、日志相对路径 |

设计取舍：

- **sha256 同时出现在 manifest 与 `SHA256SUMS.txt`**：manifest 给机器读（CI 回验、发布页渲染），`SHA256SUMS.txt` 给人读且兼容 `sha256sum -c`。两者不一致就是出包流程出错，CI 会失败。
- `fileCount`/`uncompressedBytes` 记的是 zip 内条目数与解压后总字节数，用来让「体积变了」有可比对象。
- 不记 MSBuild 命令行全文：太长且含本机路径（`verify-layout` 的 H-10 禁止入库绝对路径，虽然 manifest 在 artifacts/ 下不入库，但同一套生成代码不该产出带本机路径的东西）。
- `schemaVersion` 演进规则：只加字段 +1 不改；删字段/改语义必须提升主版本，并在 `CHANGELOG.md` 写明。

`SHA256SUMS.txt` 格式：每行 `<64 位小写十六进制><两个空格><文件名>`，ASCII 编码、LF 换行（`sha256sum -c` 的格式）。

## 6. 明确不做的决策

| 项 | 为什么现在不做 | 什么时候做 |
| --- | --- | --- |
| MSI / 系统级安装器 | 09 篇 §5 不做 MSI，也绝不写 `HKLM`／不请求 UAC | 有企业分发需求时再评估 |
| 代码签名 | 通道已接（`Sign-Artifacts.ps1` + `release.ps1` 签名步骤 + `manifest.signing`），只缺证书 | 拿到证书后传 `-Thumbprint`／`-PfxPath`，并一次性去掉文件名 `-unsigned`、置 `signed=true` |
| macOS / Linux 包 | M6 不含（PKG-04 才有）；Avalonia 交叉发布容易，但需要在目标系统冒烟 | PKG-04 |
| 自动更新 | 没有更新源，也没有签名，自动更新反而放大风险 | 签名 + 稳定分发渠道之后 |
| 单文件 / 裁剪 | 见 D4/D5 | 有完整冒烟用例集之后 |
| 内嵌 git sha 到 About 页链接 | 需要仓库 remote 信息，本机与 CI 写法不一致 | 可选改进 |

## 7. 需要别的模块配合的接线点

| 事项 | 落点 | 说明 |
| --- | --- | --- |
| 文档索引登记 | `docs/README.md` 的「索引 · 运维与设计」表 | 新增了 `ops/release.md` 与 `design/packaging.md`，`verify-layout` H-05 要求索引覆盖所有文档 |
| 主程序名 | `src/CkanUI.App/CkanUI.App.csproj` 的 `AssemblyName` | 已统一为 `CkanUI`，产物 `CkanUI.exe`（与 09 篇 §2.1 一致）；Avalonia 资源 URI 同步改成 `avares://CkanUI/...`，改名后冒烟通过 |
| 关于页许可证文案 | `src/CkanUI.ViewModels/Dialogs/AboutDialogViewModel.cs` 的 `LicenseText` | 现写 GPL-3.0，上游 CKAN 是 MIT；`AboutDialogTests` 有对应断言 |
| 项目许可证文件 | 仓库根 `LICENSE.md` | 已有（GPL-3.0 全文 + 上游 MIT 说明）；`release.ps1` 把它复制到包根，文件缺失时直接中止出包 |

## 修订记录

| 日期 | 变更 |
| --- | --- |
| 2026-10-29 | 首版：M6 / PKG-01..03 的决策表、版本词法、布局、确定性 zip、manifest schema |
| 2026-10-29 | v1.4 回填：AssemblyName 改 `CkanUI`（产物 `CkanUI.exe`）、包内新增 `LICENSE.md` 与 `install/`、签名通道接进 `release.ps1`、`-NoReadyToRun` 开关与体积实测 |
