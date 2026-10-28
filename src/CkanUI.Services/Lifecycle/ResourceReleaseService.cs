using System.Diagnostics;
using CkanUI.Services.Abstractions;
using CkanUI.Services.Abstractions.Dtos;
using log4net;

namespace CkanUI.Services.Lifecycle;

/// <summary>
/// 分层资源释放的编排（18 篇 §3/§4/§7）。
/// </summary>
/// <remarks>
/// <para>档位决定「跑到第几层」：保守 = L0+L1，均衡 = L0–L2，激进 = L0–L4，关闭 = 一列不跑。
/// 时机不在这里：调用方按 18 篇 §4 的 T3/T4/T5 多次调用 <see cref="ReleaseAsync"/>，本服务用
/// 「已成功的层集合」做幂等，所以 <c>T3→Conservative、T4→Balanced、T5→Aggressive</c> 的推进
/// 实际效果就是每层只跑一次。</para>
/// <para>L2 是唯一「不可忽略」的层：Core 没有清缓存 API，实际动作是关掉注册表
/// （<see cref="IRegistryService.CloseAsync"/> → <c>RegistryManager.DisposeAll()</c>）并丢掉仓库快照。
/// 因此只要 L2 成功，<see cref="RestoreAsync"/> 就必须能把这两样重新打开。</para>
/// <para>每层独立成败：任何一层抛异常只记 Warn 并继续下一层；失败的层出现在报告的
/// <see cref="ResourceReleaseReport.FailedLayers"/> 里，本服务从不把这些异常扔给调用方。</para>
/// </remarks>
public sealed class ResourceReleaseService : IResourceReleaseService
{
    private static readonly ILog Log = LogManager.GetLogger(typeof(ResourceReleaseService));

    private static readonly ResourceReleaseLayer[] AllLayers =
    [
        ResourceReleaseLayer.View,
        ResourceReleaseLayer.Caches,
        ResourceReleaseLayer.CoreData,
        ResourceReleaseLayer.ManagedHeap,
        ResourceReleaseLayer.OsMemory,
    ];

    private readonly IRegistryService _registry;
    private readonly IRepositoryService _repositories;
    private readonly IInstanceService _instances;
    private readonly IPlatformMemory _memory;
    private readonly IReadOnlyList<IViewResourceParticipant> _viewParticipants;
    private readonly IReadOnlyList<IAppCacheParticipant> _cacheParticipants;

    private readonly object _gate = new();
    private readonly HashSet<ResourceReleaseLayer> _completed = [];
    private bool _dataReleased;
    private InstanceInfo? _releasedInstance;

    public ResourceReleaseService(
        IRegistryService registry,
        IRepositoryService repositories,
        IInstanceService instances,
        IPlatformMemory memory,
        IEnumerable<IViewResourceParticipant>? viewParticipants = null,
        IEnumerable<IAppCacheParticipant>? cacheParticipants = null)
    {
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        _repositories = repositories ?? throw new ArgumentNullException(nameof(repositories));
        _instances = instances ?? throw new ArgumentNullException(nameof(instances));
        _memory = memory ?? throw new ArgumentNullException(nameof(memory));

        // 参与方可选：L0（界面字形与背景位图）由 App 层登记，L1（页面行投影、本地化诊断表）由
        // App 与 Services 分别登记；单元测试或 headless 场景下 DI 里可能一个都没有，
        // 此时两层照样「执行成功、释放 0 条」，调用方不需要分辨「没得清」和「清过了」。
        _viewParticipants = (viewParticipants ?? []).Where(p => p is not null).ToArray();
        _cacheParticipants = (cacheParticipants ?? []).Where(p => p is not null).ToArray();
    }

    public bool IsReleased
    {
        get
        {
            lock (_gate)
            {
                return _dataReleased;
            }
        }
    }

    public event Action<ResourceReleaseReport>? Released;

