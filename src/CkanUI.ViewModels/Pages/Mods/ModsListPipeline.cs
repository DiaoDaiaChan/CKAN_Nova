using CkanUI.Services.Abstractions.Dtos;

namespace CkanUI.ViewModels.Pages.Mods;

/// <summary>
/// 列表加工管线（纯函数，VM-01 / 04 篇 §2.1）：搜索 → 过滤 → 排序，一次生成整张表。
/// 页面只在结果上做「一次 SetProperty」，不做增量 CollectionChanged（3,000 项增量的卡顿主因）。
/// </summary>
public static class ModsListPipeline
{
    /// <summary>对全量行应用查询、过滤器与排序。</summary>
    public static IReadOnlyList<ModRowData> Apply(
        IReadOnlyList<ModRowData> all,
        ModsQuery? query,
        ModsInstallFilter install,
        ModsCompatibilityFilter compatibility,
        ModsSortColumn column,
        bool ascending)
    {
        ArgumentNullException.ThrowIfNull(all);

        var matches = new List<ModRowData>(all.Count);
        foreach (var row in all)
        {
            if (query is not null && !query.Matches(row))
            {
                continue;
            }

            if (!ModsRowFilter.Matches(row, install, compatibility))
            {
                continue;
            }

            matches.Add(row);
        }

        return ModsRowSorter.Sort(matches, column, ascending);
    }
}
