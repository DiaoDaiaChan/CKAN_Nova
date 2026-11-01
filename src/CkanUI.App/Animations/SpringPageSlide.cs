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

        // 用帧时间而不是定时器：定时器在无头测试里不随 ForceRenderTimerTick 推进，
        // 过渡会一直挂着不结束；帧通道与弹簧同源，测试与应用走同一条路径。
        var settle = new SettleTicker(SettleSeconds);
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

    /// <summary>按帧累计到点的等待器：走帧泵，不依赖 UI 定时器。</summary>
    private sealed class SettleTicker : IMotionTicker
    {
        // 不加 RunContinuationsAsynchronously：续体要在完成它的那条 UI 线程上跑，
        // 否则 finally 里的 MoveTo 会从线程池线程写控件。
        private readonly TaskCompletionSource _done = new();
        private readonly double _seconds;
        private double _elapsed;

        internal SettleTicker(double seconds) => _seconds = seconds;

        internal Task Task => _done.Task;

        internal void Complete() => _done.TrySetResult();

        public bool Advance(double seconds)
        {
            _elapsed += seconds;
            if (_elapsed < _seconds)
            {
                return true;
            }

            _done.TrySetResult();
            return false;
        }
    }
}
