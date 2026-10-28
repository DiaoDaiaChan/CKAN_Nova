using System;

using Avalonia.Controls;
using Avalonia.Input.Platform;

using CkanUI.ViewModels.CommandPalette;

namespace CkanUI.App.Views.Dialogs;

/// <summary>
/// 快捷键目录对话框（只读）。
/// </summary>
/// <remarks>
/// 关闭语义全部由基类 <see cref="DialogWindow"/> 观察 <c>DialogViewModelBase.IsClosed</c> 完成，
/// 本视图只补一件事：把「复制为文本」的纯文本写进系统剪贴板——视图模型层不依赖 Avalonia，剪贴板只能在视图里做。
/// 剪贴板被占用或平台不支持时静默失败：目录本身仍然可以手动选中复制，不值得为它弹错误框。
/// </remarks>
public sealed partial class ShortcutsDialogView : DialogWindow
{
    private ShortcutsCatalogViewModel? _viewModel;

    /// <summary>初始化视图。</summary>
    public ShortcutsDialogView() => InitializeComponent();

    /// <inheritdoc />
    protected override void OnDataContextChanged(EventArgs e)
    {
        if (_viewModel is not null)
        {
            _viewModel.CopyRequested -= OnCopyRequested;
        }

        _viewModel = DataContext as ShortcutsCatalogViewModel;

        if (_viewModel is not null)
        {
            _viewModel.CopyRequested += OnCopyRequested;
        }

        base.OnDataContextChanged(e);
    }

    /// <inheritdoc />
    protected override void OnClosed(EventArgs e)
    {
        if (_viewModel is not null)
        {
            _viewModel.CopyRequested -= OnCopyRequested;
            _viewModel = null;
        }

        base.OnClosed(e);
    }

    private async void OnCopyRequested(string report)
    {
        try
        {
            var topLevel = TopLevel.GetTopLevel(this);
            if (topLevel?.Clipboard is { } clipboard)
            {
                await clipboard.SetTextAsync(report).ConfigureAwait(true);
            }
        }
        catch (Exception)
        {
            // 剪贴板在远程会话/受限环境下可能不可用：不打断对话框，用户仍可手动复制。
        }
    }
}
