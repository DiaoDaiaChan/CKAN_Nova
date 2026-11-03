using System;
using System.Runtime.CompilerServices;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.VisualTree;

namespace CkanUI.App.Animations;

/// <summary>
/// 把图标悬浮动效挂到界面上的附加属性：图标自己响应指针，宿主则代表"整块留白也算划过"。
/// </summary>
/// <remarks>
/// 加载分两层：
/// <list type="bullet">
/// <item><description><c>Enabled</c> 加在图标上，指针进出图标本身时驱动它；</description></item>
/// <item><description><c>Host</c> 加在按钮、标签页、导航项、图标底板这类宿主上，指针落在宿主的任何留白上，
/// 都驱动它内部的全部图标——用户划的是"那块地方"，不是 16 像素的字形。</description></item>
/// </list>
/// 两层都会走 <see cref="IconHoverTrack"/> 的引用计数，指针从留白移到图标上时不会重播进场。
/// </remarks>
public sealed class IconHoverMotion : AvaloniaObject
{
    /// <summary>打开某个图标的悬浮动效。</summary>
    public static readonly AttachedProperty<bool> EnabledProperty =
        AvaloniaProperty.RegisterAttached<IconHoverMotion, Control, bool>("Enabled");

    /// <summary>把某个控件声明为图标宿主：划过它的整块区域都驱动内部图标。</summary>
    public static readonly AttachedProperty<bool> HostProperty =
        AvaloniaProperty.RegisterAttached<IconHoverMotion, Control, bool>("Host");

    /// <summary>指定循环形态；图标自身没设时向上找第一个设过的祖先。</summary>
    public static readonly AttachedProperty<IconHoverShape> ShapeProperty =
        AvaloniaProperty.RegisterAttached<IconHoverMotion, Control, IconHoverShape>("Shape");

    private static readonly ConditionalWeakTable<Control, Wiring> Wirings = new();

    static IconHoverMotion()
    {
        EnabledProperty.Changed.AddClassHandler<Control>(static (control, args) => AttachSelf(control, args.GetNewValue<bool>()));
        HostProperty.Changed.AddClassHandler<Control>(static (control, args) => AttachHost(control, args.GetNewValue<bool>()));
    }

    private IconHoverMotion()
    {
    }

    /// <summary>读取图标是否启用悬浮动效。</summary>
    /// <param name="control">控件。</param>
    public static bool GetEnabled(Control control) => control.GetValue(EnabledProperty);

    /// <summary>设置图标是否启用悬浮动效。</summary>
    /// <param name="control">控件。</param>
    /// <param name="value">是否启用。</param>
    public static void SetEnabled(Control control, bool value) => control.SetValue(EnabledProperty, value);

    /// <summary>读取控件是否是图标宿主。</summary>
    /// <param name="control">控件。</param>
    public static bool GetHost(Control control) => control.GetValue(HostProperty);

    /// <summary>设置控件是否是图标宿主。</summary>
    /// <param name="control">控件。</param>
    /// <param name="value">是否作为宿主。</param>
    public static void SetHost(Control control, bool value) => control.SetValue(HostProperty, value);

    /// <summary>读取控件的循环形态。</summary>
    /// <param name="control">控件。</param>
    public static IconHoverShape GetShape(Control control) => control.GetValue(ShapeProperty);

    /// <summary>设置控件的循环形态（图标自身设了就用它，否则从祖先继承）。</summary>
    /// <param name="control">控件。</param>
    /// <param name="value">循环形态。</param>
    public static void SetShape(Control control, IconHoverShape value) => control.SetValue(ShapeProperty, value);

    /// <summary>沿视觉树向上找第一个明确指定的形态，都没有就是摇摆。</summary>
    /// <param name="visual">起点。</param>
    public static IconHoverShape ShapeOf(Visual visual)
    {
        var explicitShape = ExplicitShapeOf(visual);
        return explicitShape == IconHoverShape.Auto ? IconHoverShape.Waggle : explicitShape;
    }

