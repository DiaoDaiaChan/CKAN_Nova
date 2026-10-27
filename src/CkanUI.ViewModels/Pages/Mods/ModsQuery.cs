using CkanUI.Services.Abstractions.Dtos;

namespace CkanUI.ViewModels.Pages.Mods;

/// <summary>
/// 模组页的搜索条件（03 篇 §3.4 / 12 篇 F-20/F-21）。
/// </summary>
/// <remarks>
/// <para>
/// 语法解析**不在本类**：文本由服务层的 <see cref="CkanUI.Services.Abstractions.IModSearchParser"/> 委托给
/// 上游 <c>ModSearch.Parse</c>，这里只消费解析好的 <see cref="ModSearchSpec"/>，逐条镜像
/// <c>ModSearch.Matches</c> 的语义去匹配 <see cref="ModRowData"/>。
/// 这样前缀白名单只有一处（模型层），页面不再维护第二份。
/// </para>
/// <para>
/// 需要实例上下文的条件（`dep:`/`rec:`/`sug:`/`conf:`/`sup:`/`label:`/`is:cached`/
/// `is:newly-compatible`/`is:replaceable`）来自行上的 <see cref="ModSearchFacts"/>，由服务层填充；
/// 解析不了的条件留在 <see cref="Unsupported"/> 里，页面必须在状态栏说明，不能静默丢弃。
/// </para>
/// </remarks>
public sealed class ModsQuery
{
    private static readonly StringComparison IgnoreCase = StringComparison.InvariantCultureIgnoreCase;

    private readonly ModSearchSpec _spec;
    private readonly IReadOnlyList<LabelGroup> _labelGroups;

    private ModsQuery(ModSearchSpec spec)
    {
        _spec = spec;
        _labelGroups = BuildLabelGroups(spec.Labels);
    }

    /// <summary>空查询：匹配全部（上游 `Parse` 对空白输入返回 null 的同义表示）。</summary>
    public static ModsQuery Empty { get; } = new(ModSearchSpec.Empty);

    /// <summary>用解析结果构造查询。</summary>
    public static ModsQuery FromSpec(ModSearchSpec? spec) => spec is null ? Empty : new ModsQuery(spec);

    /// <summary>
    /// 没有解析器时的降级查询（设计器、或者宿主没接线）：不做任何过滤，
    /// 并把每个词都记为"需要实例上下文"的无法解析条件，保证状态栏会提示而不是假装成功。
    /// </summary>
    public static ModsQuery Unparsed(string? text)
    {
        var terms = (text ?? string.Empty).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        var unsupported = new List<ModSearchUnsupportedTerm>(terms.Length);
        foreach (var term in terms)
        {
            unsupported.Add(new ModSearchUnsupportedTerm(term, ModSearchUnsupportedReason.InstanceContext));
        }

        return new ModsQuery(new ModSearchSpec
        {
            Raw = text ?? string.Empty,
            Unsupported = unsupported,
        });
    }

    /// <summary>解析结果（含原文与无法解析的词）。</summary>
    public ModSearchSpec Spec => _spec;

    /// <summary>无法解析的查询词（含原因）。</summary>
    public IReadOnlyList<ModSearchUnsupportedTerm> Unsupported => _spec.Unsupported;

    /// <summary>无法解析的查询词原文（界面 chips 用）。</summary>
    public IReadOnlyList<string> UnsupportedTerms
    {
        get
        {
            if (_spec.Unsupported.Count == 0) return Array.Empty<string>();

            var terms = new string[_spec.Unsupported.Count];
            for (var i = 0; i < terms.Length; i++) terms[i] = _spec.Unsupported[i].Term;
            return terms;
        }
    }

    /// <summary>是否存在无法解析的词（状态栏据此提示）。</summary>
    public bool HasUnsupportedTerms => _spec.Unsupported.Count > 0;

    /// <summary>是否没有任何有效条件（等于"匹配全部"）。</summary>
    public bool IsEmpty => _spec.IsEmpty;

    /// <summary>无法解析的中文说明，例如「当前无法解析：foo:bar（未知的搜索前缀）」。</summary>
    public string UnsupportedMessage
    {
        get
        {
            if (_spec.Unsupported.Count == 0) return string.Empty;

            var builder = new System.Text.StringBuilder();
            foreach (var term in _spec.Unsupported)
            {
                if (builder.Length > 0) builder.Append("；");
                builder.Append("当前无法解析：")
                       .Append(term.Term)
                       .Append('（')
                       .Append(ReasonText(term.Reason))
                       .Append('）');
            }
            return builder.ToString();
        }
    }

