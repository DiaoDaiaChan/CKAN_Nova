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
| F-02 | 克隆实例（源/目标路径、新名、设为默认、切换） | `src/CkanUI.Services/Instances/InstanceService.cs:71`（`CloneAsync`）；VM 侧 `src/CkanUI.ViewModels/Pages/InstancesPageViewModel.cs:530`（`BeginClone`） | InstancesPageViewModelTests:28（克隆：`:195-197` 进模式、`:203` 未填表不调服务、`:213-218` 调用一次且源/默认新名、`:227-233` 目标已存在、`:241-268` 取消与校验失败）、InstancesProjectionTests:9（`:103` 同级路径）、InstanceServiceTests:1 | 强（VM 入口与路径推导都有直接断言；服务层 CloneAsync 仍无断言，属「VM 强 / 服务层薄」，见 §12.1） |
| F-03 | Steam 库导入 | `src/CkanUI.Services/Platform/SteamLibraryLocator.cs:22`、`src/CkanUI.Core/Steam/SteamLibraryParser.cs:12` | SteamLibraryLocatorTests:10、SteamLibraryParserTests:2 | 强 |
| F-04 | 实例右键：打开目录 / 加入 CKAN / 克隆 | `InstancesPageViewModel.cs:530`（`BeginClone`）、`:789`（`OpenFolderAsync`）、`:719`（`ForgetAsync`）；菜单本体 `src/CkanUI.App/Views/InstancesPageView.axaml:235-252`（`FAMenuFlyout` 六项）+ 工具条 `:29-37` | InstancesPageViewModelTests:28（`:430-441` 打开目录：`OpenFolderCalls==1`、`LastOpened==MainDir`、状态栏提示）、InstancesLaunchCommandTests:44 | 部分或缺口：打开目录与克隆有断言，但「加入 CKAN」在 `src/`、`tests/` 全仓 grep `加入 CKAN\|JoinCkan\|AddToCkan\|JoinToCkan` **0 命中**；菜单本体仍是 XAML 无自动化（见 §4.1） |
| F-05 | 启动游戏（多命令选择） | `src/CkanUI.Services.Abstractions/IInstanceShellLauncher.cs:24`（`LaunchGameAsync`）、`src/CkanUI.App/Views/InstancesPageView.axaml.cs:208`（真实进程启动） | InstancesLaunchCommandTests:44、InstancesLaunchReleaseTests:8 | 中：真实进程外启动需手测，测试用假 launcher |
| F-06 | 游戏命令行编辑（列表、重置、增、接受、取消） | `src/CkanUI.ViewModels/Pages/Instances/LaunchCommandsPanelViewModel.cs:17` | InstancesLaunchCommandTests:44（`:446` 起「F-06」专区 9 例：添加 / 重复拒绝 / 修改 / 取消 / 删到最后一条 / 上移下移 / 重置默认 / 接受交回页面 / 无文本通道提示） | 强（见 §12.2） |
| F-07 | 打开游戏目录（菜单 + 托盘） | `InstancesPageViewModel.cs:789`（`OpenFolderAsync`）/`:810`（`launcher.OpenFolderAsync(row.GameDir)`）+ `IInstanceShellLauncher` 打开目录成员 | InstancesPageViewModelTests:28（`:430-441`：调用次数、路径、状态栏三断言）、InstancesLaunchCommandTests:44 | 强（外壳调用与提示都有断言；真实文件管理器仍是平台替身，同 F-05，见 §12.3） |
| F-08 | 实例锁定/注册表损坏处置（删锁、回滚、提示升级 CKAN） | `src/CkanUI.Services.Abstractions/Instances/IInstanceRepairService.cs:12`（Diagnose/ForceUnlock/Quarantine/CheckCompatibility）、`src/CkanUI.Services/Instances/InstanceRepairService.cs:24`、`src/CkanUI.Services.Abstractions/Instances/InstanceRepairModels.cs:6`、`src/CkanUI.ViewModels/Pages/InstancesPageViewModel.cs:1195`（`Repair` 注入）/`:1243`/`:1278`/`:1352`（三个命令）、`src/CkanUI.App/Views/InstancesPageView.axaml`（「实例自检」区）；原锁定拦截仍在 `src/CkanUI.Services/Instances/InstanceService.cs:48-49`、`src/CkanUI.Services/Registry/RegistryService.cs:38` | InstanceRepairServiceTests:24、InstancesRepairTests:22、RegistryServiceTests:1、InstanceServiceTests:1 | 已实现：删锁（游戏运行中拒绝并可重试、非空锁目录不递归、动作写 `%LocalAppData%/CKAN/instance-repair-journal.jsonl`）、坏注册表隔离为 `registry.json.corrupt-yyyyMMdd-HHmmss`（字节保留）、CKAN 过旧与游戏版本过低给升级建议；§2.6 的原反证留档 |
| F-09 | 切换实例后按需刷新 | `InstanceService.cs:37`（RefreshAsync）、`:40`（SwitchAsync）、`:28`（InstanceChanged 事件） | InstancesPageViewModelTests:28、InstancesProjectionTests:9、InstanceServiceTests:1 | 强 |
| F-10 | 元数据仓库管理 | `src/CkanUI.Services/Repositories/RepositoryService.cs:140`（LoadRepositoriesAsync）、`:149`（AddRepositoryAsync）、`SettingsPageViewModel.cs:20`+`:540`（仓库分组） | RepositoryCatalogTests:26、RepositoryServiceListTests:12、ReposPageViewModelTests:29、SettingsPageViewModelTests:51 | 强 |
| F-11 | 认证令牌（host + token，脱敏显示） | `src/CkanUI.Services/Settings/SettingsService.cs:59`/`:61`（主机枚举与掩码）、`src/CkanUI.ViewModels/Pages/Settings/AuthTokenEditorViewModel.cs:13`、`SettingsPageViewModel.cs:94`（`TokenEditor`） | SettingsPageViewModelTests:51、SettingsServiceTests:2 | 强 |
| F-12 | 仓库刷新（手动/启动时/定时/暂停/Ctrl 强制重下） | `RepositoryService.cs:84`（`RefreshAsync(ct)`）、`:87`（`RefreshAsync(bool skipETags, ct)`）、`:101`（强制重下进度文案） | RepositorySkipETagsTests:2、RepositoryServiceListTests:12 | 强（主表实现位置列为空，本表补齐） |
| F-13 | 稳定性容忍度（全局 + 单 mod 覆盖） | `src/CkanUI.ViewModels/Pages/Settings/StabilityToleranceFile.cs:18`、`src/CkanUI.ViewModels/Dialogs/ModDetailsViewModel.cs:35`（单 mod 覆盖） | SettingsStabilityToleranceTests:17（`:44` 路径、`:50/:63/:73/:76/:84` 读、`:98/:114/:123` 原子写与非法值、`:133-138` 归一、`:144-157` 单模组覆盖、`:171-172` 同步读、`:181-188` 覆盖写） | 强（文件级读写与单模组覆盖都有直接断言，见 §12.4） |
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
| F-35 | 推荐/建议/被支持选择 | `RecommendationDialogViewModel.cs:14` | ModDialogsTests:25（`:22/:35/:48/:62/:78/:90/:104/:119/:136/:153` 共 10 处直接构造，含单候选与空 Request 零候选；`Request(...)` 辅助在 `:379`） | 强（无独立 fixture 文件，但断言直指该类并覆盖候选边界，见 §12.5） |
| F-36 | 推荐审计 | `AuditRecommendationsDialogViewModel.cs:18` | AuditRecommendationsDialogTests:10 | 强 |
| F-37 | 安装/卸载/升级执行（进度、取消、重试、日志） | `src/CkanUI.Services/Installers/InstallerService.cs:21`、`src/CkanUI.Services/Operations/OperationQueue.cs:21`、`OperationQueueViewModel.cs:22` | InstallerServiceTests:14、OperationsQueueViewModelTests:20、DownloadServiceTests:18 | 强 |
| F-38 | 下载失败处置（逐项 Retry/Skip、复制诊断） | `src/CkanUI.ViewModels/Dialogs/DownloadErrorsDialogViewModel.cs:15`、`src/CkanUI.Services/Downloads/DownloadFailureClassifier.cs:34` | DownloadErrorsDialogViewModelTests:11、DownloadFailureClassifierTests:13、DownloadRetryPolicyTests:6 | 强（主表实现位置列为空，本表补齐） |
| F-39 | 从 .ckan 安装（多选文件） | `src/CkanUI.Services/ModPacks/ModPackCatalog.cs:22`、`ModPacksPageViewModel.cs:18` | ModPackCatalogTests:43、ModPacksPageViewModelTests:43 | 强 |
| F-40 | 导入已下载 mods（ZIP → 缓存） | `src/CkanUI.Services/Cache/CkanCacheStore.cs:28`、`CachePageViewModel.cs:239`（`ImportZipAsync`） | CacheServiceTests:14、CachePageViewModelTests:22 | 强 |
| F-41 | 去重（跨实例扫描并共享） | `src/CkanUI.Services/Dedup/DedupScanner.cs:71`、`DedupExecutor.cs:15`、`DedupPageViewModel.cs:20` | DedupServiceTests:13、DedupPageViewModelTests:12 | 强（缺口照主表：非 Windows 无 inode、无共享存储目录模式） |
| F-42 | 未管理文件（树、删除、接管为受管） | `src/CkanUI.Services/Unmanaged/UnmanagedScanner.cs:15`+`:62`（改走 `GameDataWalker`）、`UnmanagedAdopter.cs:22`、`CkanManagedFileRegistrar.cs:19`、`UnmanagedPageViewModel.cs:15`；新增 `GameDataWalker.cs:15`/`:59` | UnmanagedServiceTests:20、UnmanagedAdopterTests:10、UnmanagedAdoptTests:7、UnmanagedPageViewModelTests:18、**UnmanagedTraversalBoundaryTests:8（本任务新增）** | 强（本轮补齐联接遍历/接管/拒删边界，见 §3.1） |
| F-43 | 残留目录清理 | `src/CkanUI.Services/Cleanup/ResidualCleanupService.cs:22`、`ResidualCleanupDialogViewModel.cs:22`、`CkanInstallerBackend.cs:180` | ResidualCleanupServiceTests:15、ResidualCleanupDialogTests:12 | 强 |
| F-44 | 安装历史（时间戳列表、已装/未装分组、从历史重装） | `src/CkanUI.Services/History/InstallHistoryService.cs:21`、`InstallHistoryStore.cs:56`/`:69`/`:104`（`DeleteAt`）/`:140`（`Clear`）、`InstallHistoryRecorder.cs:84`/`:106`、`InstallHistoryPageViewModel.cs:18`/`:230`（`DeleteSelectedAsync`）/`:268`（`ClearHistoryAsync`）、`HistoryRecordingInstaller.cs:22` | InstallHistoryServiceTests:14、InstallHistoryPageViewModelTests:15、**InstallHistoryMaintenanceTests:12（新增）**、**InstallHistoryMaintenancePageViewModelTests:12（新增）** | 强（删除/清空缺口已补齐见 §3.2；`RemoveExtras` 勾选也已落地：`InstallHistoryPageViewModelTests:233-264` 断言默认关闭与勾选后进预览/请求，见 §9 ⑥、§12.6） |
| F-45 | 导出：已装列表 / modpack | `src/CkanUI.Services/ModPacks/InstalledListExporter.cs:15`（主表写在 ViewModels 下，实际在 Services）、`CkanModPackStore.cs:27` | InstalledListExporterTests:10、ModPacksPageViewModelTests:43 | 强（路径漂移已校正） |
| F-46 | modpack 编辑器 | `src/CkanUI.Services/ModPacks/ModPackEditorStore.cs:22`、`ModPackEditorValidator.cs:25`、`ModPackEditorPageViewModel.cs:25` | ModPackEditorStoreTests:20、ModPackEditorValidatorTests:26、ModPackEditorPageViewModelTests:24 | 强（主表 @BT@ 占位符已修正） |
| F-47 | 下载统计（饼图 + 表格） | `src/CkanUI.Services/Stats/DownloadStatsAggregator.cs:8`、`DownloadStatsService.cs:10`、`DownloadStatsPageViewModel.cs:14`、`src/CkanUI.App/Controls/DonutChart.cs:13` | DownloadStatsServiceTests:19、DownloadStatsPageViewModelTests:16 | 强（主表 @BT@ 占位符已修正；按仓库维度未做） |
| F-48 | 游戏时长（实例名 / 小时数，F2 就地编辑） | `src/CkanUI.Services/PlayTime/PlayTimeService.cs:22`/`:46`（`DefaultPath`）、`PlayTimeSessionLedger.cs:28`、`src/CkanUI.Services.Abstractions/PlayTime/IPlayTimeTracker.cs`、`PlayTimePageViewModel.cs:12`、`InstancesPageViewModel.cs:95`（`PlayTime` 属性）/`:859`（BeginSession）/`:937`（EndSession） | PlayTimeServiceTests:3、**PlayTimeTrackerTests:14（新增）**、PlayTimePageViewModelTests:1、**InstancesPlayTimeTrackingTests:9（新增）** | 强（自动累计缺口见 §3.3；重置入口见 `PlayTimePageViewModelTests:70-167`（确认后调用 / 取消 / 失败 / 异常 / 无对话框禁用），见 §9 ⑦、§12.7；仍缺真实实例下的整链集成） |
| F-49 | 设置总表 | `SettingsPageViewModel.cs:20`、`:540`（`SettingGroupKey.Repository` 分组）；主表 :264 未复现 | SettingsPageViewModelTests:51、SettingsAccentTests:25、SettingsMotionScaleTests:24、SettingsFontScaleTests:17、SettingsStabilityToleranceTests:17、SettingsBackgroundTests:12、SettingsKeyMirrorTests:11、SettingsAccentInstanceTests:8、SettingsTrayTests:8、SettingsDensityTests:7、SettingsHighContrastTests:4 | 强 |
| F-50 | 下载缓存管理 | `src/CkanUI.Services/Cache/CacheService.cs:17`、`CachePageViewModel.cs:11`/`:193`（Refresh）/`:213`（PurgeAll）/`:325`（OpenFolder）/`:359`（ResetLimit） | CacheServiceTests:14、CachePageViewModelTests:22 | 强 |
| F-51 | 托盘图标（启用/最小化到托盘；7 项右键菜单） | `src/CkanUI.ViewModels/Shell/TrayMenuViewModel.cs:23`、`TrayMenuItemViewModel.cs:39`、`src/CkanUI.App/Tray/TrayHost.cs:29`、`TrayHostActions.cs:27`、`ITrayHostActions.cs:16`、`UiSettings.MinimizeToTray`/`CloseToTray` | TrayMenuTests:17、SettingsTrayTests:8 | 中：VM 侧 7 项菜单见 `TrayMenuTests:34-40`；宿主层（`TrayHost.cs:29` 的真实图标/菜单点击）无自动化通道，需桌面手测（见 §4.1） |
| F-52 | 自动更新（启动时检查、dev builds、检查/安装、新版本对话框） | 检查侧 `src/CkanUI.Services.Abstractions/IUpdateCheckService.cs:10`、`src/CkanUI.Services/Update/UpdateCheckService.cs:17`（`PickUpdateAsset` 解析 `assets[].browser_download_url` 的 Windows zip 与 `digest` 归一成 SHA256）、`src/CkanUI.Services.Abstractions/Dtos/UpdateCheckResult.cs`（`AssetUrl`/`AssetSha256`/`AssetSize`）；安装侧 `src/CkanUI.Services.Abstractions/Update/IUpdateInstaller.cs:14`、`src/CkanUI.Services/Update/UpdateInstaller.cs:30`、`src/CkanUI.ViewModels/Dialogs/UpdateAvailableDialogViewModel.cs:33`（`InstallCommand`）、`src/CkanUI.App/Views/Dialogs/UpdateAvailableDialogView.axaml`（安装区） | UpdateInstallerTests:17、UpdateInstallDialogTests:15、UpdateCheckServiceTests:11、UpdateDialogViewModelTests:9 | 已实现：下载走既有 `IDownloadService`、SHA256/大小校验、zip-slip 整包拒绝、剥壳最多 4 层、覆盖前 `backup-<时间戳>` 备份、失败或取消自动回滚、成功置 `RestartRequired`；残留见 `docs/design/app-update.md` §10（不校验签名、原地升级需独立 updater）；§2.5 的原反证留档 |
| F-53 | 语言与本地化（选择需重启；多语 resx/json） | `src/CkanUI.Localization/Localizer.cs:16`、`LanguagePackLoader.cs:12`、`SettingsPageViewModel.cs:54`（`LocaleChoicesProvider`）/`:96`（`RefreshLocaleChoices`）/`:572`（语言项） | LocalizerTests:10、LanguageSwitchTests:8、PseudoLocalizationTests:10、ShippedPacksTests:7、FallbackTests:6、FormatTests:6、LocaleInfoTests:3、LocalizationKeyDriftTests:4、LocalizerCacheParticipantTests:3、**SettingsLocaleChoicesTests:4（新增）** | 强（候选与已发包语种对齐见 §3.4、§12.8；页面 VM 文案已迁 852 处（§11.1）并有三条守护用例（§11.4），只剩子 VM 与静态格式化器（§11.5）属横切债） |
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

