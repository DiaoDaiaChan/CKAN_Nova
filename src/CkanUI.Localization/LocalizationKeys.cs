namespace CkanUI.Localization;

/// <summary>
/// 新版 UI 文案键常量（16 篇 §5.1 键规范：<c>Ui.&lt;Area&gt;_&lt;Element&gt;[_&lt;Qualifier&gt;]</c>，
/// 区域词表取规格原文的 11 个：NavRail / ManageMods / ModInfo / Changeset / Settings / Instances /
/// Repos / Log / Saves / ModConfig / Tray / Common）。
/// 与语言包的对应关系由 ShippedPacksTests（常量 ⇒ 两套包都有译文）与 LocalizationKeyDriftTests
/// （包里有键但常量表没有 ⇒ 死键）双向校验，改这里必须同步 i18n/packs/{zh-CN,en-US}.json。
/// </summary>
public static class LocalizationKeys
{
    /// <summary>导航栏：模组。</summary>
    public const string NavRailManageMods = "Ui_NavRail_ManageMods";

    /// <summary>导航栏：变更集。</summary>
    public const string NavRailChanges = "Ui_NavRail_Changes";

    /// <summary>导航：内容预览页（12 篇 F-29）。</summary>
    public const string NavRailContent = "Ui_NavRail_Content";

    /// <summary>页面标题：变更集。</summary>
    public const string ChangesetTitle = "Ui_Changeset_Title";

    /// <summary>导航栏：实例。</summary>
    public const string NavRailRepos = "Ui_NavRail_Repos";

    /// <summary>导航栏：导入导出页。</summary>
    public const string NavRailModPacks = "Ui_NavRail_ModPacks";

    /// <summary>导航栏：modpack 编辑器页（F-46）。</summary>
    public const string NavRailModPackEditor = "Ui_NavRail_ModPackEditor";

    public const string NavRailInstances = "Ui_NavRail_Instances";

    /// <summary>导航栏：未管理文件页（F-42）。</summary>
    public const string NavRailUnmanaged = "Ui_NavRail_Unmanaged";

    /// <summary>导航栏：游戏时长（F-48）。</summary>
    public const string NavRailPlayTime = "Ui_NavRail_PlayTime";

    /// <summary>导航栏：存档工作台（M7/E1）。</summary>
    public const string NavRailSaves = "Ui_NavRail_Saves";

    /// <summary>导航栏：下载缓存面板（F-50）。</summary>
    public const string NavRailCache = "Ui_NavRail_Cache";

    /// <summary>导航栏：跨实例去重页（F-41）。</summary>
    /// <summary>导航：下载统计页（12 篇 F-47 / 06 篇 UI-25）。</summary>
    public const string NavRailDownloadStats = "Ui_NavRail_DownloadStats";

    public const string NavRailDedup = "Ui_NavRail_Dedup";

    /// <summary>导航栏：安装历史（F-44）。</summary>
    public const string NavRailInstallHistory = "Ui_NavRail_InstallHistory";

    /// <summary>导航栏：插件（F-17）。</summary>
    public const string NavRailPlugins = "Ui_NavRail_Plugins";

    /// <summary>导航栏：模组翻译（词条检索与人工覆盖；属于「模组」入口的第 6 个页签）。</summary>
    public const string NavRailModText = "Ui_NavRail_ModText";

    /// <summary>导航栏：日志与诊断（M8 / 14 篇 §4.1 / UI-LOG-01）。</summary>
    public const string NavRailLogs = "Ui_NavRail_Logs";

    /// <summary>导航栏：设置。</summary>
    public const string NavRailSettings = "Ui_NavRail_Settings";

    /// <summary>导航栏：Mod 配置适配层（M9）。</summary>
    public const string NavRailModConfig = "Ui_NavRail_ModConfig";

    /// <summary>导航入口：数据（下载缓存 / 下载统计 / 跨实例去重同处一个入口）。</summary>
    public const string NavRailData = "Ui_NavRail_Data";

    /// <summary>导航入口：工具（目前只有插件一页，页签条不会出现）。</summary>
    public const string NavRailTools = "Ui_NavRail_Tools";

    /// <summary>导航栏：收起为图标栏（提示文案与无障碍名称）。</summary>
    public const string NavRailCollapse = "Ui_NavRail_Collapse";

    /// <summary>导航栏：展开为完整侧栏（提示文案与无障碍名称）。</summary>
    public const string NavRailExpand = "Ui_NavRail_Expand";

    /// <summary>导航分组标题：核心业务（模组、内容、仓库、整合包、实例）。</summary>
    public const string NavGroupCore = "Ui_NavGroup_Core";

    /// <summary>导航分组标题：工具与数据（时长、存档、缓存、统计、去重、历史、配置）。</summary>
    public const string NavGroupTools = "Ui_NavGroup_Tools";

    /// <summary>导航分组标题：扩展（插件页与插件贡献的页面）。</summary>
    public const string NavGroupExtensions = "Ui_NavGroup_Extensions";

    /// <summary>外壳底部提示条：当前页有未保存的改动（IUnsavedChangesAware 驱动）。</summary>
    public const string CommonUnsavedHint = "Ui_Common_UnsavedHint";

    /// <summary>未保存提示条上的「放弃修改」按钮（页面不提供 DiscardChangesCommand 时隐藏）。</summary>
    public const string CommonUnsavedDiscard = "Ui_Common_Unsaved_Discard";

    /// <summary>未保存提示条上的「保存更改」按钮（页面不提供 SaveChangesCommand 时隐藏）。</summary>
    public const string CommonUnsavedSave = "Ui_Common_Unsaved_Save";

    /// <summary>页面标题：模组管理。</summary>
    public const string ManageModsTitle = "Ui_ManageMods_Title";

    /// <summary>页面标题：实例。</summary>
    public const string InstancesTitle = "Ui_Instances_Title";

    /// <summary>页面标题：设置。</summary>
    public const string SettingsTitle = "Ui_Settings_Title";

    /// <summary>设置页：语言分组标题。</summary>
    public const string SettingsLanguage = "Ui_Settings_Language";

    /// <summary>设置页：语言切换即时生效说明。</summary>
    public const string SettingsLanguageHint = "Ui_Settings_Language_Hint";

    /// <summary>设置页：选西语时"旧版 CKAN 不支持"提示（16 篇 §6.3 / Q4）。</summary>
    public const string SettingsLanguageLegacyWarning = "Ui_Settings_Language_LegacyWarning";

    // ---- 命令面板与快捷键目录（UI-27） ----

    /// <summary>命令面板 / 快捷键目录（UI-27）：Ui_Cmd_Palette_Placeholder。</summary>
    public const string CmdPalettePlaceholder = "Ui_Cmd_Palette_Placeholder";

    /// <summary>命令面板 / 快捷键目录（UI-27）：Ui_Cmd_Palette_Empty。</summary>
    public const string CmdPaletteEmpty = "Ui_Cmd_Palette_Empty";

    /// <summary>命令面板 / 快捷键目录（UI-27）：Ui_Cmd_Palette_FooterHint。</summary>
    public const string CmdPaletteFooterHint = "Ui_Cmd_Palette_FooterHint";

    /// <summary>命令面板 / 快捷键目录（UI-27）：Ui_Cmd_Palette_ResultCountFormat。</summary>
    public const string CmdPaletteResultCountFormat = "Ui_Cmd_Palette_ResultCountFormat";

    /// <summary>命令面板 / 快捷键目录（UI-27）：Ui_Cmd_Palette_TruncatedSuffix。</summary>
    public const string CmdPaletteTruncatedSuffix = "Ui_Cmd_Palette_TruncatedSuffix";

    /// <summary>命令面板 / 快捷键目录（UI-27）：Ui_Cmd_Shortcuts_Title。</summary>
    public const string CmdShortcutsTitle = "Ui_Cmd_Shortcuts_Title";

    /// <summary>命令面板 / 快捷键目录（UI-27）：Ui_Cmd_Shortcuts_FilterWatermark。</summary>
    public const string CmdShortcutsFilterWatermark = "Ui_Cmd_Shortcuts_FilterWatermark";

    /// <summary>命令面板 / 快捷键目录（UI-27）：Ui_Cmd_Shortcuts_Empty。</summary>
    public const string CmdShortcutsEmpty = "Ui_Cmd_Shortcuts_Empty";

    /// <summary>命令面板 / 快捷键目录（UI-27）：Ui_Cmd_Shortcuts_Copy。</summary>
    public const string CmdShortcutsCopy = "Ui_Cmd_Shortcuts_Copy";

    /// <summary>命令面板 / 快捷键目录（UI-27）：Ui_Cmd_Shortcuts_Close。</summary>
    public const string CmdShortcutsClose = "Ui_Cmd_Shortcuts_Close";

    /// <summary>命令面板 / 快捷键目录（UI-27）：Ui_Cmd_Shortcuts_TotalCountFormat。</summary>
    public const string CmdShortcutsTotalCountFormat = "Ui_Cmd_Shortcuts_TotalCountFormat";

    /// <summary>命令面板 / 快捷键目录（UI-27）：Ui_Cmd_Category_Global。</summary>
    public const string CmdCategoryGlobal = "Ui_Cmd_Category_Global";

    /// <summary>命令面板 / 快捷键目录（UI-27）：Ui_Cmd_Category_Palette。</summary>
    public const string CmdCategoryPalette = "Ui_Cmd_Category_Palette";

    /// <summary>命令面板 / 快捷键目录（UI-27）：Ui_Cmd_Category_ShortcutsDialog。</summary>
    public const string CmdCategoryShortcutsDialog = "Ui_Cmd_Category_ShortcutsDialog";

    /// <summary>命令面板 / 快捷键目录（UI-27）：Ui_Cmd_Shortcut_MoveUp。</summary>
    public const string CmdShortcutMoveUp = "Ui_Cmd_Shortcut_MoveUp";

    /// <summary>命令面板 / 快捷键目录（UI-27）：Ui_Cmd_Shortcut_MoveDown。</summary>
    public const string CmdShortcutMoveDown = "Ui_Cmd_Shortcut_MoveDown";

    /// <summary>命令面板 / 快捷键目录（UI-27）：Ui_Cmd_Shortcut_Execute。</summary>
    public const string CmdShortcutExecute = "Ui_Cmd_Shortcut_Execute";

    /// <summary>命令面板 / 快捷键目录（UI-27）：Ui_Cmd_Shortcut_Dismiss。</summary>
    public const string CmdShortcutDismiss = "Ui_Cmd_Shortcut_Dismiss";

    /// <summary>命令面板 / 快捷键目录（UI-27）：Ui_Cmd_Shortcut_CloseDialog。</summary>
    public const string CmdShortcutCloseDialog = "Ui_Cmd_Shortcut_CloseDialog";

    /// <summary>命令面板 / 快捷键目录（UI-27）：Ui_Cmd_Category_Navigate。</summary>
    public const string CmdCategoryNavigate = "Ui_Cmd_Category_Navigate";

    /// <summary>命令面板 / 快捷键目录（UI-27）：Ui_Cmd_Category_Interface。</summary>
    public const string CmdCategoryInterface = "Ui_Cmd_Category_Interface";

    /// <summary>命令面板 / 快捷键目录（UI-27）：Ui_Cmd_Action_ShowPalette。</summary>
    public const string CmdActionShowPalette = "Ui_Cmd_Action_ShowPalette";

    /// <summary>命令面板 / 快捷键目录（UI-27）：Ui_Cmd_Action_ShowShortcuts。</summary>
    public const string CmdActionShowShortcuts = "Ui_Cmd_Action_ShowShortcuts";

    /// <summary>命令面板 / 快捷键目录（UI-27）：Ui_Cmd_Action_GoBack。</summary>
    public const string CmdActionGoBack = "Ui_Cmd_Action_GoBack";

    /// <summary>命令面板 / 快捷键目录（UI-27）：Ui_Cmd_Action_GoForward。</summary>
    public const string CmdActionGoForward = "Ui_Cmd_Action_GoForward";

    /// <summary>命令面板 / 快捷键目录（UI-27）：Ui_Cmd_Action_OpenInstanceFolder。</summary>
    public const string CmdActionOpenInstanceFolder = "Ui_Cmd_Action_OpenInstanceFolder";

    /// <summary>命令面板 / 快捷键目录（UI-27）：Ui_Cmd_Action_OpenLogFolder。</summary>
    public const string CmdActionOpenLogFolder = "Ui_Cmd_Action_OpenLogFolder";

    /// <summary>命令面板 / 快捷键目录（UI-27）：Ui_Cmd_Action_ExportDiagnostics。</summary>
    public const string CmdActionExportDiagnostics = "Ui_Cmd_Action_ExportDiagnostics";

    /// <summary>命令面板 / 快捷键目录（UI-27）：Ui_Cmd_Action_CheckUpdates。</summary>
    public const string CmdActionCheckUpdates = "Ui_Cmd_Action_CheckUpdates";

    /// <summary>命令面板 / 快捷键目录（UI-27）：Ui_Cmd_Action_ToggleTheme。</summary>
    public const string CmdActionToggleTheme = "Ui_Cmd_Action_ToggleTheme";

    /// <summary>命令面板 / 快捷键目录（UI-27）：Ui_Cmd_Action_ToggleDensity。</summary>
    public const string CmdActionToggleDensity = "Ui_Cmd_Action_ToggleDensity";

    /// <summary>通用：页面占位说明。</summary>
    public const string CommonPagePending = "Ui_Common_Page_Pending";

    /// <summary>通用：页面未实现的里程碑说明。</summary>
    public const string CommonPageImplNote = "Ui_Common_Page_ImplNote";

    /// <summary>通用：后退按钮提示（F-25）。</summary>
    public const string CommonNavBack = "Ui_Common_Nav_Back";

    /// <summary>通用：前进按钮提示。</summary>
    public const string CommonNavForward = "Ui_Common_Nav_Forward";

    /// <summary>通用：状态栏就绪。</summary>
    public const string CommonStatusReady = "Ui_Common_Status_Ready";

    /// <summary>通用：启动未完成（占位符 {0} = 原因）。</summary>
    public const string CommonStatusStartupIncomplete = "Ui_Common_Status_StartupIncomplete";

    /// <summary>通用：启动失败。</summary>
    public const string CommonStatusStartupFailed = "Ui_Common_Status_StartupFailed";

    /// <summary>通用：语言已切换的系统消息（占位符 {0} = 语言显示名）。</summary>
    public const string CommonLanguageSwitched = "Ui_Common_LanguageSwitched";

    /// <summary>通用：未处理错误窗口标题。</summary>
    public const string CommonErrorUnhandledTitle = "Ui_Common_Error_Unhandled_Title";

    /// <summary>通用：未处理错误窗口正文。</summary>
    public const string CommonErrorUnhandledBody = "Ui_Common_Error_Unhandled_Body";

    /// <summary>通用：未知错误（占位符 {0} 的兜底文本）。</summary>
    public const string CommonErrorUnknown = "Ui_Common_Error_Unknown";

    // ---- 页面 VM 运行期文案（缺口 ⑧）：由 PageViewModel.Text(fallback, key) 取译文 ----
    // 约定：页面标题优先复用导航栏同名键（Ui_NavRail_*）；只有页眉文案与导航不同才另建键。

    // 模组管理页（ModsPageViewModel）

    /// <summary>模组管理页（ModsPageViewModel）：、</summary>
    public const string CommonListSeparator = "Ui_Common_ListSeparator";

    /// <summary>模组管理页（ModsPageViewModel）：显示 {0} / {1} 个模组</summary>
    public const string ManageModsCountSummary = "Ui_ManageMods_CountSummary";

    /// <summary>模组管理页（ModsPageViewModel）：已选 {0} · 变更 +{1}/-{2}</summary>
    public const string ManageModsSelectionSummary = "Ui_ManageMods_SelectionSummary";

    /// <summary>模组管理页（ModsPageViewModel）：无需下载</summary>
    public const string ManageModsDownloadNone = "Ui_ManageMods_DownloadNone";

    /// <summary>模组管理页（ModsPageViewModel）：需下载 {0}</summary>
    public const string ManageModsDownloadNeeded = "Ui_ManageMods_DownloadNeeded";

    /// <summary>模组管理页（ModsPageViewModel）：搜索：{0}</summary>
    public const string ManageModsChipSearch = "Ui_ManageMods_ChipSearch";

    /// <summary>模组管理页（ModsPageViewModel）：安装状态：{0}</summary>
    public const string ManageModsChipInstallState = "Ui_ManageMods_ChipInstallState";

    /// <summary>模组管理页（ModsPageViewModel）：兼容性：{0}</summary>
    public const string ManageModsChipCompatibility = "Ui_ManageMods_ChipCompatibility";

    /// <summary>模组管理页（ModsPageViewModel）：仅兼容</summary>
    public const string ManageModsFilterCompatibleOnly = "Ui_ManageMods_FilterCompatibleOnly";

    /// <summary>模组管理页（ModsPageViewModel）：仅不兼容</summary>
    public const string ManageModsFilterIncompatibleOnly = "Ui_ManageMods_FilterIncompatibleOnly";

    /// <summary>模组管理页（ModsPageViewModel）：无法解析：{0}</summary>
    public const string ManageModsChipUnparsed = "Ui_ManageMods_ChipUnparsed";

    /// <summary>模组管理页（ModsPageViewModel）：标签组：{0}</summary>
    public const string ManageModsChipLabels = "Ui_ManageMods_ChipLabels";

    /// <summary>模组管理页（ModsPageViewModel）：未按标签组过滤</summary>
    public const string ManageModsChipLabelsInactive = "Ui_ManageMods_ChipLabelsInactive";

    /// <summary>模组管理页（ModsPageViewModel）：无标签组</summary>
    public const string ManageModsDetailLabelsNone = "Ui_ManageMods_DetailLabelsNone";

    /// <summary>模组管理页（ModsPageViewModel）：收藏</summary>
    public const string ManageModsLabelFavourite = "Ui_ManageMods_LabelFavourite";

    /// <summary>模组管理页（ModsPageViewModel）：隐藏</summary>
    public const string ManageModsLabelHidden = "Ui_ManageMods_LabelHidden";

    /// <summary>模组管理页（ModsPageViewModel）：锁定版本</summary>
    public const string ManageModsLabelHeld = "Ui_ManageMods_LabelHeld";

    /// <summary>模组管理页（ModsPageViewModel）：设置{0}失败。</summary>
    public const string ManageModsLabelSetFailed = "Ui_ManageMods_LabelSetFailed";

    /// <summary>模组管理页（ModsPageViewModel）：已把 {0} 加入{1}。</summary>
    public const string ManageModsLabelAdded = "Ui_ManageMods_LabelAdded";

    /// <summary>模组管理页（ModsPageViewModel）：已把 {0} 移出{1}。</summary>
    public const string ManageModsLabelRemoved = "Ui_ManageMods_LabelRemoved";

    /// <summary>模组管理页（ModsPageViewModel）：设置{0}失败，请查看错误栏。</summary>
    public const string ManageModsLabelSetFailedSeeErrors = "Ui_ManageMods_LabelSetFailedSeeErrors";

    /// <summary>模组管理页（ModsPageViewModel）：未找到 KSP 实例</summary>
    public const string ManageModsEmptyNoInstance = "Ui_ManageMods_EmptyNoInstance";

    /// <summary>模组管理页（ModsPageViewModel）：尚未加载模组索引</summary>
    public const string ManageModsEmptyNoIndex = "Ui_ManageMods_EmptyNoIndex";

    /// <summary>模组管理页（ModsPageViewModel）：没有匹配的模组</summary>
    public const string ManageModsEmptyNoMatch = "Ui_ManageMods_EmptyNoMatch";

    /// <summary>模组管理页（ModsPageViewModel）：请先在「实例」页添加或选择一个 KSP 安装目录。</summary>
    public const string ManageModsEmptyNoInstanceHint = "Ui_ManageMods_EmptyNoInstanceHint";

    /// <summary>模组管理页（ModsPageViewModel）：仓库索引为空，点「刷新仓库」重试。</summary>
    public const string ManageModsEmptyNoIndexHint = "Ui_ManageMods_EmptyNoIndexHint";

    /// <summary>模组管理页（ModsPageViewModel）：试着放宽过滤条件，或按 Esc 清空搜索。</summary>
    public const string ManageModsEmptyNoMatchHint = "Ui_ManageMods_EmptyNoMatchHint";

    /// <summary>模组管理页（ModsPageViewModel）：未选择模组</summary>
    public const string ManageModsDetailNoSelection = "Ui_ManageMods_DetailNoSelection";

    /// <summary>模组管理页（ModsPageViewModel）：无标签</summary>
    public const string ManageModsDetailNoTags = "Ui_ManageMods_DetailNoTags";

    /// <summary>模组管理页（ModsPageViewModel）：更新模组列表失败，请查看错误栏。</summary>
    public const string ManageModsStatusListFailed = "Ui_ManageMods_StatusListFailed";

    /// <summary>模组管理页（ModsPageViewModel）：更新已安装模组失败，请查看错误栏。</summary>
    public const string ManageModsStatusInstalledFailed = "Ui_ManageMods_StatusInstalledFailed";

    /// <summary>模组管理页（ModsPageViewModel）：刷新标签组失败，请查看错误栏。</summary>
    public const string ManageModsStatusLabelsFailed = "Ui_ManageMods_StatusLabelsFailed";

    /// <summary>模组管理页（ModsPageViewModel）：切换实例后刷新失败，请查看错误栏。</summary>
    public const string ManageModsStatusInstanceSwitchFailed = "Ui_ManageMods_StatusInstanceSwitchFailed";

    /// <summary>模组管理页（ModsPageViewModel）：读取模组数据失败，请查看错误栏。</summary>
    public const string ManageModsStatusLoadFailed = "Ui_ManageMods_StatusLoadFailed";

    /// <summary>模组管理页（ModsPageViewModel）：已装</summary>
    public const string ManageModsFilterInstalled = "Ui_ManageMods_FilterInstalled";

    /// <summary>模组管理页（ModsPageViewModel）：未装</summary>
    public const string ManageModsFilterNotInstalled = "Ui_ManageMods_FilterNotInstalled";

    /// <summary>模组管理页（ModsPageViewModel）：有更新</summary>
    public const string ManageModsFilterUpgradeable = "Ui_ManageMods_FilterUpgradeable";

    /// <summary>模组管理页（ModsPageViewModel）：全部</summary>
    public const string ManageModsFilterAll = "Ui_ManageMods_FilterAll";

    /// <summary>模组管理页（ModsPageViewModel）：正在刷新仓库索引…</summary>
    public const string ManageModsStatusRefreshingRegistry = "Ui_ManageMods_StatusRefreshingRegistry";

    /// <summary>模组管理页（ModsPageViewModel）：刷新仓库索引失败，已保留旧数据。</summary>
    public const string ManageModsStatusRegistryFailed = "Ui_ManageMods_StatusRegistryFailed";

    /// <summary>模组管理页（ModsPageViewModel）：刷新已安装模组失败。</summary>
    public const string ManageModsStatusInstalledRefreshFailed = "Ui_ManageMods_StatusInstalledRefreshFailed";

    /// <summary>模组管理页（ModsPageViewModel）：已刷新：{0} 个模组</summary>
    public const string ManageModsStatusRefreshedCount = "Ui_ManageMods_StatusRefreshedCount";

    /// <summary>模组管理页（ModsPageViewModel）：刷新失败，请查看错误栏。</summary>
    public const string ManageModsStatusRefreshFailed = "Ui_ManageMods_StatusRefreshFailed";

    /// <summary>模组管理页（ModsPageViewModel）：计算变更集失败。</summary>
    public const string ManageModsStatusChangeSetFailed = "Ui_ManageMods_StatusChangeSetFailed";

    /// <summary>模组管理页（ModsPageViewModel）：计算变更集失败，请查看错误栏。</summary>
    public const string ManageModsStatusChangeSetFailedSeeErrors = "Ui_ManageMods_StatusChangeSetFailedSeeErrors";

    /// <summary>模组管理页（ModsPageViewModel）：请先勾选要变更的模组。</summary>
    public const string ManageModsStatusNoSelectionToChange = "Ui_ManageMods_StatusNoSelectionToChange";

