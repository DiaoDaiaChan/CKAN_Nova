using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using CkanUI.Services.Abstractions;
using CkanUI.Services.Abstractions.Dtos;
using CkanUI.ViewModels.Operations;

namespace CkanUI.ViewModels.Tests;

/// <summary>测试用操作队列：可手动推送快照并记录取消请求（05 篇 §8）。</summary>
internal sealed class FakeOperationQueue : IOperationQueue
{
    private readonly List<OperationProgress> _operations = new();

    /// <inheritdoc />
    public IReadOnlyList<OperationProgress> Operations => _operations.ToArray();

    /// <inheritdoc />
    public event Action<IReadOnlyList<OperationProgress>>? Updated;

    /// <summary>收到的取消请求（按调用顺序）。</summary>
    public List<string> Cancelled { get; } = new();

    /// <inheritdoc />
    public bool IsBusy => _operations.Any(op => op.State is OperationState.Pending or OperationState.Running);

    /// <inheritdoc />
    public string Enqueue(string kind, string title, Func<CancellationToken, Task> work)
        => throw new NotSupportedException("测试用队列不执行任务");

    /// <inheritdoc />
    public void Cancel(string taskId) => Cancelled.Add(taskId);

    /// <summary>替换快照并触发 Updated（模拟队列的 100ms 推送）。</summary>
    public void Publish(params OperationProgress[] operations)
    {
        _operations.Clear();
        _operations.AddRange(operations);
        Updated?.Invoke(_operations.ToArray());
    }

    /// <summary>替换快照但不触发事件（用于断言构造时确实读了 Operations）。</summary>
    public void Seed(params OperationProgress[] operations)
    {
        _operations.Clear();
        _operations.AddRange(operations);
    }

    /// <summary>清空全部订阅者（测试收尾用，避免事件串到下一个用例）。</summary>
    public void Reset()
    {
        _operations.Clear();
        Cancelled.Clear();
        Updated = null;
    }
}

/// <summary>测试用单调时钟（05 篇 §8：速率推算必须可复现）。</summary>
internal sealed class FakeOperationClock : IOperationClock
{
    /// <inheritdoc />
    public long TimestampMilliseconds { get; set; }

    /// <summary>推进时钟。</summary>
    public void Advance(long milliseconds) => TimestampMilliseconds += milliseconds;
}
