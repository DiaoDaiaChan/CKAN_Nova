# 55 项特性验收取证（feature-matrix 证据附件）

> 母表：[特性对齐状态矩阵](feature-matrix.md) §1。任务来源：[06 WBS 与里程碑](../plan/06-wbs-milestones.md)、[12 全特性对齐清单](../plan/12-feature-parity.md) §5。
> 本文**不改母表结论**，只回答一个问题：那 55 行「已实现」是否都有可指认的代码行与测试。
> 取证方式：在 `src/`、`tests/` 上做 `grep` / `read` 实检索，只记录复核时真实存在的符号行；每条的「强度」是它与母表结论的吻合度，不是代码质量评分。

---

## 0. 取证口径

### 0.1 实现证据只认 file:line

「目录里有这个文件」不算证据。每行至少给出 1 个真实存在的 `class` / 方法 / 属性 / 命令定义行；只写文件名而不给行号的条目按未取证处理。母表里形如 `Xxx.cs:1` 的行号是文件头占位，等于没给证据（见 §2.7）。

### 0.2 测试证据是静态属性行数，不是运行用例数

表中 `Fixture:N` 的 N = 该测试文件里 `[Test]` 行数 + `[TestCase(...)]` 行数（不含 `[TestFixture]`，也不含注释里的示例）。这与母表口径一致：母表 `ContentServiceTests`(11)、`ModPackEditorStoreTests`(20)、`TrayMenuTests`(17)、`SettingsTrayTests`(8)、`AboutDialogTests`(10) 与本轮静态统计逐一对齐。
一个 `[TestCase(...)]` 在 NUnit 里展开成一条用例、占一行属性，两者数值相同；本轮引用的 fixture 均未使用 `[Values]`/`[Range]` 组合，因此静态行数 ≈ 运行用例数。runner 展开后的总计数见 §5。

### 0.3 强度分级

| 强度 | 判定规则 |
|---|---|
| 强 | 服务/VM 层有非桩实现，且有直接断言其行为的用例 |
| 中 | 实现存在，但用例是间接覆盖（混在大 fixture 里）、或关键路径是真实 I/O / 桌面通道，自动化只能验替身 |
| 部分或缺口 | 母表写「已实现」，实测只实现了其中一部分，或根本找不到实现（§2 逐条展开） |

---

## 1. 逐条取证（F-01..F-55）