    /// <summary>模组管理页（ModsPageViewModel）：当前没有可用的导航服务，无法打开变更集页。</summary>
    public const string ManageModsStatusNoNavigation = "Ui_ManageMods_StatusNoNavigation";

    /// <summary>模组管理页（ModsPageViewModel）：无法打开变更集页。</summary>
    public const string ManageModsStatusCannotOpenChanges = "Ui_ManageMods_StatusCannotOpenChanges";

    /// <summary>模组管理页（ModsPageViewModel）：打开变更集页失败，请查看错误栏。</summary>
    public const string ManageModsStatusOpenChangesFailed = "Ui_ManageMods_StatusOpenChangesFailed";

    /// <summary>模组管理页（ModsPageViewModel）：当前所有已安装模组均为最新版本。</summary>
    public const string ManageModsStatusAllUpToDate = "Ui_ManageMods_StatusAllUpToDate";

    /// <summary>模组管理页（ModsPageViewModel）：已将 {0} 个有更新的模组加入待变更列表。</summary>
    public const string ManageModsStatusAddedUpgrades = "Ui_ManageMods_StatusAddedUpgrades";

    /// <summary>模组管理页（ModsPageViewModel）：当前没有待应用的变更，请先勾选模组或点击全量更新。</summary>
    public const string ManageModsStatusNoPendingChanges = "Ui_ManageMods_StatusNoPendingChanges";

    /// <summary>模组管理页（ModsPageViewModel）：请先选择一个已安装的模组。</summary>
    public const string ManageModsStatusSelectInstalledMod = "Ui_ManageMods_StatusSelectInstalledMod";

    /// <summary>模组管理页（ModsPageViewModel）：已将模组 {0} 加入变更列表（准备重新安装）。</summary>
    public const string ManageModsStatusAddedReinstall = "Ui_ManageMods_StatusAddedReinstall";

    /// <summary>模组管理页（ModsPageViewModel）：未选中任何模组。</summary>
    public const string ManageModsStatusNoSelection = "Ui_ManageMods_StatusNoSelection";

    /// <summary>模组管理页（ModsPageViewModel）：标识符：{0}（当前宿主未接入剪贴板）</summary>
    public const string ManageModsStatusIdentifierNoClipboard = "Ui_ManageMods_StatusIdentifierNoClipboard";

    /// <summary>模组管理页（ModsPageViewModel）：已复制标识符“{0}”到剪贴板。</summary>
    public const string ManageModsStatusIdentifierCopied = "Ui_ManageMods_StatusIdentifierCopied";

    /// <summary>模组管理页（ModsPageViewModel）：写入剪贴板失败。</summary>
    public const string ManageModsStatusClipboardFailed = "Ui_ManageMods_StatusClipboardFailed";

    /// <summary>模组管理页（ModsPageViewModel）：该模组没有记录下载链接。</summary>
    public const string ManageModsStatusNoDownloadLink = "Ui_ManageMods_StatusNoDownloadLink";

    /// <summary>模组管理页（ModsPageViewModel）：下载链接：{0}（当前宿主未接入剪贴板）</summary>
    public const string ManageModsStatusDownloadLinkNoClipboard = "Ui_ManageMods_StatusDownloadLinkNoClipboard";

    /// <summary>模组管理页（ModsPageViewModel）：已复制模组下载地址到剪贴板。</summary>
    public const string ManageModsStatusDownloadLinkCopied = "Ui_ManageMods_StatusDownloadLinkCopied";

    /// <summary>模组管理页（ModsPageViewModel）：该模组没有配置主页链接。</summary>
    public const string ManageModsStatusNoHomepage = "Ui_ManageMods_StatusNoHomepage";

    /// <summary>模组管理页（ModsPageViewModel）：主页链接：{0}（当前宿主未接入外壳浏览器能力）</summary>
    public const string ManageModsStatusHomepageNoBrowser = "Ui_ManageMods_StatusHomepageNoBrowser";

    /// <summary>模组管理页（ModsPageViewModel）：正在打开主页…</summary>
    public const string ManageModsStatusOpeningHomepage = "Ui_ManageMods_StatusOpeningHomepage";

    /// <summary>模组管理页（ModsPageViewModel）：打开主页失败。</summary>
    public const string ManageModsStatusOpenHomepageFailed = "Ui_ManageMods_StatusOpenHomepageFailed";

    // 实例页（InstancesPageViewModel）

    /// <summary>实例页（InstancesPageViewModel）：共 {0} 个实例</summary>
    public const string InstancesCount = "Ui_Instances_Count";

    /// <summary>实例页（InstancesPageViewModel）：准备启动…</summary>
    public const string InstancesPhasePreparing = "Ui_Instances_PhasePreparing";

    /// <summary>实例页（InstancesPageViewModel）：启动中…</summary>
    public const string InstancesPhaseStarting = "Ui_Instances_PhaseStarting";

    /// <summary>实例页（InstancesPageViewModel）：游戏运行中</summary>
    public const string InstancesPhaseRunning = "Ui_Instances_PhaseRunning";

    /// <summary>实例页（InstancesPageViewModel）：恢复数据…</summary>
    public const string InstancesPhaseRestoring = "Ui_Instances_PhaseRestoring";

    /// <summary>实例页（InstancesPageViewModel）：精简模式</summary>
    public const string InstancesTrimMode = "Ui_Instances_TrimMode";

    /// <summary>实例页（InstancesPageViewModel）：已卸载注册表与仓库快照以让出启动资源；游戏退出或点「恢复数据」后恢复。</summary>
    public const string InstancesTrimModeHint = "Ui_Instances_TrimModeHint";

    /// <summary>实例页（InstancesPageViewModel）：还没有游戏实例</summary>
    public const string InstancesEmptyTitle = "Ui_Instances_EmptyTitle";

    /// <summary>实例页（InstancesPageViewModel）：添加一个 KSP 安装目录即可开始。</summary>
    public const string InstancesEmptyHint = "Ui_Instances_EmptyHint";

    /// <summary>实例页（InstancesPageViewModel）：添加实例</summary>
    public const string InstancesEmptyAction = "Ui_Instances_EmptyAction";

    /// <summary>实例页（InstancesPageViewModel）：当前实例：{0}</summary>
    public const string InstancesCurrentSummary = "Ui_Instances_CurrentSummary";

    /// <summary>实例页（InstancesPageViewModel）：当前实例：未选择</summary>
    public const string InstancesCurrentNone = "Ui_Instances_CurrentNone";

    /// <summary>实例页（InstancesPageViewModel）：未选择实例</summary>
    public const string InstancesSelectionNone = "Ui_Instances_SelectionNone";

    /// <summary>实例页（InstancesPageViewModel）：当前实例</summary>
    public const string InstancesBadgeCurrent = "Ui_Instances_BadgeCurrent";

    /// <summary>实例页（InstancesPageViewModel）：默认实例</summary>
    public const string InstancesBadgeDefault = "Ui_Instances_BadgeDefault";

    /// <summary>实例页（InstancesPageViewModel）：普通实例</summary>
    public const string InstancesBadgePlain = "Ui_Instances_BadgePlain";

    /// <summary>实例页（InstancesPageViewModel）：按该实例的启动命令启动游戏；有多条命令时会先让你选一条。</summary>
    public const string InstancesLaunchHint = "Ui_Instances_LaunchHint";

    /// <summary>实例页（InstancesPageViewModel）：正在扫描实例…</summary>
    public const string InstancesBusyScanning = "Ui_Instances_BusyScanning";

    /// <summary>实例页（InstancesPageViewModel）：实例列表已刷新。</summary>
    public const string InstancesStatusRefreshed = "Ui_Instances_StatusRefreshed";

    /// <summary>实例页（InstancesPageViewModel）：请先选择一个实例。</summary>
    public const string InstancesErrorSelectFirst = "Ui_Instances_ErrorSelectFirst";

    /// <summary>实例页（InstancesPageViewModel）：「{0}」已经是当前实例。</summary>
    public const string InstancesStatusAlreadyCurrent = "Ui_Instances_StatusAlreadyCurrent";

    /// <summary>实例页（InstancesPageViewModel）：正在切换实例…</summary>
    public const string InstancesBusySwitching = "Ui_Instances_BusySwitching";

    /// <summary>实例页（InstancesPageViewModel）：已切换到「{0}」。</summary>
    public const string InstancesStatusSwitched = "Ui_Instances_StatusSwitched";

    /// <summary>实例页（InstancesPageViewModel）：切换失败，仍停留在原实例。</summary>
    public const string InstancesStatusSwitchFailed = "Ui_Instances_StatusSwitchFailed";

    /// <summary>实例页（InstancesPageViewModel）：实例「{0}」被其他进程锁定（不可重试）：请先关闭占用该目录的程序，再点「刷新」后重试。</summary>
    public const string InstancesErrorSwitchLocked = "Ui_Instances_ErrorSwitchLocked";
    /// <summary>实例页（InstancesPageViewModel）：当前环境没有可用的目录选择器，无法添加实例。</summary>
    public const string InstancesErrorNoFolderPicker = "Ui_Instances_ErrorNoFolderPicker";

    /// <summary>实例页（InstancesPageViewModel）：选择 KSP 安装目录</summary>
    public const string InstancesPickFolderTitle = "Ui_Instances_PickFolderTitle";

    /// <summary>实例页（InstancesPageViewModel）：已取消选择目录。</summary>
    public const string InstancesStatusFolderPickCancelled = "Ui_Instances_StatusFolderPickCancelled";

    /// <summary>实例页（InstancesPageViewModel）：该目录已经是实例列表中的实例：{0}</summary>
    public const string InstancesErrorDuplicatePath = "Ui_Instances_ErrorDuplicatePath";

    /// <summary>实例页（InstancesPageViewModel）：目录不存在：{0}（不可重试）</summary>
    public const string InstancesErrorDirectoryMissing = "Ui_Instances_ErrorDirectoryMissing";

    /// <summary>实例页（InstancesPageViewModel）：请先选择要克隆的实例。</summary>
    public const string InstancesErrorSelectToClone = "Ui_Instances_ErrorSelectToClone";

    /// <summary>实例页（InstancesPageViewModel）：请先选择要重命名的实例。</summary>
    public const string InstancesErrorSelectToRename = "Ui_Instances_ErrorSelectToRename";

    /// <summary>实例页（InstancesPageViewModel）：已取消。</summary>
    public const string InstancesStatusCancelled = "Ui_Instances_StatusCancelled";

    /// <summary>实例页（InstancesPageViewModel）：正在添加实例…</summary>
    public const string InstancesBusyAdding = "Ui_Instances_BusyAdding";

    /// <summary>实例页（InstancesPageViewModel）：已添加实例「{0}」。</summary>
    public const string InstancesStatusAdded = "Ui_Instances_StatusAdded";

    /// <summary>实例页（InstancesPageViewModel）：目标目录已存在</summary>
    public const string InstancesCloneConflictTitle = "Ui_Instances_CloneConflictTitle";

    /// <summary>实例页（InstancesPageViewModel）：克隆目标目录「{0}」已经存在，继续会把源实例的文件覆盖进去。是否继续？</summary>
    public const string InstancesCloneConflictBody = "Ui_Instances_CloneConflictBody";

    /// <summary>实例页（InstancesPageViewModel）：已取消克隆：目标目录已存在。</summary>
    public const string InstancesStatusCloneCancelled = "Ui_Instances_StatusCloneCancelled";

    /// <summary>实例页（InstancesPageViewModel）：正在克隆实例（可能需要几分钟）…</summary>
    public const string InstancesBusyCloning = "Ui_Instances_BusyCloning";

    /// <summary>实例页（InstancesPageViewModel）：已克隆为「{0}」。</summary>
    public const string InstancesStatusCloned = "Ui_Instances_StatusCloned";

    /// <summary>实例页（InstancesPageViewModel）：重命名实例</summary>
    public const string InstancesRenameTitle = "Ui_Instances_RenameTitle";

    /// <summary>实例页（InstancesPageViewModel）：确定把实例「{0}」重命名为「{1}」吗？只会修改实例记录，不会改名游戏目录。</summary>
    public const string InstancesRenameBody = "Ui_Instances_RenameBody";

    /// <summary>实例页（InstancesPageViewModel）：已取消重命名。</summary>
    public const string InstancesStatusRenameCancelled = "Ui_Instances_StatusRenameCancelled";

    /// <summary>实例页（InstancesPageViewModel）：请先选择要遗忘的实例。</summary>
    public const string InstancesErrorSelectToForget = "Ui_Instances_ErrorSelectToForget";

    /// <summary>实例页（InstancesPageViewModel）：不能遗忘当前正在使用的实例：请先切换到其他实例。</summary>
    public const string InstancesErrorForgetCurrent = "Ui_Instances_ErrorForgetCurrent";

    /// <summary>实例页（InstancesPageViewModel）：遗忘实例</summary>
    public const string InstancesForgetTitle = "Ui_Instances_ForgetTitle";

    /// <summary>实例页（InstancesPageViewModel）：确定要遗忘实例「{0}」吗？只会从实例列表中移除记录，不会删除游戏目录里的任何文件。</summary>
    public const string InstancesForgetBody = "Ui_Instances_ForgetBody";

    /// <summary>实例页（InstancesPageViewModel）：已取消遗忘。</summary>
    public const string InstancesStatusForgetCancelled = "Ui_Instances_StatusForgetCancelled";

    /// <summary>实例页（InstancesPageViewModel）：正在遗忘实例…</summary>
    public const string InstancesBusyForgetting = "Ui_Instances_BusyForgetting";

    /// <summary>实例页（InstancesPageViewModel）：已遗忘实例「{0}」，游戏目录未做任何改动。</summary>
    public const string InstancesStatusForgotten = "Ui_Instances_StatusForgotten";

    /// <summary>实例页（InstancesPageViewModel）：当前环境没有可用的外壳启动器，无法打开目录。</summary>
    public const string InstancesErrorNoShellLauncher = "Ui_Instances_ErrorNoShellLauncher";

    /// <summary>实例页（InstancesPageViewModel）：已在文件管理器中打开：{0}</summary>
    public const string InstancesStatusOpenedFolder = "Ui_Instances_StatusOpenedFolder";

    /// <summary>实例页（InstancesPageViewModel）：实例「{0}」被其他进程占用（可能游戏已在运行），不能重复启动。</summary>
    public const string InstancesErrorBusyLaunching = "Ui_Instances_ErrorBusyLaunching";

    /// <summary>实例页（InstancesPageViewModel）：实例「{0}」没有可用的启动命令，请先编辑启动命令。</summary>
    public const string InstancesErrorNoLaunchCommand = "Ui_Instances_ErrorNoLaunchCommand";

    /// <summary>实例页（InstancesPageViewModel）：启动「{0}」</summary>
    public const string InstancesLaunchDialogTitle = "Ui_Instances_LaunchDialogTitle";

    /// <summary>实例页（InstancesPageViewModel）：该实例有多条启动命令，请选择要使用的一条：</summary>
    public const string InstancesLaunchDialogBody = "Ui_Instances_LaunchDialogBody";

    /// <summary>实例页（InstancesPageViewModel）：已取消启动。</summary>
    public const string InstancesStatusLaunchCancelled = "Ui_Instances_StatusLaunchCancelled";

    /// <summary>实例页（InstancesPageViewModel）：启动游戏尚未接入（未注入外壳启动器）；可先用「打开目录」自行启动。</summary>
    public const string InstancesErrorLauncherMissing = "Ui_Instances_ErrorLauncherMissing";

    /// <summary>实例页（InstancesPageViewModel）：已启动「{0}」：{1}</summary>
    public const string InstancesStatusLaunched = "Ui_Instances_StatusLaunched";

    /// <summary>实例页（InstancesPageViewModel）：游戏已退出，已恢复数据（本次释放 {0} 项）。</summary>
    public const string InstancesStatusRestored = "Ui_Instances_StatusRestored";

    /// <summary>实例页（InstancesPageViewModel）：注册表锁未能释放（{0}），仍继续启动。</summary>
    public const string InstancesStatusLockReleaseFailedDetail = "Ui_Instances_StatusLockReleaseFailedDetail";

    /// <summary>实例页（InstancesPageViewModel）：注册表锁未能释放，仍继续启动。</summary>
    public const string InstancesStatusLockReleaseFailed = "Ui_Instances_StatusLockReleaseFailed";

    /// <summary>实例页（InstancesPageViewModel）：正在恢复数据…</summary>
    public const string InstancesBusyRestoring = "Ui_Instances_BusyRestoring";

    /// <summary>实例页（InstancesPageViewModel）：请先选择要编辑启动命令的实例。</summary>
    public const string InstancesErrorSelectToEditCommands = "Ui_Instances_ErrorSelectToEditCommands";

    /// <summary>实例页（InstancesPageViewModel）：已取消编辑启动命令。</summary>
    public const string InstancesStatusEditCommandsCancelled = "Ui_Instances_StatusEditCommandsCancelled";

    /// <summary>实例页（InstancesPageViewModel）：启动命令存储尚未接入，无法保存。</summary>
    public const string InstancesErrorCommandStoreMissing = "Ui_Instances_ErrorCommandStoreMissing";

    /// <summary>实例页（InstancesPageViewModel）：已保存「{0}」的启动命令（{1} 条）。</summary>
    public const string InstancesStatusCommandsSaved = "Ui_Instances_StatusCommandsSaved";

    /// <summary>实例页（InstancesPageViewModel）：保存启动命令时出错，详见错误栏。</summary>
    public const string InstancesErrorSaveCommandsFailed = "Ui_Instances_ErrorSaveCommandsFailed";

    /// <summary>实例页（InstancesPageViewModel）：Steam 库探测尚未接入：请使用「添加实例」手动选择目录。</summary>
    public const string InstancesErrorSteamMissing = "Ui_Instances_ErrorSteamMissing";

    /// <summary>实例页（InstancesPageViewModel）：正在扫描 Steam 库…</summary>
    public const string InstancesBusyScanningSteam = "Ui_Instances_BusyScanningSteam";

    /// <summary>实例页（InstancesPageViewModel）：未在 Steam 库中找到新的 KSP 安装目录。</summary>
    public const string InstancesStatusSteamNoneFound = "Ui_Instances_StatusSteamNoneFound";

    /// <summary>实例页（InstancesPageViewModel）：从 Steam 库导入</summary>
    public const string InstancesSteamDialogTitle = "Ui_Instances_SteamDialogTitle";

    /// <summary>实例页（InstancesPageViewModel）：请选择要导入的 KSP 安装目录：</summary>
    public const string InstancesSteamDialogBody = "Ui_Instances_SteamDialogBody";

    /// <summary>实例页（InstancesPageViewModel）：已取消导入。</summary>
    public const string InstancesStatusImportCancelled = "Ui_Instances_StatusImportCancelled";

    /// <summary>实例页（InstancesPageViewModel）：导入实例</summary>
    public const string InstancesImportTitle = "Ui_Instances_ImportTitle";

    /// <summary>实例页（InstancesPageViewModel）：要把「{0}」添加为游戏实例吗？不会修改该目录中的任何文件。</summary>
    public const string InstancesImportBody = "Ui_Instances_ImportBody";

    /// <summary>实例页（InstancesPageViewModel）：已从 Steam 库导入实例「{0}」。</summary>
    public const string InstancesStatusImported = "Ui_Instances_StatusImported";

    /// <summary>实例页（InstancesPageViewModel）：尚未自检。</summary>
    public const string InstancesDiagnosisNotRun = "Ui_Instances_DiagnosisNotRun";

    /// <summary>实例页（InstancesPageViewModel）：实例「{0}」自检通过：锁与注册表都正常。</summary>
    public const string InstancesDiagnosisPassed = "Ui_Instances_DiagnosisPassed";

    /// <summary>实例页（InstancesPageViewModel）：存在问题</summary>
    public const string InstancesDiagnosisIssue = "Ui_Instances_DiagnosisIssue";

    /// <summary>实例页（InstancesPageViewModel）：实例「{0}」发现 {1} 个问题：{2}。</summary>
    public const string InstancesDiagnosisIssues = "Ui_Instances_DiagnosisIssues";

    /// <summary>实例页（InstancesPageViewModel）：自检</summary>
    public const string InstancesActionSelfCheck = "Ui_Instances_ActionSelfCheck";

    /// <summary>实例页（InstancesPageViewModel）：正在自检实例…</summary>
    public const string InstancesBusySelfChecking = "Ui_Instances_BusySelfChecking";

    /// <summary>实例页（InstancesPageViewModel）：强制解锁</summary>
    public const string InstancesActionForceUnlock = "Ui_Instances_ActionForceUnlock";

    /// <summary>实例页（InstancesPageViewModel）：请先对实例「{0}」点一次「自检」，确认游戏没有在运行后再强制解锁。</summary>
    public const string InstancesErrorSelfCheckFirstUnlock = "Ui_Instances_ErrorSelfCheckFirstUnlock";

    /// <summary>实例页（InstancesPageViewModel）：游戏正在运行，无法解锁实例「{0}」。请先退出游戏再重试。（不可重试）</summary>
    public const string InstancesErrorUnlockRunning = "Ui_Instances_ErrorUnlockRunning";

    /// <summary>实例页（InstancesPageViewModel）：实例「{0}」当前没有被锁定，无需解锁。（不可重试）</summary>
    public const string InstancesErrorUnlockNotLocked = "Ui_Instances_ErrorUnlockNotLocked";

    /// <summary>实例页（InstancesPageViewModel）：强制解锁实例</summary>
    public const string InstancesUnlockTitle = "Ui_Instances_UnlockTitle";

    /// <summary>实例页（InstancesPageViewModel）：会删掉实例「{0}」的占用标记（CKAN/registry.locked），不会动注册表、模组或存档。要继续吗？</summary>
    public const string InstancesUnlockBody = "Ui_Instances_UnlockBody";

    /// <summary>实例页（InstancesPageViewModel）：已取消解锁。</summary>
    public const string InstancesStatusUnlockCancelled = "Ui_Instances_StatusUnlockCancelled";

    /// <summary>实例页（InstancesPageViewModel）：正在强制解锁…</summary>
    public const string InstancesBusyUnlocking = "Ui_Instances_BusyUnlocking";

    /// <summary>实例页（InstancesPageViewModel）：隔离注册表</summary>
    public const string InstancesActionIsolateRegistry = "Ui_Instances_ActionIsolateRegistry";

    /// <summary>实例页（InstancesPageViewModel）：请先对实例「{0}」点一次「自检」，确认注册表已损坏后再隔离。</summary>
    public const string InstancesErrorSelfCheckFirstIsolate = "Ui_Instances_ErrorSelfCheckFirstIsolate";

    /// <summary>实例页（InstancesPageViewModel）：实例「{0}」的 registry.json 当前可以正常解析，无需隔离。（不可重试）</summary>
    public const string InstancesErrorIsolateHealthy = "Ui_Instances_ErrorIsolateHealthy";

    /// <summary>实例页（InstancesPageViewModel）：隔离损坏的注册表</summary>
    public const string InstancesIsolateTitle = "Ui_Instances_IsolateTitle";

    /// <summary>实例页（InstancesPageViewModel）：会把解析不了的 registry.json 改名成 registry.json.corrupt-&lt;时间戳&gt; 留在原目录，CKAN 下次启动会重建一个空注册表（模组文件不会被删除）。要继续吗？</summary>
    public const string InstancesIsolateBody = "Ui_Instances_IsolateBody";

    /// <summary>实例页（InstancesPageViewModel）：已取消隔离。</summary>
    public const string InstancesStatusIsolateCancelled = "Ui_Instances_StatusIsolateCancelled";

    /// <summary>实例页（InstancesPageViewModel）：正在隔离注册表…</summary>
    public const string InstancesBusyIsolating = "Ui_Instances_BusyIsolating";

    /// <summary>实例页（InstancesPageViewModel）：请先选择一个实例，再点「{0}」。</summary>
    public const string InstancesErrorSelectFirstThenAction = "Ui_Instances_ErrorSelectFirstThenAction";