> **第五轮对账（2026-10-29）**：原 16 行按 `tests/**` 的真实断言逐行复核——**12 行移出、4 行保留**。判定只看「有没有断言该行为的用例」，不看文件名（`InstancesLaunchCommandTests`、`ModDialogsTests` 这类大 fixture 里都藏着专门用例）。逐条 file:line 见 **§12**。

### 4.1 仍证据不足（4 行）

| F-ID | 不足点 | 缺少什么断言 / 补救建议 |
|---|---|---|
| F-04 | 规格里的「加入 CKAN」在 `src/`、`tests/` 全仓 grep `加入 CKAN\|JoinCkan\|AddToCkan\|JoinToCkan` **0 命中**；打开目录与克隆两动作已有用例（§1 F-04 / F-02） | 二选一：实现该动作（可走 `InstanceService.AddAsync`，`InstanceService.cs:59`）并补用例，或把该动作从母表描述里去掉；菜单本体在 `InstancesPageView.axaml:235-252`，XAML 层无自动化，列桌面手测 |
| F-05 | 真实进程启动在 `src/CkanUI.App/Views/InstancesPageView.axaml.cs:208`，测试用假 `IInstanceShellLauncher`；`LaunchGameAsync` 在 `tests/**` grep **0 命中** | 把「按命令解析 binary/args」提成纯函数并补用例；真实启动、命令优先级、退订留桌面手测 |
| F-17 | 卸载是 ALC 尽力而为且不校验签名；`PluginServiceTests` 里 grep `Uninstall` **0 命中**（`AdapterInstallerTests:603` 是 adapter 包目录删除，不是插件卸载） | 补 `Uninstall_*` 断言：卸载后移出激活集合、程序集不可再解析、失败保留条目；真实 DLL 卸载只标「尽力而为」 |
| F-51 | 宿主层 `TrayHost.cs:29` 的真实图标与菜单点击无自动化通道（`TrayMenuTests.cs:18` 注释即「图标与原生菜单的装配留在 App 层的 TrayHost（无 GUI 会话无法验证）」） | VM 侧 7 项菜单已有用例（`TrayMenuTests:34-40`）；把宿主编排抽到可注入的 `ITrayHostActions` 后补用例，其余桌面手测 |

