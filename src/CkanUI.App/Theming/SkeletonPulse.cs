using System;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Controls;
using Avalonia.Styling;

namespace CkanUI.App.Theming;

/// <summary>
/// 骨架屏脉动（加载态占位）：把「节奏」交给动效 token，而不是在视图里写死动画时长。
/// 用法：<c>&lt;Border Classes="CkanSkeleton" app:SkeletonPulse.IsEnabled="True" /&gt;</c>。
/// 动效强度关到「瞬时」时 token 为 0，本行为不再启动动画，骨架屏保持静态底色（无障碍要求：不做无法关闭的闪烁）。
/// </summary>
public sealed class SkeletonPulse
{
    /// <summary>脉动最暗处的透明度（再低就看不见骨架块的轮廓了）。</summary>
    private const double DimOpacity = 0.35d;

    /// <summary>是否启用脉动。</summary>
    public static readonly AttachedProperty<bool> IsEnabledProperty =
        AvaloniaProperty.RegisterAttached<SkeletonPulse, Control, bool>("IsEnabled");

    /// <summary>每个控件自己的取消源：关闭脉动或离开可视树时立刻停掉动画，避免后台空转。</summary>
    private static readonly AttachedProperty<CancellationTokenSource?> SessionProperty =
        AvaloniaProperty.RegisterAttached<SkeletonPulse, Control, CancellationTokenSource?>("Session");

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

    /// <summary>按当前动效 token 启动无限交替呼吸；时长归零时什么都不做。</summary>
    private static void Start(Control control)
    {
        if (control.GetValue(SessionProperty) is not null)
        {
            return;
        }

        if (!control.TryFindResource(MotionHost.BaseKey, out var value) || value is not TimeSpan duration || duration <= TimeSpan.Zero)
        {
            return;
        }

        var session = new CancellationTokenSource();
        control.SetValue(SessionProperty, session);
        _ = PulseAsync(control, duration, session);
    }

    private static void Stop(Control control)
    {
        if (control.GetValue(SessionProperty) is { } session)
        {
            control.SetValue(SessionProperty, null);
            session.Cancel();
            session.Dispose();
        }

        control.Opacity = 1d;
    }

    private static async Task PulseAsync(Control control, TimeSpan duration, CancellationTokenSource session)
    {
        var animation = new Animation
        {
            Duration = duration,
            IterationCount = IterationCount.Infinite,
            PlaybackDirection = PlaybackDirection.Alternate,
            Children =
            {
                new KeyFrame
                {
                    Cue = new Cue(0d),
                    Setters = { new Setter(Visual.OpacityProperty, 1d) },
                },
                new KeyFrame
                {
                    Cue = new Cue(1d),
                    Setters = { new Setter(Visual.OpacityProperty, DimOpacity) },
                },
            },
        };

        try
        {
            await animation.RunAsync(control, session.Token).ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
            // 关闭脉动或控件离开树：正常退出路径，不需要处理。
        }
        finally
        {
            if (!session.IsCancellationRequested)
            {
                control.Opacity = 1d;
            }
        }
    }
}