using Avalonia.Controls;

namespace CkanUI.Plugin.Soundtrack.UI;

/// <summary>
/// 「音乐」页签视图。
/// </summary>
/// <remarks>
/// <para>必须是 XxxPageView、与 <see cref="SoundtrackPageViewModel"/> 同命名空间同程序集、且有无参构造函数：
/// 宿主 ViewLocator 靠这条约定把插件页 VM 映射到视图（docs/design/plugins.md §6.1）。</para>
/// <para>视图代码只做一件事：把“有人在看”告诉 VM。页面实例可能被宿主缓存复用，
/// 所以载入跟随可视树而不是 VM 构造；释放归宿主，视图不 Dispose 页面。</para>
/// </remarks>
public partial class SoundtrackPageView : UserControl
{
    /// <summary>供宿主 ViewLocator 反射实例化。</summary>
    public SoundtrackPageView()
    {
        InitializeComponent();
        AttachedToVisualTree += OnAttachedToVisualTree;
        DetachedFromVisualTree += OnDetachedFromVisualTree;
    }

    private void OnAttachedToVisualTree(object? sender, Avalonia.VisualTreeAttachmentEventArgs e)
    {
        (DataContext as SoundtrackPageViewModel)?.Start();
    }

    private void OnDetachedFromVisualTree(object? sender, Avalonia.VisualTreeAttachmentEventArgs e)
    {
        (DataContext as SoundtrackPageViewModel)?.Stop();
    }
}
