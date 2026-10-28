using System;
using System.ComponentModel;

using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;

using CkanUI.ViewModels.CommandPalette;

namespace CkanUI.App.Views;

/// <summary>
/// 命令面板浮层（Ctrl+K）。
/// </summary>
/// <remarks>
/// 宿主（MainWindow）把本控件放在最外层 Panel 里，DataContext 指向 <see cref="CommandPaletteViewModel"/>；
/// 「打开面板」的 Ctrl+K 键由外壳绑定到 <c>ToggleCommand</c>，本控件不抢这个键，
/// 只处理面板打开后的内部导航：↑/↓ 移动选中、Enter 执行、Esc 关闭、点遮罩关闭。
/// 键盘用 Tunnel 阶段接管：搜索框自己也会吃掉方向键（移动光标），必须先于它处理。
/// 打开瞬间自动聚焦搜索框并全选，否则用户输入会落到别的控件上（05 篇 §7 焦点口径）。
/// </remarks>
public sealed partial class CommandPaletteView : UserControl
{
    private CommandPaletteViewModel? _viewModel;

    /// <summary>初始化视图。</summary>
    public CommandPaletteView()
    {
        InitializeComponent();

        AddHandler(KeyDownEvent, OnPreviewKeyDown, RoutingStrategies.Tunnel);
    }

    /// <inheritdoc />
    protected override void OnDataContextChanged(EventArgs e)
    {
        if (_viewModel is not null)
        {
            _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
        }

        _viewModel = DataContext as CommandPaletteViewModel;

        if (_viewModel is not null)
        {
            _viewModel.PropertyChanged += OnViewModelPropertyChanged;
        }

        base.OnDataContextChanged(e);
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(CommandPaletteViewModel.IsOpen)
            || _viewModel is null
            || !_viewModel.IsOpen)
        {
            return;
        }

        // 面板刚打开：等一次布局再抢焦点，此时搜索框才在可视树上，Focus() 不会落空。
        Dispatcher.UIThread.Post(
            () =>
            {
                var searchBox = this.FindControl<TextBox>("SearchBox");
                searchBox?.Focus();
                searchBox?.SelectAll();
            },
            DispatcherPriority.Input);
    }

    private void OnPreviewKeyDown(object? sender, KeyEventArgs e)
    {
        if (_viewModel is null || !_viewModel.IsOpen)
        {
            return;
        }

        switch (e.Key)
        {
            case Key.Up:
                _viewModel.MoveUpCommand.Execute(null);
                e.Handled = true;
                break;

            case Key.Down:
                _viewModel.MoveDownCommand.Execute(null);
                e.Handled = true;
                break;

            case Key.Enter:
                _viewModel.ExecuteSelectedCommand.Execute(null);
                e.Handled = true;
                break;

            case Key.Escape:
                _viewModel.CloseCommand.Execute(null);
                e.Handled = true;
                break;

            default:
                break;
        }
    }

    private void OnScrimPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        // 只有点在遮罩本身（不是面板内部的子控件）才关闭，卡片内的点击照常选中行。
        if (_viewModel is null || !ReferenceEquals(e.Source, sender))
        {
            return;
        }

        _viewModel.CloseCommand.Execute(null);
    }
}
