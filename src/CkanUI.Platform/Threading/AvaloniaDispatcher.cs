using System;
using System.Threading.Tasks;

using Avalonia.Threading;

using IDispatcher = CkanUI.Services.Abstractions.IDispatcher;

namespace CkanUI.Platform.Threading;

/// <summary>
/// <see cref="IDispatcher"/> 的 Avalonia 实现（05 篇 §2 封送矩阵）。
/// 全应用只允许 CkanUI.Platform / CkanUI.App / CkanUI.Controls 直接使用 Avalonia.Threading，
/// 业务代码一律通过 <see cref="IDispatcher"/> 依赖倒置，这样 ViewModel 与服务层都不需要引用 Avalonia。
/// </summary>
/// <remarks>
/// 该类型只做转发、不含逻辑，因此没有窗口级测试；封送语义由 tests/CkanUI.TestSupport 的
/// FakeDispatcher 与调用方测试覆盖（本仓库未引入 Avalonia.Headless，无法驱动真实消息循环）。
/// </remarks>
public sealed class AvaloniaDispatcher : IDispatcher
{
    private readonly Dispatcher _dispatcher;

    /// <summary>以指定 dispatcher 构造（测试或非 UI 线程场景显式传入）。</summary>
    public AvaloniaDispatcher(Dispatcher dispatcher)
    {
        ArgumentNullException.ThrowIfNull(dispatcher);
        _dispatcher = dispatcher;
    }

    /// <summary>使用进程的 UI 线程 dispatcher 构造。</summary>
    public AvaloniaDispatcher()
        : this(Dispatcher.UIThread)
    {
    }

    /// <inheritdoc />
    public bool IsOnUiThread => _dispatcher.CheckAccess();

    /// <inheritdoc />
    /// <remarks>
    /// 一律走 <c>Dispatcher.Post</c> 排队，即使调用方已在 UI 线程也不就地执行：
    /// 就地执行会让「先更新状态、后订阅事件」的代码在中途被重入（05 篇 §3 死锁铁律的推论）。
    /// </remarks>
    public void Post(Action action)
    {
        ArgumentNullException.ThrowIfNull(action);
        _dispatcher.Post(action, DispatcherPriority.Normal);
    }

    /// <inheritdoc />
    public Task InvokeAsync(Action action)
    {
        ArgumentNullException.ThrowIfNull(action);
        return _dispatcher.InvokeAsync(action).GetTask();
    }

    /// <inheritdoc />
    public Task<T> InvokeAsync<T>(Func<T> func)
    {
        ArgumentNullException.ThrowIfNull(func);
        return _dispatcher.InvokeAsync(func).GetTask();
    }
}
