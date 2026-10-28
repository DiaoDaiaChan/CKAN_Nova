namespace CkanUI.Services.Search;

using System;
using System.Collections.Generic;
using System.Linq;
using CkanUI.Services.Abstractions;
using CkanUI.Services.Abstractions.Dtos;
using CkanUI.Services.Models;

/// <summary>
/// 基于上游 <c>ModSearch.getCombined</c> 的格式化实现（12 篇 F-20/F-21）。
/// 直接复用上游构造函数，前缀、否定 <c>-</c>、标签组去空格、<c>is:</c>/<c>not:</c>
/// 全部由它决定，与 <c>ModSearch.Parse</c> 天然互为逆运算。
/// </summary>
public sealed class ModSearchFormatter : IModSearchFormatter
{
    /// <inheritdoc />
    public string Format(ModSearchSpec spec)
    {
        ArgumentNullException.ThrowIfNull(spec);
        if (spec.IsEmpty)
        {
            return string.Empty;
        }

        // 传 null 的 allLabels/instance：标签组按名字前缀匹配即可，不需要实例上下文。
        var search = new ModSearch(
            null, null,
            spec.Name ?? string.Empty,
            Copy(spec.Authors) ?? new List<string>(),
            spec.Description ?? string.Empty,
            Copy(spec.Licenses),
            Copy(spec.Localizations),
            Copy(spec.DependsOn),
            Copy(spec.Recommends),
            Copy(spec.Suggests),
            Copy(spec.ConflictsWith),
            Copy(spec.Supports),
            Copy(spec.Tags),
            Copy(spec.Labels),
            spec.Compatible,
            spec.Installed,
            spec.Cached,
            spec.NewlyCompatible,
            spec.Upgradeable,
            spec.Replaceable);
        return search.Combined ?? string.Empty;
    }

    private static List<string>? Copy(IReadOnlyList<string>? values)
    {
        if (values is null || values.Count == 0)
        {
            return null;
        }

        // 上游会对列表就地排序/去重，必须给副本，别把调用方的 DTO 改了。
        return values.Where(v => !string.IsNullOrWhiteSpace(v)).Select(v => v.Trim()).ToList();
    }
}
