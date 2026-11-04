using System;
using System.Collections.Generic;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;

namespace CkanUI.Ui.Charts;

/// <summary>
/// 多序列曲线图（网格 + 折线/面积 + 图例 + 悬停十字线）。
/// </summary>
/// <remarks>
/// <para>纯代码控件：不依赖主题资源，插件只要引 CkanUI.Ui 就能用。数据为空时只画一块留白，
/// 由页面负责给出「没有数据」的说明——控件不猜原因。</para>
/// <para>缺失点（null）会断开折线，而不是补 0：把「没采到」画成「零」是最容易误导人的做法。</para>
/// </remarks>
public sealed class CurveChart : Control
{
    /// <summary>序列集合。</summary>
    public static readonly StyledProperty<IReadOnlyList<ChartSeries>?> SeriesProperty =
        AvaloniaProperty.Register<CurveChart, IReadOnlyList<ChartSeries>?>(nameof(Series));

    /// <summary>前景色（网格、轴标签、图例）。</summary>
    public static readonly StyledProperty<IBrush?> ForegroundProperty =
        AvaloniaProperty.Register<CurveChart, IBrush?>(nameof(Foreground));

    /// <summary>是否绘制水平网格。</summary>
    public static readonly StyledProperty<bool> ShowGridProperty =
        AvaloniaProperty.Register<CurveChart, bool>(nameof(ShowGrid), true);

    /// <summary>是否绘制图例。</summary>
    public static readonly StyledProperty<bool> ShowLegendProperty =
        AvaloniaProperty.Register<CurveChart, bool>(nameof(ShowLegend), true);

    /// <summary>是否绘制悬停十字线。</summary>
    public static readonly StyledProperty<bool> ShowHoverProperty =
        AvaloniaProperty.Register<CurveChart, bool>(nameof(ShowHover), true);

    /// <summary>值域是否包含 0。</summary>
    public static readonly StyledProperty<bool> IncludeZeroProperty =
        AvaloniaProperty.Register<CurveChart, bool>(nameof(IncludeZero), true);

    /// <summary>强制下界（为空用数据下界）。</summary>
    public static readonly StyledProperty<double?> MinYProperty =
        AvaloniaProperty.Register<CurveChart, double?>(nameof(MinY));

    /// <summary>强制上界（为空用数据上界）。</summary>
    public static readonly StyledProperty<double?> MaxYProperty =
        AvaloniaProperty.Register<CurveChart, double?>(nameof(MaxY));

    /// <summary>Y 轴数字格式。</summary>
    public static readonly StyledProperty<string?> ValueFormatProperty =
        AvaloniaProperty.Register<CurveChart, string?>(nameof(ValueFormat), "0.#");

    /// <summary>Y 轴数值后缀（如 <c>ms</c> / <c>fps</c>）。</summary>
    public static readonly StyledProperty<string?> ValueSuffixProperty =
        AvaloniaProperty.Register<CurveChart, string?>(nameof(ValueSuffix));

    /// <summary>Y 轴标签区宽度（0 表示不画 Y 轴标签）。</summary>
    public static readonly StyledProperty<double> YAxisLabelWidthProperty =
        AvaloniaProperty.Register<CurveChart, double>(nameof(YAxisLabelWidth), 40d);

    private static readonly IBrush DefaultForeground = new SolidColorBrush(Color.Parse("#8A8F98"));

    private static readonly Color[] PaletteColors =
    [
        Color.Parse("#4F8CFF"),
        Color.Parse("#F5A524"),
        Color.Parse("#34C759"),
        Color.Parse("#C77DFF"),
        Color.Parse("#FF6B6B"),
        Color.Parse("#2DD4BF"),
    ];

    private static readonly IBrush HoverChipBackground = new SolidColorBrush(Color.Parse("#E61F2430"));
    private static readonly IBrush HoverChipForeground = new SolidColorBrush(Colors.White);
    private static readonly Typeface LabelTypeface = new("Segoe UI, Microsoft YaHei, sans-serif");
    private static readonly Cursor CrossCursor = new(StandardCursorType.Cross);

    private int _hoverIndex = -1;

    static CurveChart()
    {
        AffectsRender<CurveChart>(
            SeriesProperty,
            ForegroundProperty,
            ShowGridProperty,
            ShowLegendProperty,
            ShowHoverProperty,
            IncludeZeroProperty,
            MinYProperty,
            MaxYProperty,
            ValueFormatProperty,
            ValueSuffixProperty,
            YAxisLabelWidthProperty);
    }

