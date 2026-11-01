using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.VisualTree;

namespace CkanUI.App.Animations;

/// <summary>页内页签：下划线换成弹簧冲量，内容舞台按切换方向滑一小段。</summary>
/// <remarks>
/// <para>下划线原本靠样式里的 TransformOperationsTransition + SpringEasing：连续点页签时目标二次变化就从 t=0 重算，速度归零。</para>
/// <para>内容舞台（PART_SelectedContentHost）完全没人动过——样式给了过渡，但没有代码去改它的 RenderTransform，等于死配置。</para>
/// </remarks>
public static class TabStripMotion
{
    /// <summary>内容舞台的行程。</summary>
    public const double SlidePixels = 12d;

    /// <summary>下划线未展开时的横向比例。</summary>
    public const double UnderlineStartScale = 0.35d;

    /// <summary>模板里的下划线名字。</summary>
    public const string UnderlineName = "PART_TabUnderline";

    /// <summary>模板里的内容舞台名字。</summary>
    public const string ContentHostName = "PART_SelectedContentHost";

    private const int MaxDepth = 12;

    private const int MaxVisited = 6000;

    private static readonly ConditionalWeakTable<TabControl, State> States = new();

    /// <summary>接管 root 下的页签；返回本次新增接管的 TabControl 数量。</summary>
    public static int Attach(Visual? root)
    {
        if (root is null)
        {
            return 0;
        }

        MotionScan.Subscribe(root, nameof(TabStripMotion), () => Attach(root));

        var count = 0;
        foreach (var tab in Enumerate(root))
        {
            if (States.TryGetValue(tab, out _))
            {
                continue;
            }

            // 以接管时的选中项作为方向基准：TabControl 常在建项时就把第一项选中，
            // 那一拍没有 SelectionChanged，不记下来第一次切换就会被当成"无方向的首次选中"。
            var state = new State { LastIndex = tab.SelectedIndex };
            States.Add(tab, state);
            tab.SelectionChanged += (_, args) => OnSelectionChanged(tab, state, args);
            count++;
        }

        return count;
    }

    private static void OnSelectionChanged(TabControl tab, State state, SelectionChangedEventArgs args)
    {
        if (MotionBudget.IsCollapsed)
        {
            return;
        }

        var index = tab.SelectedIndex;
        var sign = index == state.LastIndex || state.LastIndex < 0 ? 0d : index > state.LastIndex ? 1d : -1d;
        state.LastIndex = index;

        foreach (var removed in args.RemovedItems)
        {
            if (removed is TabItem item)
            {
                RetractUnderline(item);
            }
        }

        foreach (var added in args.AddedItems)
        {
            if (added is TabItem item)
            {
                PlayUnderline(item);
            }
        }

        PlayContentStage(tab, sign);
    }

    private static void PlayUnderline(TabItem item)
    {
        if (Underline(item) is not { } underline)
        {
            return;
        }

        KillTransitions(underline);
        var spring = TransformSpring.For(underline, SpringSpec.Snappy);
        spring.MoveTo(opacity: 0d, scaleX: UnderlineStartScale, snap: true);
        spring.MoveTo(opacity: 1d, scaleX: 1d);
    }

    private static void RetractUnderline(TabItem item)
    {
        if (Underline(item) is not { } underline)
        {
            return;
        }

        KillTransitions(underline);
        TransformSpring.For(underline, SpringSpec.Snappy).MoveTo(opacity: 0d, scaleX: UnderlineStartScale);
    }

    private static void PlayContentStage(TabControl tab, double sign)
    {
        if (ContentHost(tab) is not { } host)
        {
            return;
        }

        KillTransitions(host);
        var spring = TransformSpring.For(host, SpringSpec.Glide);
        if (sign == 0d)
        {
            // 首次选中：没有方向，只做一次轻微下沉，避免凭空出现。
            spring.MoveTo(y: MotionBudget.Amplitude(SlidePixels * 0.5d), snap: true);
            spring.MoveTo(y: 0d);
            return;
        }

        spring.MoveTo(x: sign * MotionBudget.Amplitude(SlidePixels), snap: true);
        spring.MoveTo(x: 0d);
    }

    /// <summary>模板里的下划线；模板还没套上时返回 null。</summary>
    private static Border? Underline(TabItem item) => Find<Border>(item, UnderlineName);

    private static ContentPresenter? ContentHost(TabControl tab) => Find<ContentPresenter>(tab, ContentHostName);

    private static T? Find<T>(Visual root, string name)
        where T : Control
    {
        foreach (var descendant in root.GetVisualDescendants())
        {
            if (descendant is T control && control.Name == name)
            {
                return control;
            }
        }

        return null;
    }

    private static void KillTransitions(Control control)
    {
        // 样式里的过渡会把逐帧写入再平滑一次（起点读出来仍是旧值），接管前先摘掉。
        if (control.Transitions is not null)
        {
            control.Transitions = null;
        }
    }

    private static IEnumerable<TabControl> Enumerate(Visual root)
    {
        var queue = new Queue<(Visual Node, int Depth)>();
        queue.Enqueue((root, 0));
        var visited = 0;

        while (queue.Count > 0 && visited < MaxVisited)
        {
            var (node, depth) = queue.Dequeue();
            visited++;

            if (node is TabControl tab)
            {
                yield return tab;
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

    private sealed class State
    {
        internal int LastIndex { get; set; } = -1;
    }
}