### 4.2 本轮移出（12 行）

| F-ID | 移出理由 | 依据 |
|---|---|---|
| F-02 | VM 侧克隆 6 组断言 + 投影层同级路径推导（原写「无克隆专属 fixture，入口混在 `InstancesLaunchCommandTests:45`」不成立） | `InstancesPageViewModelTests.cs:195-268`、`InstancesProjectionTests.cs:103`（§12.1） |
| F-06 | `InstancesLaunchCommandTests` 里的「F-06」专区共 9 例 | `InstancesLaunchCommandTests.cs:446-572`（§12.2） |
| F-07 | 打开目录有调用次数 / 路径 / 状态栏三断言 | `InstancesPageViewModelTests.cs:430-441`（§12.3） |
| F-13 | `SettingsStabilityToleranceTests` 直测 `StabilityToleranceFile` 读写、归一与单模组覆盖 | `SettingsStabilityToleranceTests.cs:44-188`（§12.4） |
| F-35 | `ModDialogsTests` 内 10 处直接构造 `RecommendationDialogViewModel`，覆盖单候选与空 Request | `ModDialogsTests.cs:22-153`（§12.5） |
| F-44 | `RemoveExtras` 勾选已落地并有默认值 / 传递断言 | `InstallHistoryPageViewModelTests.cs:233-264`（§12.6） |
| F-48 | 重置入口 5 例（确认后调用 / 取消 / 失败 / 异常 / 无对话框禁用） | `PlayTimePageViewModelTests.cs:70-167`（§12.7） |
| F-53 | 页面文案 852 处已迁 + 三条守护用例；语言候选 4 例 | §11.1 / §11.4、`SettingsLocaleChoicesTests.cs:13-99`（§12.8） |
| F-08 | 第二轮补齐：自检服务 + 页面三命令 + 动作前确认 | §7 表（F-08 行）、`InstanceRepairServiceTests`（24）、`InstancesRepairTests`（22） |
| F-26 | 第二轮补齐：四页签容器 + 标题计数 + 切换复位 | §7 表（F-26 行）、`DialogsModDetailsTabsTests`（27） |
| F-30 | 第三轮补齐：版本集合契约 + 页面注入 + 指定版本安装 | §8、`CkanModVersionCatalogTests`（13）、`ModsVersionCatalogTests`（12） |
| F-52 | 第二轮补齐：下载—校验—替换—回滚 | §7 表（F-52 行）、`UpdateInstallerTests`（17） |

