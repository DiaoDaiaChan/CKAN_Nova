using System;
using System.Diagnostics;
using Avalonia.Threading;

namespace CkanUI.App.Diagnostics;

/// <summary>
/// 空闲期托管堆回收策略：纯函数集合，便于单测覆盖门槛与冷却判定。
/// </summary>
public static class IdleMemoryReclaimerPolicy
{
    /// <summary>轮询节拍：每隔这么久问一次“现在能回收吗”。</summary>
    public const double PollSeconds = 8;

    /// <summary>窗口挂上后至少等这么久才允许第一次回收：启动加载期堆还在长，此时回收纯属白干。
    /// 实测 8 秒就回收的版本会在加载完成后又被撑回 840 MB，然后被 120 秒冷却卡死——撑回是冷却太长造成的，
    /// 不是等得不够久；现在冷却降到 20 秒且有 128 MB 增长门限，12 秒的首次等待足够跨过首屏加载。
    /// 首屏 1 GB 峰值实测在 t+45~55s 才被压回 440-514 MB，改到 12 秒后提前到 t+12~20s。</summary>
    public const double MinAttachSeconds = 12;

    /// <summary>工作集比上次回收后至少多涨这么多才值得再回收一次（128 MB）。</summary>
    public const long GrowthThresholdBytes = 134_217_728L;

    /// <summary>两次回收之间的最小间隔：避免频繁 GC 拖慢交互。20 秒配合 128 MB 增长门限，
    /// 既能在一个会话里最多多回收一两次，又不会因为加载把工作集撑回去后一直卡在冷却里。</summary>
    public static TimeSpan MinInterval => TimeSpan.FromSeconds(20);

    /// <summary>最后一次用户输入之后要再静置这么久才允许压缩堆：IsIdle 只要 2.5 秒，
    /// 用户看完一段文字随手一点时压缩式 GC 正好在跑，就会被记成一次"页面卡死"（实测单次 0.7-1.5 秒）。</summary>
    public const double MinQuietSeconds = 8;

    /// <summary>判定此刻是否值得做一次空闲回收。</summary>
    /// <param name="isIdle">当前是否处于空闲降频状态。</param>
    /// <param name="sinceLastInteraction">距离最后一次用户输入的时长。</param>
    /// <param name="sinceAttach">窗口挂上至今的时长。</param>
    /// <param name="hasReclaimed">进程启动以来是否已经回收过。</param>
    /// <param name="workingSetBytes">当前工作集字节数。</param>
    /// <param name="workingSetAfterLastReclaim">上次回收完成时的工作集字节数。</param>
    /// <param name="sinceLastReclaim">距离上次回收的时长。</param>
    /// <returns>true 表示应当立刻回收。</returns>
    public static bool ShouldReclaim(
        bool isIdle,
        TimeSpan sinceLastInteraction,
        TimeSpan sinceAttach,
        bool hasReclaimed,
        long workingSetBytes,
        long workingSetAfterLastReclaim,
        TimeSpan sinceLastReclaim)
    {
        if (!isIdle || sinceAttach < TimeSpan.FromSeconds(MinAttachSeconds))
        {
            return false;
        }

        if (sinceLastInteraction < TimeSpan.FromSeconds(MinQuietSeconds))
        {
            return false;
        }

        if (!hasReclaimed)
        {
            return true;
        }

        if (sinceLastReclaim < MinInterval)
        {
            return false;
        }

        return workingSetBytes - workingSetAfterLastReclaim >= GrowthThresholdBytes;
    }
}

/// <summary>
/// 空闲期托管堆回收器：每 8 秒问一次“现在空闲且长够了 128 MB 吗”，是就做一次压缩式第 2 代回收，
/// 把已经不需要的托管内存还给操作系统。启动加载期（前 24 秒）一律跳过，只在用户无操作时执行。
/// 3000 个模组的满载会话实测工作集 995 MB → 433 MB；稳态 981 MB → 481 MB。
/// </summary>
public sealed class IdleMemoryReclaimer : IDisposable
{
    private static readonly Lazy<IdleMemoryReclaimer> LazyInstance = new(() => new IdleMemoryReclaimer());

