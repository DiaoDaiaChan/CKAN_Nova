using CkanUI.Services.Abstractions.Dtos;

namespace CkanUI.ViewModels.Pages.Stats;

/// <summary>下载统计明细表的一行（不可变）。</summary>
public sealed record DownloadStatsRowViewModel(
    string Identifier,
    string Name,
    long DownloadCount,
    long CacheBytes,
    bool IsInstalled,
    double DownloadPercent = 0)
{
    /// <summary>从 DTO 投影；<paramref name="maxDownloads"/> 用来把下载量换算成 0..100 的条长。</summary>
    public static DownloadStatsRowViewModel From(DownloadStatsRow row, long maxDownloads = 0) =>
        new(
            row.Identifier,
            row.Name,
            row.DownloadCount,
            row.CacheBytes,
            row.IsInstalled,
            maxDownloads <= 0 ? 0 : Math.Round(row.DownloadCount * 100.0 / maxDownloads, 1));

    /// <summary>下载量文本。</summary>
    public string DownloadsText => DownloadStatsFormat.Count(DownloadCount);

    /// <summary>缓存占用文本。</summary>
    public string CacheText => CacheBytes <= 0 ? "—" : DownloadStatsFormat.Bytes(CacheBytes);

    /// <summary>安装状态文本。</summary>
    public string InstalledText => IsInstalled ? "已安装" : "未安装";
}
