using System;
using System.Linq;
using System.Runtime.CompilerServices;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;

namespace CkanUI.App.Animations;

/// <summary>
/// 键盘/焦点导航把目标带进视野时，滚动条不再瞬跳，而是弹簧滑过去。
/// </summary>
/// <remarks>
/// <para>列表默认走 <c>ScrollIntoView</c>：一按方向键，内容整屏换位，眼睛要重新定位。
/// 这里只改"怎么到那儿"——目标位置仍然由可见性算法算，弹簧只负责把它滑过去，并且可以被下一次按键打断
/// （<see cref="SpringChannel.Retarget"/> 保留当前位置与速度）。</para>
/// <para>滚动偏移不是 <c>RenderTransform</c>，没法交给 <see cref="TransformSpring"/>；但同一条帧泵
/// （<see cref="MotionFramePump"/>）能推任何 <see cref="IMotionTicker"/>，所以这里自己实现一个通道。</para>
/// </remarks>
public static class SmoothScrollMotion
{
    /// <summary>目标与视口边缘之间留的呼吸距离。</summary>
    public const double EdgeMarginPixels = 28d;

    /// <summary>滑到目标附近就不再抖：小于这个距离直接落位。</summary>
    private const double SnapPixels = 0.5d;

    private static readonly ConditionalWeakTable<Control, object> Hosts = new();
    private static readonly ConditionalWeakTable<ScrollViewer, Track> Tracks = new();

    /// <summary>接管 <paramref name="root"/> 下所有获得焦点的控件以及滚动容器的平滑滚动与刹车。</summary>
    public static int Attach(Visual? root)
    {
        if (root is not Control host)
        {
            return 0;
        }

        MotionScan.Subscribe(root, nameof(SmoothScrollMotion), () => Attach(root));

        var attachedCount = 0;
        if (!Hosts.TryGetValue(host, out _))
        {
            Hosts.Add(host, new object());
            host.AddHandler(InputElement.GotFocusEvent, OnGotFocus, RoutingStrategies.Bubble, handledEventsToo: true);
            host.AddHandler(InputElement.PointerWheelChangedEvent, OnPointerWheelChanged, RoutingStrategies.Tunnel, handledEventsToo: false);
            host.AddHandler(InputElement.KeyDownEvent, OnKeyDown, RoutingStrategies.Tunnel, handledEventsToo: false);
            attachedCount++;
        }

        return attachedCount;
    }

    /// <summary>
    /// 把一个控件滚进视野；返回是否真的开始了一段滚动（已经可见、关闭动效时为 false）。
    /// 焦点事件之外也能直接调用，便于门禁与命令式定位。
    /// </summary>
    public static bool EnsureVisible(Control target)
    {
        ArgumentNullException.ThrowIfNull(target);

        var scroll = target.GetVisualAncestors().OfType<ScrollViewer>().FirstOrDefault();
        if (scroll is null || scroll.Viewport.Height <= 0d)
        {
            return false;
        }

        var top = target.TranslatePoint(new Point(0d, 0d), scroll)?.Y;
        if (top is not { } topEdge || double.IsNaN(topEdge))
        {
            return false;
        }

        var bottom = topEdge + target.Bounds.Height;
        var viewport = scroll.Viewport.Height;
        var margin = Math.Min(EdgeMarginPixels, viewport / 4d);
        var current = scroll.Offset.Y;
        var desired = CalculateDesiredOffset(topEdge, bottom, viewport, margin, current, scroll.Extent.Height);

        if (Math.Abs(desired - current) < SnapPixels)
        {
            return false;
        }

        if (MotionBudget.IsCollapsed)
        {
            scroll.Offset = new Vector(scroll.Offset.X, desired);
            return false;
        }

        var track = Tracks.GetValue(scroll, owner => new Track(owner));
        track.Retarget(desired);
        return true;
    }