    /// <summary>创建曲线图。</summary>
    public CurveChart()
    {
        ClipToBounds = true;
    }

    /// <summary>序列集合。</summary>
    public IReadOnlyList<ChartSeries>? Series
    {
        get => GetValue(SeriesProperty);
        set => SetValue(SeriesProperty, value);
    }

    /// <summary>前景色。</summary>
    public IBrush? Foreground
    {
        get => GetValue(ForegroundProperty);
        set => SetValue(ForegroundProperty, value);
    }

    /// <summary>是否绘制水平网格。</summary>
    public bool ShowGrid
    {
        get => GetValue(ShowGridProperty);
        set => SetValue(ShowGridProperty, value);
    }

    /// <summary>是否绘制图例。</summary>
    public bool ShowLegend
    {
        get => GetValue(ShowLegendProperty);
        set => SetValue(ShowLegendProperty, value);
    }

    /// <summary>是否绘制悬停十字线。</summary>
    public bool ShowHover
    {
        get => GetValue(ShowHoverProperty);
        set => SetValue(ShowHoverProperty, value);
    }

    /// <summary>值域是否包含 0。</summary>
    public bool IncludeZero
    {
        get => GetValue(IncludeZeroProperty);
        set => SetValue(IncludeZeroProperty, value);
    }

    /// <summary>强制下界。</summary>
    public double? MinY
    {
        get => GetValue(MinYProperty);
        set => SetValue(MinYProperty, value);
    }

    /// <summary>强制上界。</summary>
    public double? MaxY
    {
        get => GetValue(MaxYProperty);
        set => SetValue(MaxYProperty, value);
    }

    /// <summary>Y 轴数字格式。</summary>
    public string? ValueFormat
    {
        get => GetValue(ValueFormatProperty);
        set => SetValue(ValueFormatProperty, value);
    }

    /// <summary>Y 轴数值后缀。</summary>
    public string? ValueSuffix
    {
        get => GetValue(ValueSuffixProperty);
        set => SetValue(ValueSuffixProperty, value);
    }

    /// <summary>Y 轴标签区宽度。</summary>
    public double YAxisLabelWidth
    {
        get => GetValue(YAxisLabelWidthProperty);
        set => SetValue(YAxisLabelWidthProperty, value);
    }

    /// <summary>是否至少有一条可绘制的序列。</summary>
    public bool HasData
    {
        get
        {
            if (Series is null)
            {
                return false;
            }

            foreach (var item in Series)
            {
                if (item is { HasData: true })
                {
                    return true;
                }
            }

            return false;
        }
    }

    /// <summary>当前值域（测试与外部刻度对齐用）。</summary>
    public CurveRange Range => CurveMath.ResolveRange(Series, IncludeZero, MinY, MaxY);

    /// <inheritdoc />
    protected override Size MeasureOverride(Size availableSize)
    {
        var width = double.IsInfinity(availableSize.Width) ? 360d : Math.Max(availableSize.Width, 120d);
        var height = double.IsInfinity(availableSize.Height) ? 160d : Math.Max(availableSize.Height, 80d);
        return new Size(width, height);
    }

    /// <inheritdoc />
    public override void Render(DrawingContext context)
    {
        base.Render(context);

        var foreground = Foreground ?? DefaultForeground;
        var bounds = new Rect(Bounds.Size);
        if (bounds.Width < 8 || bounds.Height < 8)
        {
            return;
        }

        var legendHeight = ShowLegend && Series is { Count: > 0 } ? 18d : 0d;
        var labelWidth = Math.Max(YAxisLabelWidth, 0d);
        var plot = new Rect(
            labelWidth,
            legendHeight + 6,
            Math.Max(bounds.Width - labelWidth - 10, 1),
            Math.Max(bounds.Height - legendHeight - 6 - 18, 1));

        if (legendHeight > 0)
        {
            DrawLegend(context, foreground, legendHeight);
        }

        var range = Range;
        var series = Series ?? Array.Empty<ChartSeries>();
        var count = MaxCount(series);

        if (!range.HasData || count == 0)
        {
            return;
        }

        if (ShowGrid)
        {
            DrawGrid(context, foreground, plot, range);
        }

        for (var i = 0; i < series.Count; i++)
        {
            DrawSeries(context, series[i], plot, range, i);
        }

        if (ShowHover && _hoverIndex >= 0 && _hoverIndex < count)
        {
            DrawHover(context, plot, range, series, count);
        }
    }

