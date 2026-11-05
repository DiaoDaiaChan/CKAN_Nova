using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Runtime.CompilerServices;

using Avalonia;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;

using CkanUI.Ui.Theming;

namespace CkanUI.Ui.Tables;

/// <summary>
/// 表格重排动画：排序、筛选、换源之后行换了位置，按「从旧位置滑到新位置」重新落位。
/// </summary>
/// <remarks>
/// <para>做法是 FLIP：数据源发 Reset 的那一刻容器还在旧位置，先把「行 → 顶边」记下来；
/// 布局跑完再按旧顶边与新顶边的差值给一次位移补偿，然后逐帧放回 0。
/// 行因此是滑过去的，不是整块啪地换掉。</para>
/// <para>容器复用（虚拟化回收、排序后同一容器换了数据）靠「数据项」对位：
/// 捕获与落位都用 <see cref="ItemsControl.ItemFromContainer"/> 取到的对象配对，
/// 容器身份变了也不会配错。</para>
/// <para>时长从 <see cref="CkanMotionTokens.Base"/> 读：动效强度关到「瞬时」时 token 为 0，
/// 这里直接不演（不做关不掉的动画）。</para>
/// </remarks>
public sealed class TableReorderMotion
{
    /// <summary>是否给这张表开重排动画。</summary>
    public static readonly AttachedProperty<bool> IsEnabledProperty =
        AvaloniaProperty.RegisterAttached<TableReorderMotion, Control, bool>("IsEnabled");

    /// <summary>没有旧位置的行（筛选后新露出的行）从下方浮上来的距离。</summary>
    public const double FullOffsetPixels = 12d;

    /// <summary>精简档的入场位移。</summary>
    public const double ReducedOffsetPixels = 4d;

    /// <summary>相邻行的错峰间隔（毫秒）。</summary>
    public const int StepMilliseconds = 18;

    private const int MaxItems = 24;

    private const double DurationFloorMilliseconds = 60d;

    private const double DurationCapMilliseconds = 320d;

    private const double ReducedDurationMilliseconds = 150d;

    private const double MinOffset = 0.5d;

    private static readonly ConditionalWeakTable<Control, Track> Tracks = new();

    private static readonly ConditionalWeakTable<TopLevel, Pump> Pumps = new();

    private static readonly CubicEaseOut Easing = new();

    private TableReorderMotion()
    {
    }

    /// <summary>读取是否启用。</summary>
    public static bool GetIsEnabled(Control control) => control.GetValue(IsEnabledProperty);

    /// <summary>设置是否启用。</summary>
    public static void SetIsEnabled(Control control, bool value) => control.SetValue(IsEnabledProperty, value);

    /// <summary>数据源变更之后调用：先记下当前行位置，等这一轮布局跑完再让它们滑到新位置。</summary>
    public static void Play(ItemsControl list)
    {
        ArgumentNullException.ThrowIfNull(list);
        if (!GetIsEnabled(list))
        {
            return;
        }

        Play(list, list.GetRealizedContainers(), list.ItemFromContainer);
    }

    /// <summary>
    /// 给不继承 <see cref="ItemsControl"/> 的表格用（<see cref="Control"/> 版）：
    /// 容器与「容器对应的数据项」由调用方给，DataGrid 的行容器是 <c>DataGridRow</c>，不在共享层可引用的包里。
    /// </summary>
    public static void Play(Control host, IEnumerable<Control> containers, Func<Control, object?> itemOf)
    {
        ArgumentNullException.ThrowIfNull(host);
        ArgumentNullException.ThrowIfNull(containers);
        ArgumentNullException.ThrowIfNull(itemOf);

        if (!GetIsEnabled(host))
        {
            return;
        }

        var before = Capture(host, containers, itemOf);
        if (before is null)
        {
            return;
        }

        // 集合刚变、容器还停在旧位置：等这一轮布局把新位置摆好，再算各自的位移。
        // 布局完成的信号有两条：Layoutable.LayoutUpdated（真实渲染时最准）与
        // Loaded 优先级的派发（无头测试里只跑 Dispatcher 时靠它兜底）。
        var layoutable = host as Layoutable;
        var settled = false;
        var settling = false;

        void Detach()
        {
            if (layoutable is not null)
            {
                layoutable.LayoutUpdated -= OnLayoutUpdated;
            }
        }

        void Settle(bool layoutDone)
        {
            // 重入即递归：UpdateLayout 会同步发 LayoutUpdated，回调里再 UpdateLayout 会一直套下去。
            if (settled || settling)
            {
                return;
            }

            settling = true;
            try
            {
                // 派发回调用在布局之前跑完时行还站在原地，差值是 0：先把这一轮布局逼出来。
                if (!layoutDone)
                {
                    layoutable?.UpdateLayout();
                }

                if (Reflow(host, before, containers, itemOf) > 0)
                {
                    settled = true;
                    Detach();
                }
            }
            finally
            {
                settling = false;
            }
        }

        void OnLayoutUpdated(object? sender, EventArgs args) => Settle(layoutDone: true);

        if (layoutable is not null)
        {
            layoutable.LayoutUpdated += OnLayoutUpdated;
        }

        // 容器可能还没实化（虚拟化的行要等下一轮），给几次机会再放弃。
        Dispatcher.UIThread.Post(() => Settle(layoutDone: false), DispatcherPriority.Loaded);
        Dispatcher.UIThread.Post(() => Settle(layoutDone: false), DispatcherPriority.Background);
        Dispatcher.UIThread.Post(
            () =>
            {
                Settle(layoutDone: false);
                Detach();
            },
            DispatcherPriority.ContextIdle);
    }

