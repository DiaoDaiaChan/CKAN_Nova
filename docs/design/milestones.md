# 里程碑状态表（M0–M14）

> 本文是 06 篇 WBS 的**执行实况**对照：每一行给出该里程碑的验收门禁、当前状态、证据所在文件。
> 规格侧口径以 `docs/plan/06-wbs-milestones.md` 与 `docs/plan/00-index.md` §3 为准；状态一旦变化必须同步 `docs/design/feature-matrix.md`。
> 生成方式：人工核对 + `dotnet build ckan-ui.sln`、`dotnet run --project tests/CkanUI.TestRunner`、`dotnet run --project tools/verify-layout -- --all` 三条命令的实测结果。

## 1. 总览

| 里程碑 | 主题 | 状态 | 门禁实测 |
| --- | --- | --- | --- |
| M0 | 骨架与契约冻结（ENV-01..05、ARC-01..05） | ✅ 完成 | 构建 0 警告 0 错误；架构测试 21 例全绿（`DependencyRulesTests` 7 + `NamespaceRulesTests` 3 + `MotionSkeletonTests` 6 + `WindowChromeTests` 2 + `PageTextLocalizationTests` 3，见 §6.2）；CI 三平台 matrix 已落 `.github/workflows/ci.yml` |
| M1 | 五个主页面 + 服务层打通（UI-01..UI-09、INT-04..08） | ✅ 完成 | 页面 VM 与视图全部可解析（`ViewLocatorTests` 钉死）；测试全绿 |
| M2 | 安装/卸载真实执行（INT-08 收口、E2） | 🚧 进行中 | `IInstallerService` + `CkanInstallerBackend` 可用；`IDownloadService`（临时文件 + 大小/SHA256 校验 + 原子 Move + 取消清理 + 失败分类）已落地；仍缺「真实联网装一个模组」的端到端冒烟 |
| M3 | 仓库、实例与设置周边补全（UI-06、UI-07、UI-28..UI-34） | ✅ 基本完成 | 仓库页（F-10）、设置页 8 组与密度、实例重命名（`RenameAsync`）、启动命令编辑（F-05/F-06）、启动后分层资源释放（18 篇 E6）均已落地 |
| M4 | 模组详情、关系与搜索打磨（UI-03、UI-10、UI-25..UI-27） | ✅ 完成 | 详情面板（四页签）与关系树已实现；未管理文件树与接管（F-42）已落地；组合搜索面板（`ModsSearchPanelViewModel` + `ModsSearchPanelView.axaml`，18 字段三态表单）已落地，F-20..F-23 全部「已实现」（见 feature-matrix F-20/F-21 与本文 §3 第 67 行） |
| M5 | 导入导出与工具族（F-39..F-48） | ✅ 完成 | F-39..F-48 十项全部「已实现」：`.ckan` 导入、modpack 导出与五种文本格式导出、实例比对、未管理文件树、ZIP 灌缓存、跨实例去重、安装历史（含删除/清空与 `RemoveExtras` 勾选）、游戏时长（含自动累计与重置）、modpack 编辑器（F-46）、下载统计环图 + 排序表格（F-47）；残留不改变状态，见 feature-matrix §3 |
| M6 | 插件系统与外观个性化（UI-33、THM-xx） | 🚧 基本完成 | 主题 token 与 Light/Dark/HighContrast 已生效、密度三档落盘；插件页（Active/Dormant 两栏 + 可回收 ALC）、插件声明页签与插件工具项均已落地；外观已交付 P-02/P-03/P-09/P-10 |
| M7 | 存档工作台（E1，SFS 格式） | ✅ 基本完成 | SFS 内核（解析/格式化/校验 + CLI）93 例全绿；存档工作台页面已落地（枚举/诊断/安全格式化）；V-09 `.loadmeta` 一致性校验未做 |
| M8–M14 | 日志与 AI 调试、Mod 配置适配、国际化、资源让路、打包发布、外观个性化 | ✅ 已交付（带残留） | 证据：`docs/design/logging-and-diagnostics.md`（M8）、`docs/design/mod-config-adapters.md` + `adapters/**`（M9）、`docs/design/i18n-runtime.md` 与 930 键双包（M10/M11）、`src/CkanUI.Services/Lifecycle/ResourceReleaseService.cs`（M12）、`docs/ops/release.md` + `docs/design/packaging.md` + `tools/release/release.ps1` + `.github/workflows/release.yml`（M13：自包含 277 文件/150 MB、框架依赖 90 文件/73 MB、`manifest.json` signed=false）、`src/CkanUI.App/Tray/TrayHost.cs` 与主题/密度/字号/动效（M14）。**真实剩余**：① 发布物仍未签名——但签名通道已就绪（`tools/release/Sign-Artifacts.ps1` 已接进 `release.ps1` 的签名步骤、`-SignDryRun` 可预演、`manifest.signing` 记 `{requested, applied, method, timestampUrl}`），缺的只是一张证书；② 安装器只有**随包 per-user 脚本**（`install/install.ps1` + `uninstall.ps1` 已由 `release.ps1` 打进包并列入必需条目，装/覆盖装/卸载实测通过）+ 未构建的 Inno 模板 `install/CkanUISetup.iss`，M13 验收口径的 `CkanUISetup.exe` 尚未产出；③ 体积：默认（R2R）自包含解压 152 MB、`-NoReadyToRun` 后 119 MB，下载包 63.7 / 49.7 MB 在 09 篇 §11 的 60–90 MB 预算内（解压地板与两条被挡死的瘦身路见 09 篇 §14）；④ 性能与端到端验收缺有网真机（§4 第 7 条） |

