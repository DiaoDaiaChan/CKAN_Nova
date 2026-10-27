namespace CkanUI.Services.Abstractions.Dtos;

/// <summary>
/// 单层的执行结果（18 篇 §3「每层独立成败」）：失败只记 Warn 并继续下一层，
/// 因此报告里同时保留 <see cref="Attempted"/> 与 <see cref="Succeeded"/>，让调用方能区分
/// 「没轮到执行」与「执行了但失败」。
/// </summary>
/// <param name="Layer">层号。</param>
/// <param name="Attempted">是否真的执行过（档位未包含、已在此次启动中执行过、被取消都会是 false）。</param>
/// <param name="Succeeded">是否成功；<see cref="Attempted"/> 为 false 时恒为 false。</param>
/// <param name="ReleasedItemCount">本层释放的条目数（视图/缓存条目、注册表模组数、仓库行数、回收轮数等）。</param>
/// <param name="Elapsed">本层耗时。</param>
/// <param name="Detail">失败原因或跳过原因；成功且无需说明时为 null。</param>
public sealed record ResourceReleaseLayerReport(
    ResourceReleaseLayer Layer,
    bool Attempted,
    bool Succeeded,
    int ReleasedItemCount,
    TimeSpan Elapsed,
    string? Detail)
{
    /// <summary>未执行的一层（档位未包含 / 已执行过 / 被取消）。</summary>
    public static ResourceReleaseLayerReport Skipped(ResourceReleaseLayer layer, string detail) =>
        new(layer, Attempted: false, Succeeded: false, ReleasedItemCount: 0, TimeSpan.Zero, detail);
}
