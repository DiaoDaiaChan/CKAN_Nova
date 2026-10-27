using CkanUI.Services.Abstractions.Dtos;

namespace CkanUI.ViewModels.Pages.Mods;

/// <summary>
/// 列表加工管线（纯函数，VM-01 / 04 篇 §2.1）：搜索 → 过滤 → 排序，一次生成整张表。
/// 页面只在结果上做「一次 SetProperty」，不做增量 CollectionChanged（3,000 项增量的卡顿主因）。
/// </summary>
public static class ModsListPipeline
{
    /// <summary>对全量行应用查询、过滤器与排序。</summary>
    /// <param name="labels">
    /// 选中的标签组名称（12 篇 F-24）。null 或空表示不按标签过滤；多选为"或"（见 <see cref="ModsLabelFilter"/>）。
    /// 追加在末尾并带默认值，既有调用点无需改动。
    /// </param>
    public static IReadOnlyList<ModRowData> Apply(
        IReadOnlyList<ModRowData> all,
        ModsQuery? query,
        ModsInstallFilter install,
        ModsCompatibilityFilter compatibility,
        ModsSortColumn column,
        bool ascending,
        IReadOnlyCollection<string>? labels = null)
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

            if (!ModsLabelFilter.Matches(row, labels))
            {
                continue;
            }

            matches.Add(row);
        }

        return ModsRowSorter.Sort(matches, column, ascending);
    }
}
