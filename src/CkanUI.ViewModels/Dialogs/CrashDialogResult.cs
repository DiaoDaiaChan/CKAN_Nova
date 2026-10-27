namespace CkanUI.ViewModels.Dialogs;

/// <summary>崩溃对话框的关闭语义（03 篇 §8 D-08 / ARC-04 崩溃兜底）。</summary>
public enum CrashDialogResult
{
    /// <summary>返回程序（用户想先复制信息或看日志）。</summary>
    ReturnToApp = 0,

    /// <summary>退出程序。</summary>
    Exit = 1,
}
