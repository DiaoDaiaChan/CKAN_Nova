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

    /// <summary>接管 <paramref name="root"/> 下所有获得焦点的控件的滚动定位。</summary>
    public static int Attach(Visual? root)
    {
        if (root is not Control host)
        {
            return 0;
        }

        MotionScan.Subscribe(root, nameof(SmoothScrollMotion), () => Attach(root));

        if (!Hosts.TryGetValue(host, out _))
        {
            Hosts.Add(host, new object());
            host.AddHandler(InputElement.GotFocusEvent, OnGotFocus, RoutingStrategies.Bubble, handledEventsToo: true);
            return 1;
        }

        return 0;
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
        var desired = current;

        if (topEdge < margin)
        {
            desired = current + (topEdge - margin);
        }
        else if (bottom > viewport - margin)
        {
            desired = current + (bottom - (viewport - margin));
        }

        var max = Math.Max(0d, scroll.Extent.Height - viewport);
        desired = Math.Clamp(desired, 0d, max);

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

        internal Track(ScrollViewer owner) => _owner = owner;

        internal void Retarget(double targetY)
        {
            _x = _owner.Offset.X;
            _y.SnapTo(_owner.Offset.Y);
            _y.Retarget(targetY);
            if (TopLevel.GetTopLevel(_owner) is { } top)
            {
                MotionFramePump.Subscribe(top, this);
            }
            else
            {
                _owner.Offset = new Vector(_x, targetY);
            }
        }

        public bool Advance(double seconds)
        {
            var moving = _y.Advance(seconds);
            _owner.Offset = new Vector(_x, moving ? _y.Value : _y.Target);
            return moving;
        }
    }
}
