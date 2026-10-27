using Avalonia.Input;
using CkanUI.ViewModels.Dialogs;

namespace CkanUI.App.Views.Dialogs;

/// <summary>单选对话框视图（03 篇 §8 D-02）：双击条目等价于选中并确定。</summary>
public sealed partial class SelectDialogView : DialogWindow
{
    /// <summary>初始化视图。</summary>
    public SelectDialogView() => InitializeComponent();

    /// <summary>双击候选项直接确认（走 VM 的 Activate 命令，返回值语义与确定按钮一致）。</summary>
    private void OnOptionsDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (DataContext is SelectDialogViewModel viewModel && viewModel.SelectedText is { } text)
        {
            viewModel.ActivateCommand.Execute(text);
        }
    }
}
