using System.Collections.Generic;
using System.Collections.Specialized;
using System.Runtime.CompilerServices;

using Avalonia;
using Avalonia.Controls;
using Avalonia.VisualTree;

namespace CkanUI.App.Animations;

/// <summary>
/// 页面内的"同格叠放层"（空态 ↔ 数据态、未选中 ↔ 已选中）改由弹簧帧驱动。
/// </summary>
/// <remarks>
/// <para>样式里 <c>Control.view-layer</c> 用 <c>TransformOperationsTransition</c> 播 6 px 上浮 + 0.98 缩放（接管后 9 px / 0.97，见 §5.9.15）。
/// 两层互斥切换常发生在请求还没回来时（先显示空态、数据到了再切数据态），过渡从 t=0 重算、速度归零，
/// 观感是"停住再重新起步"。这里只监听 <c>is-active</c>，让切换带速度反向。</para>
/// <para>跳过自带悬停/按压位移的控件（<c>CkanCardInteractive</c> 等）：它们的 <c>RenderTransform</c> 归样式管，
/// 接管会吃掉悬停抬升。跳过即保持旧过渡，不破坏既有反馈。</para>
/// </remarks>
public static class ViewLayerMotion
{
    /// <summary>视图层类名。</summary>
    public const string ViewLayerClass = "view-layer";

    /// <summary>激活状态类。</summary>
    public const string ActiveClass = "is-active";

    /// <summary>未激活时的上浮距离（样式里是 6 px，接管时放大到 9 px 才看得出来）。</summary>
    public const double ClosedOffsetPixels = 9d;

    /// <summary>未激活时的缩放（样式里是 0.98，接管时放大到 0.97）。</summary>
    public const double ClosedScale = 0.97d;

    private const int MaxDepth = 10;

    private const int MaxVisited = 2400;

    /// <summary>这些类自己拥有 RenderTransform 动画，交给样式继续管。</summary>
    private static readonly string[] ConflictClasses =
    [
        "CkanCardInteractive",
        "stagger-card",
        "overlay-drawer",
        "floating-pill-bar",
    ];

    private static readonly ConditionalWeakTable<Control, object> Tracked = new();

    /// <summary>接管 <paramref name="root"/> 下的视图层；返回本次新增接管的数量。</summary>
    public static int Attach(Visual? root)
    {
        if (root is null)
        {
            return 0;
        }

        // 迟到的控件（模板、晚到的数据、互斥切换）靠根节点的节流补扫接管，只赌换页那一拍会漏。
        MotionScan.Subscribe(root, nameof(ViewLayerMotion), () => Attach(root));

        var count = 0;
        foreach (var control in Enumerate(root))
        {
            if (!control.Classes.Contains(ViewLayerClass) || HasConflict(control) || Tracked.TryGetValue(control, out _))
            {
                continue;
            }

            NotifyCollectionChangedEventHandler handler = (_, _) => Apply(control, snap: false);
            ((INotifyCollectionChanged)control.Classes).CollectionChanged += handler;
            Tracked.Add(control, handler);

            control.Transitions = null;
            Apply(control, snap: true);
            count++;
        }

        return count;
    }

    /// <summary>按当前类把该层放到目标状态；<paramref name="snap"/> 为真时立即落位。</summary>
    public static void Apply(Control control, bool snap)
    {
        var active = control.Classes.Contains(ActiveClass);

        TransformSpring.For(control, SpringSpec.Glide).MoveTo(
            y: active ? 0d : MotionBudget.Amplitude(ClosedOffsetPixels),
            scaleX: active ? 1d : MotionBudget.Scaled(ClosedScale),
            scaleY: active ? 1d : MotionBudget.Scaled(ClosedScale),
            opacity: active ? 1d : 0d,
            snap: snap);
    }

    private static bool HasConflict(Control control)
    {
        foreach (var conflict in ConflictClasses)
        {
            if (control.Classes.Contains(conflict))
            {
                return true;
            }
        }

        return false;
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