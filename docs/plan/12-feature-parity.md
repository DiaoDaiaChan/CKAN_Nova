# 12 全特性对齐清单（Feature Parity）

> 本文是"**完全特性支持**"的唯一权威判据表。核心指令（用户，2026-10-26）：新 UI 必须覆盖 CKAN 原版 GUI 的**全部**用户可感知能力，不做 MVP 减配、不砍功能。
> 上游文档：[00 索引](00-index.md) ／ [03 界面规格](03-ui-spec.md) ／ [06 WBS](06-wbs-milestones.md) ／ [11 技术栈](11-tech-stack-decision.md)。
> 证据口径：所有条目均取自仓库当前 HEAD（`c0650d45`）的 GUI 源码与资源键，可逐条复核。

## 1. "完全特性支持"的判据

| # | 判据 | 含义 |
|---|---|---|
| C1 | **功能点等价** | 原版菜单项、按钮、列、对话框、右键动作，在新 UI 中都能找到对应入口与同等效果 |
| C2 | **结果等价** | 同一输入下，注册表/缓存/配置的落盘结果与 CLI 及原版一致（由 TST-04 对拍保障） |
| C3 | **无静默降级** | 任何做不到的能力必须显式告知用户并给出替代路径，不得悄悄消失 |
| C4 | **可验收** | 每条 F-xx 都有一个可执行验收点，写入 [07 测试](07-testing.md) 的矩阵 |

"完全"指 **C1–C4**。它不要求像素级复刻，也不要求沿用 WinForms 的控件形态（见 §3）。

## 2. 特性总表

类型：**页**=主窗口页签／**框**=对话框或浮层／**动**=菜单项、按钮、右键动作／**隐**=后台行为。

### 2.1 A 实例与游戏（9 项）

| ID | 特性 | 原版证据 | 类型 | 映射任务 |
|---|---|---|---|---|
| F-01 | 多实例列表（名称/游戏/版本/时长/路径，选中、设为默认、遗忘、重命名、新建） | `GUI/Dialogs/ManageGameInstancesDialog.resx`: `GameInstallName` / `Game` / `GameInstallVersion` / `GamePlayTime` / `GameInstallPath` / `SelectButton` / `SetAsDefaultCheckbox` / `ForgetButton` / `RenameButton` / `AddNewButton` | 框 | UI-05, UI-32, INT-04 |
| F-02 | 克隆实例（源/目标路径、新名、设为默认、切换、共享 stock、可选路径复制） | `GUI/Dialogs/CloneGameInstanceDialog.resx`: `labelOldInstance` / `labelNewPath` / `checkBoxShareStock` / `OptionalPathsLabel` | 框 | UI-32, INT-04 |
| F-03 | Steam 库导入 | 同上: `ImportFromSteamMenuItem`（`TooltipText`=扫描 Steam 库） | 动 | UI-32 |
| F-04 | 实例右键：打开目录 / 加入 CKAN / 克隆 | 同上: `openDirectoryMenuItem` / `AddToCKANMenuItem` / `CloneGameInstanceMenuItem` | 动 | UI-32 |
| F-05 | 启动游戏（多命令选择） | `GUI/Controls/ManageMods.resx`: `LaunchGameToolStripMenuItem`="Play" | 动 | UI-28, INT-04 |
| F-06 | 游戏命令行编辑（列表、重置、增、接受、取消） | `GUI/Dialogs/GameCommandLineOptionsDialog.resx`: `CmdLineColumn` / `ResetToDefaultsButton` / `AddButton` | 框 | UI-28 |
| F-07 | 打开游戏目录（菜单 + 托盘） | `GUI/Main/MainMenu.cs:70`；`GUI/Main/Main.resx`: `OpenGameDirectoryToolStripMenuItem` / `OpenGameDirectoryToolStripMenuItem1` | 动 | UI-01 |
| F-08 | 实例锁定/注册表损坏的处置（删锁、回滚、提示升级 CKAN） | `GUI/Main/MainMenu.cs:15` 起（`RegistryInUseKraken` / `RegistryVersionNotSupportedKraken`） | 隐 | INT-04, UI-09 |
| F-09 | 切换实例后按需刷新（`InstanceChanged` → 重载列表） | `GUI/Main/Main.cs` 订阅 `GameInstanceManager.InstanceChanged` | 隐 | INT-04, VM-04 |

