using System.Runtime.CompilerServices;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.VisualTree;
using CkanUI.Services.Abstractions;
using CkanUI.ViewModels;
using CkanUI.ViewModels.Pages;

namespace CkanUI.App;

/// <summary>
/// VM → View 解析（ARC-03）。按命名约定把 XxxPageViewModel 映射到 XxxPageView；
/// 尚未实现的页面统一回退到占位视图，保证导航永不因缺视图而崩（03 篇 §1）。
/// </summary>
public sealed class ViewLocator : IDataTemplate
{
    /// <summary>
    /// 按单例页面 VM 缓存已实例化的视图控件：避免每次切页都重新 new UserControl，
    /// 否则 InitializeComponent 期间 DataContext 为 null 会让页内 .view-layer 反复从 Opacity=0 闪回 Opacity=1。
    /// </summary>
    private static readonly ConditionalWeakTable<object, Control> ViewCache = new();

    public bool Match(object? data) => data is PageViewModel or IPluginPage;

    public Control? Build(object? param)
    {
        if (param is null) return null;

        if (ViewCache.TryGetValue(param, out var cached) &&
            cached.Parent is null &&
            cached.GetVisualParent() is null)
        {
            return cached;
        }

        // 顺序很重要：先换命名空间再换类型后缀。
        // 反过来的话 "CkanUI.ViewModels.Pages.ModsPageViewModel" 会变成 "...Views.Pages.ModsPageView"（Pages 段被留下），
        // 而页面视图都在 CkanUI.App.Views 下，于是所有页面静默回退到占位视图。
        if (ResolveViewType(param.GetType()) is { } viewType && Activator.CreateInstance(viewType) is Control view)
        {
            view.DataContext = param;
            ViewCache.AddOrUpdate(param, view);
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
        return Type.GetType(viewName) ?? ResolvePluginViewType(viewModelType, viewModelName);
    }

    /// <summary>
    /// 插件页的视图解析（12 篇 F-17）：视图必须与视图模型同程序集同命名空间，只差类型名后缀。
    /// </summary>
    /// <remarks>
    /// 这里不能用 <see cref="Type.GetType(string)"/>：插件程序集加载在可卸载 <c>AssemblyLoadContext</c> 里，
    /// 默认上下文按名字查不到。改成在视图模型自己的程序集里找，顺带也就支持了插件自带的视图。
    /// </remarks>
    private static Type? ResolvePluginViewType(Type viewModelType, string viewModelName)
    {
        var viewName = viewModelName.Replace("ViewModel", "View", StringComparison.Ordinal);
        if (string.Equals(viewName, viewModelName, StringComparison.Ordinal))
        {
            return null;
        }

        try
        {
            return viewModelType.Assembly.GetType(viewName, throwOnError: false);
        }
        catch (Exception ex) when (ex is TypeLoadException or FileLoadException or BadImageFormatException)
        {
            return null;
        }
    }
}