| F-ID | 特性 | 实现证据（file:line） | 覆盖测试（类:属性数） | 强度 |
|---|---|---|---|---|
| F-01 | 多实例列表（名称/游戏/版本/时长/路径，选中、设为默认、遗忘、重命名、新建） | `src/CkanUI.Services/Instances/InstanceService.cs:9`（class）、`:28`（订阅 InstanceChanged 并投影）、`src/CkanUI.ViewModels/Pages/InstancesPageViewModel.cs:22`（class） | InstancesPageViewModelTests:28、InstancesProjectionTests:9、InstanceServiceTests:1 | 强 |
| F-02 | 克隆实例（源/目标路径、新名、设为默认、切换） | `src/CkanUI.Services/Instances/InstanceService.cs:71`（`CloneAsync`）；VM 侧命令入口在 `InstancesPageViewModel`（命令层，未逐行取证） | InstancesLaunchCommandTests:44（含克隆入口）、InstanceServiceTests:1 | 中：服务层 CloneAsync 无专门断言用例，主要靠 VM 入口用例 |
| F-03 | Steam 库导入 | `src/CkanUI.Services/Platform/SteamLibraryLocator.cs:22`、`src/CkanUI.Core/Steam/SteamLibraryParser.cs:12` | SteamLibraryLocatorTests:10、SteamLibraryParserTests:2 | 强 |
| F-04 | 实例右键：打开目录 / 加入 CKAN / 克隆 | `InstancesPageViewModel.cs:747`（OpenFolder）、`:785`（Launch）；右键菜单本体在 `src/CkanUI.App/Views/InstancesPageView.axaml`（XAML，未逐项取证） | InstancesLaunchCommandTests:44、InstancesPageViewModelTests:28 | 中：菜单项靠 XAML，UI 层无自动化 |
| F-05 | 启动游戏（多命令选择） | `src/CkanUI.Services.Abstractions/IInstanceShellLauncher.cs:24`（`LaunchGameAsync`）、`src/CkanUI.App/Views/InstancesPageView.axaml.cs:208`（真实进程启动） | InstancesLaunchCommandTests:44、InstancesLaunchReleaseTests:8 | 中：真实进程外启动需手测，测试用假 launcher |
| F-06 | 游戏命令行编辑（列表、重置、增、接受、取消） | `src/CkanUI.ViewModels/Pages/Instances/LaunchCommandsPanelViewModel.cs:17` | InstancesLaunchCommandTests:44（部分） | 中 |
| F-07 | 打开游戏目录（菜单 + 托盘） | `InstancesPageViewModel.cs:747`（`OpenFolderAsync`）+ `IInstanceShellLauncher` 打开目录成员 | InstancesPageViewModelTests:28、InstancesLaunchCommandTests:44 | 中 |
| F-08 | 实例锁定/注册表损坏处置（删锁、回滚、提示升级 CKAN） | `src/CkanUI.Services.Abstractions/Instances/IInstanceRepairService.cs:12`（Diagnose/ForceUnlock/Quarantine/CheckCompatibility）、`src/CkanUI.Services/Instances/InstanceRepairService.cs:24`、`src/CkanUI.Services.Abstractions/Instances/InstanceRepairModels.cs:6`、`src/CkanUI.ViewModels/Pages/InstancesPageViewModel.cs:1195`（`Repair` 注入）/`:1243`/`:1278`/`:1352`（三个命令）、`src/CkanUI.App/Views/InstancesPageView.axaml`（「实例自检」区）；原锁定拦截仍在 `src/CkanUI.Services/Instances/InstanceService.cs:48-49`、`src/CkanUI.Services/Registry/RegistryService.cs:38` | InstanceRepairServiceTests:24、InstancesRepairTests:22、RegistryServiceTests:1、InstanceServiceTests:1 | 已实现：删锁（游戏运行中拒绝并可重试、非空锁目录不递归、动作写 `%LocalAppData%/CKAN/instance-repair-journal.jsonl`）、坏注册表隔离为 `registry.json.corrupt-yyyyMMdd-HHmmss`（字节保留）、CKAN 过旧与游戏版本过低给升级建议；§2.6 的原反证留档 |
| F-09 | 切换实例后按需刷新 | `InstanceService.cs:37`（RefreshAsync）、`:40`（SwitchAsync）、`:28`（InstanceChanged 事件） | InstancesPageViewModelTests:28、InstancesProjectionTests:9、InstanceServiceTests:1 | 强 |
| F-10 | 元数据仓库管理 | `src/CkanUI.Services/Repositories/RepositoryService.cs:140`（LoadRepositoriesAsync）、`:149`（AddRepositoryAsync）、`SettingsPageViewModel.cs:20`+`:540`（仓库分组） | RepositoryCatalogTests:26、RepositoryServiceListTests:12、ReposPageViewModelTests:29、SettingsPageViewModelTests:51 | 强 |
| F-11 | 认证令牌（host + token，脱敏显示） | `src/CkanUI.Services/Settings/SettingsService.cs:59`/`:61`（主机枚举与掩码）、`src/CkanUI.ViewModels/Pages/Settings/AuthTokenEditorViewModel.cs:13`、`SettingsPageViewModel.cs:94`（`TokenEditor`） | SettingsPageViewModelTests:51、SettingsServiceTests:2 | 强 |
| F-12 | 仓库刷新（手动/启动时/定时/暂停/Ctrl 强制重下） | `RepositoryService.cs:84`（`RefreshAsync(ct)`）、`:87`（`RefreshAsync(bool skipETags, ct)`）、`:101`（强制重下进度文案） | RepositorySkipETagsTests:2、RepositoryServiceListTests:12 | 强（主表实现位置列为空，本表补齐） |
| F-13 | 稳定性容忍度（全局 + 单 mod 覆盖） | `src/CkanUI.ViewModels/Pages/Settings/StabilityToleranceFile.cs:18`、`src/CkanUI.ViewModels/Dialogs/ModDetailsViewModel.cs:35`（单 mod 覆盖） | SettingsStabilityToleranceTests:17 | 中：设置页覆盖面广，文件级读写无独立 fixture |
| F-14 | 兼容游戏版本管理 | `src/CkanUI.Services/Compatibility/CkanCompatibleVersionsService.cs:16`、`CompatibleVersionsDialogViewModel.cs:18` | CompatibleVersionsServiceTests:14、CompatibleVersionsDialogViewModelTests:13 | 强 |
| F-15 | 首选下载主机设置 | `src/CkanUI.Services/Settings/PreferredHostsService.cs:13`、`PreferredHostsDialogViewModel.cs:13` | PreferredHostsServiceTests:9、PreferredHostsDialogViewModelTests:14 | 强 |
| F-16 | 安装过滤器（全局 + 实例级） | `src/CkanUI.Services/Settings/InstallFiltersService.cs:18`、`InstallFiltersDialogViewModel.cs:14` | InstallFiltersServiceTests:5、InstallFiltersDialogViewModelTests:12 | 强 |
| F-17 | 插件系统（激活/停用/重载/卸载/新增） | `src/CkanUI.Services.Abstractions/IPluginService.cs:16`、`src/CkanUI.Services/Plugins/PluginService.cs:24`、`PluginsPageViewModel.cs:20`、`PluginToolCatalog.cs:31`、`PluginToolItemHost.cs:19` | PluginServiceTests:23、PluginsPageViewModelTests:15、PluginToolCatalogTests:12、PluginToolItemHostTests:7、PluginToolItemTests:8、PluginPageCatalogTests:11、PluginPageFactoryTests:13 | 强（缺口照主表：工具项只有模组页落点、ALC 卸载尽力而为、不校验签名） |
| F-18 | Mod 列表 15 列 | `src/CkanUI.ViewModels/Pages/Mods/ModsRowProjection.cs:16`、`ModsRowView.cs:14`（主表写 :13，实测 record 在 :14）、`src/CkanUI.App/Views/ModsPageView.axaml:160` | ModsColumnWidthTests:14、ModsProjectionTests:4、ModListProjectionTests:4 | 强（行号漂移已校正） |
| F-19 | 11 种状态过滤器 + 排序 | `src/CkanUI.Services/Models/GUIModFilter.cs:3`、`src/CkanUI.ViewModels/Pages/Mods/ModsRowFilter.cs:6`、`ModsRowSorter.cs:6` | ModListFilterTests:5、ModsQueryTests:24、ModsListPipelineTests:8 | 强 |
| F-20 | 组合搜索面板（Search / Or 两组） | `src/CkanUI.ViewModels/Pages/Mods/ModsSearchPanelViewModel.cs:21`（class）、`:103`（`BuildSpec`）；主表写的 `ModSearchDrawerViewModel.cs` **不存在** | ModsSearchPanelViewModelTests:12、ModSearchTests:20、ModSearchFormatterTests:14 | 强（主表路径过期，见 §2.3） |
| F-21 | 搜索字段 18 项 | `ModsSearchPanelViewModel.cs:103`（字段到 spec 的映射）、`ModsSearchFieldViewModel.cs:22`；解析器在 `src/CkanUI.Services/Search/ModSearchParser.cs` | ModSearchTests:20、ModSearchFormatterTests:14 | 强 |
| F-22 | 标签（Tags）与标签徽章、点击显隐 | `ModsSearchPanelViewModel.cs:148`（`"tag" => spec with { Tags = terms }`）、`:231`（回填语法文本） | ModsSearchPanelViewModelTests:12、ModSearchTests:20 | 强 |
| F-23 | Label 过滤与编辑入口 | `src/CkanUI.ViewModels/Dialogs/LabelEditDialogViewModel.cs:110`、`src/CkanUI.App/Views/Dialogs/LabelEditDialogView.axaml:1` | ModsLabelTests:14、LabelEditDialogViewModelTests:12 | 强 |
| F-24 | Label 属性（10 项） | `src/CkanUI.Services/Labels/LabelService.cs:22` | LabelServiceTests:14、LabelEditDialogViewModelTests:12 | 强 |
| F-25 | 前进/后退导航（选中历史） | `src/CkanUI.ViewModels/Navigation/NavigationService.cs:6`、`ModsPageViewModel.cs:66`（`_selectionBack`）；主表写的 `NavigationService.cs:31`/`ModsPageViewModel.cs:365` 未复现 | NavigationServiceTests:4、NavigationHistoryTests:10、ModsSelectionHistoryTests:8 | 强（行号漂移） |
| F-26 | 模组详情多页签架构（元数据/关系/版本/本地文件） | `src/CkanUI.ViewModels/Dialogs/ModDetailsViewModel.cs:288`（`TabCount = 4`）/`:299`（`SelectedTabIndex`，带越界钳制）/`:323`（版本页标题带计数）/`:328`（文件页标题带计数）、`src/CkanUI.App/Views/Dialogs/ModDetailsView.axaml:61`（`<TabControl>`）与 `:67`/`:135`/`:204`/`:282`（元数据 / 关系 / 全部版本 / 文件与内容四个 `TabItem`）、宿于 `src/CkanUI.App/Views/ModsPageView.axaml:314`（`<dialogs:ModDetailsView DataContext="{Binding Details}" />`），快照映射 `src/CkanUI.ViewModels/Pages/Mods/ModDetailsSnapshotBuilder.cs` | DialogsModDetailsTabsTests:27、DialogsModDetailsTests:15、ModDialogsTests:25 | 已实现：四页签容器 + 标题计数角标 + 切换模组时复位到元数据页，两处空态文案；§2.1 的原反证留档 |
| F-27 | 元数据页（Identifier、Author、Downloads 等） | `ModDetailsViewModel.cs:172`（`MetadataItems`）、`ModDetailsSnapshot.cs:12`、`ModDetailsFormatter.cs:6`（主表写 :10，实测 class 在 :6） | DialogsModDetailsTests:15 | 强（行号漂移已校正） |
| F-28 | 关系树（九色图例、反向关系、双击跳转） | `src/CkanUI.ViewModels/Relations/RelationTreeViewModel.cs:11`、`RelationLegendItem.cs:6`、`src/CkanUI.App/Views/.../RelationTreeView.axaml` | RelationTreeViewModelTests:13、RelationsTreeTests:12 | 强 |
| F-29 | 内容页（未缓存提示、下载、ZIP 树预览） | `src/CkanUI.Services/Content/ContentService.cs:8`、`NetModuleCacheZipStore.cs:8`、`src/CkanUI.ViewModels/Pages/ContentPageViewModel.cs:10`、`ContentPageView.axaml:55` | ContentServiceTests:11、ContentPageViewModelTests:14 | 强（主表 @BT@ 占位符已修正，见 §2.4） |
| F-30 | 版本列表与指定版本安装 | 数据契约 `src/CkanUI.Services.Abstractions/Mods/IModVersionCatalog.cs:11`、`ModVersionOption.cs:18`；实现 `src/CkanUI.Services/Mods/CkanModVersionCatalog.cs:17`/`:32`（`GetVersionsAsync`）/`:83`（`Project`）→ `IRegistryQuerier.AvailableByIdentifier` + `GameInstance.VersionCriteria()`；DI `src/CkanUI.Services/ServiceCollectionExtensions.cs:54`、`src/CkanUI.App/ServiceRegistration.cs:145`；界面与动作 `src/CkanUI.ViewModels/Dialogs/ModDetailsViewModel.cs:340`（`Versions`）/`:375`（`HasVersions`）/`:378`（计数文案）/`:577`（`ApplyVersions`）/`:636`（`[RelayCommand(CanExecute = nameof(CanInstallVersion))]`）/`:637`（`InstallVersionAsync`）、投影 `src/CkanUI.ViewModels/Pages/Mods/ModDetailsSnapshotBuilder.cs:56`（`BuildVersions`）、页面查询 `src/CkanUI.ViewModels/Pages/ModsPageViewModel.cs:1246`（`VersionCatalog`）/`:1271`（`RefreshVersionsAsync`）、页签 `src/CkanUI.App/Views/Dialogs/ModDetailsView.axaml:204` | `CkanModVersionCatalogTests`:13、`ModsVersionCatalogTests`:12、`DialogsModDetailsTabsTests`:27 | 强：版本号降序 + KSP 兼容文案 + 发布时间/体积/下载链接 + 已装/不稳定标记全链贯通；选中行连续变化取消上一次查询（后发先至不串台）；「安装该版本」仍走 `new ChangeRequest(identifier, ChangeAction.Install, version.Version)` → `IChangeSetService.BuildAsync`（见 §7 与 §8） |
| F-31 | 右键动作（重装、复制标识符、复制下载地址、打开主页） | `src/CkanUI.ViewModels/Pages/ModsPageViewModel.cs:1782`（`CanReinstallSelected`）、`:1785`（`CanOpenHomepage`）、`:1789`（`ReinstallSelected`）；主表 :1630 未复现 | ModsContextMenuTests:10、ModsPageViewModelTests:22 | 强（行号漂移） |
| F-32 | 全量更新（Update all）、应用（Apply） | `ModsPageViewModel.cs:1183`（`UpdateAllCommand.NotifyCanExecuteChanged()`）、`src/CkanUI.App/Views/ModsPageView.axaml:146`（绑定 UpdateAllCommand）；主表 :1587 未复现 | ModsUpdateAllTests:5、ModsChangePreviewTests:6、ModsChangesHandoffTests:7 | 强（行号漂移） |
| F-33 | 变更集页（四列、清空、应用、游戏运行警告） | `src/CkanUI.ViewModels/Pages/ChangesPageViewModel.cs:21`、`src/CkanUI.Core/ChangeSets/CoreChangeSetPlanner.cs:21`、`InstanceService.cs:122`（`IsGameRunning`） | ChangesPageViewModelTests:29、ChangesFormatterTests:9、ChangeSetServiceTests:17、ChangesGameRunningTests:8、GameProcessProbeTests:12 | 强 |
| F-34 | 提供者选择（虚拟 mod 多候选单选） | `src/CkanUI.ViewModels/Dialogs/VirtualModuleDialogViewModel.cs:13`、`src/CkanUI.ViewModels/Dialogs/IModDialogService.cs:10` | ModDialogsTests:25、DialogsConfirmAndSelectTests:9 | 强 |
| F-35 | 推荐/建议/被支持选择 | `RecommendationDialogViewModel.cs:14` | ModDialogsTests:25（部分） | 中：无独立 fixture 名，混在 ModDialogsTests 里 |
| F-36 | 推荐审计 | `AuditRecommendationsDialogViewModel.cs:18` | AuditRecommendationsDialogTests:10 | 强 |
| F-37 | 安装/卸载/升级执行（进度、取消、重试、日志） | `src/CkanUI.Services/Installers/InstallerService.cs:21`、`src/CkanUI.Services/Operations/OperationQueue.cs:21`、`OperationQueueViewModel.cs:22` | InstallerServiceTests:14、OperationsQueueViewModelTests:20、DownloadServiceTests:18 | 强 |
| F-38 | 下载失败处置（逐项 Retry/Skip、复制诊断） | `src/CkanUI.ViewModels/Dialogs/DownloadErrorsDialogViewModel.cs:15`、`src/CkanUI.Services/Downloads/DownloadFailureClassifier.cs:34` | DownloadErrorsDialogViewModelTests:11、DownloadFailureClassifierTests:13、DownloadRetryPolicyTests:6 | 强（主表实现位置列为空，本表补齐） |
| F-39 | 从 .ckan 安装（多选文件） | `src/CkanUI.Services/ModPacks/ModPackCatalog.cs:22`、`ModPacksPageViewModel.cs:18` | ModPackCatalogTests:43、ModPacksPageViewModelTests:43 | 强 |
| F-40 | 导入已下载 mods（ZIP → 缓存） | `src/CkanUI.Services/Cache/CkanCacheStore.cs:28`、`CachePageViewModel.cs:239`（`ImportZipAsync`） | CacheServiceTests:14、CachePageViewModelTests:22 | 强 |
| F-41 | 去重（跨实例扫描并共享） | `src/CkanUI.Services/Dedup/DedupScanner.cs:71`、`DedupExecutor.cs:15`、`DedupPageViewModel.cs:20` | DedupServiceTests:13、DedupPageViewModelTests:12 | 强（缺口照主表：非 Windows 无 inode、无共享存储目录模式） |
| F-42 | 未管理文件（树、删除、接管为受管） | `src/CkanUI.Services/Unmanaged/UnmanagedScanner.cs:15`+`:62`（改走 `GameDataWalker`）、`UnmanagedAdopter.cs:22`、`CkanManagedFileRegistrar.cs:19`、`UnmanagedPageViewModel.cs:15`；新增 `GameDataWalker.cs:15`/`:59` | UnmanagedServiceTests:20、UnmanagedAdopterTests:10、UnmanagedAdoptTests:7、UnmanagedPageViewModelTests:18、**UnmanagedTraversalBoundaryTests:8（本任务新增）** | 强（本轮补齐联接遍历/接管/拒删边界，见 §3.1） |
| F-43 | 残留目录清理 | `src/CkanUI.Services/Cleanup/ResidualCleanupService.cs:22`、`ResidualCleanupDialogViewModel.cs:22`、`CkanInstallerBackend.cs:180` | ResidualCleanupServiceTests:15、ResidualCleanupDialogTests:12 | 强 |
| F-44 | 安装历史（时间戳列表、已装/未装分组、从历史重装） | `src/CkanUI.Services/History/InstallHistoryService.cs:21`、`InstallHistoryStore.cs:56`/`:69`/`:104`（`DeleteAt`）/`:140`（`Clear`）、`InstallHistoryRecorder.cs:84`/`:106`、`InstallHistoryPageViewModel.cs:18`/`:230`（`DeleteSelectedAsync`）/`:268`（`ClearHistoryAsync`）、`HistoryRecordingInstaller.cs:22` | InstallHistoryServiceTests:14、InstallHistoryPageViewModelTests:15、**InstallHistoryMaintenanceTests:12（新增）**、**InstallHistoryMaintenancePageViewModelTests:12（新增）** | 强（删除/清空缺口已补齐，见 §3.2；仍缺 RemoveExtras 界面开关） |
| F-45 | 导出：已装列表 / modpack | `src/CkanUI.Services/ModPacks/InstalledListExporter.cs:15`（主表写在 ViewModels 下，实际在 Services）、`CkanModPackStore.cs:27` | InstalledListExporterTests:10、ModPacksPageViewModelTests:43 | 强（路径漂移已校正） |
| F-46 | modpack 编辑器 | `src/CkanUI.Services/ModPacks/ModPackEditorStore.cs:22`、`ModPackEditorValidator.cs:25`、`ModPackEditorPageViewModel.cs:25` | ModPackEditorStoreTests:20、ModPackEditorValidatorTests:26、ModPackEditorPageViewModelTests:24 | 强（主表 @BT@ 占位符已修正） |
| F-47 | 下载统计（饼图 + 表格） | `src/CkanUI.Services/Stats/DownloadStatsAggregator.cs:8`、`DownloadStatsService.cs:10`、`DownloadStatsPageViewModel.cs:14`、`src/CkanUI.App/Controls/DonutChart.cs:13` | DownloadStatsServiceTests:19、DownloadStatsPageViewModelTests:16 | 强（主表 @BT@ 占位符已修正；按仓库维度未做） |
| F-48 | 游戏时长（实例名 / 小时数，F2 就地编辑） | `src/CkanUI.Services/PlayTime/PlayTimeService.cs:22`/`:46`（`DefaultPath`）、`PlayTimeSessionLedger.cs:28`、`src/CkanUI.Services.Abstractions/PlayTime/IPlayTimeTracker.cs`、`PlayTimePageViewModel.cs:12`、`InstancesPageViewModel.cs:95`（`PlayTime` 属性）/`:859`（BeginSession）/`:937`（EndSession） | PlayTimeServiceTests:3、**PlayTimeTrackerTests:14（新增）**、PlayTimePageViewModelTests:1、**InstancesPlayTimeTrackingTests:9（新增）** | 强（自动累计缺口已补齐，见 §3.3；仍缺真实实例集成与清空入口） |
| F-49 | 设置总表 | `SettingsPageViewModel.cs:20`、`:540`（`SettingGroupKey.Repository` 分组）；主表 :264 未复现 | SettingsPageViewModelTests:51、SettingsAccentTests:25、SettingsMotionScaleTests:24、SettingsFontScaleTests:17、SettingsStabilityToleranceTests:17、SettingsBackgroundTests:12、SettingsKeyMirrorTests:11、SettingsAccentInstanceTests:8、SettingsTrayTests:8、SettingsDensityTests:7、SettingsHighContrastTests:4 | 强 |
| F-50 | 下载缓存管理 | `src/CkanUI.Services/Cache/CacheService.cs:17`、`CachePageViewModel.cs:11`/`:193`（Refresh）/`:213`（PurgeAll）/`:325`（OpenFolder）/`:359`（ResetLimit） | CacheServiceTests:14、CachePageViewModelTests:22 | 强 |
| F-51 | 托盘图标（启用/最小化到托盘；7 项右键菜单） | `src/CkanUI.ViewModels/Shell/TrayMenuViewModel.cs:23`、`TrayMenuItemViewModel.cs:39`、`src/CkanUI.App/Tray/TrayHost.cs:29`、`TrayHostActions.cs:27`、`ITrayHostActions.cs:16`、`UiSettings.MinimizeToTray`/`CloseToTray` | TrayMenuTests:17、SettingsTrayTests:8 | 中：宿主层（真实图标/菜单点击）无自动化通道，需桌面手测；主表 @BT@ 占位符与 — 已修正 |
| F-52 | 自动更新（启动时检查、dev builds、检查/安装、新版本对话框） | 检查侧 `src/CkanUI.Services.Abstractions/IUpdateCheckService.cs:10`、`src/CkanUI.Services/Update/UpdateCheckService.cs:17`（`PickUpdateAsset` 解析 `assets[].browser_download_url` 的 Windows zip 与 `digest` 归一成 SHA256）、`src/CkanUI.Services.Abstractions/Dtos/UpdateCheckResult.cs`（`AssetUrl`/`AssetSha256`/`AssetSize`）；安装侧 `src/CkanUI.Services.Abstractions/Update/IUpdateInstaller.cs:14`、`src/CkanUI.Services/Update/UpdateInstaller.cs:30`、`src/CkanUI.ViewModels/Dialogs/UpdateAvailableDialogViewModel.cs:33`（`InstallCommand`）、`src/CkanUI.App/Views/Dialogs/UpdateAvailableDialogView.axaml`（安装区） | UpdateInstallerTests:17、UpdateInstallDialogTests:15、UpdateCheckServiceTests:11、UpdateDialogViewModelTests:9 | 已实现：下载走既有 `IDownloadService`、SHA256/大小校验、zip-slip 整包拒绝、剥壳最多 4 层、覆盖前 `backup-<时间戳>` 备份、失败或取消自动回滚、成功置 `RestartRequired`；残留见 `docs/design/app-update.md` §10（不校验签名、原地升级需独立 updater）；§2.5 的原反证留档 |
| F-53 | 语言与本地化（选择需重启；多语 resx/json） | `src/CkanUI.Localization/Localizer.cs:16`、`LanguagePackLoader.cs:12`、`SettingsPageViewModel.cs:54`（`LocaleChoicesProvider`）/`:96`（`RefreshLocaleChoices`）/`:572`（语言项） | LocalizerTests:10、LanguageSwitchTests:8、PseudoLocalizationTests:10、ShippedPacksTests:7、FallbackTests:6、FormatTests:6、LocaleInfoTests:3、LocalizationKeyDriftTests:4、LocalizerCacheParticipantTests:3、**SettingsLocaleChoicesTests:4（新增）** | 强（候选与已发包语种对齐，见 §3.4；VM 侧中文硬编码属横切债，记入缺口） |
| F-54 | 显示格式（隐藏 epoch、隐藏 v、自动排序） | `src/CkanUI.ViewModels/Pages/Settings/VersionDisplayFormat.cs:6`、`ModsRowView.cs:134` | VersionDisplayFormatTests:7、ModsVersionDisplayTests:8 | 强 |
| F-55 | 帮助与关于（链接、关于框、单实例互斥、ckan://、错误/崩溃对话框） | `src/CkanUI.ViewModels/Dialogs/AboutDialogViewModel.cs:24`、`src/CkanUI.App/AboutInfoFactory.cs:9`、`src/CkanUI.Platform/SingleInstance/SingleInstanceGuard.cs:20`、`CkanUri.cs:16`、`ErrorDialogViewModel.cs:13` | AboutDialogTests:10、CkanUriTests:11、SingleInstanceGuardTests:4、DialogsErrorAndDiagnosticTests:10 | 强（许可证文案已改为双声明口径） |

