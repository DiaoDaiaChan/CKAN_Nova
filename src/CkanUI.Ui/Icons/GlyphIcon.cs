using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace CkanUI.Ui.Icons;

/// <summary>
/// 页内矢量图标（按 <see cref="Size"/> 等比缩放、颜色走 <see cref="Foreground"/>）。
/// </summary>
/// <remarks>
/// <para>纯代码控件，不带模板：插件只要 <c>xmlns:icons="using:CkanUI.Ui.Icons"</c> 就能用，
/// 既不新增 NuGet 包，也不需要宿主提供样式资源。</para>
/// <para>字形名未知或为空时<b>不画</b>（不是画一个占位方块）：图标是解释信息，猜错比空着更糟。</para>
/// </remarks>
public sealed class GlyphIcon : Control
{
    /// <summary>字形名（见 <see cref="GlyphCatalog.Names"/>）。</summary>
    public static readonly StyledProperty<string?> GlyphProperty =
        AvaloniaProperty.Register<GlyphIcon, string?>(nameof(Glyph));

    /// <summary>图标颜色。</summary>
    public static readonly StyledProperty<IBrush?> ForegroundProperty =
        AvaloniaProperty.Register<GlyphIcon, IBrush?>(nameof(Foreground));

    /// <summary>边长（正方形容器，默认 16）。</summary>
    public static readonly StyledProperty<double> SizeProperty =
        AvaloniaProperty.Register<GlyphIcon, double>(nameof(Size), 16d);

    private static readonly IBrush DefaultForeground = new SolidColorBrush(Color.Parse("#8A8F98"));

    static GlyphIcon()
    {
        AffectsRender<GlyphIcon>(GlyphProperty, ForegroundProperty);
        AffectsMeasure<GlyphIcon>(SizeProperty);
    }

    /// <summary>字形名。</summary>
    public string? Glyph
    {
        get => GetValue(GlyphProperty);
        set => SetValue(GlyphProperty, value);
    }

    /// <summary>图标颜色。</summary>
    public IBrush? Foreground
    {
        get => GetValue(ForegroundProperty);
        set => SetValue(ForegroundProperty, value);
    }

    /// <summary>边长。</summary>
    public double Size
    {
        get => GetValue(SizeProperty);
        set => SetValue(SizeProperty, value);
    }

    /// <summary>字形是否可解析（视图/测试用；不可解析时不占位、不绘制）。</summary>
    public bool HasGlyph => GlyphCatalog.Resolve(Glyph) is not null;

    /// <inheritdoc />
    protected override Size MeasureOverride(Size availableSize)
    {
        var side = Normalize(Size);
        return new Size(side, side);
    }

    /// <inheritdoc />
    public override void Render(DrawingContext context)
    {
        base.Render(context);

        var geometry = GlyphCatalog.Resolve(Glyph);
        if (geometry is null)
        {
            return;
        }

        var box = Normalize(Size);
        var width = Bounds.Width > 0 ? Math.Min(Bounds.Width, box) : box;
        var height = Bounds.Height > 0 ? Math.Min(Bounds.Height, box) : box;
        var scale = Math.Min(width, height) / 24d;
        if (scale <= 0)
        {
            return;
        }

        using (context.PushTransform(Matrix.CreateScale(scale, scale)))
        {
            context.DrawGeometry(Foreground ?? DefaultForeground, null, geometry);
        }
    }

    private static double Normalize(double side)
        => double.IsNaN(side) || double.IsInfinity(side) || side <= 0 ? 16d : side;
}
