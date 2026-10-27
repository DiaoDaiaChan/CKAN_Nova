namespace CkanUI.Localization;

/// <summary>
/// 新版 UI 文案键常量（16 篇 §5.1 键规范：<c>Ui.&lt;Area&gt;_&lt;Element&gt;[_&lt;Qualifier&gt;]</c>，
/// 区域词表取规格原文的 11 个：NavRail / ManageMods / ModInfo / Changeset / Settings / Instances /
/// Repos / Log / Saves / ModConfig / Tray / Common）。
/// 与语言包的对应关系由 LanguagePackTests 校验：常量必须在 zh-CN 与 en-US 两套包里都存在。
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

    /// <summary>导航栏：设置。</summary>
    public const string NavRailSettings = "Ui_NavRail_Settings";

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