---

## 2. 与母表不一致的地方

> 同步状态：本节的反证已于首轮回写母表 —— F-08 / F-26 / F-30 / F-52 的状态列改为「部分实现」，母表 §2.1 汇总更新为「已实现 51 / 部分实现 4 / 未开始 0」，§1 的实现位置空缺与行号漂移也按 §2.4/§2.7 修正。下面各节保留改判前的原始表述，作为判定依据留档。
>
> **第二轮补录**：F-08（实例自检/删锁/注册表隔离）、F-52（下载—校验—替换—回滚）、F-26（四页签容器与宿主）已补齐，§1 对应行改判「已实现」；F-30 仍为「部分实现」（界面与动作已通，缺版本集合来源）。母表 §2.1 汇总现为「已实现 54 / 部分实现 1 / 未开始 0」。逐条见 **§7**。

### 2.1 F-26「多页签架构」实际是分区单页

母表描述为「模组详情多页签架构（元数据/关系/版本/本地文件）」，实现位置给 `src/CkanUI.ViewModels/Dialogs/ModDetailsViewModel.cs:15`。实测：

- `src/CkanUI.App/Views/Dialogs/ModDetailsView.axaml:15` 起是**一个** `ScrollViewer`，里面顺序堆 4 组分区（元数据 / 依赖 / 推荐 / 冲突）；
- 在该文件内 grep `TabControl|TabItem` **0 命中**（Avalonia 的 `TabControl` 在别的视图里有使用，详情视图没有）；
- VM 侧对应的是 `MetadataItems`（`src/CkanUI.ViewModels/Dialogs/ModDetailsViewModel.cs:172`）这类分组集合，没有「当前页签」状态；
- 描述里的「版本」「本地文件」两组也没有对应实现：详情对话框内既无版本列表（见 §2.2），也无本地文件树。

