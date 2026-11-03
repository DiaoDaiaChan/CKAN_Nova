using System;
using Avalonia.Controls;
using Avalonia.Input;
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

    private async void OnOpened(object? sender, EventArgs e)
    {
        if (DataContext is SfsWorkbenchViewModel viewModel)
        {
            await viewModel.InitializeAsync();
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
