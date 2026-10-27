using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using CkanUI.ViewModels.Pages;
using CkanUI.ViewModels.Pages.Mods;

namespace CkanUI.App.Views;

/// <summary>
/// 模组管理页视图（03 篇 §3 / 06 篇 UI-02）。命名与命名空间必须与 <c>CkanUI.ViewModels.Pages.ModsPageViewModel</c>
/// 严格对应，<see cref="ViewLocator"/> 才能按命名约定反射解析到本视图。
/// </summary>
/// <remarks>
/// 视图只做四件"必须碰控件"的事：把 DataGrid 的选中项转给 VM、把行内复选框点击转给 VM 命令、
/// 处理快捷键、以及列宽的"套用/回写"（列宽是控件属性，VM 只负责记忆）。
/// 其余状态全部由 VM 属性驱动。
/// </remarks>
public partial class ModsPageView : UserControl
{
    /// <summary>
    /// 列键顺序，必须与 XAML 里 <c>DataGrid.Columns</c> 的声明顺序以及
    /// <c>ModsPageViewModel.DefaultColumnWidths</c> 的键一致（列不在名字作用域里，只能按下标取）。
    /// </summary>
    private static readonly string[] ColumnKeys =
    {
        "select", "name", "installed", "latest", "author", "downloads", "tags", "size",
    };

    private bool _columnHooksAttached;
    private bool _applyingColumnWidths;

    /// <summary>构造视图。</summary>
    public ModsPageView()
    {
        InitializeComponent();
        Loaded += OnViewLoaded;
        Unloaded += OnViewUnloaded;
    }

    private ModsPageViewModel? ViewModel => DataContext as ModsPageViewModel;

    // ---- 列宽：恢复与回写（UI-02 列定义 / 列宽记忆）----------------------

    private void OnViewLoaded(object? sender, RoutedEventArgs e)
    {
        ApplyColumnWidths();
        HookColumnWidths();
    }

    private void OnViewUnloaded(object? sender, RoutedEventArgs e) => UnhookColumnWidths();

    /// <summary>把 VM 记住的列宽套到各列（缺失的键保留 XAML 里的默认宽度）。</summary>
    private void ApplyColumnWidths()
    {
        if (ViewModel is not { } vm)
        {
            return;
        }

        IReadOnlyDictionary<string, double> widths;
        try
        {
            widths = vm.RestoreColumnWidths();
        }
        catch (Exception)
        {
            // VM 已经做过异常收口，这里再兜一层：列宽恢复失败绝不能挡住页面渲染。
            return;
        }

        _applyingColumnWidths = true;
        try
        {
            for (var i = 0; i < ColumnKeys.Length; i++)
            {
                if (ColumnAt(i) is { } column
                    && widths.TryGetValue(ColumnKeys[i], out var width)
                    && width > 0)
                {
                    column.Width = new DataGridLength(width);
                }
            }
        }
        finally
        {
            _applyingColumnWidths = false;
        }
    }

    private void HookColumnWidths()
    {
        if (_columnHooksAttached)
        {
            return;
        }

        for (var i = 0; i < ColumnKeys.Length; i++)
        {
            if (ColumnAt(i) is { } column)
            {
                column.PropertyChanged += OnColumnPropertyChanged;
            }
        }

        _columnHooksAttached = true;
    }

    private void UnhookColumnWidths()
    {
        if (!_columnHooksAttached)
        {
            return;
        }

        for (var i = 0; i < ColumnKeys.Length; i++)
        {
            if (ColumnAt(i) is { } column)
            {
                column.PropertyChanged -= OnColumnPropertyChanged;
            }
        }

        _columnHooksAttached = false;
    }

    /// <summary>用户拖拽列头后宽度会变（像素宽度才记），交给 VM 做 300ms 防抖落盘。</summary>
    private void OnColumnPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (_applyingColumnWidths || e.Property.Name != "Width")
        {
            return;
        }

        PersistColumnWidths();
    }

    private void PersistColumnWidths()
    {
        if (ViewModel is not { } vm)
        {
            return;
        }

        var widths = new Dictionary<string, double>(StringComparer.Ordinal);
        for (var i = 0; i < ColumnKeys.Length; i++)
        {
            if (ColumnAt(i) is not { } column)
            {
                continue;
            }

            var length = column.Width;
            if (length.UnitType == DataGridLengthUnitType.Pixel
                && double.IsFinite(length.Value)
                && length.Value > 0)
            {
                widths[ColumnKeys[i]] = length.Value;
            }
        }

        if (widths.Count > 0)
        {
            vm.SaveColumnWidths(widths);
        }
    }

    private DataGridColumn? ColumnAt(int index) =>
        index >= 0 && index < ModsGrid.Columns.Count ? ModsGrid.Columns[index] : null;

    // ---- 选择与快捷键 -----------------------------------------------------

    private void OnRowSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (ViewModel is { } vm && sender is DataGrid grid)
        {
            vm.SelectedRow = grid.SelectedItem as ModsRowView;
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
