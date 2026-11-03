using Avalonia.Controls;

namespace CkanUI.Plugin.Performance;

/// <summary>
/// 「性能」页签视图。
/// </summary>
/// <remarks>
/// <para>必须是 <c>XxxPageView</c>、与 <see cref="PerformancePageViewModel"/> 同命名空间同程序集、且有无参构造函数：
/// 宿主 ViewLocator 靠这条约定把插件页 VM 映射到视图（docs/design/plugins.md §6.1）。</para>
/// <para>视图代码里只有一件事：把"有人在看"告诉 VM。宿主每次导航新建页面实例，
/// 但插件页实例也可能被缓存复用，所以轮询跟随可视树而非 VM 构造。</para>
/// </remarks>
public partial class PerformancePageView : UserControl
{
    /// <summary>供宿主 ViewLocator 反射实例化。</summary>
    public PerformancePageView()
    {
        InitializeComponent();
        AttachedToVisualTree += OnAttachedToVisualTree;
        DetachedFromVisualTree += OnDetachedFromVisualTree;
    }

    private void OnAttachedToVisualTree(object? sender, Avalonia.VisualTreeAttachmentEventArgs e)
    {
        (DataContext as PerformancePageViewModel)?.StartPolling();
    }

    private void OnDetachedFromVisualTree(object? sender, Avalonia.VisualTreeAttachmentEventArgs e)
    {
        (DataContext as PerformancePageViewModel)?.StopPolling();
    }
}
