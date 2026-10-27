using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using CkanUI.ViewModels.Pages;
using CkanUI.ViewModels.Pages.PlayTime;

namespace CkanUI.App.Views;

/// <summary>游戏时长页面键盘交互：F2 编辑，Enter 保存，Esc 取消。</summary>
public partial class PlayTimePageView : UserControl
{
    public PlayTimePageView()
    {
        InitializeComponent();
        AddHandler(KeyDownEvent, OnKeyDown, RoutingStrategies.Tunnel);
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (DataContext is not PlayTimePageViewModel vm || e.Key != Key.F2) return;
        if (FindListBox()?.SelectedItem is not PlayTimeRowViewModel row) return;
        vm.BeginEditCommand.Execute(row);
        e.Handled = true;
    }

    private void OnRowKeyDown(object? sender, KeyEventArgs e)
    {
        if (DataContext is not PlayTimePageViewModel vm || sender is not TextBox { DataContext: PlayTimeRowViewModel row }) return;
        if (e.Key == Key.Enter) { vm.CommitEditCommand.Execute(row); e.Handled = true; }
        else if (e.Key == Key.Escape) { vm.CancelEditCommand.Execute(row); e.Handled = true; }
    }

    private ListBox? FindListBox() => this.FindControl<ListBox>("PlayTimeList");
}