    /// <summary>实例页（InstancesPageViewModel）：实例自检功能尚未接线：请在容器里注册 IInstanceRepairService 后重试。</summary>
    public const string InstancesErrorRepairMissing = "Ui_Instances_ErrorRepairMissing";

    // 模组汉化/翻译页（ModTextPageViewModel）

    /// <summary>模组汉化/翻译页（ModTextPageViewModel）：模组翻译</summary>
    public const string ModTextTitle = "Ui_ModText_Title";

    // 游戏时长页（PlayTimePageViewModel）

    /// <summary>游戏时长页（PlayTimePageViewModel）：请先选中一行再重置。</summary>
    public const string PlayTimeErrorNoSelection = "Ui_PlayTime_ErrorNoSelection";

    /// <summary>游戏时长页（PlayTimePageViewModel）：当前环境没有确认对话框，已取消这次操作。</summary>
    public const string PlayTimeErrorNoDialog = "Ui_PlayTime_ErrorNoDialog";

    /// <summary>游戏时长页（PlayTimePageViewModel）：重置「{0}」的游玩时长？</summary>
    public const string PlayTimeResetConfirmTitle = "Ui_PlayTime_ResetConfirmTitle";

    /// <summary>游戏时长页（PlayTimePageViewModel）：将把「{0}」的累计游玩时长清零（手动补录的与会话自动累计的一起清），重置后无法撤销。</summary>
    public const string PlayTimeResetConfirmBody = "Ui_PlayTime_ResetConfirmBody";

    /// <summary>游戏时长页（PlayTimePageViewModel）：已取消重置。</summary>
    public const string PlayTimeStatusResetCancelled = "Ui_PlayTime_StatusResetCancelled";

    /// <summary>游戏时长页（PlayTimePageViewModel）：重置「{0}」的游玩时长失败，请稍后重试。</summary>
    public const string PlayTimeErrorResetFailed = "Ui_PlayTime_ErrorResetFailed";

    /// <summary>游戏时长页（PlayTimePageViewModel）：已重置「{0}」的时长。</summary>
    public const string PlayTimeStatusReset = "Ui_PlayTime_StatusReset";

    /// <summary>游戏时长页（PlayTimePageViewModel）：重置「{0}」的游玩时长时出现异常：{1}</summary>
    public const string PlayTimeErrorResetException = "Ui_PlayTime_ErrorResetException";

    /// <summary>游戏时长页（PlayTimePageViewModel）：请输入大于或等于零的有效小时数。</summary>
    public const string PlayTimeErrorHoursInvalid = "Ui_PlayTime_ErrorHoursInvalid";

    /// <summary>游戏时长页（PlayTimePageViewModel）：游戏时长保存失败，请检查文件权限后重试。</summary>
    public const string PlayTimeErrorSaveFailed = "Ui_PlayTime_ErrorSaveFailed";


    /// <summary>游戏时长页（PlayTimePageViewModel）：{0}（可重试）</summary>
    public const string PlayTimeRetryableSuffix = "Ui_PlayTime_RetryableSuffix";

    /// <summary>游戏时长页（PlayTimePageViewModel）：{0} 小时</summary>
    public const string PlayTimeTotalHours = "Ui_PlayTime_TotalHours";

    /// <summary>游戏时长页（PlayTimePageViewModel）：{0} 个实例</summary>
    public const string PlayTimeInstanceCount = "Ui_PlayTime_InstanceCount";

    /// <summary>游戏时长页（PlayTimePageViewModel）：暂无记录</summary>
    public const string PlayTimeLongestEmpty = "Ui_PlayTime_LongestEmpty";

    // 内容预览页（ContentPageViewModel）

    /// <summary>内容预览页（ContentPageViewModel）：请先在列表中选中一个文件或目录。</summary>
    public const string ContentErrorNoSelection = "Ui_Content_ErrorNoSelection";

    /// <summary>内容预览页（ContentPageViewModel）：当前宿主没有提供剪贴板支持。</summary>
    public const string ContentErrorNoClipboard = "Ui_Content_ErrorNoClipboard";

    /// <summary>内容预览页（ContentPageViewModel）：已复制路径：{0}</summary>
    public const string ContentStatusCopied = "Ui_Content_StatusCopied";

    /// <summary>内容预览页（ContentPageViewModel）：复制失败。</summary>
    public const string ContentErrorCopyFailed = "Ui_Content_ErrorCopyFailed";

    /// <summary>内容预览页（ContentPageViewModel）：当前模组 ZIP 尚未下载，无法打开所在目录。</summary>
    public const string ContentErrorZipMissing = "Ui_Content_ErrorZipMissing";

    /// <summary>内容预览页（ContentPageViewModel）：当前宿主没有提供文件管理器支持。</summary>
    public const string ContentErrorNoFileManager = "Ui_Content_ErrorNoFileManager";

    /// <summary>内容预览页（ContentPageViewModel）：ZIP 所在目录不存在。</summary>
    public const string ContentErrorFolderMissing = "Ui_Content_ErrorFolderMissing";

    /// <summary>内容预览页（ContentPageViewModel）：打开目录失败。</summary>
    public const string ContentErrorOpenFolderFailed = "Ui_Content_ErrorOpenFolderFailed";

    /// <summary>内容预览页（ContentPageViewModel）：内容预览</summary>
    public const string ContentPageName = "Ui_Content_PageName";

    /// <summary>内容预览页（ContentPageViewModel）：请先在模组页选择一个模组。</summary>
    public const string ContentEmptyNoMod = "Ui_Content_EmptyNoMod";

    /// <summary>内容预览页（ContentPageViewModel）：无法读取此模组的内容。</summary>
    public const string ContentEmptyReadFailed = "Ui_Content_EmptyReadFailed";

    /// <summary>内容预览页（ContentPageViewModel）：ZIP 中没有文件条目。</summary>
    public const string ContentEmptyNoEntries = "Ui_Content_EmptyNoEntries";

    /// <summary>内容预览页（ContentPageViewModel）：操作已取消。</summary>
    public const string CommonActionCancelled = "Ui_Common_ActionCancelled";

    /// <summary>内容预览页（ContentPageViewModel）：读取模组内容失败。</summary>
    public const string ContentErrorLoadFailed = "Ui_Content_ErrorLoadFailed";


    // 下载统计页（DownloadStatsPageViewModel）

    /// <summary>下载统计页（DownloadStatsPageViewModel）：共 {0} 个模组，{1} 次下载</summary>
    public const string DownloadStatsStatusSummary = "Ui_DownloadStats_StatusSummary";

    /// <summary>下载统计页（DownloadStatsPageViewModel）：标签</summary>
    public const string DownloadStatsDimensionTags = "Ui_DownloadStats_DimensionTags";

    /// <summary>下载统计页（DownloadStatsPageViewModel）：安装状态</summary>
    public const string DownloadStatsDimensionInstalled = "Ui_DownloadStats_DimensionInstalled";

    /// <summary>下载统计页（DownloadStatsPageViewModel）：仓库</summary>
    public const string DownloadStatsDimensionRepositories = "Ui_DownloadStats_DimensionRepositories";

    /// <summary>下载统计页（DownloadStatsPageViewModel）：模组</summary>
    public const string DownloadStatsDimensionMods = "Ui_DownloadStats_DimensionMods";

    /// <summary>下载统计页（DownloadStatsPageViewModel）：部分模组没有下载量数据。</summary>
    public const string DownloadStatsUnavailableHint = "Ui_DownloadStats_UnavailableHint";

    /// <summary>下载统计页（DownloadStatsPageViewModel）：无法读取下载统计。</summary>
    public const string DownloadStatsErrorLoad = "Ui_DownloadStats_ErrorLoad";

    /// <summary>下载统计页（DownloadStatsPageViewModel）：下载统计失败：{0}</summary>
    public const string DownloadStatsErrorException = "Ui_DownloadStats_ErrorException";

    // 仓库页（ReposPageViewModel）

    /// <summary>仓库页（ReposPageViewModel）：仓库列表已从本地注册表载入。</summary>
    public const string ReposStatusLoadedLocal = "Ui_Repos_StatusLoadedLocal";

    /// <summary>仓库页（ReposPageViewModel）：共 {0} 个仓库</summary>
    public const string ReposCountSummary = "Ui_Repos_CountSummary";

    /// <summary>仓库页（ReposPageViewModel）：还没有配置任何仓库</summary>
    public const string ReposEmptyTitle = "Ui_Repos_EmptyTitle";

    /// <summary>仓库页（ReposPageViewModel）：仓库决定模组索引从哪来。</summary>
    public const string ReposEmptyHint = "Ui_Repos_EmptyHint";

    /// <summary>仓库页（ReposPageViewModel）：选择一个仓库查看详情。</summary>
    public const string ReposEmptySelectHint = "Ui_Repos_EmptySelectHint";

    /// <summary>仓库页（ReposPageViewModel）：仓库列表已重新读取。</summary>
    public const string ReposStatusReloaded = "Ui_Repos_StatusReloaded";

    /// <summary>仓库页（ReposPageViewModel）：正在读取仓库列表…</summary>
    public const string ReposBusyLoading = "Ui_Repos_BusyLoading";

    /// <summary>仓库页（ReposPageViewModel）：请填写仓库名称和地址。</summary>
    public const string ReposErrorNameRequired = "Ui_Repos_ErrorNameRequired";

    /// <summary>仓库页（ReposPageViewModel）：已添加仓库「{0}」。</summary>
    public const string ReposStatusAdded = "Ui_Repos_StatusAdded";

    /// <summary>仓库页（ReposPageViewModel）：保存失败。</summary>
    public const string ReposErrorSaveFailed = "Ui_Repos_ErrorSaveFailed";

    /// <summary>仓库页（ReposPageViewModel）：正在保存仓库列表…</summary>
    public const string ReposBusySaving = "Ui_Repos_BusySaving";

    /// <summary>仓库页（ReposPageViewModel）：移除仓库</summary>
    public const string ReposRemoveTitle = "Ui_Repos_RemoveTitle";

    /// <summary>仓库页（ReposPageViewModel）：确定要移除仓库「{0}」吗？</summary>
    public const string ReposRemoveConfirm = "Ui_Repos_RemoveConfirm";

    /// <summary>仓库页（ReposPageViewModel）：移除后该仓库提供的索引将不再参与搜索，已安装的模组不受影响。</summary>
    public const string ReposRemoveHint = "Ui_Repos_RemoveHint";

    /// <summary>仓库页（ReposPageViewModel）：已取消移除仓库。</summary>
    public const string ReposStatusRemoveCancelled = "Ui_Repos_StatusRemoveCancelled";

    /// <summary>仓库页（ReposPageViewModel）：已移除仓库「{0}」。</summary>
    public const string ReposStatusRemoved = "Ui_Repos_StatusRemoved";

    /// <summary>仓库页（ReposPageViewModel）：仓库「{0}」已刷新。</summary>
    public const string ReposStatusRefreshed = "Ui_Repos_StatusRefreshed";

    /// <summary>仓库页（ReposPageViewModel）：正在刷新仓库索引…</summary>
    public const string ReposBusyRefreshing = "Ui_Repos_BusyRefreshing";

    /// <summary>仓库页（ReposPageViewModel）：仓库索引刷新失败。</summary>
    public const string ReposErrorRefreshFailed = "Ui_Repos_ErrorRefreshFailed";

    /// <summary>仓库页（ReposPageViewModel）：仓库索引已刷新。</summary>
    public const string ReposStatusRefreshDone = "Ui_Repos_StatusRefreshDone";

    /// <summary>仓库页（ReposPageViewModel）：恢复默认仓库</summary>
    public const string ReposRestoreTitle = "Ui_Repos_RestoreTitle";

    /// <summary>仓库页（ReposPageViewModel）：恢复为官方默认仓库列表？</summary>
    public const string ReposRestoreConfirm = "Ui_Repos_RestoreConfirm";

    /// <summary>仓库页（ReposPageViewModel）：当前仓库列表会被替换，需要联网获取官方列表。</summary>
    public const string ReposRestoreHint = "Ui_Repos_RestoreHint";

    /// <summary>仓库页（ReposPageViewModel）：已取消恢复默认仓库。</summary>
    public const string ReposStatusRestoreCancelled = "Ui_Repos_StatusRestoreCancelled";

    /// <summary>仓库页（ReposPageViewModel）：已恢复为官方默认仓库列表。</summary>
    public const string ReposStatusRestored = "Ui_Repos_StatusRestored";

    /// <summary>仓库页（ReposPageViewModel）：正在获取官方仓库列表…</summary>
    public const string ReposBusyFetchingDefaults = "Ui_Repos_BusyFetchingDefaults";

    /// <summary>仓库页（ReposPageViewModel）：已上移仓库「{0}」。</summary>
    public const string ReposStatusMovedUp = "Ui_Repos_StatusMovedUp";

    /// <summary>仓库页（ReposPageViewModel）：已下移仓库「{0}」。</summary>
    public const string ReposStatusMovedDown = "Ui_Repos_StatusMovedDown";

    /// <summary>仓库页（ReposPageViewModel）：仓库列表操作失败。</summary>
    public const string ReposErrorOperation = "Ui_Repos_ErrorOperation";

    /// <summary>仓库页（ReposPageViewModel）：仓库列表已更新。</summary>
    public const string ReposStatusUpdated = "Ui_Repos_StatusUpdated";

    // 插件页（PluginsPageViewModel）

    /// <summary>插件页（PluginsPageViewModel）：没有激活的插件</summary>
    public const string PluginsActiveNone = "Ui_Plugins_ActiveNone";

    /// <summary>插件页（PluginsPageViewModel）：已激活 {0} 个</summary>
    public const string PluginsActiveCount = "Ui_Plugins_ActiveCount";

    /// <summary>插件页（PluginsPageViewModel）：没有待激活的插件</summary>
    public const string PluginsDormantNone = "Ui_Plugins_DormantNone";

    /// <summary>插件页（PluginsPageViewModel）：未激活 {0} 个</summary>
    public const string PluginsDormantCount = "Ui_Plugins_DormantCount";

    /// <summary>插件页（PluginsPageViewModel）：扫描插件目录失败。</summary>
    public const string PluginsErrorScanFailed = "Ui_Plugins_ErrorScanFailed";

    /// <summary>插件页（PluginsPageViewModel）：已扫描插件目录：激活 {0} 个，未激活 {1} 个。</summary>
    public const string PluginsStatusScanned = "Ui_Plugins_StatusScanned";

    /// <summary>插件页（PluginsPageViewModel）：正在扫描插件目录…</summary>
    public const string PluginsBusyScanning = "Ui_Plugins_BusyScanning";

    /// <summary>插件页（PluginsPageViewModel）：当前视图没有提供文件选择器，无法新增插件。</summary>
    public const string PluginsErrorNoFilePicker = "Ui_Plugins_ErrorNoFilePicker";

    /// <summary>插件页（PluginsPageViewModel）：正在新增插件…</summary>
    public const string PluginsBusyAdding = "Ui_Plugins_BusyAdding";

    /// <summary>插件页（PluginsPageViewModel）：正在激活插件…</summary>
    public const string PluginsBusyActivating = "Ui_Plugins_BusyActivating";

    /// <summary>插件页（PluginsPageViewModel）：已停用插件「{0}」。</summary>
    public const string PluginsStatusDeactivated = "Ui_Plugins_StatusDeactivated";

    /// <summary>插件页（PluginsPageViewModel）：停用插件失败。</summary>
    public const string PluginsErrorDeactivateFailed = "Ui_Plugins_ErrorDeactivateFailed";

    /// <summary>插件页（PluginsPageViewModel）：正在停用插件…</summary>
    public const string PluginsBusyDeactivating = "Ui_Plugins_BusyDeactivating";

    /// <summary>插件页（PluginsPageViewModel）：正在重载插件…</summary>
    public const string PluginsBusyReloading = "Ui_Plugins_BusyReloading";

    /// <summary>插件页（PluginsPageViewModel）：已卸载插件「{0}」。dll 仍在插件目录里，下次扫描会重新出现。</summary>
    public const string PluginsStatusUninstalled = "Ui_Plugins_StatusUninstalled";

    /// <summary>插件页（PluginsPageViewModel）：卸载插件失败。</summary>
    public const string PluginsErrorUninstallFailed = "Ui_Plugins_ErrorUninstallFailed";

    /// <summary>插件页（PluginsPageViewModel）：正在卸载插件…</summary>
    public const string PluginsBusyUninstalling = "Ui_Plugins_BusyUninstalling";

    /// <summary>插件页（PluginsPageViewModel）：加载失败</summary>
    public const string PluginsLoadFailedLabel = "Ui_Plugins_LoadFailedLabel";

    /// <summary>插件页（PluginsPageViewModel）：插件</summary>
    public const string PluginsFallbackName = "Ui_Plugins_FallbackName";


    /// <summary>插件页（PluginsPageViewModel）：已新增插件「{0}」。</summary>
    public const string PluginsAdded = "Ui_Plugins_Added";


    /// <summary>插件页（PluginsPageViewModel）：已新增插件失败。</summary>
    public const string PluginsAddedFailed = "Ui_Plugins_AddedFailed";


    /// <summary>插件页（PluginsPageViewModel）：已激活插件「{0}」。</summary>
    public const string PluginsActivated = "Ui_Plugins_Activated";


    /// <summary>插件页（PluginsPageViewModel）：已激活插件失败。</summary>
    public const string PluginsActivateFailed = "Ui_Plugins_ActivateFailed";


    /// <summary>插件页（PluginsPageViewModel）：已重载插件「{0}」。</summary>
    public const string PluginsReloaded = "Ui_Plugins_Reloaded";


    /// <summary>插件页（PluginsPageViewModel）：已重载插件失败。</summary>
    public const string PluginsReloadFailed = "Ui_Plugins_ReloadFailed";

    // ModConfig 页（ModConfigPageViewModel）

    /// <summary>ModConfig 页（ModConfigPageViewModel）：Mod 配置</summary>
    public const string ModConfigPageTitle = "Ui_ModConfig_PageTitle";

    /// <summary>ModConfig 页（ModConfigPageViewModel）：选择左侧的 mod 查看可配置项；没有适配器的 mod 只提供原始只读视图。</summary>
    public const string ModConfigStatusHint = "Ui_ModConfig_StatusHint";

    /// <summary>ModConfig 页（ModConfigPageViewModel）：KSP 正在运行，写入需重启生效。</summary>
    public const string ModConfigKspRunningHint = "Ui_ModConfig_KspRunningHint";

    /// <summary>ModConfig 页（ModConfigPageViewModel）：未选择 mod</summary>
    public const string ModConfigNoModSelected = "Ui_ModConfig_NoModSelected";

    /// <summary>ModConfig 页（ModConfigPageViewModel）：保存改动</summary>
    public const string ModConfigSaveButton = "Ui_ModConfig_SaveButton";

    /// <summary>ModConfig 页（ModConfigPageViewModel）：保存 {0} 项改动</summary>
    public const string ModConfigSaveButtonPending = "Ui_ModConfig_SaveButtonPending";

    /// <summary>ModConfig 页（ModConfigPageViewModel）：保存时逐文件写盘，失败只报该文件。</summary>
    public const string ModConfigWriteNote = "Ui_ModConfig_WriteNote";

    /// <summary>ModConfig 页（ModConfigPageViewModel）：恢复上次快照</summary>
    public const string ModConfigRestoreLastSnapshot = "Ui_ModConfig_RestoreLastSnapshot";

    /// <summary>ModConfig 页（ModConfigPageViewModel）：以下文件没有写成，改动已保留在页面上：</summary>
    public const string ModConfigSaveFailureTitle = "Ui_ModConfig_SaveFailureTitle";

    /// <summary>ModConfig 页（ModConfigPageViewModel）：左侧选择一个 mod 查看它的可配置项。</summary>
    public const string ModConfigNoSelectionHint = "Ui_ModConfig_NoSelectionHint";

    /// <summary>ModConfig 页（ModConfigPageViewModel）：正在读取 mod 配置文件…</summary>
    public const string ModConfigBusyLoading = "Ui_ModConfig_BusyLoading";

    /// <summary>ModConfig 页（ModConfigPageViewModel）：读取 mod 配置失败。</summary>
    public const string ModConfigErrorLoad = "Ui_ModConfig_ErrorLoad";

    /// <summary>ModConfig 页（ModConfigPageViewModel）：读取 mod 配置失败：{0}</summary>
    public const string ModConfigErrorLoadDetail = "Ui_ModConfig_ErrorLoadDetail";

    /// <summary>ModConfig 页（ModConfigPageViewModel）：这一行没有需要保存的改动。</summary>
    public const string ModConfigStatusRowNoChanges = "Ui_ModConfig_StatusRowNoChanges";

    /// <summary>ModConfig 页（ModConfigPageViewModel）：没有可恢复的快照：只有保存过一次之后才有 .bak 快照。</summary>
    public const string ModConfigStatusNoSnapshot = "Ui_ModConfig_StatusNoSnapshot";

    /// <summary>ModConfig 页（ModConfigPageViewModel）：已取消恢复快照。</summary>
    public const string ModConfigStatusRestoreCancelled = "Ui_ModConfig_StatusRestoreCancelled";

    /// <summary>ModConfig 页（ModConfigPageViewModel）：恢复快照失败。</summary>
    public const string ModConfigErrorRestore = "Ui_ModConfig_ErrorRestore";

    /// <summary>ModConfig 页（ModConfigPageViewModel）：恢复快照失败：{0}</summary>
    public const string ModConfigErrorRestoreDetail = "Ui_ModConfig_ErrorRestoreDetail";

    /// <summary>ModConfig 页（ModConfigPageViewModel）：没有配置适配器目录，适配器清单为空。</summary>
    public const string ModConfigEmptyNoAdapterDir = "Ui_ModConfig_EmptyNoAdapterDir";

    /// <summary>ModConfig 页（ModConfigPageViewModel）：正在导入 adapter…</summary>
    public const string ModConfigBusyImporting = "Ui_ModConfig_BusyImporting";

    /// <summary>ModConfig 页（ModConfigPageViewModel）：导入 adapter 失败。</summary>
    public const string ModConfigErrorImport = "Ui_ModConfig_ErrorImport";

    /// <summary>ModConfig 页（ModConfigPageViewModel）：已取消导入。</summary>
    public const string ModConfigStatusImportCancelled = "Ui_ModConfig_StatusImportCancelled";

    /// <summary>ModConfig 页（ModConfigPageViewModel）：这个实例里没有已适配的 mod：可以把 adapter.json 放进用户目录后重试。</summary>
    public const string ModConfigEmptyNoAdaptedMod = "Ui_ModConfig_EmptyNoAdaptedMod";

    /// <summary>ModConfig 页（ModConfigPageViewModel）：没有需要保存的改动。</summary>
    public const string ModConfigStatusNoChanges = "Ui_ModConfig_StatusNoChanges";

    /// <summary>ModConfig 页（ModConfigPageViewModel）：当前环境没有写盘入口（IAdapterWriter），已取消保存。</summary>
    public const string ModConfigErrorNoWriter = "Ui_ModConfig_ErrorNoWriter";

    /// <summary>ModConfig 页（ModConfigPageViewModel）：还没有读到实例模型，已取消保存。</summary>
    public const string ModConfigErrorNoModel = "Ui_ModConfig_ErrorNoModel";

    /// <summary>ModConfig 页（ModConfigPageViewModel）：这些改动找不到所属的 mod，已取消保存。</summary>
    public const string ModConfigErrorOrphanEdits = "Ui_ModConfig_ErrorOrphanEdits";

    /// <summary>ModConfig 页（ModConfigPageViewModel）：KSP 正在运行</summary>
    public const string ModConfigKspRunningTitle = "Ui_ModConfig_KspRunningTitle";

    /// <summary>ModConfig 页（ModConfigPageViewModel）：已取消保存：KSP 正在运行时的写盘需要确认。</summary>
    public const string ModConfigStatusCancelKsp = "Ui_ModConfig_StatusCancelKsp";

    /// <summary>ModConfig 页（ModConfigPageViewModel）：确认修改高风险字段</summary>
    public const string ModConfigConfirmHighRiskTitle = "Ui_ModConfig_ConfirmHighRiskTitle";

