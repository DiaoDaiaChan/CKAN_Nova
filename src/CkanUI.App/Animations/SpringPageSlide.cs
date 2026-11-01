using System;
using System.Threading;
using System.Threading.Tasks;

using Avalonia;
using Avalonia.Animation;
using Avalonia.Controls;
using Avalonia.Threading;

namespace CkanUI.App.Animations;

/// <summary>
/// 换页：两页像一张帘子一样横向错开，接缝从一侧移到另一侧——只动位置，不动不透明度。
/// </summary>
/// <remarks>
/// <para>为什么不用交叉淡入淡出：两页同时半透明叠在背景上，中间那段亮度必然凹陷
/// （历史记录里实测 38.3 → 28.6 → 34.7），文字还会出现双影；那正是 <c>PageTransition</c> 一直被置空的理由。</para>
/// <para>这里两页始终不透明、互不重叠：旧页从 0 滑到 +travel，新页从 −travel 滑到 0，接缝扫过整幅。
/// 因此既没有重影也没有亮度抖动，而且两页各自的弹簧可被下一次换页打断（<see cref="TransformSpring"/> 保位置与速度）。</para>
/// <para>位移量与档位一致：<see cref="MotionBudget.Amplitude"/> 缩放，关闭档直接返回（外壳回到瞬时替换）。</para>
/// </remarks>
public sealed class SpringPageSlide : IPageTransition
{
    /// <summary>单页最多滑多宽（超出这个宽度的窗口按这个宽度算，避免超宽屏滑得太夸张）。</summary>
    public const double MaxTravelPixels = 900d;

    /// <summary>没有测到宽度时的兜底行程。</summary>
    private const double FallbackTravelPixels = 640d;

    /// <summary>等弹簧收敛的时长：Glide 预设 0.32 s 收敛 + 一点余量。</summary>
    private const double SettleSeconds = 0.42d;

    /// <summary>兜底上限：两页还没停住也到此为止，避免掉帧时过渡一直挂着。</summary>
    private const double HardCapSeconds = 0.5d;

    /// <summary>精简档的行程上限：选了"精简"的人要的是轻推一下，不是整屏横扫。</summary>
    public const double ReducedTravelPixels = 48d;

    /// <inheritdoc />
    public async Task Start(Visual? from, Visual? to, bool forward, CancellationToken cancellationToken)
    {
        if (to is not Control incoming || MotionBudget.IsCollapsed)
        {
            return;
        }

        var outgoing = from as Control;
        var width = incoming.Bounds.Width > 0d ? incoming.Bounds.Width : FallbackTravelPixels;
        var travel = MotionBudget.Amplitude(Math.Min(width, MaxTravelPixels));
        if (MotionBudget.Mode == MotionMode.Reduced)
        {
            // 0.45 倍后仍有 400 px 左右，对"精简"来说还是整屏横扫；再压到 48 px 以内。
            travel = Math.Min(travel, ReducedTravelPixels);
        }
        var sign = forward ? 1d : -1d;

        var toSpring = TransformSpring.For(incoming, SpringSpec.Glide);
        toSpring.MoveTo(x: -sign * travel, snap: true);
        toSpring.MoveTo(x: 0d);

        var fromSpring = default(TransformSpring);
        if (outgoing is not null)
        {
            fromSpring = TransformSpring.For(outgoing, SpringSpec.Glide);
            fromSpring.MoveTo(x: 0d, snap: true);
            fromSpring.MoveTo(x: sign * travel);
        }

        // 收尾靠弹簧自己的"静止"事件：两页弹簧停住的那一刻就是幕布到位之时。
        // 不靠"帧还会继续来"——弹簧一停就没有新的失效，合成器不再排帧，
        // 等帧的等待器会永远挂着（精简档实测把旧页留在行程终点）。
        var settle = new SettleHandle(toSpring, fromSpring);
        if (TopLevel.GetTopLevel(incoming) is { } top)
        {
            MotionFramePump.Subscribe(top, settle);
        }
        else
        {
            settle.Complete();
        }

        using (cancellationToken.Register(settle.Complete))
        {
            await settle.Task.ConfigureAwait(false);
        }

        // 续体可能落在帧泵之外（取消、测试线程），而写控件必须回 UI 线程：
        // 真实应用里 CheckAccess 为真，直接原地执行，不多一次调度。
        if (Dispatcher.UIThread.CheckAccess())
        {
            Reset(toSpring, fromSpring);
        }
        else
        {
            await Dispatcher.UIThread.InvokeAsync(() => Reset(toSpring, fromSpring));
        }
    }

    private static void Reset(TransformSpring toSpring, TransformSpring? fromSpring)
    {
        toSpring.MoveTo(x: 0d, snap: true);
        fromSpring?.MoveTo(x: 0d, snap: true);
    }

    /// <summary>等两页弹簧都停住的收尾器：静止事件为主，帧累计只做硬上限兜底。</summary>
    private sealed class SettleHandle : IMotionTicker
    {
        // 不加 RunContinuationsAsynchronously：续体要在完成它的那条 UI 线程上跑，
        // 否则 reset 会从线程池线程写控件。
        private readonly TaskCompletionSource _done = new();
        private readonly double _hardCap;
        private readonly TransformSpring _to;
        private readonly TransformSpring? _from;
        private double _elapsed;

        internal SettleHandle(TransformSpring to, TransformSpring? from)
        {
            _hardCap = SettleSeconds + HardCapSeconds;
            _to = to;
            _from = from;
            _to.Settled += OnSpringSettled;
            if (_from is not null)
            {
                _from.Settled += OnSpringSettled;
            }

            // 起跑姿态是 snap 出来的，可能已经静止：先自己看一眼，别等事件。
            CheckSettled();
        }

        internal Task Task => _done.Task;

        public bool Advance(double seconds)
        {
            _elapsed += seconds;
            if (_elapsed >= _hardCap)
            {
                Complete();
            }

            return !_done.Task.IsCompleted;
        }

        internal void Complete()
        {
            if (_done.Task.IsCompleted)
            {
                return;
            }

            _to.Settled -= OnSpringSettled;
            if (_from is not null)
            {
                _from.Settled -= OnSpringSettled;
            }

            _done.TrySetResult();
        }

        private void OnSpringSettled(object? sender, EventArgs e) => CheckSettled();

        private void CheckSettled()
        {
            if (_to.IsSettled && (_from?.IsSettled ?? true))
            {
                Complete();
            }
        }
    }
}
