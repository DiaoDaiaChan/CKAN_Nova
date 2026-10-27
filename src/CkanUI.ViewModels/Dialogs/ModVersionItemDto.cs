namespace CkanUI.ViewModels.Dialogs;

/// <summary>
/// 模组版本列表条目 DTO（F-30 版本页 / UI-03 详情面板版本页签）。
/// 包含版本号、KSP 兼容范围、发布时间、下载体积、下载链接、是否当前已安装、是否兼容等信息。
/// </summary>
public sealed record ModVersionItemDto(
    string Version,
    string? GameCompatibility = null,
    DateTimeOffset? ReleaseDate = null,
    long? DownloadSizeBytes = null,
    string? DownloadUrl = null,
    bool IsInstalled = false,
    bool IsCompatible = true,
    bool IsUnstable = false)
{
    /// <summary>格式化后的版本号。</summary>
    public string VersionText => ModDetailsFormatter.FormatVersion(Version);

    /// <summary>KSP / 游戏兼容性文本。</summary>
    public string CompatibilityText => string.IsNullOrWhiteSpace(GameCompatibility) ? "未知兼容" : GameCompatibility;

    /// <summary>发布日期展示文案（yyyy-MM-dd 或 "未知"）。</summary>
    public string ReleaseDateText => ReleaseDate?.ToString("yyyy-MM-dd") ?? "未知";

    /// <summary>下载体积展示文案。</summary>
    public string DownloadSizeText => ModDetailsFormatter.FormatSize(DownloadSizeBytes);

    /// <summary>是否有下载链接。</summary>
    public bool HasDownloadUrl => !string.IsNullOrWhiteSpace(DownloadUrl);

    /// <summary>安装状态标记文本。</summary>
    public string InstalledStatusText => IsInstalled ? "当前已安装" : "未安装";

    /// <summary>综合兼容与稳定性状态文案（双通道，非纯颜色）。</summary>
    public string StatusBadgeText
    {
        get
        {
            if (IsInstalled)
            {
                return "已安装";
            }

            if (!IsCompatible)
            {
                return "不兼容";
            }

            if (IsUnstable)
            {
                return "不稳定/测试版";
            }

            return "兼容可用";
        }
    }
}