    /// <summary>ModConfig 页（ModConfigPageViewModel）：已取消保存：高风险字段的改动需要二次确认。</summary>
    public const string ModConfigStatusCancelHighRisk = "Ui_ModConfig_StatusCancelHighRisk";

    /// <summary>ModConfig 页（ModConfigPageViewModel）：写入配置失败：{0}</summary>
    public const string ModConfigErrorWriteDetail = "Ui_ModConfig_ErrorWriteDetail";

    /// <summary>ModConfig 页（ModConfigPageViewModel）：写入失败。</summary>
    public const string ModConfigErrorWrite = "Ui_ModConfig_ErrorWrite";

    /// <summary>ModConfig 页（ModConfigPageViewModel）：保存失败：这次没有任何文件写成，改动已保留。</summary>
    public const string ModConfigErrorSaveNothing = "Ui_ModConfig_ErrorSaveNothing";

    /// <summary>ModConfig 页（ModConfigPageViewModel）：重读 mod 配置失败。</summary>
    public const string ModConfigErrorReload = "Ui_ModConfig_ErrorReload";

    /// <summary>ModConfig 页（ModConfigPageViewModel）：重读 mod 配置失败：{0}</summary>
    public const string ModConfigErrorReloadDetail = "Ui_ModConfig_ErrorReloadDetail";

    /// <summary>ModConfig 页（ModConfigPageViewModel）：当前环境没有确认对话框，已取消这次写盘。</summary>
    public const string ModConfigErrorNoDialog = "Ui_ModConfig_ErrorNoDialog";

    // 日志页（LogPageViewModel）

    /// <summary>日志页（LogPageViewModel）：未选中日志行</summary>
    public const string LogsNoSelection = "Ui_Logs_NoSelection";

    /// <summary>日志页（LogPageViewModel）：命中 {0} 条（显示 {1}）· 错误 {2} · 警告 {3}</summary>
    public const string LogsSummaryHits = "Ui_Logs_SummaryHits";

    /// <summary>日志页（LogPageViewModel）：有 {0} 个错误</summary>
    public const string LogsSummaryErrors = "Ui_Logs_SummaryErrors";

    /// <summary>日志页（LogPageViewModel）：有 {0} 个警告</summary>
    public const string LogsSummaryWarnings = "Ui_Logs_SummaryWarnings";

    /// <summary>日志页（LogPageViewModel）：没有新的错误或警告</summary>
    public const string LogsSummaryClean = "Ui_Logs_SummaryClean";

    /// <summary>日志页（LogPageViewModel）：当前过滤不命中，试试恢复默认。</summary>
    public const string LogsEmptyFiltered = "Ui_Logs_EmptyFiltered";

    /// <summary>日志页（LogPageViewModel）：缓冲区还没有日志，采集到后自动出现。</summary>
    public const string LogsEmptyNoLogs = "Ui_Logs_EmptyNoLogs";

    /// <summary>日志页（LogPageViewModel）：按「级别 + 族 + 消息骨架」折叠，取前 {0} 组</summary>
    public const string LogsAggregatedHint = "Ui_Logs_AggregatedHint";

    /// <summary>日志页（LogPageViewModel）：已按事件顺序展开</summary>
    public const string LogsExpandedHint = "Ui_Logs_ExpandedHint";

    /// <summary>日志页（LogPageViewModel）：日志读取失败：{0}</summary>
    public const string LogsErrorReadFailed = "Ui_Logs_ErrorReadFailed";

    /// <summary>日志页（LogPageViewModel）：已清空缓冲区（序号不回退，旧日志里的证据行引用仍然唯一）。</summary>
    public const string LogsStatusCleared = "Ui_Logs_StatusCleared";

    /// <summary>日志页（LogPageViewModel）：诊断包服务未接线，请让外壳注入 ILogBundleService。</summary>
    public const string LogsErrorNoBundleService = "Ui_Logs_ErrorNoBundleService";

    /// <summary>日志页（LogPageViewModel）：请先选择诊断包的保存位置。</summary>
    public const string LogsErrorNoBundlePath = "Ui_Logs_ErrorNoBundlePath";

    /// <summary>日志页（LogPageViewModel）：导出诊断包失败。</summary>
    public const string LogsErrorBundleFailed = "Ui_Logs_ErrorBundleFailed";

    /// <summary>日志页（LogPageViewModel）：已导出诊断包：{0} 行日志、{1} 条错误，共 {2} 字节。</summary>
    public const string LogsStatusBundleExported = "Ui_Logs_StatusBundleExported";

    /// <summary>日志页（LogPageViewModel）：导出诊断包失败：{0}</summary>
    public const string LogsErrorBundleDetail = "Ui_Logs_ErrorBundleDetail";

    /// <summary>日志页（LogPageViewModel）：请先导出诊断包：AI 分析只读打包并脱敏后的内容，不会直接读本机路径。</summary>
    public const string LogsErrorNeedBundleFirst = "Ui_Logs_ErrorNeedBundleFirst";

    // 跨实例去重页（DedupPageViewModel）

    /// <summary>跨实例去重页（DedupPageViewModel）：没有发现可去重的重复内容</summary>
    public const string DedupEmptyTitle = "Ui_Dedup_EmptyTitle";

    /// <summary>跨实例去重页（DedupPageViewModel）：没有发现可合并的重复文件。</summary>
    public const string DedupEmptyHint = "Ui_Dedup_EmptyHint";

    /// <summary>跨实例去重页（DedupPageViewModel）：当前平台不支持创建硬链接，只能查看重复内容，不能执行去重。</summary>
    public const string DedupUnsupportedHint = "Ui_Dedup_UnsupportedHint";

    /// <summary>跨实例去重页（DedupPageViewModel）：重复：{0} 组、{1} 个文件，可释放 {2}；跳过 {3} 项</summary>
    public const string DedupStatsText = "Ui_Dedup_StatsText";

    /// <summary>跨实例去重页（DedupPageViewModel）：释放前 {0}</summary>
    public const string DedupOccupiedBefore = "Ui_Dedup_OccupiedBefore";

    /// <summary>跨实例去重页（DedupPageViewModel）：释放后 {0}</summary>
    public const string DedupOccupiedAfter = "Ui_Dedup_OccupiedAfter";

    /// <summary>跨实例去重页（DedupPageViewModel）：可释放 {0}%</summary>
    public const string DedupReclaimPercent = "Ui_Dedup_ReclaimPercent";

    /// <summary>跨实例去重页（DedupPageViewModel）：已选 {0} / {1} 个实例</summary>
    public const string DedupSelectionText = "Ui_Dedup_SelectionText";

    /// <summary>跨实例去重页（DedupPageViewModel）：预演去重</summary>
    public const string DedupExecuteDryRun = "Ui_Dedup_ExecuteDryRun";

    /// <summary>跨实例去重页（DedupPageViewModel）：执行去重</summary>
    public const string DedupExecuteReal = "Ui_Dedup_ExecuteReal";

    /// <summary>跨实例去重页（DedupPageViewModel）：预演：只列出改动，不写盘。</summary>
    public const string DedupDryRunHint = "Ui_Dedup_DryRunHint";

    /// <summary>跨实例去重页（DedupPageViewModel）：写盘：改成硬链接，多实例共用数据。</summary>
    public const string DedupWriteHint = "Ui_Dedup_WriteHint";

    /// <summary>跨实例去重页（DedupPageViewModel）：预演不会写盘，可以直接执行。</summary>
    public const string DedupConfirmDryRunHint = "Ui_Dedup_ConfirmDryRunHint";

    /// <summary>跨实例去重页（DedupPageViewModel）：将把 {0} 个文件改成硬链接，可释放 {1}。硬链接后多实例共用同一份数据。</summary>
    public const string DedupConfirmWriteHint = "Ui_Dedup_ConfirmWriteHint";

    /// <summary>跨实例去重页（DedupPageViewModel）：扫描失败。</summary>
    public const string DedupErrorScanFailed = "Ui_Dedup_ErrorScanFailed";

    /// <summary>跨实例去重页（DedupPageViewModel）：正在扫描重复内容…</summary>
    public const string DedupBusyScanning = "Ui_Dedup_BusyScanning";

    /// <summary>跨实例去重页（DedupPageViewModel）：已取消执行，未改动磁盘。</summary>
    public const string DedupStatusCancelled = "Ui_Dedup_StatusCancelled";

    /// <summary>跨实例去重页（DedupPageViewModel）：去重失败。</summary>
    public const string DedupErrorExecuteFailed = "Ui_Dedup_ErrorExecuteFailed";

    /// <summary>跨实例去重页（DedupPageViewModel）：正在预演去重…</summary>
    public const string DedupBusyDryRunning = "Ui_Dedup_BusyDryRunning";

    /// <summary>跨实例去重页（DedupPageViewModel）：正在建立硬链接…</summary>
    public const string DedupBusyLinking = "Ui_Dedup_BusyLinking";

    /// <summary>跨实例去重页（DedupPageViewModel）：执行跨实例去重</summary>
    public const string DedupConfirmTitle = "Ui_Dedup_ConfirmTitle";

    /// <summary>跨实例去重页（DedupPageViewModel）：即将把 {0} 个文件改成硬链接，可释放 {1}。</summary>
    public const string DedupConfirmBody = "Ui_Dedup_ConfirmBody";

    /// <summary>跨实例去重页（DedupPageViewModel）：只会处理 GameData 内、非受管、非符号链接、同卷且至少两份内容相同的文件。</summary>
    public const string DedupConfirmHint = "Ui_Dedup_ConfirmHint";

    /// <summary>跨实例去重页（DedupPageViewModel）：再次确认去重</summary>
    public const string DedupSecondConfirmTitle = "Ui_Dedup_SecondConfirmTitle";

    /// <summary>跨实例去重页（DedupPageViewModel）：硬链接后多个实例共用同一份数据，删除或改动任一实例的文件都会影响其它实例。确定继续？</summary>
    public const string DedupSecondConfirmBody = "Ui_Dedup_SecondConfirmBody";

    /// <summary>跨实例去重页（DedupPageViewModel）：内容不会改变，但这份文件不再是独立副本；CKAN 卸载某个实例的模组时会删掉受管文件——受管路径已被排除，所以不会被本操作牵连。</summary>
    public const string DedupSecondConfirmHint = "Ui_Dedup_SecondConfirmHint";

    /// <summary>跨实例去重页（DedupPageViewModel）：扫描完成：{0} 个实例、{1} 组重复内容，可释放 {2}；跳过 {3} 项。</summary>
    public const string DedupStatusScanned = "Ui_Dedup_StatusScanned";

    /// <summary>跨实例去重页（DedupPageViewModel）：扫描完成：{0} 个实例，没有发现重复内容。</summary>
    public const string DedupStatusScannedClean = "Ui_Dedup_StatusScannedClean";

    /// <summary>跨实例去重页（DedupPageViewModel）：预演完成：将把 {0} 个文件改成硬链接，可释放 {1}；跳过 {2} 项。未改动磁盘。</summary>
    public const string DedupStatusPreviewed = "Ui_Dedup_StatusPreviewed";

    /// <summary>跨实例去重页（DedupPageViewModel）：已建立 {0} 个硬链接，释放 {1}；失败 {2} 个、回滚 {3} 个。重新扫描可查看剩余重复。</summary>
    public const string DedupStatusLinkedWithFailures = "Ui_Dedup_StatusLinkedWithFailures";

    /// <summary>跨实例去重页（DedupPageViewModel）：已建立 {0} 个硬链接，释放 {1}。重新扫描可查看剩余重复。</summary>
    public const string DedupStatusLinked = "Ui_Dedup_StatusLinked";

    /// <summary>跨实例去重页（DedupPageViewModel）：去重操作失败。</summary>
    public const string DedupErrorOperation = "Ui_Dedup_ErrorOperation";

    // 下载缓存页（CachePageViewModel）

    /// <summary>下载缓存页（CachePageViewModel）：未设上限</summary>
    public const string CacheNoLimit = "Ui_Cache_NoLimit";

    /// <summary>下载缓存页（CachePageViewModel）：上限 {0}  ·  已用 {1}</summary>
    public const string CacheCapacityText = "Ui_Cache_CapacityText";

    /// <summary>下载缓存页（CachePageViewModel）：缓存是空的</summary>
    public const string CacheEntriesEmpty = "Ui_Cache_EntriesEmpty";

    /// <summary>下载缓存页（CachePageViewModel）：共 {0} 个条目</summary>
    public const string CacheEntriesCount = "Ui_Cache_EntriesCount";

    /// <summary>下载缓存页（CachePageViewModel）：未选中条目</summary>
    public const string CacheNoSelection = "Ui_Cache_NoSelection";

    /// <summary>下载缓存页（CachePageViewModel）：已选中 {0} 个条目</summary>
    public const string CacheSelectionText = "Ui_Cache_SelectionText";

    /// <summary>下载缓存页（CachePageViewModel）：正在读取下载缓存…</summary>
    public const string CacheBusyLoading = "Ui_Cache_BusyLoading";

    /// <summary>下载缓存页（CachePageViewModel）：按上限清理下载缓存失败。</summary>
    public const string CacheErrorPurgeToLimit = "Ui_Cache_ErrorPurgeToLimit";

    /// <summary>下载缓存页（CachePageViewModel）：正在清理下载缓存…</summary>
    public const string CacheBusyTrimming = "Ui_Cache_BusyTrimming";

    /// <summary>下载缓存页（CachePageViewModel）：清空下载缓存</summary>
    public const string CachePurgeTitle = "Ui_Cache_PurgeTitle";

    /// <summary>下载缓存页（CachePageViewModel）：将删除 {0} 个缓存条目（{1}）。</summary>
    public const string CachePurgeBody = "Ui_Cache_PurgeBody";

    /// <summary>下载缓存页（CachePageViewModel）：正在被已安装模组使用的条目会被跳过，不会影响游戏运行。</summary>
    public const string CachePurgeHint = "Ui_Cache_PurgeHint";

    /// <summary>下载缓存页（CachePageViewModel）：已取消。</summary>
    public const string CacheStatusCancelled = "Ui_Cache_StatusCancelled";

    /// <summary>下载缓存页（CachePageViewModel）：清空下载缓存失败。</summary>
    public const string CacheErrorPurgeAll = "Ui_Cache_ErrorPurgeAll";

    /// <summary>下载缓存页（CachePageViewModel）：正在清空下载缓存…</summary>
    public const string CacheBusyPurgingAll = "Ui_Cache_BusyPurgingAll";

    /// <summary>下载缓存页（CachePageViewModel）：当前宿主没有提供文件选择能力，无法从 ZIP 灌入。</summary>
    public const string CacheErrorNoFilePicker = "Ui_Cache_ErrorNoFilePicker";

    /// <summary>下载缓存页（CachePageViewModel）：从 ZIP 灌入下载缓存失败。</summary>
    public const string CacheErrorImportZip = "Ui_Cache_ErrorImportZip";

    /// <summary>下载缓存页（CachePageViewModel）：已灌入 {0}（{1}）。</summary>
    public const string CacheStatusImported = "Ui_Cache_StatusImported";

    /// <summary>下载缓存页（CachePageViewModel）：安装模组</summary>
    public const string CacheInstallModTitle = "Ui_Cache_InstallModTitle";

    /// <summary>下载缓存页（CachePageViewModel）：已成功将 {0} 导入缓存。是否立即前往变更集安装此模组？</summary>
    public const string CacheInstallModBody = "Ui_Cache_InstallModBody";

    /// <summary>下载缓存页（CachePageViewModel）：正在校验并复制 ZIP…</summary>
    public const string CacheBusyImportingZip = "Ui_Cache_BusyImportingZip";

    /// <summary>下载缓存页（CachePageViewModel）：未选中条目。</summary>
    public const string CacheStatusNoSelection = "Ui_Cache_StatusNoSelection";

    /// <summary>下载缓存页（CachePageViewModel）：删除缓存条目</summary>
    public const string CacheDeleteTitle = "Ui_Cache_DeleteTitle";

    /// <summary>下载缓存页（CachePageViewModel）：将删除选中的 {0} 个条目。</summary>
    public const string CacheDeleteBody = "Ui_Cache_DeleteBody";

    /// <summary>下载缓存页（CachePageViewModel）：正在被已安装模组使用的条目无法删除。</summary>
    public const string CacheDeleteHint = "Ui_Cache_DeleteHint";

    /// <summary>下载缓存页（CachePageViewModel）：删除失败。</summary>
    public const string CacheErrorDelete = "Ui_Cache_ErrorDelete";

    /// <summary>下载缓存页（CachePageViewModel）：有 {0} 个条目没能删除：{1}</summary>
    public const string CacheErrorDeletePartial = "Ui_Cache_ErrorDeletePartial";

    /// <summary>下载缓存页（CachePageViewModel）：已删除 {0} 个条目。</summary>
    public const string CacheStatusDeleted = "Ui_Cache_StatusDeleted";

    /// <summary>下载缓存页（CachePageViewModel）：正在删除缓存条目…</summary>
    public const string CacheBusyDeleting = "Ui_Cache_BusyDeleting";

    /// <summary>下载缓存页（CachePageViewModel）：当前宿主没有提供打开目录的外壳能力。</summary>
    public const string CacheErrorNoShell = "Ui_Cache_ErrorNoShell";

    /// <summary>下载缓存页（CachePageViewModel）：下载缓存目录为空，无法打开。</summary>
    public const string CacheErrorDirEmpty = "Ui_Cache_ErrorDirEmpty";

    /// <summary>下载缓存页（CachePageViewModel）：未配置下载缓存目录且无法获取默认目录。</summary>
    public const string CacheErrorDirUnset = "Ui_Cache_ErrorDirUnset";

    /// <summary>下载缓存页（CachePageViewModel）：已打开下载缓存目录。</summary>
    public const string CacheStatusDirOpened = "Ui_Cache_StatusDirOpened";

    /// <summary>下载缓存页（CachePageViewModel）：打开下载缓存目录失败。</summary>
    public const string CacheErrorOpenDir = "Ui_Cache_ErrorOpenDir";

    /// <summary>下载缓存页（CachePageViewModel）：正在打开缓存目录…</summary>
    public const string CacheBusyOpeningDir = "Ui_Cache_BusyOpeningDir";

    /// <summary>下载缓存页（CachePageViewModel）：重置为默认推荐上限（2048 MiB）</summary>
    public const string CacheResetToDefault = "Ui_Cache_ResetToDefault";

    /// <summary>下载缓存页（CachePageViewModel）：设为无限制</summary>
    public const string CacheSetUnlimited = "Ui_Cache_SetUnlimited";

    /// <summary>下载缓存页（CachePageViewModel）：重置容量上限</summary>
    public const string CacheResetLimitTitle = "Ui_Cache_ResetLimitTitle";

    /// <summary>下载缓存页（CachePageViewModel）：请选择如何重置下载缓存容量上限：</summary>
    public const string CacheResetLimitPrompt = "Ui_Cache_ResetLimitPrompt";

    /// <summary>下载缓存页（CachePageViewModel）：重置缓存容量上限失败。</summary>
    public const string CacheErrorResetLimit = "Ui_Cache_ErrorResetLimit";

    /// <summary>下载缓存页（CachePageViewModel）：已将缓存容量上限重置为 {0}。</summary>
    public const string CacheStatusLimitReset = "Ui_Cache_StatusLimitReset";

    /// <summary>下载缓存页（CachePageViewModel）：已将缓存容量上限设为无限制。</summary>
    public const string CacheStatusLimitUnlimited = "Ui_Cache_StatusLimitUnlimited";

    /// <summary>下载缓存页（CachePageViewModel）：正在重置容量上限…</summary>
    public const string CacheBusyResettingLimit = "Ui_Cache_BusyResettingLimit";

    /// <summary>下载缓存页（CachePageViewModel）：读取下载缓存统计失败。</summary>
    public const string CacheErrorLoadStats = "Ui_Cache_ErrorLoadStats";

    /// <summary>下载缓存页（CachePageViewModel）：读取下载缓存条目失败。</summary>
    public const string CacheErrorLoadEntries = "Ui_Cache_ErrorLoadEntries";

    /// <summary>下载缓存页（CachePageViewModel）：已读取 {0} 个缓存条目。</summary>
    public const string CacheStatusLoaded = "Ui_Cache_StatusLoaded";

    /// <summary>下载缓存页（CachePageViewModel）：下载缓存操作失败。</summary>
    public const string CacheErrorOperation = "Ui_Cache_ErrorOperation";

    // 存档页（SavesPageViewModel）

    /// <summary>存档页（SavesPageViewModel）：当前实例：{0}</summary>
    public const string SavesCurrentInstance = "Ui_Saves_CurrentInstance";

    /// <summary>存档页（SavesPageViewModel）：当前实例：未选择</summary>
    public const string SavesCurrentInstanceNone = "Ui_Saves_CurrentInstanceNone";

    /// <summary>存档页（SavesPageViewModel）：共 {0} 个存档</summary>
    public const string SavesCountText = "Ui_Saves_CountText";

    /// <summary>存档页（SavesPageViewModel）：自带存档（{0}）</summary>
    public const string SavesStockToggle = "Ui_Saves_StockToggle";

    /// <summary>存档页（SavesPageViewModel）：存档都被过滤器收起了</summary>
    public const string SavesStockOnlyTitle = "Ui_Saves_StockOnlyTitle";

    /// <summary>存档页（SavesPageViewModel）：勾上「自带存档」就能看到它们。</summary>
    public const string SavesStockOnlyHint = "Ui_Saves_StockOnlyHint";

    /// <summary>存档页（SavesPageViewModel）：显示自带存档</summary>
    public const string SavesStockOnlyAction = "Ui_Saves_StockOnlyAction";

    /// <summary>存档页（SavesPageViewModel）：这个实例还没有存档</summary>
    public const string SavesEmptyTitle = "Ui_Saves_EmptyTitle";

    /// <summary>存档页（SavesPageViewModel）：在游戏里保存一次就会出现在这里。</summary>
    public const string SavesEmptyHint = "Ui_Saves_EmptyHint";

    /// <summary>存档页（SavesPageViewModel）：打开 saves/ 目录</summary>
    public const string SavesEmptyAction = "Ui_Saves_EmptyAction";

    /// <summary>存档页（SavesPageViewModel）：存档属于某个游戏实例：请先在「实例」页选择或添加一个实例。</summary>
    public const string SavesNoInstanceHint = "Ui_Saves_NoInstanceHint";

    /// <summary>存档页（SavesPageViewModel）：未选择存档</summary>
    public const string SavesNoSelection = "Ui_Saves_NoSelection";

    /// <summary>存档页（SavesPageViewModel）：没有发现问题：结构完整，大括号配对，缩进与换行也合规。</summary>
    public const string SavesDiagnosticsEmpty = "Ui_Saves_DiagnosticsEmpty";

    /// <summary>存档页（SavesPageViewModel）：尚未校验</summary>
    public const string SavesNotValidated = "Ui_Saves_NotValidated";

    /// <summary>存档页（SavesPageViewModel）：发现 {0} 个错误 / {1} 个警告</summary>
    public const string SavesDiagnosticsErrors = "Ui_Saves_DiagnosticsErrors";

    /// <summary>存档页（SavesPageViewModel）：校验通过，{0} 个警告</summary>
    public const string SavesDiagnosticsWarnings = "Ui_Saves_DiagnosticsWarnings";

    /// <summary>存档页（SavesPageViewModel）：校验通过，没有发现问题</summary>
    public const string SavesDiagnosticsPassed = "Ui_Saves_DiagnosticsPassed";

    /// <summary>存档页（SavesPageViewModel）：就地重写存档</summary>
    public const string SavesRewriteTitle = "Ui_Saves_RewriteTitle";

    /// <summary>存档页（SavesPageViewModel）：会先为「{0}」写一份写前快照，再就地重写该文件。内容语义（键、值、节点顺序）保持不变，只有缩进、分隔符与换行会被规范化。确定继续？</summary>
    public const string SavesRewriteBody = "Ui_Saves_RewriteBody";

