using CkanUI.Services.Abstractions;
using CkanUI.Services.Abstractions.Dtos;
using log4net;

namespace CkanUI.Services.Plugins;

/// <summary>
/// 启动期插件引导（12 篇 F-17）：建目录 → 扫描（含内置插件激活）→ 恢复持久化激活态。
/// </summary>
/// <remarks>
/// <para>为什么单独一个类而不是塞进 <see cref="PluginService"/>：启动编排属于宿主生命周期，
/// 服务层只管「发现 + 状态」。App 启动时调一次 <see cref="RunAsync"/> 即可，服务本身仍可被单独构造与测试。</para>
/// <para><b>本方法永不抛异常</b>：插件是第三方代码，磁盘也可能只读，启动路径上任何一个失败都不该让主窗口打不开。
/// 每一步各自 try/catch 并记日志，失败的那一步跳过，其余继续。</para>
/// <para>安全口径不变：<see cref="IPluginService.DiscoverAsync(bool, CancellationToken)"/> 传
/// <c>activateBundled: true</c> 只对<b>内置</b>（宿主随包携带的首方）插件生效，表示「宿主为自己带的插件背书」；
/// 用户目录里的插件发现后依旧是 Dormant，只有用户上次显式激活过的才由下面的恢复步骤重新激活。</para>
/// </remarks>
public sealed class PluginBootstrapper
{
    private static readonly ILog Log = LogManager.GetLogger(typeof(PluginBootstrapper));

    private readonly IPluginService _plugins;
    private readonly IPluginActivationStore _activation;

    /// <summary>用插件服务构造；激活状态从默认路径（<c>%LocalAppData%/CKAN/plugin-activation.json</c>）读。</summary>
    /// <param name="plugins">插件服务。</param>
    public PluginBootstrapper(IPluginService plugins)
        : this(plugins, null)
    {
    }

    /// <summary>用插件服务与指定的激活状态存储构造（测试与多实例共存场景用）。</summary>
    /// <param name="plugins">插件服务。</param>
    /// <param name="activation">激活状态存储；<c>null</c> 用默认路径的实现。</param>
    public PluginBootstrapper(IPluginService plugins, IPluginActivationStore? activation)
    {
        _plugins = plugins ?? throw new ArgumentNullException(nameof(plugins));
        _activation = activation ?? new PluginActivationStore();
    }

    /// <summary>
    /// 执行启动期引导：确保插件目录存在、扫描全部插件、恢复用户的持久化激活状态。
    /// </summary>
    /// <param name="ct">取消令牌；调用方取消即放弃剩余步骤（不抛异常）。</param>
    /// <returns>表示整个引导过程的 <see cref="Task"/>；任何失败都以日志形式留痕，不会以异常形式返回。</returns>
    public async Task RunAsync(CancellationToken ct = default)
    {
        EnsureDirectory(SafeBundledDirectory());
        EnsureDirectory(SafePluginsDirectory());

        if (!await TryDiscoverAsync(ct).ConfigureAwait(false))
        {
            return;
        }

        await RestoreActivationAsync(ct).ConfigureAwait(false);
    }

    private string SafeBundledDirectory()
    {
        try
        {
            return _plugins.BundledDirectory;
        }
        catch (Exception ex)
        {
            Log.Warn("读取内置插件目录失败（跳过该目录）。", ex);
            return string.Empty;
        }
    }

    private string SafePluginsDirectory()
    {
        try
        {
            return _plugins.PluginsDirectory;
        }
        catch (Exception ex)
        {
            Log.Warn("读取插件目录失败（跳过该目录）。", ex);
            return string.Empty;
        }
    }

    private async Task<bool> TryDiscoverAsync(CancellationToken ct)
    {
        try
        {
            var result = await _plugins.DiscoverAsync(activateBundled: true, ct).ConfigureAwait(false);
            if (!result.IsSuccess)
            {
                Log.Warn("启动期扫描插件失败：" + result.Message);
                return false;
            }

            Log.Info("启动期扫描插件完成，当前已加载 " + Describe(result.Value) + "。");
            return true;
        }
        catch (Exception ex)
        {
            // 包括 OperationCanceledException：启动路径不因为有插件代码炸了就打不开主窗口。
            Log.Warn("启动期扫描插件抛出异常（已忽略）。", ex);
            return false;
        }
    }

    private async Task RestoreActivationAsync(CancellationToken ct)
    {
        IReadOnlyDictionary<string, PluginActivationEntry> entries;
        try
        {
            entries = _activation.Load();
        }
        catch (Exception ex)
        {
            Log.Warn("读取插件激活状态失败（跳过恢复）。", ex);
            return;
        }

        if (entries.Count == 0)
        {
            return;
        }

        IReadOnlyList<PluginInfo> dormant;
        try
        {
            dormant = _plugins.Dormant;
        }
        catch (Exception ex)
        {
            Log.Warn("读取未激活插件列表失败（跳过激活状态恢复）。", ex);
            return;
        }

        // 只处理「上次被用户显式激活过、这次发现后仍在 Dormant」的插件：
        // 已经 Active 的不能再调一次 Initialize（重复初始化是插件自己的噩梦）；
        // 没被发现的（dll 被删了）也不该被凭空复活。
        foreach (var info in dormant.ToArray())
        {
            if (ct.IsCancellationRequested)
            {
                Log.Info("插件激活状态恢复已取消。");
                return;
            }

            if (!entries.TryGetValue(info.Id, out var entry) || !entry.Activated)
            {
                continue;
            }

            try
            {
                var result = await _plugins.ActivateAsync(info.Id, ct).ConfigureAwait(false);
                if (result.IsSuccess)
                {
                    Log.Info("已恢复插件的持久化激活状态：" + info.Id);
                }
                else
                {
                    Log.Warn("恢复插件「" + info.Id + "」的激活状态失败：" + result.Message);
                }
            }
            catch (Exception ex)
            {
                Log.Warn("恢复插件「" + info.Id + "」的激活状态时抛异常（已忽略，插件留在未激活状态）。", ex);
            }
        }
    }

    private static string Describe(IReadOnlyList<PluginInfo>? plugins) =>
        plugins is null ? "0 个插件" : plugins.Count + " 个插件";

    private static void EnsureDirectory(string directory)
    {
        if (string.IsNullOrWhiteSpace(directory))
        {
            return;
        }

        try
        {
            Directory.CreateDirectory(directory);
        }
        catch (Exception ex)
        {
            // 发布目录只读（Program Files 等）很常见：建不出来就跳过，扫描阶段会各自尝试。
            Log.Warn("创建插件目录失败：" + directory, ex);
        }
    }
}
