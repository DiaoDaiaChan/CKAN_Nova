namespace CkanUI.Services.Lifecycle;

/// <summary>
/// 单次内存回收的结果（18 篇 §3 L3/L4）。<see cref="Supported"/> 为 false 表示当前平台没接入这项回收，
/// 编排层会把它记成「未执行」而不是「失败」——未接入不等于出错。
/// </summary>
/// <param name="Supported">当前平台是否支持这项回收。</param>
/// <param name="ReleasedItemCount">回收的次数/批次（用于报告里计数，不代表字节数）。</param>
/// <param name="ReleasedBytes">观测到的回收字节数；测不出来时为 0。</param>
/// <param name="Detail">不支持的原因或观测说明。</param>
public sealed record MemoryTrimResult(
    bool Supported,
    int ReleasedItemCount,
    long ReleasedBytes,
    string? Detail)
{
    /// <summary>平台未接入。</summary>
    public static MemoryTrimResult Unsupported(string detail) => new(false, 0, 0, detail);
}
