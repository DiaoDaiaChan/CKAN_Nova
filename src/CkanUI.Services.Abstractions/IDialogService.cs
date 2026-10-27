namespace CkanUI.Services.Abstractions;

/// <summary>VM 发起的模态交互（04 篇 §3）。实现负责在 UI 线程上创建窗口并绑定 owner。</summary>
public interface IDialogService
{
    Task<bool> ConfirmAsync(string title, string message, string? detail = null);

    /// <summary>单选；返回 0 基索引，取消返回 -1（与 Core IUser.RaiseSelectionDialog 语义一致）。</summary>
    Task<int> SelectAsync(string title, string message, IReadOnlyList<string> options, int defaultIndex = 0);

    Task ShowMessageAsync(string title, string message);

    Task ShowErrorAsync(string title, string message, string? detail = null);

    /// <summary>带进度与取消的长任务对话框；work 在后台执行，进度已在 UI 线程回调。</summary>
    Task<OperationResult<bool>> RunWithProgressAsync(
        string title,
        Func<IProgress<double>, CancellationToken, Task> work);
}
