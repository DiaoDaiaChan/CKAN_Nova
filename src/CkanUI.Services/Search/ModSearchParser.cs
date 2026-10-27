using CkanUI.Services.Abstractions;
using CkanUI.Services.Abstractions.Dtos;
using CkanUI.Services.Models;

namespace CkanUI.Services.Search;

/// <summary>
/// <see cref="IModSearchParser"/> 的实现（03 篇 §3.4 / 12 篇 F-20/F-21）：
/// 直接调用模型层 <c>ModSearch.Parse(string)</c> 得到结构化条件，再投影成
/// <see cref="ModSearchSpec"/>，视图模型因此不再需要自己维护前缀白名单。
/// </summary>
/// <remarks>
/// <para>
/// 上游 <c>ModSearch</c> 有两处会静默忽略输入：`is:`/`not:` 的未知状态值，以及未知的
/// `前缀:`（后者会整体回落成名称搜索）。本类在投影时额外扫描一遍原始 token，
/// 把这些情况记进 <see cref="ModSearchSpec.Unsupported"/>，由页面在状态栏说明，
/// 不改变 <c>ModSearch</c> 的任何行为。
/// </para>
/// <para>
/// 需要实例上下文的条件（`dep:`/`rec:`/`sug:`/`conf:`/`sup:`/`label:`/`is:cached`/
/// `is:newly-compatible`/`is:replaceable`）在这里照常解析成条件，事实值由
/// <see cref="Models.ModSearchFacts"/> 随行提供，因此不需要降级成"不支持"。
/// </para>
/// </remarks>
public sealed class ModSearchParser : IModSearchParser
{
    /// <summary>`is:`/`not:` 认的状态值（与 <c>ModSearch.Parse</c> 一样按序数比较）。</summary>
    private static readonly HashSet<string> KnownStates = new(StringComparer.Ordinal)
    {
        "compatible",
        "installed",
        "cached",
        "newly-compatible",
        "upgradeable",
        "replaceable",
    };

    /// <summary>已支持的前缀（不含 `@作者`，它没有冒号）。</summary>
    private static readonly HashSet<string> KnownPrefixes = new(StringComparer.Ordinal)
    {
        "desc:", "lic:", "lang:", "dep:", "rec:", "sug:", "conf:", "sup:",
        "tag:", "label:", "is:", "not:",
    };

    public ModSearchSpec Parse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return ModSearchSpec.Empty;

        var search = ModSearch.Parse(text);
        return new ModSearchSpec
        {
            Name = search.Name,
            Description = search.Description,
            Authors = Copy(search.Authors),
            Licenses = Copy(search.Licenses),
            Localizations = Copy(search.Localizations),
            DependsOn = Copy(search.DependsOn),
            Recommends = Copy(search.Recommends),
            Suggests = Copy(search.Suggests),
            ConflictsWith = Copy(search.ConflictsWith),
            Supports = Copy(search.Supports),
            Tags = Copy(search.TagNames),
            Labels = Copy(search.LabelNames),
            Compatible = search.Compatible,
            Installed = search.Installed,
            Cached = search.Cached,
            NewlyCompatible = search.NewlyCompatible,
            Upgradeable = search.Upgradeable,
            Replaceable = search.Replaceable,
            Raw = text.Trim(),
            Unsupported = FindUnsupported(text),
        };
    }

    /// <summary>扫描原始 token，找出上游会静默丢弃的条件（不重复、按出现顺序）。</summary>
    private static IReadOnlyList<ModSearchUnsupportedTerm> FindUnsupported(string text)
    {
        List<ModSearchUnsupportedTerm>? unsupported = null;
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var piece in text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
        {
            var token = piece.StartsWith('-') && piece.Length > 1 ? piece[1..] : piece;
            if (token.StartsWith('@')) continue;

            var colon = token.IndexOf(':');
            if (colon <= 0) continue;

            var prefix = token[..(colon + 1)];
            var value = token[(colon + 1)..];
            ModSearchUnsupportedReason? reason =
                prefix is "is:" or "not:"
                    ? (KnownStates.Contains(value) ? null : ModSearchUnsupportedReason.UnknownState)
                    : !KnownPrefixes.Contains(prefix)
                        ? ModSearchUnsupportedReason.UnknownPrefix
                        : value.Length == 0
                            ? ModSearchUnsupportedReason.EmptyValue
                            : null;

            if (reason is null || !seen.Add(piece)) continue;
            (unsupported ??= new List<ModSearchUnsupportedTerm>())
                .Add(new ModSearchUnsupportedTerm(piece, reason.Value));
        }

        return unsupported is null
            ? Array.Empty<ModSearchUnsupportedTerm>()
            : unsupported;
    }

    private static IReadOnlyList<string> Copy(List<string>? source)
        => source is { Count: > 0 } ? source.ToArray() : Array.Empty<string>();
}
