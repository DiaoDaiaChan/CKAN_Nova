namespace CkanUI.Services.Abstractions.Dtos;

/// <summary>SpaceDock 浏览接口的一页结果。</summary>
/// <param name="Total">符合条件的模组总数。</param>
/// <param name="Page">当前页码（从 1 开始）。</param>
/// <param name="Pages">总页数。</param>
/// <param name="Mods">当前页的模组。</param>
public sealed record SpaceDockBrowsePage(
    int Total,
    int Page,
    int Pages,
    IReadOnlyList<SpaceDockModInfo> Mods);
