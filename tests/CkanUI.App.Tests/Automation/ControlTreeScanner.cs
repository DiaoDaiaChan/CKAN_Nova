using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.LogicalTree;
using Avalonia.VisualTree;
using System.Windows.Input;

namespace CkanUI.App.Tests.Automation;

/// <summary>
/// 控件树交互项描述信息。
/// </summary>
public sealed record DiscoveredControlInfo(
    Control Control,
    string ControlType,
    string? Name,
    string DisplayText,
    ICommand? Command,
    object? CommandParameter,
    bool IsEnabled,
    bool CanExecute,
    string TreePath);

/// <summary>
/// 递归扫描视觉与逻辑控件树，发现所有可交互的按钮、选择器和菜单项。
/// </summary>
public static class ControlTreeScanner
{
    public static IReadOnlyList<DiscoveredControlInfo> ScanInteractiveControls(Control root)
    {
        ArgumentNullException.ThrowIfNull(root);

        var result = new List<DiscoveredControlInfo>();
        var seen = new HashSet<Control>();

        // 1. 优先扫描逻辑树（针对 MVVM 模板与页面内容）
        foreach (var item in root.GetLogicalDescendants())
        {
            if (item is Control c && seen.Add(c))
            {
                InspectControl(c, result);
            }
        }

        // 2. 补充扫描视觉树（捕获模板内部生成的控件）
        foreach (var item in root.GetVisualDescendants())
        {
            if (item is Control c && seen.Add(c))
            {
                InspectControl(c, result);
            }
        }

        return result;
    }

    private static void InspectControl(Control control, List<DiscoveredControlInfo> list)
    {
        if (control is Button btn)
        {
            var text = ExtractText(btn.Content) ?? btn.Name ?? "(unnamed button)";
            var cmd = btn.Command;
            var param = btn.CommandParameter;
            var canExecute = cmd?.CanExecute(param) ?? btn.IsEnabled;

            list.Add(new DiscoveredControlInfo(
                Control: btn,
                ControlType: btn.GetType().Name,
                Name: btn.Name,
                DisplayText: text,
                Command: cmd,
                CommandParameter: param,
                IsEnabled: btn.IsEnabled,
                CanExecute: canExecute,
                TreePath: BuildTreePath(btn)));
        }
        else if (control is ToggleButton tb)
        {
            var text = ExtractText(tb.Content) ?? tb.Name ?? "(toggle button)";
            var cmd = tb.Command;
            var param = tb.CommandParameter;
            var canExecute = cmd?.CanExecute(param) ?? tb.IsEnabled;

            list.Add(new DiscoveredControlInfo(
                Control: tb,
                ControlType: tb.GetType().Name,
                Name: tb.Name,
                DisplayText: text,
                Command: cmd,
                CommandParameter: param,
                IsEnabled: tb.IsEnabled,
                CanExecute: canExecute,
                TreePath: BuildTreePath(tb)));
        }
        else if (control is MenuItem mi)
        {
            var text = ExtractText(mi.Header) ?? mi.Name ?? "(menu item)";
            var cmd = mi.Command;
            var param = mi.CommandParameter;
            var canExecute = cmd?.CanExecute(param) ?? mi.IsEnabled;

            list.Add(new DiscoveredControlInfo(
                Control: mi,
                ControlType: mi.GetType().Name,
                Name: mi.Name,
                DisplayText: text,
                Command: cmd,
                CommandParameter: param,
                IsEnabled: mi.IsEnabled,
                CanExecute: canExecute,
                TreePath: BuildTreePath(mi)));
        }
    }

    private static string? ExtractText(object? content)
    {
        if (content is null) return null;
        if (content is string s) return s;
        if (content is TextBlock tb) return tb.Text;
        return content.ToString();
    }

    private static string BuildTreePath(Control control)
    {
        var parts = new Stack<string>();
        ILogical? current = control;
        while (current is not null)
        {
            if (current is Control c)
            {
                var label = c.GetType().Name;
                if (!string.IsNullOrEmpty(c.Name))
                {
                    label += $"#{c.Name}";
                }
                parts.Push(label);
            }
            current = current.LogicalParent;
        }

        return string.Join(" > ", parts);
    }
}
