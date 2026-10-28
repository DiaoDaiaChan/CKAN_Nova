using CkanUI.Services.Abstractions.Dtos;

namespace CkanUI.ViewModels.Navigation;

/// <summary>
/// 一个可用的插件工具项（12 篇 F-17「往现有工具条插按钮」）。
/// </summary>
/// <param name="PluginId">声明它的插件标识（回调时用它找插件实例）。</param>
/// <param name="PluginName">插件显示名（按钮提示里点名用）。</param>
/// <param name="TargetPageKey">目标页键（已去空白，忽略大小写）。</param>
/// <param name="Key">工具项键（插件内唯一，忽略大小写）。</param>
/// <param name="Title">按钮文字（插件自述，宿主不本地化）。</param>
/// <param name="ToolTip">悬停提示；插件没给时由界面拼「插件名 · 键」。</param>
public sealed record PluginToolItemEntry(
    string PluginId,
    string PluginName,
    string TargetPageKey,
    string Key,
    string Title,
    string? ToolTip);

/// <summary>
/// 把插件自述的工具项整理成「某页工具条上的一组按钮」。
/// </summary>
/// <remarks>
/// <para>与 <see cref="PluginPageCatalog"/> 同一套过滤口径，理由相同：规则写两份必然漂移。
/// 只收已激活插件（默认不启用是插件安全模型的前提）；目标页键、工具项键、标题任一为空即丢弃；
/// 同一页上键重复只保留第一个（按插件显示名排序，结果稳定）。</para>
/// <para>键不认识不算错误：宿主不认识的目标页就是「装错了页面」，插件本身照常工作。</para>
/// </remarks>
public static class PluginToolCatalog
{
    /// <summary>目前支持工具项的宿主页键（模组管理页）。</summary>
    public const string ModsPageKey = "mods";

    /// <summary>
    /// 收集当前可用的工具项。
    /// </summary>
    /// <param name="activePlugins">已激活插件（<see cref="IPluginService.Active"/>）。</param>
    /// <returns>按插件显示名、插件内声明顺序排列；没有可用的返回空列表。</returns>
    public static IReadOnlyList<PluginToolItemEntry> Collect(IEnumerable<PluginInfo>? activePlugins)
    {
        if (activePlugins is null)
        {
            return Array.Empty<PluginToolItemEntry>();
        }

        var taken = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var entries = new List<PluginToolItemEntry>();

        var ordered = activePlugins
            .Where(plugin => plugin is not null && plugin.State == PluginState.Active)
            .OrderBy(plugin => plugin.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(plugin => plugin.Id, StringComparer.Ordinal);

        foreach (var plugin in ordered)
        {
            if (plugin.ToolItems is null)
            {
                continue;
            }

            foreach (var item in plugin.ToolItems)
            {
                if (item is null)
                {
                    continue;
                }

                var pageKey = item.TargetPageKey?.Trim();
                var itemKey = item.Key?.Trim();
                var title = item.Title?.Trim();
                if (string.IsNullOrEmpty(pageKey) || string.IsNullOrEmpty(itemKey) || string.IsNullOrEmpty(title))
                {
                    continue;
                }

                if (!taken.Add(pageKey + "/" + itemKey))
                {
                    continue;
                }

                entries.Add(new PluginToolItemEntry(
                    plugin.Id,
                    plugin.DisplayName,
                    pageKey!,
                    itemKey!,
                    title!,
                    string.IsNullOrWhiteSpace(item.ToolTip) ? null : item.ToolTip!.Trim()));
            }
        }

        return entries;
    }

    /// <summary>
    /// 取出挂在某一页上的工具项（保持 <see cref="Collect"/> 的顺序）。
    /// </summary>
    /// <param name="entries">已收集的工具项。</param>
    /// <param name="pageKey">宿主页键。</param>
    public static IReadOnlyList<PluginToolItemEntry> ForPage(
        IEnumerable<PluginToolItemEntry>? entries,
        string? pageKey)
    {
        if (entries is null || string.IsNullOrWhiteSpace(pageKey))
        {
            return Array.Empty<PluginToolItemEntry>();
        }

        var key = pageKey.Trim();
        return entries
            .Where(entry => string.Equals(entry.TargetPageKey, key, StringComparison.OrdinalIgnoreCase))
            .ToArray();
    }
}