## 2. M0 已交付证据

| 条目 | 证据 |
| --- | --- |
| ENV-01 仓库骨架 | `src/` 11 个项目 + `tests/` 8 个项目 + `tools/verify-layout`，全部进 `ckan-ui.sln` |
| ENV-02 接入 Core | `src/CkanUI.Services/CkanUI.Services.csproj` 对 `external/CKAN/Core/CKAN-core.csproj` 的 ProjectReference（本地 junction，CI 走浅克隆） |
| ENV-03 日志落位 | `src/CkanUI.App/log4net.xml` + `src/CkanUI.Services/Logging/LoggingSetup.cs`（`__CKAN_LOG_DIR__` 令牌替换） |
| ENV-04 CI | `.github/workflows/ci.yml`（三平台 matrix + `TestCategory!=RequiresTxF` 分流 + Windows publish） |
| ENV-05 架构测试 | `tests/CkanUI.Architecture.Tests/DependencyRulesTests.cs`（7 条）+ `NamespaceRulesTests.cs`（3 条），加上 `MotionSkeletonTests`（6）、`WindowChromeTests`（2）、`PageTextLocalizationTests`（3），该程序集共 21 例（§6.2） |
| ARC-01 DI 组装根 | `src/CkanUI.App/ServiceRegistration.cs` |
| ARC-02 IDispatcher | `src/CkanUI.Platform/Threading/AvaloniaDispatcher.cs` |
| ARC-03 ViewLocator | `src/CkanUI.App/ViewLocator.cs`（`ResolveViewType` 可测） |
| ARC-04 异常兜底 | `Program.InstallCrashHandlers` + `IErrorReporter` + `CrashDialogViewModel` |
| ARC-05 单实例与 `ckan://` | `SingleInstanceGuard` + `App.HandleExternalArguments` |

## 3. M1–M5 已落地模块清单

