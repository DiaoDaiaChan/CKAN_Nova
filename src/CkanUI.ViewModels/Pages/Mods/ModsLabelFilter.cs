using CkanUI.Services.Abstractions.Dtos;

namespace CkanUI.ViewModels.Pages.Mods;

/// <summary>
/// 标签组过滤（12 篇 F-24）：多选为"或"，与搜索语法里的 `label:` 条件（同组内为"与"）互补。
/// </summary>
/// <remarks>
/// 纯函数，不碰服务、不碰 IO：成员关系来自行投影的 <see cref="ModSearchFacts.Labels"/>
/// （服务层从上游 <c>ModuleLabelList</c> 投影），因此过滤在 headless 下可测。
/// 名称比较忽略大小写与空格，与 <see cref="ModsQuery"/> 的 <c>label:</c> 匹配保持一致。
/// </remarks>
public static class ModsLabelFilter
{
    /// <summary>该行是否通过标签过滤：未选任何标签时全部通过，否则命中任一选中标签即可。</summary>
    public static bool Matches(ModRowData row, IReadOnlyCollection<string>? selected)
    {
        if (selected is null || selected.Count == 0) return true;
        if (row.Facts.Labels is not { Count: > 0 } labels) return false;

        foreach (var label in labels)
        {
            if (string.IsNullOrWhiteSpace(label)) continue;
            var normalized = label.Replace(" ", string.Empty);
            foreach (var name in selected)
            {
                if (string.Equals(normalized, name?.Replace(" ", string.Empty), StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
        }

        return false;
    }

    /// <summary>该行是否属于指定标签组。</summary>
    public static bool HasLabel(ModRowData row, string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return false;
        if (row.Facts.Labels is not { Count: > 0 } labels) return false;

        var target = name.Replace(" ", string.Empty);
        foreach (var label in labels)
        {
            if (string.Equals(label?.Replace(" ", string.Empty), target, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>该行的标签组名称（去掉空项，保持原有顺序）。</summary>
    public static IReadOnlyList<string> NamesOf(ModRowData row)
    {
        if (row.Facts.Labels is not { Count: > 0 } labels) return Array.Empty<string>();

        var names = new List<string>(labels.Count);
        foreach (var label in labels)
        {
            if (!string.IsNullOrWhiteSpace(label)) names.Add(label);
        }
        return names;
    }
}
