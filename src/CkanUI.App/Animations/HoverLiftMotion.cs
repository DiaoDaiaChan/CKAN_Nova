using System.Collections.Generic;
using System.Runtime.CompilerServices;

using Avalonia.Controls;
using Avalonia.VisualTree;

namespace CkanUI.App.Animations;

/// <summary>
/// 可点卡片悬停时向上抬 2 px 的位移，改由弹簧帧驱动。
/// </summary>
/// <remarks>
/// <para>样式里 `Border.CkanCardInteractive:pointerover` 用 `TransformOperationsTransition + Fast(90ms)` 播这 2 px。
/// 鼠标扫过一排卡片时，每张卡都要先把上一次的速度清零再起步，抬升看起来"一顿一顿"。这里按
/// <see cref="TransformSpring"/> 接管位移：连续掠过多张卡时，抬升之间带速度过渡。</para>
/// <para>范围：可点卡片、引导卡片（Border 与 Button 两种承载），以及 `ListBoxItem`/`TreeViewItem`/`DataGridRow` 的整行悬停——
/// 后两者原来什么反馈都没有，鼠标扫过列表只剩指针变化。</para>
/// <para>样式里的 `:pressed` 会把 RenderTransform 换成 `scale(0.995)`；本类的本地值优先，
/// 按压手感仍由 <see cref="InteractionMotion"/> 的缩放通道负责（两者改的是不同通道，互不覆盖）。</para>
/// </remarks>
public static class HoverLiftMotion
{
    /// <summary>可点卡片的类名。</summary>
    public const string CardClass = "CkanCardInteractive";

    /// <summary>引导卡片的类名（空态里"下一步做什么"的入口，同样用 2 px 抬升）。</summary>
    public const string GuideCardClass = "CkanGuideCard";

    /// <summary>按钮版引导卡片的类名（Button 承载，类型选择器不跨 Border，故按类识别）。</summary>
    public const string GuideButtonClass = "CkanGuide";

    /// <summary>悬停抬升距离（像素，向上为负）。</summary>
    public const double LiftPixels = 2d;

    /// <summary>列表/树的整行抬升距离：整行比卡片宽，2 px 会像抖动，减半。</summary>
    public const double RowLiftPixels = 1d;

    private const int MaxDepth = 12;

    private const int MaxVisited = 3000;

    private static readonly ConditionalWeakTable<Control, object> Tracked = new();

    /// <summary>给 <paramref name="root"/> 下的可点卡片挂上弹簧悬停；返回本次新增接管的数量。</summary>
    public static int Attach(Avalonia.Visual? root)
    {
        if (root is null)
        {
            return 0;
        }

        // 迟到的控件（模板、晚到的数据、互斥切换）靠根节点的节流补扫接管，只赌换页那一拍会漏。
        MotionScan.Subscribe(root, nameof(HoverLiftMotion), () => Attach(root));

        var count = 0;
        foreach (var control in Enumerate(root))
        {
            if (LiftOf(control) is null || Tracked.TryGetValue(control, out _))
            {
                continue;
            }

            control.PointerEntered += (_, _) => Apply(control, hovered: true);
            control.PointerExited += (_, _) => Apply(control, hovered: false);
            Tracked.Add(control, control);
            count++;
        }

        return count;
    }

    /// <summary>按悬停状态把卡片放到抬升或原位（关闭档直接不动）。</summary>
    public static void Apply(Control control, bool hovered)
    {
        if (MotionBudget.IsCollapsed)
        {
            return;
        }

        var lift = MotionBudget.Amplitude(LiftOf(control) ?? LiftPixels);
        TransformSpring.For(control, SpringSpec.Glide).MoveTo(y: hovered ? -lift : 0d);
    }

    /// <summary>该类表面要不要抬、抬多少；不认识的类返回 <see langword="null"/>。</summary>
    private static double? LiftOf(Control control) =>
        control.Classes.Contains(CardClass)
        || control.Classes.Contains(GuideCardClass)
        || control.Classes.Contains(GuideButtonClass)
            ? LiftPixels
            : control is ListBoxItem or TreeViewItem or DataGridRow
                ? RowLiftPixels
                : null;

    private static IEnumerable<Control> Enumerate(Avalonia.Visual root)
    {
        var queue = new Queue<(Avalonia.Visual Node, int Depth)>();
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