    /// <summary>立刻按快照落位。</summary>
    /// <param name="list">目标列表。</param>
    /// <param name="before">旧位置快照（数据项 → 顶边）。</param>
    /// <returns>本次真正演出的行数。</returns>
    public static int Reflow(ItemsControl list, IReadOnlyDictionary<object, double> before)
        => Reflow(list, before, list.GetRealizedContainers(), list.ItemFromContainer);

    private static int Reflow(Control list, IReadOnlyDictionary<object, double> before, IEnumerable<Control> containers, Func<Control, object?> itemOf)
    {
        ArgumentNullException.ThrowIfNull(list);
        ArgumentNullException.ThrowIfNull(before);

        if (DurationOf(list) is not { } duration || !list.IsEffectivelyVisible)
        {
            return 0;
        }

        // 容器要在这一刻重新取：Reset 会把旧容器回收，早先抓到的枚举器可能已经空了。
        if (list is ItemsControl itemsControl)
        {
            containers = itemsControl.GetRealizedContainers();
            itemOf = itemsControl.ItemFromContainer;
        }

        var offset = duration.TotalMilliseconds < ReducedDurationMilliseconds ? ReducedOffsetPixels : FullOffsetPixels;
        var rows = new List<(Control Container, double Top, double Shift)>();

        foreach (var container in containers)
        {
            if (!container.IsVisible || itemOf(container) is not { } item)
            {
                continue;
            }

            var top = TopOf(container, list);
            if (double.IsNaN(top))
            {
                continue;
            }

            // 有旧位置的行滑到新位置；新露出的行从下方浮上来。
            var shift = before.TryGetValue(item, out var oldTop) ? oldTop - top : offset;
            if (Math.Abs(shift) < MinOffset)
            {
                continue;
            }

            rows.Add((container, top, shift));
        }

        if (rows.Count == 0)
        {
            return 0;
        }

        // 自上而下错峰：视觉上是一列一行地压过去，而不是所有行同时抖。
        rows.Sort(static (left, right) => left.Top.CompareTo(right.Top));

        var step = Math.Min(StepMilliseconds / 1000d, duration.TotalSeconds / 3d);
        var played = 0;

        for (var i = 0; i < rows.Count && played < MaxItems; i++)
        {
            var (container, _, shift) = rows[i];
            Start(container, shift, duration.TotalSeconds, i * step);
            played++;
        }

        return played;
    }

    private static Dictionary<object, double>? Capture(Control list, IEnumerable<Control> containers, Func<Control, object?> itemOf)
    {
        if (DurationOf(list) is null)
        {
            return null;
        }

        var before = new Dictionary<object, double>();
        foreach (var container in containers)
        {
            if (!container.IsVisible || itemOf(container) is not { } item)
            {
                continue;
            }

            var top = TopOf(container, list);
            if (!double.IsNaN(top))
            {
                before[item] = top;
            }
        }

        return before.Count == 0 ? null : before;
    }

    private static void Start(Control container, double shift, double duration, double delay)
    {
        var track = Tracks.GetValue(container, static target => new Track(target));
        track.Reset();
        track.From = shift;
        track.Duration = duration;
        track.Delay = delay;
        track.Attach();

        // 立刻先站到旧位置上，否则第一帧之前会闪一下新位置，再跳回去重新滑。
        track.Transform.Y = track.From;
        track.Running = true;

        if (TopLevel.GetTopLevel(container) is { } topLevel)
        {
            Pumps.GetValue(topLevel, static top => new Pump(top)).Add(track);
        }
        else
        {
            // 不在可视树里就没有帧可排：直接落到终点，避免留下半截位移。
            track.Complete();
        }
    }

    private static double TopOf(Control container, Visual host)
    {
        var point = container.TranslatePoint(default, host);
        return point?.Y ?? double.NaN;
    }

