using log4net;

namespace CkanUI.App.Plugins;

/// <summary>
/// 插件页实例缓存：一个导航键一个视图模型实例，插件离开激活表时连同实例一起释放。
/// </summary>
/// <remarks>
/// <para>为什么要缓存：内置页的工厂是 <c>services.GetService(type)</c>（单例），
/// 插件页却是每次导航都 <c>ActivatorUtilities.CreateInstance</c> 出一个新实例。外壳把页面视图
/// 常驻在舞台上（换页只切可见性），于是每进一次插件页就多一个视图、多一个轮询定时器，
/// 而旧的那些既不会离开可视树、也没人调用它们的 <see cref="IDisposable.Dispose"/>。</para>
/// <para>为什么不缓存 <see cref="Type"/>：缓存类型会钉住整个可卸载 <c>AssemblyLoadContext</c>，
/// 插件卸载后程序集再也回收不了（这是 <see cref="PluginPageFactory"/> 里写明的口径）。
/// 缓存的是实例，插件停用/卸载时由 <see cref="RetainOnly"/> 释放实例并清空引用。</para>
/// </remarks>
public sealed class PluginPageCache
{
    private static readonly ILog Log = LogManager.GetLogger(typeof(PluginPageCache));

    private readonly Dictionary<string, object> _pages = new(StringComparer.Ordinal);

    /// <summary>当前缓存的页面实例数（诊断与测试用）。</summary>
    public int Count => _pages.Count;

    /// <summary>按导航键取页面实例，没有就调一次工厂并记住结果（工厂返回 <c>null</c> 不缓存）。</summary>
    public object? GetOrCreate(string key, Func<object?> factory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentNullException.ThrowIfNull(factory);

        if (_pages.TryGetValue(key, out var cached))
        {
            return cached;
        }

        var created = factory();
        if (created is not null)
        {
            _pages[key] = created;
        }

        return created;
    }

    /// <summary>
    /// 只保留 <paramref name="liveKeys"/> 里的页面实例，其余先移除再释放。
    /// </summary>
    /// <returns>被移除的导航键（按发现顺序）。</returns>
    public IReadOnlyList<string> RetainOnly(IEnumerable<string> liveKeys)
    {
        ArgumentNullException.ThrowIfNull(liveKeys);

        var live = new HashSet<string>(liveKeys, StringComparer.OrdinalIgnoreCase);
        List<string>? removed = null;
        foreach (var key in _pages.Keys.ToList())
        {
            if (live.Contains(key))
            {
                continue;
            }

            var page = _pages[key];
            // 先摘引用再释放：Dispose 里若回调宿主（例如发事件）不会看到半释放的缓存。
            _pages.Remove(key);
            (removed ??= []).Add(key);

            if (page is not IDisposable disposable)
            {
                continue;
            }

            try
            {
                disposable.Dispose();
            }
            catch (Exception ex)
            {
                // 插件写坏了 Dispose 也不能把宿主带崩：引用已经摘掉，收集仍会继续。
                Log.Warn("释放已下线的插件页失败：" + key, ex);
            }
        }

        return removed ?? (IReadOnlyList<string>)Array.Empty<string>();
    }
}
