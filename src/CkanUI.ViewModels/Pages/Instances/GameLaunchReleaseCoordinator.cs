using CkanUI.Services.Abstractions;
using CkanUI.Services.Abstractions.Dtos;

namespace CkanUI.ViewModels.Pages.Instances;

/// <summary>
/// 一次成功启动后的分阶段资源释放（18 篇 §4 的 T3/T4/T5）。
/// </summary>
/// <remarks>
/// <para>时间轴（相对 T2「游戏主窗口出现」）：T3=0s 跑 L0+L1（保守档），T4=+10s 补 L2（均衡档），
/// T5=+30s 补 L3/L4（激进档）。三段跑的是同一个服务，服务本身按「已成功的层」幂等，
/// 所以每一层只执行一次，档位越高只是把后面几段也排进时间表。</para>
/// <para>游戏提前退出时 <see cref="EndAsync"/> 先取消计时再 <c>RestoreAsync</c>：取消保证不再往下跑
/// （尤其不跑 L3 的阻塞 GC），Restore 把 L2 丢掉的注册表与仓库快照重新打开。</para>
/// <para>本类不碰 <c>Process</c>、不碰 UI：既可以由实例页在 UI 线程驱动，也可以在测试里用假
/// 调度器同步推进。</para>
/// </remarks>
public sealed class GameLaunchReleaseCoordinator
{
    private readonly IResourceReleaseService _release;
    private readonly IInstancesDelayScheduler _scheduler;
    private readonly object _gate = new();

    private CancellationTokenSource? _cancellation;
    private Task? _pending;

    public GameLaunchReleaseCoordinator(IResourceReleaseService release, IInstancesDelayScheduler scheduler)
    {
        _release = release ?? throw new ArgumentNullException(nameof(release));
        _scheduler = scheduler ?? throw new ArgumentNullException(nameof(scheduler));
    }

    /// <summary>本次排程的档位（<see cref="ResourceReleaseTier.Off"/> 表示没有排程）。</summary>
    public ResourceReleaseTier Tier { get; private set; } = ResourceReleaseTier.Off;

    /// <summary>是否还有后续阶段没跑完。</summary>
    public bool IsRunning
    {
        get
        {
            lock (_gate)
            {
                return _pending is { IsCompleted: false };
            }
        }
    }

    /// <summary>排程的任务句柄，供测试 <c>await</c>；没有排程时为 null。</summary>
    public Task? Pending
    {
        get
        {
            lock (_gate)
            {
                return _pending;
            }
        }
    }

    /// <summary>最近一次收到报告（失败的阶段不会覆盖成空）。</summary>
    public ResourceReleaseReport? LastReport { get; private set; }

    /// <summary>上一次排程遇到的错误（用户可读）；成功时为 null。</summary>
    public string? LastError { get; private set; }

    /// <summary>已完成的阶段数（T3/T4/T5 中执行到哪一段），供状态栏与测试使用。</summary>
    public int CompletedStages { get; private set; }

    /// <summary>
    /// 开始排程。档位为 <see cref="ResourceReleaseTier.Off"/> 时什么都不做（连计时器都不起）；
    /// 重复调用会先取消上一次排程，保证同一时间只有一条时间轴。
    /// </summary>
    public void Begin(ResourceReleaseTier tier)
    {
        CancelPending();

        Tier = tier;
        LastError = null;
        CompletedStages = 0;
        if (tier == ResourceReleaseTier.Off)
        {
            Tier = ResourceReleaseTier.Off;
            return;
        }

        var cancellation = new CancellationTokenSource();
        lock (_gate)
        {
            _cancellation = cancellation;
            _pending = RunAsync(tier, cancellation.Token);
        }
    }

    /// <summary>
    /// 结束排程：取消后续阶段（含正在跑的阻塞 GC），然后恢复被 L2 释放的注册表与仓库快照。
    /// 未排程或游戏从未启动过时是空操作。
    /// </summary>
    public async Task<OperationResult> EndAsync(CancellationToken ct = default)
    {
        var pending = CancelPending();
        if (pending is not null)
        {
            try
            {
                // 等时间轴真的停下来，避免 Restore 与 L2 抢同一个注册表锁。
                await pending.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // 取消是正常路径。
            }
        }

        var restore = await _release.RestoreAsync(ct).ConfigureAwait(false);
        if (!restore.IsSuccess)
        {
            LastError = restore.Message;
        }

        return restore;
    }

    private Task? CancelPending()
    {
        CancellationTokenSource? cancellation;
        Task? pending;
        lock (_gate)
        {
            cancellation = _cancellation;
            pending = _pending;
            _cancellation = null;
            _pending = null;
        }

        if (cancellation is null)
        {
            return null;
        }

        try
        {
            cancellation.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // 已经结束的排程被再次取消，忽略。
        }

        return pending ?? Task.CompletedTask;
    }

    private async Task RunAsync(ResourceReleaseTier tier, CancellationToken ct)
    {
        try
        {
            foreach (var (delay, stageTier) in Schedule(tier))
            {
                if (delay > TimeSpan.Zero)
                {
                    await _scheduler.DelayAsync(delay, ct).ConfigureAwait(false);
                }

                ct.ThrowIfCancellationRequested();

                var result = await _release.ReleaseAsync(stageTier, null, ct).ConfigureAwait(false);
                if (!result.IsSuccess)
                {
                    LastError = result.Message;
                    return;
                }

                if (result.Value is { } report)
                {
                    LastReport = report;
                }

                CompletedStages++;
                if (result.Value?.Cancelled == true)
                {
                    return;
                }
            }
        }
        catch (OperationCanceledException)
        {
            // 游戏退出 / 页面离开：后续阶段不再执行，恢复交给 EndAsync。
        }
        catch (Exception ex)
        {
            LastError = ex.Message;
        }
    }

    /// <summary>
    /// T3/T4/T5 的时间表（第二项是「距上一段」的等待）：T3 立即，T4 在 T3 之后 10 秒，
    /// T5 再 20 秒——累加正好是规格里的 T2+10s 与 T2+30s（层本身的耗时会让它略晚，可接受）。
    /// </summary>
    private static IReadOnlyList<(TimeSpan Delay, ResourceReleaseTier Tier)> Schedule(ResourceReleaseTier tier) =>
        tier switch
        {
            ResourceReleaseTier.Conservative =>
            [
                (TimeSpan.Zero, ResourceReleaseTier.Conservative),
            ],
            ResourceReleaseTier.Balanced =>
            [
                (TimeSpan.Zero, ResourceReleaseTier.Conservative),
                (TimeSpan.FromSeconds(10), ResourceReleaseTier.Balanced),
            ],
            ResourceReleaseTier.Aggressive =>
            [
                (TimeSpan.Zero, ResourceReleaseTier.Conservative),
                (TimeSpan.FromSeconds(10), ResourceReleaseTier.Balanced),
                (TimeSpan.FromSeconds(20), ResourceReleaseTier.Aggressive),
            ],
            _ => [],
        };
}
