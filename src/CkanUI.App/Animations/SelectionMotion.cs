using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace CkanUI.App.Animations;

/// <summary>
/// 选中与键盘焦点的反馈。
/// 列表/树/表格的选中态原来只有背景色瞬间切换：鼠标点下去视觉上"跳"一下，
/// 没有任何位移或缩放交代；键盘 Tab 到按钮也只有一圈静态描边。
/// 这里给被选中的容器和获得焦点的控件各补一次冲量，走 <see cref="TransformSpring.KickScale"/>，
/// 与悬停（位移通道）互不干扰。
/// </summary>
public static class SelectionMotion
{
    /// <summary>选中一行时的缩放冲量：1.5% 的弹一下。</summary>
    public const double SelectionPop = 0.015d;

    /// <summary>键盘焦点落到控件时的缩放冲量：略小于选中，避免抢视线。</summary>
    public const double FocusPop = 0.012d;

    private const int MaxDepth = 24;
    private const int MaxVisited = 20000;

    private static readonly ConditionalWeakTable<Control, object> Tracked = new();

    /// <summary>接管 <paramref name="root"/> 下所有行容器与键盘焦点的反馈。</summary>
    public static int Attach(Visual? root)
    {
        if (root is not Control host || MotionBudget.IsCollapsed)
        {
            return 0;
        }

        MotionScan.Subscribe(root, nameof(SelectionMotion), () => Attach(root));

        int adopted = 0;
        foreach (var control in Enumerate(root))
        {
            if (Tracked.TryGetValue(control, out _))
            {
                continue;
            }

            if (!IsRow(control))
            {
                switch (control)
                {
                    case ComboBox:
                        continue;
                    case SelectingItemsControl selecting:
                        selecting.SelectionChanged += OnSelectingChanged;
                        break;
                    case TreeView tree:
                        tree.SelectionChanged += OnTreeChanged;
                        break;
                    case DataGrid grid:
                        grid.SelectionChanged += OnGridChanged;
                        break;
                    default:
                        continue;
                }

                Tracked.Add(control, new object());
                adopted++;
                continue;
            }

            // 行容器会被虚拟化回收复用：挂在容器上而不是"选中事件 + 遍历"，换数据后照样弹。
            control.PropertyChanged += OnRowPropertyChanged;
            Tracked.Add(control, new object());
            adopted++;

            // 容器晚于选中态实现（虚拟化列表里常见）：没有属性变化可听，补一次。
            if (IsSelected(control))
            {
                Pop(control);
            }
        }

        if (!Tracked.TryGetValue(host, out _))
        {
            Tracked.Add(host, new object());
            host.AddHandler(InputElement.GotFocusEvent, OnGotFocus, RoutingStrategies.Bubble, handledEventsToo: true);
        }

        return adopted;
    }

    /// <summary>键盘焦点落到 <paramref name="control"/> 时的反馈（焦点事件之外也能直接调用，便于门禁）。</summary>
    public static void NotifyFocused(Control control)
    {
        if (MotionBudget.IsCollapsed)
        {
            return;
        }

        // 指针已经在控件上（鼠标点出来的焦点）就不再补冲量：按压与悬停已经交代过了。
        if (control.IsPointerOver)
        {
            return;
        }

        if (control is TextBox or ComboBox or AutoCompleteBox)
        {
            return;
        }

        Kick(control, FocusPop);
    }

    private static void OnSelectingChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (sender is SelectingItemsControl list)
        {
            PopAfterLayout(list, () => PopSelected(list));
        }
    }

    private static void OnTreeChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (sender is TreeView tree)
        {
            PopAfterLayout(tree, () => PopSelected(tree));
        }
    }

    private static void OnGridChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (sender is DataGrid grid)
        {
            PopAfterLayout(grid, () => PopSelected(grid));
        }
    }

    /// <summary>选中之后的那次布局里，虚拟化列表才把行容器实现出来：立刻数一次，再等一次布局补数。</summary>
    private static void PopAfterLayout(Control owner, Action pop)
    {
        pop();

        EventHandler? once = null;
        once = (_, _) =>
        {
            owner.LayoutUpdated -= once;
            pop();
        };

        owner.LayoutUpdated += once;
    }

    private static void PopSelected(SelectingItemsControl list) => PopSelected(list.GetRealizedContainers());

    private static void PopSelected(TreeView tree) =>
        PopSelected(tree.GetVisualDescendants().OfType<TreeViewItem>().Cast<Control>());

    private static void PopSelected(DataGrid grid) =>
        PopSelected(grid.GetVisualDescendants().OfType<DataGridRow>().Cast<Control>());

    private static void PopSelected(IEnumerable<Control> containers)
    {
        foreach (var container in containers)
        {
            if (IsSelected(container))
            {
                Pop(container);
            }
        }
    }

    private static void OnRowPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (sender is not Control control || !IsSelectionProperty(control, e.Property))
        {
            return;
        }

        if (e.NewValue is true)
        {
            Pop(control);
        }
    }

    /// <summary>给一个刚变成选中态的行容器补一次弹一下；容器实现时机不对时可从外部补。</summary>
    public static void NotifySelected(Control control) => Pop(control);

    private static void Pop(Control control) => Kick(control, SelectionPop);

    /// <summary>
    /// 冲量之前先摘掉控件自己的过渡：样式里的 TransformOperationsTransition 会把
    /// 我们写下的 TransformGroup 遮成"正在过渡中的 TransformOperations"，
    /// 弹簧的逐帧位移被它再平滑一次，看起来就是卡住不动。缩放冲量本身已经交代了变化，摘掉无副作用。
    /// </summary>
    private static void Kick(Control control, double pop)
    {
        MotionTransitions.Strip(control, includeOpacity: false);
        TransformSpring.For(control, SpringSpec.Snappy).KickScale(MotionBudget.Amplitude(pop));
    }

    private static bool IsSelected(Control control) => control switch
    {
        ListBoxItem item => item.IsSelected,
        TreeViewItem item => item.IsSelected,
        DataGridRow row => row.IsSelected,
        _ => false,
    };

    private static bool IsRow(Control control) => control is ListBoxItem or TreeViewItem or DataGridRow;

    private static bool IsSelectionProperty(Control control, AvaloniaProperty property) =>
        (control, property) switch
        {
            (ListBoxItem, var p) when p == ListBoxItem.IsSelectedProperty => true,
            (TreeViewItem, var p) when p == TreeViewItem.IsSelectedProperty => true,
            (DataGridRow, var p) when p == DataGridRow.IsSelectedProperty => true,
            _ => false,
        };

    private static void OnGotFocus(object? sender, FocusChangedEventArgs e)
    {
        if (e.NewFocusedElement is Control control)
        {
            NotifyFocused(control);
        }
    }

    private static IEnumerable<Control> Enumerate(Visual root)
    {
        var queue = new Queue<(Visual Node, int Depth)>();
        queue.Enqueue((root, 0));
        int visited = 0;

        while (queue.Count > 0 && visited < MaxVisited)
        {
            var (node, depth) = queue.Dequeue();
            visited++;

            if (node is Control control && !ReferenceEquals(control, root))
            {
                yield return control;
            }

            if (depth >= MaxDepth)
            {
                continue;
            }

            foreach (var child in node.GetVisualChildren())
            {
                queue.Enqueue((child, depth + 1));
            }
        }
    }
}
