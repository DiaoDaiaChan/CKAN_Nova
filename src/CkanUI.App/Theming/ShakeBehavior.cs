using System;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Styling;

namespace CkanUI.App.Theming;

/// <summary>
/// 校验失败的抖动反馈（20 篇 P-10）：只有「输入被拒绝」才抖，其它失败不抖（抖动会被读成报错）。
/// 用法：<c>app:ShakeBehavior.IsError="{Binding HasValidationError}"</c>，false → true 的瞬间抖一次。
/// 采用欠阻尼弹簧，抖幅逐次衰减，不改布局尺寸。
/// </summary>
public static class ShakeBehavior
{
    /// <summary>抖动总时长（一次约 0.4 秒，再长就烦人了）。</summary>
    private static readonly TimeSpan Duration = TimeSpan.FromMilliseconds(420);

    /// <summary>抖动最大位移（12px，宽于 12 会有「控件在跳」的错觉）。</summary>
    private const double Amplitude = 12d;

    /// <summary>
    /// 附加属性的宿主类型：Avalonia 的 RegisterAttached 需要一个非静态的 TOwner（否则 CS0718
    /// 「静态类型不能用作类型参数」），而对外暴露的类必须是 static 才能写 <c>app:ShakeBehavior.IsError</c>，
    /// 所以用一个私有空类当登记宿主。
    /// </summary>
    private sealed class Host;

    /// <summary>是否处于校验失败态。</summary>
    public static readonly AttachedProperty<bool> IsErrorProperty =
        AvaloniaProperty.RegisterAttached<Host, Control, bool>("IsError");

    static ShakeBehavior()
    {
        IsErrorProperty.Changed.AddClassHandler<Control>(OnIsErrorChanged);
    }

    /// <summary>读取校验失败态。</summary>
    public static bool GetIsError(Control control) => control.GetValue(IsErrorProperty);

    /// <summary>设置校验失败态。</summary>
    public static void SetIsError(Control control, bool value) => control.SetValue(IsErrorProperty, value);

    private static void OnIsErrorChanged(Control control, AvaloniaPropertyChangedEventArgs args)
    {
        if (!args.GetNewValue<bool>())
        {
            return;
        }

        var duration = MotionPrimitives.ResolveDuration(control, MotionHost.BaseKey);
        if (duration <= TimeSpan.Zero)
        {
            return;
        }

        _ = ShakeAsync(control, duration);
    }

    private static async Task ShakeAsync(Control control, TimeSpan tokenDuration)
    {
        // token 是「基础时长」，抖动固定用略短的节奏，避免高动效档下拖成慢摇。
        var duration = tokenDuration < Duration ? tokenDuration : Duration;

        if (control.RenderTransform is not TranslateTransform transform)
        {
            transform = new TranslateTransform();
            control.RenderTransform = transform;
        }

        var animation = new Animation
        {
            Duration = duration,
            FillMode = FillMode.Forward,
            Children =
            {
                Frame(0d, 0d),
                Frame(0.18d, -Amplitude),
                Frame(0.36d, Amplitude * 0.7d),
                Frame(0.54d, -Amplitude * 0.45d),
                Frame(0.72d, Amplitude * 0.25d),
                Frame(0.88d, -Amplitude * 0.1d),
                Frame(1d, 0d),
            },
        };

        try
        {
            await animation.RunAsync(transform).ConfigureAwait(true);
        }
        catch (InvalidOperationException)
        {
            // 控件在抖动途中被移除：忽略，收尾统一归零。
        }

        transform.X = 0d;
    }

    private static KeyFrame Frame(double cue, double offset) => new()
    {
        Cue = new Cue(cue),
        Setters = { new Setter(TranslateTransform.XProperty, offset) },
    };
}