| 模块 | 入口文件 | 说明 |
| --- | --- | --- |
| 外壳与导航 | `src/CkanUI.ViewModels/ShellViewModel.cs` | 十九项导航（`_baseNavItems`，`ShellViewModel.cs:85-107`）：模组 / 内容预览 / 变更集 / 仓库 / 导入导出 / modpack 编辑器 / 实例 / 游戏时长 / 存档 / 未管理文件 / 下载缓存 / 下载统计 / 跨实例去重 / 安装历史 / 插件 / 模组汉化 / 日志 / 设置 / Mod 配置；插件页签由 `PluginPageCatalog` 动态插在「插件」之后 |
| 模组管理 | `src/CkanUI.ViewModels/Pages/ModsPageViewModel.cs` | DataGrid 8 列、列宽持久化、80ms 防抖搜索、勾选→变更请求 |
| 变更集 | `src/CkanUI.ViewModels/Pages/ChangesPageViewModel.cs` | 预览/应用状态机；有 `IInstallerService` 时执行真实安装 |
| 仓库管理 | `src/CkanUI.ViewModels/Pages/ReposPageViewModel.cs` | 增删/上下移/单仓库刷新/恢复默认（F-10） |
| 游戏实例 | `src/CkanUI.ViewModels/Pages/InstancesPageViewModel.cs` | 列表/克隆/遗忘/重命名/打开目录/Steam 库导入/启动（多命令选择） |
| Steam 库定位 | `src/CkanUI.Services/Platform/SteamLibraryLocator.cs` | 根来源注册表 `HKCU\Software\Valve\Steam\SteamPath` 优先（`SteamRegistryProbe`，`OperatingSystem.IsWindows()` + `[SupportedOSPlatform]` 守 CA1416），其次两个默认安装路径；每根先读 `config/libraryfolders.vdf` 再读旧版 `steamapps` 版，都没读到时把根自己当库根。解析器 `src/CkanUI.Core/Steam/SteamLibraryParser.cs`（F-03 / UI-32） |
| 启动命令编辑 | `src/CkanUI.ViewModels/Pages/Instances/LaunchCommandsPanelViewModel.cs` | 增删改排序/重置默认/持久化到 `GUIConfig.json.CommandLines`（F-05/F-06） |
| 设置 | `src/CkanUI.ViewModels/Pages/SettingsPageViewModel.cs` | 八组（含兼容性与诊断）、改一项写一项、读回校验、密度三档 |
| 动效强度 | `src/CkanUI.App/Theming/MotionHost.cs` + `src/CkanUI.ViewModels/Pages/Settings/UiMotionScale.cs` | 跟随系统/完整/精简/关闭四档落到 `CkanMotionDurationFast/Base/Page`，视图过渡全部改用 token，页面切换按 token 装配（P-10） |
| 字号缩放 | `src/CkanUI.App/Theming/FontScaleHost.cs` + `src/CkanUI.ViewModels/Pages/Settings/UiFontScale.cs` | 90%/100%/110%/120%/130% 五档落到 `CkanFontSizeCaption/Body/Subtitle/Heading/Title`，落在半磅上；表格数据区跟随主题默认字号（P-09） |
| 强调色与对比度门禁 | `src/CkanUI.ViewModels/Pages/Settings/AccentContrast.cs` + `src/CkanUI.App/Theming/AccentHost.cs` | 12 预设 + 自定义 `#RRGGBB`；按当前生效变体底色判 WCAG AA（≥4.5:1），不合规拒绝并给建议色；宿主对配置文件里的手改值做同样兜底（P-03 / THM-02） |
| 高对比度强制 | `src/CkanUI.App/ThemeHost.cs`（探针 + `ForcedStateChanged`）+ 设置页置灰提示 | 系统开高对比度即压过用户主题与强调色（摘掉 4 个覆盖键），系统配色变化自动重算（P-02 / THM-10） |
| 背景与按实例外观 | `src/CkanUI.App/Theming/BackgroundHost.cs` + `BackgroundBrushFactory` / `BackgroundImageLoader` + `src/CkanUI.Services.Abstractions/BackgroundProfile.cs` | 纯色/渐变/本机图片三来源、五种填充、暗化/模糊/不透明度；洗白层压在背景上而不是改主题底色；1600 宽解码、单张缓存；高对比度整层不画；按实例覆盖存 `BackgroundByInstance`；强调色按实例存 `AccentByInstance`（`AccentHost.ResolveAccent` 单点解析，设置页有「强调色作用范围」行）（P-06 / P-07 / THM-02 / THM-05） |
| 操作浮层 | `src/CkanUI.ViewModels/Operations/OperationQueueViewModel.cs` | 100ms 节流、可取消 |
| 导入导出 | `src/CkanUI.ViewModels/Pages/ModPacksPageViewModel.cs` | `.ckan` 导入预览与执行、modpack 导出、实例差集比对（F-39/F-45） |
| 服务层 | `src/CkanUI.Services/**` | Instances / Registry / Repositories / Settings / ChangeSets / Installers / Downloads / ModPacks / Logging / Bootstrap |
| 下载续传与重试 | `src/CkanUI.Services/Downloads/DownloadService.cs` + `DownloadRetryPolicy.cs` + `NetAsyncDownloaderTransport.cs` | 网络类失败自动重试 3 次（400ms→1200ms 递增退避），重试复用同一临时文件、由 Core `Range` 从断点续传；校验类与取消不重试；进度文案标「第 n/3 次」；续传只在一次下载之内（临时文件带 GUID，跨进程续传未做）（F-38 / UI-35） |
| 存档工作台 | `src/CkanUI.ViewModels/Pages/SavesPageViewModel.cs` | 存档枚举 + 头部/loadmeta 元数据 + 诊断 + 安全格式化（`ISaveFileService`） |
| 未管理文件 | `src/CkanUI.ViewModels/Pages/UnmanagedPageViewModel.cs` | GameData 未管理文件树、预演与二次确认删除、资源管理器定位、接管为受管安装（F-42） |
| 接管写入路径 | `src/CkanUI.Services/Unmanaged/CkanManagedFileRegistrar.cs`、`UnmanagedAdopter.cs`、`ManagedDllMerge.cs` | 写前合并 `installed_dlls`（上游 `SetDlls` 是整体替换）+ `registry.json.bak-<时间戳>` 快照 + 失败还原；判定纯函数（只接管 .dll，冲突显式拒绝）（F-42 / INT-09，见 `docs/design/service-contracts.md` §4.5） |
| 下载缓存 | `src/CkanUI.ViewModels/Pages/CachePageViewModel.cs` | 缓存占用/条目表、按上限清理、全部清理、从 ZIP 灌缓存（F-50/F-40） |
| 跨实例去重 | `src/CkanUI.ViewModels/Pages/DedupPageViewModel.cs` | 大小分组 + SHA256 扫描、硬链接共享、组内回滚、默认预演（F-41） |
| 安装历史 | `src/CkanUI.ViewModels/Pages/InstallHistoryPageViewModel.cs` | JSONL 历史、已装/未装分组、按快照重装（F-44） |
| 插件 | `src/CkanUI.ViewModels/Pages/PluginsPageViewModel.cs` | Active/Dormant 两栏、显式激活、可回收 ALC 加载（F-17） |
| 插件页签 | `src/CkanUI.ViewModels/Navigation/PluginPageCatalog.cs` + `src/CkanUI.App/Plugins/PluginPageFactory.cs` | 插件声明的页签进导航栏（插在「插件」之后）、激活/卸载即时增删、按 key 复用 NavItem、类型反射 + DI 构造、视图按同程序集约定解析（F-17） |
| 插件工具项 | `src/CkanUI.ViewModels/Navigation/PluginToolCatalog.cs` + `src/CkanUI.App/Plugins/PluginToolItemHost.cs` | 插件往内置页工具条插按钮：只收 Active、按「页面 + 键」去重、`IPluginToolHost` 分发、回调经 `InvokeToolItemAsync` 走锁外执行并把异常转成可重试失败（F-17） |
| 搜索语法解析 | `src/CkanUI.Services/Search/ModSearchParser.cs` | 页面不再有前缀白名单：18 个前缀 + 自由词 + 取反全部走模型层（F-20..F-23） |
| 组合搜索面板 | `src/CkanUI.ViewModels/Pages/Mods/ModsSearchPanelViewModel.cs` + `src/CkanUI.App/Views/Mods/ModsSearchPanelView.axaml` | 18 字段逐项编辑（12 文本 + 6 三态「不限/是/否」）、与搜索框双向同步、实时预览生成的语法文本、面板自身输入不被规范化回填改写（F-20/F-21） |
| 存档内核 | `src/CkanUI.Sfs/**` + `src/CkanUI.Sfs.Cli/**` | SFS 解析 / 格式化 / 校验（含 `format` / `validate` 子命令） |
| 导航历史 | `src/CkanUI.ViewModels/Navigation/NavigationService.cs` | 页面级前进/后退（双栈、深度 64）+ 模组选中历史（深度 32），外壳与详情面板各两个按钮（F-25） |
| 关于框 | `src/CkanUI.ViewModels/Dialogs/AboutDialogViewModel.cs` | 版本矩阵四行 + 三条官方链接 + 双许可声明（界面 GPL-3.0、内置 CKAN 核心库沿用上游 MIT、第三方清单见 `THIRD-PARTY-NOTICES.txt`）；外链走 Avalonia `Launcher`，失败只在框内提示（F-55） |
| 内容预览 | `src/CkanUI.ViewModels/Pages/ContentPageViewModel.cs` | ZIP 中央目录建树、未缓存时下载入库、路径复制与目录定位（F-29） |
| modpack 编辑器 | `src/CkanUI.ViewModels/Pages/ModPackEditorPageViewModel.cs` | 元数据/作者/许可/关系行编辑、版本折叠、互斥校验（F-46） |
| 下载统计 | `src/CkanUI.ViewModels/Pages/DownloadStatsPageViewModel.cs` | 自绘环图 + 排序表格，三档维度、超 12 段合并「其他」（F-47） |
| 系统托盘 | `src/CkanUI.App/Tray/TrayHost.cs` | 七项原生菜单、图标现画、最小化/关闭到托盘两个开关（F-51） |
| 资源释放 | `src/CkanUI.Services/Lifecycle/ResourceReleaseService.cs` | 五层（视图/缓存/注册表锁+L2/GC/工作集）四档（关/保守/均衡/激进），可逆（`RestoreAsync`），退出即还原（18 篇 E6）；L0 由 `src/CkanUI.App/Lifecycle/UiViewResourceParticipant.cs` 上报（清导航字形缓存 + 释放背景位图），L1 由 `src/CkanUI.App/Lifecycle/PageProjectionCacheParticipant.cs`（三个页面的行投影，走 `IPageProjectionTrim`）与 `src/CkanUI.Services/Lifecycle/LocalizerCacheParticipant.cs`（缺译诊断表）上报 |

