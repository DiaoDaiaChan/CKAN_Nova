using System;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Styling;
using CkanUI.App.Theming;

namespace CkanUI.App.Animations;

/// <summary>
/// 窗口级过渡动效：最小化、还原到前台、最大化/还原、关闭、隐藏到托盘/任务栏。
/// </summary>
public static class WindowAnimation
{
    private static readonly Easing SmoothDecel = new SplineEasing(0.1, 0.9, 0.2, 1.0);
    private static readonly Easing SmoothAccel = new SplineEasing(0.4, 0.0, 0.7, 0.2);
    private static readonly Easing SmoothEaseInOut = new SplineEasing(0.4, 0.0, 0.2, 1.0);

    /// <summary>
    /// 获取当前动效时长（从资源字典读 CkanMotionDurationSpringSoft/Base，精细适配平滑窗口动效）。
    /// </summary>
    public static TimeSpan GetDuration(Control? visual = null)
    {
        if (MotionBudget.IsCollapsed)
        {
            return TimeSpan.Zero;
        }

        if (visual is not null && visual.TryFindResource(MotionHost.SpringSoftKey, out var raw) && raw is TimeSpan visualDuration)
        {
            return visualDuration;
        }

        if (Application.Current?.Resources.TryGetResource(MotionHost.SpringSoftKey, null, out var appRaw) == true && appRaw is TimeSpan appDuration)
        {
            return appDuration;
        }

        return TimeSpan.FromMilliseconds(260);
    }

    /// <summary>
    /// 确保目标控件具有 ScaleTransform 与合适的分辨中心。
    /// </summary>
    public static ScaleTransform EnsureScaleTransform(Control target)
    {
        target.RenderTransformOrigin = new RelativePoint(0.5, 0.5, RelativeUnit.Relative);
        if (target.RenderTransform is ScaleTransform st)
        {
            return st;
        }

        var scale = new ScaleTransform(1d, 1d);
        target.RenderTransform = scale;
        return scale;
    }

    /// <summary>
    /// 最小化出场动画：平滑多关键帧下沉缩放（1.0 -> 0.98 -> 0.90）+ 自然淡出。
    /// </summary>
    public static async Task AnimateMinimizeAsync(Control target)
    {
        var duration = GetDuration(target);
        if (duration <= TimeSpan.Zero)
        {
            return;
        }

        EnsureScaleTransform(target);
        var animation = new Animation
        {
            Duration = duration,
            Easing = SmoothAccel,
            Children =
            {
                new KeyFrame
                {
                    Cue = new Cue(0d),
                    Setters =
                    {
                        new Setter(Visual.OpacityProperty, 1d),
                        new Setter(ScaleTransform.ScaleXProperty, 1d),
                        new Setter(ScaleTransform.ScaleYProperty, 1d),
                    }
                },
                new KeyFrame
                {
                    Cue = new Cue(0.35d),
                    Setters =
                    {
                        new Setter(Visual.OpacityProperty, 0.92d),
                        new Setter(ScaleTransform.ScaleXProperty, 0.985d),
                        new Setter(ScaleTransform.ScaleYProperty, 0.985d),
                    }
                },
                new KeyFrame
                {
                    Cue = new Cue(1d),
                    Setters =
                    {
                        new Setter(Visual.OpacityProperty, 0d),
                        new Setter(ScaleTransform.ScaleXProperty, 0.90d),
                        new Setter(ScaleTransform.ScaleYProperty, 0.90d),
                    }
                }
            }
        };

        try
        {
            await animation.RunAsync(target).ConfigureAwait(true);
        }
        catch
        {
            // 防护动画被中断或控件被销毁
        }
        finally
        {
            // 如果目标是窗口本身，动画结束后保持完全透明，防止在系统真正最小化瞬间因重置视觉而闪现原始边框
            if (target is not Window)
            {
                ResetVisualState(target);
            }
        }
    }

    /// <summary>
    /// 从最小化/后台恢复到前台动效：轻微浮现 + 自然平滑回弹（0.94 -> 1.006 -> 1.0）+ 淡入。
    /// </summary>
    public static async Task AnimateRestoreAsync(Control target)
    {
        var duration = GetDuration(target);
        if (duration <= TimeSpan.Zero)
        {
            ResetVisualState(target);
            return;
        }

        EnsureScaleTransform(target);
        target.Opacity = 0d;

        var animation = new Animation
        {
            Duration = duration,
            Easing = SmoothDecel,
            Children =
            {
                new KeyFrame
                {
                    Cue = new Cue(0d),
                    Setters =
                    {
                        new Setter(Visual.OpacityProperty, 0d),
                        new Setter(ScaleTransform.ScaleXProperty, 0.94d),
                        new Setter(ScaleTransform.ScaleYProperty, 0.94d),
                    }
                },
                new KeyFrame
                {
                    Cue = new Cue(0.65d),
                    Setters =
                    {
                        new Setter(Visual.OpacityProperty, 0.98d),
                        new Setter(ScaleTransform.ScaleXProperty, 1.006d),
                        new Setter(ScaleTransform.ScaleYProperty, 1.006d),
                    }
                },
                new KeyFrame
                {
                    Cue = new Cue(1d),
                    Setters =
                    {
                        new Setter(Visual.OpacityProperty, 1d),
                        new Setter(ScaleTransform.ScaleXProperty, 1d),
                        new Setter(ScaleTransform.ScaleYProperty, 1d),
                    }
                }
            }
        };

        try
        {
            await animation.RunAsync(target).ConfigureAwait(true);
        }
        catch
        {
            // 防护
        }
        finally
        {
            ResetVisualState(target);
        }
    }

