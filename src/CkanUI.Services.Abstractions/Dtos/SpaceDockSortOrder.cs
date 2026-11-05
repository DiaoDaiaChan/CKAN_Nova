namespace CkanUI.Services.Abstractions.Dtos;

/// <summary>SpaceDock 浏览列表的排序维度（api.md 的 orderby 参数）。</summary>
public enum SpaceDockSortOrder
{
    /// <summary>按名称。</summary>
    Name,

    /// <summary>按最近更新。</summary>
    Updated,

    /// <summary>按发布时间。</summary>
    Created,
}
