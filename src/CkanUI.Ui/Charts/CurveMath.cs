using System;
using System.Collections.Generic;
using Avalonia;

namespace CkanUI.Ui.Charts;

/// <summary>曲线的值域（<see cref="HasData"/> 为 false 时表示没有可画的点）。</summary>
/// <param name="Min">下界。</param>
/// <param name="Max">上界。</param>
/// <param name="HasData">是否有任何有限数值。</param>
public readonly record struct CurveRange(double Min, double Max, bool HasData)
{
    /// <summary>值域跨度（至少为正数）。</summary>
    public double Span => Max > Min ? Max - Min : 1d;
}

/// <summary>
/// 曲线图的纯函数部分：值域归并与坐标投影。
/// </summary>
/// <remarks>
/// 抽出来的理由：绘制代码需要窗口与渲染上下文，单测里跑不动；而「min/max 怎么算」「第 i 个点落在哪」
/// 恰恰是最容易算错的部分。这里只依赖 <see cref="Point"/>，可以直接断言。
/// </remarks>
public static class CurveMath
{
    /// <summary>
    /// 归并所有序列的值域。
    /// </summary>
    /// <param name="series">序列集合（可为 null / 空）。</param>
    /// <param name="includeZero">是否把 0 纳入值域（柱/面积图常用）。</param>
    /// <param name="min">强制下界（为空则用数据下界）。</param>
    /// <param name="max">强制上界（为空则用数据上界）。</param>
    public static CurveRange ResolveRange(
        IEnumerable<ChartSeries>? series,
        bool includeZero = true,
        double? min = null,
        double? max = null)
    {
        var low = double.PositiveInfinity;
        var high = double.NegativeInfinity;
        var hasData = false;

        if (series is not null)
        {
            foreach (var item in series)
            {
                if (item?.Values is null)
                {
                    continue;
                }

                foreach (var value in item.Values)
                {
                    if (value is not { } number || double.IsNaN(number) || double.IsInfinity(number))
                    {
                        continue;
                    }

                    low = Math.Min(low, number);
                    high = Math.Max(high, number);
                    hasData = true;
                }
            }
        }

        if (!hasData)
        {
            return new CurveRange(0d, 1d, false);
        }

        if (includeZero)
        {
            low = Math.Min(low, 0d);
            high = Math.Max(high, 0d);
        }

        if (min is { } floor)
        {
            low = floor;
        }

        if (max is { } ceiling)
        {
            high = ceiling;
        }

        // 全平的数据：给一个可视线，否则所有点会贴在底边（与 PerfSparkline 的规则一致）。
        if (high - low < 1e-9)
        {
            var pad = Math.Abs(high) > 1e-9 ? Math.Abs(high) * 0.05 : 1d;
            low -= pad;
            high += pad;
        }

        return new CurveRange(low, high, true);
    }

    /// <summary>
    /// 把序列投影到绘图区；无法绘制的点（null / NaN / ∞）返回 <c>null</c>。
    /// </summary>
    /// <param name="values">数值序列。</param>
    /// <param name="plot">绘图区（不含轴标签区）。</param>
    /// <param name="range">值域。</param>
    public static IReadOnlyList<Point?> Project(IReadOnlyList<double?>? values, Rect plot, CurveRange range)
    {
        if (values is null || values.Count == 0)
        {
            return Array.Empty<Point?>();
        }

        var points = new Point?[values.Count];
        var span = range.Span;
        var step = values.Count > 1 ? plot.Width / (values.Count - 1) : 0d;

        for (var i = 0; i < values.Count; i++)
        {
            if (values[i] is not { } value || double.IsNaN(value) || double.IsInfinity(value))
            {
                continue;
            }

            var ratio = (value - range.Min) / span;
            var y = plot.Bottom - ratio * plot.Height;
            y = Math.Clamp(y, plot.Top, plot.Bottom);
            points[i] = new Point(plot.X + (i * step), y);
        }

        return points;
    }
}