    /// <summary>存档页（SavesPageViewModel）：已取消就地重写，原文件未改动。</summary>
    public const string SavesStatusRewriteCancelled = "Ui_Saves_StatusRewriteCancelled";

    /// <summary>存档页（SavesPageViewModel）：当前没有选择游戏实例，无法打开 saves/ 目录。</summary>
    public const string SavesErrorNoInstanceForDir = "Ui_Saves_ErrorNoInstanceForDir";

    /// <summary>存档页（SavesPageViewModel）：当前环境不支持打开文件夹（宿主未注入外壳启动器）。</summary>
    public const string SavesErrorNoLauncher = "Ui_Saves_ErrorNoLauncher";

    /// <summary>存档页（SavesPageViewModel）：打开存档目录失败。</summary>
    public const string SavesErrorOpenDir = "Ui_Saves_ErrorOpenDir";

    /// <summary>存档页（SavesPageViewModel）：已在文件管理器中打开：{0}</summary>
    public const string SavesStatusDirOpened = "Ui_Saves_StatusDirOpened";

    /// <summary>存档页（SavesPageViewModel）：就绪</summary>
    public const string SavesStatusReady = "Ui_Saves_StatusReady";

    /// <summary>存档页（SavesPageViewModel）：当前没有选择游戏实例：请先在「实例」页选择或添加一个实例。</summary>
    public const string SavesStatusNoInstance = "Ui_Saves_StatusNoInstance";

    /// <summary>存档页（SavesPageViewModel）：正在读取存档…</summary>
    public const string SavesBusyLoading = "Ui_Saves_BusyLoading";

    /// <summary>存档页（SavesPageViewModel）：读取存档列表失败。</summary>
    public const string SavesErrorLoad = "Ui_Saves_ErrorLoad";

    /// <summary>存档页（SavesPageViewModel）：「{0}」的 saves/ 目录里没有存档。</summary>
    public const string SavesStatusEmpty = "Ui_Saves_StatusEmpty";

    /// <summary>存档页（SavesPageViewModel）：已读取 {0} 个存档。</summary>
    public const string SavesStatusLoaded = "Ui_Saves_StatusLoaded";

    /// <summary>存档页（SavesPageViewModel）：正在校验存档…</summary>
    public const string SavesBusyValidating = "Ui_Saves_BusyValidating";

    /// <summary>存档页（SavesPageViewModel）：校验存档失败。</summary>
    public const string SavesErrorValidate = "Ui_Saves_ErrorValidate";

    /// <summary>存档页（SavesPageViewModel）：正在写前快照并就地重写…</summary>
    public const string SavesBusyRewriting = "Ui_Saves_BusyRewriting";

    /// <summary>存档页（SavesPageViewModel）：正在写出格式化副本…</summary>
    public const string SavesBusyWritingCopy = "Ui_Saves_BusyWritingCopy";

    /// <summary>存档页（SavesPageViewModel）：格式化存档失败。</summary>
    public const string SavesErrorFormat = "Ui_Saves_ErrorFormat";

    /// <summary>存档页（SavesPageViewModel）：格式化被拒绝，原文件未改动。</summary>
    public const string SavesErrorRefused = "Ui_Saves_ErrorRefused";

    /// <summary>存档页（SavesPageViewModel）：节点 {0} / 键值 {1} / 行 {2} / 最大深度 {3}</summary>
    public const string SavesSelectionStats = "Ui_Saves_SelectionStats";


    /// <summary>存档页（SavesPageViewModel）：重排 {0} 行、补分隔符 {1} 处、统一换行 {2} 处</summary>
    public const string SavesFormatStats = "Ui_Saves_FormatStats";

    /// <summary>存档页（SavesPageViewModel）：格式化完成但未写盘：{0}。</summary>
    public const string SavesStatusNotWritten = "Ui_Saves_StatusNotWritten";

    /// <summary>存档页（SavesPageViewModel）：已写出 {0}（原文件未改动，{1}，{2}）。</summary>
    public const string SavesStatusWrittenCopy = "Ui_Saves_StatusWrittenCopy";

    /// <summary>存档页（SavesPageViewModel）：已就地重写 {0}，写前快照：{1}（{2}，{3}）。</summary>
    public const string SavesStatusRewritten = "Ui_Saves_StatusRewritten";

    // 模组翻译页（ModTextPageViewModel）

    /// <summary>模组翻译页（ModTextPageViewModel）：模组文本</summary>
    public const string ModTextPageName = "Ui_ModText_PageName";

    /// <summary>模组翻译页（ModTextPageViewModel）：正在载入翻译词库...</summary>
    public const string ModTextBusyLoading = "Ui_ModText_BusyLoading";

    /// <summary>模组翻译页（ModTextPageViewModel）：载入完成，共 {0} 条词条。</summary>
    public const string ModTextStatusLoaded = "Ui_ModText_StatusLoaded";

    /// <summary>模组翻译页（ModTextPageViewModel）：载入模组翻译包失败</summary>
    public const string ModTextErrorLoad = "Ui_ModText_ErrorLoad";

    /// <summary>模组翻译页（ModTextPageViewModel）：载入失败: {0}</summary>
    public const string ModTextStatusLoadFailed = "Ui_ModText_StatusLoadFailed";

    /// <summary>模组翻译页（ModTextPageViewModel）：正在切换至语言: {0}...</summary>
    public const string ModTextBusySwitching = "Ui_ModText_BusySwitching";

    /// <summary>模组翻译页（ModTextPageViewModel）：已切换至 {0}，载入 {1} 条词条。</summary>
    public const string ModTextStatusSwitched = "Ui_ModText_StatusSwitched";

    /// <summary>模组翻译页（ModTextPageViewModel）：切换语言 '{0}' 失败</summary>
    public const string ModTextErrorSwitchLanguage = "Ui_ModText_ErrorSwitchLanguage";

    /// <summary>模组翻译页（ModTextPageViewModel）：切换失败: {0}</summary>
    public const string ModTextStatusSwitchFailed = "Ui_ModText_StatusSwitchFailed";

    /// <summary>模组翻译页（ModTextPageViewModel）：已显示 {0} / {1} 条条目</summary>
    public const string ModTextSummaryShown = "Ui_ModText_SummaryShown";

    /// <summary>模组翻译页（ModTextPageViewModel）：正在保存 '{0}' 的人工覆盖翻译...</summary>
    public const string ModTextBusySavingOverride = "Ui_ModText_BusySavingOverride";

    /// <summary>模组翻译页（ModTextPageViewModel）：保存成功！已热更新至 overrides.jsonl 并生效。</summary>
    public const string ModTextStatusOverrideSaved = "Ui_ModText_StatusOverrideSaved";

    /// <summary>模组翻译页（ModTextPageViewModel）：保存人工覆盖翻译失败</summary>
    public const string ModTextErrorSaveOverride = "Ui_ModText_ErrorSaveOverride";

    /// <summary>模组翻译页（ModTextPageViewModel）：保存失败: {0}</summary>
    public const string ModTextStatusSaveFailed = "Ui_ModText_StatusSaveFailed";

    /// <summary>模组翻译页（ModTextPageViewModel）：正在删除 '{0}' 的人工覆盖翻译...</summary>
    public const string ModTextBusyDeletingOverride = "Ui_ModText_BusyDeletingOverride";

    /// <summary>模组翻译页（ModTextPageViewModel）：已成功从 overrides.jsonl 删除覆盖项并生效。</summary>
    public const string ModTextStatusOverrideDeleted = "Ui_ModText_StatusOverrideDeleted";

    /// <summary>模组翻译页（ModTextPageViewModel）：删除人工覆盖翻译失败</summary>
    public const string ModTextErrorDeleteOverride = "Ui_ModText_ErrorDeleteOverride";

    /// <summary>模组翻译页（ModTextPageViewModel）：删除失败: {0}</summary>
    public const string ModTextStatusDeleteFailed = "Ui_ModText_StatusDeleteFailed";

    /// <summary>模组翻译页（ModTextPageViewModel）：正在从 UniversalI18n 仓库同步翻译词库...</summary>
    public const string ModTextBusySyncing = "Ui_ModText_BusySyncing";

    /// <summary>模组翻译页（ModTextPageViewModel）：同步 UniversalI18n 失败</summary>
    public const string ModTextErrorSync = "Ui_ModText_ErrorSync";

    /// <summary>模组翻译页（ModTextPageViewModel）：同步失败: {0}</summary>
    public const string ModTextStatusSyncFailed = "Ui_ModText_StatusSyncFailed";

    /// <summary>模组翻译页（ModTextPageViewModel）：同步成功！已同步 {0} 个词库文件并实时重载。</summary>
    public const string ModTextStatusSynced = "Ui_ModText_StatusSynced";

    /// <summary>模组翻译页（ModTextPageViewModel）：打开翻译文件目录失败</summary>
    public const string ModTextErrorOpenFolder = "Ui_ModText_ErrorOpenFolder";

    // modpack 编辑器页（ModPackEditorPageViewModel）

    /// <summary>modpack 编辑器页（ModPackEditorPageViewModel）：填写表单后点「校验」，或直接「保存」。</summary>
    public const string ModPackEditorStatusReady = "Ui_ModPackEditor_StatusReady";

    /// <summary>modpack 编辑器页（ModPackEditorPageViewModel）：共 {0} 条问题</summary>
    public const string ModPackEditorIssueSummary = "Ui_ModPackEditor_IssueSummary";

    /// <summary>modpack 编辑器页（ModPackEditorPageViewModel）：作者</summary>
    public const string ModPackEditorLabelAuthor = "Ui_ModPackEditor_LabelAuthor";

    /// <summary>modpack 编辑器页（ModPackEditorPageViewModel）：许可证</summary>
    public const string ModPackEditorLabelLicense = "Ui_ModPackEditor_LabelLicense";

    /// <summary>modpack 编辑器页（ModPackEditorPageViewModel）：读取 .ckan 失败。</summary>
    public const string ModPackEditorErrorRead = "Ui_ModPackEditor_ErrorRead";

    /// <summary>modpack 编辑器页（ModPackEditorPageViewModel）：已读取：{0}</summary>
    public const string ModPackEditorStatusRead = "Ui_ModPackEditor_StatusRead";

    /// <summary>modpack 编辑器页（ModPackEditorPageViewModel）：正在读取…</summary>
    public const string ModPackEditorBusyReading = "Ui_ModPackEditor_BusyReading";

    /// <summary>modpack 编辑器页（ModPackEditorPageViewModel）：校验未通过，没有保存。请按下面列出的原因逐条修正。</summary>
    public const string ModPackEditorErrorValidationBlocked = "Ui_ModPackEditor_ErrorValidationBlocked";

    /// <summary>modpack 编辑器页（ModPackEditorPageViewModel）：保存 .ckan 失败。</summary>
    public const string ModPackEditorErrorSave = "Ui_ModPackEditor_ErrorSave";

    /// <summary>modpack 编辑器页（ModPackEditorPageViewModel）：已保存：{0}</summary>
    public const string ModPackEditorStatusSaved = "Ui_ModPackEditor_StatusSaved";

    /// <summary>modpack 编辑器页（ModPackEditorPageViewModel）：正在保存…</summary>
    public const string ModPackEditorBusySaving = "Ui_ModPackEditor_BusySaving";

    /// <summary>modpack 编辑器页（ModPackEditorPageViewModel）：校验通过，可以保存。</summary>
    public const string ModPackEditorStatusValidationPassed = "Ui_ModPackEditor_StatusValidationPassed";

    /// <summary>modpack 编辑器页（ModPackEditorPageViewModel）：校验发现 {0} 个错误，修好后才能保存。</summary>
    public const string ModPackEditorStatusValidationErrors = "Ui_ModPackEditor_StatusValidationErrors";

    /// <summary>modpack 编辑器页（ModPackEditorPageViewModel）：已清空表单，填写标识与名称后保存。</summary>
    public const string ModPackEditorStatusCleared = "Ui_ModPackEditor_StatusCleared";

    /// <summary>modpack 编辑器页（ModPackEditorPageViewModel）：已添加一条依赖关系，填写模组标识。</summary>
    public const string ModPackEditorStatusDependencyAdded = "Ui_ModPackEditor_StatusDependencyAdded";

    // Mod 配置页（ModConfigPageViewModel）

    /// <summary>Mod 配置页：mod 卡片数量文案（没有可编辑 mod 时）。</summary>
    public const string ModConfigModCountEmpty = "Ui_ModConfig_ModCountEmpty";


    /// <summary>Mod 配置页：mod 卡片数量文案（含有适配器的 mod 时）。</summary>
    public const string ModConfigModCount = "Ui_ModConfig_ModCount";


    /// <summary>Mod 配置页：字段搜索框占位文案。</summary>
    public const string ModConfigSearchPlaceholder = "Ui_ModConfig_SearchPlaceholder";


    /// <summary>Mod 配置页：字段筛选后的可见条数。</summary>
    public const string ModConfigVisibleCount = "Ui_ModConfig_VisibleCount";


    /// <summary>Mod 配置页：选中 mod 的改动条数标记。</summary>
    public const string ModConfigModifiedCount = "Ui_ModConfig_ModifiedCount";


    /// <summary>Mod 配置页：跨 mod 累计的待保存条数标记。</summary>
    public const string ModConfigPendingCount = "Ui_ModConfig_PendingCount";


    /// <summary>Mod 配置页：有快照时的回滚按钮文案。</summary>
    public const string ModConfigRestoreButtonWithCount = "Ui_ModConfig_RestoreButtonWithCount";


    /// <summary>Mod 配置页：快照文件清单说明。</summary>
    public const string ModConfigBackupHint = "Ui_ModConfig_BackupHint";


    /// <summary>Mod 配置页：适配器清单标题。</summary>
    public const string ModConfigAdapterListTitle = "Ui_ModConfig_AdapterListTitle";


    /// <summary>Mod 配置页：状态栏：放弃未保存改动。</summary>
    public const string ModConfigStatusDiscarded = "Ui_ModConfig_StatusDiscarded";


    /// <summary>Mod 配置页：回滚快照的确认正文。</summary>
    public const string ModConfigRestoreConfirmBody = "Ui_ModConfig_RestoreConfirmBody";


    /// <summary>Mod 配置页：错误栏：回滚部分失败。</summary>
    public const string ModConfigErrorRestorePartial = "Ui_ModConfig_ErrorRestorePartial";


    /// <summary>Mod 配置页：状态栏：回滚全部成功。</summary>
    public const string ModConfigStatusRestored = "Ui_ModConfig_StatusRestored";


    /// <summary>Mod 配置页：状态栏：重载适配器清单。</summary>
    public const string ModConfigStatusAdaptersReloaded = "Ui_ModConfig_StatusAdaptersReloaded";


    /// <summary>Mod 配置页：错误栏：选包异常。</summary>
    public const string ModConfigErrorPickAdapter = "Ui_ModConfig_ErrorPickAdapter";


    /// <summary>Mod 配置页：状态栏：覆盖安装 adapter。</summary>
    public const string ModConfigStatusAdapterReplaced = "Ui_ModConfig_StatusAdapterReplaced";


    /// <summary>Mod 配置页：状态栏：新增导入 adapter。</summary>
    public const string ModConfigStatusAdapterImported = "Ui_ModConfig_StatusAdapterImported";


    /// <summary>Mod 配置页：错误栏：导入 adapter 异常。</summary>
    public const string ModConfigErrorImportDetail = "Ui_ModConfig_ErrorImportDetail";


    /// <summary>Mod 配置页：页面顶部实例摘要（含实例名）。</summary>
    public const string ModConfigInstanceSummary = "Ui_ModConfig_InstanceSummary";


    /// <summary>Mod 配置页：页面顶部实例摘要（没有实例名）。</summary>
    public const string ModConfigGameDirSummary = "Ui_ModConfig_GameDirSummary";


    /// <summary>Mod 配置页：状态栏：配置读取完成。</summary>
    public const string ModConfigStatusLoaded = "Ui_ModConfig_StatusLoaded";


    /// <summary>Mod 配置页：KSP 运行中写盘的确认正文。</summary>
    public const string ModConfigKspRunningConfirmBody = "Ui_ModConfig_KspRunningConfirmBody";


    /// <summary>Mod 配置页：高风险字段二次确认正文。</summary>
    public const string ModConfigConfirmHighRiskBody = "Ui_ModConfig_ConfirmHighRiskBody";


    /// <summary>Mod 配置页：状态栏：保存成功摘要。</summary>
    public const string ModConfigStatusSaved = "Ui_ModConfig_StatusSaved";


    /// <summary>Mod 配置页：错误栏：保存部分失败。</summary>
    public const string ModConfigErrorSavePartial = "Ui_ModConfig_ErrorSavePartial";

    // 模组包页（ModPacksPageViewModel）

    /// <summary>模组包页（ModPacksPageViewModel）：选择 .ckan 文件预览导入，或把当前实例的模组列表导出成 .ckan。</summary>
    public const string ModPacksStatusReady = "Ui_ModPacks_StatusReady";

    /// <summary>模组包页（ModPacksPageViewModel）：当前实例：{0}</summary>
    public const string ModPacksCurrentInstance = "Ui_ModPacks_CurrentInstance";

    /// <summary>模组包页（ModPacksPageViewModel）：当前没有选中的游戏实例，导出与导入预览都不可用。</summary>
    public const string ModPacksNoInstanceHint = "Ui_ModPacks_NoInstanceHint";

    /// <summary>模组包页（ModPacksPageViewModel）：共 {0} 条，可导入 {1} 条</summary>
    public const string ModPacksPreviewCount = "Ui_ModPacks_PreviewCount";

    /// <summary>模组包页（ModPacksPageViewModel）：已勾选 {0} 条</summary>
    public const string ModPacksSelectionCount = "Ui_ModPacks_SelectionCount";

    /// <summary>模组包页（ModPacksPageViewModel）：还没有导入预览</summary>
    public const string ModPacksPreviewEmptyTitle = "Ui_ModPacks_PreviewEmptyTitle";

    /// <summary>模组包页（ModPacksPageViewModel）：每行填一个 .ckan 路径，再点「预览导入」。</summary>
    public const string ModPacksPreviewEmptyHint = "Ui_ModPacks_PreviewEmptyHint";

    /// <summary>模组包页（ModPacksPageViewModel）：两个实例的模组列表一致。</summary>
    public const string ModPacksDiffIdentical = "Ui_ModPacks_DiffIdentical";

    /// <summary>模组包页（ModPacksPageViewModel）：共 {0} 处差异</summary>
    public const string ModPacksDiffCount = "Ui_ModPacks_DiffCount";

    /// <summary>模组包页（ModPacksPageViewModel）：还没有对比结果</summary>
    public const string ModPacksDiffEmptyTitle = "Ui_ModPacks_DiffEmptyTitle";

    /// <summary>模组包页（ModPacksPageViewModel）：选两个实例后点「开始对比」，只读取。</summary>
    public const string ModPacksDiffEmptyHint = "Ui_ModPacks_DiffEmptyHint";

    /// <summary>模组包页（ModPacksPageViewModel）：当前没有选中的游戏实例，请先选择一个实例。</summary>
    public const string ModPacksErrorNoInstance = "Ui_ModPacks_ErrorNoInstance";

    /// <summary>模组包页（ModPacksPageViewModel）：请填写导出文件的目标路径。</summary>
    public const string ModPacksErrorExportPathRequired = "Ui_ModPacks_ErrorExportPathRequired";

    /// <summary>模组包页（ModPacksPageViewModel）：正在导出模组列表…</summary>
    public const string ModPacksBusyExporting = "Ui_ModPacks_BusyExporting";

    /// <summary>模组包页（ModPacksPageViewModel）：导出完成。</summary>
    public const string ModPacksStatusExportDone = "Ui_ModPacks_StatusExportDone";

    /// <summary>模组包页（ModPacksPageViewModel）：已导出 {0} 个模组。</summary>
    public const string ModPacksStatusExported = "Ui_ModPacks_StatusExported";

    /// <summary>模组包页（ModPacksPageViewModel）：导出模组列表失败。</summary>
    public const string ModPacksErrorExportFailed = "Ui_ModPacks_ErrorExportFailed";

    /// <summary>模组包页（ModPacksPageViewModel）：请填写至少一个 .ckan 文件路径（每行一个）。</summary>
    public const string ModPacksErrorNoPaths = "Ui_ModPacks_ErrorNoPaths";

    /// <summary>模组包页（ModPacksPageViewModel）：导入预览失败。</summary>
    public const string ModPacksErrorPreviewFailed = "Ui_ModPacks_ErrorPreviewFailed";

    /// <summary>模组包页（ModPacksPageViewModel）：预览完成：没有可导入的条目。</summary>
    public const string ModPacksStatusPreviewEmpty = "Ui_ModPacks_StatusPreviewEmpty";

    /// <summary>模组包页（ModPacksPageViewModel）：预览完成：共 {0} 条。</summary>
    public const string ModPacksStatusPreviewCount = "Ui_ModPacks_StatusPreviewCount";

    /// <summary>模组包页（ModPacksPageViewModel）：正在解析 .ckan 文件…</summary>
    public const string ModPacksBusyParsing = "Ui_ModPacks_BusyParsing";

    /// <summary>模组包页（ModPacksPageViewModel）：请先预览要导入的 .ckan 文件。</summary>
    public const string ModPacksErrorPreviewRequired = "Ui_ModPacks_ErrorPreviewRequired";

    /// <summary>模组包页（ModPacksPageViewModel）：请至少勾选一条可导入的条目。</summary>
    public const string ModPacksErrorNoSelection = "Ui_ModPacks_ErrorNoSelection";

    /// <summary>模组包页（ModPacksPageViewModel）：正在导入模组…</summary>
    public const string ModPacksBusyImporting = "Ui_ModPacks_BusyImporting";

    /// <summary>模组包页（ModPacksPageViewModel）：导入失败。</summary>
    public const string ModPacksErrorImportFailed = "Ui_ModPacks_ErrorImportFailed";

    /// <summary>模组包页（ModPacksPageViewModel）：导入结束。</summary>
    public const string ModPacksStatusImportDone = "Ui_ModPacks_StatusImportDone";

    /// <summary>模组包页（ModPacksPageViewModel）：导入完成：{0} 个模组已安装。</summary>
    public const string ModPacksStatusImported = "Ui_ModPacks_StatusImported";

    /// <summary>模组包页（ModPacksPageViewModel）：导入结束：成功 {0} 个，失败 {1} 个。</summary>
    public const string ModPacksStatusImportedPartial = "Ui_ModPacks_StatusImportedPartial";

    /// <summary>模组包页（ModPacksPageViewModel）：请选择要对比的两个实例。</summary>
    public const string ModPacksErrorCompareNeedTwo = "Ui_ModPacks_ErrorCompareNeedTwo";

    /// <summary>模组包页（ModPacksPageViewModel）：请选择两个不同的实例进行对比。</summary>
    public const string ModPacksErrorCompareSame = "Ui_ModPacks_ErrorCompareSame";

    /// <summary>模组包页（ModPacksPageViewModel）：实例对比失败。</summary>
    public const string ModPacksErrorCompareFailed = "Ui_ModPacks_ErrorCompareFailed";

    /// <summary>模组包页（ModPacksPageViewModel）：对比完成：两个实例的模组列表一致。</summary>
    public const string ModPacksStatusCompareIdentical = "Ui_ModPacks_StatusCompareIdentical";

    /// <summary>模组包页（ModPacksPageViewModel）：对比完成：共 {0} 处差异。</summary>
    public const string ModPacksStatusCompareDiff = "Ui_ModPacks_StatusCompareDiff";

    /// <summary>模组包页（ModPacksPageViewModel）：正在对比两个实例…</summary>
    public const string ModPacksBusyComparing = "Ui_ModPacks_BusyComparing";

    /// <summary>模组包页（ModPacksPageViewModel）：读取实例列表失败。</summary>
    public const string ModPacksErrorInstancesFailed = "Ui_ModPacks_ErrorInstancesFailed";

    /// <summary>模组包页（ModPacksPageViewModel）：已读取 {0} 个实例。</summary>
    public const string ModPacksStatusInstances = "Ui_ModPacks_StatusInstances";

