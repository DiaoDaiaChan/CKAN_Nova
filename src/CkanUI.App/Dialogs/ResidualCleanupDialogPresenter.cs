using Avalonia.Controls;
using CkanUI.App.Views.Dialogs;
using CkanUI.Services.Abstractions;
using CkanUI.Services.Abstractions.Dtos;
using CkanUI.ViewModels.Dialogs;

namespace CkanUI.App.Dialogs;

/// <summary>
/// <see cref="IResidualCleanupDialogService"/> 的 App 实现（12 篇 F-43 / 06 篇 UI-22）：
/// 用 <c>src/CkanUI.App/Views/Dialogs/ResidualCleanupDialogView.axaml</c> 弹出残留目录清理框。
/// </summary>
/// <remarks>
/// owner 口径与 <see cref="DialogPresenter"/> 一致（有 owner 走严格模态，没有就降级非模态并等窗口关闭）。
/// 之所以单独成类：本对话框只在卸载收尾时出现一次，和错误框/确认框那条主干不是同一关注点，
/// 也免得为了它去改 <c>DialogPresenter</c> 这个已被多处依赖的文件。
/// </remarks>
public sealed class ResidualCleanupDialogPresenter : IResidualCleanupDialogService
{
    private readonly Func<Window?> _ownerProvider;
    private readonly IResidualCleanupService _cleanup;
    private readonly IErrorReporter? _errors;

    /// <param name="ownerProvider">返回当前主窗口，用于 <c>ShowDialog(owner)</c>。</param>
    /// <param name="cleanup">残留目录清理服务（单例，实例由调用方带进来）。</param>
    /// <param name="errors">错误上报器；可为 <c>null</c>。</param>
    public ResidualCleanupDialogPresenter(
        Func<Window?> ownerProvider,
        IResidualCleanupService cleanup,
        IErrorReporter? errors = null)
    {
        ArgumentNullException.ThrowIfNull(ownerProvider);
        ArgumentNullException.ThrowIfNull(cleanup);
        _ownerProvider = ownerProvider;
        _cleanup = cleanup;
        _errors = errors;
    }

    /// <inheritdoc />
    public async Task<ResidualCleanupChoice> ShowAsync(
        InstanceInfo instance,
        IReadOnlyCollection<string> reportedByUninstall,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(instance);
        ArgumentNullException.ThrowIfNull(reportedByUninstall);
        ct.ThrowIfCancellationRequested();

        var viewModel = new ResidualCleanupDialogViewModel(_cleanup, instance, reportedByUninstall, _errors);
        var window = new ResidualCleanupDialogView { DataContext = viewModel };
        await ShowWindowAsync(window, viewModel).ConfigureAwait(true);
        return viewModel.Result;
    }

    /// <summary>有 owner 时严格模态，否则降级非模态并等窗口关闭（与 <see cref="DialogPresenter"/> 同口径）。</summary>
    private async Task ShowWindowAsync(DialogWindow window, DialogViewModelBase viewModel)
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
