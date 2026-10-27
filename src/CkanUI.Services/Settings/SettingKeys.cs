namespace CkanUI.Services.Settings;

/// <summary>
/// ISettingsService 的键名约定（02 篇 §3）：标量键与 Core IConfiguration 属性同名；
/// 字典型配置用「前缀 + 子键」表达，例如 <c>AuthToken:spacedock.info</c>、<c>GlobalInstallFilters:KSP</c>。
/// </summary>
public static class SettingKeys
{
    public const string AutoStartInstance = nameof(AutoStartInstance);
    public const string DownloadCacheDir = nameof(DownloadCacheDir);
    public const string CacheSizeLimit = nameof(CacheSizeLimit);
    public const string RefreshRate = nameof(RefreshRate);
    public const string Language = nameof(Language);
    public const string DevBuilds = nameof(DevBuilds);

    /// <summary>值为 JSON 字符串数组；数组里的 null 表示「其余主机」的位置（Core 语义）。</summary>
    public const string PreferredHosts = nameof(PreferredHosts);

    /// <summary>前缀 + 主机名；值为 null/空 表示删除该令牌。</summary>
    public const string AuthTokenPrefix = "AuthToken:";

    /// <summary>前缀 + 游戏短名（KSP / KSP2）；值为 JSON 字符串数组，null/空数组 表示清除。</summary>
    public const string GlobalInstallFiltersPrefix = "GlobalInstallFilters:";

    /// <summary>全部标量键（GetAllAsync 的固定部分）。</summary>
    public static IReadOnlyList<string> Scalars { get; } = new[]
    {
        AutoStartInstance, DownloadCacheDir, CacheSizeLimit, RefreshRate, Language, DevBuilds, PreferredHosts,
    };
}