    public async Task<OperationResult<ResourceReleaseReport>> ReleaseAsync(
        ResourceReleaseTier tier,
        IProgress<OperationProgress>? progress = null,
        CancellationToken ct = default)
    {
        var takenAt = DateTimeOffset.UtcNow;
        var total = Stopwatch.StartNew();
        var layers = new List<ResourceReleaseLayerReport>(AllLayers.Length);
        var cancelled = false;

        foreach (var layer in AllLayers)
        {
            if (!IsIncluded(tier, layer))
            {
                layers.Add(ResourceReleaseLayerReport.Skipped(layer, $"档位「{TierName(tier)}」不包含该层。"));
                continue;
            }

            if (AlreadyCompleted(layer))
            {
                layers.Add(ResourceReleaseLayerReport.Skipped(layer, "本次启动已执行过该层。"));
                continue;
            }

            if (ct.IsCancellationRequested)
            {
                // 取消只是「不再往下做」，已执行的层不回滚——回滚统一走 RestoreAsync。
                cancelled = true;
                layers.Add(ResourceReleaseLayerReport.Skipped(layer, "本次释放已取消。"));
                continue;
            }

            Report(progress, layer, OperationState.Running, $"{LayerName(layer)}：执行中…", PercentOf(layer));
            var report = await ExecuteLayerAsync(layer, ct).ConfigureAwait(false);
            layers.Add(report);
            Report(progress, layer,
                report.Attempted && report.Succeeded ? OperationState.Succeeded : OperationState.Failed,
                report.Attempted && report.Succeeded
                    ? $"{LayerName(layer)}：释放 {report.ReleasedItemCount} 项"
                    : $"{LayerName(layer)}：{report.Detail ?? "未执行"}",
                PercentOf(layer));

            if (report.Attempted && report.Succeeded)
            {
                lock (_gate)
                {
                    _completed.Add(layer);
                }
            }
            else if (report.Attempted)
            {
                Log.Warn($"资源释放 L{(int)layer}（{LayerName(layer)}）失败，继续后续层：{report.Detail}");
            }
        }

        total.Stop();
        var result = new ResourceReleaseReport(tier, takenAt, total.Elapsed, layers) { Cancelled = cancelled };

        Report(progress, null, OperationState.Succeeded,
            result.Cancelled
                ? $"资源释放已取消：{result.Layers.Count(l => l.Attempted)} 层已执行。"
                : $"资源释放完成：{result.ReleasedItemCount} 项，耗时 {result.Elapsed.TotalSeconds:0.0}s。",
            100);

        RaiseReleased(result);
        return OperationResult<ResourceReleaseReport>.Ok(result);
    }

    public async Task<OperationResult> RestoreAsync(CancellationToken ct = default)
    {
        InstanceInfo? instance;
        lock (_gate)
        {
            if (!_dataReleased || _releasedInstance is null)
            {
                return OperationResult.Ok();
            }

            instance = _releasedInstance;
        }

        try
        {
            // 顺序与 18 篇 §6 一致：先拿回注册表锁，再按本地缓存重建仓库快照。
            // 这里刻意不联网：恢复必须能在离线时完成，联网刷新交给模组页自己的刷新入口。
            var open = await _registry.OpenAsync(instance, ct).ConfigureAwait(false);
            if (!open.IsSuccess)
            {
                Log.Warn($"恢复注册表失败：{open.Message}");
                return OperationResult.Fail(
                    "恢复注册表失败，可以重试。",
                    $"{open.Message}{Environment.NewLine}{open.Detail}".TrimEnd(),
                    retryable: true);
            }

            var load = await _repositories.LoadCachedAsync(ct).ConfigureAwait(false);
            if (!load.IsSuccess)
            {
                Log.Warn($"重建仓库快照失败：{load.Message}");
                return OperationResult.Fail(
                    "重建仓库列表失败，可以重试。",
                    $"{load.Message}{Environment.NewLine}{load.Detail}".TrimEnd(),
                    retryable: true);
            }

            lock (_gate)
            {
                _dataReleased = false;
                _releasedInstance = null;
                _completed.Clear();
            }

            return OperationResult.Ok();
        }
        catch (OperationCanceledException)
        {
            return OperationResult.Fail("恢复被打断，可以重试。", "操作已取消。", retryable: true);
        }
        catch (Exception ex)
        {
            Log.Warn("恢复资源时抛出异常。", ex);
            return OperationResult.Fail("恢复资源失败，可以重试。", ex.Message, retryable: true);
        }
    }