    /// <summary>计算滚动条把目标保持在可见安全区时的目标 Y 偏移量。</summary>
    public static double CalculateDesiredOffset(double topEdge, double bottomEdge, double viewportHeight, double margin, double currentOffset, double extentHeight)
    {
        var desired = currentOffset;
        if (topEdge < margin)
        {
            desired = currentOffset + (topEdge - margin);
        }
        else if (bottomEdge > viewportHeight - margin)
        {
            desired = currentOffset + (bottomEdge - (viewportHeight - margin));
        }

        var max = Math.Max(0d, extentHeight - viewportHeight);
        return Math.Clamp(desired, 0d, max);
    }

    /// <summary>单次滚轮滚动的基准行步进（像素）。</summary>
    public const double WheelStepPixels = 72d;

    /// <summary>滚轮驱动的快速平滑滚动：快速启动 + 弹簧刹车停靠。</summary>
    public static bool ScrollByDelta(ScrollViewer scroll, double deltaY)
    {
        ArgumentNullException.ThrowIfNull(scroll);

        if (scroll.Viewport.Height <= 0d || Math.Abs(deltaY) < 1e-3)
        {
            return false;
        }

        var max = Math.Max(0d, scroll.Extent.Height - scroll.Viewport.Height);
        if (max <= 0d)
        {
            return false;
        }

        var track = Tracks.GetValue(scroll, owner => new Track(owner));
        var targetY = Math.Clamp(track.TargetY + deltaY, 0d, max);

        if (MotionBudget.IsCollapsed)
        {
            track.Snap(targetY);
            return false;
        }

        track.AccumulateTarget(targetY, deltaY);
        return true;
    }

    /// <summary>
    /// 从事件源解析适用的 ScrollViewer：优先向上遍历祖先链，其次自身，再次向下查找子 ScrollViewer（适用于光标在 DataGrid、ListBox 或表头上的情况）。
    /// </summary>
    public static ScrollViewer? FindScrollViewer(Visual? source, double deltaY = 0d)
    {
        if (source is null)
        {
            return null;
        }

        // 1. 优先沿着视觉树向上寻找可滚动的祖先 ScrollViewer
        var current = source;
        while (current is not null)
        {
            if (current is ScrollViewer sv && CanScroll(sv, deltaY))
            {
                return sv;
            }
            current = current.GetVisualParent();
        }

        // 2. 如果源控件本身就是 ScrollViewer 且可滚
        if (source is ScrollViewer directSv && CanScroll(directSv, deltaY))
        {
            return directSv;
        }

        // 3. 如果源控件内部包含可滚动的子 ScrollViewer（例如光标落在 DataGrid/ListBox 自身或表头上）
        foreach (var desc in source.GetVisualDescendants().OfType<ScrollViewer>())
        {
            if (CanScroll(desc, deltaY))
            {
                return desc;
            }
        }

        // 4. 兜底返回最近的 ScrollViewer
        return source.GetVisualAncestors().OfType<ScrollViewer>().FirstOrDefault()
            ?? (source as ScrollViewer)
            ?? source.GetVisualDescendants().OfType<ScrollViewer>().FirstOrDefault();
    }

    private static bool CanScroll(ScrollViewer sv, double deltaY)
    {
        if (sv.Viewport.Height <= 0d)
        {
            return false;
        }

        var max = Math.Max(0d, sv.Extent.Height - sv.Viewport.Height);
        if (max <= 0d)
        {
            return false;
        }

        if (deltaY < -1e-3 && sv.Offset.Y <= SnapPixels)
        {
            return false; // 顶部已到极限
        }

        if (deltaY > 1e-3 && sv.Offset.Y >= max - SnapPixels)
        {
            return false; // 底部已到极限
        }

        return true;
    }