---

## 5. 复核命令与本轮真实计数

> **时效说明**：下面的计数是第 5 轮当时的状态（当时 `ModConfig` 有 24 例失败，属并行改动中的适配器）。**2026-10-29 全量基线**已是 3110 例、0 失败、0 跳过（App 163 / Architecture 21 / Core 3 / Localization 76 / ModText 19 / Services 1205 / Sfs 93 / ViewModels 1530，`artifacts/final-tests-6.log` 与当日实测一致），`docs/design/milestones.md` §6.2 记的是后者；引用测试数字时以基线为准，本节表格只作历史留档。

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


---

## 9. 第四轮补录（2026-10-29，功能缺口九项）

本轮不新增特性编号，专收 §6 与盘点里剩下的九条功能缺口。判定口径不变（服务实现 + 界面落点双条件），逐条证据：

| # | 缺口 | 本轮补齐内容 | 关键实现证据 | 覆盖用例 |
|---|---|---|---|---|
| ① | 启动流程缺 E6 状态机（未还注册表锁、无精简模式） | 点「启动」先只放 Core 数据层（`RegistryManager.DisposeAll()` + 丢仓库快照）再拉进程，锁不必等 10 秒后的均衡档；放锁成功进「精简模式」（状态栏徽标 + 「恢复数据」按钮），启动失败立刻回滚，游戏退出或手动恢复退出精简模式；放锁失败不阻断启动。页面状态 `Idle/Preparing/Starting/Running/Restoring` 线性推进 | `src/CkanUI.Services.Abstractions/IResourceReleaseService.cs:38`（DIM `ReleaseCoreDataAsync`，默认返回失败以免替身假装放锁）、`src/CkanUI.Services/Lifecycle/ResourceReleaseService.cs:206`/`:308`、`src/CkanUI.ViewModels/Pages/Instances/GameLaunchPhase.cs`、`src/CkanUI.ViewModels/Pages/InstancesPageViewModel.cs:146`/`:179`/`:893`/`:1053`/`:1085`/`:1124`、`src/CkanUI.App/Views/InstancesPageView.axaml:292`/`:296`/`:299` | `InstancesLaunchReleaseTests`（套件 16 例，本轮 +8：先还锁再启动 / 关闭档不动锁 / 放锁失败照常启动 / 启动失败回滚 / 退出清精简模式 / 手动恢复 / 恢复失败可重试 / 阶段文案） |
| ② | L4 工作集归还未接 psapi | Windows 上 `EmptyWorkingSet` 真回收，用前后 `Process.WorkingSet64` 差值报数；非 Windows 与 Win32 失败都安全降级（0 项 + Warn，不抛） | `src/CkanUI.Services/Lifecycle/PlatformMemory.cs:52`/`:72`/`:92`/`:106`、`src/CkanUI.Services/CkanUI.Services.csproj:6`（`AllowUnsafeBlocks`，源生成 P/Invoke 硬要求） | `PlatformMemoryTests`（3；平台差异就地分支，不用 `Assert.Ignore` 以免汇总变 Warning） |
| ③ | Mod 配置缺取色器 / 按键捕获 / 路径浏览 | 颜色字段 22×22 色块预览 + 十六进制输入（认不出的写法按原文写盘）；按键字段按住修饰键即捕获成 `Ctrl+Alt+Shift+Win+Key`；路径字段「浏览…」选目录填草稿 | `src/CkanUI.ViewModels/Pages/ModConfig/ModConfigColorText.cs:21`/`:86`、`KeyGestureText.cs:20`、`ModConfigFieldRowViewModel.cs:154`/`:174`/`:177`/`:188`/`:251`、`src/CkanUI.App/Converters/HexColorToBrushConverter.cs:17`、`src/CkanUI.App/Views/ModConfigPageView.axaml:281`/`:294`/`:304`、`ModConfigPageView.axaml.cs:90`/`:159` | `ModConfigFieldEditingTests`（改写 9 例）、`ModConfigColorAndGestureTests`（9 例） |
| ④ | adapter 覆盖窄、无导入 / 哈希 / 更新 | 「Mod 配置」页可从本地 `.json`/`.zip` 装第三方 adapter：SHA-256 清单校验、zip-slip 整包拒绝、原子落盘 + 失败回滚、装完热重扫；远程 URL 与 ETag 条件请求（304 = 无更新）在服务层就绪 | `src/CkanUI.Services.Abstractions/ModConfig/AdapterPackageModels.cs`、`IAdapterPackagePorts.cs`、`src/CkanUI.Services/ModConfig/{AdapterInstaller,AdapterPackageInstaller,AdapterPackageReader,AdapterManifestFile,HttpAdapterPackageFetcher}.cs`、`AdapterImportServiceCollectionExtensions.cs:18`、`src/CkanUI.ViewModels/Pages/ModConfigPageViewModel.cs:436`、`src/CkanUI.App/Views/ModConfigPageView.axaml:83`、`docs/design/adapter-import.md` | `AdapterInstallerTests`（29）、`ModConfigAdapterImportTests`（9） |
| ⑤ | ModText 抛异常而非结果对象、`Stale` 结构性不可达 | 导出 / 同步改 `OperationResult`（同步的显式目录不存在直接失败，不静默回落主目录）；新增 `(identifier, field)` 二级索引，机翻条目在原文变化后判 `Stale` 并可回退人工译文 | `src/CkanUI.ModText/IModTextService.cs:78`/`:84`、`src/CkanUI.Services/ModText/ModTextService.cs:415`/`:446`、`src/CkanUI.ModText/ModTextProvider.cs:21`/`:58`/`:136`/`:188`/`:224`、`src/CkanUI.ViewModels/Pages/ModTextPageViewModel.cs:349` | ModText +3、Services +3、ViewModels +2 |
| ⑥ | 安装历史「卸载快照之外的模组」无界面开关 | 工具条加勾选（默认关闭），仅勾选时预览多一句风险说明；请求随勾选带 `RemoveExtras` | `src/CkanUI.ViewModels/Pages/InstallHistoryPageViewModel.cs:148`/`:170`/`:194`/`:361`/`:393`/`:602`、`src/CkanUI.App/Views/InstallHistoryPageView.axaml:77` | ViewModels 安装历史 +2、Services +4 |
| ⑦ | 时长页没有重置入口 | 按实例「重置」：二次确认（含实例名与「无法撤销」），先清会话账本再把基线写 0 | `src/CkanUI.Services.Abstractions/IPlayTimeService.cs:24`、`src/CkanUI.Services/PlayTime/PlayTimeService.cs:96`/`:131`、`src/CkanUI.Services/PlayTime/PlayTimeSessionLedger.cs:125`、`src/CkanUI.ViewModels/Pages/PlayTimePageViewModel.cs:47`/`:70`、`src/CkanUI.App/Views/PlayTimePageView.axaml:7` | ViewModels 时长 +5、Services +4 |

