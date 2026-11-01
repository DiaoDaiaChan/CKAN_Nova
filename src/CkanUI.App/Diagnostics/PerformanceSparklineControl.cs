using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace CkanUI.App.Diagnostics;

/// <summary>
/// 帧时间火花线自绘控件：
/// 高性能自绘最近 60~120 帧的耗时柱状走势图，标定 8ms (p50 目标)、16.7ms (60 FPS) 与 33.3ms (30 FPS) 参考线。
/// </summary>
public sealed class PerformanceSparklineControl : Control
{
    public static readonly StyledProperty<double[]> DurationsProperty =
        AvaloniaProperty.Register<PerformanceSparklineControl, double[]>(nameof(Durations), Array.Empty<double>());

    public double[] Durations
    {
        get => GetValue(DurationsProperty);
        set => SetValue(DurationsProperty, value);
    }

    private static readonly IBrush BrushGood = new SolidColorBrush(Color.FromRgb(16, 185, 129));      // 10B981
    private static readonly IBrush BrushNormal = new SolidColorBrush(Color.FromRgb(14, 165, 233));    // 0EA5E9
    private static readonly IBrush BrushWarning = new SolidColorBrush(Color.FromRgb(245, 158, 11));   // F59E0B
    private static readonly IBrush BrushCritical = new SolidColorBrush(Color.FromRgb(239, 68, 68));   // EF4444
    private static readonly IBrush BrushGrid = new SolidColorBrush(Color.FromArgb(60, 140, 140, 140));

    private static readonly IPen Pen8Ms = new Pen(new SolidColorBrush(Color.FromArgb(160, 16, 185, 129)), 1, DashStyle.Dash);
    private static readonly IPen Pen16Ms = new Pen(new SolidColorBrush(Color.FromArgb(180, 14, 165, 233)), 1, DashStyle.Dash);
    private static readonly IPen Pen33Ms = new Pen(new SolidColorBrush(Color.FromArgb(180, 245, 158, 11)), 1, DashStyle.Dash);

    static PerformanceSparklineControl()
    {
        AffectsRender<PerformanceSparklineControl>(DurationsProperty);
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);

        var bounds = Bounds;
        var width = bounds.Width;
        var height = bounds.Height;
        if (width <= 0 || height <= 0) return;

        // 背景微弱网格底衬
        context.FillRectangle(new SolidColorBrush(Color.FromArgb(25, 0, 0, 0)), new Rect(0, 0, width, height));

        const double maxScaleMs = 40.0; // 图表纵轴上限标为 40ms

        // 绘制标尺参考线
        DrawReferenceLine(context, 8.0, maxScaleMs, height, width, Pen8Ms);
        DrawReferenceLine(context, 16.7, maxScaleMs, height, width, Pen16Ms);
        DrawReferenceLine(context, 33.3, maxScaleMs, height, width, Pen33Ms);

        var samples = Durations;
        if (samples is null || samples.Length == 0) return;

        var barSpacing = 1.0;
        var barWidth = Math.Max(1.0, (width - (samples.Length * barSpacing)) / samples.Length);
        var totalStep = barWidth + barSpacing;

        for (var i = 0; i < samples.Length; i++)
        {
            var ms = samples[i];
            var clamped = Math.Min(maxScaleMs, Math.Max(0.5, ms));
            var barHeight = Math.Max(1.0, (clamped / maxScaleMs) * height);
            var x = i * totalStep;
            var y = height - barHeight;

            IBrush brush;
            if (ms <= 8.0)
            {
                brush = BrushGood;
            }
            else if (ms <= 16.7)
            {
                brush = BrushNormal;
            }
            else if (ms <= 33.3)
            {
                brush = BrushWarning;
            }
            else
            {
                brush = BrushCritical;
            }

            context.FillRectangle(brush, new Rect(x, y, barWidth, barHeight));
        }
    }

    private static void DrawReferenceLine(DrawingContext context, double targetMs, double maxScaleMs, double height, double width, IPen pen)
    {
        var y = height - (targetMs / maxScaleMs) * height;
        if (y >= 0 && y <= height)
        {
            context.DrawLine(pen, new Point(0, y), new Point(width, y));
        }
    }
}
