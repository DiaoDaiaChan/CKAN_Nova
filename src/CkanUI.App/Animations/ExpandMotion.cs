using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.VisualTree;

namespace CkanUI.App.Animations;

/// <summary>
/// 展开即淡入：`Expander` 与 `TreeViewItem` 展开时内容一帧硬切出现，补一段上浮 + 淡入。
/// </summary>
/// <remarks>
/// <para>只动展开（`IsExpanded` 变 true）。收起时内容立刻从可视树摘掉，没有可演的中间态；
/// 硬要演就得自己接管高度与裁剪，等于重写模板布局，收益不抵风险。</para>
/// <para>不动高度：高度是布局结果，逐帧写会被 arrange 覆盖（和 `ProgressBar` 的 `PART_Indicator.Width` 同一个理由）。
/// 这里补的是"出现方式"——内容从下方 8 px 浮上来、同时淡入。</para>
/// </remarks>
public static class ExpandMotion
{
    /// <summary>完整档的起步下移量（px）。</summary>
    public const double OffsetPixels = 8d;

    /// <summary>精简档的起步下移量（px）。</summary>
    public const double ReducedOffsetPixels = 3d;

    private const int MaxDepth = 24;
    private const int MaxVisited = 20000;

    private static readonly ConditionalWeakTable<Control, object> Tracked = new();

    /// <summary>给一棵可视树里的 `Expander` / `TreeViewItem` 挂上展开入场；返回本次新接管的数量。</summary>
    /// <param name="root">要扫描的根（页面宿主、对话框或整个窗口）。</param>
    public static int Attach(Visual? root)
    {
        if (root is not Control || MotionBudget.IsCollapsed)
        {
            return 0;
        }

        MotionScan.Subscribe(root, nameof(ExpandMotion), () => Attach(root));

        var adopted = 0;
        foreach (var control in Enumerate(root))
        {
            if (Tracked.TryGetValue(control, out _))
            {
                continue;
            }

            if (control is Expander expander)
            {
                Tracked.Add(expander, expander);
                expander.PropertyChanged += OnExpandedChanged;
                adopted++;
            }
            else if (control is TreeViewItem item)
            {
                Tracked.Add(item, item);
                item.PropertyChanged += OnExpandedChanged;
                adopted++;
            }
        }

        return adopted;
    }

    /// <summary>对某个已经展开的内容宿主播一次入场（供测试与需要手动触发的场合）。</summary>
    /// <param name="host">展开出来的内容宿主（`ContentPresenter` / `ItemsPresenter`）。</param>
    public static void Play(Control host)
    {
        if (MotionBudget.IsCollapsed)
        {
            return;
        }

        host.Transitions = null;

        var full = MotionBudget.AllowsPageChoreography;
        var offset = full ? MotionBudget.Amplitude(OffsetPixels) : ReducedOffsetPixels;
        var rest = host.Opacity > 0d ? host.Opacity : 1d;
        var spring = TransformSpring.For(host, SpringSpec.Glide);
        spring.MoveTo(y: offset, opacity: 0d, snap: true);
        spring.MoveTo(y: 0d, opacity: rest);
    }

    private static void OnExpandedChanged(object? sender, AvaloniaPropertyChangedEventArgs args)
    {
        if (sender is not Control owner || args.NewValue is not true)
        {
            return;
        }

        if (args.Property != Expander.IsExpandedProperty && args.Property != TreeViewItem.IsExpandedProperty)
        {
            return;
        }

        if (HostOf(owner) is { } host)
        {
            Play(host);
            return;
        }

        // 模板可能晚一拍才把内容宿主实现出来（虚拟化 / 数据刚到位）：下一次布局再找一次就够，不常驻。
        void Retry(object? _, EventArgs __)
        {
            owner.LayoutUpdated -= Retry;

            if (HostOf(owner) is { } late)
            {
                Play(late);
            }
        }

        owner.LayoutUpdated += Retry;
    }

    /// <summary>`Expander` 取正文的 `ContentPresenter`（页眉也是一个 `ContentPresenter`，所以取最后一个）；
    /// `TreeViewItem` 取子项的 `ItemsPresenter`。</summary>
    private static Control? HostOf(Control owner)
    {
        Control? found = null;

        foreach (var descendant in owner.GetVisualDescendants())
        {
            if (owner is Expander && descendant is ContentPresenter presenter)
            {
                found = presenter;
            }
            else if (owner is TreeViewItem && descendant is ItemsPresenter items)
            {
                return items;
            }
        }

        return found;
    }

    private static IEnumerable<Control> Enumerate(Visual root)
    {
        var queue = new Queue<(Visual Node, int Depth)>();
        queue.Enqueue((root, 0));
        var visited = 0;

        while (queue.Count > 0 && visited < MaxVisited)
        {
            var (node, depth) = queue.Dequeue();
            visited++;

            if (node is Control control)
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