③④ 的接线：`AddCkanAdapterImport()` 挂在 `AddCkanModConfig()` 之后（安装器要拿 `IAdapterCatalog` 才能在装完热重扫）；`ModConfigPageViewModel` 的导入端口与路径选择器是**可写属性**，由视图挂载时补（构造参数已 7 个，再加会让 DI 注册与设计时构造都变脆）。

⑨ 三条已知取舍里，**背景「游戏截图」来源本轮补齐**（不是取舍，是缺口）：设置页「背景来源」新增「游戏截图（最新一张）」，
`src/CkanUI.Services/Settings/BackgroundImageLocator.cs` 取 `<GameDir>/Screenshots` 里最新一张，
`src/CkanUI.App/Theming/BackgroundHost.cs:334`(`IsImageKind`)/`:341`(`ResolveImagePath`)/`:351`(`ResolveImagePathFor` 纯函数可测)/`:376`(`BuildSignature` 收 imagePath) 接入，
解析出的路径进缓存签名；用例 `tests/CkanUI.Services.Tests/Settings/BackgroundImageLocatorTests.cs`（11）+ `tests/CkanUI.App.Tests/BackgroundHostTests.cs` 新增 2 例。
另两条仍按取舍处理，只在 `docs/design/milestones.md` §4 标注性质：实例重命名不搬目录与上游语义一致（第 2 条）、插件页签视图必须自带是设计约束（第 5 条）。