影响：信息没缺，缺的是「页签」这个交互形态与两组列出的分区。若验收句按字面要求页签，F-26 应为「部分」。

建议：母表把描述改成「分区单页」；若要页签，需要新增 `SelectedSection` 状态并把 4 组分区改造成 `TabItem`。

### 2.2 F-30「版本列表与指定版本安装」没有界面实现

母表标「已实现」，但实现位置列为空。实测：

- `src/CkanUI.ViewModels/Dialogs/ModDetailsViewModel.cs:146` / `:150` 只有 `InstalledVersionText` / `LatestVersionText` 两个**文本**属性，没有版本集合、没有选中版本；
- 在 `src/` grep `AvailableVersion|VersionRow|VersionList|指定版本|历史版本` 仅 1 处命中，是注释：`src/CkanUI.Services.Abstractions/IChangeSetService.cs:5`「用户的一次选择请求（勾选/取消勾选/指定版本）」。该契约确实带版本字段——`src/CkanUI.Services.Abstractions/IChangeSetService.cs:6` 的 `ChangeRequest(string Identifier, ChangeAction Action, string? TargetVersion = null)`，`src/CkanUI.Services/ChangeSets/CoreChangeSetPlanner.cs:203` / `:208` 也认这个字段；
- 但**没有任何界面能把 `TargetVersion` 填成用户挑的旧版本**：详情对话框无版本列表，变更集页的勾选也只产出「装/不装」。