    /// <summary>模组包页（ModPacksPageViewModel）：正在读取实例列表…</summary>
    public const string ModPacksBusyInstances = "Ui_ModPacks_BusyInstances";

    /// <summary>设置页（SettingsPageViewModel）：尚未读取设置。</summary>
    public const string SettingsStatusNotLoaded = "Ui_Settings_Status_NotLoaded";

    /// <summary>设置页（SettingsPageViewModel）：跟随系统。</summary>
    public const string SettingsChoiceLocaleFollowSystem = "Ui_Settings_Choice_Locale_FollowSystem";

    /// <summary>设置页（SettingsPageViewModel）：系统已开启高对比度：主题、强调色与背景由系统接管，这里的改动不会生效。。</summary>
    public const string SettingsNoticeHighContrastLock = "Ui_Settings_Notice_HighContrastLock";

    /// <summary>设置页（SettingsPageViewModel）：没有需要保存的修改。</summary>
    public const string SettingsStatusNoChanges = "Ui_Settings_Status_NoChanges";

    /// <summary>设置页（SettingsPageViewModel）：已放弃未保存的修改。</summary>
    public const string SettingsStatusReverted = "Ui_Settings_Status_Reverted";

    /// <summary>设置页（SettingsPageViewModel）：没有可重试的失败项。</summary>
    public const string SettingsStatusNoRetryable = "Ui_Settings_Status_NoRetryable";

    /// <summary>设置页（SettingsPageViewModel）：已重试 {0} 项。</summary>
    public const string SettingsStatusRetried = "Ui_Settings_Status_Retried";

    /// <summary>设置页（SettingsPageViewModel）：读取设置失败。</summary>
    public const string SettingsErrorLoadFailed = "Ui_Settings_Error_LoadFailed";

    /// <summary>设置页（SettingsPageViewModel）：已读取 {0} 项设置、{1} 条令牌。</summary>
    public const string SettingsStatusLoaded = "Ui_Settings_Status_Loaded";

    /// <summary>设置页（SettingsPageViewModel）：{0}：请先修正输入。</summary>
    public const string SettingsStatusNeedsFix = "Ui_Settings_Status_NeedsFix";

    /// <summary>设置页（SettingsPageViewModel）：{0}：系统高对比度已接管外观。</summary>
    public const string SettingsStatusHighContrastOwned = "Ui_Settings_Status_HighContrastOwned";

    /// <summary>设置页（SettingsPageViewModel）：{0}：没有修改。</summary>
    public const string SettingsStatusNoChange = "Ui_Settings_Status_NoChange";

    /// <summary>设置页（SettingsPageViewModel）：保存失败。</summary>
    public const string SettingsErrorSaveFailed = "Ui_Settings_Error_SaveFailed";

    /// <summary>设置页（SettingsPageViewModel）：未知的保存去向。</summary>
    public const string SettingsErrorUnknownPersistence = "Ui_Settings_Error_UnknownPersistence";

    /// <summary>设置页（SettingsPageViewModel）：{0}：保存失败。</summary>
    public const string SettingsStatusItemSaveFailed = "Ui_Settings_Status_ItemSaveFailed";

    /// <summary>设置页（SettingsPageViewModel）：设置项标题「开发版更新通道」。</summary>
    public const string SettingsItemDevBuilds = "Ui_Settings_Item_DevBuilds";

    /// <summary>设置页（SettingsPageViewModel）：设置项「开发版更新通道」的说明文案。</summary>
    public const string SettingsItemDevBuildsDesc = "Ui_Settings_Item_DevBuilds_Desc";

    /// <summary>设置页（SettingsPageViewModel）：立即检查更新。</summary>
    public const string SettingsAuxiliaryCheckUpdates = "Ui_Settings_Auxiliary_CheckUpdates";

    /// <summary>设置页（SettingsPageViewModel）：分组标题「通用」。</summary>
    public const string SettingsSectionGeneral = "Ui_Settings_Section_General";

    /// <summary>设置页（SettingsPageViewModel）：分组「通用」的说明文案。</summary>
    public const string SettingsSectionGeneralDesc = "Ui_Settings_Section_General_Desc";

    /// <summary>设置页（SettingsPageViewModel）：设置项标题「自动打开的实例」。</summary>
    public const string SettingsItemAutoStartInstance = "Ui_Settings_Item_AutoStartInstance";

    /// <summary>设置页（SettingsPageViewModel）：设置项「自动打开的实例」的说明文案。</summary>
    public const string SettingsItemAutoStartInstanceDesc = "Ui_Settings_Item_AutoStartInstance_Desc";

    /// <summary>设置页（SettingsPageViewModel）：不自动打开。</summary>
    public const string SettingsChoiceAutoStartInstanceNone = "Ui_Settings_Choice_AutoStartInstance_None";

    /// <summary>设置页（SettingsPageViewModel）：分组标题「实例」。</summary>
    public const string SettingsSectionInstance = "Ui_Settings_Section_Instance";

    /// <summary>设置页（SettingsPageViewModel）：分组「实例」的说明文案。</summary>
    public const string SettingsSectionInstanceDesc = "Ui_Settings_Section_Instance_Desc";

    /// <summary>设置页（SettingsPageViewModel）：设置项标题「缓存目录」。</summary>
    public const string SettingsItemDownloadCacheDir = "Ui_Settings_Item_DownloadCacheDir";

    /// <summary>设置页（SettingsPageViewModel）：设置项「缓存目录」的说明文案。</summary>
    public const string SettingsItemDownloadCacheDirDesc = "Ui_Settings_Item_DownloadCacheDir_Desc";

    /// <summary>设置页（SettingsPageViewModel）：打开目录。</summary>
    public const string SettingsAuxiliaryOpenFolder = "Ui_Settings_Auxiliary_OpenFolder";

    /// <summary>设置页（SettingsPageViewModel）：分组标题「下载缓存」。</summary>
    public const string SettingsSectionDownloadCache = "Ui_Settings_Section_DownloadCache";

    /// <summary>设置页（SettingsPageViewModel）：分组「下载缓存」的说明文案。</summary>
    public const string SettingsSectionDownloadCacheDesc = "Ui_Settings_Section_DownloadCache_Desc";

    /// <summary>设置页（SettingsPageViewModel）：设置项标题「缓存容量上限（MiB）」。</summary>
    public const string SettingsItemCacheSizeLimit = "Ui_Settings_Item_CacheSizeLimit";

    /// <summary>设置页（SettingsPageViewModel）：设置项「缓存容量上限（MiB）」的说明文案。</summary>
    public const string SettingsItemCacheSizeLimitDesc = "Ui_Settings_Item_CacheSizeLimit_Desc";

    /// <summary>设置页（SettingsPageViewModel）：分组标题「仓库与网络」。</summary>
    public const string SettingsSectionRepository = "Ui_Settings_Section_Repository";

    /// <summary>设置页（SettingsPageViewModel）：分组「仓库与网络」的说明文案。</summary>
    public const string SettingsSectionRepositoryDesc = "Ui_Settings_Section_Repository_Desc";

    /// <summary>设置页（SettingsPageViewModel）：设置项标题「自动刷新间隔（分钟）」。</summary>
    public const string SettingsItemRefreshRate = "Ui_Settings_Item_RefreshRate";

    /// <summary>设置页（SettingsPageViewModel）：设置项「自动刷新间隔（分钟）」的说明文案。</summary>
    public const string SettingsItemRefreshRateDesc = "Ui_Settings_Item_RefreshRate_Desc";

    /// <summary>设置页（SettingsPageViewModel）：设置项标题「首选主机顺序」。</summary>
    public const string SettingsItemPreferredHosts = "Ui_Settings_Item_PreferredHosts";

    /// <summary>设置页（SettingsPageViewModel）：设置项「首选主机顺序」的说明文案。</summary>
    public const string SettingsItemPreferredHostsDesc = "Ui_Settings_Item_PreferredHosts_Desc";

    /// <summary>设置页（SettingsPageViewModel）：分组标题「认证令牌」。</summary>
    public const string SettingsSectionAuthToken = "Ui_Settings_Section_AuthToken";

    /// <summary>设置页（SettingsPageViewModel）：分组「认证令牌」的说明文案。</summary>
    public const string SettingsSectionAuthTokenDesc = "Ui_Settings_Section_AuthToken_Desc";

    /// <summary>设置页（SettingsPageViewModel）：分组标题「外观」。</summary>
    public const string SettingsSectionAppearance = "Ui_Settings_Section_Appearance";

    /// <summary>设置页（SettingsPageViewModel）：分组「外观」的说明文案。</summary>
    public const string SettingsSectionAppearanceDesc = "Ui_Settings_Section_Appearance_Desc";

    /// <summary>设置页（SettingsPageViewModel）：设置项标题「主题」。</summary>
    public const string SettingsItemTheme = "Ui_Settings_Item_Theme";

    /// <summary>设置页（SettingsPageViewModel）：设置项「主题」的说明文案。</summary>
    public const string SettingsItemThemeDesc = "Ui_Settings_Item_Theme_Desc";

    /// <summary>设置页（SettingsPageViewModel）：候选文案「跟随系统」（主题）。</summary>
    public const string SettingsChoiceThemeSystem = "Ui_Settings_Choice_Theme_System";

    /// <summary>设置页（SettingsPageViewModel）：候选文案「浅色」（主题）。</summary>
    public const string SettingsChoiceThemeLight = "Ui_Settings_Choice_Theme_Light";

    /// <summary>设置页（SettingsPageViewModel）：候选文案「深色」（主题）。</summary>
    public const string SettingsChoiceThemeDark = "Ui_Settings_Choice_Theme_Dark";

    /// <summary>设置页（SettingsPageViewModel）：候选文案「高对比度」（主题）。</summary>
    public const string SettingsChoiceThemeHighContrast = "Ui_Settings_Choice_Theme_HighContrast";

    /// <summary>设置页（SettingsPageViewModel）：设置项标题「界面语言」。</summary>
    public const string SettingsItemLocale = "Ui_Settings_Item_Locale";

    /// <summary>设置页（SettingsPageViewModel）：设置项「界面语言」的说明文案。</summary>
    public const string SettingsItemLocaleDesc = "Ui_Settings_Item_Locale_Desc";

    /// <summary>设置页（SettingsPageViewModel）：设置项标题「界面密度」。</summary>
    public const string SettingsItemDensity = "Ui_Settings_Item_Density";

    /// <summary>设置页（SettingsPageViewModel）：设置项「界面密度」的说明文案。</summary>
    public const string SettingsItemDensityDesc = "Ui_Settings_Item_Density_Desc";

    /// <summary>设置页（SettingsPageViewModel）：候选文案「紧凑」（界面密度）。</summary>
    public const string SettingsChoiceDensityCompact = "Ui_Settings_Choice_Density_Compact";

    /// <summary>设置页（SettingsPageViewModel）：候选文案「标准」（界面密度）。</summary>
    public const string SettingsChoiceDensityStandard = "Ui_Settings_Choice_Density_Standard";

    /// <summary>设置页（SettingsPageViewModel）：候选文案「舒适」（界面密度）。</summary>
    public const string SettingsChoiceDensityComfortable = "Ui_Settings_Choice_Density_Comfortable";

    /// <summary>设置页（SettingsPageViewModel）：设置项标题「字号缩放」。</summary>
    public const string SettingsItemFontScale = "Ui_Settings_Item_FontScale";

    /// <summary>设置页（SettingsPageViewModel）：设置项「字号缩放」的说明文案。</summary>
    public const string SettingsItemFontScaleDesc = "Ui_Settings_Item_FontScale_Desc";

    /// <summary>设置页（SettingsPageViewModel）：候选文案「100%（默认）」（字号缩放）。</summary>
    public const string SettingsChoiceFontScale100 = "Ui_Settings_Choice_FontScale_100";

    /// <summary>设置页（SettingsPageViewModel）：设置项标题「强调色」。</summary>
    public const string SettingsItemAccentColor = "Ui_Settings_Item_AccentColor";

    /// <summary>设置页（SettingsPageViewModel）：设置项「强调色」的说明文案。</summary>
    public const string SettingsItemAccentColorDesc = "Ui_Settings_Item_AccentColor_Desc";

    /// <summary>设置页（SettingsPageViewModel）：候选文案「跟随系统 / 经典」（强调色）。</summary>
    public const string SettingsChoiceAccentColorDefault = "Ui_Settings_Choice_AccentColor_Default";

    /// <summary>设置页（SettingsPageViewModel）：候选文案「经典蓝 / Blue」（强调色）。</summary>
    public const string SettingsChoiceAccentColorBlue = "Ui_Settings_Choice_AccentColor_Blue";

    /// <summary>设置页（SettingsPageViewModel）：候选文案「靛青 / Indigo」（强调色）。</summary>
    public const string SettingsChoiceAccentColorIndigo = "Ui_Settings_Choice_AccentColor_Indigo";

    /// <summary>设置页（SettingsPageViewModel）：候选文案「星云紫 / Purple」（强调色）。</summary>
    public const string SettingsChoiceAccentColorPurple = "Ui_Settings_Choice_AccentColor_Purple";

    /// <summary>设置页（SettingsPageViewModel）：候选文案「品红 / Magenta」（强调色）。</summary>
    public const string SettingsChoiceAccentColorMagenta = "Ui_Settings_Choice_AccentColor_Magenta";

    /// <summary>设置页（SettingsPageViewModel）：候选文案「火箭红 / Red」（强调色）。</summary>
    public const string SettingsChoiceAccentColorRed = "Ui_Settings_Choice_AccentColor_Red";

    /// <summary>设置页（SettingsPageViewModel）：候选文案「轨道橙 / Orange」（强调色）。</summary>
    public const string SettingsChoiceAccentColorOrange = "Ui_Settings_Choice_AccentColor_Orange";

    /// <summary>设置页（SettingsPageViewModel）：候选文案「琥珀金 / Amber」（强调色）。</summary>
    public const string SettingsChoiceAccentColorAmber = "Ui_Settings_Choice_AccentColor_Amber";

    /// <summary>设置页（SettingsPageViewModel）：候选文案「坎星绿 / Lime」（强调色）。</summary>
    public const string SettingsChoiceAccentColorLime = "Ui_Settings_Choice_AccentColor_Lime";

    /// <summary>设置页（SettingsPageViewModel）：候选文案「极光绿 / Green」（强调色）。</summary>
    public const string SettingsChoiceAccentColorGreen = "Ui_Settings_Choice_AccentColor_Green";

    /// <summary>设置页（SettingsPageViewModel）：候选文案「青碧 / Teal」（强调色）。</summary>
    public const string SettingsChoiceAccentColorTeal = "Ui_Settings_Choice_AccentColor_Teal";

    /// <summary>设置页（SettingsPageViewModel）：候选文案「天蓝 / Cyan」（强调色）。</summary>
    public const string SettingsChoiceAccentColorCyan = "Ui_Settings_Choice_AccentColor_Cyan";

    /// <summary>设置页（SettingsPageViewModel）：候选文案「灰蓝 / Slate」（强调色）。</summary>
    public const string SettingsChoiceAccentColorSlate = "Ui_Settings_Choice_AccentColor_Slate";

    /// <summary>设置页（SettingsPageViewModel）：设置项标题「自定义强调色」。</summary>
    public const string SettingsItemAccentColorCustom = "Ui_Settings_Item_AccentColorCustom";

    /// <summary>设置页（SettingsPageViewModel）：填 #RRGGBB（例如 #1F6FEB）；保存时按 WCAG AA 校验与底色…</summary>
    public const string SettingsItemAccentColorCustomDesc = "Ui_Settings_Item_AccentColorCustom_Desc";

    /// <summary>设置页（SettingsPageViewModel）：设置项标题「强调色作用范围」。</summary>
    public const string SettingsItemAccentScope = "Ui_Settings_Item_AccentScope";

    /// <summary>设置页（SettingsPageViewModel）：设置项「强调色作用范围」的说明文案。</summary>
    public const string SettingsItemAccentScopeDesc = "Ui_Settings_Item_AccentScope_Desc";

    /// <summary>设置页（SettingsPageViewModel）：候选文案「全局」（强调色作用范围）。</summary>
    public const string SettingsChoiceAccentScopeGlobal = "Ui_Settings_Choice_AccentScope_Global";

    /// <summary>设置页（SettingsPageViewModel）：候选文案「按实例（当前实例）」（强调色作用范围）。</summary>
    public const string SettingsChoiceAccentScopeInstance = "Ui_Settings_Choice_AccentScope_Instance";

    /// <summary>设置页（SettingsPageViewModel）：设置项标题「圆角尺度」。</summary>
    public const string SettingsItemRadiusScale = "Ui_Settings_Item_RadiusScale";

    /// <summary>设置页（SettingsPageViewModel）：设置项「圆角尺度」的说明文案。</summary>
    public const string SettingsItemRadiusScaleDesc = "Ui_Settings_Item_RadiusScale_Desc";

    /// <summary>设置页（SettingsPageViewModel）：候选文案「直角 (0x)」（圆角尺度）。</summary>
    public const string SettingsChoiceRadiusScaleNone = "Ui_Settings_Choice_RadiusScale_None";

    /// <summary>设置页（SettingsPageViewModel）：候选文案「标准微圆角 (1x)」（圆角尺度）。</summary>
    public const string SettingsChoiceRadiusScaleStandard = "Ui_Settings_Choice_RadiusScale_Standard";

    /// <summary>设置页（SettingsPageViewModel）：候选文案「饱满圆角 (1.5x)」（圆角尺度）。</summary>
    public const string SettingsChoiceRadiusScaleRound = "Ui_Settings_Choice_RadiusScale_Round";

    /// <summary>设置页（SettingsPageViewModel）：设置项标题「窗口材质与透明」。</summary>
    public const string SettingsItemBackdropKind = "Ui_Settings_Item_BackdropKind";

    /// <summary>设置页（SettingsPageViewModel）：设置项「窗口材质与透明」的说明文案。</summary>
    public const string SettingsItemBackdropKindDesc = "Ui_Settings_Item_BackdropKind_Desc";

    /// <summary>设置页（SettingsPageViewModel）：候选文案「自动推荐 (Mica / Acrylic)」（窗口材质与透明）。</summary>
    public const string SettingsChoiceBackdropKindAuto = "Ui_Settings_Choice_BackdropKind_Auto";

    /// <summary>设置页（SettingsPageViewModel）：候选文案「Mica 云母」（窗口材质与透明）。</summary>
    public const string SettingsChoiceBackdropKindMica = "Ui_Settings_Choice_BackdropKind_Mica";

    /// <summary>设置页（SettingsPageViewModel）：候选文案「Acrylic 亚克力磨砂」（窗口材质与透明）。</summary>
    public const string SettingsChoiceBackdropKindAcrylic = "Ui_Settings_Choice_BackdropKind_Acrylic";

    /// <summary>设置页（SettingsPageViewModel）：候选文案「纯色无透明」（窗口材质与透明）。</summary>
    public const string SettingsChoiceBackdropKindNone = "Ui_Settings_Choice_BackdropKind_None";

    /// <summary>设置页（SettingsPageViewModel）：设置项标题「动效强度」。</summary>
    public const string SettingsItemMotionIntensity = "Ui_Settings_Item_MotionIntensity";

    /// <summary>设置页（SettingsPageViewModel）：设置项「动效强度」的说明文案。</summary>
    public const string SettingsItemMotionIntensityDesc = "Ui_Settings_Item_MotionIntensity_Desc";

    /// <summary>设置页（SettingsPageViewModel）：候选文案「跟随系统 (推荐)」（动效强度）。</summary>
    public const string SettingsChoiceMotionIntensityAuto = "Ui_Settings_Choice_MotionIntensity_Auto";

    /// <summary>设置页（SettingsPageViewModel）：候选文案「流畅动画」（动效强度）。</summary>
    public const string SettingsChoiceMotionIntensityFull = "Ui_Settings_Choice_MotionIntensity_Full";

    /// <summary>设置页（SettingsPageViewModel）：候选文案「减少动画」（动效强度）。</summary>
    public const string SettingsChoiceMotionIntensityReduced = "Ui_Settings_Choice_MotionIntensity_Reduced";

    /// <summary>设置页（SettingsPageViewModel）：候选文案「关闭动画」（动效强度）。</summary>
    public const string SettingsChoiceMotionIntensityNone = "Ui_Settings_Choice_MotionIntensity_None";

    /// <summary>设置页（SettingsPageViewModel）：设置项标题「背景来源」。</summary>
    public const string SettingsItemBackgroundKind = "Ui_Settings_Item_BackgroundKind";

    /// <summary>设置页（SettingsPageViewModel）：设置项「背景来源」的说明文案。</summary>
    public const string SettingsItemBackgroundKindDesc = "Ui_Settings_Item_BackgroundKind_Desc";

    /// <summary>设置页（SettingsPageViewModel）：候选文案「无（跟随主题）」（背景来源）。</summary>
    public const string SettingsChoiceBackgroundKindNone = "Ui_Settings_Choice_BackgroundKind_None";

    /// <summary>设置页（SettingsPageViewModel）：候选文案「纯色」（背景来源）。</summary>
    public const string SettingsChoiceBackgroundKindSolid = "Ui_Settings_Choice_BackgroundKind_Solid";

    /// <summary>设置页（SettingsPageViewModel）：候选文案「渐变」（背景来源）。</summary>
    public const string SettingsChoiceBackgroundKindGradient = "Ui_Settings_Choice_BackgroundKind_Gradient";

    /// <summary>设置页（SettingsPageViewModel）：候选文案「本机图片」（背景来源）。</summary>
    public const string SettingsChoiceBackgroundKindImage = "Ui_Settings_Choice_BackgroundKind_Image";

    /// <summary>设置页（SettingsPageViewModel）：候选文案「游戏截图（最新一张）」（背景来源）。</summary>
    public const string SettingsChoiceBackgroundKindScreenshot = "Ui_Settings_Choice_BackgroundKind_Screenshot";

    /// <summary>设置页（SettingsPageViewModel）：设置项标题「背景色」。</summary>
    public const string SettingsItemBackgroundColor = "Ui_Settings_Item_BackgroundColor";

    /// <summary>设置页（SettingsPageViewModel）：设置项「背景色」的说明文案。</summary>
    public const string SettingsItemBackgroundColorDesc = "Ui_Settings_Item_BackgroundColor_Desc";

    /// <summary>设置页（SettingsPageViewModel）：设置项标题「渐变起色」。</summary>
    public const string SettingsItemBackgroundGradientStart = "Ui_Settings_Item_BackgroundGradientStart";

    /// <summary>设置页（SettingsPageViewModel）：设置项「渐变起色」的说明文案。</summary>
    public const string SettingsItemBackgroundGradientStartDesc = "Ui_Settings_Item_BackgroundGradientStart_Desc";

    /// <summary>设置页（SettingsPageViewModel）：设置项标题「渐变止色」。</summary>
    public const string SettingsItemBackgroundGradientEnd = "Ui_Settings_Item_BackgroundGradientEnd";

    /// <summary>设置页（SettingsPageViewModel）：设置项「渐变止色」的说明文案。</summary>
    public const string SettingsItemBackgroundGradientEndDesc = "Ui_Settings_Item_BackgroundGradientEnd_Desc";

    /// <summary>设置页（SettingsPageViewModel）：设置项标题「渐变角度」。</summary>
    public const string SettingsItemBackgroundGradientAngle = "Ui_Settings_Item_BackgroundGradientAngle";

    /// <summary>设置页（SettingsPageViewModel）：设置项「渐变角度」的说明文案。</summary>
    public const string SettingsItemBackgroundGradientAngleDesc = "Ui_Settings_Item_BackgroundGradientAngle_Desc";

    /// <summary>设置页（SettingsPageViewModel）：设置项标题「背景图片」。</summary>
    public const string SettingsItemBackgroundImagePath = "Ui_Settings_Item_BackgroundImagePath";

    /// <summary>设置页（SettingsPageViewModel）：设置项「背景图片」的说明文案。</summary>
    public const string SettingsItemBackgroundImagePathDesc = "Ui_Settings_Item_BackgroundImagePath_Desc";

