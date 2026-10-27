using CkanUI.Services.Abstractions.Dtos;

namespace CkanUI.Services.Abstractions;

/// <summary>应用级任务队列（04 篇 §3 / 05 篇 §4）：聚合所有进行中操作，每 100ms 推一次汇总。</summary>
public interface IOperationQueue
{
    IReadOnlyList<OperationProgress> Operations { get; }

    /// <summary>汇总变化。已在 UI 线程触发，最多每 100ms 一次。</summary>
    event Action<IReadOnlyList<OperationProgress>>? Updated;

    /// <summary>入队并立即开始；返回任务 id。</summary>
    string Enqueue(string kind, string title, Func<CancellationToken, Task> work);

    /// <summary>请求取消（幂等）。</summary>
    void Cancel(string taskId);

    bool IsBusy { get; }
}
