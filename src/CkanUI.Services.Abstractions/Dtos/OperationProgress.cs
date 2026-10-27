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
    OperationState State);