    private async Task<ResourceReleaseLayerReport> ExecuteLayerAsync(ResourceReleaseLayer layer, CancellationToken ct)
    {
        var watch = Stopwatch.StartNew();
        try
        {
            var report = layer switch
            {
                ResourceReleaseLayer.View => TrimViewResources(watch),
                ResourceReleaseLayer.Caches => TrimAppCaches(watch),
                ResourceReleaseLayer.CoreData => await ReleaseCoreDataAsync(watch, ct).ConfigureAwait(false),
                ResourceReleaseLayer.ManagedHeap => CollectManagedHeap(watch),
                ResourceReleaseLayer.OsMemory => TrimWorkingSet(watch),
                _ => ResourceReleaseLayerReport.Skipped(layer, "未知层。"),
            };

            return report;
        }
        catch (OperationCanceledException)
        {
            return ResourceReleaseLayerReport.Skipped(layer, "本次释放已取消。");
        }
        catch (Exception ex)
        {
            Log.Warn($"资源释放 L{(int)layer}（{LayerName(layer)}）抛出异常。", ex);
            watch.Stop();
            return new ResourceReleaseLayerReport(layer, Attempted: true, Succeeded: false, 0, watch.Elapsed, ex.Message);
        }
    }

    private ResourceReleaseLayerReport TrimViewResources(Stopwatch watch)
    {
        var items = 0;
        string? failure = null;
        foreach (var participant in _viewParticipants)
        {
            try
            {
                items += participant.TrimViewResources();
            }
            catch (Exception ex)
            {
                // 一个参与方失败不影响其余参与方：这一层继续清，最后按「有没有失败」判成败。
                Log.Warn($"L0 视图参与方「{participant.Name}」清理失败。", ex);
                failure ??= $"{participant.Name}：{ex.Message}";
            }
        }

        watch.Stop();
        return new ResourceReleaseLayerReport(
            ResourceReleaseLayer.View, Attempted: true, Succeeded: failure is null, items, watch.Elapsed, failure);
    }

    private ResourceReleaseLayerReport TrimAppCaches(Stopwatch watch)
    {
        var items = 0;
        string? failure = null;
        foreach (var participant in _cacheParticipants)
        {
            try
            {
                items += participant.TrimAppCache();
            }
            catch (Exception ex)
            {
                Log.Warn($"L1 缓存参与方「{participant.Name}」清理失败。", ex);
                failure ??= $"{participant.Name}：{ex.Message}";
            }
        }

        watch.Stop();
        return new ResourceReleaseLayerReport(
            ResourceReleaseLayer.Caches, Attempted: true, Succeeded: failure is null, items, watch.Elapsed, failure);
    }

    private async Task<ResourceReleaseLayerReport> ReleaseCoreDataAsync(Stopwatch watch, CancellationToken ct)
    {
        var instance = _instances.Current;
        if (instance is null)
        {
            watch.Stop();
            return new ResourceReleaseLayerReport(
                ResourceReleaseLayer.CoreData, Attempted: true, Succeeded: false, 0, watch.Elapsed,
                "当前没有活动实例，无法卸载注册表。");
        }

        // 计数在关闭之前取：关闭之后两个快照都变成 Empty，就数不出来了。
        var items = _registry.Current.Installed.Count + _repositories.Current.Available.Count;

        var close = await _registry.CloseAsync(ct).ConfigureAwait(false);
        if (!close.IsSuccess)
        {
            watch.Stop();
            return new ResourceReleaseLayerReport(
                ResourceReleaseLayer.CoreData, Attempted: true, Succeeded: false, 0, watch.Elapsed,
                close.Message ?? "关闭注册表失败。");
        }

        _repositories.DropCachedSnapshot();

        lock (_gate)
        {
            _dataReleased = true;
            _releasedInstance = instance;
        }

        watch.Stop();
        Log.Info($"L2 已卸载 Core 数据：{items} 项（实例「{instance.Name}」）。");
        return new ResourceReleaseLayerReport(
            ResourceReleaseLayer.CoreData, Attempted: true, Succeeded: true, items, watch.Elapsed, null);
    }

