using System;
using Avalonia;
using Avalonia.Media;

namespace CkanUI.App.Controls.Backgrounds;

/// <summary>
/// 一帧程序化背景需要的全部外部状态。场景渲染器保持无状态：同一份输入永远画出同一帧，
/// 时间推进和光标缓动都由 <see cref="BackgroundSceneCanvas"/> 统一负责。
/// </summary>
public readonly record struct BackgroundSceneContext(
    Size Size,
    double Time,
    double Intensity,
    Point? Pointer,
    double PointerStrength,
    bool MotionEnabled,
    Color TextColor,
    Color AccentColor)
{
    public double Width => Size.Width;

    public double Height => Size.Height;

    public double MinSide => Math.Min(Width, Height);

    /// <summary>以 820 DIP 为基准的等比缩放，窗口变大时线稿跟着长，不会缩成一小团。</summary>
    public double Scale => Math.Clamp(MinSide / 820.0, 0.52, 1.85);

    /// <summary>没有光标时的构图锚点：画面中心。</summary>
    public Point Anchor => new(Width * 0.5, Height * 0.5);

    /// <summary>有光标用光标，没有就用中心；渲染器只关心“当前影响点”。</summary>
    public Point Influence => Pointer ?? Anchor;

    /// <summary>光标影响强度：没有光标或动效关闭时也能安全使用。</summary>
    public double InfluenceStrength => Pointer is null ? 0 : Math.Clamp(PointerStrength, 0, 1);

    public Color AccentAt(double opacity) => WithAlpha(AccentColor, opacity * Intensity);

    public Color TextAt(double opacity) => WithAlpha(TextColor, opacity * Intensity);

    /// <summary>把任意颜色按透明度缩放；透明度会夹在 0–1，渲染器不用自己防负值。</summary>
    public static Color WithAlpha(Color color, double opacity)
    {
        var alpha = (byte)Math.Clamp(opacity * 255.0, 0, 255);
        return Color.FromArgb(alpha, color.R, color.G, color.B);
    }
}
