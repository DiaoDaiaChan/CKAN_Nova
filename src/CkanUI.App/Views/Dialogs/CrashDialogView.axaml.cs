namespace CkanUI.App.Views.Dialogs;

/// <summary>崩溃报告对话框视图（03 篇 §8 D-08）：显示日志路径与完整堆栈，可复制诊断信息。</summary>
public sealed partial class CrashDialogView : DialogWindow
{
    /// <summary>初始化视图。</summary>
    public CrashDialogView() => InitializeComponent();
}
