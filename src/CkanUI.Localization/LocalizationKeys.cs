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

    /// <summary>页面标题：变更集。</summary>
    public const string ChangesetTitle = "Ui_Changeset_Title";

    /// <summary>导航栏：实例。</summary>
    public const string NavRailRepos = "Ui_NavRail_Repos";

    /// <summary>导航栏：导入导出页。</summary>
    public const string NavRailModPacks = "Ui_NavRail_ModPacks";

    public const string NavRailInstances = "Ui_NavRail_Instances";

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
