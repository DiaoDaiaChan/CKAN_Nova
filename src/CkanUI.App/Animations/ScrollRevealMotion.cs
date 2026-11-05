using System;
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
/// <para>横向信息流（标签带、横向卡片条）同样覆盖：内容比视口宽且宽度是主轴时改判左右边，动的是 <c>translateX</c>。</para>
/// <para>只动一个方向的平移与不透明度，不参与布局；关闭档整层不挂。</para>
/// </remarks>
public static class ScrollRevealMotion
{
    /// <summary>起点下移距离（完整档，像素）。</summary>
    public const double OffsetPixels = 24d;

    /// <summary>起点下移距离（精简档，像素）。</summary>
    public const double ReducedOffsetPixels = 4d;

    private const int MaxDepth = 10;

    // 找滚动容器的遍历比"挑行层"深得多：页面里 ScrollViewer 常嵌在模板/列表里第 11 层，
    // 用挑行层的深度限制会直接扫不到（ships 页就是这样整页不演）。
    private const int MaxScrollDepth = 20;

    private const int MaxScrollVisited = 12000;

    private const int MaxTargets = 400;

    private const int MaxVisited = 1600;

    /// <summary>一次评估内滚动位移超过视口的这个倍数，按"整片跳转"处理，不做逐行淡入。</summary>
    private const double JumpViewportRatio = 1.5d;

    /// <summary>一次评估内"没见过"的行达到这个数量、且过半时，同样按整片跳转处理（虚拟化整屏换行）。</summary>
    private const int JumpFreshRows = 3;

    private static readonly ConditionalWeakTable<Visual, RootState> Roots = new();

    private static readonly ConditionalWeakTable<ScrollViewer, ScrollState> Attached = new();

    private static readonly ConditionalWeakTable<Control, object> Seen = new();

    // 虚拟化列表会回收容器：行控件还是同一个，装的数据已经换了。按数据对象记"演过"，
    // 否则滚出来的每一行都被当成"见过"，长列表滚不动。
    private static readonly ConditionalWeakTable<object, object> SeenData = new();

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

        // 滚动条的"正在滚"高亮跟着同一批根走：滚动入场的接管点，就是需要活动态提示的接管点。
        ScrollBarActivityMotion.Attach(root);

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

            // 只有在真正拿到"信息流行"这一层之后才算初始化：控件树/容器还没成形时先放过，
            // 否则首屏的行会被当成"已经见过"，之后滚动永远不演。
            state.Initialized = RevealNow(scrollViewer, state, initial: true);
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

    /// <summary>评估一次；只有找到了可演的行层才返回 <see langword="true"/>。</summary>
    private static bool RevealNow(ScrollViewer scrollViewer, ScrollState state, bool initial)
    {
        if (MotionBudget.IsCollapsed)
        {
            return false;
        }

        // 横向信息流（标签带、横向卡片条）走同一条判定，只是把"上/下边"换成"左/右边"。
        var horizontal = IsHorizontal(scrollViewer);
        var viewport = horizontal ? scrollViewer.Viewport.Width : scrollViewer.Viewport.Height;
        if (viewport <= 0d)
        {
            return false;
        }

        var (panel, candidates) = Candidates(scrollViewer, state);
        if (panel is null || candidates.Count == 0)
        {
            return false;
        }

        // 整层只做一次坐标换算：子项位置用各自的 Bounds.Y 相加，滚动一帧不再逐项 TranslatePoint。
        var panelOrigin = panel.TranslatePoint(new Point(0d, 0d), scrollViewer);
        if (panelOrigin is null)
        {
            return false;
        }

        // 拖动滚动条 / 翻页 / 程序化跳转：视口整片换了一批行，不是"从底边露头"。
        // 逐行从 Opacity=0 淡入会让整屏在拖动期间一直停在半透明，用户看到的就是"内容消失"。
        var axisOffset = horizontal ? scrollViewer.Offset.X : scrollViewer.Offset.Y;
        var previousOffset = state.HasOffset ? state.LastOffset : axisOffset;
        state.LastOffset = axisOffset;
        state.HasOffset = true;
        var offsetJumped = Math.Abs(axisOffset - previousOffset) > viewport * JumpViewportRatio;

        var offset = MotionBudget.Amplitude(OffsetPixels);
        var baseY = horizontal ? panelOrigin.Value.X : panelOrigin.Value.Y;
        var pending = new List<Control>(candidates.Count);
        var entering = new List<(Control Item, bool Horizontal)>();
        var visible = 0;
        var fresh = 0;

        foreach (var item in candidates)
        {
            var extent = horizontal ? item.Bounds.Width : item.Bounds.Height;
            if (extent <= 0d)
            {
                pending.Add(item);
                continue;
            }

            var top = baseY + (horizontal ? item.Bounds.X : item.Bounds.Y);
            var bottom = top + extent;
            var data = KeyOf(item);
            var known = state.LastTop.TryGetValue(item, out var last) && ReferenceEquals(last.Data, data);
            var previousTop = last.Top;
            state.LastTop[item] = (data, top);

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

            visible++;
            var alreadySeen = IsSeen(item);
            if (!known && !alreadySeen)
            {
                // 从没量过、也没演过 = 这一帧刚被虚拟化实例化出来。
                fresh++;
            }

            if (alreadySeen)
            {
                continue;
            }

            if (initial)
            {
                // 首屏：登记但不补演，避免和页面入场叠成两次淡入。
                MarkSeen(state, item);
                continue;
            }

            if (known && previousTop < viewport - 1d)
            {
                // 早就在视口里的（数据晚到、静态内容）：登记但不补演，避免整屏闪一下。
                MarkSeen(state, item);
                continue;
            }

            // 其余都是刚露头：上一帧还在底边外，或这一帧才被虚拟化实例化出来。
            entering.Add((item, horizontal));
        }

        // 整片跳转（拖滚动条、翻页、虚拟化整屏换行）：这些行是"换了一批"，不是"从底边长出来"。
        // 还给它们逐个从 Opacity=0 淡入，拖动期间每一帧都会把可见行全部刷成半透明 —— 看起来就是内容丢失。
        var jumped = offsetJumped || (fresh >= JumpFreshRows && fresh * 2 > visible);
        foreach (var (item, isHorizontal) in entering)
        {
            MarkSeen(state, item);
            if (jumped)
            {
                continue;
            }

            var rest = item.Opacity > 0d ? item.Opacity : 1d;
            var spring = TransformSpring.For(item, SpringSpec.Glide);
            if (isHorizontal)
            {
                spring.MoveTo(x: offset, opacity: 0d, snap: true);
                spring.MoveTo(x: 0d, opacity: rest);
            }
            else
            {
                spring.MoveTo(y: offset, opacity: 0d, snap: true);
                spring.MoveTo(y: 0d, opacity: rest);
            }
        }

        state.Candidates = pending;
        return true;
    }

