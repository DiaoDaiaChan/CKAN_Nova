using Avalonia.Controls;
using Avalonia.Controls.Templates;
using CkanUI.ViewModels;
using CkanUI.ViewModels.Pages;

namespace CkanUI.App;

/// <summary>
/// VM → View 解析（ARC-03）。按命名约定把 XxxPageViewModel 映射到 XxxPageView；
/// 尚未实现的页面统一回退到占位视图，保证导航永不因缺视图而崩（03 篇 §1）。
/// </summary>
public sealed class ViewLocator : IDataTemplate
{
    public bool Match(object? data) => data is PageViewModel;

    public Control? Build(object? param)
    {
        if (param is null) return null;

        // 顺序很重要：先换命名空间再换类型后缀。
        // 反过来的话 "CkanUI.ViewModels.Pages.ModsPageViewModel" 会变成 "...Views.Pages.ModsPageView"（Pages 段被留下），
        // 而页面视图都在 CkanUI.App.Views 下，于是所有页面静默回退到占位视图。
        if (ResolveViewType(param.GetType()) is { } viewType && Activator.CreateInstance(viewType) is Control view)
        {
            view.DataContext = param;
            return view;
        }

        return new Views.PagePlaceholderView { DataContext = param };
    }

    /// <summary>
    /// 按命名约定求视图类型（ARC-03）。抽成公开静态方法是为了让测试能直接断言映射结果，
    /// 不必真的实例化视图（实例化会加载 XAML，需要完整的 Avalonia 运行时）。
    /// </summary>
    /// <param name="viewModelType">页面视图模型类型。</param>
    /// <returns>视图类型；尚未实现时返回 <c>null</c>。</returns>
    public static Type? ResolveViewType(Type viewModelType)
    {
        ArgumentNullException.ThrowIfNull(viewModelType);

        var viewModelName = viewModelType.FullName;
        if (viewModelName is null)
        {
            return null;
        }

        var viewName = viewModelName.Replace(".ViewModels.Pages.", ".App.Views.", StringComparison.Ordinal)
                                    .Replace(".ViewModels.", ".App.Views.", StringComparison.Ordinal)
                                    .Replace("ViewModel", "View", StringComparison.Ordinal);
        return Type.GetType(viewName);
    }
}