### 2.2 B 仓库源与元数据（8 项）

| ID | 特性 | 原版证据 | 类型 | 映射任务 |
|---|---|---|---|---|
| F-10 | 元数据仓库管理（增、删、上移、下移、优先级、名称/URL 编辑、官方仓库预设） | `GUI/Dialogs/SettingsDialog.resx`: `RepositoryGroupBox` / `NewRepoButton` / `UpRepoButton` / `DownRepoButton` / `DeleteRepoButton` / `RepoNameHeader`；`GUI/Dialogs/NewRepoDialog.resx` | 框 | UI-06, INT-06 |
| F-11 | 认证令牌（host + token，增删，脱敏显示） | 同上: `AuthTokensGroupBox` / `NewAuthTokenButton` / `DeleteAuthTokenButton` / `AuthHostHeader` / `AuthTokenHeader` | 框 | UI-34 |
| F-12 | 仓库刷新（手动、启动时、定时、暂停、Ctrl 强制重下） | `GUI/Controls/ManageMods.resx`: `RefreshToolButton`(ToolTip 说明 Ctrl/Shift 点击语义) / `PauseRefreshCheckBox`；`GUI/Dialogs/SettingsDialog.resx`: `RefreshOnStartupCheckbox` / `RefreshPreLabel` / `PauseRefreshCheckBox` | 动 | UI-06, INT-06 |
| F-13 | 稳定性容忍度（全局 + 单 mod 覆盖） | `GUI/Dialogs/SettingsDialog.resx`: `StabilityToleranceLabel`；`GUI/Controls/ModInfo/Versions.resx`: `StabilityToleranceLabel`="Mod stability override" | 框 | UI-07, UI-29 |
| F-14 | 兼容游戏版本（当前版本 + 追加版本列表、通配 `1.12`→`1.12.x`、风险警示） | `GUI/Dialogs/CompatibleGameVersionsDialog.resx`: `GameVersionLabel` / `ListHeaderLabel` / `AddVersionLabel` / `WildcardExplanationLabel` / `WarningLabel` | 框 | UI-29 |
| F-15 | 首选下载主机（可用按热度 / 首选按优先级，拖动排序） | `GUI/Dialogs/PreferredHostsDialog.resx`: `AvailableHostsLabel` / `PreferredHostsLabel` / `ExplanationLabel` | 框 | UI-30 |
| F-16 | 安装过滤器（全局 + 实例级，仅影响后续安装） | `GUI/Dialogs/InstallFiltersDialog.resx`: `GlobalFiltersGroupBox` / `InstanceFiltersGroupBox` / `WarningLabel` | 框 | UI-31 |
| F-17 | 插件系统（激活/停用/重载/卸载/新增，Active 与 Dormant 两栏） | `GUI/Dialogs/PluginsDialog.resx`: `groupBox1`="Active Plugins" / `groupBox2`="Dormant Plugins" / `ActivatePluginButton` / `ReloadPluginButton` / `UnloadPluginButton`；`GUI/Plugins/IGUIPlugin.cs:7` | 框 | UI-33 |

### 2.3 C Mod 列表与搜索（17 项）

