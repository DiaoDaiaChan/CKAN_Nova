using System;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;

using CkanUI.App.Controls;

namespace CkanUI.App.Animations;

/// <summary>
/// 指针按压的表面反馈：命中可交互表面后向中心收一点，松开弹回。
/// </summary>
/// <remarks>
/// 收缩量按"边"而不是按"比例"：100px 的按钮收 3.2px 看得出来，1000px 的卡片收 32px 就是塌陷。
/// 因此小控件保留 <see cref="PressedScale"/> 的下限，大控件只收 <see cref="PressInsetPixels"/>。
/// </remarks>
public static class InteractionMotion
{
    /// <summary>小控件的最小收缩比例。</summary>
    public const double PressedScale = 0.95d;

    /// <summary>大控件单边收缩的像素数。</summary>
    public const double PressInsetPixels = 12d;

    private static Control? _pressed;

    /// <summary>按下：返回被压缩的表面；没有命中就返回 null。</summary>
    public static Control? Press(PointerEventArgs e, Visual? root)
    {
        Release();

        if (MotionBudget.IsCollapsed)
        {
            return null;
        }

        var surface = PointerAuraLayer.FindSurface(e.Source as Visual, root);
        if (surface is null || surface is Border || surface is DataGridRow or MenuItem or TreeViewItem)
        {
            return null;
        }

        var scale = ScaleFor(surface.Bounds.Width, surface.Bounds.Height);

        // 收缩锚点必须落在按下点：以中心收缩会把边缘 3~12px 的区域缩到指针外，
        // 松开时命中点已经不在控件上，Button 不触发 Click —— 看起来就是"动画吃掉了点击"。
        var position = e.GetPosition(surface);
        if (surface.Bounds.Width > 0 && surface.Bounds.Height > 0)
        {
            surface.RenderTransformOrigin = new RelativePoint(
                Math.Clamp(position.X / surface.Bounds.Width, 0d, 1d),
                Math.Clamp(position.Y / surface.Bounds.Height, 0d, 1d),
                RelativeUnit.Relative);
        }

        _pressed = surface;
        TransformSpring.For(surface, SpringSpec.Tactile).MoveTo(scaleX: scale, scaleY: scale);
        return surface;
    }

    /// <summary>松开：把上一次按下的表面弹回原尺寸。</summary>
    public static void Release()
    {
        if (_pressed is null)
        {
            return;
        }

        var target = _pressed;
        _pressed = null;
        TransformSpring.For(target, SpringSpec.Tactile).MoveTo(scaleX: 1, scaleY: 1);
    }

    /// <summary>按表面尺寸算收缩比例。</summary>
    public static double ScaleFor(double width, double height)
    {
        var extent = Math.Max(width, height);
        var uniform = extent > 0
            ? Math.Clamp(1d - (PressInsetPixels / extent), PressedScale, 1d)
            : PressedScale;

        return MotionBudget.Scaled(uniform);
    }
}
