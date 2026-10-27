using Avalonia.Controls;
using Avalonia.Interactivity;

namespace CkanUI.App.Views.Dialogs;

/// <summary>单行文本输入对话框视图：打开即聚焦并全选，方便直接改写已有命令。</summary>
public sealed partial class TextInputDialogView : DialogWindow
{
    /// <summary>初始化视图。</summary>
    public TextInputDialogView() => InitializeComponent();

    /// <inheritdoc />
    protected override void OnLoaded(RoutedEventArgs e)
    {
        base.OnLoaded(e);
        InputBox?.Focus();
        InputBox?.SelectAll();
    }
}
