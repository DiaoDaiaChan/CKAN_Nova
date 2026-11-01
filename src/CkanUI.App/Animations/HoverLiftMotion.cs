using System.Collections.Generic;
using System.Linq;
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
/// <para>引导卡/引导按钮悬停时，卡内的 `GuideArrow` 箭头同时右滑 3 px——样式里同样是 90 ms 的
/// `TransformOperationsTransition`，跟卡片一起抬的时候箭头会迟半步，看起来像"卡动了、箭头没跟上"。</para>
/// <para>普通 `Button` 也接管：基础样式靠 `TransformOperationsTransition`(`CkanMotionDurationSpringCrisp`)
/// 播 1 px 抬升，鼠标横扫一排按钮/工具条时每一个都从零速重新起步；换成弹簧后 1.5 px 且速度接力。
/// 卡片内部的按钮不接管——整张卡已经在抬，再叠一层会让卡内的按钮"比卡还高"，看起来像错位。</para>
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

    /// <summary>普通按钮的悬停抬升距离：样式里是 1 px，这里略加重到 1.5 px 才看得出来。</summary>
    public const double ButtonLiftPixels = 1.5d;

    /// <summary>图标按钮的类名：不接管位移，悬停反馈由图标自身的旋转动画负责。</summary>
    public const string IconButtonClass = "CkanIcon";

    /// <summary>引导卡片右箭头的类名：悬停时向右滑 3 px。</summary>
    public const string ArrowClass = "GuideArrow";

    /// <summary>箭头滑出距离（像素）。</summary>
    public const double ArrowTravelPixels = 3d;

    private const int MaxDepth = 24;

    private const int MaxVisited = 20000;

    private const int ShellMaxVisited = 60000;

    private static readonly ConditionalWeakTable<Control, object> Tracked = new();

    private static readonly ConditionalWeakTable<Control, Control?> Arrows = new();

    /// <summary>给 <paramref name="root"/> 下的可点卡片挂上弹簧悬停；返回本次新增接管的数量。</summary>
    public static int Attach(Avalonia.Visual? root) => Attach(root, MaxVisited);

    /// <summary>给整窗（标题栏、状态条、工具条）挂弹簧悬停，跳过导航树；返回本次新增接管的数量。</summary>
    /// <remarks>
    /// 外壳控件不在 PageHost 里面，页面级挂载永远扫不到——标题栏按钮、日志状态按钮原来悬停时一动不动。
    /// FANavigationViewItem 子树跳过（只跳导航项本身，导航壳里的标题栏按钮照常接管）：
    /// 导航项自己的悬停由 IconHoverRole 的 IconHoverShape.Sway 负责，再叠 1 px 行抬升会互相打架。
    /// </remarks>
    public static int AttachShell(Avalonia.Visual? root) => Attach(root, ShellMaxVisited);

    private static int Attach(Avalonia.Visual? root, int budget)
    {
        if (root is null)
        {
            return 0;
        }

        // 迟到的控件（模板、晚到的数据、互斥切换）靠根节点的节流补扫接管，只赌换页那一拍会漏。
        MotionScan.Subscribe(root, nameof(HoverLiftMotion), () => Attach(root, budget));

        var count = 0;
        foreach (var control in Enumerate(root, budget))
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
        if (MotionBudget.IsCollapsed || LiftOf(control) is not { } distance)
        {
            return;
        }

        var lift = MotionBudget.Amplitude(distance);
        var spec = control is Button ? SpringSpec.Snappy : SpringSpec.Glide;
        TransformSpring.For(control, spec).MoveTo(y: hovered ? -lift : 0d);

        if (ArrowOf(control) is { } arrow)
        {
            var travel = MotionBudget.Amplitude(ArrowTravelPixels);
            TransformSpring.For(arrow, SpringSpec.Snappy).MoveTo(x: hovered ? travel : 0d);
        }
    }

    /// <summary>该类表面要不要抬、抬多少；不认识的类返回 <see langword="null"/>。</summary>
    private static double? LiftOf(Control control)
    {
        if (control.Classes.Contains(CardClass)
            || control.Classes.Contains(GuideCardClass)
            || control.Classes.Contains(GuideButtonClass))
        {
            return LiftPixels;
        }

        if (control is ListBoxItem or TreeViewItem or DataGridRow)
        {
            return RowLiftPixels;
        }

        if (control is not Button)
        {
            return null;
        }

        // 图标按钮的 :pointerover 明确写 RenderTransform = none（图标本体由 IconHoverMotion 转角度），
        // 硬接管会跟那条 setter 抢 RenderTransform，结果是两边都不动。
        if (control.Classes.Contains(IconButtonClass))
        {
            return null;
        }

        // 卡片内部的按钮不抬：整张卡已经在抬，两层叠加会让按钮"浮"到卡面之上。
        var insideLiftingCard = control
            .GetVisualAncestors()
            .OfType<Control>()
            .Any(ancestor => ancestor.Classes.Contains(CardClass) || ancestor.Classes.Contains(GuideCardClass));

        return insideLiftingCard ? null : ButtonLiftPixels;
    }

    /// <summary>引导卡片的箭头；不认识的卡片返回 <see langword="null"/>（每个宿主只找一次）。</summary>
    private static Control? ArrowOf(Control host)
    {
        if (!host.Classes.Contains(GuideCardClass) && !host.Classes.Contains(GuideButtonClass))
        {
            return null;
        }

        if (Arrows.TryGetValue(host, out var cached))
        {
            return cached;
        }

        var found = host
            .GetVisualDescendants()
            .OfType<Control>()
            .FirstOrDefault(child => child.Classes.Contains(ArrowClass));

        if (found is not null)
        {
            Arrows.Add(host, found);
        }

        return found;
    }

    private static IEnumerable<Control> Enumerate(Avalonia.Visual root, int budget)
    {
        var queue = new Queue<(Avalonia.Visual Node, int Depth)>();
        queue.Enqueue((root, 0));
        var visited = 0;

        while (queue.Count > 0 && visited < budget)
        {
            var (node, depth) = queue.Dequeue();
            visited++;

            if (node is Control control)
            {
                yield return control;
            }

            if (depth >= MaxDepth || node is FluentAvalonia.UI.Controls.FANavigationViewItem)
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