> 九项里只剩 ⑧（页面 VM 运行期文案 i18n）是横切债，另见 §10。
## 10. 页面 VM 运行期文案的本地化（缺口 ⑧）

### 10.1 机制

XAML 侧文案本来就是 `DynamicResource` 走词包，缺的是页面 ViewModel 里拼出来的运行期文案（标题、状态栏、校验反馈、确认提示）。
现在由 `src/CkanUI.ViewModels/PageViewModel.cs` 提供两个可写解析器与两个受保护方法：

- `public Func<string, string>? TextResolver { get; set; }`、`public Func<string, object?[], string>? TextFormatter { get; set; }`；
- `protected string Text(string fallback, string key)` 与 `protected string Text(string fallback, string key, params object?[] args)`：
  有解析器时取译文（带占位符的走 formatter），没有解析器时返回 fallback；formatter 抛 `FormatException` 也回落 fallback，绝不让一条坏译文把页面打崩。

解析器由 `src/CkanUI.ViewModels/Navigation/NavigationService.cs` 在导航时用 `??=` 挂上（页面自己设过就不覆盖），
具体值来自 `src/CkanUI.App/ServiceRegistration.cs:275` 的 `ILocalizer.Get` / `ILocalizer.Format`。

**为什么绕这一层**：① 页面 VM 不许引用本地化实现（04 篇分层铁律，也便于设计时构造与单测替身）；
② 既有 1500+ 用例断言的是中文原文，只要不给解析器就退回中文，用例天然继续绿；
③ 给解析器就能在一条用例里验证「键→译文」而不必真的加载语言包。

### 10.2 覆盖与数字（第一轮）

| 项 | 数字 |
| --- | --- |
| `LocalizationKeys` 常量 | 76 → **242**（新增 166，每键带中文 `<summary>` 说明用在哪） |
| 两套语言包 | zh-CN 242 / en-US 242，与常量严格双向一致（`ShippedPacksTests` + `LocalizationKeyDriftTests`；脚本核 missing 0 / dead 0） |
| 代码 fallback 与 zh-CN 译文一致性 | 175 处 `Text(fallback, key)` 逐字比对，mismatch 0 |
| `Text(...)` 转换点 | **192**：Mods 正文 71、Instances 正文 104、16 个页面 `Title` + Instances 锁定错误文案 1 |

已覆盖的页：`ModsPageViewModel`、`InstancesPageViewModel` 整页正文；其余 16 个页面 ViewModel（Content / DownloadStats / InstallHistory / Dedup / Changes / Plugins / Cache / PlayTime / ModText / Unmanaged / Repos / Settings / ModPacks / Saves / ModPackEditor / Logs）本轮只接标题。

用例：`tests/CkanUI.ViewModels.Tests/PageTextLocalizationTests.cs` 12 例——无解析器回中文原文、假 resolver（键→`EN:键`）取到映射值、带 `{0}` 无 formatter 按中文模板格式化、有 formatter 走映射、只挂 resolver 时仍格式化中文模板、坏模板回落原文、Mods 与 Instances 真实页面两例、`NavigationService` 导航后自动挂解析器且 `??=` 不覆盖页面自带的三例。

### 10.3 刻意不本地化的东西

`_errors.Report(ex, 上下文串, 用户消息)` 的**上下文串**、日志文案、`OperationResult.Detail` 里的诊断码、字段名、单位符号、异常原文一律不搬进词包。
理由：这些是排障线索而不是界面文案——诊断码要被 grep、上下文串要和日志对得上，翻译过来只会让「用户截图 + 日志」两头对不上。

### 10.4 已知剩余