## 4. 已知残留与横切债（按影响排序）

> 本节是**已知残留与横切债清单，不是未开工清单**：55 项特性的状态以 [feature-matrix.md](feature-matrix.md) 为准（当前 55 / 0 / 0 全部「已实现」，「已实现」= 基础逻辑 + 界面落点双条件）。本节列的是「已实现但仍有残留」与跨特性债务，两边口径互链，不要当成矛盾。

1. **真实下载未端到端验证**：`CkanInstallerBackend` 的联网路径只有离线假后端用例；沙箱内无法访问 CDN，需要在有网络的机器上跑一次「装一个真实模组」的冒烟。
2. **（已定取舍）实例重命名只改配置不搬目录**：`IInstanceService.RenameAsync` 已接 `GameInstanceManager.RenameInstance`（重名可重试拒绝、自启动名同步跟随），但不移动磁盘目录——与上游语义一致，界面已注明。
3. **L4 工作集归还已接入，缺真机量化**：`src/CkanUI.Services/Lifecycle/PlatformMemory.cs` 用 `[LibraryImport("psapi.dll")] EmptyWorkingSet` 真回收工作集，以回收前后 `Process.WorkingSet64` 差值报数；非 Windows 与 Win32 失败仍安全降级（0 项 + Warn，不抛）。缺的是**真机量化**：沙箱内派生的 GUI 进程结论不可信（见第 9 条），需要在普通桌面跑一次 `Aggressive` 档对照工作集曲线。
4. **托盘只到编译与 VM 单测**：`TrayHost` 的真实图标、菜单点击与两个开关联动仍需人工目视一次——本轮已确认真实窗口能起来、也能用 UI Automation 驱动（见 §6.3），但托盘区图标没有自动化通道；图标固定 32×32 不随 DPI 重绘，菜单文案是 VM 内中文常量。
5. **（设计约束，非缺陷）插件页签的视图必须自带**：视图符合同程序集命名约定才能被解析，否则回退占位视图；工具项目前只有模组页一个落点（`PluginToolCatalog.ModsPageKey`），新页面要接需实现 `IPluginToolHost` 并在 DI 注册别名（见 `docs/design/plugins.md` §6.1 / §6.2）。
6. **（已补齐，自动轮换留 v2）背景的「游戏截图」来源**：第四轮接上 20 篇 §9.1——设置页「背景来源」新增「游戏截图（最新一张）」，
  由 `src/CkanUI.Services/Settings/BackgroundImageLocator.cs` 读 `<GameDir>/Screenshots` 里最新一张，路径计入背景缓存签名；
  仍未做的只有 20 篇 §4 Q2 的「每次启动自动换一张」（v2）。