    private ResourceReleaseLayerReport CollectManagedHeap(Stopwatch watch)
    {
        var result = _memory.CollectManagedHeap();
        watch.Stop();
        return result.Supported
            ? new ResourceReleaseLayerReport(
                ResourceReleaseLayer.ManagedHeap, Attempted: true, Succeeded: true, result.ReleasedItemCount,
                watch.Elapsed, result.Detail)
            : ResourceReleaseLayerReport.Skipped(ResourceReleaseLayer.ManagedHeap, result.Detail ?? "平台未接入托管堆回收。");
    }

    private ResourceReleaseLayerReport TrimWorkingSet(Stopwatch watch)
    {
        var result = _memory.TrimWorkingSet();
        watch.Stop();
        return result.Supported
            ? new ResourceReleaseLayerReport(
                ResourceReleaseLayer.OsMemory, Attempted: true, Succeeded: true, result.ReleasedItemCount,
                watch.Elapsed, result.Detail)
            : ResourceReleaseLayerReport.Skipped(ResourceReleaseLayer.OsMemory, result.Detail ?? "平台未接入工作集回收。");
    }

    private bool AlreadyCompleted(ResourceReleaseLayer layer)
    {
        lock (_gate)
        {
            return _completed.Contains(layer);
        }
    }

    private void RaiseReleased(ResourceReleaseReport report)
    {
        var handlers = Released;
        if (handlers is null)
        {
            return;
        }

        try
        {
            handlers(report);
        }
        catch (Exception ex)
        {
            Log.Warn("资源释放报告的订阅方抛出异常。", ex);
        }
    }

    private static bool IsIncluded(ResourceReleaseTier tier, ResourceReleaseLayer layer) => tier switch
    {
        ResourceReleaseTier.Conservative => layer is ResourceReleaseLayer.View or ResourceReleaseLayer.Caches,
        ResourceReleaseTier.Balanced => layer is ResourceReleaseLayer.View
            or ResourceReleaseLayer.Caches
            or ResourceReleaseLayer.CoreData,
        ResourceReleaseTier.Aggressive => true,
        _ => false,
    };

    private static string TierName(ResourceReleaseTier tier) => tier switch
    {
        ResourceReleaseTier.Off => "关闭",
        ResourceReleaseTier.Conservative => "保守",
        ResourceReleaseTier.Balanced => "均衡",
        ResourceReleaseTier.Aggressive => "激进",
        _ => tier.ToString(),
    };

    private static string LayerName(ResourceReleaseLayer layer) => layer switch
    {
        ResourceReleaseLayer.View => "视图资源",
        ResourceReleaseLayer.Caches => "应用缓存",
        ResourceReleaseLayer.CoreData => "Core 数据",
        ResourceReleaseLayer.ManagedHeap => "托管堆",
        ResourceReleaseLayer.OsMemory => "工作集",
        _ => layer.ToString(),
    };

    private static double PercentOf(ResourceReleaseLayer layer) => (int)layer * 20;

    private void Report(
        IProgress<OperationProgress>? progress,
        ResourceReleaseLayer? layer,
        OperationState state,
        string message,
        double percent)
    {
        if (progress is null)
        {
            return;
        }

        var title = layer is null ? "释放启动后的占用" : $"释放启动后的占用（{LayerName(layer.Value)}）";
        var update = new OperationProgress(
            IResourceReleaseService.ProgressTaskId, "resource-release", title, message, percent, false, state);
        try
        {
            progress.Report(update);
        }
        catch (Exception ex)
        {
            Log.Warn("资源释放进度订阅方抛出异常。", ex);
        }
    }
}