    private static TimeSpan? DurationOf(Control list)
    {
        if (!list.TryFindResource(CkanMotionTokens.Base, out var value)
            || value is not TimeSpan span
            || span <= TimeSpan.Zero)
        {
            return null;
        }

        // 表格重排比页面入场安静：取常规时长的一半，并且不许超过 token 本身。
        var milliseconds = Math.Clamp(span.TotalMilliseconds * 0.5, DurationFloorMilliseconds, DurationCapMilliseconds);
        return TimeSpan.FromMilliseconds(Math.Min(milliseconds, span.TotalMilliseconds));
    }

    /// <summary>一行的位移轨道：可能独占 RenderTransform，也可能挂在别人已建的变换组末尾。</summary>
    private sealed class Track
    {
        private readonly Control _container;

        private ITransform? _previous;

        public Track(Control container) => _container = container;

        public readonly TranslateTransform Transform = new();

        public double From;

        public double Duration;

        public double Delay;

        public double Elapsed;

        public bool Running;

        /// <summary>把位移变换接进容器的 RenderTransform。</summary>
        public void Attach()
        {
            _previous = _container.RenderTransform;

            if (_previous is null)
            {
                _container.RenderTransform = Transform;
                return;
            }

            // 别的动效（悬停抬升、按压缩放）已经把 RenderTransform 占了：
            // 挂到变换组末尾，别把人家的变换顶掉。
            if (_previous is TransformGroup group)
            {
                if (!group.Children.Contains(Transform))
                {
                    group.Children.Add(Transform);
                }

                return;
            }

            if (_previous is not Transform previous)
            {
                // 不是我们能拆解的变换：让它让位，动画结束再原样还回去。
                _previous = _container.RenderTransform;
                _container.RenderTransform = Transform;
                return;
            }

            var combined = new TransformGroup();
            combined.Children.Add(previous);
            combined.Children.Add(Transform);
            _container.RenderTransform = combined;
        }

        /// <summary>回到起点并清空计时。</summary>
        public void Reset()
        {
            Running = false;
            Elapsed = 0d;
            Transform.Y = 0d;
        }

        /// <summary>落到终点并撤掉自己挂上去的变换。</summary>
        public void Complete()
        {
            Running = false;
            Transform.Y = 0d;

            if (_container.RenderTransform is TransformGroup group && group.Children.Contains(Transform))
            {
                group.Children.Remove(Transform);
                if (group.Children.Count == 1)
                {
                    _container.RenderTransform = group.Children[0];
                }
            }
            else if (ReferenceEquals(_container.RenderTransform, Transform))
            {
                // 原来没有变换就还原成没有；原来有别人的变换就还回去。
                _container.RenderTransform = _previous;
            }

            _previous = null;
        }
    }

    /// <summary>每个 TopLevel 一台帧泵：没有活跃行就停止排帧。</summary>
    private sealed class Pump
    {
        private readonly TopLevel _topLevel;
        private readonly List<Track> _tracks = [];
        private bool _requested;
        private TimeSpan _last;

        public Pump(TopLevel topLevel) => _topLevel = topLevel;

        public void Add(Track track)
        {
            _tracks.Add(track);
            Request();
        }

        private void Request()
        {
            if (_requested || _tracks.Count == 0)
            {
                return;
            }

            _requested = true;
            _last = TimeSpan.Zero;
            _topLevel.RequestAnimationFrame(OnFrame);
        }

        private void OnFrame(TimeSpan timestamp)
        {
            _requested = false;
            if (_tracks.Count == 0)
            {
                return;
            }

            var delta = _last == TimeSpan.Zero ? 1d / 60d : (timestamp - _last).TotalSeconds;
            _last = timestamp;
            if (!(delta > 0d))
            {
                delta = 1d / 60d;
            }

            // 掉帧时封顶 1/30s，否则一次长停顿会把位移按真实时间推过去，看起来像瞬移。
            delta = Math.Min(delta, 1d / 30d);

            for (var i = _tracks.Count - 1; i >= 0; i--)
            {
                var track = _tracks[i];
                if (!track.Running)
                {
                    _tracks.RemoveAt(i);
                    continue;
                }

                track.Elapsed += delta;
                if (track.Elapsed < track.Delay)
                {
                    continue;
                }

                var progress = track.Duration <= 0d
                    ? 1d
                    : Math.Clamp((track.Elapsed - track.Delay) / track.Duration, 0d, 1d);
                track.Transform.Y = track.From * (1d - Easing.Ease(progress));

                if (progress < 1d)
                {
                    continue;
                }

                track.Complete();
                _tracks.RemoveAt(i);
            }

            Request();
        }
    }

}
