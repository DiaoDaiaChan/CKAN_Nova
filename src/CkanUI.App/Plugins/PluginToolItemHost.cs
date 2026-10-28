using CkanUI.Services.Abstractions;
using CkanUI.ViewModels.Navigation;
using log4net;

namespace CkanUI.App.Plugins;

/// <summary>
/// 把插件声明的工具项推进宿主页面的工具条（12 篇 F-17「往现有工具条插按钮」）。
/// </summary>
/// <remarks>
/// <para>为什么在 App 层：只有这一层同时看得见插件服务（已激活插件自述的工具项）与页面实例
/// （<see cref="IPluginToolHost"/> 由 DI 单例提供，外壳拿到的就是同一批实例），
/// 视图模型层不必引用插件解析细节，页面也不必知道插件从哪来。</para>
/// <para>刷新时机：安装时算一次（发现/激活可能发生在安装之前），之后只跟随
/// <see cref="IPluginService.Changed"/>；一律经 <see cref="IDispatcher"/> 封送回 UI 线程，
/// 因为插件按钮是 <c>ObservableCollection</c> 的内容。</para>
/// <para>容错：单个页面组装失败只记日志，不影响其它页面；插件服务取列表本身是快照，读不到就是空。</para>
/// </remarks>
public sealed class PluginToolItemHost : IDisposable
{
    private static readonly ILog Log = LogManager.GetLogger(typeof(PluginToolItemHost));

    private readonly IPluginService _plugins;
    private readonly IReadOnlyList<IPluginToolHost> _hosts;
    private readonly IDispatcher _dispatcher;
    private bool _installed;

    /// <summary>构造函数。</summary>
    /// <param name="plugins">插件服务。</param>
    /// <param name="hosts">愿意接收插件按钮的页面（DI 里的全部 <see cref="IPluginToolHost"/>）。</param>
    /// <param name="dispatcher">UI 线程调度器。</param>
    public PluginToolItemHost(
        IPluginService plugins,
        IEnumerable<IPluginToolHost> hosts,
        IDispatcher dispatcher)
    {
        _plugins = plugins ?? throw new ArgumentNullException(nameof(plugins));
        ArgumentNullException.ThrowIfNull(hosts);
        _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
        _hosts = hosts.ToArray();
    }

    /// <summary>安装：先算一次，再订阅插件状态变化。可重复调用。</summary>
    public void Install()
    {
        if (_installed)
        {
            return;
        }

        _installed = true;
        Apply();
        _plugins.Changed += OnPluginsChanged;
    }

    /// <summary>
    /// 重新计算并推送到各页面（安装时、插件状态变化时、以及测试直接调用）。
    /// </summary>
    public void Apply()
    {
        IReadOnlyList<PluginToolItemEntry> entries;
        try
        {
            entries = PluginToolCatalog.Collect(_plugins.Active);
        }
        catch (Exception ex)
        {
            Log.Warn("读取插件工具项失败，本次不刷新工具条。", ex);
            return;
        }

        foreach (var host in _hosts)
        {
            try
            {
                var tools = PluginToolCatalog.ForPage(entries, host.ToolHostKey)
                    .Select(entry => new PluginToolButtonViewModel(
                        entry,
                        ct => _plugins.InvokeToolItemAsync(entry.PluginId, entry.Key, ct)))
                    .ToArray();
                host.SetPluginTools(tools);
            }
            catch (Exception ex)
            {
                Log.Warn("刷新页面「" + host.ToolHostKey + "」的插件工具项失败。", ex);
            }
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (!_installed)
        {
            return;
        }

        _installed = false;
        _plugins.Changed -= OnPluginsChanged;
    }

    private void OnPluginsChanged(object? sender, EventArgs e) => _dispatcher.Post(Apply);
}