- 其余页面的正文仍是中文常量（按中文串量排序：Settings 约 193、ModPacks 66、InstallHistory 64、Unmanaged 58、Cache 56、Saves 55、Changes 45、Repos 42，其余 8–36 条不等）；
- 子 VM 与格式器（`ModsSearchPanelViewModel`、`ChangesFormatter`、`ChangesLabels`、`ModPackEditorText`、`LogGlyphs`、`InstallHistoryText`、`SaveRowViewModel`、`ModConfigFieldRowViewModel` 的中文提示）未动；
- `ModConfigPageViewModel` 自带旧式「中文即键」的 `private string T(string chinese)`（约 :932），需要统一到 `Text(fallback, key)`，混用两套解析器会打架；
- 错误栏消息（`InstancesErrorText.Compose(..., "xxx失败")` 的兜底句）未进文案层。

> 上面这一节是**第一轮**的现场记录。第二轮（12 页）与第三轮（4 页）已把它逐条关掉，最终账见 §11。

## 11. 页面 VM 运行期文案的本地化收尾（缺口 ⑧ · 第二、三轮）

### 11.1 最终覆盖

17 个页面 ViewModel 的标题与正文全部迁完，共 **852 处** `Text(...)`：

| 页面 | 站点 | 页面 | 站点 |
| --- | --- | --- | --- |
| `SettingsPageViewModel` | 197 | `ModPacksPageViewModel` | 42 |
| `InstancesPageViewModel` | 105 | `ReposPageViewModel` | 38 |
| `ModsPageViewModel` | 71 | `SavesPageViewModel` | 38 |
| `InstallHistoryPageViewModel` | 62 | `DedupPageViewModel` | 32 |
| `UnmanagedPageViewModel` | 55 | `ModTextPageViewModel` | 26 |
| `CachePageViewModel` | 51 | `PluginsPageViewModel` | 20 |
| `ChangesPageViewModel` | 43 | `LogPageViewModel` | 18 |
| | | `ModPackEditorPageViewModel` | 18 |
| | | `ContentPageViewModel` | 15 |
| | | `PlayTimePageViewModel` | 12 |
| | | `DownloadStatsPageViewModel` | 9 |

`ModConfigPageViewModel` 不在上表：它自带旧式「中文即键」的 `T(...)`，本轮把适配器统一成 `T(fallback, key, args)`（内部转调 `Text`，`_translate` 委托仍以中文原文为入参，`TranslateDelegate_IsUsedForFixedTexts` 保持绿），页面侧 62 处 `T(...)` 沿用该适配器。

### 11.2 数字

| 项 | 数字 |
| --- | --- |
| `LocalizationKeys` 常量 | 76 → **930**（每键带中文 `<summary>`） |
| 两套语言包 | zh-CN 930 / en-US 930，键集与 `{n}` 占位符完全一致（`ShippedPacksTests` + `LocalizationKeyDriftTests`），en-US 无汉字、zh 与 en 逐条相同仅 1 条（`Ui_InstallHistory_Selection_Title` 只有分隔符 `｜`） |
| 代码 fallback 与 zh-CN 译文 | 930 键全覆盖，mismatch **0** |
| `Text(...)` 站点 | **852**（19 个文件：17 页 + `PageViewModel` 自身 + `ModConfig` 适配器见上） |

### 11.3 纪律：哪些中文仍然保留

| 类别 | 例子 | 理由 |
| --- | --- | --- |
| `_errors.Report(ex, ctx, msg)` 的**第 2 参上下文串** | `"读取模组版本"` | 要和日志对得上、要能 grep |
| 日志文案 / `OperationResult.Detail` 诊断码 | `adapter-zip-slip` | 排障线索，翻译只会让截图与日志两头对不上 |
| 字段名、单位符号、文件名 | `version`、`MB` | 数据而非文案 |
| design-time 桩 | `"设计时未提供实现。"` | 只在设计器里出现，且早于页面构造拿不到 `Text()` |

### 11.4 守护用例

`tests/CkanUI.Architecture.Tests/PageTextLocalizationTests.cs`（新增，3 例）：

1. `EveryPageTextFallbackMatchesTheChinesePack` —— 正则扫 `src/CkanUI.ViewModels/Pages/**/*.cs` 里所有 `Text("…", LocalizationKeys.X)`，逐条断言 fallback 与 `i18n/packs/zh-CN.json` 中该键的译文**逐字相同**；键不存在或常量名与键值对不上直接判红。这条把「子 agent 用脚本核一致性」变成门禁。
2. `PageTextCoverageStaysAboveTheBaseline` —— 断言 `Text(...)` 站点总数 ≥ 852、覆盖文件数 ≥ 17，防止有人把已迁移的文案改回硬编码。
3. `NoPageUsesStringInterpolatedTextCalls` —— 断言源码里不出现 `$Text(`／`Text($"` 这类把插值串当 fallback 的写法（插值会让 fallback 在运行期才成形，译文无法核对）。

### 11.5 仍未覆盖（明确的下一轮清单）

子 VM 与静态格式化器拿不到 `PageViewModel.Text()`，本轮按「收益/风险」判据选择放弃，清单与改造办法见 `docs/design/i18n-runtime.md` 的「尚未覆盖」：`ChangesFormatter`（39）、`ModPackEditorText`（39）、`ModsSearchPanelViewModel`（30）、`LogGlyphs`（24）、`ChangesLabels`（19）、`LaunchCommandsPanelViewModel`（19）、`InstallHistoryText`（17）、`SettingsDiagnosticsViewModel`（14）、`StabilityToleranceFile`（14）、`ModConfigModCardViewModel`（13）、`ModConfigFieldRowViewModel`（12）、`ModPackStateText`（11）、`SaveRowViewModel`（10）、`SettingValidator`（10）等。改法：给它们加可选 `Func<string,string>` / `Func<string,string,string>` 参数，页面构造时把 `Text` 方法组传下去，缺省回落中文原文——这样既有构造点与用例都不用改，但要逐个改构造签名，故留作单独一轮。
---

## 12. 第五轮：证据不足清单对账（2026-10-29）

