using CkanUI.Services.Abstractions.Dtos;

namespace CkanUI.ViewModels.Pages.Stats;

/// <summary>饼图图例的一项（不可变）。</summary>
public sealed record DownloadStatsSliceViewModel(string Label, long Count, long Bytes, double Percent, bool Unavailable)
{
    /// <summary>从 DTO 投影。</summary>
    public static DownloadStatsSliceViewModel From(DownloadStatsSlice slice) =>
        new(slice.Label, slice.Count, slice.Bytes, slice.Percent, slice.Unavailable);

    /// <summary>图例主文本：<c>名称 12.3%</c>。</summary>
    public string DisplayText => DownloadStatsFormat.Percent(Percent) + "  " + Label;

    /// <summary>图例副文本：下载量合计与缓存占用。</summary>
    public string DetailText => Unavailable
        ? DownloadStatsFormat.Count(Count) + " 下载（含不可用）"
        : DownloadStatsFormat.Count(Count) + " 下载 · " + DownloadStatsFormat.Bytes(Bytes);
}