    private static void OnPointerWheelChanged(object? sender, PointerWheelEventArgs e)
    {
        if (e.Handled || MotionBudget.IsCollapsed)
        {
            return;
        }

        var deltaY = -e.Delta.Y * WheelStepPixels;
        if (Math.Abs(deltaY) < 1e-3)
        {
            return;
        }

        var scroll = FindScrollViewer(e.Source as Visual, deltaY);
        if (scroll is null || scroll.Viewport.Height <= 0d)
        {
            return;
        }

        var max = Math.Max(0d, scroll.Extent.Height - scroll.Viewport.Height);
        if (max <= 0d)
        {
            return;
        }

        var currentTarget = Tracks.TryGetValue(scroll, out var existingTrack) ? existingTrack.TargetY : scroll.Offset.Y;
        var nextTarget = Math.Clamp(currentTarget + deltaY, 0d, max);

        // 如果已经在边界且还往边界外滚，交给系统默认处理（或者不拦截）
        if (Math.Abs(nextTarget - scroll.Offset.Y) < SnapPixels && Math.Abs(currentTarget - scroll.Offset.Y) < SnapPixels)
        {
            return;
        }

        if (ScrollByDelta(scroll, deltaY))
        {
            e.Handled = true;
        }
    }

    private static void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Handled || MotionBudget.IsCollapsed)
        {
            return;
        }

        if (e.Source is TextBox or AutoCompleteBox)
        {
            return;
        }

        if (e.Key == Key.PageDown)
        {
            var scroll = FindScrollViewer(e.Source as Visual, 1d);
            if (scroll is not null && scroll.Viewport.Height > 0d)
            {
                var delta = scroll.Viewport.Height * 0.85d;
                if (ScrollByDelta(scroll, delta))
                {
                    e.Handled = true;
                }
            }
        }
        else if (e.Key == Key.PageUp)
        {
            var scroll = FindScrollViewer(e.Source as Visual, -1d);
            if (scroll is not null && scroll.Viewport.Height > 0d)
            {
                var delta = -scroll.Viewport.Height * 0.85d;
                if (ScrollByDelta(scroll, delta))
                {
                    e.Handled = true;
                }
            }
        }
    }

    private static void OnGotFocus(object? sender, FocusChangedEventArgs e)
    {
        if (e.NewFocusedElement is Control control)
        {
            EnsureVisible(control);
        }
    }

    /// <summary>一个滚动容器的偏移通道：只有 Y 参与弹簧，X 原样保留（横向流由页面自己处理）。</summary>
    private sealed class Track : IMotionTicker
    {
        private readonly ScrollViewer _owner;
        private readonly SpringChannel _y = new(0d, SpringSpec.Glide, SnapPixels, 6d);
        private double _x;
        private bool _isSubscribed;

        internal Track(ScrollViewer owner)
        {
            _owner = owner;
            _y.SnapTo(_owner.Offset.Y);
        }

        internal double TargetY => _y.Target;

        internal void Snap(double targetY)
        {
            _x = _owner.Offset.X;
            _y.SnapTo(targetY);
            _owner.Offset = new Vector(_x, targetY);
        }

        internal void Retarget(double targetY)
        {
            _x = _owner.Offset.X;
            _y.SnapTo(_owner.Offset.Y);
            _y.Retarget(targetY);
            EnsurePump();
        }

        internal void AccumulateTarget(double targetY, double deltaY)
        {
            _x = _owner.Offset.X;

            // 确保起始点与当前真实偏移同步（如果之前处于静止状态）
            if (!_isSubscribed)
            {
                _y.SnapTo(_owner.Offset.Y);
            }

            _y.Retarget(targetY);

            // 快速启动冲量：如果当前速度较低，注入与滚动方向同向的启动冲量（相当于初始加速度），实现起步灵敏
            if (Math.Abs(_y.Velocity) < 100d)
            {
                var impulse = Math.Clamp(deltaY * 0.15d, -30d, 30d);
                _y.Kick(impulse);
            }

            EnsurePump();
        }

        private void EnsurePump()
        {
            if (TopLevel.GetTopLevel(_owner) is { } top)
            {
                if (!_isSubscribed)
                {
                    _isSubscribed = true;
                    MotionFramePump.Subscribe(top, this);
                }
            }
            else
            {
                _owner.Offset = new Vector(_x, _y.Target);
            }
        }

        public bool Advance(double seconds)
        {
            var moving = _y.Advance(seconds);
            _owner.Offset = new Vector(_x, moving ? _y.Value : _y.Target);
            if (!moving)
            {
                _isSubscribed = false;
            }
            return moving;
        }
    }
}