结论：F-30 应记为「部分」——底层契约与规划器支持按指定版本进变更集，版本枚举与选择界面缺失。

建议：母表改「部分」并补实现位置 `IChangeSetService.cs:6` / `CoreChangeSetPlanner.cs:203`；要补齐则：详情对话框加版本分区 → 从仓库快照枚举可用版本 → 构造 `ChangeRequest(identifier, action, targetVersion)`。

### 2.3 F-20 / F-21 / F-22 的实现位置指向不存在的文件

母表三行都写 `src/CkanUI.ViewModels/Pages/Mods/ModSearchDrawerViewModel.cs`；`glob **/ModSearchDrawerViewModel.cs` 在当前树 **0 命中**。实际实现是：

- `src/CkanUI.ViewModels/Pages/Mods/ModsSearchPanelViewModel.cs:21`（class）
- `:103`（字段 → 搜索 spec 的映射 `BuildSpec`）
- `:148`（`"tag" => spec with { Tags = terms }`）
- `:231`（把 spec 回填成搜索语法文本）

测试同样按新名字命名：`ModsSearchPanelViewModelTests:13`。建议三行路径统一改指 `ModsSearchPanelViewModel.cs`，否则按母表去查会扑空。

### 2.4 空实现列与模板占位符

- 实现位置列写 `—` 且验收列残留 `@BT@` 模板占位符的 4 行：F-29、F-46、F-47、F-51。本轮已把 `feature-matrix.md` 里 `@BT@` 全量替换成反引号（现全文 0 命中）；
- 实现位置列**仍**为 `—` 的：F-29、F-46、F-47、F-51；实现位置列**为空**的：F-12、F-38。本文 §1 已给出这 6 行应填的符号行（含本轮新增文件），母表合并时请同步；
- 未改母表结论与其余列，避免与 Lead 的表格改动互相覆盖。

### 2.5 F-52「自动更新」只有检查，没有下载/安装

母表描述含「检查/安装」与「新版本对话框」。实测：

- `src/CkanUI.Services.Abstractions/IUpdateCheckService.cs:10` 定义的接口**只有一个成员**，声明在 `:18` `CheckForUpdatesAsync`；
- 实现 `src/CkanUI.Services/Update/UpdateCheckService.cs:17` 对 GitHub Releases 与 S3 dev build feed 做只读比对（Semantic / System 版本号比较）；
- 对话框 `src/CkanUI.ViewModels/Dialogs/UpdateAvailableDialogViewModel.cs:14` 只做版本卡片展示 + 外部浏览器跳转；
- 在 `src/` grep `DownloadUpdate|ApplyUpdate|InstallUpdate|ReplaceExecutable` **0 命中**。

结论：「自动更新」实际是「检查更新 + 引导用户自己去下载」。建议母表拆成「检查（已实现）」与「下载并替换二进制（未开始）」两行，或把描述里的「安装」删掉。

### 2.6 F-08「实例锁定/注册表损坏处置」只有前半句

母表描述为「实例锁定/注册表损坏处置（删锁、回滚、提示升级 CKAN）」。实测只有锁定**检测与拒绝**：

- `src/CkanUI.Services/Instances/InstanceService.cs:48-49`：`if (target.IsMaybeLocked) return OperationResult<InstanceInfo>.Fail($"实例“{target.Name}”已被其他 CKAN 进程锁定，无法切换。");`
- `src/CkanUI.Services/Instances/InstanceService.cs:165`：实例投影带上 `gameInstance.IsMaybeLocked`
- `src/CkanUI.Services/Registry/RegistryService.cs:38`：`if (gameInstance.IsMaybeLocked) return ...Fail($"实例“{instance.Name}”已被锁定，无法打开注册表。")`
- `src/CkanUI.ViewModels/Pages/InstancesPageViewModel.cs:386` / `:805`：实例行的锁定态

「删锁」（删除 CKAN 锁文件让用户强制继续）、「注册表损坏的修复/回滚」、「提示升级 CKAN」三类动作在 `src/` **无实现**：grep `删锁|UpgradeCkan|RemoveLock|DeleteLockFile` 无相关命中；grep 到的 `Rollback` 全部是安装事务回滚（`src/CkanUI.Services/Installers/InstallerService.cs:256`、`src/CkanUI.Services/Installers/IInstallerBackend.cs:43`），与实例锁定无关；注册表侧 grep `损坏|Corrupt` 的命中都在 `src/CkanUI.Sfs` 与本地化降级逻辑里。

建议：母表 F-08 记「部分（只有锁定检测与提示）」，删锁 / 注册表修复 / 升级提示拆成后续任务。

### 2.7 行号与路径漂移

| 母表原写 | 实测位置 | 说明 |
|---|---|---|
| F-18 `ModsRowView.cs:13` | `src/CkanUI.ViewModels/Pages/Mods/ModsRowView.cs:14` | record 首行在 :14 |
| F-27 `ModDetailsFormatter.cs:10` | `src/CkanUI.ViewModels/Dialogs/ModDetailsFormatter.cs:6` | 静态类声明下移 |
| F-25 `NavigationService.cs:31`、`ModsPageViewModel.cs:365` | `src/CkanUI.ViewModels/Navigation/NavigationService.cs:6`（class；`:31` 无对应符号）、`src/CkanUI.ViewModels/Pages/ModsPageViewModel.cs:66`（`_selectionBack`） | 两处行号未复现 |
| F-31 `ModsPageViewModel.cs:1630` | 同文件 `:1782`、`:1785`、`:1789`（`CanReinstallSelected` / `CanOpenHomepage` / `ReinstallSelected`） | 行号随改动下移 |
| F-32 `ModsPageViewModel.cs:1587` | 同文件 `:1183`（`UpdateAllCommand.NotifyCanExecuteChanged()`） | 同上 |
| F-45 `src/CkanUI.ViewModels/...`（路径层错） | `src/CkanUI.Services/ModPacks/InstalledListExporter.cs:15` | 导出实现在 Services 层 |
| F-49 `SettingsPageViewModel.cs:264` | 同文件 `:540`（`SettingGroupKey.Repository` 分组） | `:264` 无对应符号 |
| F-52 `IUpdateCheckService.cs:1`、`UpdateCheckService.cs:1`、`UpdateAvailableDialogViewModel.cs:1` | 接口 `:10`、成员 `:18`；实现 `:17`；对话框 `:14` | 母表用「:1」文件头占位，等于没给证据 |