    private readonly Func<long> _readWorkingSet;
    private readonly Action _reclaim;
    private readonly Action _softReclaim;
    private readonly Func<bool> _isIdle;
    private readonly Func<TimeSpan> _sinceInteraction;
    private readonly Stopwatch _sinceLastReclaim = Stopwatch.StartNew();
    private readonly Stopwatch _attachAge = Stopwatch.StartNew();
    private readonly Func<TimeSpan> _sinceAttach;

    private DispatcherTimer? _timer;
    private bool _attached;
    private bool _hasReclaimed;
    private long _workingSetAfterLastReclaim;

    public IdleMemoryReclaimer()
        : this(
            ReadProcessWorkingSet,
            DefaultReclaim,
            () => DynamicRefreshRateController.Instance.IsIdle,
            null,
            () => DynamicRefreshRateController.Instance.SinceLastInteraction,
            SoftReclaim)
    {
    }

    /// <summary>可注入依赖的构造：策略单测用假时钟、假工作集与假回收动作。</summary>
    /// <param name="readWorkingSet">读取当前工作集字节数。</param>
    /// <param name="reclaim">执行一次实际回收。</param>
    /// <param name="isIdle">读取当前空闲状态。</param>
    /// <param name="sinceAttach">窗口挂上至今的时长；为 null 时用实例自己的计时器。</param>
    public IdleMemoryReclaimer(Func<long> readWorkingSet, Action reclaim, Func<bool> isIdle, Func<TimeSpan>? sinceAttach = null, Func<TimeSpan>? sinceInteraction = null, Action? softReclaim = null)
    {
        ArgumentNullException.ThrowIfNull(readWorkingSet);
        ArgumentNullException.ThrowIfNull(reclaim);
        ArgumentNullException.ThrowIfNull(isIdle);

        _readWorkingSet = readWorkingSet;
        _reclaim = reclaim;
        _isIdle = isIdle;
        _sinceAttach = sinceAttach ?? (() => _attachAge.Elapsed);
        // 未注入时按「很久没输入」处理：构造回收器的地方都是显式要回收的，真实场景由单例构造函数注入窗口计时。
        _sinceInteraction = sinceInteraction ?? (() => TimeSpan.FromMinutes(10));
        _softReclaim = softReclaim ?? reclaim;
    }

    /// <summary>进程级单例。</summary>
    public static IdleMemoryReclaimer Instance => LazyInstance.Value;

    /// <summary>已执行过的回收次数（诊断用）。</summary>
    public int ReclaimCount { get; private set; }

    /// <summary>挂上空闲事件；重复调用无副作用。</summary>
    public void Attach()
    {
        if (_attached)
        {
            return;
        }

        _attached = true;

        // 用固定节拍轮询而不是订阅空闲事件：窗口失焦/其它程序抢焦点会让空闲状态在毫秒级抖动，
        // 每次抖动都重排定时器就永远等不到 8 秒静置（实测 20 秒采样里焦点丢了 71 次）。
        // 每 8 秒问一次“现在空闲吗 + 涨够 128MB 吗”，不空闲就直接跳过，成本可忽略。
        _timer = new DispatcherTimer(
            TimeSpan.FromSeconds(IdleMemoryReclaimerPolicy.PollSeconds),
            DispatcherPriority.Background,
            (_, _) => Poll());
        _timer.Start();

        // 驻留预算复用这个节拍：窗口已经挂上，空闲判定与工作集都拿得到（见 PageResidencyPolicy）。
        PageResidencyPolicy.Install();
    }

    /// <summary>一次空闲节拍：先让各策略看一眼（每拍都跑），再按工作集决定要不要压堆。</summary>
    private void Poll()
    {
        InvokeIdleTick();
        ReclaimIfNeeded();
    }

    /// <summary>解绑并撤销尚未触发的回收。</summary>
    public void Detach()
    {
        if (!_attached)
        {
            return;
        }

        _attached = false;
        _timer?.Stop();
        _timer = null;
    }

