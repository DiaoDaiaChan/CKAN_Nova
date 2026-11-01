using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace CkanUI.App.Controls;

/// <summary>
/// 聚光灯镂空遮罩控件：在全屏半透明暗色遮罩中，镂空出一个带有圆角的矩形高亮洞口，并绘制呼吸强调边框。
/// </summary>
public sealed class SpotlightMask : Control
{
    public static readonly StyledProperty<Rect> HighlightRectProperty =
        AvaloniaProperty.Register<SpotlightMask, Rect>(nameof(HighlightRect));

    public static readonly StyledProperty<double> CornerRadiusProperty =
        AvaloniaProperty.Register<SpotlightMask, double>(nameof(CornerRadius), 8.0);

    public static readonly StyledProperty<IBrush?> MaskBrushProperty =
        AvaloniaProperty.Register<SpotlightMask, IBrush?>(nameof(MaskBrush));

    public static readonly StyledProperty<IBrush?> BorderBrushProperty =
        AvaloniaProperty.Register<SpotlightMask, IBrush?>(nameof(BorderBrush));

    public static readonly StyledProperty<bool> HasHighlightProperty =
        AvaloniaProperty.Register<SpotlightMask, bool>(nameof(HasHighlight), false);

    public Rect HighlightRect
    {
        get => GetValue(HighlightRectProperty);
        set => SetValue(HighlightRectProperty, value);
    }

    public double CornerRadius
    {
        get => GetValue(CornerRadiusProperty);
        set => SetValue(CornerRadiusProperty, value);
    }

    public IBrush? MaskBrush
    {
        get => GetValue(MaskBrushProperty);
        set => SetValue(MaskBrushProperty, value);
    }

    public IBrush? BorderBrush
    {
        get => GetValue(BorderBrushProperty);
        set => SetValue(BorderBrushProperty, value);
    }

    public bool HasHighlight
    {
        get => GetValue(HasHighlightProperty);
        set => SetValue(HasHighlightProperty, value);
    }

    static SpotlightMask()
    {
        AffectsRender<SpotlightMask>(
            HighlightRectProperty,
            CornerRadiusProperty,
            MaskBrushProperty,
            BorderBrushProperty,
            HasHighlightProperty);
    }

    public override void Render(DrawingContext context)
    {
        var bounds = Bounds;
        if (bounds.Width <= 0 || bounds.Height <= 0)
        {
            return;
        }

        var maskBrush = MaskBrush ?? new SolidColorBrush(Color.FromArgb(170, 0, 0, 0));
        var fullRect = new Rect(0, 0, bounds.Width, bounds.Height);

        if (!HasHighlight || HighlightRect.Width <= 0 || HighlightRect.Height <= 0)
        {
            // 全屏遮罩，无镂空
            context.FillRectangle(maskBrush, fullRect);
            return;
        }

        var fullGeom = new RectangleGeometry(fullRect);
        var holeGeom = new RectangleGeometry(HighlightRect, CornerRadius, CornerRadius);

        var combinedGeom = new CombinedGeometry(GeometryCombineMode.Exclude, fullGeom, holeGeom);
        context.DrawGeometry(maskBrush, null, combinedGeom);

        // 绘制高亮洞口的光晕与强调边框
        var borderBrush = BorderBrush ?? new SolidColorBrush(Color.Parse("#72A7FF"));
        var pen = new Pen(borderBrush, 2.0);
        context.DrawGeometry(null, pen, holeGeom);
    }
}
