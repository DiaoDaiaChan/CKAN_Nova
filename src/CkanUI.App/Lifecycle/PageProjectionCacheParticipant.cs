using CkanUI.App.Plugins;
using CkanUI.Services.Abstractions;
using CkanUI.ViewModels.Pages;

using log4net;

namespace CkanUI.App.Lifecycle;

/// <summary>
/// L1 缓存释放：把各页面的行投影丢掉（18 篇 §3）。
/// </summary>
/// <remarks>
/// 为什么由 App 层来编排：参与方集合来自 DI（<see cref="IPageProjectionTrim"/> 由容器补齐），
/// 而「页面视图模型是单例」这件事是 App 层注册时决定的，服务层不该知道有哪些页面。
/// 逐页 try/catch：某一页释放失败（比如正被枚举）不能让其它页也放不掉。
/// </remarks>
public sealed class PageProjectionCacheParticipant : IAppCacheParticipant
{
    private static readonly ILog Log = LogManager.GetLogger(typeof(PageProjectionCacheParticipant));

    private readonly IEnumerable<IPageProjectionTrim> _pages;

    private readonly PluginPageCache? _pluginPages;

    /// <summary>构造参与者。</summary>
    /// <param name="pages">所有支持裁剪的页面；容器里没有注册时为空集合。</param>
    /// <param name="pluginPages">插件页实例缓存；插件页不在 DI 里，只登记了 <see cref="IPageProjectionTrim"/> 是接不住它们的。</param>
    public PageProjectionCacheParticipant(IEnumerable<IPageProjectionTrim> pages, PluginPageCache? pluginPages = null)
    {
        _pages = pages ?? throw new ArgumentNullException(nameof(pages));
        _pluginPages = pluginPages;
    }

    /// <inheritdoc />
    public string Name => "页面行投影";

    /// <inheritdoc />
    public int TrimAppCache()
    {
        var released = 0;
        foreach (var page in _pages)
        {
            try
            {
                var count = page.TrimProjection();
                if (count > 0)
                {
                    Log.Info($"已释放页面投影：{page.PageName} 共 {count} 项。");
                }

                released += count;
            }
            catch (Exception ex)
            {
                Log.Warn($"释放页面投影失败：{page.PageName}。", ex);
            }
        }

        released += TrimPluginPages();
        return released;
    }

    /// <summary>
    /// 插件页的同一档释放：插件页实例不经过 DI（按导航键现造、由缓存持有），
    /// 所以 <see cref="IPageProjectionTrim"/> 的容器登记看不到它们。
    /// </summary>
    private int TrimPluginPages()
    {
        if (_pluginPages is null)
        {
            return 0;
        }

        var released = 0;
        foreach (var page in _pluginPages.Instances)
        {
            if (page is not IPageProjectionTrim trimmable)
            {
                continue;
            }

            try
            {
                released += trimmable.TrimProjection();
            }
            catch (Exception ex)
            {
                Log.Warn($"释放插件页投影失败：{trimmable.PageName}。", ex);
            }
        }

        return released;
    }
}
