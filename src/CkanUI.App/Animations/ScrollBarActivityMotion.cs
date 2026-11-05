using System;
using System.Linq;
using System.Runtime.CompilerServices;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace CkanUI.App.Animations;

/// <summary>
/// 滚动条活动态：内容在滚的时候把滚动条提到满不透明，停下 <see cref="RestAfterSeconds"/> 秒再落回静止的 0.7。
/// </summary>
/// <remarks>
/// <para>静止态留 0.7 是为了"看得见、拖得到"；可它和内容一起动，拖动时眼睛根本没空去找那条浅灰带。
/// 这里只往滚动条上挂一个 <c>is-scrolling</c> 类，亮度过渡交给样式里已有的
/// <c>DoubleTransition Opacity 0.12s</c>，落回静止态由一次"惰性倒计时"收尾。</para>
/// <para>倒计时不用帧泵：没有任何东西在动的时候帧泵会停，收尾恰恰发生在"什么都没动"的时候，
/// 挂帧泵反而要靠自己造帧。这里用一次性的 <see cref="DispatcherTimer.RunOnce"/>，
/// 连续滚动时它到期只看一眼时间戳、发现还在滚就再等一格，同一个滚动容器始终只有一个待触发的计时器。</para>
/// <para>只改 <c>Classes</c>，不直接写 <c>Opacity</c>——直接赋值会和样式里的过渡打架，
/// 松手后必须"从当前值滑回去"而不是硬切。</para>
/// </remarks>
public static class ScrollBarActivityMotion
{
    /// <summary>滚动期间挂在滚动条上的类名；样式里靠它把不透明度提到 1。</summary>
    public const string ActiveClass = "is-scrolling";

    /// <summary>最后一次滚动之后保持高亮的时长（秒）。</summary>
    public const double RestAfterSeconds = 0.7d;

    /// <summary>倒计时到期时的复查间隔（秒）：还在滚就再等这么久。</summary>
    private const double RecheckSeconds = 0.12d;

    private static readonly ConditionalWeakTable<ScrollViewer, State> Attached = new();

    /// <summary>倒计时调度器；默认交给 <see cref="DispatcherTimer.RunOnce"/>。</summary>
    /// <remarks>
    /// 无头宿主不实现调度器的定时器（<c>RunJobs</c> 只排空作业队列、不推进定时器队列），
    /// 所以门禁里换成手动触发的那一个，"松手 0.7 秒后收回" 才是可确定验证的行为。
    /// </remarks>
    public static Action<TimeSpan, Action> Scheduler { get; set; } =
        static (delay, callback) => DispatcherTimer.RunOnce(callback, delay);

    /// <summary>接管 <paramref name="root"/> 下所有滚动容器的滚动活动态；返回本次新增接管的数量。</summary>
    public static int Attach(Visual? root)
    {
        if (root is null)
        {
            return 0;
        }

        var attached = 0;
        foreach (var scroll in root.GetVisualDescendants().OfType<ScrollViewer>())
        {
            if (Attached.TryGetValue(scroll, out _))
            {
                continue;
            }

            Attached.Add(scroll, new State(scroll));
            attached++;
        }

        if (attached > 0)
        {
            // 页面模板、数据列表、虚拟化容器都可能晚一拍成形：布局后补扫，迟到的滚动条也要接管。
            MotionScan.Subscribe(root, nameof(ScrollBarActivityMotion), () => Attach(root));
        }

        return attached;
    }

    /// <summary>一个滚动容器的活动态：滚动时点亮自己的滚动条，松手后倒计时收回。</summary>
    private sealed class State
    {
        private readonly ScrollViewer _owner;

        private ScrollBar[] _bars = [];

        private DateTime _lastScroll;

        private bool _counting;

        internal State(ScrollViewer owner)
        {
            _owner = owner;
            _owner.ScrollChanged += OnScrollChanged;
        }

        private void OnScrollChanged(object? sender, ScrollChangedEventArgs e)
        {
            // 拖动、滚轮、键盘、代码调用都走这里：谁让内容动了，滚动条就亮一次。
            _lastScroll = DateTime.UtcNow;
            Light();

            if (!_counting)
            {
                _counting = true;
                ScheduleRecheck(RestAfterSeconds);
            }
        }

        private void ScheduleRecheck(double seconds)
        {
            Scheduler(
                TimeSpan.FromSeconds(Math.Max(seconds, RecheckSeconds)),
                () =>
                {
                    if (!_counting)
                    {
                        return;
                    }

                    var remaining = RestAfterSeconds - (DateTime.UtcNow - _lastScroll).TotalSeconds;
                    if (remaining > 0d)
                    {
                        // 还在滚：不清类，只把下一次复查往后挪。
                        ScheduleRecheck(Math.Min(remaining, RecheckSeconds));
                        return;
                    }

                    _counting = false;
                    Dim();
                });
        }

        private void Light()
        {
            foreach (var bar in Bars())
            {
                bar.Classes.Add(ActiveClass);
            }
        }

        private void Dim()
        {
            foreach (var bar in Bars())
            {
                bar.Classes.Remove(ActiveClass);
            }
        }

        /// <summary>只认自己这颗模板里的滚动条；嵌在本容器里的子滚动容器由它们自己点亮。</summary>
        private ScrollBar[] Bars()
        {
            if (_bars.Length > 0)
            {
                return _bars;
            }

            _bars = _owner.GetVisualDescendants()
                .OfType<ScrollBar>()
                .Where(bar => ReferenceEquals(bar.GetVisualAncestors().OfType<ScrollViewer>().FirstOrDefault(), _owner))
                .ToArray();

            return _bars;
        }
    }
}