`§4` 原有 16 行按「有没有断言该行为的用例」逐行复核，不看文件名（大 fixture 里常有专区）。下表每条只给真实存在的 `[Test]`/断言行；行号取自 `2026-10-29` 的工作区文本，后续改动可能漂移。

### 12.1 F-02 克隆实例（移出）

`tests/CkanUI.ViewModels.Tests/InstancesPageViewModelTests.cs`：

- `:195` `h.Vm.BeginCloneCommand.Execute(null);`、`:197` 断言 `Form.Mode == InstanceFormMode.Clone`；
- `:203` 只开表单不调服务（`CloneCalls == 0`）；
- `:213-218` 提交后 `CloneCalls == 1`、`LastCloneSource == "Main"`、`LastCloneName == "Main - 副本"`；
- `:227-233` 目标已存在时仍调用并回显错误；`:241-268` 取消与校验失败都不调用。

`tests/CkanUI.ViewModels.Tests/InstancesProjectionTests.cs:103` `CloneDestination_为源目录的同级目录()`（`:105` 断言同级路径）。服务层仍薄：`tests/CkanUI.Services.Tests/Instances/InstanceServiceTests.cs` grep `Clone` **0 命中**，克隆路径推导只有投影层证据。

### 12.2 F-06 命令行编辑（移出）

`tests/CkanUI.ViewModels.Tests/InstancesLaunchCommandTests.cs`：`:364` 与 `:446` 两处分区注释均写「F-06」，`:446` 之后 9 例——`:449` 添加、`:462` 重复拒绝、`:476` 修改、`:491` 取消、`:505` 删到最后一条、`:520` / `:537` 上移下移、`:552` 重置默认、`:572` 接受交回页面（另有「无文本通道」提示一例）。

### 12.3 F-07 打开游戏目录（移出）

`InstancesPageViewModelTests.cs:430` `打开目录_调用外壳并提示路径`：`:439` `OpenFolderCalls == 1`、`:440` `LastOpened == MainDir`、`:441` 状态栏「已在文件管理器中打开：…」。实现 `src/CkanUI.ViewModels/Pages/InstancesPageViewModel.cs:789`（`OpenFolderAsync`）/`:810`（`launcher.OpenFolderAsync(row.GameDir)`）。外壳层仍是平台替身，同 F-05。

### 12.4 F-13 稳定性容忍度（移出）

`tests/CkanUI.ViewModels.Tests/SettingsStabilityToleranceTests.cs`：`:44` 路径推导（`stability_tolerance.json`）、`:50/:63/:73/:76/:84` 读取（缺失 / 坏 JSON / 未知值）、`:98/:114/:123` 原子写与非法值、`:133-138` `NormalizeTolerance`（null/空 → stable、`TESTING` → testing、`beta` → stable）、`:144-157` 单模组覆盖、`:171-172` 同步读、`:181-188` 覆盖写。原「文件级读写无独立 fixture」不成立。

### 12.5 F-35 推荐 / 建议对话框（移出）

`tests/CkanUI.ViewModels.Tests/ModDialogsTests.cs` 内 10 处直接构造：`:22`、`:35`、`:48`、`:62`、`:78`（单候选）、`:90`（空 Request 零候选）、`:104`、`:119`、`:136`、`:153`；`Request(...)` 辅助定义在 `:379`。仍无独立 fixture 文件，但断言直指该类并覆盖候选边界，按 §0.3 归「强」。

### 12.6 F-44 `RemoveExtras` 勾选（移出）

`tests/CkanUI.ViewModels.Tests/InstallHistoryPageViewModelTests.cs:233` `RemoveExtras_DefaultsToFalseAndIsCarriedAsFalse`（`:239` 默认 false、`:244/:245` 预览与请求均 false）、`:250` `RemoveExtras_CheckedIsCarriedIntoPreviewAndRestore`（`:256` 置 true、`:263/:264` 预览与请求均 true）。实现见 §9 ⑥。

### 12.7 F-48 重置入口（移出）

`tests/CkanUI.ViewModels.Tests/PlayTimePageViewModelTests.cs`：`:70` 确认后调用并刷新（`:88` `Resets == 1`、`:89` 实例名、`:92` 状态栏文案）、`:99` 取消不调用、`:119` 失败不抛、`:138` 异常收进错误文案、`:153` 无 `IDialogService` 时 `CanReset == false`（`:162-167`）。实现见 §9 ⑦。

### 12.8 F-53 语言候选（移出）

`tests/CkanUI.ViewModels.Tests/SettingsLocaleChoicesTests.cs:13`（fixture）：`:46/:53` 提供器注入 + `RefreshLocaleChoices()`、`:72/:80` 顺序与去重、`:97/:99` 空集合回退随包语种。页面文案迁移见 §11.1（852 处）与 §11.4（3 条守护用例）。

### 12.9 复核命令与结论

```bash
# F-02 / F-07：克隆与打开目录的视图模型断言
grep -n "CloneCalls\|OpenFolderCalls" tests/CkanUI.ViewModels.Tests/InstancesPageViewModelTests.cs
# F-04 缺口：加入 CKAN 全仓无实现（0 命中）
grep -rn "加入 CKAN\|JoinCkan\|AddToCkan\|JoinToCkan" src tests
# F-05 缺口：真实启动无测试（0 命中）
grep -rn "LaunchGameAsync" tests
# F-17 缺口：插件卸载无断言（0 命中）
grep -n "Uninstall" tests/CkanUI.Services.Tests/Plugins/PluginServiceTests.cs
```

结论：§4 从 16 行收缩到 **4 行**（F-04 部分、F-05、F-17、F-51），其中 F-04 的缺口是「加入 CKAN 未实现」而不是「没有断言」。母表 §1 与本文 §1 的强度列已同轮对齐；`milestones.md` §4 与 `feature-matrix.md` 的互链口径见两文引言。

