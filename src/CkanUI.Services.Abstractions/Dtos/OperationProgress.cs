namespace CkanUI.Services.Abstractions.Dtos;

public enum OperationState
{
    Pending,
    Running,
    Succeeded,
    Failed,
    Cancelled,
}

/// <summary>应用级任务进度（04 篇 §6 / 05 篇 §4）。节流后每 100ms 最多推一次。</summary>
public sealed record OperationProgress(
    string TaskId,
    string Kind,
    string Title,
    string? Message,
    double Percent,
    bool CanCancel,
    OperationState State)
{
    /// <summary>
    /// 阶段短标签（例如「下载 3/12」「安装 2/5」），供状态栏的阶段徽标直接显示。
    /// <para>以 <c>init</c> 属性追加而不是新增构造参数：既有构造点（含测试替身）一个都不用改。</para>
    /// </summary>
    public string? StageLabel { get; init; }

    /// <summary>阶段内进度（0–100）；没有更细的阶段数据时为 <c>null</c>。</summary>
    public double? StagePercent { get; init; }
}
