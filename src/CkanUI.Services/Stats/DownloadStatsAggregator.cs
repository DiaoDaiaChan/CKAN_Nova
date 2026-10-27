using CkanUI.Services.Abstractions.Dtos;

namespace CkanUI.Services.Stats;

/// <summary>
/// 下载统计的纯聚合（12 篇 F-47）：不碰文件、不联网、不起任务，便于逐条断言。
/// </summary>
public static class DownloadStatsAggregator
{
    /// <summary>饼图最多显示的分段数；超出部分合并为「其他」。超过 8 段即合并。</summary>
    public const int MaxSlices = 8;

    /// <summary>下载量不可用的分组名。</summary>
    public const string UnavailableLabel = "下载量不可用";

    /// <summary>无标签模组的分组名。</summary>
    public const string NoTagLabel = "无标签";

    /// <summary>安装状态分组的两个名字。</summary>
    public const string InstalledLabel = "已安装";

    /// <summary>未安装分组的名字。</summary>
    public const string NotInstalledLabel = "未安装";

    /// <summary>未知仓库分组名。</summary>
    public const string UnknownRepositoryLabel = "未知仓库";

    /// <summary>合并段的名字。</summary>
    public const string OtherLabel = "其他";

    /// <summary>按请求聚合。</summary>
    public static DownloadStatsReport Compute(DownloadStatsInput input, DownloadStatsRequest request)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(request);

        var modules = input.Modules;
        var cache = input.Cache;

        var cacheBytes = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in cache)
        {
            if (string.IsNullOrWhiteSpace(entry.Identifier))
            {
                continue;
            }

            cacheBytes[entry.Identifier] = cacheBytes.GetValueOrDefault(entry.Identifier)
                + Math.Max(0L, entry.SizeBytes);
        }

        var rows = new List<DownloadStatsRow>(modules.Count);
        var totalDownloads = 0L;
        var hasUnavailable = false;
        foreach (var module in modules)
        {
            if (string.IsNullOrWhiteSpace(module.Identifier))
            {
                continue;
            }

            var downloads = Math.Max(0L, module.DownloadCount);
            if (downloads == 0)
            {
                hasUnavailable = true;
            }

            totalDownloads += downloads;
            rows.Add(new DownloadStatsRow(
                module.Identifier,
                module.Name,
                downloads,
                cacheBytes.GetValueOrDefault(module.Identifier),
                module.IsInstalled));
        }

        var slices = BuildSlices(modules, request.Dimension, input.Repositories);
        var sorted = Sort(rows, request.Sort, request.Descending);
        if (request.TopLimit > 0 && sorted.Count > request.TopLimit)
        {
            sorted = sorted.GetRange(0, request.TopLimit);
        }

        return new DownloadStatsReport(
            slices,
            sorted,
            rows.Count,
            totalDownloads,
            cacheBytes.Values.Sum(),
            hasUnavailable);
    }

    private static List<DownloadStatsSlice> BuildSlices(
        IReadOnlyList<ModRowData> modules,
        DownloadStatsDimension dimension,
        IReadOnlyDictionary<string, string>? repositories)
    {
        var groups = new Dictionary<string, (long Count, long Bytes, bool Unavailable)>(StringComparer.OrdinalIgnoreCase);
        foreach (var module in modules)
        {
            if (string.IsNullOrWhiteSpace(module.Identifier))
            {
                continue;
            }

            var downloads = Math.Max(0L, module.DownloadCount);
            var unavailable = downloads == 0;
            foreach (var label in LabelsFor(module, dimension, repositories))
            {
                var previous = groups.GetValueOrDefault(label);
                groups[label] = (
                    previous.Count + downloads,
                    previous.Bytes,
                    previous.Unavailable || unavailable);
            }
        }

        var ordered = groups
            .OrderByDescending(pair => pair.Value.Count)
            .ThenBy(pair => pair.Key, StringComparer.Ordinal)
            .ToList();

        if (ordered.Count > MaxSlices)
        {
            var kept = ordered.Take(MaxSlices - 1).ToList();
            var rest = ordered.Skip(MaxSlices - 1).ToList();
            var otherCount = rest.Sum(pair => pair.Value.Count);
            var otherUnavailable = rest.Any(pair => pair.Value.Unavailable);
            ordered = kept;
            ordered.Add(new KeyValuePair<string, (long, long, bool)>(OtherLabel, (otherCount, 0L, otherUnavailable)));
        }

        var basis = ordered.Sum(pair => pair.Value.Count);
        var slices = new List<DownloadStatsSlice>(ordered.Count);
        foreach (var pair in ordered)
        {
            var percent = basis == 0
                ? 0d
                : Math.Round(pair.Value.Count * 100d / basis, 1);
            slices.Add(new DownloadStatsSlice(pair.Key, pair.Value.Count, pair.Value.Bytes, percent, pair.Value.Unavailable));
        }

        return slices;
    }

    private static IEnumerable<string> LabelsFor(
        ModRowData module,
        DownloadStatsDimension dimension,
        IReadOnlyDictionary<string, string>? repositories)
    {
        switch (dimension)
        {
            case DownloadStatsDimension.Tags:
                if (module.Tags.Count == 0)
                {
                    return new[] { NoTagLabel };
                }

                return module.Tags.Where(tag => !string.IsNullOrWhiteSpace(tag)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();

            case DownloadStatsDimension.Installed:
                return new[] { module.IsInstalled ? InstalledLabel : NotInstalledLabel };

            case DownloadStatsDimension.Repositories:
                if (repositories is not null && repositories.TryGetValue(module.Identifier, out var repoName) && !string.IsNullOrWhiteSpace(repoName))
                {
                    return new[] { repoName };
                }
                return new[] { UnknownRepositoryLabel };

            default:
                return new[] { module.DownloadCount <= 0 ? UnavailableLabel : module.Name.Length == 0 ? module.Identifier : module.Name };
        }
    }

    private static List<DownloadStatsRow> Sort(
        List<DownloadStatsRow> rows,
        DownloadStatsSortColumn sort,
        bool descending)
    {
        var comparer = sort switch
        {
            DownloadStatsSortColumn.Cache => Comparer<DownloadStatsRow>.Create((left, right) => left.CacheBytes.CompareTo(right.CacheBytes)),
            DownloadStatsSortColumn.Name => Comparer<DownloadStatsRow>.Create((left, right) => string.Compare(left.Name, right.Name, StringComparison.CurrentCultureIgnoreCase)),
            _ => Comparer<DownloadStatsRow>.Create((left, right) => left.DownloadCount.CompareTo(right.DownloadCount)),
        };

        var sorted = descending
            ? rows.OrderByDescending(row => row, comparer).ThenBy(row => row.Identifier, StringComparer.Ordinal).ToList()
            : rows.OrderBy(row => row, comparer).ThenBy(row => row.Identifier, StringComparer.Ordinal).ToList();

        return sorted;
    }
}