7. **发布链路已交付，签名与安装器只剩最后一口气**：M8–M14 的打包发布已交付——`tools/release/release.ps1` 一条命令出双发布包（默认 R2R：自包含 152 MB、框架依赖 75.5 MB；`-NoReadyToRun` 时 119 MB / 42 MB）与 `SHA256SUMS.txt`/`manifest.json`，`.github/workflows/release.yml` 按 tag 触发并带冒烟，文档见 `docs/ops/release.md`、`docs/design/packaging.md`。本轮补上两件：① **代码签名通道**——`tools/release/Sign-Artifacts.ps1`（`-Thumbprint`/`-PfxPath`/`-DryRun`、找 signtool、带时间戳）已由 `release.ps1` 在 stage 组装后、打 zip 前调用，失败中止出包，`manifest.signing` 记录状态，`New-DevCertificate.ps1` 可造自签测试证书；② **安装器**——`tools/release/install/{install.ps1,uninstall.ps1}` 走每用户安装（只写 `HKCU` 卸载项 + `App Paths`、按 `<FileVersion>` 侧装、`current\version.txt` 指针、误删守卫），随包发在 `install/` 并被列为 zip 必需条目，装/覆盖装/卸载均实测；`CkanUISetup.iss` 是 Inno 6 模板（本机无 Inno，未跑 ISCC）。真实剩余：① **发布物仍未签名**（无证书，文件名照旧带 `-unsigned`、`manifest.signed=false`）；② M13 验收口径的 `CkanUISetup.exe` 未产出；③ **有网真机的性能与端到端**：真实模组下载安装、F12 帧率/可打断性、900–1600 双宽度视觉复核、macOS/Linux 冒烟。体积口径见 `docs/ops/release.md` §3.5 与 09 篇 §14（下载包在预算内，解压有 ~119 MB 地板）。
8. **页面 VM 运行期文案的本地化（第四轮已收口主体）**：机制见 `src/CkanUI.ViewModels/PageViewModel.cs` 的 `TextResolver`/`TextFormatter` 与 `Text(中文, 键)`，导航时由 `NavigationService` 用 `??=` 注入，没译文就回落中文原文；17 个页面 ViewModel 的标题与正文共 852 处已迁移（含 `ModConfigPageViewModel` 旧式 `T()` 的统一），文案键 76 → 930，中英两套包严格同步，代码 fallback 与 zh-CN 译文逐字一致由 `tests/CkanUI.Architecture.Tests/PageTextLocalizationTests.cs` 守护。**仍剩子 VM 与静态格式化器**（`ChangesFormatter` 39、`ChangesLabels` 19、`ModPackEditorText` 39、`ModsSearchPanelViewModel` 30、`LogGlyphs` 24、`SettingsDiagnosticsViewModel` 14、`StabilityToleranceFile` 14、`InstallHistoryText` 17 等），它们不是 `PageViewModel`，需先加 `Func<string,string,…>` 适配器；清单与理由见 `docs/design/i18n-runtime.md` 的「尚未覆盖」与 `docs/design/feature-matrix-verification.md` §11。
   仍剩：只有**子 VM 与静态格式化器**（`ChangesFormatter` 39、`ChangesLabels` 19、`ModPackEditorText` 39、`ModsSearchPanelViewModel` 30、`LogGlyphs` 24 等，清单见 `docs/design/feature-matrix-verification.md` §11.5）。原写的「其余页面正文未迁移」已随第四轮收口失效——17 个页面 ViewModel 的标题与正文共 852 处已全部迁完（取证 §11.1），`ModConfigPageViewModel` 的 `T()` 也已统一，错误栏兜底句同样入包。
