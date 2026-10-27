namespace CkanUI.Services.Abstractions.Dtos;

/// <summary>
/// 一行模组的高级搜索事实（12 篇 F-20/F-21）：由服务层从 CKAN 元数据投影，视图模型据此实现
/// `desc:`/`lic:`/`lang:`/`dep:`/`rec:`/`sug:`/`conf:`/`sup:`/`label:` 以及
/// `is:cached`/`is:newly-compatible`/`is:replaceable`。
/// </summary>
/// <remarks>
/// 只承载"视图模型判定不了、但服务层拿得到"的字段；名称、作者、标签、安装/兼容状态仍在
/// <see cref="ModRowData"/> 上，避免同一事实存两份。
/// 所有成员都有默认值：<c>default(ModSearchFacts)</c> 表示"没有额外搜索事实"，
/// 依赖这些字段的条件一律不命中（语义与上游 <c>ModSearch.Matches</c> 的 null 分支一致，
/// 不会静默地"永远命中"）。
/// </remarks>
public readonly record struct ModSearchFacts
{
    /// <summary>模组简介（上游 <c>CkanModule.@abstract</c>，未做去标点规范化）。</summary>
    public string? Abstract { get; init; }

    /// <summary>模组详细描述（上游 <c>CkanModule.description</c>）。</summary>
    public string? Description { get; init; }

    /// <summary>许可证列表（上游 <c>CkanModule.license</c> 的字符串形式，`lic:` 按前缀匹配）。</summary>
    public IReadOnlyList<string>? Licenses { get; init; }

    /// <summary>本地化语言列表（上游 <c>CkanModule.localizations</c>，`lang:` 按前缀匹配）。</summary>
    public IReadOnlyList<string>? Localizations { get; init; }

    /// <summary>`dep:` 依赖的标识符集合（已展开 any_of 关系）。</summary>
    public IReadOnlyList<string>? DependsOn { get; init; }

    /// <summary>`rec:` 推荐关系的标识符集合。</summary>
    public IReadOnlyList<string>? Recommends { get; init; }

    /// <summary>`sug:` 建议关系的标识符集合。</summary>
    public IReadOnlyList<string>? Suggests { get; init; }

    /// <summary>`conf:` 冲突关系的标识符集合。</summary>
    public IReadOnlyList<string>? ConflictsWith { get; init; }

    /// <summary>`sup:` 支持关系的标识符集合。</summary>
    public IReadOnlyList<string>? Supports { get; init; }

    /// <summary>所属标签组名称（上游 <c>ModuleLabelList</c> 的 Favourites/Hidden/Held 等）。</summary>
    public IReadOnlyList<string>? Labels { get; init; }

    /// <summary>安装包是否已在下载缓存里（`is:cached`）。</summary>
    public bool IsCached { get; init; }

    /// <summary>是否"新近变为兼容/新近入库"（`is:newly-compatible`，对应上游 <c>GUIMod.IsNew</c>）。</summary>
    public bool IsNew { get; init; }

    /// <summary>已安装且存在替代模组（`is:replaceable`，对应上游 <c>isInstalled &amp;&amp; hasReplacement</c>）。</summary>
    public bool IsReplaceable { get; init; }
}
