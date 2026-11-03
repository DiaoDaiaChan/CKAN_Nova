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

    /// <summary>可点卡片的单边收缩像素数：整张卡比按钮大得多，按 12 px 收会像"整块面塌下去"。</summary>
    public const double CardInsetPixels = 6d;

    /// <summary>可点卡片的类名（与 <see cref="HoverLiftMotion.CardClass"/> 一致）。</summary>
    public const string CardClass = "CkanCardInteractive";

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
        if (surface is null)
        {
            return null;
        }

        // 收不收、收多少，统一看 InteractionSurface 的矩阵：按钮族与卡片收，行 / 菜单 / 页签不收。
        // 卡片容器（CkanCard / CkanCardInteractive / CkanEmptyStateCard …）必须收：
        // 样式里那条 :pressed 只有 scale(0.995)，300 px 的卡只缩 1.5 px，等于没反馈。
        // 卡片按整块面收 6 px 而不是按钮的 12 px —— 大面积按 12 px 收会像整块塌下去。
        // 这不会带回"双重边框"：PointerAuraLayer 的描边走元素自身的 RenderTransform，
        // 按下缩多少，描边就跟着缩多少，任何时刻只有一条边。
        var kind = InteractionSurface.KindOf(surface);
        if (!InteractionSurface.DipsOnPress(kind))
        {
            return null;
        }

        var scale = ScaleFor(
            surface.Bounds.Width,
            surface.Bounds.Height,
            InteractionSurface.DipInset(kind));

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
    public static double ScaleFor(double width, double height) => ScaleFor(width, height, PressInsetPixels);

    /// <summary>按表面尺寸与单边收缩量算收缩比例。</summary>
    public static double ScaleFor(double width, double height, double insetPixels)
    {
        var extent = Math.Max(width, height);
        var uniform = extent > 0
            ? Math.Clamp(1d - (insetPixels / extent), PressedScale, 1d)
            : PressedScale;

        return MotionBudget.Scaled(uniform);
    }
}