---

## 3. 本轮补齐的四项缺口

### 3.1 F-42 未管理区（GameData）遍历与删除的联接边界

**缺口**：`UnmanagedScanner` / `UnmanagedAdopter` 之前直接 `Directory.EnumerateFileSystemEntries` 递归，遇到目录联接（junction）会跟进去——同一份 GameData 内容可能被两个实例各接管一份，删除时也可能顺着联接到实例目录之外。

**改动**：

- 新增 `src/CkanUI.Services/Unmanaged/GameDataWalker.cs:15`（深度优先遍历、跳过重解析点；取不到属性时宁可不扫也不猜）、`:59` `public static bool IsLink(string path)`；
- 调用点：`src/CkanUI.Services/Unmanaged/UnmanagedScanner.cs:62`、`src/CkanUI.Services/Unmanaged/UnmanagedAdopter.cs:158`、`src/CkanUI.Services/Unmanaged/CkanManagedPathProvider.cs:56`；
- `UnmanagedAdopter.Plan` 对联接目录返回 `Refuse`（文案：「这是目录联接或符号链接，扫描不会跟进它，也不能接管其中的文件。」）；
- `UnmanagedScanner.Delete` 在 `dryRun` 之前统一拒删联接（「选中项包含目录联接或符号链接，已拒绝删除：…」，`retryable: false`）——删除是先写盘再说的动作，必须在 dryRun 阶段就拒绝，而不是删一半再报错。

**覆盖**：`tests/CkanUI.Services.Tests/UnmanagedTraversalBoundaryTests.cs`（8 例；需要造联接但权限不足时 `Assert.Ignore`，不伪装成通过）。

**残留**：只跳过「重解析点」，不检测挂载点 / 网络驱动器；联接判定依赖 `FileAttributes.ReparsePoint`，在其他平台的语义不同。

### 3.2 F-44 安装历史的删除与清空

**缺口**：历史页只能看不能改，用户要删记录只能手动找文件（原 `docs/design/install-history.md` §4 第 2 条）。

**改动**：

- 契约 `src/CkanUI.Services.Abstractions/History/IInstallHistoryMaintenance.cs`：`DeleteAsync(DateTimeOffset, ct)` / `ClearAsync(ct)` → `OperationResult<int>`（返回删除的行数）；
- `src/CkanUI.Services/History/InstallHistoryStore.cs:104` `public int DeleteAt(DateTimeOffset timestamp)`：按 `At` 同时删 entry 与它对应的 extras 快照两行，保留不可解析的行（不因为一行坏数据让整页删不动），`removed == 0` 时不写盘；`:140` `public int Clear()`：写空文件而不是删文件（保留文件与权限，读侧仍是空表）；
- `src/CkanUI.Services/History/InstallHistoryRecorder.cs:84` / `:106`：无实例或没有写权限时返回**可重试**的中文失败；
- `src/CkanUI.Services/History/InstallHistoryService.cs:21` 改为 `: IInstallHistoryService, IInstallHistoryMaintenance`，`ct` 触发时返回「操作已取消。」；
- `src/CkanUI.ViewModels/Pages/InstallHistoryPageViewModel.cs:18`（class）、`:230` `DeleteSelectedAsync`、`:268` `ClearHistoryAsync`，新增 `CanEditHistory` / `CanDeleteSelected` / `CanClearHistory` 与 `ConfirmAsync` 二次确认：**没有 `IDialogService` 时命令直接不可用**——宁可不能删，也不做无确认的静默删数据；删完重读并重算 `CountText`；
- 视图按钮「删除选中」「清空历史」加在 `src/CkanUI.App/Views/InstallHistoryPageView.axaml`。

**覆盖**：`tests/CkanUI.Services.Tests/InstallHistoryMaintenanceTests.cs`（12 例）、`tests/CkanUI.ViewModels.Tests/InstallHistoryMaintenancePageViewModelTests.cs`（12 例）。

**残留**：`RemoveExtras`（「要不要卸载快照之外的模组」）仍无界面开关，默认只补装不卸载。

### 3.3 F-48 游戏时长的自动累计

**缺口**：`PlayTimeService.Save` 只由页面手工编辑调用，实例启动/退出没有记账方（原 `docs/design/play-time-and-version-display.md` §5 第 1 条）。手动编辑与自动计时混在同一个数值上会互相覆盖，所以不能简单在启动时累加。

**改动**：

- 新契约 `src/CkanUI.Services.Abstractions/PlayTime/IPlayTimeTracker.cs`：`ActiveInstanceName` / `BeginSession(string instanceName)` / `EndSession()` / `DiscardOpenSession()`（带默认实现的接口成员，其他实现不用全量补）；
- 新增 `src/CkanUI.Services/PlayTime/PlayTimeSessionLedger.cs:28`：`%LocalAppData%/CKAN/play-time.sessions` 存**会话区间**（实例名 + 开始时间 + 结束时间，未结束的会话没有结束时间），按会话**开始时刻所在的自然日**切段求和（跨零点不会被算两天）；上次进程崩在游戏里留下的未闭合会话，下次 `Begin` 时直接丢弃（不做「按开机时长补算」的猜测，宁可少记不凭空多记）；写盘用 `Utf8JsonWriter` + `File.Move(overwrite: true)` 原子替换；
- `src/CkanUI.Services/PlayTime/PlayTimeService.cs:22` 改为 `: IPlayTimeService, IPlayTimeTracker`：`Load` 返回「手动基线 + 自动累计」，`Save(name, hours)` 把入参当**总量**、写盘前换算基线 `max(0, 总量 − 自动累计)`，反复保存不会把自动时长叠加两次；`DefaultPath` 在 `:46`；
- `src/CkanUI.ViewModels/Pages/InstancesPageViewModel.cs:95` `public IPlayTimeTracker? PlayTime { get; set; }`、`:859` `PlayTime?.BeginSession(row.Name);`、`:937` `PlayTime?.EndSession();`；`StartReleaseSchedule` 改成「有 `PlayTime` 或 `ResourceRelease` 就 `AttachExitSignal`」，`OnDeactivated` 只在没有未闭合会话时退订，`_releaseSettled` 幂等守卫保证资源释放只做一次。

**覆盖**：`tests/CkanUI.Services.Tests/PlayTimeTrackerTests.cs`（14 例：跨零点切日、崩溃遗留会话被覆盖、时钟回拨记 0、手动存 5h 不叠加自动 2h、存 1h 但自动已 2h 仍显示 2h、双实例并行、路径命名）、`tests/CkanUI.ViewModels.Tests/InstancesPlayTimeTrackingTests.cs`（9 例：启动成功记、启动失败不记、退出结算、无释放服务仍订阅、资源策略 Off 仍结算、未注入依赖时行为不变、离开页面后退出仍结算、只恢复一次、无会话时离开页面照旧退订）。

**残留**：只在 `InstancesPageViewModel` 这条启动路径上记账，其他入口（`ckan://` 唤起、托盘启动）绕开该页面则不计时；也没有「先启动游戏再打开界面」的补记；没有「清空单个实例时长」的界面入口。

