using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using CkanUI.Services.Abstractions.Dtos;

namespace CkanUI.Plugin.Saves;

/// <summary>
/// SFS 工作台窗口：只做“打开时加载一次”和“点搜索命中时转交 VM”这两件事。
/// </summary>
public partial class SfsWorkbenchWindow : Window
{
    /// <summary>构造窗口（宿主用 <c>new SfsWorkbenchWindow { DataContext = vm }</c>）。</summary>
    public SfsWorkbenchWindow()
    {
        InitializeComponent();
        Opened += OnOpened;
    }

    /// <summary>
    /// 无系统标题栏后的拖动区：点在按钮上时交给命令，双击切换最大化，其余情况拖动窗口。
    /// </summary>
    private void OnTitleBarPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.Source is Visual source && source.FindAncestorOfType<Button>(includeSelf: true) is not null)
        {
            return;
        }

        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            return;
        }

        if (e.ClickCount == 2)
        {
            WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
            e.Handled = true;
            return;
        }

        if (WindowState == WindowState.Maximized)
        {
            WindowState = WindowState.Normal;
        }

        BeginMoveDrag(e);
        e.Handled = true;
    }

    private void OnMinimizeClick(object? sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void OnMaximizeRestoreClick(object? sender, RoutedEventArgs e) =>
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

    private void OnCloseClick(object? sender, RoutedEventArgs e) => Close();

    private async void OnOpened(object? sender, EventArgs e)
    {
        if (DataContext is SfsWorkbenchViewModel viewModel)
        {
            viewModel.ClipboardWriter = text => Clipboard?.SetTextAsync(text) ?? System.Threading.Tasks.Task.CompletedTask;
            await viewModel.InitializeAsync();
        }
    }

    private void OnEditValueKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || DataContext is not SfsWorkbenchViewModel viewModel)
        {
            return;
        }

        if (viewModel.ApplyValueCommand.CanExecute(null))
        {
            viewModel.ApplyValueCommand.Execute(null);
            e.Handled = true;
        }
    }

    private void OnSearchBoxKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || DataContext is not SfsWorkbenchViewModel viewModel)
        {
            return;
        }

        if (viewModel.SearchCommand.CanExecute(null))
        {
            viewModel.SearchCommand.Execute(null);
        }

        e.Handled = true;
    }

    private void OnNodeDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (DataContext is SfsWorkbenchViewModel viewModel && viewModel.ExpandNodeCommand.CanExecute(null))
        {
            viewModel.ExpandNodeCommand.Execute(null);
            e.Handled = true;
        }
    }

    private void OnMatchSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (DataContext is not SfsWorkbenchViewModel viewModel)
        {
            return;
        }

        if (sender is ListBox { SelectedItem: SaveOutlineMatchDto match })
        {
            viewModel.OpenMatchCommand.Execute(match);
        }
    }
}
