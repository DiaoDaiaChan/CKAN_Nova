using System;
using System.Collections.Generic;
using System.Threading.Tasks;

using CkanUI.Services.Abstractions;

namespace CkanUI.TestSupport;

/// <summary>
/// 测试用 <see cref="IDispatcher"/>（05 篇 §2）。
/// 默认「就地执行」，与生产行为在「事件订阅方立刻收到回调」这一点上等价，方便断言；
/// 需要观察「封送是否延迟」时用 <c>new FakeDispatcher(queuePosts: true)</c> + <see cref="PumpPosts"/>。
/// </summary>
public sealed class FakeDispatcher : IDispatcher
{
    private readonly List<Action> _posted = new();

    /// <param name="queuePosts">true 时 Post 只入队，需手动 <see cref="PumpPosts"/> 才执行。</param>
    public FakeDispatcher(bool queuePosts = false)
    {
        QueuePosts = queuePosts;
    }

    /// <summary>Post 是否入队而不立即执行。</summary>
    public bool QueuePosts { get; }

    /// <summary>当前线程是否被视为 UI 线程；测试可改写以模拟后台线程回调。</summary>
    public bool IsOnUiThread { get; set; } = true;

    /// <summary>尚在队列中的 Post 回调（按入队顺序）。</summary>
    public IReadOnlyList<Action> Posted => _posted;

    /// <summary>InvokeAsync 被调用的次数，用于断言「同步弹框确实走了封送」。</summary>
    public int InvokeCount { get; private set; }

    /// <inheritdoc />
    public void Post(Action action)
    {
        ArgumentNullException.ThrowIfNull(action);
        if (QueuePosts)
        {
            _posted.Add(action);
            return;
        }

        action();
    }

    /// <summary>把队列中的 Post 回调依次执行并清空。</summary>
    public void PumpPosts()
    {
        var pending = _posted.ToArray();
        _posted.Clear();
        foreach (var action in pending)
        {
            action();
        }
    }

    /// <inheritdoc />
    public Task InvokeAsync(Action action)
    {
        ArgumentNullException.ThrowIfNull(action);
        InvokeCount++;
        action();
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<T> InvokeAsync<T>(Func<T> func)
    {
        ArgumentNullException.ThrowIfNull(func);
        InvokeCount++;
        return Task.FromResult(func());
    }
}
