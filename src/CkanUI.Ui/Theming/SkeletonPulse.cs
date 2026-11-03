using System;
using System.Collections.Generic;
using System.Diagnostics;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Controls;
using Avalonia.Threading;

namespace CkanUI.Ui.Theming;

/// <summary>
/// 骨架屏脉动（加载态占位）：把「节奏」交给动效 token，而不是在视图里写死动画时长。
/// 用法：<c>&lt;Border Classes="CkanSkeleton" app:SkeletonPulse.IsEnabled="True" /&gt;</c>。
/// 动效强度关到「瞬时」时 token 为 0，本行为不再启动动画，骨架屏保持静态底色（无障碍要求：不做无法关闭的闪烁）。
/// </summary>
/// <remarks>
/// 实现说明：Avalonia 的 <see cref="Animation.RunAsync"/> 明确拒绝无限循环动画
/// （抛 <c>InvalidOperationException: Looping animations must not use the Run method</c>），
/// 早期版本因此每个骨架块都抛一次未观察异常、脉动从未真正跑起来。
/// 现在改为共享一个 33 ms 的 <see cref="DispatcherTimer"/>，按三角波算透明度：
/// 一个定时器服务所有骨架块，注册表空了就把定时器停掉，不留下常驻空转。
/// </remarks>
public sealed class SkeletonPulse
{
    /// <summary>脉动最暗处的透明度（再低就看不见骨架块的轮廓了）。</summary>
    private const double DimOpacity = 0.35d;

    /// <summary>共享节拍：33 ms ≈ 30 fps，只为几十个骨架块改一次 Opacity。</summary>
    private static readonly TimeSpan TickInterval = TimeSpan.FromMilliseconds(33);

    /// <summary>透明度变化小于这个值时不下发，避免把同一帧的重复命中算成重绘。</summary>
    private const double OpacityEpsilon = 0.004d;

    /// <summary>是否启用脉动。</summary>
    public static readonly AttachedProperty<bool> IsEnabledProperty =
        AvaloniaProperty.RegisterAttached<SkeletonPulse, Control, bool>("IsEnabled");

    /// <summary>每个控件自己的脉动状态：关闭脉动或离开可视树时立刻摘掉，避免后台空转。</summary>
    private static readonly AttachedProperty<PulseState?> StateProperty =
        AvaloniaProperty.RegisterAttached<SkeletonPulse, Control, PulseState?>("State");

    private static readonly List<PulseState> Active = new();
    private static readonly Stopwatch Clock = Stopwatch.StartNew();

    private static DispatcherTimer? _ticker;

    private SkeletonPulse()
    {
    }

    static SkeletonPulse()
    {
        IsEnabledProperty.Changed.AddClassHandler<Control>(OnIsEnabledChanged);

    }

    /// <summary>读取是否启用脉动。</summary>
    public static bool GetIsEnabled(Control control) => control.GetValue(IsEnabledProperty);

    /// <summary>设置是否启用脉动。</summary>
    public static void SetIsEnabled(Control control, bool value) => control.SetValue(IsEnabledProperty, value);

    private static void OnIsEnabledChanged(Control control, AvaloniaPropertyChangedEventArgs args)
    {
        if (args.GetNewValue<bool>())
        {
            // 资源要在挂进可视树之后才查得到（应用级 ThemeDictionaries 也是），所以等挂载再启动。
            control.AttachedToVisualTree -= OnAttached;
            control.AttachedToVisualTree += OnAttached;
            control.DetachedFromVisualTree -= OnDetached;
            control.DetachedFromVisualTree += OnDetached;

            if (TopLevel.GetTopLevel(control) is not null)
            {
                Start(control);
            }
        }
        else
        {
            Stop(control);
        }
    }

    private static void OnAttached(object? sender, VisualTreeAttachmentEventArgs e)
    {
        if (sender is Control control)
        {
            Start(control);
        }
    }

    private static void OnDetached(object? sender, VisualTreeAttachmentEventArgs e)
    {
        if (sender is Control control)
        {
            Stop(control);
        }
    }

    /// <summary>按当前动效 token 启动呼吸；时长归零时什么都不做。</summary>
    private static void Start(Control control)
    {
        if (control.GetValue(StateProperty) is not null)
        {
            return;
        }

        if (!control.TryFindResource(CkanMotionTokens.Base, out var value) || value is not TimeSpan duration || duration <= TimeSpan.Zero)
        {
            return;
        }

        var state = new PulseState(control, duration);
        control.SetValue(StateProperty, state);
        Active.Add(state);
        EnsureTicker();
    }

    private static void Stop(Control control)
    {
        if (control.GetValue(StateProperty) is { } state)
        {
            control.SetValue(StateProperty, null);
            Active.Remove(state);
        }

        control.Opacity = 1d;

        if (Active.Count == 0)
        {
            _ticker?.Stop();
            _ticker = null;
        }
    }

    private static void EnsureTicker()
    {
        if (_ticker is not null)
        {
            return;
        }

        _ticker = new DispatcherTimer(TickInterval, DispatcherPriority.Render, (_, _) => Tick());
        _ticker.Start();
    }

    private static void Tick()
    {
        if (Active.Count == 0)
        {
            _ticker?.Stop();
            _ticker = null;
            return;
        }

        var now = Clock.ElapsedMilliseconds;

        // 倒序遍历：Tick 里不改集合，但保留安全余量，避免将来加上“播完自动移除”时踩下标。
        for (var i = Active.Count - 1; i >= 0; i--)
        {
            var state = Active[i];

            // 骨架块都躺在加载态遮罩里：遮罩隐藏时控件仍挂在可视树上。
            // 不可见就不算透明度 = 不触发重绘，比按“挂载”判断省下整条 30 fps 的无用刷新。
            if (!state.Control.IsEffectivelyVisible)
            {
                if (state.Control.Opacity != 1d)
                {
                    state.Control.Opacity = 1d;
                }

                continue;
            }

            var opacity = ComputeOpacity(now - state.StartedMs, state.HalfCycleMs);
            if (Math.Abs(state.Control.Opacity - opacity) >= OpacityEpsilon)
            {
                state.Control.Opacity = opacity;
            }
        }
    }

    /// <summary>三角波 + smoothstep：一个完整呼吸是「亮 → 暗 → 亮」，时长 = 2 × halfCycleMs。</summary>
    private static double ComputeOpacity(double elapsedMs, double halfCycleMs)
    {
        if (halfCycleMs <= 0)
        {
            return 1d;
        }

        var phase = elapsedMs % (2d * halfCycleMs) / halfCycleMs;
        var triangle = phase <= 1d ? phase : 2d - phase;
        var eased = triangle * triangle * (3d - (2d * triangle));
        return 1d - ((1d - DimOpacity) * eased);
    }

    private sealed class PulseState(Control control, TimeSpan duration)
    {
        public Control Control { get; } = control;

        public double HalfCycleMs { get; } = duration.TotalMilliseconds;

        public long StartedMs { get; } = Clock.ElapsedMilliseconds;
    }
}