    /// <summary>判断行是否满足全部条件（镜像 <c>ModSearch.Matches</c>，全部条件 AND）。</summary>
    public bool Matches(ModRowData row)
        => MatchesName(row)
           && MatchesAuthors(row)
           && MatchesDescription(row)
           && MatchesLicenses(row)
           && MatchesLocalizations(row)
           && MatchesTerms(_spec.DependsOn, row.Facts.DependsOn)
           && MatchesTerms(_spec.Recommends, row.Facts.Recommends)
           && MatchesTerms(_spec.Suggests, row.Facts.Suggests)
           && MatchesTerms(_spec.ConflictsWith, row.Facts.ConflictsWith)
           && MatchesTerms(_spec.Supports, row.Facts.Supports)
           && MatchesTags(row)
           && MatchesLabels(row)
           && MatchesState(_spec.Compatible, row.IsCompatible)
           && MatchesState(_spec.Installed, row.IsInstalled)
           && MatchesState(_spec.Cached, row.Facts.IsCached)
           && MatchesState(_spec.NewlyCompatible, row.Facts.IsNew)
           && MatchesState(_spec.Upgradeable, row.HasUpdate)
           && MatchesState(_spec.Replaceable, row.IsInstalled && row.Facts.IsReplaceable);

    private bool MatchesName(ModRowData row)
    {
        if (string.IsNullOrWhiteSpace(_spec.Name)) return true;

        var negate = Negated(_spec.Name, out var sub);
        var hit = ModsSearchText.Abbreviation(row.Name).IndexOf(sub, IgnoreCase) >= 0
                  || ModsSearchText.Normalize(row.Name).IndexOf(sub, IgnoreCase) >= 0
                  || ModsSearchText.Normalize(row.Identifier).IndexOf(sub, IgnoreCase) >= 0;
        return negate ^ hit;
    }

    private bool MatchesAuthors(ModRowData row)
    {
        if (_spec.Authors.Count < 1) return true;

        var authors = AuthorsOf(row);
        foreach (var term in _spec.Authors)
        {
            var negate = Negated(term, out var sub);
            var hit = false;
            foreach (var author in authors)
            {
                if (!author.StartsWith(sub, IgnoreCase)) continue;
                hit = true;
                break;
            }
            if (!(negate ^ hit)) return false;
        }
        return true;
    }

    private bool MatchesDescription(ModRowData row)
    {
        if (string.IsNullOrWhiteSpace(_spec.Description)) return true;

        var negate = Negated(_spec.Description, out var sub);
        var hit = ModsSearchText.Normalize(row.Facts.Abstract).IndexOf(sub, IgnoreCase) >= 0
                  || ModsSearchText.Normalize(row.Facts.Description).IndexOf(sub, IgnoreCase) >= 0;
        return negate ^ hit;
    }

    private bool MatchesLicenses(ModRowData row)
    {
        if (_spec.Licenses.Count < 1) return true;
        if (row.Facts.Licenses is not { Count: > 0 } licenses) return false;

        foreach (var term in _spec.Licenses)
        {
            var negate = Negated(term, out var sub);
            var hit = false;
            foreach (var license in licenses)
            {
                if (!license.StartsWith(sub, IgnoreCase)) continue;
                hit = true;
                break;
            }
            if (!(negate ^ hit)) return false;
        }
        return true;
    }

    private bool MatchesLocalizations(ModRowData row)
    {
        if (_spec.Localizations.Count < 1) return true;
        if (row.Facts.Localizations is not { Count: > 0 } localizations) return false;

        foreach (var term in _spec.Localizations)
        {
            var negate = Negated(term, out var sub);
            var hit = false;
            foreach (var localization in localizations)
            {
                if (!localization.StartsWith(sub, IgnoreCase)) continue;
                hit = true;
                break;
            }
            if (!(negate ^ hit)) return false;
        }
        return true;
    }

