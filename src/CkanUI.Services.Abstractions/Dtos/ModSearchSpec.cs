namespace CkanUI.Services.Abstractions.Dtos;

/// <summary>
/// 一条无法解析的搜索词（12 篇 F-20/F-21）。页面据此在状态栏给出中文提示，绝不静默忽略。
/// </summary>
/// <param name="Term">用户输入的原始查询词（含 <c>-</c> 否定前缀）。</param>
/// <param name="Reason">无法解析的原因分类。</param>
public sealed record ModSearchUnsupportedTerm(string Term, ModSearchUnsupportedReason Reason);

/// <summary>无法解析的原因分类（中文文案由视图模型给出，服务层不持有 UI 文案）。</summary>
public enum ModSearchUnsupportedReason
{
    /// <summary>语法合法，但需要游戏实例上下文（注册表/标签组）才能求值。</summary>
    InstanceContext = 0,

    /// <summary>`is:`/`not:` 后面跟了未知的状态值。</summary>
    UnknownState = 1,

    /// <summary>未知的 `前缀:` 形式，按上游语义回落成自由文本，同时提示用户。</summary>
    UnknownPrefix = 2,

    /// <summary>已知前缀但后面没写值（例如只输入 <c>lic:</c>），条件无从求值。</summary>
    EmptyValue = 3,
}

/// <summary>
/// 结构化搜索条件（12 篇 F-20/F-21）：服务层用 <c>CkanUI.Services.Models.ModSearch.Parse</c>
/// 解析用户输入后，投影成这份只依赖 Abstractions 的 DTO，视图模型据此匹配 <see cref="ModRowData"/>。
/// </summary>
/// <remarks>
/// 字段与上游 <c>ModSearch</c> 的公开字段一一对应（名称已按 <c>nonAlphaNumsAnyLanguage</c> 规范化），
/// 因此视图模型的匹配逻辑可以逐条镜像 <c>ModSearch.Matches</c>，不必再自己维护前缀白名单。
/// </remarks>
public sealed record ModSearchSpec
{
    /// <summary>空查询（匹配全部，03 篇 §3.4）。</summary>
    public static ModSearchSpec Empty { get; } = new();

    /// <summary>名称/标识符/缩写文本（已去标点）。</summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>描述文本（已去标点，多段直接相连）。</summary>
    public string Description { get; init; } = string.Empty;

    /// <summary>`@作者` 条件。</summary>
    public IReadOnlyList<string> Authors { get; init; } = Array.Empty<string>();

    /// <summary>`lic:` 条件。</summary>
    public IReadOnlyList<string> Licenses { get; init; } = Array.Empty<string>();

    /// <summary>`lang:` 条件。</summary>
    public IReadOnlyList<string> Localizations { get; init; } = Array.Empty<string>();

    /// <summary>`dep:` 条件。</summary>
    public IReadOnlyList<string> DependsOn { get; init; } = Array.Empty<string>();

    /// <summary>`rec:` 条件。</summary>
    public IReadOnlyList<string> Recommends { get; init; } = Array.Empty<string>();

    /// <summary>`sug:` 条件。</summary>
    public IReadOnlyList<string> Suggests { get; init; } = Array.Empty<string>();

    /// <summary>`conf:` 条件。</summary>
    public IReadOnlyList<string> ConflictsWith { get; init; } = Array.Empty<string>();

    /// <summary>`sup:` 条件。</summary>
    public IReadOnlyList<string> Supports { get; init; } = Array.Empty<string>();

    /// <summary>`tag:` 条件。</summary>
    public IReadOnlyList<string> Tags { get; init; } = Array.Empty<string>();

    /// <summary>`label:` 条件（标签组名，已去掉空格）。</summary>
    public IReadOnlyList<string> Labels { get; init; } = Array.Empty<string>();

    /// <summary>`is:compatible`/`not:compatible`（null = 不限）。</summary>
    public bool? Compatible { get; init; }

    /// <summary>`is:installed`/`not:installed`。</summary>
    public bool? Installed { get; init; }

    /// <summary>`is:cached`/`not:cached`。</summary>
    public bool? Cached { get; init; }

    /// <summary>`is:newly-compatible`/`not:newly-compatible`。</summary>
    public bool? NewlyCompatible { get; init; }

    /// <summary>`is:upgradeable`/`not:upgradeable`。</summary>
    public bool? Upgradeable { get; init; }

    /// <summary>`is:replaceable`/`not:replaceable`。</summary>
    public bool? Replaceable { get; init; }

    /// <summary>用户输入的原文。</summary>
    public string Raw { get; init; } = string.Empty;

    /// <summary>无法解析的查询词（空数组 = 全部解析成功）。</summary>
    public IReadOnlyList<ModSearchUnsupportedTerm> Unsupported { get; init; } =
        Array.Empty<ModSearchUnsupportedTerm>();

    /// <summary>是否没有任何有效条件（等于"全部命中"）。</summary>
    public bool IsEmpty =>
        string.IsNullOrWhiteSpace(Name)
        && string.IsNullOrWhiteSpace(Description)
        && Authors.Count == 0
        && Licenses.Count == 0
        && Localizations.Count == 0
        && DependsOn.Count == 0
        && Recommends.Count == 0
        && Suggests.Count == 0
        && ConflictsWith.Count == 0
        && Supports.Count == 0
        && Tags.Count == 0
        && Labels.Count == 0
        && !Compatible.HasValue
        && !Installed.HasValue
        && !Cached.HasValue
        && !NewlyCompatible.HasValue
        && !Upgradeable.HasValue
        && !Replaceable.HasValue;
}
