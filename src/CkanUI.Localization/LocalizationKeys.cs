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

    /// <summary>导航栏：模组汉化/文本。</summary>
    public const string NavRailModText = "Ui_NavRail_ModText";

    /// <summary>导航栏：日志与诊断（M8 / 14 篇 §4.1 / UI-LOG-01）。</summary>
    public const string NavRailLogs = "Ui_NavRail_Logs";

    /// <summary>导航栏：设置。</summary>
    public const string NavRailSettings = "Ui_NavRail_Settings";

    /// <summary>导航栏：Mod 配置适配层（M9）。</summary>
    public const string NavRailModConfig = "Ui_NavRail_ModConfig";

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
}
