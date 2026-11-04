using System.Globalization;
using Avalonia.Media;
using CkanUI.Plugin.Performance.Telemetry;
using CkanUI.Ui.Charts;

namespace CkanUI.Plugin.Performance;

/// <summary>帧时间曲线的构建结果（曲线 + 是否可画 + 说明文案）。</summary>
/// <param name="CostSeries">帧耗时序列（单条；没有点时为**空数组**，不是一条空序列）。</param>
/// <param name="FpsSeries">帧率序列（同上）。</param>
/// <param name="HasData">两条里至少一条有可画的点。</param>
/// <param name="Summary">给用户看的说明：为什么画 / 为什么不画、横轴是什么。</param>
public sealed record FrameSeriesResult(
    IReadOnlyList<ChartSeries> CostSeries,
    IReadOnlyList<ChartSeries> FpsSeries,
    bool HasData,
    string Summary);

/// <summary>
/// 把 <see cref="KppTelemetrySnapshot.Timeline"/> 的帧序列翻成可画的曲线。
/// </summary>
/// <remarks>
/// <para>纯函数：不碰 UI、不读盘、不改状态，随便什么时候调都安全——这样这一层能脱离视图单测。</para>
/// <para>不补 0、不外推：缺席的帧就是 <c>null</c>（断线），全空就返回空数组并说明原因。</para>
/// <para>帧耗时与帧率量纲不同，**分成两张图**，不共用一根 Y 轴（否则帧率会把耗时压成一条贴底的线）。</para>
/// </remarks>
public static class FrameSeriesBuilder
{
    /// <summary>帧耗时曲线的面积填充色（半透明强调色）。</summary>
    public static readonly IBrush CostFill = new SolidColorBrush(Color.Parse("#334F8CFF"));

    /// <summary>无窗口长度时的占位文案，与视图模型一致。</summary>
    private const string NotAvailable = "N/A";

    /// <summary>构建。</summary>
    /// <param name="snapshot">遥测快照；<c>Timeline</c> 为 <c>null</c> 时返回空结果。</param>
    public static FrameSeriesResult Build(KppTelemetrySnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        var timeline = snapshot.Timeline;
        var frames = timeline?.Frames;
        if (frames is null || frames.Count == 0)
        {
            return new FrameSeriesResult(
                Array.Empty<ChartSeries>(),
                Array.Empty<ChartSeries>(),
                false,
                "时间线不可用（latest.json 无 timeline.frames），不画曲线");
        }

        var cost = new double?[frames.Count];
        var fps = new double?[frames.Count];
        for (var i = 0; i < frames.Count; i++)
        {
            cost[i] = frames[i].FrameMs;
            fps[i] = frames[i].Fps;
        }

        var costSeries = new ChartSeries("帧耗时", cost, Fill: CostFill);
        var fpsSeries = new ChartSeries("帧率", fps);
        var hasCost = costSeries.HasData;
        var hasFps = fpsSeries.HasData;
        var hasData = hasCost || hasFps;

        string summary;
        if (hasData)
        {
            var window = timeline?.Window is { } seconds && double.IsFinite(seconds)
                ? seconds.ToString("0.0", CultureInfo.InvariantCulture)
                : NotAvailable;
            summary = string.Create(
                CultureInfo.InvariantCulture,
                $"{frames.Count} 帧 · 窗口 {window}s · 横轴为帧序号（最新在右）");
        }
        else
        {
            summary = $"时间线有 {frames.Count} 帧，但 frameMs/fps 全为空，不画曲线";
        }

        return new FrameSeriesResult(
            hasCost ? new[] { costSeries } : Array.Empty<ChartSeries>(),
            hasFps ? new[] { fpsSeries } : Array.Empty<ChartSeries>(),
            hasData,
            summary);
    }
}