    /// <summary>
    /// 关闭出场动画：平滑缩放缓冲（1.0 -> 0.98 -> 0.92）+ 自然淡出。
    /// </summary>
    public static async Task AnimateCloseAsync(Control target)
    {
        var duration = GetDuration(target);
        if (duration <= TimeSpan.Zero)
        {
            return;
        }

        EnsureScaleTransform(target);
        var animation = new Animation
        {
            Duration = duration,
            Easing = SmoothAccel,
            Children =
            {
                new KeyFrame
                {
                    Cue = new Cue(0d),
                    Setters =
                    {
                        new Setter(Visual.OpacityProperty, 1d),
                        new Setter(ScaleTransform.ScaleXProperty, 1d),
                        new Setter(ScaleTransform.ScaleYProperty, 1d),
                    }
                },
                new KeyFrame
                {
                    Cue = new Cue(0.35d),
                    Setters =
                    {
                        new Setter(Visual.OpacityProperty, 0.92d),
                        new Setter(ScaleTransform.ScaleXProperty, 0.985d),
                        new Setter(ScaleTransform.ScaleYProperty, 0.985d),
                    }
                },
                new KeyFrame
                {
                    Cue = new Cue(1d),
                    Setters =
                    {
                        new Setter(Visual.OpacityProperty, 0d),
                        new Setter(ScaleTransform.ScaleXProperty, 0.92d),
                        new Setter(ScaleTransform.ScaleYProperty, 0.92d),
                    }
                }
            }
        };

        try
        {
            await animation.RunAsync(target).ConfigureAwait(true);
        }
        catch
        {
            // 防护
        }
    }

    /// <summary>
    /// 隐藏到任务栏/托盘动效：平滑收缩（1.0 -> 0.97 -> 0.88）+ 自然淡出。
    /// </summary>
    public static async Task AnimateHideToTaskbarAsync(Control target)
    {
        var duration = GetDuration(target);
        if (duration <= TimeSpan.Zero)
        {
            return;
        }

        EnsureScaleTransform(target);
        var animation = new Animation
        {
            Duration = duration,
            Easing = SmoothAccel,
            Children =
            {
                new KeyFrame
                {
                    Cue = new Cue(0d),
                    Setters =
                    {
                        new Setter(Visual.OpacityProperty, 1d),
                        new Setter(ScaleTransform.ScaleXProperty, 1d),
                        new Setter(ScaleTransform.ScaleYProperty, 1d),
                    }
                },
                new KeyFrame
                {
                    Cue = new Cue(0.35d),
                    Setters =
                    {
                        new Setter(Visual.OpacityProperty, 0.90d),
                        new Setter(ScaleTransform.ScaleXProperty, 0.97d),
                        new Setter(ScaleTransform.ScaleYProperty, 0.97d),
                    }
                },
                new KeyFrame
                {
                    Cue = new Cue(1d),
                    Setters =
                    {
                        new Setter(Visual.OpacityProperty, 0d),
                        new Setter(ScaleTransform.ScaleXProperty, 0.88d),
                        new Setter(ScaleTransform.ScaleYProperty, 0.88d),
                    }
                }
            }
        };

        try
        {
            await animation.RunAsync(target).ConfigureAwait(true);
        }
        catch
        {
            // 防护
        }
        finally
        {
            if (target is not Window)
            {
                ResetVisualState(target);
            }
        }
    }

    /// <summary>
    /// 最大化/还原动效：针对窗口内容执行带有弹性冲顶的平滑多关键帧缩放过渡（最大化：0.985 -> 1.003 -> 1.0；还原：1.015 -> 0.998 -> 1.0）。
    /// </summary>
    public static async Task AnimateMaximizeRestoreAsync(Control target, bool isMaximizing)
    {
        var duration = GetDuration(target);
        if (duration <= TimeSpan.Zero)
        {
            ResetVisualState(target);
            return;
        }

        EnsureScaleTransform(target);
        double startScale = isMaximizing ? 0.985d : 1.015d;
        double overshootScale = isMaximizing ? 1.003d : 0.998d;
        double startOpacity = isMaximizing ? 0.92d : 0.94d;

        var animation = new Animation
        {
            Duration = duration,
            Easing = SmoothDecel,
            Children =
            {
                new KeyFrame
                {
                    Cue = new Cue(0d),
                    Setters =
                    {
                        new Setter(Visual.OpacityProperty, startOpacity),
                        new Setter(ScaleTransform.ScaleXProperty, startScale),
                        new Setter(ScaleTransform.ScaleYProperty, startScale),
                    }
                },
                new KeyFrame
                {
                    Cue = new Cue(0.60d),
                    Setters =
                    {
                        new Setter(Visual.OpacityProperty, 0.99d),
                        new Setter(ScaleTransform.ScaleXProperty, overshootScale),
                        new Setter(ScaleTransform.ScaleYProperty, overshootScale),
                    }
                },
                new KeyFrame
                {
                    Cue = new Cue(1d),
                    Setters =
                    {
                        new Setter(Visual.OpacityProperty, 1d),
                        new Setter(ScaleTransform.ScaleXProperty, 1d),
                        new Setter(ScaleTransform.ScaleYProperty, 1d),
                    }
                }
            }
        };

        try
        {
            await animation.RunAsync(target).ConfigureAwait(true);
        }
        catch
        {
            // 防护
        }
        finally
        {
            ResetVisualState(target);
        }
    }

    /// <summary>
    /// 恢复控件的默认视觉状态（Opacity = 1, Scale = 1）。
    /// </summary>
    public static void ResetVisualState(Control target)
    {
        target.Opacity = 1d;
        if (target.RenderTransform is ScaleTransform st)
        {
            st.ScaleX = 1d;
            st.ScaleY = 1d;
        }
    }
}
