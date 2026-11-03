using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace CkanUI.Plugin.Performance;

/// <summary>
/// 迷你折线（走势图）。
/// </summary>
/// <remarks>
/// <para>为什么自绘而不是引图表库：插件不许新增 NuGet 包（硬约束），而走势只需要一条折线——
/// 为此引 LiveCharts/OxyPlot 是把一个依赖塞进宿主进程，代价远大于 40 行绘制代码。</para>
/// <para>没有数据时<b>什么都不画</b>（<see cref="HasData"/> 为 false 时视图显示"不可用"文案），
/// 不画一条平线或空框来假装有数据（契约的 N/A 纪律同样适用于图形）。</para>
/// </remarks>
public sealed class PerfSparkline : Control
{
    /// <summary>数据序列；空或单点时不可画。</summary>
    public static readonly StyledProperty<IReadOnlyList<double>?> ValuesProperty =
        AvaloniaProperty.Register<PerfSparkline, IReadOnlyList<double>?>(nameof(Values));

    /// <summary>线色。</summary>
    public static readonly StyledProperty<IBrush?> StrokeProperty =
        AvaloniaProperty.Register<PerfSparkline, IBrush?>(nameof(Stroke));

    /// <summary>填充色（在线下方的渐隐块；可为空）。</summary>
    public static readonly StyledProperty<IBrush?> FillProperty =
        AvaloniaProperty.Register<PerfSparkline, IBrush?>(nameof(Fill));

    /// <summary>线宽。</summary>
    public static readonly StyledProperty<double> StrokeThicknessProperty =
        AvaloniaProperty.Register<PerfSparkline, double>(nameof(StrokeThickness), 1.6d);

    static PerfSparkline()
    {
        AffectsRender<PerfSparkline>(ValuesProperty, StrokeProperty, FillProperty, StrokeThicknessProperty);
    }

    /// <summary>数据序列。</summary>
    public IReadOnlyList<double>? Values
    {
        get => GetValue(ValuesProperty);
        set => SetValue(ValuesProperty, value);
    }

    /// <summary>线色。</summary>
    public IBrush? Stroke
    {
        get => GetValue(StrokeProperty);
        set => SetValue(StrokeProperty, value);
    }

    /// <summary>填充色。</summary>
    public IBrush? Fill
    {
        get => GetValue(FillProperty);
        set => SetValue(FillProperty, value);
    }

    /// <summary>线宽。</summary>
    public double StrokeThickness
    {
        get => GetValue(StrokeThicknessProperty);
        set => SetValue(StrokeThicknessProperty, value);
    }

    /// <summary>是否有足够的数据画一条线（视图用它决定"画图"还是"显示不可用"）。</summary>
    public bool HasData => Values is { Count: > 1 };

    /// <inheritdoc />
    public override void Render(DrawingContext context)
    {
        base.Render(context);

        var values = Values;
        var stroke = Stroke;
        if (values is null || values.Count < 2 || stroke is null)
        {
            return;
        }

        var width = Bounds.Width;
        var height = Bounds.Height;
        if (width <= 1 || height <= 1)
        {
            return;
        }

        var min = double.MaxValue;
        var max = double.MinValue;
        foreach (var value in values)
        {
            if (double.IsNaN(value) || double.IsInfinity(value))
            {
                continue;
            }

            min = Math.Min(min, value);
            max = Math.Max(max, value);
        }

        if (min > max)
        {
            return;
        }

        // 全平的数据给一个固定的可视线：否则 min==max 会把折线压在底边上。
        var span = max - min;
        if (span < 1e-9)
        {
            span = Math.Max(1e-9, Math.Abs(max) * 0.1);
            min -= span / 2;
        }

        var padding = Math.Min(2d, height / 8d);
        var usable = Math.Max(1d, height - padding * 2);
        var step = width / (values.Count - 1);

        var geometry = new StreamGeometry();
        Point? previous = null;
        var index = 0;
        using (var sink = geometry.Open())
        {
            var started = false;
            foreach (var value in values)
            {
                if (double.IsNaN(value) || double.IsInfinity(value))
                {
                    index++;
                    continue;
                }

                var x = index * step;
                var y = padding + usable * (1d - (value - min) / span);
                var point = new Point(x, y);
                if (!started)
                {
                    sink.BeginFigure(point, isFilled: false);
                    started = true;
                }
                else
                {
                    sink.LineTo(point);
                }

                previous = point;
                index++;
            }

            if (started && previous is not null)
            {
                sink.EndFigure(isClosed: false);
            }
        }

        context.DrawGeometry(null, new Pen(stroke, StrokeThickness), geometry);
    }
}