| ID | 特性 | 原版证据 | 类型 | 映射任务 |
|---|---|---|---|---|
| F-18 | Mod 列表 15 列（Installed、Auto-installed、Update、Replace、Name、Author、Installed version、Latest version、Max game version、Download、Install、Release date、Install date、Downloads、Description） | `GUI/Controls/ManageMods.resx`: `Installed` / `AutoInstalled` / `UpdateCol` / `ReplaceCol` / `ModName` / `Author` / `InstalledVersion` / `LatestVersion` / `GameCompatibility` / `DownloadSize` / `InstallSize` / `ReleaseDate` / `InstallDate` / `DownloadCount` / `Description`（均 `.HeaderText`） | 页 | UI-02, VM-01 |
| F-19 | 11 种状态过滤器 + 排序（点击列头，含 Update 列自动排序选项） | 同上: `FilterCompatibleButton` / `FilterInstalledButton` / `FilterInstalledUpdateButton` / `FilterReplaceableButton` / `FilterCachedButton` / `FilterUncachedButton` / `FilterNewButton` / `FilterNotInstalledButton` / `FilterIncompatibleButton` / `FilterAllButton`；`AutoSortUpdateCheckBox`（`SettingsDialog.resx`） | 动 | UI-02, VM-01 |
| F-20 | 组合搜索面板（Search / Or 两组，逐字段可选） | `GUI/Controls/EditModSearch.resx`: `FilterCombinedLabel` / `FilterOrLabel` | 框 | UI-14, MOD-01 |
| F-21 | 搜索字段 18 项（Name、Author、Description、Licence、Language、Depends on、Recommends、Suggests、Conflicts with、Supports、Tags、Labels、Compatible、Installed、Cached、Newly compatible、Upgradeable、Replaceable） | `GUI/Controls/EditModSearchDetails.resx` 全部 `FilterBy*Label` + `CompatibleLabel` / `InstalledLabel` / `CachedLabel` / `NewlyCompatibleLabel` / `UpgradeableLabel` / `ReplaceableLabel` | 框 | UI-14, MOD-01 |
| F-22 | 标签（Tags）与标签徽章、点击显隐 | `GUI/Controls/ManageMods.resx`: `FilterTagsToolButton` / `hiddenTagsLabelsLinkList.TagToolTipText`；`GUI/Controls/TagsLabelsLinkList.cs` | 动 | UI-02 |
| F-23 | Label 过滤与编辑入口 | 同上: `FilterLabelsToolButton` / `FilterLabelsEditToolStripMenuItem`；`GUI/Dialogs/EditLabelsDialog.resx` | 动 | UI-02, UI-24b |
| F-24 | Label 属性（名称、背景色、隐藏于其它过滤器、更新时通知/移除、安装时提醒/移除、不升级、忽略缺失文件、导出/删除） | `GUI/Dialogs/EditLabelsDialog.resx`: `NameLabel` / `ColorButton` / `HideFromOtherFiltersCheckBox` / `NotifyOnChangesCheckBox` / `RemoveOnChangesCheckBox` / `AlertOnInstallCheckBox` / `RemoveOnInstallCheckBox` / `HoldVersionCheckBox` / `IgnoreMissingFilesCheckBox` / `ExportButton` | 框 | UI-24b |
| F-25 | 前进/后退导航（选中历史） | `GUI/Controls/ManageMods.resx`: `NavBackwardToolButton.ToolTipText` / `NavForwardToolButton.ToolTipText`；`GUI/NavigationHistory.cs` | 动 | UI-02 |
| F-26 | 详情四页：元数据 / 关系 / 内容 / 版本 | `GUI/Controls/ModInfo/ModInfo.resx`: `MetadataTabPage` / `RelationshipTabPage` / `ContentTabPage` / `VersionsTabPage` | 页 | UI-03 |
| F-27 | 元数据页（Identifier、Replaced by、Max game ver.、Release status、Author、Downloads、Licence、Version） | `GUI/Controls/ModInfo/Metadata.resx` 全部 `*Label` | 页 | UI-03 |
| F-28 | 关系树（Installed/Incompatible/Virtual/Provides/Depends/Recommends/Suggests/Supports/Conflicts 九色图例、反向关系、双击跳转） | `GUI/Controls/ModInfo/Relationships.resx`: `Legend*Label` / `ReverseRelationshipsCheckbox`；`GUI/Controls/ModInfo/Relationships.cs:24`（`DependsGraphTree` 树控件，非画布） | 页 | UI-18 |
| F-29 | 内容页（未缓存时提示、下载、打开 ZIP 预览文件树） | `GUI/Controls/ModInfo/Contents.resx`: `NotCachedLabel` / `ContentsDownloadButton` / `ContentsOpenButton` | 页 | UI-03 |
| F-30 | 版本页（全部版本、兼容/已装/不稳定标记、发布日期） | `GUI/Controls/ModInfo/Versions.resx`: `OverallSummaryLabel` / `ModVersion` / `CompatibleGameVersion` / `ReleaseDate` / `LatestCompatibleLabel` / `PrereleaseLabel` | 页 | UI-03 |
| F-31 | 右键动作（重装、下载到缓存、从缓存清除、Labels 子菜单） | `GUI/Controls/ManageMods.resx`: `reinstallToolStripMenuItem` / `downloadContentsToolStripMenuItem` / `purgeContentsToolStripMenuItem` / `labelsToolStripMenuItem` / `editLabelsToolStripMenuItem` | 动 | UI-02, INT-07 |
| F-32 | 全量更新（Update all）、应用（Apply） | 同上: `UpdateAllToolButton` / `ApplyToolButton` | 动 | UI-02, UI-04 |
| F-33 | 变更集页（Mod / Change / Reasons / Skip? 四列、清空、应用、游戏运行警告） | `GUI/Controls/Changeset.resx`: `ModColumn` / `ChangeTypeColumn` / `ReasonsColumn` / `DeleteColumn` / `CancelChangesButton` / `ConfirmChangesButton` / `CloseTheGameLabel` | 页 | UI-04, VM-02 |
| F-34 | 提供者选择（虚拟 mod 多候选单选） | `GUI/Controls/ChooseProvidedMods.resx`: `ChooseProvidedModsLabel` / `modNameColumnHeader` / `modDescriptionColumnHeader` | 框 | UI-16 |

