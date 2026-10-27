using CkanUI.Services.Abstractions.Dtos;

namespace CkanUI.ViewModels.Operations;

/// <summary>
/// 多任务聚合进度（03 篇 §1「操作状态区」/ 06 篇 UI-08「多任务并行显示正确」）。
/// </summary>
/// <remarks>
/// 聚合口径：已结束的任务（成功/失败/取消）一律按 100% 计入，进行中的按各自百分比计入，再取平均。
/// 这样"3 个任务里 1 个完成、2 个各 50%"显示 67%，而不是只显示正在跑的那一个，
/// 也不会因为某个任务失败就永远停在 90%（失败同样是"结束了"）。
/// </remarks>
/// <param name="TotalCount">快照里的任务总数。</param>
/// <param name="ActiveCount">仍在等待或进行中的任务数。</param>
/// <param name="CompletedCount">成功数。</param>
/// <param name="FailedCount">失败数。</param>
/// <param name="CancelledCount">已取消数。</param>
/// <param name="OverallPercent">聚合百分比（0..100）。</param>
/// <param name="IsAllCompleted">任务数大于 0 且没有进行中的任务。</param>
/// <param name="HasFailures">是否存在失败任务。</param>
public sealed record OperationAggregate(
    int TotalCount,
    int ActiveCount,
    int CompletedCount,
    int FailedCount,
    int CancelledCount,
    double OverallPercent,
    bool IsAllCompleted,
    bool HasFailures)
{
    /// <summary>空队列的聚合结果。</summary>
    public static OperationAggregate Empty { get; } = new(0, 0, 0, 0, 0, 0, false, false);

    /// <summary>判断状态是否已结束（成功/失败/取消都算结束）。</summary>
    /// <param name="state">任务状态。</param>
    /// <returns>已结束为 true。</returns>
    public static bool IsFinishedState(OperationState state)
        => state is OperationState.Succeeded or OperationState.Failed or OperationState.Cancelled;

    /// <summary>按快照计算聚合结果（纯函数，便于单测）。</summary>
    /// <param name="snapshot">队列快照。</param>
    /// <returns>聚合结果；空快照返回 <see cref="Empty"/>。</returns>
    public static OperationAggregate Compute(IReadOnlyList<OperationProgress> snapshot)
    {
        if (snapshot is null || snapshot.Count == 0)
        {
            return Empty;
        }

        var active = 0;
        var completed = 0;
        var failed = 0;
        var cancelled = 0;
        double sum = 0;

        foreach (var progress in snapshot)
        {
            switch (progress.State)
            {
                case OperationState.Pending:
                case OperationState.Running:
                    active++;
                    sum += Math.Clamp(progress.Percent, 0, 100);
                    break;
                case OperationState.Succeeded:
                    completed++;
                    sum += 100;
                    break;
                case OperationState.Failed:
                    failed++;
                    sum += 100;
                    break;
                case OperationState.Cancelled:
                    cancelled++;
                    sum += 100;
                    break;
                default:
                    active++;
                    break;
            }
        }

        var overall = sum / snapshot.Count;
        return new OperationAggregate(
            snapshot.Count,
            active,
            completed,
            failed,
            cancelled,
            Math.Clamp(overall, 0, 100),
            active == 0,
            failed > 0);
    }
}
