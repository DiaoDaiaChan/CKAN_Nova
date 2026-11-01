using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.CompilerServices;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.VisualTree;

namespace CkanUI.App.Animations;

/// <summary>
/// 滚动入场：信息流滚动时，刚从视口底边露出来的元素自己浮上来一次。
/// </summary>
/// <remarks>
/// <para>评估走 <c>RequestAnimationFrame</c>：滚动事件一帧可能来十几次，逐次做坐标换算会拖慢滚动；
/// 这里只标脏、一帧评估一次，和 <see cref="MotionFramePump"/> 同一条 VSync。</para>
/// <para>每个元素用"上一帧的顶边"判断是不是刚露出来：只有在上一帧还在视口下方、这一帧进入视口的元素才演。
/// 第一次见到时就已经在视口里的（数据晚到、首屏、虚拟化跳转）直接登记为看过，不补演——
/// 否则滚动一下整屏会同时闪一下。</para>
/// <para>演过写进 <see cref="Seen"/>，回滚再下来不重演；滚出顶边的元素登记为看过。</para>
/// <para>只动 <c>translateY</c> 与不透明度，不参与布局；关闭档整层不挂。</para>
/// </remarks>
public static class ScrollRevealMotion
{
    /// <summary>起点下移距离（完整档，像素）。</summary>
    public const double OffsetPixels = 12d;

    /// <summary>起点下移距离（精简档，像素）。</summary>
    public const double ReducedOffsetPixels = 3d;

    private const int MaxDepth = 10;

    private const int MaxTargets = 400;

    private const int MaxVisited = 4000;

    private static readonly ConditionalWeakTable<Visual, RootState> Roots = new();

    private static readonly ConditionalWeakTable<ScrollViewer, ScrollState> Attached = new();

    private static readonly ConditionalWeakTable<Control, object> Seen = new();

    /// <summary>给 <paramref name="root"/> 下的滚动容器挂上滚动入场；返回本次新增接管的滚动容器数量。</summary>
    public static int Attach(Visual? root)
    {
        if (root is null || MotionBudget.IsCollapsed)
        {
            return 0;
        }

        var state = Roots.GetValue(root, static _ => new RootState());
        if (!state.Hooked)
        {
            state.Hooked = true;
            // 页面模板、数据加载、滚动容器都可能晚一拍成形：布局后补扫，避免只赌换页瞬间那一棵树。
            if (root is Layoutable layoutable)
            {
                layoutable.LayoutUpdated += (_, _) => Scan(root, state, throttle: true);
            }
        }

        return Scan(root, state, throttle: false);
    }

    private static int Scan(Visual root, RootState rootState, bool throttle)
    {
        if (throttle)
        {
            var now = Stopwatch.GetTimestamp();
            if (now - rootState.LastScan < Stopwatch.Frequency / 8)
            {
                return 0;
            }

            rootState.LastScan = now;
        }

        var count = 0;
        foreach (var scrollViewer in Enumerate<ScrollViewer>(root))
        {
            if (!Attached.TryGetValue(scrollViewer, out var state))
            {
                state = new ScrollState();
                Attached.Add(scrollViewer, state);
                scrollViewer.ScrollChanged += (_, _) => RequestReveal(scrollViewer, state);
                count++;
            }

            if (state.Initialized || scrollViewer.Viewport.Height <= 0d)
            {
                continue;
            }

            state.Initialized = true;
            RevealNow(scrollViewer, state, initial: true);
        }

        return count;
    }

    private static void RequestReveal(ScrollViewer scrollViewer, ScrollState state)
    {
        state.Dirty = true;
        if (state.FrameRequested)
        {
            return;
        }

        var topLevel = TopLevel.GetTopLevel(scrollViewer);
        if (topLevel is null)
        {
            state.Dirty = false;
            RevealNow(scrollViewer, state, initial: false);
            return;
        }

        state.FrameRequested = true;
        topLevel.RequestAnimationFrame(_ =>
        {
            state.FrameRequested = false;
            if (!state.Dirty)
            {
                return;
            }

            state.Dirty = false;
            RevealNow(scrollViewer, state, initial: false);
        });
    }