### 2.4 D 安装、缓存与文件（14 项）

| ID | 特性 | 原版证据 | 类型 | 映射任务 |
|---|---|---|---|---|
| F-35 | 推荐/建议/被支持选择（三组、Check All / Uncheck All / Always uncheck all / Check Recommendations / Check Suggestions） | `GUI/Controls/ChooseRecommendedMods.resx`: `RecommendationsGroup` / `SuggestionsGroup` / `SupportedByGroup` / `CheckAllButton` / `UncheckAllButton` / `AlwaysUncheckAllButton` / `CheckRecommendationsButton` / `CheckSuggestionsButton` | 框 | UI-15, INT-07 |
| F-36 | 推荐审计（遍历已装 mod 的推荐与建议） | `GUI/Main/Main.resx`: `AuditRecommendationsToolStripMenuItem`（ToolTip=Review recommendations and suggestions of all installed mods）；`GUI/Main/MainRecommendations.cs` | 动 | UI-17 |
| F-37 | 安装/卸载/升级执行（进度、取消、重试、状态日志） | `GUI/Controls/Wait.resx`: `CancelCurrentActionButton` / `RetryCurrentActionButton` / `MessageTextBox`；`GUI/Main/Main.resx`: `WaitTabPage`="Status Log" | 页 | UI-08, INT-08 |
| F-38 | 下载失败处置（逐项 Retry?/Skip?、错误文本、复制到剪贴板） | `GUI/Dialogs/DownloadsFailedDialog.resx`: `RetryColumn` / `SkipColumn` / `ErrorColumn` / `CopyErrorToolStripMenuItem` | 框 | UI-35 |
| F-39 | 从 .ckan 安装（多选文件） | `GUI/Main/Main.resx`: `InstallFromckanToolStripMenuItem`（ToolTip 说明）；`GUI/Main/MainInstall.cs` | 动 | UI-20 |
| F-40 | 导入已下载 mods（ZIP → 缓存） | 同上: `ImportDownloadsToolStripMenuItem` | 动 | UI-20 |
| F-41 | 去重（跨实例扫描并共享可共享文件） | 同上: `DeduplicateToolStripMenuItem`；`GUI/Main/MainDeduplicate.cs` | 动 | UI-21 |
| F-42 | 未管理文件（树、刷新、全展开/全折叠/重置、在文件夹中显示、删除） | `GUI/Controls/UnmanagedFiles.resx`: `RefreshButton` / `ExpandAllButton` / `CollapseAllButton` / `ResetCollapseButton` / `ShowInFolderButton` / `DeleteButton` | 页 | UI-10 |
| F-43 | 残留目录清理（目录树 + 内容预览、打开目录、删除勾选、全部保留） | `GUI/Controls/DeleteDirectories.resx`: `DirectoryColumn` / `FileColumn` / `OpenDirectoryButton` / `DeleteButton` / `KeepAllButton` / `ExplanationLabel`；`GUI/Main/MainInstall.cs`（`possibleConfigOnlyDirs`） | 页 | UI-22 |
| F-44 | 安装历史（时间戳列表、已装/未装分组、从历史重装） | `GUI/Controls/InstallationHistory.resx`: `TimestampColumn` / `InstalledGroup` / `NotInstalledGroup` / `InstallButton` / `SelectInstallMessage`；`GUI/Main/MainHistory.cs` | 页 | UI-23 |
| F-45 | 导出：已装列表（不可回导）／modpack（可回导） | `GUI/Main/Main.resx`: `ExportModListToolStripMenuItem` / `ExportModPackToolStripMenuItem`；`GUI/Main/MainExport.cs` | 动 | UI-24 |
| F-46 | modpack 编辑器（标识/名称/摘要/作者/版本/游戏版本/许可、Save mod versions、Include optional relationships、Depends/Recommends/Suggests/Ignored 分组与单 mod 单选） | `GUI/Controls/EditModpack.resx`（全量）；`GUI/Model/ExportOption.cs` | 页 | UI-24 |
| F-47 | 下载统计（饼图 + 表格） | `GUI/Controls/DownloadStatistics/DownloadStatisticsPieChart.cs:9-11`（`OxyPlot` + `PieSeries`）；`GUI/Main/Main.resx`: `DownloadStatisticsToolStripMenuItem` | 页 | UI-25 |
| F-48 | 游戏时长（实例名 / 小时数，F2 就地编辑） | `GUI/Controls/PlayTime.resx`: `NameColumn` / `TimeColumn` / `EditHelpLabel` | 页 | UI-10 |