### 3.4 F-53 界面语言候选与已发包语种对齐

**缺口**：「界面语言」硬编码 6 项（跟随系统 / zh-CN / zh-TW / en-US / ja-JP / de-DE），但 `i18n/packs` 实际只发了 `en-US.json` 与 `zh-CN.json`。选 `日本語` / `Deutsch` / `繁體中文` 会把没包的语言写进 `IUiSettingsStore.Locale`，重启后 `src/CkanUI.Localization/Localizer.cs:322` `ResolveInitialLocale` 找不到语言包，静默退回系统语言——**设置了却没效果，且没有任何提示**（假 UI）。

**改动**：

- `src/CkanUI.ViewModels/Pages/SettingsPageViewModel.cs:54` `public Func<IReadOnlyList<(string Code, string Display)>>? LocaleChoicesProvider { get; set; }`（形状与 `SettingsDiagnosticsViewModel.CoreVersionProvider` 一致：VM 不引用本地化层，用什么语种由组装根决定）；
- `:96` `public void RefreshLocaleChoices()`：重建「界面语言」这一项的候选；构成规则——第 0 项固定空 code「跟随系统」，其余按提供器顺序、`OrdinalIgnoreCase` 去重、跳过空白 code、显示名空白时退回 code；
- 构造期提供器还没注入，`ctor`（`:85`）先调一次兜底；`:104` 的 `ShippedLocales` 只有 `zh-CN` / `en-US`，**不再列出没有语言包的语种**；语言项本体在 `:572`，`requiresRestart: true` 与描述「语言切换需要重启界面后生效（F-53）」保持原样（规格：`docs/plan/12-feature-parity.md:98`、`docs/ui/03-ui-spec.md:321` 就是「需重启」）。

**覆盖**：`tests/CkanUI.ViewModels.Tests/SettingsLocaleChoicesTests.cs`（4 例：无提供器时恰为跟随系统/zh-CN/en-US 且不含 ja-JP/zh-TW/de-DE；提供器注入后按顺序重建且选中值仍正确写入 `UiSettings.Locale`；去重与空 code 跳过；提供器返回空集合时退回随包语种）。

**残留**：`i18n/packs` 只有两套包，候选列表因此只有两项——这是数据问题不是代码问题；VM 侧运行期文案仍是中文硬编码（见 §4 与 `milestones.md` §4 第 8 条）。

---

## 4. 证据不足清单与补救建议

| F-ID | 不足点 | 补救建议 |
|---|---|---|
| F-02 | 服务层 `InstanceService.CloneAsync`（`:71`）有实现，但没有克隆专属 fixture，入口混在 `InstancesLaunchCommandTests:45` 里 | 补 `CloneInstanceTests`，断言同级路径推导、新名冲突、设为默认 |
| F-04 | 右键菜单项写在 `src/CkanUI.App/Views/InstancesPageView.axaml`，UI 层无自动化通道 | 列桌面手测项（打开目录 / 加入 CKAN / 克隆三项目测） |
| F-05 | 真实进程启动在 `src/CkanUI.App/Views/InstancesPageView.axaml.cs:208`，测试用假 `IInstanceShellLauncher` | 桌面手测：启动真实游戏、命令优先级、退订 |
| F-06 | 命令行编辑只在 VM 层（`LaunchCommandsPanelViewModel.cs:17`）有实现证据 | 手测重置/增/接受/取消四条路径 |
| F-07 | 与 F-05 同源：打开目录靠平台 launcher，无自动化 | 并入 F-05 手测项 |
| F-08 | 见 §2.6：只有锁定检测与提示 | 母表改「部分」，删锁/注册表修复/升级提示单列任务 |
| F-13 | 稳定性容忍度的文件级读写（`StabilityToleranceFile.cs:18`）无独立 fixture，靠设置页用例间接覆盖 | 补 `StabilityToleranceFileTests`（损坏文件、并发写、覆盖层级） |
| F-17 | 插件卸载是 ALC 尽力而为，且不校验签名；用例覆盖激活/停用/重载，未覆盖真实 DLL 卸载 | 文档明确「卸载尽力而为」；需要强隔离时再评估新进程宿主 |
| F-26 | 见 §2.1：实现形态与描述不符 | 母表改描述，或补页签容器 |
| F-30 | 见 §2.2：无版本列表与选择界面 | 母表改「部分」并补实现位置；UI 补齐见 §2.2 建议 |
| F-35 | 推荐/建议/被支持三种对话框混在 `ModDialogsTests` 里，无独立 fixture | 拆 `RecommendationDialogViewModelTests` |
| F-44 | 残留：`RemoveExtras` 无界面开关 | 历史页重装选项加一个「卸载快照之外的模组」勾选 |
| F-48 | 残留：只在实例页启动路径记账；无清空单实例时长入口 | 把 `IPlayTimeTracker` 下沉到启动协调器；时长页加「重置」按钮 |
| F-51 | 托盘真实图标与菜单点击无自动化通道（宿主层 `TrayHost.cs:29` 需真实桌面） | 桌面手测 7 项菜单 + 最小化/关闭到托盘 |
| F-52 | 见 §2.5：只有检查，没有下载/安装 | 母表拆行；实现下载与替换时需先定签名与回滚策略 |
| F-53 | 残留：VM 侧运行期文案与导航标题中文硬编码 130+ 处（`src/CkanUI.ViewModels/Pages/ModTextPageViewModel.cs:208`、`src/CkanUI.ViewModels/Pages/ModsPageViewModel.cs:128`） | 按 16 篇 §5 键规范迁移；`milestones.md` §4 第 8 条已登记为横切债 |

---

## 5. 复核命令与本轮真实计数

```bash
# 全解决方案构建（干净基线）
dotnet build ckan-ui.sln --nologo -v q /nodeReuse:false
# 单测（禁 dotnet test：沙箱拒绝 Testhost）
dotnet build tests/CkanUI.Services.Tests/CkanUI.Services.Tests.csproj --nologo -v q /nodeReuse:false
dotnet build tests/CkanUI.ViewModels.Tests/CkanUI.ViewModels.Tests.csproj --nologo -v q /nodeReuse:false
dotnet build tests/CkanUI.App.Tests/CkanUI.App.Tests.csproj --nologo -v q /nodeReuse:false
dotnet run --project tests/CkanUI.TestRunner --no-build -- CkanUI.Services.Tests
dotnet run --project tests/CkanUI.TestRunner --no-build -- CkanUI.ViewModels.Tests
dotnet run --project tests/CkanUI.TestRunner --no-build -- CkanUI.App.Tests
```

| 程序集 | Test Count | Passed | Failed | 备注 |
|---|---|---|---|---|
| `CkanUI.Services.Tests` | 1091 | 1067 | 24 | 失败**全部**在 `CkanUI.Services.Tests.ModConfig.*`（`AdapterWriterTests`、`AdapterJsonReaderTests`、`ModConfigVersionRangeTests`），属并行改动中的 ModConfig 适配器，不在本任务范围；本任务触及的 `PlayTime*`、`InstallHistory*`、`Unmanaged*` fixture 0 失败 |
| `CkanUI.ViewModels.Tests` | 1015 | 1015 | 0 | 含本轮新增 `InstancesPlayTimeTrackingTests`(9)、`InstallHistoryMaintenancePageViewModelTests`(12)、`SettingsLocaleChoicesTests`(4) |
| `CkanUI.App.Tests` | 156 | 156 | 0 | 含 `ViewLocatorTests` 对页面视图映射的钉死断言 |