9. **沙箱里派生的 GUI 进程写不进 `%LocalAppData%\CKAN`**：本轮冒烟见状态栏「启动未完成：实例操作失败。」与模组汉化页 `Access to the path 'C:\Users\43701\AppData\Local\CKAN\ckan-ui\18n\packs' is denied.`；同一路径在 bash 里可写、GUI 进程不可写，判为沙箱派生进程的权限副作用而非产品缺陷。需在普通桌面（双击 exe）启动下复核：能写则撤销本条，仍报错按真缺陷修。

## 5. 如何复核本表

```bash
cd ckan-ui
dotnet build ckan-ui.sln -v q --nologo          # 期望 0 警告 0 错误
dotnet run --project tests/CkanUI.TestRunner    # 期望 0 失败
dotnet run --project tools/verify-layout -- --all   # 期望「结论: 绿」
```

> 注：本沙箱内 `dotnet test` 不可用（Testhost 被拒），故本地一律用 `tests/CkanUI.TestRunner`（NUnitLite 进程内跑）；CI 上仍用 `dotnet test`。

## 6. 结档记录（2026-10-28）

### 6.1 本轮补齐的缺口

| 缺口 | 落点 | 覆盖 |
| --- | --- | --- |
| P-02 强制高对比度 | `src/CkanUI.App/ThemeHost.cs`（`IsHighContrastForced` / `ForcedStateChanged` / `Refresh`）、`src/CkanUI.App/Theming/AccentHost.cs`、设置页 `AppearanceKeys` 整组禁用 | ThemeHostTests / SettingsPageViewModelTests |
| P-03 强调色对比度门禁 | `src/CkanUI.ViewModels/Pages/Settings/AccentContrast.cs`（4.5:1，按当前底判定）、设置页「自定义强调色」 | AccentContrastTests |
| P-06 背景与壁纸 | `src/CkanUI.Services.Abstractions/BackgroundProfile.cs`、`src/CkanUI.App/Theming/BackgroundBrushFactory.cs` / `BackgroundImageLoader.cs` / `BackgroundHost.cs`、`MainWindow.axaml` 背景两层 | BackgroundProfileTests / BackgroundBrushFactoryTests / BackgroundHostTests |
| P-07 按实例强调色 | `UiSettings.AccentByInstance`、`SettingsKeyNames.AccentScope`、`AccentHost.ResolveAccent` | AccentHostTests / SettingsPageViewModelTests |
| P-09 字号缩放 / P-10 动效强度 | `UiFontScale.cs` + `FontScaleHost.cs`；`UiMotionScale.cs` + `SystemMotionPreference.cs` + `MotionHost.cs`，视图过渡时长改成 `{DynamicResource}` 令牌 | UiFontScaleTests / UiMotionScaleTests / MotionHostTests |
| F-20/F-21 组合搜索面板 | `IModSearchFormatter` + `ModSearchFormatter` + `ModsSearchPanelViewModel`（18 个字段）+ `ModsSearchPanelView.axaml` | ModsSearchPanelViewModelTests / ModSearchFormatterTests |
| F-17 插件页签 | `IPluginPage`、`PluginPageCatalog`、`PluginPageFactory`、`ViewLocator.ResolvePluginViewType`、`ShellViewModel.NavItems` 差量更新 | PluginPageCatalogTests / PluginPageFactoryTests / ShellViewModelTests |
| F-17 插件工具项 | `PluginToolItemDescriptor` + `ICKANUIPlugin.ToolItems`/`ExecuteToolItemAsync` + `PluginService.InvokeToolItemAsync`；`PluginToolCatalog`、`PluginToolButtonViewModel`、`IPluginToolHost`、`src/CkanUI.App/Plugins/PluginToolItemHost.cs`；模组页工具条 11 列 | PluginToolItemTests / PluginToolCatalogTests / PluginToolItemHostTests |
| 下载断点续传与重试 | `src/CkanUI.Services/Downloads/DownloadRetryPolicy.cs`、`DownloadService` 重试循环、`NetAsyncDownloaderTransport` 用 `FileLengthOrZero` 走 Range 续传 | DownloadServiceTests |
| 资源释放 L0/L1 | `src/CkanUI.ViewModels/Pages/IPageProjectionTrim.cs`、`src/CkanUI.App/Lifecycle/UiViewResourceParticipant.cs` / `PageProjectionCacheParticipant.cs`、`src/CkanUI.Services/Lifecycle/LocalizerCacheParticipant.cs` | ResourceReleaseServiceTests / LifecycleParticipantTests |
| Steam 库定位 | `src/CkanUI.Core/Steam/SteamLibraryParser.cs`、`src/CkanUI.Services/Platform/SteamLibraryLocator.cs` / `SteamRegistryProbe.cs`（config 版 vdf 优先、注册表根优先） | SteamLibraryParserTests / SteamLibraryLocatorTests |
| 未管理文件接管（INT-09） | `src/CkanUI.Services/Unmanaged/UnmanagedAdopter.cs` / `ManagedDllMerge.cs` / `CkanManagedFileRegistrar.cs`、页面「接管为受管」 | UnmanagedAdopterTests / ManagedDllMergeTests / UnmanagedServiceTests |