### 2.5 E 应用外壳、设置与平台（7 项）

| ID | 特性 | 原版证据 | 类型 | 映射任务 |
|---|---|---|---|---|
| F-49 | 设置总表（仓库、令牌、缓存、自动更新、行为、更多 6 组 + 全部子项） | `GUI/Dialogs/SettingsDialog.resx`（47 个 `.Text` 键，见 §2.2/§2.5 各处） | 框 | UI-07 |
| F-50 | 下载缓存管理（文件数/容量、路径编辑、上限 MiB、Purge→Purge to limit/Purge all、Reset、打开） | 同上: `CacheGroupBox` / `CacheSummary` / `CacheLimitPreLabel` / `CachePathEditButton` / `ClearCacheButton` / `PurgeToLimitMenuItem` / `PurgeAllMenuItem` / `ResetCacheButton` / `OpenCacheButton` | 框 | UI-19 |
| F-51 | 托盘图标（启用/最小化到托盘；右键 Refresh / Pause / Open CKAN / Launch Game / Open Game Directory / CKAN Settings / Quit） | `GUI/Main/Main.resx`: `EnableTrayIconCheckBox` / `MinimizeToTrayCheckBox` / `refreshToolStripMenuItem` / `pauseToolStripMenuItem` / `openCKANToolStripMenuItem` / `openGameToolStripMenuItem` / `quitToolStripMenuItem`；`GUI/Main/MainTrayIcon.cs` | 隐 | UI-26 |
| F-52 | 自动更新（启动时检查、dev builds、检查/安装、新版本对话框、首次询问） | `GUI/Dialogs/SettingsDialog.resx`: `AutoUpdateGroupBox` / `CheckUpdateOnLaunchCheckbox` / `DevBuildsCheckbox` / `CheckForUpdatesButton` / `InstallUpdateButton`；`GUI/Dialogs/NewUpdateDialog.resx`；`GUI/Dialogs/AskUserForAutoUpdatesDialog.resx` | 框 | UI-27 |
| F-53 | 语言与本地化（语言选择需重启；多语 resx） | 同上: `LanguageSelectionLabel`；`GUI/Localization/<lang>/*.resx`（de-DE / es-ES / en-US / fr-FR / it-IT / ja-JP / ko-KR / nl-NL / pl-PL / pt-BR / ru-RU / zh-CN / zh-TW 等） | 框 | UI-13 |
| F-54 | 显示格式（隐藏 epoch、隐藏 "v"、自动排序） | 同上: `HideEpochsCheckbox` / `HideVCheckbox` / `AutoSortUpdateCheckBox` | 框 | UI-07 |
| F-55 | 帮助与关于（游戏 Discord、模组求助、CKAN 用户指南、CKAN Discord、报告客户端/元数据问题、关于框、单实例互斥、`ckan://` 协议、错误对话框、崩溃兜底） | `GUI/Main/Main.resx`: `gameDiscordToolStripMenuItem` / `modSupportToolStripMenuItem` / `userGuideToolStripMenuItem` / `discordToolStripMenuItem` / `reportClientIssueToolStripMenuItem` / `reportMetadataIssueToolStripMenuItem` / `aboutToolStripMenuItem`；`GUI/Dialogs/AboutDialog.resx`；`GUI/URLHandlers.cs`；`GUI/Dialogs/ErrorDialog.resx` | 动 | UI-36, ARC-04, ARC-05, UI-09 |