- 全解决方案构建：**0 警告 0 错误**。
- 本任务新增用例合计 **59**：Services 34（`PlayTimeTrackerTests` 14 + `InstallHistoryMaintenanceTests` 12 + `UnmanagedTraversalBoundaryTests` 8）、ViewModels 25（`InstancesPlayTimeTrackingTests` 9 + `InstallHistoryMaintenancePageViewModelTests` 12 + `SettingsLocaleChoicesTests` 4）。
- 复现说明：`tests/CkanUI.TestRunner` 只按**程序集名**过滤（`tests/CkanUI.TestRunner/Program.cs:21-23`），传 fixture/命名空间片段不会只跑那一部分。

---

## 6. 维护约定

1. 新增特性或改动实现位置时，`feature-matrix.md` 与本文要一起改；本文只维护「证据」与「不一致」，结论仍归母表。
2. 本文 `Fixture:N` 按 §0.2 口径重算（`[Test]` + `[TestCase]` 行数），不要直接抄 runner 的展开数。
3. 发现母表与实现不一致时，**不要**直接改母表结论，在 §2 记一条并给建议——结论变更需要母表作者确认。

---

## 7. 第二轮补录（2026-10-29）

本轮为「完善功能 + 视觉/动效/交互升级」阶段，四条被 §2 反证的缺口中有三条补齐、一条收窄。判定口径不变（基础逻辑 + 界面落点双条件），证据按下表复核：

| F-ID | 本轮补齐内容 | 关键实现证据 | 覆盖用例 | 状态变化 |
|---|---|---|---|---|
| F-08 | 实例自检服务：锁诊断（文件/目录两态 + 占用者三档线索）、强制解锁（游戏运行中拒绝并可重试、非空锁目录不递归、动作写 JSONL 操作记录）、坏注册表隔离（改名 `.corrupt-yyyyMMdd-HHmmss`、字节保留）、CKAN 过旧与游戏版本过低给升级建议；实例页「实例自检」区 + 三个命令 + 动作前确认 + 动作后重检 | `src/CkanUI.Services.Abstractions/Instances/IInstanceRepairService.cs:12`、`src/CkanUI.Services/Instances/InstanceRepairService.cs:24`、`src/CkanUI.ViewModels/Pages/InstancesPageViewModel.cs:1195`/`:1243`/`:1278`/`:1352` | `InstanceRepairServiceTests`（24）、`InstancesRepairTests`（22） | 部分 → **已实现** |
| F-52 | 更新安装器：复用 `IDownloadService` 下载、SHA256/大小校验、zip-slip 整包拒绝、剥壳最多 4 层、覆盖前备份、失败或取消自动回滚、成功置 `RestartRequired`；检查侧解析 GitHub `assets` 选 Windows zip 并把 `digest` 归一成 SHA256，弹窗据 `AssetUrl` 自动推导安装计划 | `src/CkanUI.Services.Abstractions/Update/IUpdateInstaller.cs:14`、`src/CkanUI.Services/Update/UpdateInstaller.cs:30`、`src/CkanUI.Services/Update/UpdateCheckService.cs:17`、`src/CkanUI.Services.Abstractions/Dtos/UpdateCheckResult.cs`、`src/CkanUI.ViewModels/Dialogs/UpdateAvailableDialogViewModel.cs:33` | `UpdateInstallerTests`（17）、`UpdateInstallDialogTests`（15）、`UpdateCheckServiceTests`（11）、`UpdateDialogViewModelTests`（9） | 部分 → **已实现** |
| F-26 | 详情面板由分区单页改为四页签（元数据 / 关系 / 全部版本 / 文件与内容），标题带计数角标，切换模组复位到第一页，并真正挂进模组页右侧面板 | `src/CkanUI.ViewModels/Dialogs/ModDetailsViewModel.cs:288`/`:299`、`src/CkanUI.App/Views/Dialogs/ModDetailsView.axaml:61`（`<TabControl>`）/:67/:135/:204/:282、宿主 `src/CkanUI.App/Views/ModsPageView.axaml:314` | `DialogsModDetailsTabsTests`（27）、`DialogsModDetailsTests`（15）、`ModDialogsTests`（25） | 部分 → **已实现** |
| F-30 | 版本集合与「安装该版本」入口已通（`ChangeRequest(..., version.Version)` → `BuildAsync`），但**无人填充 `ModDetailsSnapshot.Versions`**：`src/CkanUI.ViewModels/Pages/Mods/ModDetailsSnapshotBuilder.cs` 不填该字段，`CkanUI.Services.Abstractions` 也没有可用版本集合的来源（`RegistrySnapshot` 只有已装清单） | `src/CkanUI.ViewModels/Dialogs/ModDetailsViewModel.cs:340`/`:636`/`:637`/`:691`、`src/CkanUI.App/Views/Dialogs/ModDetailsView.axaml:204` | 无专门断言（仅空态与页签由 `DialogsModDetailsTabsTests` 覆盖） | 部分实现（收窄：界面已就位，缺数据源） |

汇总变化：**已实现 51 → 54**、**部分实现 4 → 1**、未开始 0 → 0。

> 下一轮若要收口 F-30，需要先在 `CkanUI.Services.Abstractions` 里加「某模组可用版本集合」的契约（当前 `RegistrySnapshot`/`InstalledModuleInfo` 都不含版本历史），再让 `ModDetailsSnapshotBuilder` 填充 `Versions` —— 属新接口工作量，不是界面缝补。

---

## 8. 第三轮补录（2026-10-29）

本轮按 §7 末尾点名的方向补上 F-30 的数据源，55 项至此全部达到「基础逻辑 + 界面落点」双条件。

| F-ID | 本轮补齐内容 | 关键实现证据 | 覆盖用例 | 状态变化 |
|---|---|---|---|---|
| F-30 | 新增「模组可用版本集合」契约与实现：`IRegistryQuerier.AvailableByIdentifier` 跨所有已加载仓库取同一标识符全部版本（内存对象图，不联网不读盘），投影成纯记录 `ModVersionOption`（版本号降序、KSP 兼容文案、发布时间、下载体积/链接、已装、兼容、不稳定）；`ModsPageViewModel` 在选中行变化时异步查询并回填详情面板，取消上一次查询，失败只记错误栏不打断浏览 | `src/CkanUI.Services.Abstractions/Mods/IModVersionCatalog.cs:11`、`ModVersionOption.cs:18`、`src/CkanUI.Services/Mods/CkanModVersionCatalog.cs:17`/\`:32`/`:83`、`src/CkanUI.ViewModels/Pages/Mods/ModDetailsSnapshotBuilder.cs:56`、`src/CkanUI.ViewModels/Dialogs/ModDetailsViewModel.cs:577`、`src/CkanUI.ViewModels/Pages/ModsPageViewModel.cs:1246`/\`:1271` | `CkanModVersionCatalogTests`（13）、`ModsVersionCatalogTests`（12） | 部分 → **已实现** |

汇总变化：**已实现 54 → 55**、部分实现 1 → 0、未开始 0 → 0（母表 §1 F-30 行、§2.1 汇总与 `docs/README.md` 索引口径同步更新）。

> 契约设计要点：视图模型不引用 `CKAN` 类型，服务层把 `CkanModule` 投影成纯记录 `ModVersionOption`；仓库里查不到该标识符（`ModuleNotFoundKraken`，例如只在本地装过）返回空列表而不是失败，详情页因此显示「暂无版本记录」；其余异常返回可重试的失败文案。

> 仍未落地的细节（不改变状态）：版本列表没有「只看兼容版本」过滤与「强制安装不兼容版本」的二次确认；「文件与内容」页签的 `InstalledFiles` 仍无契约来源（`RegistrySnapshot` 只有已装清单，没有逐文件表），页签保持空态。

