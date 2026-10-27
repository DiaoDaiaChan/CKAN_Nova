namespace CkanUI.ViewModels.Pages.Settings;

/// <summary>
/// 设置键名常量。真源是 <c>src/CkanUI.Services/Settings/SettingKeys.cs</c>，这里再声明一份是因为
/// 分层铁律禁止 ViewModels 引用 CkanUI.Services（01 篇 §5）；键名是 VM 与服务之间唯一的字符串契约，
/// 两侧一致由 <c>SettingsPageViewModelTests.KeyNames_MatchServicesContract</c> 断言，防止悄悄漂移。
/// </summary>
public static class SettingsKeyNames
{
    /// <summary>Core：使用开发版更新通道（bool?）。</summary>
    public const string DevBuilds = "DevBuilds";

    /// <summary>Core：启动时自动打开的实例名。</summary>
    public const string AutoStartInstance = "AutoStartInstance";

    /// <summary>Core：下载缓存目录。</summary>
    public const string DownloadCacheDir = "DownloadCacheDir";

    /// <summary>Core：缓存容量上限（字节；VM 以 MiB 呈现）。</summary>
    public const string CacheSizeLimit = "CacheSizeLimit";

    /// <summary>Core：仓库自动刷新间隔（分钟）。</summary>
    public const string RefreshRate = "RefreshRate";

    /// <summary>Core：界面语言（由 I18N-04 负责写入，设置页的语言项写 ckan-ui.json）。</summary>
    public const string Language = "Language";

    /// <summary>Core：首选主机顺序（JSON 字符串数组，null 元素表示「其余主机」的位置）。</summary>
    public const string PreferredHosts = "PreferredHosts";

    /// <summary>Core：认证令牌前缀 + 主机名（02 篇 §3）。</summary>
    public const string AuthTokenPrefix = "AuthToken:";

    /// <summary>Core：安装过滤器前缀 + 游戏短名（KSP / KSP2）。</summary>
    public const string GlobalInstallFiltersPrefix = "GlobalInstallFilters:";

    /// <summary>
    /// 实例级文件：稳定版容差（写 <c>&lt;实例目录&gt;/CKAN/stability_tolerance.json</c>）。
    /// 它不是 Core 的 config.json 键，也不是 ckan-ui.json 字段，仅作为设置项标识用。
    /// </summary>
    public const string StabilityTolerance = "StabilityTolerance";

    /// <summary>ckan-ui.json：主题变体。</summary>
    public const string Theme = "Theme";

    /// <summary>ckan-ui.json：界面语言。</summary>
    public const string Locale = "Locale";

    /// <summary>ckan-ui.json：界面密度（compact / standard / comfortable）。</summary>
    public const string Density = "Density";

    /// <summary>ckan-ui.json：版本号隐藏 epoch 前缀（F-54）。</summary>
    public const string HideVersionEpoch = "HideVersionEpoch";

    /// <summary>ckan-ui.json：版本号隐藏 v 前缀（F-54）。</summary>
    public const string HideVersionVPrefix = "HideVersionVPrefix";

    /// <summary>ckan-ui.json：模组列表自动把可升级项排到前面（F-54）。</summary>
    public const string AutoSortUpdates = "AutoSortUpdates";

    /// <summary>ckan-ui.json：不再询问推荐模组（F-35 的「不再提示」落盘）。</summary>
    public const string SuppressRecommendations = "SuppressRecommendations";

    /// <summary>拼出某个主机的认证令牌键。</summary>
    public static string AuthToken(string host) => AuthTokenPrefix + host;

    /// <summary>拼出某个游戏的安装过滤器键。</summary>
    public static string GlobalInstallFilters(string gameShortName) => GlobalInstallFiltersPrefix + gameShortName;
}