> 计数：A 9 + B 8 + C 17 + D 14 + E 7 = **55 条**。原版主窗口 **11 个页签**（`Main.resx` 的 `*TabPage.Text`）与 **18 个对话框**（`GUI/Dialogs/`）全部在上表出现。

## 3. 允许的实现差异（不算减配）

| 维度 | 原版 | 新 UI | 理由 |
|---|---|---|---|
| 控件形态 | WinForms DataGridView / TreeView / ToolStrip | Avalonia DataGrid / TreeView / 自绘按钮 | 框架不同 |
| 主窗口结构 | 单窗口 + 11 个动态加/删页签 | NavRail 导航 + ContentHost，页签态用页面 + 面包屑表达 | 同一能力，导航方式更清晰；**取消页签切换按钮**但**页面入口一个不少** |
| 材质 | 无 | Mica/Acrylic 三档降级 | 11 篇 §3 |
| 关系视图 | TreeView 树 | 树或缩进列表（不引入画布） | 保持信息等价，降低渲染风险 |
| 图表库 | `OxyPlot.WindowsForms` | `OxyPlot.Avalonia` 或自绘饼图（SP-14 验证） | 框架不同 |
| 插件 API | `abstract class IGUIPlugin`（`GUI/Plugins/IGUIPlugin.cs:7`）+ 反射加载 `Assembly.UnsafeLoadFrom`，类型名约定 `<dll>.<dll>` | 同名成员的新接口 `ICKANUIPlugin`（`GetName/GetVersion/Initialize/Deinitialize`）+ 同样的反射加载与目录约定 | 抽象类可原样保留，**插件作者需重编译**；这是平台差异，必须写进发布说明（C3） |

## 4. 缺口分析：v1.1 WBS 未覆盖的条目

下表中"原任务"为 `null` 的即为 v1.1 遗漏项：

| F-ID | 原任务 | 说明 |
|---|---|---|
| F-01/02/03/04 | INT-04 仅含服务，**无 UI** | 实例管理对话框组缺失 |
| F-06 | 无 | 游戏命令行编辑器 |
| F-11 | 无 | 认证令牌 |
| F-13/14 | 无 | 稳定性容忍度 UI、兼容游戏版本对话框 |
| F-15 | 无 | 首选下载主机 |
| F-16 | 无 | 安装过滤器 |
| F-17 | 无 | 插件系统（含 API 重定义） |
| F-20/21 | MOD-01 仅含模型 | 组合搜索 UI 缺失（**已补齐**：`src/CkanUI.ViewModels/Pages/Mods/ModsSearchPanelViewModel.cs` + `src/CkanUI.App/Views/Mods/ModsSearchPanelView.axaml`） |
| F-24 | 无 | Label 属性编辑 |
| F-28 | 无 | 关系树 |
| F-34 | 无 | 提供者选择 |
| F-35 | INT-07 仅含 `FindRecommendations` | 推荐选择 UI 缺失 |
| F-36 | 无 | 推荐审计 |
| F-38 | INT-08 仅含"部分失败汇报" | 下载失败逐项对话框缺失 |
| F-39/40/41 | 无 | 从 .ckan 安装、导入已下载、去重 |
| F-43 | 无 | 残留目录清理 |
| F-44 | UI-10 含页面，**无重装动作** | 从历史重装 |
| F-46 | UI-11 仅含导入预览 | modpack **编辑器**缺失 |
| F-47 | UI-10 含页面，图表库待定 | OxyPlot 替换（SP-14） |
| F-50 | 无 | 缓存管理 |
| F-51 | 无 | 托盘 |
| F-52 | 无 | 自动更新（且 Core `AutoUpdate` 不可直接复用，见 02 篇 §5 风险 6） |

