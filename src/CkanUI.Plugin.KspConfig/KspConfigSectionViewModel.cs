namespace CkanUI.Plugin.KspConfig;

/// <summary>
/// 一个分类下的一组配置项。列表按分类分节：「全部」页签每个分类一节，单分类页签只有一节。
/// 分组只影响展示，条目对象仍是同一批 <see cref="KspSettingItemViewModel"/> 实例，编辑状态不会丢。
/// </summary>
public sealed class KspConfigSectionViewModel(
    string title,
    string icon,
    int count,
    IReadOnlyList<KspSettingItemViewModel> items)
{
    /// <summary>节标题（与分类页签同名）。</summary>
    public string Title { get; } = title;

    /// <summary>节标题左侧的图标名，走 GlyphCatalog。</summary>
    public string Icon { get; } = icon;

    /// <summary>本节条目数（分节头右侧的计数）。</summary>
    public int Count { get; } = count;

    /// <summary>本节条目，保持 ApplyFilter 的排序（常用优先，再按键名）。</summary>
    public IReadOnlyList<KspSettingItemViewModel> Items { get; } = items;
}
