namespace CkanUI.Services.Abstractions.Dtos;

/// <summary>
/// 检查更新的结果契约（F-52）。
/// </summary>
/// <param name="HasUpdate">是否存在更高版本的新更新</param>
/// <param name="CurrentVersion">当前版本字符串</param>
/// <param name="LatestVersion">发现的最新版本字符串</param>
/// <param name="ReleaseNotes">发布日志 / 更新说明</param>
/// <param name="ReleaseUrl">发布地址或下载页面链接</param>
/// <param name="IsDevBuild">该新版本是否为开发预览版 (dev build)</param>
/// <param name="AssetUrl">可直接下载的更新包地址（GitHub release 资产）；为空表示上游没给包，只能打开发布页。</param>
/// <param name="AssetSha256">更新包的 SHA256（上游 digest 字段，64 位十六进制）；为空表示上游没提供校验值。</param>
/// <param name="AssetSize">更新包字节数；0 表示未知。</param>
public sealed record UpdateCheckResult(
    bool HasUpdate,
    string? CurrentVersion,
    string? LatestVersion,
    string? ReleaseNotes,
    string? ReleaseUrl,
    bool IsDevBuild,
    string? AssetUrl = null,
    string? AssetSha256 = null,
    long AssetSize = 0)
{
    /// <summary>是否给出了可直接下载的更新包（决定「立即安装」是否可用）。</summary>
    public bool HasAsset => !string.IsNullOrWhiteSpace(AssetUrl);
}
