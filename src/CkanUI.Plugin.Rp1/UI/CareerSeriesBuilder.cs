using System;
using System.Collections.Generic;
using Avalonia.Media;
using CkanUI.Plugin.Rp1.Career;
using CkanUI.Ui.Charts;

namespace CkanUI.Plugin.Rp1.UI;

/// <summary>生涯曲线的构建结果（序列 + 是否可画 + 说明文案）。</summary>
/// <param name="Series">曲线序列；没有可绘制数值时为**空数组**。</param>
/// <param name="HasData">是否有可绘制的点。</param>
/// <param name="Summary">给用户看的说明：画的是哪一期到哪一期、横轴是什么、为什么不画。</param>
public sealed record CareerSeriesResult(
    IReadOnlyList<ChartSeries> Series,
    bool HasData,
    string Summary);

/// <summary>
/// 把 RP-1 预算周期翻成可画的曲线。
/// </summary>
/// <remarks>
/// <para>纯函数：不碰 UI、不读盘、不改状态，可脱离视图单测。</para>
/// <para>一次只画一个量纲（资金 / 科技 / 信心 / 声望）——四个量级差两个数量级，共用一根 Y 轴等于什么都没画。</para>
/// <para>缺席的周期保持 <c>null</c>（断线），不补 0：补 0 会把「没记录」画成「钱花光了」。</para>
/// </remarks>
public static class CareerSeriesBuilder
{
    /// <summary>资金曲线的面积填充色。</summary>
    public static readonly IBrush FundsFill = new SolidColorBrush(Color.Parse("#334F8CFF"));

    /// <summary>资金指标键。</summary>
    public const string FundsKey = "funds";

    /// <summary>构建。</summary>
    /// <param name="periods">预算周期（按游戏内时间正序；乱序会被这里重新排序）。</param>
    /// <param name="metricKey">指标键（funds / science / confidence / reputation）。</param>
    /// <param name="metricLabel">指标显示名（曲线的序列名）。</param>
    public static CareerSeriesResult Build(
        IReadOnlyList<Rp1CareerPeriod>? periods,
        string? metricKey,
        string? metricLabel)
    {
        if (periods is null || periods.Count == 0)
        {
            return new CareerSeriesResult(
                Array.Empty<ChartSeries>(),
                false,
                "存档里没有预算周期，不画曲线");
        }

        var key = string.IsNullOrWhiteSpace(metricKey) ? FundsKey : metricKey.Trim();
        var label = string.IsNullOrWhiteSpace(metricLabel) ? "指标" : metricLabel;

        var values = new double?[periods.Count];
        for (var i = 0; i < periods.Count; i++)
        {
            var period = periods[i];
            values[i] = key switch
            {
                "science" => period.Science,
                "confidence" => period.Confidence,
                "reputation" => period.Reputation,
                _ => period.Funds,
            };
        }

        var series = new ChartSeries(label, values, Fill: string.Equals(key, FundsKey, StringComparison.Ordinal) ? FundsFill : null);
        if (!series.HasData)
        {
            return new CareerSeriesResult(
                Array.Empty<ChartSeries>(),
                false,
                $"{periods.Count} 期里没有「{label}」的可绘制数值，不画曲线");
        }

        return new CareerSeriesResult(
            new[] { series },
            true,
            $"{periods.Count} 期 · {periods[0].DateText} → {periods[^1].DateText} · 横轴为期序号（按游戏内时间正序）");
    }
}
