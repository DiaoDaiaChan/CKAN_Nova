using Avalonia.Controls;
using Avalonia.Threading;
using CkanUI.App.Views.Dialogs;
using CkanUI.Services.Abstractions;
using CkanUI.Services.Abstractions.Dtos;
using CkanUI.ViewModels.Dialogs;

namespace CkanUI.App.Dialogs;

/// <summary>
/// <see cref="IModDialogService"/> 的 App 实现（F-34/F-35/F-36）：用 <c>src/CkanUI.App/Views/Dialogs</c> 里的真实视图弹窗。
/// </summary>
/// <remarks>
/// 只在 UI 线程被调用（页面 VM 的命令都在 UI 线程上跑）；owner 由构造时注入的委托在 UI 线程上取，
/// 没有可见 owner 时降级为非模态并等窗口关闭，与 <see cref="DialogPresenter"/> 的口径一致。
/// 取消令牌触发时关掉对话框，调用方拿到的就是 null。
/// </remarks>
public sealed class ModDialogService : IModDialogService
{
    private readonly Func<Window?> _ownerProvider;
    private readonly Func<string, Task>? _copyTextAsync;

    /// <param name="ownerProvider">返回当前主窗口，用于 <c>ShowDialog(owner)</c>。</param>
    /// <param name="copyTextAsync">剪贴板写入委托；为 null 时冲突详情框的复制按钮只提示无通道。</param>
    public ModDialogService(Func<Window?> ownerProvider, Func<string, Task>? copyTextAsync = null)
    {
        ArgumentNullException.ThrowIfNull(ownerProvider);
        _ownerProvider = ownerProvider;
        _copyTextAsync = copyTextAsync;
    }

    /// <inheritdoc />
    public async Task<RecommendationSelection?> SelectRecommendationsAsync(
        RecommendationDialogRequest request,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ct.ThrowIfCancellationRequested();

        var viewModel = new RecommendationDialogViewModel(request);
        using var registration = CloseOnCancellation(viewModel, ct);
        await ShowAsync(new RecommendationDialogView { DataContext = viewModel }, viewModel).ConfigureAwait(true);
        return viewModel.Result;
    }

    /// <inheritdoc />
    public async Task<VirtualModuleChoice?> SelectVirtualModuleAsync(
        VirtualModuleRequest request,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ct.ThrowIfCancellationRequested();

        var viewModel = new VirtualModuleDialogViewModel(request);
        using var registration = CloseOnCancellation(viewModel, ct);
        await ShowAsync(new VirtualModuleDialogView { DataContext = viewModel }, viewModel).ConfigureAwait(true);
        return viewModel.Result;
    }

    /// <inheritdoc />
    public async Task ShowConflictDetailsAsync(ConflictDetailsRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ct.ThrowIfCancellationRequested();

        var viewModel = new ConflictDetailsDialogViewModel(request, _copyTextAsync);
        using var registration = CloseOnCancellation(viewModel, ct);
        await ShowAsync(new ConflictDetailsDialogView { DataContext = viewModel }, viewModel).ConfigureAwait(true);
    }

    /// <summary>有 owner 时严格模态，否则降级非模态并等窗口关闭。</summary>
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

    /// <summary>取消令牌触发时关窗（封送回 UI 线程，VM 的关闭信号由视图响应）。</summary>
    private static CancellationTokenRegistration CloseOnCancellation(
        DialogViewModelBase viewModel,
        CancellationToken ct)
    {
        if (!ct.CanBeCanceled)
        {
            return default;
        }

        return ct.Register(() => Dispatcher.UIThread.Post(viewModel.Close));
    }
}
