using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.VisualTree;
using CkanUI.ViewModels.Dialogs;
using CkanUI.ViewModels.Dialogs.Mods;

namespace CkanUI.App.Views.Dialogs;

/// <summary>虚拟模组提供者选择视图（F-35）：双击某行等价于选中并确定。</summary>
public sealed partial class VirtualModuleDialogView : DialogWindow
{
    /// <summary>初始化视图。</summary>
    public VirtualModuleDialogView() => InitializeComponent();

    /// <summary>双击候选项直接选定（走 VM 的 Activate 命令，语义与确定按钮一致）。</summary>
    private void OnProvidersDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (DataContext is not VirtualModuleDialogViewModel viewModel)
        {
            return;
        }

        if (e.Source is not Control source)
        {
            return;
        }

        var row = source.FindAncestorOfType<ListBoxItem>(true);
        if (row?.DataContext is VirtualModuleCandidateViewModel candidate)
        {
            viewModel.ActivateCommand.Execute(candidate);
        }
    }
}
