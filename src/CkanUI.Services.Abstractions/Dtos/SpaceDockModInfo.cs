namespace CkanUI.Services.Abstractions.Dtos;

/// <summary>SpaceDock 模组条目（浏览 / 搜索 / 详情三处的并集）。</summary>
/// <remarks>
/// 浏览与搜索接口不返回长描述，只有 <see cref="ShortDescription"/>；详情接口才补
/// <see cref="Description"/>。所有 URL 在服务层就补成绝对地址，页面不拼站点前缀。
/// </remarks>
public sealed record SpaceDockModInfo(
    long Id,
    string Name,
    string Author,
    string ShortDescription,
    string Description,
    long Downloads,
    int Followers,
    string? License,
    string? Website,
    string? SourceCode,
    string? BackgroundUrl,
    string Url,
    IReadOnlyList<SpaceDockVersionInfo> Versions)
{
    /// <summary>最新版本（SpaceDock 的 versions 按发布时间倒序返回）；没有版本时为空。</summary>
    public SpaceDockVersionInfo? LatestVersion => Versions.Count > 0 ? Versions[0] : null;
}
