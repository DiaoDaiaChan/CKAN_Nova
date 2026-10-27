using Avalonia.Controls;

namespace CkanUI.App.Views.Dialogs;

/// <summary>关于框视图（12 篇 F-55）：只渲染 VM，关闭由 <see cref="DialogWindow"/> 的基类逻辑负责。</summary>
public sealed partial class AboutDialogView : DialogWindow
{
    /// <summary>初始化视图。</summary>
    public AboutDialogView() => InitializeComponent();
}
