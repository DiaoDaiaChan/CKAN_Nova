using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using CkanUI.ViewModels;
using CkanUI.ViewModels.Pages;
using CkanUI.ViewModels.Pages.ModConfig;

namespace CkanUI.App.Views;

/// <summary>
/// Mod 配置页视图（M9 / UI-CFG-01 骨架 + CFG-04 编辑控件）：只做绑定、键盘快捷方式与生命周期接线，
/// 业务全在 <see cref="CkanUI.ViewModels.Pages.ModConfigPageViewModel"/> 里。
/// </summary>
/// <remarks>
/// 键盘：Ctrl+S 保存整页（隧道阶段接住，焦点在搜索框/文本框里也能用）；
/// 单行编辑框里 Enter 提交当前行，多行（list）编辑框里 Ctrl+Enter 才提交，Enter 留给换行。
/// 视图挂到可视树时激活 VM（每次进页面重读配置），移出时取消激活。
/// </remarks>
public partial class ModConfigPageView : UserControl
{
    /// <summary>构造视图（ViewLocator 无参构造）。</summary>
    public ModConfigPageView()
    {
        InitializeComponent();
        AddHandler(KeyDownEvent, OnRootKeyDown, RoutingStrategies.Tunnel);
        AttachedToVisualTree += (_, _) => (DataContext as PageViewModel)?.Activate();
        DetachedFromVisualTree += (_, _) => (DataContext as PageViewModel)?.Deactivate();
    }

    private void OnRootKeyDown(object? sender, KeyEventArgs e)
    {
        if (DataContext is not ModConfigPageViewModel vm
            || e.Key != Key.S
            || !e.KeyModifiers.HasFlag(KeyModifiers.Control))
        {
            return;
        }

        if (vm.SaveCommand.CanExecute(null))
        {
            vm.SaveCommand.Execute(null);
        }

        e.Handled = true;
    }

    private void OnEditorKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter
            || DataContext is not ModConfigPageViewModel vm
            || sender is not TextBox { DataContext: ModConfigFieldRowViewModel row })
        {
            return;
        }

        // 多行（list）框里 Enter 是换行，提交要按 Ctrl+Enter。
        if (row.IsMultilineEditor && !e.KeyModifiers.HasFlag(KeyModifiers.Control))
        {
            return;
        }

        if (row.HasPendingChange && vm.SaveRowCommand.CanExecute(row))
        {
            vm.SaveRowCommand.Execute(row);
        }

        e.Handled = true;
    }
}
