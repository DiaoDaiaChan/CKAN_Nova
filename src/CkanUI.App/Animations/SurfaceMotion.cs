using System.Collections.Generic;
using System.Collections.Specialized;
using System.Runtime.CompilerServices;

using Avalonia;
using Avalonia.Controls;
using Avalonia.VisualTree;

namespace CkanUI.App.Animations;

/// <summary>
/// 让"整块浮层"（底部日志抽屉、未保存浮条）的显隐由弹簧帧驱动，而不是 <c>Transitions</c>。
/// </summary>
/// <remarks>
/// <para>样式里的 <c>TransformOperationsTransition</c> + <c>SpringEasing</c> 只有一个目标值通道：收起途中又点开，
/// 过渡会从 t=0 重算、速度归零，观感是"顿一下再走"（§5.9.8）。抽屉与浮条恰恰是最容易被连点的两处，
/// 所以这里接管它们的 RenderTransform / Opacity，弹簧能带速度反向。</para>
/// <para>接入方式仍是状态类：只监听 <c>Classes</c> 里的 <c>is-open</c>，XAML 与绑定一行不改；
/// 首次接管时把 <c>Transitions</c> 置 null（本地值优先于样式），否则逐帧写入会被样式过渡再平滑一次，
/// 起点永远落不下去（与 §5.9.13 提示条同一个坑）。</para>
/// </remarks>
public static class SurfaceMotion
{
    /// <summary>底部日志抽屉的类名。</summary>
    public const string DrawerClass = "overlay-drawer";

    /// <summary>未保存浮条的类名。</summary>
    public const string PillClass = "floating-pill-bar";

    /// <summary>展开状态类。</summary>
    public const string OpenClass = "is-open";

    /// <summary>抽屉收起时整块停在视口下方的距离（与样式里的 260 px 一致）。</summary>
    public const double DrawerClosedOffset = 260d;

    /// <summary>浮条收起时的下移距离（与样式里的 40 px 一致）。</summary>
    public const double PillClosedOffset = 40d;

    /// <summary>浮条收起时的缩放（与样式里的 0.96 一致）。</summary>
    public const double PillClosedScale = 0.96d;

    private const int MaxDepth = 8;

    private const int MaxVisited = 1200;

    private static readonly ConditionalWeakTable<Control, object> Tracked = new();

    /// <summary>接管 <paramref name="root"/> 下所有抽屉与浮条；返回本次新增接管的数量。</summary>
    public static int Attach(Visual? root)
    {
        if (root is null)
        {
            return 0;
        }

        // 迟到的控件（模板、晚到的数据、互斥切换）靠根节点的节流补扫接管，只赌换页那一拍会漏。
        MotionScan.Subscribe(root, nameof(SurfaceMotion), () => Attach(root));

        var count = 0;
        foreach (var control in Enumerate(root))
        {
            if (!IsSurface(control) || Tracked.TryGetValue(control, out _))
            {
                continue;
            }

            NotifyCollectionChangedEventHandler handler = (_, _) => Apply(control, snap: false);
            ((INotifyCollectionChanged)control.Classes).CollectionChanged += handler;
            Tracked.Add(control, handler);

            // 本地值压过样式过渡：不摘掉的话，逐帧写入会被过渡再平滑一次，弹簧等于被冻结。
            control.Transitions = null;
            Apply(control, snap: true);
            count++;
        }

        return count;
    }

    /// <summary>按当前状态类把浮层放到目标位置；<paramref name="snap"/> 为真时立即落位。</summary>
    public static void Apply(Control control, bool snap)
    {
        var open = control.Classes.Contains(OpenClass);
        var isDrawer = control.Classes.Contains(DrawerClass);
        var closedOffset = isDrawer ? DrawerClosedOffset : PillClosedOffset;
        var closedScale = isDrawer ? 1d : PillClosedScale;

        var y = open ? 0d : MotionBudget.Amplitude(closedOffset);
        var scale = open ? 1d : MotionBudget.Scaled(closedScale);

        TransformSpring.For(control, isDrawer ? SpringSpec.Glide : SpringSpec.Snappy)
            .MoveTo(y: y, scaleX: scale, scaleY: scale, opacity: open ? 1d : 0d, snap: snap);
    }

    private static bool IsSurface(Control control) =>
        control.Classes.Contains(DrawerClass) || control.Classes.Contains(PillClass);

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