### 6.2 门禁结果（本次结档复跑）

| 门禁 | 结果 |
| --- | --- |
| `dotnet build ckan-ui.sln --nologo -v q` | 0 警告 0 错误（9.24 s，`artifacts/final-tests-6.log`） |
| `dotnet run --project tests/CkanUI.TestRunner --no-build` | **3110 个用例 0 失败 0 skip**：App 163 / Architecture 21 / Core 3 / Localization 76 / ModText 19 / Services 1205 / Sfs 93 / ViewModels 1530（`artifacts/final-tests-6.log` 与 2026-10-29 实测一致） |
| `dotnet run --project tools/verify-layout --no-build -- --all` | 阻断违规 0 · 警告 0 · 结论 绿（H-11 仅 `--prune` 执行） |

### 6.3 运行冒烟（真实窗口 + UI Automation）

跑法：先 `taskkill /F /IM CkanUI.exe`，再起 `src/CkanUI.App/bin/Debug/net10.0/CkanUI.exe`；窗口用 `SetWindowPos` 钉到 (80,60) 1400×900，捕捉用 `PrintWindow(hWnd, hdc, PW_RENDERFULLCONTENT=2)`（DWM 合成窗口用 `CopyFromScreen` 会被遮挡影响）。驱动用 `System.Windows.Automation`：Avalonia 的 ListItem / Button 都暴露 `SelectionItemPattern` 与 `InvokePattern`，但**导航列表的 Select() 生效、插件行必须真鼠标点**（`SetCursorPos` + `mouse_event` 到行中心，且等页面渲染完再取 `BoundingRectangle`，否则坐标是上一帧的）。

