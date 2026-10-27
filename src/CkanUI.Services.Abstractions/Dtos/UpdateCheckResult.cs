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
public sealed record UpdateCheckResult(
    bool HasUpdate,
    string? CurrentVersion,
    string? LatestVersion,
    string? ReleaseNotes,
    string? ReleaseUrl,
    bool IsDevBuild);