## 5. 新增任务与工时

| ID | 任务 | 工时 | 依赖 | 验收（对应 F） |
|---|---|---|---|---|
| UI-14 | 组合搜索面板（Search/Or + 18 字段） | 8 | UI-02, MOD-01 | F-20/21：18 个字段各一条用例 |
| UI-15 | 推荐/建议/支持选择对话框 + 批量策略持久化 | 10 | INT-07 | F-35：三组勾选与"总是取消全选"落盘 |
| UI-16 | 提供者选择对话框 | 4 | INT-07 | F-34：虚拟 mod 二选一后变更集正确 |
| UI-17 | 推荐审计（批量报告 + 一键采纳） | 6 | INT-07 | F-36：已装 20 mod 审计不卡 UI |
| UI-18 | 关系树（九类着色、反向切换、双击跳转） | 10 | UI-03 | F-28：每类关系至少一例可见 |
| UI-19 | 缓存管理面板（统计、上限、三种清理、打开、重置） | 8 | INT-02 | F-50：上限清理后容量 ≤ 上限 |
| UI-20 | 从 .ckan 安装 / 导入已下载 mods / modpack 导入 | 12 | INT-10 | F-39/40：多选文件与 ZIP 批量入库 |
| UI-21 | 去重（扫描、预览、共享、进度） | 10 | INT-09 | F-41：两实例重复文件变硬链接 |
| UI-22 | 残留目录清理（树 + 内容预览 + 勾选删除） | 8 | INT-08 | F-43：删除后目录为空 |
| UI-23 | 安装历史 + 从历史重装 | 10 | INT-08 | F-44：回滚到旧时间点的变更集正确 |
| UI-24 | modpack 编辑器（三组关系 + 单 mod 单选 + 两个选项） | 12 | INT-10 | F-46：导出后能被 CLI `ckan import` 接受 |
| UI-24b | Label 编辑（属性 10 项、颜色选择、导出） | 8 | INT-05 | F-23/24：`EditLabelsDialog` 全项可写 |
| UI-25 | 下载统计（饼图 + 表 + 排序） | 8 | INT-06 | F-47：3,000 mod 统计 ≤ 1s |
| UI-26 | 托盘图标 + 最小化到托盘 + 7 项右键菜单 | 10 | UI-01 | F-51：关窗最小化后能恢复并退出 |
| UI-27 | 自动更新（自建检查器 + 下载 + 替换 + 新版本对话框） | 20 | ARC-01, PKG-01 | F-52：假源升级一次完整走通 |
| UI-28 | 游戏命令行编辑器 + Play 命令选择 | 4 | INT-04 | F-05/06：增删命令后启动参数正确 |
| UI-29 | 兼容游戏版本对话框（含通配与警示） | 6 | INT-02 | F-14：加 `1.12` 后 `1.12.3` 的 mod 变兼容 |
| UI-30 | 首选下载主机（拖动排序 + 热度列表） | 6 | INT-06 | F-15：优先级变化后 URL 选择顺序正确 |
| UI-31 | 安装过滤器（全局/实例两组 + 规则编辑） | 8 | INT-02 | F-16：过滤规则生效且不影响已装文件 |
| UI-32 | 实例对话框组（新增/克隆/重命名/遗忘/Steam 导入/右键） | 8 | INT-04 | F-01..04：克隆实例可启动且注册表独立 |
| UI-33 | 插件系统（`ICKANUIPlugin` + 加载/停用/重载/卸载 + 示例插件） | 24 | ARC-01 | F-17：示例插件注入一个页签后能停用 |
| UI-34 | 认证令牌管理（增删 + 脱敏） | 4 | INT-02 | F-11：令牌不下发明文到日志 |
| UI-35 | 下载失败对话框（逐项重试/跳过 + 复制错误） | 4 | INT-08 | F-38：断网重试成功后继续安装 |
| UI-36 | 帮助/关于/报告问题/用户指南链接 | 3 | UI-01 | F-55：6 个链接可打开 |
| VM-06 | 上述对话框的 VM 扩展（实例/仓库/过滤/导出/历史/缓存/插件/设置子页） | 16 | VM-03, VM-04 | 各 VM headless 单测覆盖 |
| TST-05 | 全特性验收矩阵（F-01..F-55 逐条：自动化或手工脚本） | 12 | TST-01..04 | 55 条全部有结论，红色项为 0 |
| SP-14 | 图表库选型验证（`OxyPlot.Avalonia` vs 自绘饼图，含 Avalonia 12 兼容性） | 3 | ENV-02 | 二者之一能画出饼图并随主题变色 |