    /// <summary>设置页（SettingsPageViewModel）：设置项标题「图片填充方式」。</summary>
    public const string SettingsItemBackgroundFill = "Ui_Settings_Item_BackgroundFill";

    /// <summary>设置页（SettingsPageViewModel）：设置项「图片填充方式」的说明文案。</summary>
    public const string SettingsItemBackgroundFillDesc = "Ui_Settings_Item_BackgroundFill_Desc";

    /// <summary>设置页（SettingsPageViewModel）：候选文案「填充（等比铺满）」（图片填充方式）。</summary>
    public const string SettingsChoiceBackgroundFillFill = "Ui_Settings_Choice_BackgroundFill_Fill";

    /// <summary>设置页（SettingsPageViewModel）：候选文案「适应（完整显示）」（图片填充方式）。</summary>
    public const string SettingsChoiceBackgroundFillFit = "Ui_Settings_Choice_BackgroundFill_Fit";

    /// <summary>设置页（SettingsPageViewModel）：候选文案「拉伸（不保比例）」（图片填充方式）。</summary>
    public const string SettingsChoiceBackgroundFillStretch = "Ui_Settings_Choice_BackgroundFill_Stretch";

    /// <summary>设置页（SettingsPageViewModel）：候选文案「平铺」（图片填充方式）。</summary>
    public const string SettingsChoiceBackgroundFillTile = "Ui_Settings_Choice_BackgroundFill_Tile";

    /// <summary>设置页（SettingsPageViewModel）：候选文案「居中（原尺寸）」（图片填充方式）。</summary>
    public const string SettingsChoiceBackgroundFillCenter = "Ui_Settings_Choice_BackgroundFill_Center";

    /// <summary>设置页（SettingsPageViewModel）：设置项标题「背景暗化」。</summary>
    public const string SettingsItemBackgroundDim = "Ui_Settings_Item_BackgroundDim";

    /// <summary>设置页（SettingsPageViewModel）：设置项「背景暗化」的说明文案。</summary>
    public const string SettingsItemBackgroundDimDesc = "Ui_Settings_Item_BackgroundDim_Desc";

    /// <summary>设置页（SettingsPageViewModel）：设置项标题「背景模糊」。</summary>
    public const string SettingsItemBackgroundBlur = "Ui_Settings_Item_BackgroundBlur";

    /// <summary>设置页（SettingsPageViewModel）：设置项「背景模糊」的说明文案。</summary>
    public const string SettingsItemBackgroundBlurDesc = "Ui_Settings_Item_BackgroundBlur_Desc";

    /// <summary>设置页（SettingsPageViewModel）：设置项标题「背景不透明度」。</summary>
    public const string SettingsItemBackgroundOpacity = "Ui_Settings_Item_BackgroundOpacity";

    /// <summary>设置页（SettingsPageViewModel）：设置项「背景不透明度」的说明文案。</summary>
    public const string SettingsItemBackgroundOpacityDesc = "Ui_Settings_Item_BackgroundOpacity_Desc";

    /// <summary>设置页（SettingsPageViewModel）：设置项标题「背景作用范围」。</summary>
    public const string SettingsItemBackgroundScope = "Ui_Settings_Item_BackgroundScope";

    /// <summary>设置页（SettingsPageViewModel）：设置项「背景作用范围」的说明文案。</summary>
    public const string SettingsItemBackgroundScopeDesc = "Ui_Settings_Item_BackgroundScope_Desc";

    /// <summary>设置页（SettingsPageViewModel）：候选文案「全局（所有实例）」（背景作用范围）。</summary>
    public const string SettingsChoiceBackgroundScopeGlobal = "Ui_Settings_Choice_BackgroundScope_Global";

    /// <summary>设置页（SettingsPageViewModel）：候选文案「仅当前实例」（背景作用范围）。</summary>
    public const string SettingsChoiceBackgroundScopeInstance = "Ui_Settings_Choice_BackgroundScope_Instance";

    /// <summary>设置页（SettingsPageViewModel）：设置项标题「稳定版容差」。</summary>
    public const string SettingsItemStabilityTolerance = "Ui_Settings_Item_StabilityTolerance";

    /// <summary>设置页（SettingsPageViewModel）：设置项「稳定版容差」的说明文案。</summary>
    public const string SettingsItemStabilityToleranceDesc = "Ui_Settings_Item_StabilityTolerance_Desc";

    /// <summary>设置页（SettingsPageViewModel）：候选文案「稳定」（稳定版容差）。</summary>
    public const string SettingsChoiceStabilityToleranceStable = "Ui_Settings_Choice_StabilityTolerance_Stable";

    /// <summary>设置页（SettingsPageViewModel）：候选文案「测试」（稳定版容差）。</summary>
    public const string SettingsChoiceStabilityToleranceTesting = "Ui_Settings_Choice_StabilityTolerance_Testing";

    /// <summary>设置页（SettingsPageViewModel）：候选文案「开发」（稳定版容差）。</summary>
    public const string SettingsChoiceStabilityToleranceDevelopment = "Ui_Settings_Choice_StabilityTolerance_Development";

    /// <summary>设置页（SettingsPageViewModel）：分组标题「兼容性」。</summary>
    public const string SettingsSectionCompatibility = "Ui_Settings_Section_Compatibility";

    /// <summary>设置页（SettingsPageViewModel）：分组「兼容性」的说明文案。</summary>
    public const string SettingsSectionCompatibilityDesc = "Ui_Settings_Section_Compatibility_Desc";

    /// <summary>设置页（SettingsPageViewModel）：设置项标题「安装过滤器（KSP）」。</summary>
    public const string SettingsItemGlobalInstallFiltersKsp = "Ui_Settings_Item_GlobalInstallFiltersKsp";

    /// <summary>设置页（SettingsPageViewModel）：设置项「安装过滤器（KSP）」的说明文案。</summary>
    public const string SettingsItemGlobalInstallFiltersKspDesc = "Ui_Settings_Item_GlobalInstallFiltersKsp_Desc";

    /// <summary>设置页（SettingsPageViewModel）：设置项标题「安装过滤器（KSP2）」。</summary>
    public const string SettingsItemGlobalInstallFiltersKsp2 = "Ui_Settings_Item_GlobalInstallFiltersKsp2";

    /// <summary>设置页（SettingsPageViewModel）：设置项「安装过滤器（KSP2）」的说明文案。</summary>
    public const string SettingsItemGlobalInstallFiltersKsp2Desc = "Ui_Settings_Item_GlobalInstallFiltersKsp2_Desc";

    /// <summary>设置页（SettingsPageViewModel）：设置项标题「隐藏版本 epoch 前缀」。</summary>
    public const string SettingsItemHideVersionEpoch = "Ui_Settings_Item_HideVersionEpoch";

    /// <summary>设置页（SettingsPageViewModel）：设置项「隐藏版本 epoch 前缀」的说明文案。</summary>
    public const string SettingsItemHideVersionEpochDesc = "Ui_Settings_Item_HideVersionEpoch_Desc";

    /// <summary>设置页（SettingsPageViewModel）：设置项标题「隐藏版本 v 前缀」。</summary>
    public const string SettingsItemHideVersionVPrefix = "Ui_Settings_Item_HideVersionVPrefix";

    /// <summary>设置页（SettingsPageViewModel）：设置项「隐藏版本 v 前缀」的说明文案。</summary>
    public const string SettingsItemHideVersionVPrefixDesc = "Ui_Settings_Item_HideVersionVPrefix_Desc";

    /// <summary>设置页（SettingsPageViewModel）：设置项标题「可升级项自动置顶」。</summary>
    public const string SettingsItemAutoSortUpdates = "Ui_Settings_Item_AutoSortUpdates";

    /// <summary>设置页（SettingsPageViewModel）：设置项「可升级项自动置顶」的说明文案。</summary>
    public const string SettingsItemAutoSortUpdatesDesc = "Ui_Settings_Item_AutoSortUpdates_Desc";

    /// <summary>设置页（SettingsPageViewModel）：设置项标题「不再询问推荐模组」。</summary>
    public const string SettingsItemSuppressRecommendations = "Ui_Settings_Item_SuppressRecommendations";

    /// <summary>设置页（SettingsPageViewModel）：设置项「不再询问推荐模组」的说明文案。</summary>
    public const string SettingsItemSuppressRecommendationsDesc = "Ui_Settings_Item_SuppressRecommendations_Desc";

    /// <summary>设置页（SettingsPageViewModel）：设置项标题「最小化到托盘」。</summary>
    public const string SettingsItemMinimizeToTray = "Ui_Settings_Item_MinimizeToTray";

    /// <summary>设置页（SettingsPageViewModel）：设置项「最小化到托盘」的说明文案。</summary>
    public const string SettingsItemMinimizeToTrayDesc = "Ui_Settings_Item_MinimizeToTray_Desc";

    /// <summary>设置页（SettingsPageViewModel）：设置项标题「关闭到托盘」。</summary>
    public const string SettingsItemCloseToTray = "Ui_Settings_Item_CloseToTray";

    /// <summary>设置页（SettingsPageViewModel）：设置项「关闭到托盘」的说明文案。</summary>
    public const string SettingsItemCloseToTrayDesc = "Ui_Settings_Item_CloseToTray_Desc";

    /// <summary>设置页（SettingsPageViewModel）：分组标题「高级与诊断」。</summary>
    public const string SettingsSectionAdvanced = "Ui_Settings_Section_Advanced";

    /// <summary>设置页（SettingsPageViewModel）：分组「高级与诊断」的说明文案。</summary>
    public const string SettingsSectionAdvancedDesc = "Ui_Settings_Section_Advanced_Desc";

    /// <summary>设置页（SettingsPageViewModel）：读取界面设置失败。</summary>
    public const string SettingsErrorUiSettingsLoadFailed = "Ui_Settings_Error_UiSettingsLoadFailed";

    /// <summary>设置页（SettingsPageViewModel）：自定义（{0}）。</summary>
    public const string SettingsChoiceInstanceCustom = "Ui_Settings_Choice_Instance_Custom";

    /// <summary>设置页（SettingsPageViewModel）：背景色需要 #RRGGBB 形式的十六进制（例如 #1B1F27）。。</summary>
    public const string SettingsErrorBackgroundColorInvalid = "Ui_Settings_Error_BackgroundColorInvalid";

    /// <summary>设置页（SettingsPageViewModel）：{0}（不在当前实例列表中）。</summary>
    public const string SettingsChoiceInstanceNotInList = "Ui_Settings_Choice_Instance_NotInList";

    /// <summary>设置页（SettingsPageViewModel）：读取稳定版容差失败。</summary>
    public const string SettingsErrorStabilityReadFailed = "Ui_Settings_Error_StabilityReadFailed";

    /// <summary>设置页（SettingsPageViewModel）：尚未打开实例。</summary>
    public const string SettingsErrorNoInstanceForStability = "Ui_Settings_Error_NoInstanceForStability";

    /// <summary>设置页（SettingsPageViewModel）：稳定版容差是实例级文件，需要先打开一个实例。</summary>
    public const string SettingsErrorNoInstanceForStabilityDetail = "Ui_Settings_Error_NoInstanceForStabilityDetail";

    /// <summary>设置页（SettingsPageViewModel）：保存稳定版容差失败。</summary>
    public const string SettingsErrorStabilityWriteFailed = "Ui_Settings_Error_StabilityWriteFailed";

    /// <summary>设置页（SettingsPageViewModel）：读回稳定版容差失败。</summary>
    public const string SettingsErrorStabilityReadbackFailed = "Ui_Settings_Error_StabilityReadbackFailed";

    /// <summary>设置页（SettingsPageViewModel）：写回校验不一致。</summary>
    public const string SettingsErrorStabilityReadbackMismatch = "Ui_Settings_Error_StabilityReadbackMismatch";

    /// <summary>设置页（SettingsPageViewModel）：读出 {0}，期望 {1}。</summary>
    public const string SettingsErrorStabilityReadbackMismatchDetail = "Ui_Settings_Error_StabilityReadbackMismatchDetail";

    /// <summary>设置页（SettingsPageViewModel）：缓存目录。</summary>
    public const string SettingsFolderCache = "Ui_Settings_Folder_Cache";

    /// <summary>设置页（SettingsPageViewModel）：配置目录。</summary>
    public const string SettingsFolderConfig = "Ui_Settings_Folder_Config";

    /// <summary>设置页（SettingsPageViewModel）：当前环境没有打开目录的通道。</summary>
    public const string SettingsErrorNoFolderChannel = "Ui_Settings_Error_NoFolderChannel";

    /// <summary>设置页（SettingsPageViewModel）：{0}为空：请先填写目录并保存，或使用默认目录。</summary>
    public const string SettingsErrorFolderEmpty = "Ui_Settings_Error_FolderEmpty";

    /// <summary>设置页（SettingsPageViewModel）：已打开{0}。</summary>
    public const string SettingsStatusFolderOpened = "Ui_Settings_Status_FolderOpened";

    /// <summary>设置页（SettingsPageViewModel）：打开{0}失败：{1}。</summary>
    public const string SettingsErrorFolderOpenFailed = "Ui_Settings_Error_FolderOpenFailed";

    /// <summary>设置页（SettingsPageViewModel）：未配置更新检查服务。</summary>
    public const string SettingsErrorNoUpdateService = "Ui_Settings_Error_NoUpdateService";

    /// <summary>设置页（SettingsPageViewModel）：正在检查更新…。</summary>
    public const string SettingsStatusCheckingUpdates = "Ui_Settings_Status_CheckingUpdates";

    /// <summary>设置页（SettingsPageViewModel）：检查更新失败：{0}。</summary>
    public const string SettingsErrorUpdateCheckFailed = "Ui_Settings_Error_UpdateCheckFailed";

    /// <summary>设置页（SettingsPageViewModel）：发现新版本 {0}（当前版本：{1}）。</summary>
    public const string SettingsStatusUpdateAvailable = "Ui_Settings_Status_UpdateAvailable";

    /// <summary>设置页（SettingsPageViewModel）：已是最新版本（{0}）。</summary>
    public const string SettingsStatusUpToDate = "Ui_Settings_Status_UpToDate";

    /// <summary>设置页（SettingsPageViewModel）：没有当前实例，无法按实例保存强调色；请先添加或切换实例。。</summary>
    public const string SettingsErrorNoInstanceForAccent = "Ui_Settings_Error_NoInstanceForAccent";

    /// <summary>设置页（SettingsPageViewModel）：找不到图片文件：{0}。</summary>
    public const string SettingsErrorBackgroundImageMissing = "Ui_Settings_Error_BackgroundImageMissing";

    /// <summary>设置页（SettingsPageViewModel）：没有当前实例，无法按实例保存背景；请先添加或切换实例。。</summary>
    public const string SettingsErrorNoInstanceForBackground = "Ui_Settings_Error_NoInstanceForBackground";

    /// <summary>设置页（SettingsPageViewModel）：未知的界面设置项：{0}。</summary>
    public const string SettingsErrorUnknownUiSetting = "Ui_Settings_Error_UnknownUiSetting";

    /// <summary>设置页（SettingsPageViewModel）：保存界面设置失败。</summary>
    public const string SettingsErrorUiSettingSaveFailed = "Ui_Settings_Error_UiSettingSaveFailed";

    /// <summary>设置页（SettingsPageViewModel）：{0}：已保存，重启后生效。</summary>
    public const string SettingsStatusItemSavedRestart = "Ui_Settings_Status_ItemSavedRestart";

    /// <summary>设置页（SettingsPageViewModel）：{0}：已保存。</summary>
    public const string SettingsStatusItemSaved = "Ui_Settings_Status_ItemSaved";

    /// <summary>设置页（SettingsPageViewModel）：{0}：保存失败，可重试。</summary>
    public const string SettingsStatusItemSaveFailedRetryable = "Ui_Settings_Status_ItemSaveFailedRetryable";

    /// <summary>未管理文件页（UnmanagedPageViewModel）：GameData 里没有未管理的内容</summary>
    public const string UnmanagedEmptyTitle = "Ui_Unmanaged_Empty_Title";

    /// <summary>未管理文件页（UnmanagedPageViewModel）：所有文件都已被注册表登记为受管，没有需要清理的残留。</summary>
    public const string UnmanagedEmptyHint = "Ui_Unmanaged_Empty_Hint";

    /// <summary>未管理文件页（UnmanagedPageViewModel）：还没有选定游戏实例</summary>
    public const string UnmanagedNoInstanceTitle = "Ui_Unmanaged_NoInstance_Title";

    /// <summary>未管理文件页（UnmanagedPageViewModel）：请先在「实例」页选择或添加一个实例。</summary>
    public const string UnmanagedNoInstanceHint = "Ui_Unmanaged_NoInstance_Hint";

    /// <summary>未管理文件页（UnmanagedPageViewModel）：未管理：{0} 个文件、{1} 个目录，合计 {2}</summary>
    public const string UnmanagedStats = "Ui_Unmanaged_Stats";

    /// <summary>未管理文件页（UnmanagedPageViewModel）：未选中任何项</summary>
    public const string UnmanagedSelectionNone = "Ui_Unmanaged_Selection_None";

    /// <summary>未管理文件页（UnmanagedPageViewModel）：已选中 {0} 项，合计 {1}</summary>
    public const string UnmanagedSelectionSummary = "Ui_Unmanaged_Selection_Summary";

    /// <summary>未管理文件页（UnmanagedPageViewModel）：正在扫描 GameData…</summary>
    public const string UnmanagedBusyScan = "Ui_Unmanaged_Busy_Scan";

    /// <summary>未管理文件页（UnmanagedPageViewModel）：没有可展开的目录。</summary>
    public const string UnmanagedStatusNoExpandable = "Ui_Unmanaged_Status_NoExpandable";

    /// <summary>未管理文件页（UnmanagedPageViewModel）：已展开全部目录（共 {0} 个）。</summary>
    public const string UnmanagedStatusExpandedAll = "Ui_Unmanaged_Status_ExpandedAll";

    /// <summary>未管理文件页（UnmanagedPageViewModel）：没有可折叠的目录。</summary>
    public const string UnmanagedStatusNoCollapsible = "Ui_Unmanaged_Status_NoCollapsible";

    /// <summary>未管理文件页（UnmanagedPageViewModel）：已折叠全部目录（共 {0} 个）。</summary>
    public const string UnmanagedStatusCollapsedAll = "Ui_Unmanaged_Status_CollapsedAll";

    /// <summary>未管理文件页（UnmanagedPageViewModel）：已重置筛选。</summary>
    public const string UnmanagedStatusFilterReset = "Ui_Unmanaged_Status_FilterReset";

    /// <summary>未管理文件页（UnmanagedPageViewModel）：已选中 {0} 项。</summary>
    public const string UnmanagedStatusSelectAll = "Ui_Unmanaged_Status_SelectAll";

    /// <summary>未管理文件页（UnmanagedPageViewModel）：已取消全部选中。</summary>
    public const string UnmanagedStatusSelectNone = "Ui_Unmanaged_Status_SelectNone";

    /// <summary>未管理文件页（UnmanagedPageViewModel）：还没有选定游戏实例。</summary>
    public const string UnmanagedErrorNoInstance = "Ui_Unmanaged_Error_NoInstance";

    /// <summary>未管理文件页（UnmanagedPageViewModel）：没有选中要删除的内容。</summary>
    public const string UnmanagedErrorNoDeleteSelection = "Ui_Unmanaged_Error_NoDeleteSelection";

    /// <summary>未管理文件页（UnmanagedPageViewModel）：预演删除失败。</summary>
    public const string UnmanagedErrorPreviewFailed = "Ui_Unmanaged_Error_PreviewFailed";

    /// <summary>未管理文件页（UnmanagedPageViewModel）：预演：将删除 {0} 项（未落盘）。</summary>
    public const string UnmanagedStatusPreview = "Ui_Unmanaged_Status_Preview";

    /// <summary>未管理文件页（UnmanagedPageViewModel）：正在预演删除…</summary>
    public const string UnmanagedBusyPreview = "Ui_Unmanaged_Busy_Preview";

    /// <summary>未管理文件页（UnmanagedPageViewModel）：删除未管理文件</summary>
    public const string UnmanagedConfirmDeleteTitle = "Ui_Unmanaged_Confirm_DeleteTitle";

    /// <summary>未管理文件页（UnmanagedPageViewModel）：确定要删除选中的 {0} 项（{1}）吗？</summary>
    public const string UnmanagedConfirmDeleteMessage = "Ui_Unmanaged_Confirm_DeleteMessage";

    /// <summary>未管理文件页（UnmanagedPageViewModel）：删除会直接从磁盘移除文件，无法撤销。</summary>
    public const string UnmanagedConfirmDeleteDetail = "Ui_Unmanaged_Confirm_DeleteDetail";

    /// <summary>未管理文件页（UnmanagedPageViewModel）：已取消删除。</summary>
    public const string UnmanagedStatusDeleteCancelled = "Ui_Unmanaged_Status_DeleteCancelled";

    /// <summary>未管理文件页（UnmanagedPageViewModel）：再次确认删除</summary>
    public const string UnmanagedConfirmDeleteAgainTitle = "Ui_Unmanaged_Confirm_DeleteAgainTitle";

    /// <summary>未管理文件页（UnmanagedPageViewModel）：删除后无法恢复：{0} 项，{1}。要继续吗？</summary>
    public const string UnmanagedConfirmDeleteAgainMessage = "Ui_Unmanaged_Confirm_DeleteAgainMessage";

    /// <summary>未管理文件页（UnmanagedPageViewModel）：受管文件不在本页删除范围；含受管内容的目录会被服务拒绝。</summary>
    public const string UnmanagedConfirmDeleteAgainDetail = "Ui_Unmanaged_Confirm_DeleteAgainDetail";

    /// <summary>未管理文件页（UnmanagedPageViewModel）：删除未管理文件失败。</summary>
    public const string UnmanagedErrorDeleteFailed = "Ui_Unmanaged_Error_DeleteFailed";

    /// <summary>未管理文件页（UnmanagedPageViewModel）：已删除 {0} 项未管理内容。</summary>
    public const string UnmanagedStatusDeleted = "Ui_Unmanaged_Status_Deleted";

    /// <summary>未管理文件页（UnmanagedPageViewModel）：正在删除…</summary>
    public const string UnmanagedBusyDelete = "Ui_Unmanaged_Busy_Delete";

    /// <summary>未管理文件页（UnmanagedPageViewModel）：没有选中要接管的内容。</summary>
    public const string UnmanagedErrorNoAdoptSelection = "Ui_Unmanaged_Error_NoAdoptSelection";

    /// <summary>未管理文件页（UnmanagedPageViewModel）：接管为受管安装</summary>
    public const string UnmanagedConfirmAdoptTitle = "Ui_Unmanaged_Confirm_AdoptTitle";

    /// <summary>未管理文件页（UnmanagedPageViewModel）：确定把选中的 {0} 项接管为受管安装吗？</summary>
    public const string UnmanagedConfirmAdoptMessage = "Ui_Unmanaged_Confirm_AdoptMessage";

    /// <summary>未管理文件页（UnmanagedPageViewModel）：接管会把 .dll 登记进注册表（installed_dlls），写前自动生成 registry.json 快照。</summary>
    public const string UnmanagedConfirmAdoptDetailRegistry = "Ui_Unmanaged_Confirm_AdoptDetailRegistry";

    /// <summary>未管理文件页（UnmanagedPageViewModel）：只有 .dll 可以接管；目录会展开成其中的 DLL。</summary>
    public const string UnmanagedConfirmAdoptDetailDllOnly = "Ui_Unmanaged_Confirm_AdoptDetailDllOnly";

    /// <summary>未管理文件页（UnmanagedPageViewModel）：已取消接管。</summary>
    public const string UnmanagedStatusAdoptCancelled = "Ui_Unmanaged_Status_AdoptCancelled";

    /// <summary>未管理文件页（UnmanagedPageViewModel）：接管未管理文件失败。</summary>
    public const string UnmanagedErrorAdoptFailed = "Ui_Unmanaged_Error_AdoptFailed";