    /// <summary>
    /// 图标真正该用的形态：显式设置 &gt; 字形语义（<see cref="IconHoverGlyph"/>：齿轮转、文件夹被推开）
    /// &gt; 宿主判定（<see cref="IconHoverRole"/>：标题栏按钮、图标底板、工具条按钮各有各的手感）。
    /// </summary>
    /// <param name="visual">图标。</param>
    public static IconHoverShape ResolveShape(Visual visual)
    {
        var explicitShape = ExplicitShapeOf(visual);
        if (explicitShape != IconHoverShape.Auto)
        {
            return explicitShape;
        }

        var glyphShape = IconHoverGlyph.Resolve(visual);
        return glyphShape == IconHoverShape.Auto ? IconHoverRole.Resolve(visual) : glyphShape;
    }

    /// <summary>判断一个视觉是不是图标：标准图标元素，或类型名以 Icon 结尾的图标控件。</summary>
    /// <param name="visual">待判断的视觉。</param>
    internal static bool IsIconVisual(Visual visual)
    {
        ArgumentNullException.ThrowIfNull(visual);
        return visual is IconElement || visual.GetType().Name.EndsWith("Icon", StringComparison.Ordinal);
    }

    private static void AttachSelf(Control control, bool enabled)
    {
        var wiring = Wirings.GetValue(control, static owner => new Wiring(owner));
        if (enabled == wiring.SelfAttached)
        {
            return;
        }

        if (enabled)
        {
            control.PointerEntered += wiring.SelfEntered;
            control.PointerExited += wiring.SelfExited;
        }
        else
        {
            control.PointerEntered -= wiring.SelfEntered;
            control.PointerExited -= wiring.SelfExited;
        }

        wiring.SelfAttached = enabled;
    }

    private static void AttachHost(Control control, bool enabled)
    {
        var wiring = Wirings.GetValue(control, static owner => new Wiring(owner));
        if (enabled == wiring.HostAttached)
        {
            return;
        }

        if (enabled)
        {
            control.PointerEntered += wiring.HostEntered;
            control.PointerExited += wiring.HostExited;
        }
        else
        {
            control.PointerEntered -= wiring.HostEntered;
            control.PointerExited -= wiring.HostExited;
        }

        wiring.HostAttached = enabled;
    }

    /// <summary>沿视觉树向上找第一个明确指定的形态；都没有返回 <see cref="IconHoverShape.Auto"/>。</summary>
    private static IconHoverShape ExplicitShapeOf(Visual visual)
    {
        ArgumentNullException.ThrowIfNull(visual);

        Visual? current = visual;
        while (current is not null)
        {
            if (current is Control control)
            {
                var shape = GetShape(control);
                if (shape != IconHoverShape.Auto)
                {
                    return shape;
                }
            }

            current = current.GetVisualParent();
        }

        return IconHoverShape.Auto;
    }

    private static void DriveIcons(Control host, bool entering)
    {
        foreach (var descendant in host.GetVisualDescendants())
        {
            if (descendant is not Control icon || !IsIconVisual(icon))
            {
                continue;
            }

            var track = IconHoverTrack.For(icon);
            if (entering)
            {
                track.Enter(ResolveShape(icon));
            }
            else
            {
                track.Exit();
            }
        }
    }

    /// <summary>一个控件身上的四个指针委托，只建一次，避免重复挂事件。</summary>
    private sealed class Wiring
    {
        internal Wiring(Control owner)
        {
            SelfEntered = (_, _) => IconHoverTrack.For(owner).Enter(ResolveShape(owner));
            SelfExited = (_, _) => IconHoverTrack.For(owner).Exit();
            HostEntered = (_, _) => DriveIcons(owner, entering: true);
            HostExited = (_, _) => DriveIcons(owner, entering: false);
        }

        internal EventHandler<PointerEventArgs> SelfEntered { get; }

        internal EventHandler<PointerEventArgs> SelfExited { get; }

        internal EventHandler<PointerEventArgs> HostEntered { get; }

        internal EventHandler<PointerEventArgs> HostExited { get; }

        internal bool SelfAttached { get; set; }

        internal bool HostAttached { get; set; }
    }
}
