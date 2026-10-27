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
        var viewModelName = param.GetType().FullName!;
        var viewName = viewModelName.Replace(".ViewModels.Pages.", ".App.Views.", StringComparison.Ordinal)
                                    .Replace(".ViewModels.", ".App.Views.", StringComparison.Ordinal)
                                    .Replace("ViewModel", "View", StringComparison.Ordinal);

        if (Type.GetType(viewName) is { } viewType && Activator.CreateInstance(viewType) is Control view)
        {
            view.DataContext = param;
            return view;
        }

        return new Views.PagePlaceholderView { DataContext = param };
    }
}
