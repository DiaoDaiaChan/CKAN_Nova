using CkanUI.Services.Abstractions;

namespace CkanUI.ViewModels.Navigation;

/// <summary>页面导航服务实现（04 篇 §1 / ARC-03）。负责解析页面实例、维持生命周期调用顺序与异常收口。</summary>
public sealed class NavigationService : INavigationService
{
    private readonly Func<string, object?> _factory;

    /// <summary>初始化导航服务。</summary>
    /// <param name="factory">根据页面键解析页面 VM 的工厂委托。</param>
    public NavigationService(Func<string, object?> factory)
    {
        _factory = factory ?? throw new ArgumentNullException(nameof(factory));
    }

    /// <inheritdoc />
    public object? CurrentPage { get; private set; }

    /// <inheritdoc />
    public string CurrentKey { get; private set; } = string.Empty;

    /// <inheritdoc />
    public event Action<object?, string>? Navigated;

    /// <inheritdoc />
    public Task<bool> NavigateAsync(string pageKey, object? parameter = null, CancellationToken ct = default)
    {
        if (ct.IsCancellationRequested)
        {
            return Task.FromResult(false);
        }

        try
        {
            // 如果切到相同页面键且已经存在页面，无需重复激活重建
            if (string.Equals(CurrentKey, pageKey, StringComparison.Ordinal) && CurrentPage != null)
            {
                return Task.FromResult(true);
            }

            var next = _factory(pageKey);
            if (next == null)
            {
                // 工厂未注册或未找到目标页，收口返回 false
                return Task.FromResult(false);
            }

            var previous = CurrentPage;

            // 铁律：必须先对旧 VM 调用 Deactivate()，Deactivate 必须在 Navigated 事件之前
            if (previous is IActivatable oldActivatable)
            {
                oldActivatable.Deactivate();
            }

            CurrentPage = next;
            CurrentKey = pageKey;

            // 新页面激活
            if (next is IActivatable newActivatable)
            {
                newActivatable.Activate();
            }

            // 在旧页面 Deactivate 之后发出 Navigated
            Navigated?.Invoke(next, pageKey);
            return Task.FromResult(true);
        }
        catch
        {
            // 异常收口为 false，不穿透抛给调用方
            return Task.FromResult(false);
        }
    }
}
