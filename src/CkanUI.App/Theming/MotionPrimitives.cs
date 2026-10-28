using System;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Styling;

namespace CkanUI.App.Theming;

/// <summary>
/// 位移动画原语（20 篇 P-10）：滑入、错峰入场、抖动都只改 <see cref="Visual.RenderTransform"/> 与
/// <see cref="Visual.Opacity"/>，绝不改 Margin/Height —— 改布局属性会让整页重排，动画越顺滑越掉帧。
/// 时长一律读动效 token；关闭档（token = 0）直接落到终态，不启动任何动画。
/// </summary>
public static class MotionPrimitives
{
    /// <summary>弹簧参数：欠阻尼（ζ≈0.75），可被打断且不会来回晃。</summary>
    public const double SpringStiffness = 420d;

    /// <summary>弹簧阻尼系数。</summary>
    public const double SpringDamping = 32d;

    /// <summary>读动效 token；查不到或为 0 时返回 <see cref="TimeSpan.Zero"/>（调用方据此跳过动画）。</summary>
    public static TimeSpan ResolveDuration(Control control, string key)
    {
        return control.TryFindResource(key, out var value) && value is TimeSpan duration && duration > TimeSpan.Zero
            ? duration
            : TimeSpan.Zero;
    }

    /// <summary>从 <paramref name="offsetY"/> 位移滑回原位（抽屉、提示条、卡片入场共用）。</summary>
    public static async Task SlideInAsync(Control control, double offsetY, TimeSpan duration)
    {
        if (control.RenderTransform is not TranslateTransform transform)
        {
            transform = new TranslateTransform();
            control.RenderTransform = transform;
        }

        control.Opacity = 0d;
        transform.Y = offsetY;

        if (duration <= TimeSpan.Zero)
        {
            control.Opacity = 1d;
            transform.Y = 0d;
            return;
        }

        var animation = new Animation
        {
            Duration = duration,
            Easing = new SpringEasing
            {
                Mass = 1d,
                Stiffness = SpringStiffness,
                Damping = SpringDamping,
                InitialVelocity = 0d,
            },
            FillMode = FillMode.Forward,
            Children =
            {
                new KeyFrame
                {
                    Cue = new Cue(0d),
                    Setters = { new Setter(TranslateTransform.YProperty, offsetY) },
                },
                new KeyFrame
                {
                    Cue = new Cue(1d),
                    Setters = { new Setter(TranslateTransform.YProperty, 0d) },
                },
            },
        };

        try
        {
            await animation.RunAsync(transform).ConfigureAwait(true);
        }
        catch (InvalidOperationException)
        {
            // 控件在动画途中被换掉（页面切换、抽屉关闭）：忽略，下面统一落终态。
        }

        control.Opacity = 1d;
        transform.Y = 0d;
    }

    /// <summary>
    /// 页面错峰入场：把页面根面板的直接子元素按顺序各延迟 <paramref name="staggerMs"/> 毫秒滑入。
    /// 只处理前 <paramref name="maxItems"/> 个可见子元素，且只碰没有自带变换的元素（避免覆盖页面自己的动画）。
    /// </summary>
    public static async Task StaggerEntranceAsync(Control? page, TimeSpan duration, int maxItems = 5, int staggerMs = 40)
    {
        if (duration <= TimeSpan.Zero || page is null)
        {
            return;
        }

        var panel = Unwrap(page);
        if (panel is null)
        {
            return;
        }

        var index = 0;
        foreach (var child in panel.Children.OfType<Control>())
        {
            if (index >= maxItems)
            {
                break;
            }

            if (!child.IsVisible || child.RenderTransform is not null)
            {
                continue;
            }

            var delay = index * staggerMs;
            index++;
            await Task.Delay(delay).ConfigureAwait(true);
            _ = SlideInAsync(child, 10d, duration);
        }
    }

    /// <summary>把页面控件剥到根面板（页面通常是 UserControl → Grid/Border，最多剥两层）。</summary>
    private static Panel? Unwrap(Control page)
    {
        if (page is Panel panel)
        {
            return panel;
        }

        if (page is ContentControl { Content: Panel inner })
        {
            return inner;
        }

        return page is ContentControl { Content: Border { Child: Panel nested } } ? nested : null;
    }
}
