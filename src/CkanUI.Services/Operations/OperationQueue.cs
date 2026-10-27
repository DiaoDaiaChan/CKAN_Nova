using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using CkanUI.Services.Abstractions;
using CkanUI.Services.Abstractions.Dtos;

namespace CkanUI.Services.Operations;

/// <summary>
/// 应用级任务队列（04 篇 §3 / 05 篇 §4）：聚合所有进行中操作，每 100ms 推一次汇总快照。
/// </summary>
/// <remarks>
/// 定时器节流而不是每次状态变化就推：安装/下载会以毫秒级频率更新进度，
/// 逐条推送到 UI 线程会造成消息泵饥饿（05 篇 §4 的节流要求）。
/// 完成的任务保留 <see cref="RetentionMilliseconds"/> 后清掉，避免长会话里字典无限增长。
/// </remarks>
public sealed class OperationQueue : IOperationQueue, IDisposable
{
    private const int PublishIntervalMilliseconds = 100;
    private const int RetentionMilliseconds = 30_000;

    private readonly IDispatcher _dispatcher;
    private readonly ConcurrentDictionary<string, Item> _items = new(StringComparer.Ordinal);
    private readonly Timer _timer;
    private int _disposed;

    public OperationQueue(IDispatcher dispatcher)
    {
        ArgumentNullException.ThrowIfNull(dispatcher);
        _dispatcher = dispatcher;
        _timer = new Timer(_ => Publish(), state: null, PublishIntervalMilliseconds, PublishIntervalMilliseconds);
    }

    /// <inheritdoc />
    public event Action<IReadOnlyList<OperationProgress>>? Updated;

    /// <inheritdoc />
    public IReadOnlyList<OperationProgress> Operations => _items.Values
        .Select(item => Volatile.Read(ref item.Progress))
        .ToArray();

    /// <inheritdoc />
    public bool IsBusy => Operations.Any(progress =>
        progress.State is OperationState.Pending or OperationState.Running);

    /// <inheritdoc />
    public string Enqueue(string kind, string title, Func<CancellationToken, Task> work)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        ArgumentNullException.ThrowIfNull(work);

        var id = Guid.NewGuid().ToString("D");
        var item = new Item(new OperationProgress(id, kind, title, null, 0, true, OperationState.Pending));
        _items[id] = item;
        _ = ExecuteAsync(item, work);
        return id;
    }

    /// <inheritdoc />
    public void Cancel(string taskId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(taskId);
        if (!_items.TryGetValue(taskId, out var item))
        {
            return;
        }

        try
        {
            item.Source.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // 任务刚好完成并释放了令牌源，视为已不可取消
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        _timer.Dispose();
        foreach (var item in _items.Values)
        {
            try
            {
                item.Source.Cancel();
            }
            catch (ObjectDisposedException)
            {
                // 同上
            }
        }
    }

    private async Task ExecuteAsync(Item item, Func<CancellationToken, Task> work)
    {
        Set(item, item.Progress with { State = OperationState.Running });
        try
        {
            await work(item.Source.Token).ConfigureAwait(false);
            Set(item, item.Progress with { Percent = 100, State = OperationState.Succeeded });
        }
        catch (OperationCanceledException)
        {
            Set(item, item.Progress with { Message = "已取消", State = OperationState.Cancelled });
        }
        catch (Exception exception)
        {
            // 任务异常绝不外泄（契约 §3）：转成 Failed 状态留给上层展示
            Set(item, item.Progress with { Message = exception.Message, State = OperationState.Failed });
        }
        finally
        {
            item.Source.Dispose();
            item.FinishedAtTicks = Environment.TickCount64;
        }
    }

    private static void Set(Item item, OperationProgress progress)
        => Volatile.Write(ref item.Progress, progress);

    private void Publish()
    {
        if (Volatile.Read(ref _disposed) != 0)
        {
            return;
        }

        PruneExpired();

        var snapshot = Operations;
        try
        {
            _dispatcher.Post(() =>
            {
                try
                {
                    Updated?.Invoke(snapshot);
                }
                catch (Exception)
                {
                    // 订阅方异常不能杀死定时器线程（Timer 回调里的未处理异常会终止进程）
                }
            });
        }
        catch (ObjectDisposedException)
        {
            // dispatcher 已随应用退出释放
        }
    }

    private void PruneExpired()
    {
        var now = Environment.TickCount64;
        foreach (var pair in _items)
        {
            var finishedAt = Volatile.Read(ref pair.Value.FinishedAtTicks);
            if (finishedAt != 0 && now - finishedAt > RetentionMilliseconds)
            {
                _items.TryRemove(pair.Key, out _);
            }
        }
    }

    private sealed class Item(OperationProgress progress)
    {
        public OperationProgress Progress = progress;

        public CancellationTokenSource Source { get; } = new();

        /// <summary>终止态时间戳（TickCount64），0 表示仍在运行。</summary>
        public long FinishedAtTicks;
    }
}