    /// <summary>按策略判定并在需要时执行一次回收。</summary>
    /// <returns>true 表示本次真的执行了回收。</returns>
    public bool ReclaimIfNeeded()
    {
        if (!_isIdle())
        {
            return false;
        }

        var workingSet = _readWorkingSet();
        if (!IdleMemoryReclaimerPolicy.ShouldReclaim(
                isIdle: true,
                _sinceInteraction(),
                _sinceAttach(),
                _hasReclaimed,
                workingSet,
                _workingSetAfterLastReclaim,
                _sinceLastReclaim.Elapsed))
        {
            return false;
        }

        InvokeBeforeReclaim();
        _softReclaim();

        _hasReclaimed = true;
        _workingSetAfterLastReclaim = _readWorkingSet();
        _sinceLastReclaim.Restart();
        ReclaimCount++;
        return true;
    }

    /// <summary>立即执行一次回收（不等轮询与策略判定）：预热结束后工作集被撑大时用。</summary>
    public void ReclaimNow()
    {
        InvokeBeforeReclaim();
        _reclaim();
        _hasReclaimed = true;
        _workingSetAfterLastReclaim = _readWorkingSet();
        _sinceLastReclaim.Restart();
        ReclaimCount++;
    }

    /// <inheritdoc />
    public void Dispose() => Detach();

    private static long ReadProcessWorkingSet()
    {
        using var process = Process.GetCurrentProcess();
        return process.WorkingSet64;
    }

    /// <summary>
    /// 回收前的可选钩子：先把「离页后不再需要」的行投影丢掉，再压缩堆。
    /// 位置在 GC 之前是因为投影本身也压着大对象；先松手再压缩，回收一段就能带走。
    /// </summary>
    public static Action? BeforeReclaim { get; set; }

    /// <summary>
    /// 空闲节拍钩子：与回收判定同一个定时器，但**每拍都调**——回收判定要等工作集涨够阈值，
    /// 驻留预算之类的策略等不起（详见 <see cref="PageResidencyPolicy"/>）。
    /// </summary>
    public static Action? IdleTick { get; set; }

    private static void InvokeIdleTick()
    {
        try
        {
            IdleTick?.Invoke();
        }
        catch (Exception ex)
        {
            // 空闲策略出问题不能把回收器带停：这里只记一笔。
            Trace.WriteLine($"空闲节拍处理失败：{ex.Message}");
        }
    }

    private static void InvokeBeforeReclaim()
    {
        try
        {
            BeforeReclaim?.Invoke();
        }
        catch (Exception ex)
        {
            // 投影释放失败不该挡住 GC：这里只记一笔，继续回收。
            Trace.WriteLine($"回收前的投影释放失败：{ex.Message}");
        }
    }

    /// <summary>
    /// 空闲轮询用的"轻"回收：不压缩、只做一次阻塞式第 2 代回收。
    /// 压缩式回收要把整堆活对象搬一遍，实测一次 0.7–1.7 秒，落在导航/点击窗口里就是一整帧冻结；
    /// 轮询路径每隔几十秒就会触发一次，不值得为那点驻留拿帧率换。启动/预热阶段的 ReclaimNow 仍走压缩版。
    /// </summary>
    private static void SoftReclaim()
    {
        GC.Collect(2, GCCollectionMode.Forced, blocking: true, compacting: false);
        GC.WaitForPendingFinalizers();
    }

    private static void DefaultReclaim()
    {

        // Aggressive + compacting 才会搬动大对象堆：撑大工作集的主要是 LOH 碎片，普通第 2 代回收压不下来。
        // 只做一次：两次压缩实测在导航窗口里造成 0.7–1.5 秒的整帧冻结（[nav] 行的 pause 与 maxGap 对得上），
        // 第一次已经搬完活着的大对象，第二次基本是重复劳动。
        GC.Collect(2, GCCollectionMode.Aggressive, blocking: true, compacting: true);
        GC.WaitForPendingFinalizers();
    }

}