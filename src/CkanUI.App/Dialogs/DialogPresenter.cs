using Avalonia.Controls;
using CkanUI.App.Views.Dialogs;
using CkanUI.Platform.Dialogs;
using CkanUI.Services.Abstractions;
using CkanUI.ViewModels.Dialogs;

namespace CkanUI.App.Dialogs;

/// <summary>
/// <see cref="IDialogPresenter"/> 的 App 实现（03 篇 §8）：用 <c>src/CkanUI.App/Views/Dialogs</c> 里的真实视图弹窗。
/// </summary>
/// <remarks>
/// 只在 UI 线程被调用（<c>DialogService</c> 已封送）；owner 一律由构造时注入的委托在 UI 线程上取，
/// 后台线程弹框时也不会跨线程摸窗口。纯提示消息（非错误）故意不接管 —— 内置代码建窗版本更轻，
/// 也不该把"提示"渲染成带堆栈与复制诊断的错误框。
/// </remarks>
public sealed class DialogPresenter : IDialogPresenter
{
    private readonly Func<Window?> _ownerProvider;
    private readonly Func<string, Task>? _copyTextAsync;

    /// <param name="ownerProvider">返回当前主窗口，用于 <c>ShowDialog(owner)</c>。</param>
    /// <param name="copyTextAsync">剪贴板写入委托；为 null 时错误框的复制按钮只提示无通道。</param>
    public DialogPresenter(Func<Window?> ownerProvider, Func<string, Task>? copyTextAsync = null)
    {
        ArgumentNullException.ThrowIfNull(ownerProvider);
        _ownerProvider = ownerProvider;
        _copyTextAsync = copyTextAsync;
    }

    /// <inheritdoc />
    public async Task<bool?> ConfirmAsync(string title, string message, string? detail)
    {
        var viewModel = new ConfirmDialogViewModel(title, message, detail);
        await ShowAsync(new ConfirmDialogView { DataContext = viewModel }, viewModel).ConfigureAwait(true);
        return viewModel.Result;
    }

    /// <inheritdoc />
    public async Task<int?> SelectAsync(string title, string message, IReadOnlyList<string> options, int defaultIndex)
    {
        var viewModel = new SelectDialogViewModel(title, message, options, defaultIndex);
        await ShowAsync(new SelectDialogView { DataContext = viewModel }, viewModel).ConfigureAwait(true);
        return viewModel.Result;
    }

    /// <inheritdoc />
    public async Task<bool?> ShowNoticeAsync(bool isError, string title, string message, string? detail)
    {
        if (!isError)
        {
            // 纯提示交给 DialogService 的内置实现，避免错误框语义错位。
            return null;
        }

        var entry = new ErrorEntry(DateTime.Now, title, message, detail, null);
        var viewModel = new ErrorDialogViewModel(entry, _copyTextAsync);
        await ShowAsync(new ErrorDialogView { DataContext = viewModel }, viewModel).ConfigureAwait(true);
        return true;
    }

    /// <summary>有 owner 时严格模态，否则降级非模态并等窗口关闭（与 DialogService 的降级口径一致）。</summary>
    private async Task ShowAsync(DialogWindow window, DialogViewModelBase viewModel)
    {
        var owner = _ownerProvider();
        if (owner is not null && owner.IsVisible)
        {
            await window.ShowDialog(owner).ConfigureAwait(true);
            return;
        }

        var closed = new TaskCompletionSource();
        window.Closed += (_, _) => closed.TrySetResult();
        window.Show();
        await closed.Task.ConfigureAwait(true);
    }
}
