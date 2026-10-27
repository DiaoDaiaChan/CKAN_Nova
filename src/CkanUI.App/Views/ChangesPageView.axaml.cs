using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using CkanUI.ViewModels.Pages;
using CkanUI.ViewModels.Pages.Changes;

namespace CkanUI.App.Views;

/// <summary>
/// 变更集页视图（03 篇 §4 V-02 / 06 篇 UI-04）。命名空间与类名必须与
/// <c>CkanUI.ViewModels.Pages.ChangesPageViewModel</c> 严格对应，<see cref="ViewLocator"/> 才能按命名约定反射解析。
/// </summary>
/// <remarks>
/// 视图只做"必须碰控件"的一件事：转发 Ctrl+Enter（开始）/ Ctrl+R（重新计算）/ Esc（取消）快捷键。
/// 所有文案、颜色、状态全部由 VM 属性与 <c>Resources/Tokens.axaml</c> 的 Ckan* token 驱动。
/// </remarks>
public partial class ChangesPageView : UserControl
{
    /// <summary>构造视图。</summary>
    public ChangesPageView()
    {
        InitializeComponent();
    }

    private ChangesPageViewModel? ViewModel => DataContext as ChangesPageViewModel;

    private void OnRemoveItemClick(object? sender, RoutedEventArgs e)
    {
        if (ViewModel is { } vm && sender is Control control && control.DataContext is ChangeRowView row)
        {
            if (vm.RemoveItemCommand.CanExecute(row))
            {
                vm.RemoveItemCommand.Execute(row);
            }
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
                case Key.Enter:
                    if (vm.ApplyCommand.CanExecute(null))
                    {
                        vm.ApplyCommand.Execute(null);
                    }

                    e.Handled = true;
                    break;
                case Key.R:
                    if (vm.RecomputeCommand.CanExecute(null))
                    {
                        vm.RecomputeCommand.Execute(null);
                    }

                    e.Handled = true;
                    break;
                default:
                    break;
            }

            return;
        }

        if (e.Key == Key.Escape && vm.CancelCommand.CanExecute(null))
        {
            vm.CancelCommand.Execute(null);
            e.Handled = true;
        }
    }
}