    /// <summary>关系条件（`dep:`/`rec:`/`sug:`/`conf:`/`sup:`）：每个词都要在关系名前缀里出现。</summary>
    private static bool MatchesTerms(IReadOnlyList<string> terms, IReadOnlyList<string>? relationships)
    {
        if (terms.Count < 1) return true;
        if (relationships is not { Count: > 0 }) return false;

        foreach (var term in terms)
        {
            var negate = Negated(term, out var sub);
            var hit = false;
            foreach (var relationship in relationships)
            {
                if (!relationship.StartsWith(sub, IgnoreCase)) continue;
                hit = true;
                break;
            }
            if (!(negate ^ hit)) return false;
        }
        return true;
    }

    private bool MatchesTags(ModRowData row)
    {
        if (_spec.Tags.Count < 1) return true;

        foreach (var term in _spec.Tags)
        {
            var negate = Negated(term, out var sub);
            // 空子标签在上游表示"没有任何标签"；行投影里的 Tags 不是 null，只有空列表。
            var hit = sub.Length > 0
                      && row.Tags is { Count: > 0 }
                      && row.Tags.Contains(sub);
            if (!(negate ^ hit)) return false;
        }
        return true;
    }

    private bool MatchesLabels(ModRowData row)
    {
        foreach (var group in _labelGroups)
        {
            var inner = group.Exclude
                // 空标签名 = "不在任何标签组里"（对齐上游 LabelsByNegation 的 exclude 分支）。
                ? !HasAnyLabel(row)
                : group.Names.Count > 0 && group.Names.All(name => HasLabel(row, name));
            if (!(group.Negate ^ inner)) return false;
        }
        return true;
    }

    private static bool HasAnyLabel(ModRowData row) => row.Facts.Labels is { Count: > 0 };

    private static bool HasLabel(ModRowData row, string name)
    {
        if (row.Facts.Labels is not { Count: > 0 } labels) return false;

        foreach (var label in labels)
        {
            if (string.Equals(label.Replace(" ", ""), name, StringComparison.OrdinalIgnoreCase)) return true;
        }
        return false;
    }

    private static bool MatchesState(bool? condition, bool value) => !condition.HasValue || condition.Value == value;

    private static IReadOnlyList<string> AuthorsOf(ModRowData row)
    {
        if (string.IsNullOrWhiteSpace(row.Author)) return Array.Empty<string>();

        var parts = row.Author.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 0) return Array.Empty<string>();

        var authors = new string[parts.Length];
        for (var i = 0; i < parts.Length; i++) authors[i] = ModsSearchText.Normalize(parts[i]);
        return authors;
    }

    /// <summary>`-` 前缀取反（镜像上游 <c>ShouldNegateTerm</c>）。</summary>
    private static bool Negated(string term, out string subTerm)
    {
        if (term.StartsWith('-'))
        {
            subTerm = term[1..];
            return true;
        }

        subTerm = term;
        return false;
    }

    /// <summary>
    /// 标签条件按 (是否取反, 是否空名) 分组，与上游 <c>LabelsByNegation</c> 的分组语义一致：
    /// 同一组内的多个名字要求"同时属于"。
    /// </summary>
    private static IReadOnlyList<LabelGroup> BuildLabelGroups(IReadOnlyList<string> labels)
    {
        if (labels.Count < 1) return Array.Empty<LabelGroup>();

        var groups = new List<LabelGroup>();
        foreach (var raw in labels)
        {
            var negate = Negated(raw.Replace(" ", ""), out var name);
            var exclude = name.Length == 0;

            var index = groups.FindIndex(group => group.Negate == negate && group.Exclude == exclude);
            if (index < 0)
            {
                groups.Add(new LabelGroup(negate, exclude, new List<string> { name }));
            }
            else
            {
                groups[index].Names.Add(name);
            }
        }
        return groups;
    }

    private static string ReasonText(ModSearchUnsupportedReason reason) => reason switch
    {
        ModSearchUnsupportedReason.InstanceContext => "需要实例上下文",
        ModSearchUnsupportedReason.UnknownState => "未知的状态值，可用 compatible/installed/cached/newly-compatible/upgradeable/replaceable",
        ModSearchUnsupportedReason.UnknownPrefix => "未知的搜索前缀",
        ModSearchUnsupportedReason.EmptyValue => "缺少取值",
        _ => "无法解析",
    };

    private sealed class LabelGroup(bool negate, bool exclude, List<string> names)
    {
        public bool Negate { get; } = negate;
        public bool Exclude { get; } = exclude;
        public List<string> Names { get; } = names;
    }
}
