namespace CkanUI.Services.Abstractions.Dtos;

/// <summary>下载统计的分组维度（12 篇 F-47 / 06 篇 UI-25）。</summary>
public enum DownloadStatsDimension
{
    /// <summary>按模组分组（与上游 DownloadStatisticsPieChart 同口径）。</summary>
    Mods,

    /// <summary>按标签分组；一个模组有多个标签时会在每个标签下各计一次。</summary>
    Tags,

    /// <summary>按安装状态分组。</summary>
    Installed,

    /// <summary>按仓库分组。</summary>
    Repositories,
}

/// <summary>明细表的排序字段。</summary>
public enum DownloadStatsSortColumn
{
    /// <summary>按下载量。</summary>
    Downloads,

    /// <summary>按缓存占用。</summary>
    Cache,

    /// <summary>按名称。</summary>
    Name,
}

/// <summary>一次统计请求。</summary>
/// <param name="Dimension">饼图分组维度。</param>
/// <param name="TopLimit">明细表最多返回多少行；<c>0</c> 表示不限。</param>
/// <param name="Sort">明细表排序字段。</param>
/// <param name="Descending">是否降序。</param>
public sealed record DownloadStatsRequest(
    DownloadStatsDimension Dimension = DownloadStatsDimension.Mods,
    int TopLimit = 20,
    DownloadStatsSortColumn Sort = DownloadStatsSortColumn.Downloads,
    bool Descending = true);

/// <summary>饼图的一个扇区。</summary>
/// <param name="Label">分组名。</param>
/// <param name="Count">该组的下载量合计。</param>
/// <param name="Bytes">该组的缓存占用合计。</param>
/// <param name="Percent">占全部的百分比（0–100，一位小数）。</param>
/// <param name="Unavailable">该组里有模组的下载量不可用（<c>0</c> 或缺字段）。</param>
public sealed record DownloadStatsSlice(string Label, long Count, long Bytes, double Percent, bool Unavailable = false);

/// <summary>明细表的一行。</summary>
public sealed record DownloadStatsRow(string Identifier, string Name, long DownloadCount, long CacheBytes, bool IsInstalled);

/// <summary>聚合输入：仓库索引投影 + 缓存条目 + 仓库映射字典。</summary>
public sealed record DownloadStatsInput(
    IReadOnlyList<ModRowData> Modules,
    IReadOnlyList<CacheEntryInfo> Cache,
    IReadOnlyDictionary<string, string>? Repositories = null)
{
    /// <summary>空输入。</summary>
    public static DownloadStatsInput Empty { get; } =
        new(Array.Empty<ModRowData>(), Array.Empty<CacheEntryInfo>());
}

/// <summary>一次统计的结果。</summary>
/// <param name="Slices">饼图扇区（最多 8 段，末尾可能合并为「其他」）。</param>
/// <param name="Rows">按请求排序并截断后的明细行。</param>
/// <param name="ModuleCount">参与统计的模组数。</param>
/// <param name="TotalDownloads">下载量合计。</param>
/// <param name="CacheTotalBytes">缓存占用合计。</param>
/// <param name="Unavailable">是否存在下载量不可用的模组。</param>
public sealed record DownloadStatsReport(
    IReadOnlyList<DownloadStatsSlice> Slices,
    IReadOnlyList<DownloadStatsRow> Rows,
    int ModuleCount,
    long TotalDownloads,
    long CacheTotalBytes,
    bool Unavailable)
{
    /// <summary>空报告。</summary>
    public static DownloadStatsReport Empty { get; } = new(
        Array.Empty<DownloadStatsSlice>(), Array.Empty<DownloadStatsRow>(), 0, 0, 0, false);
}
