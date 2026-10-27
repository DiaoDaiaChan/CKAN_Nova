using CkanUI.Services.Abstractions.Dtos;

namespace CkanUI.ViewModels.Pages.Mods;

/// <summary>
/// 模组搜索查询（03 篇 §3.4 / VM-01）。把与老 GUI `GUI/Model/ModSearch.cs` 同构的查询语法
/// （`名称`、`@作者`、`tag:`、`is:`、`not:`、`-否定`）解析成对 <see cref="ModRowData"/> 的纯谓词。
/// </summary>
/// <remarks>
/// 为什么不直接复用 <c>CkanUI.Services.Models.ModSearch</c>：它接收 <c>GUIMod</c> 与 Core 的
/// <c>GameInstance</c>/<c>ModuleLabelList</c>，而架构铁律（01 篇 §11 / DependencyRulesTests）
/// 禁止 ViewModels 引用 CkanUI.Services 与 CKAN 命名空间。因此这里只保留 VM 侧可判定的子集，
/// 无法判定的前缀（desc:/lic:/lang:/dep:/rec:/sug:/conf:/sup:/label:）记入 <see cref="UnsupportedTerms"/>，
/// 由页面在状态栏给出明确提示，而不是静默地"搜不到东西"。
/// </remarks>
public sealed class ModsQuery
{
    private readonly ModsQueryTerm[] _terms;
    private readonly string[] _unsupported;

    private ModsQuery(string rawText, ModsQueryTerm[] terms, string[] unsupported)
    {
        RawText = rawText;
        _terms = terms;
        _unsupported = unsupported;
    }

    /// <summary>用户在搜索框里输入的原文。</summary>
    public string RawText { get; }

    /// <summary>当前 VM 无法判定的查询词（保留原样，便于在界面上提示）。</summary>
    public IReadOnlyList<string> UnsupportedTerms => _unsupported;

    /// <summary>是否有无法判定的查询词。</summary>
    public bool HasUnsupportedTerms => _unsupported.Length > 0;

    /// <summary>空查询等于"全部命中"（03 篇 §3.4）。</summary>
    public bool IsEmpty => _terms.Length == 0;