    /// <inheritdoc />
    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);

        if (!ShowHover || !HasData)
        {
            return;
        }

        var position = e.GetPosition(this);
        var index = IndexAt(position);
        if (index == _hoverIndex)
        {
            return;
        }

        _hoverIndex = index;
        Cursor = index >= 0 ? CrossCursor : Cursor.Default;
        InvalidateVisual();
    }

    /// <inheritdoc />
    protected override void OnPointerExited(PointerEventArgs e)
    {
        base.OnPointerExited(e);

        if (_hoverIndex < 0)
        {
            return;
        }

        _hoverIndex = -1;
        Cursor = Cursor.Default;
        InvalidateVisual();
    }

    /// <summary>把指针横坐标换算成序列下标；在绘图区外返回 -1。</summary>
    private int IndexAt(Point position)
    {
        var plot = PlotRect();
        var count = MaxCount(Series);
        if (count == 0 || !plot.Contains(position))
        {
            return -1;
        }

        if (count == 1)
        {
            return 0;
        }

        var ratio = (position.X - plot.X) / plot.Width;
        var index = (int)Math.Round(ratio * (count - 1), MidpointRounding.AwayFromZero);
        return Math.Clamp(index, 0, count - 1);
    }

    private Rect PlotRect()
    {
        var bounds = new Rect(Bounds.Size);
        var legendHeight = ShowLegend && Series is { Count: > 0 } ? 18d : 0d;
        var labelWidth = Math.Max(YAxisLabelWidth, 0d);
        return new Rect(
            labelWidth,
            legendHeight + 6,
            Math.Max(bounds.Width - labelWidth - 10, 1),
            Math.Max(bounds.Height - legendHeight - 6 - 18, 1));
    }

    private void DrawLegend(DrawingContext context, IBrush foreground, double legendHeight)
    {
        var x = 2d;
        var top = legendHeight / 2;
        for (var i = 0; i < Series!.Count; i++)
        {
            var item = Series[i];
            if (item is null)
            {
                continue;
            }

            var brush = item.Stroke ?? BrushFor(i);
            context.DrawLine(new Pen(brush, 2), new Point(x, top), new Point(x + 12, top));

            var text = Text(item.Name, 10.5, foreground);
            if (x + 16 + text.Width > Bounds.Width)
            {
                break;
            }

            context.DrawText(text, new Point(x + 15, top - (text.Height / 2)));
            x += 15 + text.Width + 14;
        }
    }

    private void DrawGrid(DrawingContext context, IBrush foreground, Rect plot, CurveRange range)
    {
        var pen = new Pen(Muted(foreground, 0.10), 1);
        var labelBrush = Muted(foreground, 0.55);
        const int divisions = 3;

        for (var i = 0; i <= divisions; i++)
        {
            var ratio = (double)i / divisions;
            var y = plot.Bottom - (ratio * plot.Height);
            context.DrawLine(pen, new Point(plot.X, y), new Point(plot.Right, y));

            if (plot.X <= 0)
            {
                continue;
            }

            var value = range.Min + (ratio * range.Span);
            var text = Text(FormatValue(value), 10, labelBrush);
            context.DrawText(text, new Point(Math.Max(plot.X - text.Width - 6, 0), y - (text.Height / 2)));
        }
    }

    private void DrawSeries(DrawingContext context, ChartSeries series, Rect plot, CurveRange range, int index)
    {
        if (series?.Values is null || series.Values.Count == 0)
        {
            return;
        }

        var points = CurveMath.Project(series.Values, plot, range);
        var brush = series.Stroke ?? BrushFor(index);
        var linePen = series.Dashed ? new Pen(brush, 1.5, DashStyle.Dash) : new Pen(brush, 1.5);

        // 连续片段：缺失点把序列切成多段，每段单独成图，断口处不连线。
        foreach (var segment in Segments(points))
        {
            if (segment.Count == 1)
            {
                context.DrawEllipse(brush, null, segment[0], 1.5, 1.5);
                continue;
            }

            var line = new StreamGeometry();
            using (var lineContext = line.Open())
            {
                lineContext.BeginFigure(segment[0], false);
                for (var i = 1; i < segment.Count; i++)
                {
                    lineContext.LineTo(segment[i]);
                }

                lineContext.EndFigure(false);
            }

            if (series.Fill is not null)
            {
                var area = new StreamGeometry();
                using (var areaContext = area.Open())
                {
                    areaContext.BeginFigure(new Point(segment[0].X, plot.Bottom), true);
                    foreach (var point in segment)
                    {
                        areaContext.LineTo(point);
                    }

                    areaContext.LineTo(new Point(segment[^1].X, plot.Bottom));
                    areaContext.EndFigure(true);
                }

                context.DrawGeometry(series.Fill, null, area);
            }

            context.DrawGeometry(null, linePen, line);
        }
    }

    private static List<List<Point>> Segments(IReadOnlyList<Point?> points)
    {
        var segments = new List<List<Point>>();
        List<Point>? current = null;
        foreach (var point in points)
        {
            if (point is not { } value)
            {
                current = null;
                continue;
            }

            if (current is null)
            {
                current = [value];
                segments.Add(current);
            }
            else
            {
                current.Add(value);
            }
        }

        return segments;
    }

    private void DrawHover(DrawingContext context, Rect plot, CurveRange range, IReadOnlyList<ChartSeries> series, int count)
    {
        var index = _hoverIndex;
        var step = count > 1 ? plot.Width / (count - 1) : 0d;
        var x = plot.X + (index * step);
        var pen = new Pen(new SolidColorBrush(Colors.White, 0.35), 1, DashStyle.Dash);
        context.DrawLine(pen, new Point(x, plot.Top), new Point(x, plot.Bottom));

        var lines = new List<(string Name, string Value)>();
        for (var i = 0; i < series.Count; i++)
        {
            var item = series[i];
            if (item?.Values is null || index >= item.Values.Count || item.Values[index] is not { } value)
            {
                continue;
            }

            var brush = item.Stroke ?? BrushFor(i);
            var point = new Point(x, plot.Bottom - (((value - range.Min) / range.Span) * plot.Height));
            point = new Point(point.X, Math.Clamp(point.Y, plot.Top, plot.Bottom));
            context.DrawEllipse(brush, new Pen(new SolidColorBrush(Colors.White, 0.85), 1), point, 3, 3);
            lines.Add((item.Name, FormatValue(value) + (ValueSuffix is { Length: > 0 } suffix ? " " + suffix : string.Empty)));
        }

        if (lines.Count == 0)
        {
            return;
        }

        var texts = new List<FormattedText>(lines.Count);
        var width = 0d;
        var height = 0d;
        foreach (var (name, value) in lines)
        {
            var text = Text(name.Length > 0 ? name + " " + value : value, 10, HoverChipForeground);
            texts.Add(text);
            width = Math.Max(width, text.Width);
            height += text.Height + 2;
        }

        var chip = new Rect(x + 8, plot.Top + 4, width + 14, height + 10);
        if (chip.Right > Bounds.Width)
        {
            chip = new Rect(Math.Max(x - chip.Width - 8, 0), chip.Y, chip.Width, chip.Height);
        }

        context.DrawRectangle(HoverChipBackground, null, chip, 4, 4);
        var y = chip.Y + 5;
        foreach (var text in texts)
        {
            context.DrawText(text, new Point(chip.X + 7, y));
            y += text.Height + 2;
        }
    }

    private FormattedText Text(string? value, double size, IBrush brush)
        => new(
            value ?? string.Empty,
            CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight,
            LabelTypeface,
            size,
            brush);

    private string FormatValue(double value)
        => value.ToString(ValueFormat is { Length: > 0 } format ? format : "0.#", CultureInfo.CurrentCulture);

    private static IBrush BrushFor(int index)
        => new SolidColorBrush(PaletteColors[index % PaletteColors.Length]);

    private static IBrush Muted(IBrush source, double opacity)
    {
        if (source is ISolidColorBrush solid)
        {
            return new SolidColorBrush(Color.FromArgb(
                (byte)Math.Clamp(solid.Color.A * opacity, 0, 255),
                solid.Color.R,
                solid.Color.G,
                solid.Color.B));
        }

        return new SolidColorBrush(Colors.Gray, opacity);
    }

    private static int MaxCount(IReadOnlyList<ChartSeries>? series)
    {
        var count = 0;
        if (series is null)
        {
            return count;
        }

        foreach (var item in series)
        {
            if (item?.Values is { } values && values.Count > count)
            {
                count = values.Count;
            }
        }

        return count;
    }
}
