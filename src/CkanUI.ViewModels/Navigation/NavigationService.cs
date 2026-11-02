using CkanUI.Services.Abstractions;

namespace CkanUI.ViewModels.Navigation;

/// <summary>页面导航服务实现（04 篇 §1 / ARC-03）。负责解析页面实例、维持生命周期调用顺序与异常收口。</summary>
public sealed class NavigationService : INavigationService
{
    private readonly Func<string, object?> _factory;
    private readonly Func<string, string>? _translate;
    private readonly Func<string, object?[], string>? _format;

    /// <summary>初始化导航服务。</summary>
    /// <param name="factory">根据页面键解析页面 VM 的工厂委托。</param>
    /// <param name="translate">
    /// 页面文案解析器（本地化键 → 译文），通常是 <c>ILocalizer.Get</c>；为 null 时页面保持中文原文。
    /// </param>
    /// <param name="format">带占位符的文案解析器，通常是 <c>ILocalizer.Format</c>；为 null 时页面用中文原文格式化。</param>
    public NavigationService(
        Func<string, object?> factory,
        Func<string, string>? translate = null,
        Func<string, object?[], string>? format = null)
    {
        _factory = factory ?? throw new ArgumentNullException(nameof(factory));
        _translate = translate;
        _format = format;
    }

    /// <inheritdoc />
    public object? CurrentPage { get; private set; }

    /// <inheritdoc />
    public string CurrentKey { get; private set; } = string.Empty;

    /// <inheritdoc />
    public event Action<object?, string>? Navigated;

    /// <summary>回退栈上限（F-25）：只保留最近 64 次跳转，避免长期使用无限增长。</summary>
    private const int MaxHistoryDepth = 64;

    private readonly List<string> _backStack = new();
    private readonly List<string> _forwardStack = new();

    /// <inheritdoc />
    public bool CanGoBack => _backStack.Count > 0;

    /// <inheritdoc />
    public bool CanGoForward => _forwardStack.Count > 0;

    /// <inheritdoc />
    public event Action? HistoryChanged;

    /// <inheritdoc />
    public Task<bool> NavigateAsync(string pageKey, object? parameter = null, CancellationToken ct = default)
        => Task.FromResult(NavigateCore(pageKey, recordHistory: true, ct));

    /// <inheritdoc />
    public Task<bool> GoBackAsync(CancellationToken ct = default)
    {
        if (_backStack.Count == 0)
        {
            return Task.FromResult(false);
        }

        var from = CurrentKey;
        var target = _backStack[^1];
        if (!NavigateCore(target, recordHistory: false, ct))
        {
            return Task.FromResult(false);
        }

        _backStack.RemoveAt(_backStack.Count - 1);
        if (!string.IsNullOrEmpty(from))
        {
            _forwardStack.Add(from);
        }

        HistoryChanged?.Invoke();
        return Task.FromResult(true);
    }

    /// <inheritdoc />
    public Task<bool> GoForwardAsync(CancellationToken ct = default)
    {
        if (_forwardStack.Count == 0)
        {
            return Task.FromResult(false);
        }

        var from = CurrentKey;
        var target = _forwardStack[^1];
        if (!NavigateCore(target, recordHistory: false, ct))
        {
            return Task.FromResult(false);
        }

        _forwardStack.RemoveAt(_forwardStack.Count - 1);
        if (!string.IsNullOrEmpty(from))
        {
            _backStack.Add(from);
        }

        HistoryChanged?.Invoke();
        return Task.FromResult(true);
    }

    /// <summary>
    /// 只解析页面视图模型、不激活也不改当前页 —— 供空闲预热在用户没在看的时候把页面视图建出来。
    /// </summary>
    public object? Peek(string pageKey) => _factory(pageKey);

    private bool NavigateCore(string pageKey, bool recordHistory, CancellationToken ct)
    {
        if (ct.IsCancellationRequested)
        {
            return false;
        }

        try
        {
            // 如果切到相同页面键且已经存在页面，无需重复激活重建
            if (string.Equals(CurrentKey, pageKey, StringComparison.Ordinal) && CurrentPage != null)
            {
                return true;
            }

            var next = _factory(pageKey);
            if (next == null)
            {
                // 工厂未注册或未找到目标页，收口返回 false
                return false;
            }

            var previous = CurrentPage;

            // 铁律：必须先对旧 VM 调用 Deactivate()，Deactivate 必须在 Navigated 事件之前
            if (previous is IActivatable oldActivatable)
            {
                oldActivatable.Deactivate();
            }

            if (recordHistory && !string.IsNullOrEmpty(CurrentKey))
            {
                // 新的主动跳转会让「前进」历史失效（与浏览器一致）。
                _backStack.Add(CurrentKey);
                if (_backStack.Count > MaxHistoryDepth)
                {
                    _backStack.RemoveAt(0);
                }

                _forwardStack.Clear();
            }

            CurrentPage = next;
            CurrentKey = pageKey;

            // 页面文案的本地化解析器在这里挂（而不是各页面构造注入）：宿主只注册一次，
            // 之后任何页面被解析出来都能拿到同一份解析器；页面自己也能覆写成别的（例如测试）。
            if (next is PageViewModel page)
            {
                page.TextResolver ??= _translate;
                page.TextFormatter ??= _format;
            }

            // 新页面激活
            if (next is IActivatable newActivatable)
            {
                newActivatable.Activate();
            }

            // 在旧页面 Deactivate 之后发出 Navigated
            Navigated?.Invoke(next, pageKey);
            if (recordHistory)
            {
                HistoryChanged?.Invoke();
            }

            return true;
        }
        catch
        {
            // 异常收口为 false，不穿透抛给调用方
            return false;
        }
    }
}
