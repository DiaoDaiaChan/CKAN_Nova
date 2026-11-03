using CkanUI.Services.Abstractions.Dtos;

namespace CkanUI.ViewModels.Navigation;

/// <summary>
/// 一个可用的插件页签（12 篇 F-17「往 UI 里加页签」）。
/// </summary>
/// <param name="Key">导航键（已去空白）。</param>
/// <param name="Title">页签标题（插件自述，宿主不本地化）。</param>
/// <param name="ViewModelTypeName">视图模型的程序集限定名，由 App 层反射解析。</param>
/// <param name="PluginName">来源插件显示名，用于排障与重名时点名。</param>
/// <param name="TitleKey">标题的本地化键；为空表示直接用 <paramref name="Title"/>（插件自述）。</param>
/// <param name="Glyph">导航栏图标字形名；为空时外壳用通用插件图标兜底。</param>
/// <param name="Section">宿主分节根键（如 <c>instances</c>）；为空表示独立侧栏入口。</param>
/// <param name="Order">同一分节内的排序权重（没有落到固定槽位时才用得到）。</param>
public sealed record PluginPageEntry(
    string Key,
    string Title,
    string ViewModelTypeName,
    string PluginName,
    string? TitleKey = null,
    string? Glyph = null,
    string? Section = null,
    int Order = 0);

/// <summary>
/// 把插件自述的页签整理成导航栏可用的一组页签。
/// </summary>
/// <remarks>
/// <para>放在视图模型层而不是 App 层：宿主外壳（<c>ShellViewModel</c>）与页面工厂
/// （<c>ServiceRegistration.CreatePageFactory</c>）都要用同一套过滤规则，规则写两份必然漂移。</para>
/// <para>过滤口径（缺一条就会出现「点了没反应」或「页签静默消失」）：
/// 只收已激活插件的页签（默认不启用是插件安全模型的前提，见 docs/design/plugins.md）；
/// 键、标题、视图模型类型名任一为空的页签丢弃（插件自述可能是坏值）；
/// 与宿主既有页签重名、或两个插件之间重名的页签只保留第一个（按插件显示名排序，结果稳定）；
/// 键比较一律忽略大小写，与导航服务按 key 查找的口径一致。</para>
/// </remarks>
public static class PluginPageCatalog
{
    /// <summary>
    /// 收集当前可用的插件页签。
    /// </summary>
    /// <param name="activePlugins">已激活插件（<see cref="IPluginService.Active"/>）。</param>
    /// <param name="reservedKeys">宿主已占用的导航键（内置页签）。</param>
    /// <returns>按插件显示名、插件内声明顺序排列的页签列表；没有可用的返回空列表。</returns>
    public static IReadOnlyList<PluginPageEntry> Collect(
        IEnumerable<PluginInfo>? activePlugins,
        IEnumerable<string>? reservedKeys)
    {
        if (activePlugins is null)
        {
            return Array.Empty<PluginPageEntry>();
        }

        var taken = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (reservedKeys is not null)
        {
            foreach (var key in reservedKeys)
            {
                if (!string.IsNullOrWhiteSpace(key))
                {
                    taken.Add(key.Trim());
                }
            }
        }

        var entries = new List<PluginPageEntry>();
        var ordered = activePlugins
            .Where(plugin => plugin is not null && plugin.State == PluginState.Active)
            .OrderBy(plugin => plugin.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(plugin => plugin.Id, StringComparer.Ordinal);

        foreach (var plugin in ordered)
        {
            if (plugin.Pages is null)
            {
                continue;
            }

            foreach (var page in plugin.Pages)
            {
                var key = page.Key?.Trim();
                var title = page.Title?.Trim();
                var typeName = page.ViewModelTypeName?.Trim();
                if (string.IsNullOrEmpty(key) || string.IsNullOrEmpty(title) || string.IsNullOrEmpty(typeName))
                {
                    continue;
                }

                if (!taken.Add(key!))
                {
                    continue;
                }

                var titleKey = page.TitleKey?.Trim();
                var glyph = page.Glyph?.Trim();
                // 分节是宿主的词汇表：插件只负责填名字，认不认由 Shell 决定（认不出退回侧栏入口）。
                var section = page.Section?.Trim();
                entries.Add(new PluginPageEntry(
                    key!,
                    title!,
                    typeName!,
                    plugin.DisplayName,
                    string.IsNullOrEmpty(titleKey) ? null : titleKey,
                    string.IsNullOrEmpty(glyph) ? null : glyph,
                    string.IsNullOrEmpty(section) ? null : section,
                    page.Order));
            }
        }

        return entries;
    }
}
