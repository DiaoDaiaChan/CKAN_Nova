namespace CkanUI.Services.Abstractions.Dtos;

/// <summary>SpaceDock 模组的一个版本（/api/mod/&lt;id&gt; 的 versions 元素）。</summary>
/// <param name="FriendlyVersion">作者填写的版本号，例如 1.10。</param>
/// <param name="GameVersion">兼容的游戏版本，可能为空。</param>
/// <param name="DownloadUrl">已补全为绝对地址的 ZIP 下载链接。</param>
/// <param name="Changelog">更新日志（纯文本或轻量标记），可能为空。</param>
/// <param name="Downloads">该版本下载次数。</param>
/// <param name="Created">发布时间。</param>
public sealed record SpaceDockVersionInfo(
    string FriendlyVersion,
    string? GameVersion,
    string DownloadUrl,
    string? Changelog,
    long Downloads,
    DateTimeOffset? Created);