走通的一段：插件页「未激活」栏出现演示插件 → 选中行（「激活」变可用）→ 激活（页面提示「已激活插件「工具条演示插件 - 1.0.0」。」）→ 回模组页，工具条右侧多出插件声明的「打个招呼」「点我计数」两个按钮。

没确认的一段：点这两个按钮后**执行回执读不到**——插件自己往 `%TEMP%\ckan-demo\demo-plugin.log` 写记录，沙箱派生的 GUI 进程写不进去（同 §4 第 8 条），按钮的 `ToolTipText` 也没在自动化树里刷新可见。故执行路径仍以 `PluginToolItemTests`（8）/ `PluginToolItemHostTests`（7）为准，真机需要手点一次确认提示文案会变。

临时产物（不入门禁，%TEMP% 可能被清理）：`C:\Users\43701\AppData\Local\Temp\ckan-demo\shot-print.png`（模组页带插件按钮的窗口截图）、`shot.ps1`（窗口捕捉）、`verify.ps1`（整段 UIA 驱动）；演示插件 dll 在 `%LocalAppData%\CKAN\plugins\DemoPlugin.dll`，源码在 `%LocalAppData%\Temp\ckan-demo\DemoPlugin`，按 `docs/design/plugins.md` §10 可重建。

### 6.4 结档时仍然打开的项

§4 现有 9 条：第四轮关掉了 2 条的后半段（第 3 条 L4 工作集归还已接 psapi，只剩真机量化；第 6 条背景「游戏截图」来源已接，只剩 v2 自动轮换），其余仍打开——真实下载端到端（第 1 条）、实例重命名不搬目录「已定取舍」（第 2 条）、托盘手测（第 4 条）、插件页签视图自带 + 工具项落点单一「设计约束」（第 5 条）、发布链路的真机性能/端到端与安装器签名体积（第 7 条）、i18n 子 VM（第 8 条）、沙箱写权限复核（第 9 条）；另有插件文档 §9 那批「需要 Lead 落地」条目其实已经落地，本轮只标注不清删。

