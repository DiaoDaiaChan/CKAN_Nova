using System.ComponentModel;
using Avalonia.Controls;
using CkanUI.ViewModels.Dialogs;

namespace CkanUI.App.Views.Dialogs;

/// <summary>
/// 对话框窗口基类（05 篇 §3.2 / 03 篇 §8）：只负责"长什么样"和"自己关掉自己"。
/// 弹窗时机与 owner 一律由调用方（<c>IDialogService</c>）用 <c>ShowDialog(owner)</c> 决定，
/// 这里绝不调用 <c>Show()</c>，也不自己找 owner —— 后台线程弹框时 owner 必须由调用方在 UI 线程提前捕获。
/// </summary>
public class DialogWindow : Window
{
    private INotifyPropertyChanged? _viewModel;

    /// <summary>初始化对话框窗口的公共外观（不可缩放、居中于 owner、不进任务栏）。</summary>
    protected DialogWindow()
    {
        CanResize = false;
        ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        MinWidth = 360;
        MaxWidth = 760;
        MaxHeight = 720;
    }

    /// <inheritdoc />
    protected override void OnDataContextChanged(EventArgs e)
    {
        if (_viewModel is not null)
        {
            _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
        }

        base.OnDataContextChanged(e);

        _viewModel = DataContext as INotifyPropertyChanged;
        if (_viewModel is not null)
        {
            _viewModel.PropertyChanged += OnViewModelPropertyChanged;
        }
    }

    /// <inheritdoc />
    protected override void OnClosed(EventArgs e)
    {
        if (_viewModel is not null)
        {
            _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
            _viewModel = null;
        }

        base.OnClosed(e);
    }

    /// <summary>VM 置 <see cref="DialogViewModelBase.IsClosed"/> 时关窗；已关闭则忽略，避免重复 Close。</summary>
    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(DialogViewModelBase.IsClosed) || !IsVisible)
        {
            return;
        }

        if (DataContext is DialogViewModelBase viewModel && viewModel.IsClosed)
        {
            Close();
        }
    }
}
