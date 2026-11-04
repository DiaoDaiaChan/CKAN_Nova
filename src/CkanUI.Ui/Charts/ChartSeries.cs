using System.Collections.Generic;
using Avalonia.Media;

namespace CkanUI.Ui.Charts;

/// <summary>
/// 曲线图的一条序列。
/// </summary>
/// <param name="Name">图例名。</param>
/// <param name="Values">按 X 轴等距排列的数值；<c>null</c> 表示该点缺失（断线，不补 0）。</param>
/// <param name="Stroke">线色；为空时用图表内置调色板。</param>
/// <param name="Fill">面积填充色；为空时只画线。</param>
/// <param name="Dashed">是否虚线（用于阈值/对照线）。</param>
public sealed record ChartSeries(
    string Name,
    IReadOnlyList<double?> Values,
    IBrush? Stroke = null,
    IBrush? Fill = null,
    bool Dashed = false)
{
    /// <summary>点的个数（空序列为 0）。</summary>
    public int Count => Values?.Count ?? 0;

    /// <summary>是否至少有一个可绘制点。</summary>
    public bool HasData
    {
        get
        {
            if (Values is null)
            {
                return false;
            }

            foreach (var value in Values)
            {
                if (value is { } v && !double.IsNaN(v) && !double.IsInfinity(v))
                {
                    return true;
                }
            }

            return false;
        }
    }
}