**新增合计：239h**（UI-14..UI-36 共 24 项 = 211h + VM-06 16h + TST-05 12h；SP-14 计入 spike 包，不重复计）

## 6. 汇总

| 项 | v1.1 | v1.2（全特性） |
|---|---|---|
| WBS 工时 | 615h | **854h** |
| 人日（8h） | 77 | **107** |
| 排期（40h/周 + 联调缓冲） | 15 周 | **23 周** |
| 功能条目 | 未逐条编号 | **55 条（§2）** |

> **口径说明**：本表是**主计划（mod 管理）**的工时，不含 [13 篇](13-savegame-sfs.md) 的 E1 存档工作台（250h / 7 周）与 [14 篇](14-log-and-ai-debug.md) 的 E2 日志与 AI 排障（186h / 5 周）、[15 篇](15-mod-config-adapters.md) 的 E3 Mod 配置（174h / 5 周）、[16 篇](16-i18n.md) 的 E4 国际化（156h / 4 周）与 [17 篇](17-mod-metadata-i18n.md) 的 E5 元数据本地化（134h / 4 周）与 [18 篇](18-launch-resource-release.md) 的 E6 启动后资源释放（110h / 3 周）。全量 = **2196h / 60 周**（含 E7 安装域 126h 净增 + E8 外观域 206h），见 [06 篇](06-wbs-milestones.md) §1.10–§1.15 与 [00 篇](00-index.md) §5。

里程碑影响（以 [06 篇](06-wbs-milestones.md) §2 为准）：M3 扩到 W7–W9（缓存/实例/仓库/过滤器/兼容版本/令牌族）、M4 扩到 W10–W14（未托管/历史/统计/PlayTime/托盘/导入导出/去重）、新增 **M5' 全特性收口 W18–W20（3 周）**（UI-27 自建更新 + UI-33 插件 + TST-05 矩阵与差距修复），M6 W21–W23 发布；合计 23 周。

## 7. 需要用户确认的三点（平台差异，非功能裁剪）

| # | 事项 | 现状 | 建议 |
|---|---|---|---|
| Q1 | 自动更新 | Core `AutoUpdate.CanUpdate` 基于"运行中的 exe 就是 ckan.exe"，新 exe 不可直接复用（02 篇 §5 风险 6） | 自建检查器（读 GitHub Releases）+ 下载 zip + 启动替换器；20h 已计入 UI-27 |
| Q2 | 插件作者需重编译 | 原 `IGUIPlugin` 是抽象类且插件直接操作 WinForms 控件 | 保留同名成员接口，插件作者迁移成本约 10 行；发布说明必须写明 |
| Q3 | 单实例互斥键 | 原版通过注册表/命名互斥与 `ckan.exe` 共存 | 新 exe 用**独立**互斥键，否则会与老 GUI 抢锁（09 篇 §3 铁律） |

## 8. 修订记录

| 日期 | 版本 | 变更 |
|---|---|---|
| 2026-10-26 | v1.0 | 依用户指令"完全特性支持"新建；扫 `GUI/` 135 个 `.cs` 与 36 个基础 `.resx` 提取 55 条特性；补 239h 缺口任务；WBS 615h→854h、15 周→23 周 |
| 2026-10-26 | v1.1 | 工时自检校正：§5 表逐行相加为 **211h**（原写 203h），故本节合计 239h、WBS 合计 **854h**（原写 846h）；F 表本身未变 |
