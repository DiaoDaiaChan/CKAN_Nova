namespace CkanUI.ViewModels.Navigation;

/// <summary>页面导航契约（04 篇 §1 / ARC-03）。负责页面解析与生命周期协调。</summary>
public interface INavigationService
{
    /// <summary>当前页面视图模型。</summary>
    object? CurrentPage { get; }

    /// <summary>当前页面键值。</summary>
    string CurrentKey { get; }

    /// <summary>页面完成导航时触发，传递 (新页面对象, 目标键值)。在旧页面 Deactivate 之后触发。</summary>
    event Action<object?, string>? Navigated;

    /// <summary>异步导航到指定页面。</summary>
    /// <param name="pageKey">目标页面唯一键值。</param>
    /// <param name="parameter">可选导航参数。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>导航是否成功。</returns>
    Task<bool> NavigateAsync(string pageKey, object? parameter = null, CancellationToken ct = default);
}