    private static void RevealNow(ScrollViewer scrollViewer, ScrollState state, bool initial)
    {
        if (MotionBudget.IsCollapsed || scrollViewer.Viewport.Height <= 0d)
        {
            return;
        }

        var (panel, candidates) = Candidates(scrollViewer, state);
        if (panel is null || candidates.Count == 0)
        {
            return;
        }

        // 整层只做一次坐标换算：子项位置用各自的 Bounds.Y 相加，滚动一帧不再逐项 TranslatePoint。
        var panelOrigin = panel.TranslatePoint(new Point(0d, 0d), scrollViewer);
        if (panelOrigin is null)
        {
            return;
        }

        var viewport = scrollViewer.Viewport.Height;
        var offset = MotionBudget.Amplitude(OffsetPixels);
        var baseY = panelOrigin.Value.Y;
        var pending = new List<Control>(candidates.Count);

        foreach (var item in candidates)
        {
            if (item.Bounds.Height <= 0d)
            {
                pending.Add(item);
                continue;
            }

            var top = baseY + item.Bounds.Y;
            var bottom = top + item.Bounds.Height;
            var known = state.LastTop.TryGetValue(item, out var previousTop);
            state.LastTop[item] = top;

            if (Seen.TryGetValue(item, out _))
            {
                continue;
            }

            if (bottom <= 0d)
            {
                // 滚过顶边：登记为看过，回滚不重演。
                MarkSeen(state, item);
                continue;
            }

            if (top >= viewport)
            {
                // 还在视口下方，等它露头。
                pending.Add(item);
                continue;
            }

            if (initial || !known || previousTop < viewport - 1d)
            {
                // 已经在视口里的（首屏、数据晚到、虚拟化跳转）：登记但不补演，避免整屏闪一下。
                MarkSeen(state, item);
                continue;
            }

            MarkSeen(state, item);
            var rest = item.Opacity > 0d ? item.Opacity : 1d;
            var spring = TransformSpring.For(item, SpringSpec.Glide);
            spring.MoveTo(y: offset, opacity: 0d, snap: true);
            spring.MoveTo(y: 0d, opacity: rest);
        }

        state.Candidates = pending;
    }

    /// <summary>取"信息流自己的行"这一层，并缓存下来：滚动一帧只算一次坐标换算。</summary>
    private static (Visual? Panel, List<Control> Items) Candidates(ScrollViewer scrollViewer, ScrollState state)
    {
        if (state.Panel is { } knownPanel)
        {
            // 层没变就只重读这一层的直接子级：一次子级枚举，不做整棵树 BFS。
            var known = knownPanel.GetVisualChildren()
                .OfType<Control>()
                .Where(child => child.IsVisible)
                .Take(MaxTargets)
                .ToList();

            if (known.Count >= 2)
            {
                state.Candidates = known;
                return (knownPanel, known);
            }

            state.Panel = null;
        }

        var start = (Visual?)scrollViewer.Content ?? scrollViewer;
        var queue = new Queue<(Visual Node, int Depth)>();
        queue.Enqueue((start, 0));
        var visited = 0;

        while (queue.Count > 0 && visited < MaxVisited)
        {
            var (node, depth) = queue.Dequeue();
            visited++;

            var children = new List<Control>();
            foreach (var child in node.GetVisualChildren())
            {
                if (child is Control { IsVisible: true } control)
                {
                    children.Add(control);
                }
            }

            if (children.Count >= 2)
            {
                state.Panel = node;
                state.Candidates = children.Count > MaxTargets ? children.GetRange(0, MaxTargets) : children;
                return (node, state.Candidates);
            }

            if (depth >= MaxDepth)
            {
                continue;
            }

            foreach (var child in children)
            {
                queue.Enqueue((child, depth + 1));
            }
        }

        state.Panel = null;
        state.Candidates = null;
        return (null, []);
    }

    private static void MarkSeen(ScrollState state, Control control)
    {
        Seen.GetValue(control, static _ => new object());
        state.LastTop.Remove(control);
    }

    private static IEnumerable<T> Enumerate<T>(Visual root)
        where T : Visual
    {
        var queue = new Queue<(Visual Node, int Depth)>();
        queue.Enqueue((root, 0));
        var visited = 0;

        while (queue.Count > 0 && visited < MaxVisited)
        {
            var (node, depth) = queue.Dequeue();
            visited++;

            if (node is T match)
            {
                yield return match;
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

    private sealed class RootState
    {
        public bool Hooked;

        public long LastScan;
    }

    private sealed class ScrollState
    {
        public bool Initialized;

        public bool Dirty;

        public bool FrameRequested;

        public Dictionary<Control, double> LastTop { get; } = new();

        public Visual? Panel;

        public List<Control>? Candidates;
    }
}