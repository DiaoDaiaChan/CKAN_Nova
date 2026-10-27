using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using CkanUI.ViewModels.Pages;
using CkanUI.ViewModels.Pages.Mods;

namespace CkanUI.App.Views;

/// <summary>
/// 模组管理页视图（03 篇 §3）。命名与命名空间必须与 <c>CkanUI.ViewModels.Pages.ModsPageViewModel</c>
/// 严格对应，<see cref="ViewLocator"/> 才能按命名约定反射解析到本视图。
/// </summary>
/// <remarks>
/// 视图只做三件"必须碰控件"的事：把 ListBox 的选中项转给 VM、把行内复选框点击转给 VM 命令、
/// 处理 Ctrl+F / Ctrl+R / Ctrl+Enter / Space / Esc 快捷键。其余状态全部由 VM 属性驱动。
/// </remarks>
public partial class ModsPageView : UserControl
{
    /// <summary>构造视图。</summary>
    public ModsPageView()
    {
        InitializeComponent();
    }

    private ModsPageViewModel? ViewModel => DataContext as ModsPageViewModel;

    private void OnRowSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (ViewModel is { } vm && sender is ListBox list)
        {
            vm.SelectedRow = list.SelectedItem as ModsRowView;
        }
    }

    private void OnSelectionCheckBoxClick(object? sender, RoutedEventArgs e)
    {
        if (ViewModel is { } vm && sender is Control control && control.DataContext is ModsRowView row)
        {
            vm.ToggleSelection(row.Identifier);
        }
    }

    private void OnSearchKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            ViewModel?.ClearSearch();
            e.Handled = true;
        }
    }

    private void OnRootKeyDown(object? sender, KeyEventArgs e)
    {
        var vm = ViewModel;
        if (vm is null)
        {
            return;
        }

        if (e.KeyModifiers.HasFlag(KeyModifiers.Control))
        {
            switch (e.Key)
            {
                case Key.F:
                    SearchBox.Focus();
                    SearchBox.SelectAll();
                    e.Handled = true;
                    break;
                case Key.R:
                    if (vm.RefreshRepositoriesCommand.CanExecute(null))
                    {
                        vm.RefreshRepositoriesCommand.Execute(null);
                    }

                    e.Handled = true;
                    break;
                case Key.Enter:
                    if (vm.ApplyChangesCommand.CanExecute(null))
                    {
                        vm.ApplyChangesCommand.Execute(null);
                    }

                    e.Handled = true;
                    break;
                default:
                    break;
            }

            return;
        }

        switch (e.Key)
        {
            case Key.Escape:
                vm.ClearSearch();
                e.Handled = true;
                break;
            case Key.Space:
                if (vm.SelectedRow is { } row)
                {
                    vm.ToggleSelection(row.Identifier);
                    e.Handled = true;
                }

                break;
            default:
                break;
        }
    }
}
