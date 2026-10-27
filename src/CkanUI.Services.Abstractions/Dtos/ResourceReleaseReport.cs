namespace CkanUI.Services.Abstractions.Dtos;

/// <summary>
/// 一次资源释放的结果（18 篇 §3/§10）。<see cref="Layers"/> 恒为 L0–L4 五条，按层号升序，
/// 没执行到的层以 <see cref="ResourceReleaseLayerReport.Attempted"/>=false 出现——调用方不必再自己拼空层。
/// </summary>
/// <param name="Tier">请求的档位。</param>
/// <param name="TakenAt">本次释放的开始时刻（UTC）。</param>
/// <param name="Elapsed">从第一层开始到最后一层结束的墙钟耗时。</param>
/// <param name="Layers">逐层结果（L0–L4 五条）。</param>
public sealed record ResourceReleaseReport(
    ResourceReleaseTier Tier,
    DateTimeOffset TakenAt,
    TimeSpan Elapsed,
    IReadOnlyList<ResourceReleaseLayerReport> Layers)
{
    /// <summary>是否被取消（后续层不再执行，已执行的层不回滚——回滚统一走 RestoreAsync）。</summary>
    public bool Cancelled { get; init; }

    /// <summary>执行过的层是否全部成功。没执行到的层不影响这个判断。</summary>
    public bool IsSuccess => Layers.All(layer => !layer.Attempted || layer.Succeeded);

    /// <summary>成功释放的条目总数。</summary>
    public int ReleasedItemCount =>
        Layers.Where(layer => layer.Succeeded).Sum(layer => layer.ReleasedItemCount);

    /// <summary>执行失败的层（已记 Warn，不阻断后续层）。</summary>
    public IReadOnlyList<ResourceReleaseLayerReport> FailedLayers =>
        Layers.Where(layer => layer.Attempted && !layer.Succeeded).ToArray();

    /// <summary>按层号取结果；报告恒含五层，因此不会取不到。</summary>
    public ResourceReleaseLayerReport this[ResourceReleaseLayer layer] =>
        Layers.FirstOrDefault(item => item.Layer == layer)
        ?? ResourceReleaseLayerReport.Skipped(layer, "报告缺少该层。");
}
