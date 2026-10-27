using CkanUI.Services.Abstractions.Dtos;

namespace CkanUI.Services.Abstractions;

/// <summary>
/// 下载统计（12 篇 F-47 / 06 篇 UI-25）：只做本地聚合，不联网。
/// 数据来自仓库索引快照与缓存清单；任一侧缺失时按空集合统计，不抛异常。
/// </summary>
public interface IDownloadStatsService
{
    /// <summary>最近一次聚合用的输入；从未计算过时为 <see cref="DownloadStatsInput.Empty"/>。</summary>
    DownloadStatsInput CurrentInput => DownloadStatsInput.Empty;

    /// <summary>按请求分组、排序并返回统计结果。</summary>
    Task<OperationResult<DownloadStatsReport>> ComputeAsync(
        DownloadStatsRequest request,
        CancellationToken ct = default) =>
        Task.FromResult(OperationResult<DownloadStatsReport>.Fail("下载统计服务不可用。"));
}