    /// <summary>解析一段查询文本；空文本返回匹配全部的查询（不抛异常）。</summary>
    public static ModsQuery Parse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return new ModsQuery(text ?? string.Empty, Array.Empty<ModsQueryTerm>(), Array.Empty<string>());
        }

        var terms = new List<ModsQueryTerm>();
        var unsupported = new List<string>();

        foreach (var raw in text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
        {
            var token = raw;
            var negate = false;
            if (token.Length > 1 && token[0] == '-')
            {
                negate = true;
                token = token[1..];
            }

            if (TryParseTerm(token, negate, out var term))
            {
                terms.Add(term);
            }
            else
            {
                unsupported.Add(raw);
            }
        }

        return new ModsQuery(text, terms.ToArray(), unsupported.ToArray());
    }

    /// <summary>该行是否命中全部查询词（多个查询词之间是逻辑与）。</summary>
    public bool Matches(ModRowData row)
    {
        foreach (var term in _terms)
        {
            var hit = MatchesTerm(row, term);
            if (term.Negate ? hit : !hit)
            {
                return false;
            }
        }

        return true;
    }

    private static bool TryParseTerm(string token, bool negate, out ModsQueryTerm term)
    {
        term = default;

        if (token.Length == 0)
        {
            return false;
        }

        if (token[0] == '@')
        {
            var author = token[1..];
            if (author.Length == 0)
            {
                return false;
            }

            term = new ModsQueryTerm(ModsQueryTermKind.Author, author, negate);
            return true;
        }

        var separator = token.IndexOf(':', StringComparison.Ordinal);
        if (separator <= 0)
        {
            term = new ModsQueryTerm(ModsQueryTermKind.Text, token, negate);
            return true;
        }

        var prefix = token[..separator].ToLowerInvariant();
        var value = token[(separator + 1)..];
        if (value.Length == 0)
        {
            return false;
        }

        switch (prefix)
        {
            case "tag":
                term = new ModsQueryTerm(ModsQueryTermKind.Tag, value, negate);
                return true;
            case "is":
            case "not":
                return TryParseStateTerm(prefix, value, negate, out term);
            case "desc":
            case "lic":
            case "lang":
            case "dep":
            case "rec":
            case "sug":
            case "conf":
            case "sup":
            case "label":
                // 语法合法，但 ModRowData 里没有对应字段（描述/许可证/依赖/标签组），交给状态栏提示。
                return false;
            default:
                // 未知前缀按老 GUI 的语义整体当作名称文本处理（ModSearch 的回落分支）。
                term = new ModsQueryTerm(ModsQueryTermKind.Text, token, negate);
                return true;
        }
    }

    private static bool TryParseStateTerm(string prefix, string value, bool negate, out ModsQueryTerm term)
    {
        term = default;
        var excluded = negate || string.Equals(prefix, "not", StringComparison.OrdinalIgnoreCase);

        var kind = value.ToLowerInvariant() switch
        {
            "installed" => ModsQueryTermKind.Installed,
            "compatible" => ModsQueryTermKind.Compatible,
            "upgradeable" or "update" or "outdated" => ModsQueryTermKind.Upgradeable,
            "autoinstalled" or "auto-installed" => ModsQueryTermKind.AutoInstalled,
            // 缓存/替换/新兼容等状态不来自 ModRowData，无法在 VM 侧判定。
            _ => ModsQueryTermKind.Unknown,
        };

        if (kind == ModsQueryTermKind.Unknown)
        {
            return false;
        }

        term = new ModsQueryTerm(kind, string.Empty, excluded);
        return true;
    }

    private static bool MatchesTerm(ModRowData row, ModsQueryTerm term) => term.Kind switch
    {
        ModsQueryTermKind.Text => Contains(row.Name, term.Value)
                                 || Contains(row.Identifier, term.Value)
                                 || Contains(row.Author, term.Value),
        ModsQueryTermKind.Author => Contains(row.Author, term.Value),
        ModsQueryTermKind.Tag => HasTag(row, term.Value),
        ModsQueryTermKind.Installed => row.IsInstalled,
        ModsQueryTermKind.Compatible => row.IsCompatible,
        ModsQueryTermKind.Upgradeable => row.HasUpdate,
        ModsQueryTermKind.AutoInstalled => row.IsAutoInstalled,
        _ => true,
    };

    private static bool Contains(string? haystack, string needle) =>
        !string.IsNullOrEmpty(haystack) && haystack.Contains(needle, StringComparison.OrdinalIgnoreCase);

    private static bool HasTag(ModRowData row, string tag)
    {
        if (row.Tags is not { Count: > 0 } tags)
        {
            return false;
        }

        foreach (var candidate in tags)
        {
            if (string.Equals(candidate, tag, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }
}

/// <summary>查询词种类（内部实现细节，测试只走 <see cref="ModsQuery"/> 的公开面）。</summary>
internal enum ModsQueryTermKind
{
    /// <summary>名称/标识符/作者文本。</summary>
    Text,

    /// <summary>`@作者`。</summary>
    Author,

    /// <summary>`tag:`。</summary>
    Tag,

    /// <summary>`is:installed` / `not:installed`。</summary>
    Installed,

    /// <summary>`is:compatible` / `not:compatible`。</summary>
    Compatible,

    /// <summary>`is:upgradeable`。</summary>
    Upgradeable,

    /// <summary>`is:autoinstalled`。</summary>
    AutoInstalled,

    /// <summary>无法判定的状态。</summary>
    Unknown,
}

/// <summary>一个已解析的查询词。</summary>
internal readonly record struct ModsQueryTerm(ModsQueryTermKind Kind, string Value, bool Negate);