    /// <summary>未管理文件页（UnmanagedPageViewModel）：已接管 {0} 个、跳过 {1} 个、拒绝 {2} 个。</summary>
    public const string UnmanagedStatusAdopted = "Ui_Unmanaged_Status_Adopted";

    /// <summary>未管理文件页（UnmanagedPageViewModel）：有 {0} 项被拒绝接管。</summary>
    public const string UnmanagedErrorAdoptRefused = "Ui_Unmanaged_Error_AdoptRefused";

    /// <summary>未管理文件页（UnmanagedPageViewModel）：正在接管…</summary>
    public const string UnmanagedBusyAdopt = "Ui_Unmanaged_Busy_Adopt";

    /// <summary>未管理文件页（UnmanagedPageViewModel）：请先选中一个文件或目录。</summary>
    public const string UnmanagedErrorNoRevealSelection = "Ui_Unmanaged_Error_NoRevealSelection";

    /// <summary>未管理文件页（UnmanagedPageViewModel）：当前宿主没有提供「在文件夹中显示」能力。</summary>
    public const string UnmanagedErrorNoRevealChannel = "Ui_Unmanaged_Error_NoRevealChannel";

    /// <summary>未管理文件页（UnmanagedPageViewModel）：打开文件夹失败。</summary>
    public const string UnmanagedErrorRevealFailed = "Ui_Unmanaged_Error_RevealFailed";

    /// <summary>未管理文件页（UnmanagedPageViewModel）：已在文件管理器中定位：{0}</summary>
    public const string UnmanagedStatusRevealed = "Ui_Unmanaged_Status_Revealed";

    /// <summary>未管理文件页（UnmanagedPageViewModel）：扫描未管理文件失败。</summary>
    public const string UnmanagedErrorScanFailed = "Ui_Unmanaged_Error_ScanFailed";

    /// <summary>未管理文件页（UnmanagedPageViewModel）：GameData 里没有未管理的内容。</summary>
    public const string UnmanagedStatusEmptyScan = "Ui_Unmanaged_Status_EmptyScan";

    /// <summary>未管理文件页（UnmanagedPageViewModel）：已扫描到 {0} 个未管理文件、{1} 个目录。</summary>
    public const string UnmanagedStatusScanned = "Ui_Unmanaged_Status_Scanned";

    /// <summary>未管理文件页（UnmanagedPageViewModel）：未管理文件操作失败。</summary>
    public const string UnmanagedErrorOperationFailed = "Ui_Unmanaged_Error_OperationFailed";

    /// <summary>安装历史页（InstallHistoryPageViewModel）：选择一条历史记录，查看当时的已装集合，或从该时间点重装。</summary>
    public const string InstallHistoryStatusSelectHint = "Ui_InstallHistory_Status_Select_Hint";

    /// <summary>安装历史页（InstallHistoryPageViewModel）：当前实例：{0}（历史文件：&lt;GameDir&gt;/CKAN/install_history.jsonl）</summary>
    public const string InstallHistoryInstanceTitle = "Ui_InstallHistory_Instance_Title";

    /// <summary>安装历史页（InstallHistoryPageViewModel）：当前没有选中的游戏实例，安装历史不可用。</summary>
    public const string InstallHistoryInstanceMissing = "Ui_InstallHistory_Instance_Missing";

    /// <summary>安装历史页（InstallHistoryPageViewModel）：暂无历史记录</summary>
    public const string InstallHistoryCountNone = "Ui_InstallHistory_Count_None";

    /// <summary>安装历史页（InstallHistoryPageViewModel）：共 {0} 条历史记录</summary>
    public const string InstallHistoryCountSummary = "Ui_InstallHistory_Count_Summary";

    /// <summary>安装历史页（InstallHistoryPageViewModel）：{0}｜{1}｜{2}</summary>
    public const string InstallHistorySelectionTitle = "Ui_InstallHistory_Selection_Title";

    /// <summary>安装历史页（InstallHistoryPageViewModel）：未选择历史记录</summary>
    public const string InstallHistorySelectionNone = "Ui_InstallHistory_Selection_None";

    /// <summary>安装历史页（InstallHistoryPageViewModel）：{0}（快照共 {1} 个模组）</summary>
    public const string InstallHistorySelectionSummary = "Ui_InstallHistory_Selection_Summary";

    /// <summary>安装历史页（InstallHistoryPageViewModel）：选中一条记录后显示当时的已装集合。</summary>
    public const string InstallHistorySelectionSummaryNone = "Ui_InstallHistory_Selection_Summary_None";

    /// <summary>安装历史页（InstallHistoryPageViewModel）：已安装（{0}）</summary>
    public const string InstallHistoryGroupInstalled = "Ui_InstallHistory_Group_Installed";

    /// <summary>安装历史页（InstallHistoryPageViewModel）：未安装（{0}）</summary>
    public const string InstallHistoryGroupMissing = "Ui_InstallHistory_Group_Missing";

    /// <summary>安装历史页（InstallHistoryPageViewModel）：先在左侧选中一条历史记录。</summary>
    public const string InstallHistoryRestoreHintNoSelection = "Ui_InstallHistory_Restore_Hint_No_Selection";

    /// <summary>安装历史页（InstallHistoryPageViewModel）：这条历史没有已装集合快照，无法重装。</summary>
    public const string InstallHistoryRestoreHintNoSnapshot = "Ui_InstallHistory_Restore_Hint_No_Snapshot";

    /// <summary>安装历史页（InstallHistoryPageViewModel）：该时间点的快照里没有任何模组，无需重装。</summary>
    public const string InstallHistoryRestoreHintEmptySnapshot = "Ui_InstallHistory_Restore_Hint_Empty_Snapshot";

    /// <summary>安装历史页（InstallHistoryPageViewModel）：重装会把当前实例的已装集合改成该时间点的样子，并卸载快照之外的模组（这些模组会被删除）。</summary>
    public const string InstallHistoryRestoreHintRemoveExtras = "Ui_InstallHistory_Restore_Hint_Remove_Extras";

    /// <summary>安装历史页（InstallHistoryPageViewModel）：重装会把当前实例的已装集合改成该时间点的样子（不会卸载快照之外的模组）。</summary>
    public const string InstallHistoryRestoreHintKeepExtras = "Ui_InstallHistory_Restore_Hint_Keep_Extras";

    /// <summary>安装历史页（InstallHistoryPageViewModel）：该时间点共 {0} 个模组</summary>
    public const string InstallHistoryPreviewSnapshot = "Ui_InstallHistory_Preview_Snapshot";

    /// <summary>安装历史页（InstallHistoryPageViewModel）：已装 {0} 个</summary>
    public const string InstallHistoryPreviewInstalled = "Ui_InstallHistory_Preview_Installed";

    /// <summary>安装历史页（InstallHistoryPageViewModel）：需变更 {0} 个（新装 {1}，换版本 {2}）</summary>
    public const string InstallHistoryPreviewChanges = "Ui_InstallHistory_Preview_Changes";

    /// <summary>安装历史页（InstallHistoryPageViewModel）：已勾选同时卸载快照之外的模组，实际还会卸载不在该快照里的模组。</summary>
    public const string InstallHistoryPreviewRemoveExtrasNote = "Ui_InstallHistory_Preview_Remove_Extras_Note";

    /// <summary>安装历史页（InstallHistoryPageViewModel）：正在读取安装历史…</summary>
    public const string InstallHistoryBusyLoad = "Ui_InstallHistory_Busy_Load";

    /// <summary>安装历史页（InstallHistoryPageViewModel）：请先选中一条历史记录。</summary>
    public const string InstallHistoryErrorNoSelection = "Ui_InstallHistory_Error_No_Selection";

    /// <summary>安装历史页（InstallHistoryPageViewModel）：当前安装历史服务不支持删除。</summary>
    public const string InstallHistoryErrorDeleteUnsupported = "Ui_InstallHistory_Error_Delete_Unsupported";

    /// <summary>安装历史页（InstallHistoryPageViewModel）：删除这条历史？</summary>
    public const string InstallHistoryConfirmDeleteTitle = "Ui_InstallHistory_Confirm_Delete_Title";

    /// <summary>安装历史页（InstallHistoryPageViewModel）：将删除 {0}｜{1} 这一条历史及其已装集合快照，删除后无法撤销。</summary>
    public const string InstallHistoryConfirmDeleteMessage = "Ui_InstallHistory_Confirm_Delete_Message";

    /// <summary>安装历史页（InstallHistoryPageViewModel）：删除安装历史失败。</summary>
    public const string InstallHistoryErrorDeleteFailed = "Ui_InstallHistory_Error_Delete_Failed";

    /// <summary>安装历史页（InstallHistoryPageViewModel）：这条历史已经不存在（列表已刷新）。</summary>
    public const string InstallHistoryStatusGone = "Ui_InstallHistory_Status_Gone";

    /// <summary>安装历史页（InstallHistoryPageViewModel）：已删除 1 条历史记录（{0} 行）。</summary>
    public const string InstallHistoryStatusDeleted = "Ui_InstallHistory_Status_Deleted";

    /// <summary>安装历史页（InstallHistoryPageViewModel）：正在删除安装历史…</summary>
    public const string InstallHistoryBusyDelete = "Ui_InstallHistory_Busy_Delete";

    /// <summary>安装历史页（InstallHistoryPageViewModel）：当前安装历史服务不支持清空。</summary>
    public const string InstallHistoryErrorClearUnsupported = "Ui_InstallHistory_Error_Clear_Unsupported";

    /// <summary>安装历史页（InstallHistoryPageViewModel）：清空安装历史？</summary>
    public const string InstallHistoryConfirmClearTitle = "Ui_InstallHistory_Confirm_Clear_Title";

    /// <summary>安装历史页（InstallHistoryPageViewModel）：将删除当前实例的全部安装历史（{0}），清空后无法撤销。</summary>
    public const string InstallHistoryConfirmClearMessage = "Ui_InstallHistory_Confirm_Clear_Message";

    /// <summary>安装历史页（InstallHistoryPageViewModel）：清空安装历史失败。</summary>
    public const string InstallHistoryErrorClearFailed = "Ui_InstallHistory_Error_Clear_Failed";

    /// <summary>安装历史页（InstallHistoryPageViewModel）：安装历史本来就是空的。</summary>
    public const string InstallHistoryStatusAlreadyEmpty = "Ui_InstallHistory_Status_Already_Empty";

    /// <summary>安装历史页（InstallHistoryPageViewModel）：已清空安装历史（{0} 行）。</summary>
    public const string InstallHistoryStatusCleared = "Ui_InstallHistory_Status_Cleared";

    /// <summary>安装历史页（InstallHistoryPageViewModel）：正在清空安装历史…</summary>
    public const string InstallHistoryBusyClear = "Ui_InstallHistory_Busy_Clear";

    /// <summary>安装历史页（InstallHistoryPageViewModel）：读取安装历史失败。</summary>
    public const string InstallHistoryErrorLoadFailed = "Ui_InstallHistory_Error_Load_Failed";

    /// <summary>安装历史页（InstallHistoryPageViewModel）：还没有历史记录：装/卸/升级任意模组后会自动记一条。</summary>
    public const string InstallHistoryStatusEmptyHistory = "Ui_InstallHistory_Status_Empty_History";

    /// <summary>安装历史页（InstallHistoryPageViewModel）：读取完成：{0}。</summary>
    public const string InstallHistoryStatusLoaded = "Ui_InstallHistory_Status_Loaded";

    /// <summary>安装历史页（InstallHistoryPageViewModel）：当前环境没有确认对话框，已取消这次操作。</summary>
    public const string InstallHistoryErrorNoDialogs = "Ui_InstallHistory_Error_No_Dialogs";

    /// <summary>安装历史页（InstallHistoryPageViewModel）：预览重装失败。</summary>
    public const string InstallHistoryErrorPreviewFailed = "Ui_InstallHistory_Error_Preview_Failed";

    /// <summary>安装历史页（InstallHistoryPageViewModel）：重装目标存在依赖冲突，已中止：{0}</summary>
    public const string InstallHistoryErrorConflicts = "Ui_InstallHistory_Error_Conflicts";

    /// <summary>安装历史页（InstallHistoryPageViewModel）：该时间点与当前已装集合一致，无需重装。</summary>
    public const string InstallHistoryPreviewNoChange = "Ui_InstallHistory_Preview_No_Change";

    /// <summary>安装历史页（InstallHistoryPageViewModel）：无需重装：当前已装集合已经与该时间点一致。</summary>
    public const string InstallHistoryStatusAlreadyMatches = "Ui_InstallHistory_Status_Already_Matches";

    /// <summary>安装历史页（InstallHistoryPageViewModel）：正在从历史重装…</summary>
    public const string InstallHistoryBusyRestoring = "Ui_InstallHistory_Busy_Restoring";

    /// <summary>安装历史页（InstallHistoryPageViewModel）：从历史重装失败，可重试。</summary>
    public const string InstallHistoryErrorRestoreFailed = "Ui_InstallHistory_Error_Restore_Failed";

    /// <summary>安装历史页（InstallHistoryPageViewModel）：重装结束。</summary>
    public const string InstallHistoryProgressDone = "Ui_InstallHistory_Progress_Done";

    /// <summary>安装历史页（InstallHistoryPageViewModel）：重装完成：安装 {0} 个，卸载 {1} 个。</summary>
    public const string InstallHistoryStatusRestored = "Ui_InstallHistory_Status_Restored";

    /// <summary>安装历史页（InstallHistoryPageViewModel）：重装结束：成功 {0} 个，失败 {1} 个。</summary>
    public const string InstallHistoryStatusRestorePartial = "Ui_InstallHistory_Status_Restore_Partial";

    /// <summary>安装历史页（InstallHistoryPageViewModel）：重装将变更 {0} 个模组</summary>
    public const string InstallHistoryPlanChanges = "Ui_InstallHistory_Plan_Changes";

    /// <summary>安装历史页（InstallHistoryPageViewModel）：新装 {0}</summary>
    public const string InstallHistoryPlanInstall = "Ui_InstallHistory_Plan_Install";

    /// <summary>安装历史页（InstallHistoryPageViewModel）：换版本 {0}</summary>
    public const string InstallHistoryPlanUpgrade = "Ui_InstallHistory_Plan_Upgrade";

    /// <summary>安装历史页（InstallHistoryPageViewModel）：卸载 {0}</summary>
    public const string InstallHistoryPlanRemove = "Ui_InstallHistory_Plan_Remove";

    /// <summary>安装历史页（InstallHistoryPageViewModel）：预计下载 {0} MB</summary>
    public const string InstallHistoryPlanDownload = "Ui_InstallHistory_Plan_Download";

    /// <summary>安装历史页（InstallHistoryPageViewModel）：重装结果：安装 {0} 个，卸载 {1} 个，失败 {2} 个</summary>
    public const string InstallHistoryResultSummary = "Ui_InstallHistory_Result_Summary";

    /// <summary>安装历史页（InstallHistoryPageViewModel）：失败清单：{0}</summary>
    public const string InstallHistoryResultFailedList = "Ui_InstallHistory_Result_Failed_List";

    /// <summary>安装历史页（InstallHistoryPageViewModel）：警告：{0}</summary>
    public const string InstallHistoryResultWarnings = "Ui_InstallHistory_Result_Warnings";

    /// <summary>安装历史页（InstallHistoryPageViewModel）：安装历史操作失败。</summary>
    public const string InstallHistoryErrorOperationFailed = "Ui_InstallHistory_Error_Operation_Failed";

    /// <summary>变更集页（ChangesPageViewModel）：存在 {0} 个冲突，解决后才能开始</summary>
    public const string ChangesConflictTitle = "Ui_Changes_Conflict_Title";

    /// <summary>变更集页（ChangesPageViewModel）：冲突需手动处理，本页不自动裁决。</summary>
    public const string ChangesConflictHint = "Ui_Changes_Conflict_Hint";

    /// <summary>变更集页（ChangesPageViewModel）：游戏正在运行</summary>
    public const string ChangesGameRunningTitle = "Ui_Changes_Game_Running_Title";

    /// <summary>变更集页（ChangesPageViewModel）：仍要执行</summary>
    public const string ChangesApplyAnywayButtonText = "Ui_Changes_Apply_Anyway_Button_Text";

    /// <summary>变更集页（ChangesPageViewModel）：当前实例的游戏进程还在运行，安装/卸载可能与游戏争用文件并导致模组损坏。建议先完全退出游戏；确实要继续请点「仍要执行」。</summary>
    public const string ChangesGameRunningHint = "Ui_Changes_Game_Running_Hint";

    /// <summary>变更集页（ChangesPageViewModel）：{0} 条提示（不阻断执行）</summary>
    public const string ChangesWarningTitle = "Ui_Changes_Warning_Title";

    /// <summary>变更集页（ChangesPageViewModel）：还没有待应用的变更</summary>
    public const string ChangesEmptyStateTitle = "Ui_Changes_Empty_State_Title";

    /// <summary>变更集页（ChangesPageViewModel）：变更集服务尚未接入（INT-07），本页暂时无法解析依赖与冲突。</summary>
    public const string ChangesEmptyHintNoService = "Ui_Changes_Empty_Hint_No_Service";

    /// <summary>变更集页（ChangesPageViewModel）：变更集为空：所有请求项都已是最新状态，或被安装过滤器排除。</summary>
    public const string ChangesEmptyHintEmptyPreview = "Ui_Changes_Empty_Hint_Empty_Preview";

    /// <summary>变更集页（ChangesPageViewModel）：先在「模组」页勾选要安装或移除的项。</summary>
    public const string ChangesEmptyHintNoRequests = "Ui_Changes_Empty_Hint_No_Requests";

    /// <summary>变更集页（ChangesPageViewModel）：变更集服务尚未接入（INT-07），暂时只能看到请求数量，无法解析依赖与冲突。</summary>
    public const string ChangesStatusNoChangeSetService = "Ui_Changes_Status_No_Change_Set_Service";

    /// <summary>变更集页（ChangesPageViewModel）：变更集已算出，但有 {0} 个冲突需要先解决。</summary>
    public const string ChangesStatusComputedWithConflicts = "Ui_Changes_Status_Computed_With_Conflicts";

    /// <summary>变更集页（ChangesPageViewModel）：变更集已算出，可以开始。</summary>
    public const string ChangesStatusComputedReady = "Ui_Changes_Status_Computed_Ready";

    /// <summary>变更集页（ChangesPageViewModel）：计算变更集失败，请查看错误栏。</summary>
    public const string ChangesErrorBuildFailed = "Ui_Changes_Error_Build_Failed";

    /// <summary>变更集页（ChangesPageViewModel）：计算变更集时发生错误。</summary>
    public const string ChangesErrorBuildException = "Ui_Changes_Error_Build_Exception";

    /// <summary>变更集页（ChangesPageViewModel）：应用变更失败，请查看错误栏。</summary>
    public const string ChangesErrorApplyFailed = "Ui_Changes_Error_Apply_Failed";

    /// <summary>变更集页（ChangesPageViewModel）：{0} 已清理残留目录。</summary>
    public const string ChangesStatusResidualCleaned = "Ui_Changes_Status_Residual_Cleaned";

    /// <summary>变更集页（ChangesPageViewModel）：清理残留目录时发生错误，卸载本身已完成。</summary>
    public const string ChangesErrorResidualCleanup = "Ui_Changes_Error_Residual_Cleanup";

    /// <summary>变更集页（ChangesPageViewModel）：失败的模组：</summary>
    public const string ChangesResultFailedHeading = "Ui_Changes_Result_Failed_Heading";

    /// <summary>变更集页（ChangesPageViewModel）：提示：</summary>
    public const string ChangesResultWarningsHeading = "Ui_Changes_Result_Warnings_Heading";

    /// <summary>变更集页（ChangesPageViewModel）：已安装：{0}</summary>
    public const string ChangesResultInstalled = "Ui_Changes_Result_Installed";

    /// <summary>变更集页（ChangesPageViewModel）：已移除：{0}</summary>
    public const string ChangesResultRemoved = "Ui_Changes_Result_Removed";

    /// <summary>变更集页（ChangesPageViewModel）：没有需要报告的变化。</summary>
    public const string ChangesResultNothing = "Ui_Changes_Result_Nothing";

    /// <summary>变更集页（ChangesPageViewModel）：无法判断游戏是否正在运行，已按未运行处理。</summary>
    public const string ChangesErrorGameRunningProbe = "Ui_Changes_Error_Game_Running_Probe";

    /// <summary>变更集页（ChangesPageViewModel）：已取消推荐模组选择，未执行任何操作。</summary>
    public const string ChangesStatusRecommendationCancelled = "Ui_Changes_Status_Recommendation_Cancelled";

    /// <summary>变更集页（ChangesPageViewModel）：本次事务已回滚，游戏目录未被改动。</summary>
    public const string ChangesResultRolledBack = "Ui_Changes_Result_Rolled_Back";

    /// <summary>变更集页（ChangesPageViewModel）：应用变更时发生错误，请查看错误栏。</summary>
    public const string ChangesErrorApplyExceptionStatus = "Ui_Changes_Error_Apply_Exception_Status";

    /// <summary>变更集页（ChangesPageViewModel）：应用变更时发生错误。</summary>
    public const string ChangesErrorApplyException = "Ui_Changes_Error_Apply_Exception";

    /// <summary>变更集页（ChangesPageViewModel）：正在取消…（安装阶段的取消会触发事务回滚）</summary>
    public const string ChangesStatusCancelling = "Ui_Changes_Status_Cancelling";

    /// <summary>变更集页（ChangesPageViewModel）：任务已结束，无需取消。</summary>
    public const string ChangesStatusNothingToCancel = "Ui_Changes_Status_Nothing_To_Cancel";

    /// <summary>变更集页（ChangesPageViewModel）：已从变更集移除 {0}（点「重新计算」可恢复权威结果）。</summary>
    public const string ChangesStatusRemovedFromSet = "Ui_Changes_Status_Removed_From_Set";

    /// <summary>变更集页（ChangesPageViewModel）：模组对话框服务未注入，无法展示冲突详情。</summary>
    public const string ChangesErrorNoDialogConflict = "Ui_Changes_Error_No_Dialog_Conflict";

    /// <summary>变更集页（ChangesPageViewModel）：检测到模组冲突</summary>
    public const string ChangesConflictDialogTitle = "Ui_Changes_Conflict_Dialog_Title";

    /// <summary>变更集页（ChangesPageViewModel）：展示冲突详情时发生错误。</summary>
    public const string ChangesErrorShowConflictDetails = "Ui_Changes_Error_Show_Conflict_Details";

    /// <summary>变更集页（ChangesPageViewModel）：检测到推荐、建议或支持项，请选择是否追加安装：</summary>
    public const string ChangesRecommendationDialogMessage = "Ui_Changes_Recommendation_Dialog_Message";

    /// <summary>变更集页（ChangesPageViewModel）：选择推荐模组对话框发生错误。</summary>
    public const string ChangesErrorRecommendationDialog = "Ui_Changes_Error_Recommendation_Dialog";

    /// <summary>变更集页（ChangesPageViewModel）：模组对话框服务未注入，无法选择虚拟模块。</summary>
    public const string ChangesErrorNoDialogVirtual = "Ui_Changes_Error_No_Dialog_Virtual";

    /// <summary>变更集页（ChangesPageViewModel）：已选择「{0}」作为虚拟模组提供者。</summary>
    public const string ChangesStatusVirtualModuleSelected = "Ui_Changes_Status_Virtual_Module_Selected";

    /// <summary>变更集页（ChangesPageViewModel）：已取消选择虚拟模组提供者，未执行任何操作。</summary>
    public const string ChangesStatusVirtualModuleCancelled = "Ui_Changes_Status_Virtual_Module_Cancelled";

    /// <summary>变更集页（ChangesPageViewModel）：选择虚拟模组提供者时发生错误。</summary>
    public const string ChangesErrorVirtualModuleDialog = "Ui_Changes_Error_Virtual_Module_Dialog";

    /// <summary>变更集页（ChangesPageViewModel）：安装器将在 M2 接入（INT-08）：当前只能查看变更预览，还不能真正安装。</summary>
    public const string ChangesMissingInstaller = "Ui_Changes_Missing_Installer";
}