    /// <summary>取"信息流自己的行"这一层，并缓存下来：滚动一帧只算一次坐标换算。</summary>
    /// <remarks>
    /// 取"可见子级最多"的那一层：设置页那种大分组下面还套着卡片列表，只挑第一层只会整组一起动，
    /// 观感像两片大块在挪；挑子级最多的一层才是真正的信息流。同数量时保留先遇到的（更浅）那层，
    /// 避免钻进某一行内部去演它的三个小格子。
    /// </remarks>
    private static (Visual? Panel, List<Control> Items) Candidates(ScrollViewer scrollViewer, ScrollState state)
    {
        var start = (Visual?)scrollViewer.Content ?? scrollViewer;
        var queue = new Queue<(Visual Node, int Depth)>();
        queue.Enqueue((start, 0));
        var visited = 0;
        Visual? bestNode = null;
        List<Control>? best = null;

        while (queue.Count > 0 && visited < MaxVisited)
        {
            var (node, depth) = queue.Dequeue();
            visited++;

            var children = VisibleChildren(node);
            if (children.Count >= 2 && (best is null || children.Count > best.Count))
            {
                bestNode = node;
                best = children;

                if (best.Count >= MaxTargets)
                {
                    break;
                }
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

        if (bestNode is null || best is null)
        {
            state.Panel = null;
            state.Candidates = null;
            return (null, []);
        }

        state.Panel = bestNode;
        state.Candidates = best;
        return (bestNode, best);
    }

    /// <summary>内容比视口宽、且宽度是主轴时按横向信息流处理。</summary>
    private static bool IsHorizontal(ScrollViewer scrollViewer) =>
        scrollViewer.Viewport.Width > 0d
        && scrollViewer.Extent.Width > scrollViewer.Viewport.Width + 1d
        && scrollViewer.Extent.Width > scrollViewer.Extent.Height;

    private static List<Control> VisibleChildren(Visual node)
    {
        var children = new List<Control>();
        foreach (var child in node.GetVisualChildren())
        {
            if (child is Control { IsVisible: true } control)
            {
                children.Add(control);
                if (children.Count >= MaxTargets)
                {
                    break;
                }
            }
        }

        return children;
    }

    private static void MarkSeen(ScrollState state, Control control)
    {
        if (KeyOf(control) is { } data)
        {
            SeenData.GetValue(data, static _ => new object());
        }
        else
        {
            Seen.GetValue(control, static _ => new object());
        }

        state.LastTop.Remove(control);
    }

    private static bool IsSeen(Control control) =>
        KeyOf(control) is { } data ? SeenData.TryGetValue(data, out _) : Seen.TryGetValue(control, out _);

    /// <summary>虚拟化容器按数据对象去重；没有数据上下文（静态行）就按控件本身。</summary>
    private static object? KeyOf(Control control) => control.DataContext is { } data && data.GetType().IsClass ? data : null;

    private static IEnumerable<T> Enumerate<T>(Visual root)
        where T : Visual
    {
        var queue = new Queue<(Visual Node, int Depth)>();
        queue.Enqueue((root, 0));
        var visited = 0;

        while (queue.Count > 0 && visited < MaxScrollVisited)
        {
            var (node, depth) = queue.Dequeue();
            visited++;

            if (node is T match)
            {
                yield return match;
            }

            if (depth >= MaxScrollDepth)
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

        public bool HasOffset;

        public double LastOffset;

        public Dictionary<Control, (object? Data, double Top)> LastTop { get; } = new();

        public Visual? Panel;

        public List<Control>? Candidates;
    }
}