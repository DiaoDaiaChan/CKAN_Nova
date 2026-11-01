using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace CkanUI.App.Animations;

/// <summary>
/// 文字逐行上屏：把页面里的 <see cref="TextBlock"/> 按视觉顺序排队，快速淡入 + 轻微上浮。
/// </summary>
/// <remarks>
/// <para>与 <see cref="EntranceChoreographer"/> 分工：那个编排"块"（卡片/面板）的位移，这个编排"字"的出现节奏。
/// 两层叠加时块的位移会给整段文字一个整体方向，行内的错峰只负责"文字正在加载"的观感。</para>
/// <para>节拍比块慢档快得多（默认 12 ms/行），因为文字是密集对象：按 30 ms 排 20 行要 0.6 s，
/// 看起来像卡顿；12 ms 排 20 行只要 0.23 s，是"快速扫过"而不是"逐字蹦出来"。</para>
/// <para>排队仍由弹簧通道的 hold 承担，换页打断时旧弹簧随可视树脱离自动失效，不留孤儿定时器。</para>
/// </remarks>
public static class TextRevealChoreographer
{
    /// <summary>最多编排多少行；再多就是满屏都在动，反而看不清内容。</summary>
    public const int MaxLines = 48;

    /// <summary>行间节拍（毫秒）。</summary>
    public const int StepMilliseconds = 12;

    /// <summary>完整档的文字上浮起点（像素）。</summary>
    public const double OffsetPixels = 7d;

    /// <summary>精简档的文字上浮起点（像素）。</summary>
    public const double ReducedOffsetPixels = 2d;

    private const int MaxDepth = 10;

    /// <summary>扫描上限，防止超长页面（虚拟化列表/大表格）在换页时走一整棵树。</summary>
    private const int MaxVisited = 2400;

    private static readonly ConditionalWeakTable<TabControl, object> Watched = new();

    /// <summary>对 <paramref name="root"/> 内的文字做逐行入场；返回被编排的文本块数量。</summary>
    /// <param name="root">页面或区域的根。</param>
    /// <param name="includeOpacity">是否同时压不透明度（只做位移时传 false）。</param>
    public static int Play(Visual? root, bool includeOpacity = true)
    {
        if (root is null || MotionBudget.IsCollapsed)
        {
            return 0;
        }

        Watch(root);

        var targets = Collect(root);
        if (targets.Count == 0)
        {
            return 0;
        }

        var full = MotionBudget.AllowsPageChoreography;
        var offset = full ? OffsetPixels : ReducedOffsetPixels;
        var step = full ? StepMilliseconds / 1000d : 0d;

        for (var index = 0; index < targets.Count; index++)
        {
            var text = targets[index];
            var spring = TransformSpring.For(text, SpringSpec.Glide);
            var hold = index * step;

            if (includeOpacity)
            {
                var rest = text.Opacity > 0 ? text.Opacity : 1d;
                spring.MoveTo(y: offset, opacity: 0, snap: true);
                spring.MoveTo(y: 0, opacity: rest, hold: hold);
            }
            else
            {
                spring.MoveTo(y: offset, snap: true);
                spring.MoveTo(y: 0, hold: hold);
            }
        }

        return targets.Count;
    }

    /// <summary>页内页签（<see cref="TabControl"/>）切换时，让新内容的文字同样逐行上屏。</summary>
    /// <remarks>
    /// 页签切换发生在同一页内部，不走换页编排，新内容此前是硬出现的。这里只订阅一次
    /// <c>SelectionChanged</c>，把新选中的内容宿主重新排一遍；模板还没套上时排到 <c>Loaded</c> 再来一次。
    /// </remarks>
    private static void Watch(Visual root)
    {
        foreach (var tabControl in FindTabControls(root))
        {
            if (Watched.TryGetValue(tabControl, out _))
            {
                continue;
            }

            Watched.Add(tabControl, tabControl);
            tabControl.SelectionChanged += (_, _) => Dispatcher.UIThread.Post(
                () => PlayTabContent(SelectedHost(tabControl)),
                DispatcherPriority.Loaded);
        }
    }

    /// <summary>页签切换后，把新内容的整套入场补齐：块级位移 + 文字逐行 + 滚动/数据到达接管。</summary>
    private static void PlayTabContent(ContentPresenter? host)
    {
        if (host is null || MotionBudget.IsCollapsed)
        {
            return;
        }

        // 新内容里可能刚长出滚动容器与列表：先挂接管再演，后续项才有入场可用。
        ScrollRevealMotion.Attach(host);
        FeedArrivalMotion.Attach(host);
        EntranceChoreographer.Play(host, includeOpacity: false);
        Play(host);
    }

    private static IEnumerable<TabControl> FindTabControls(Visual root)
    {
        var stack = new Stack<(Visual Node, int Depth)>();
        stack.Push((root, 0));
        var visited = 0;

        while (stack.Count > 0 && visited < MaxVisited)
        {
            var (node, depth) = stack.Pop();
            visited++;

            if (node is TabControl tabControl)
            {
                yield return tabControl;
            }

            if (depth >= MaxDepth)
            {
                continue;
            }

            foreach (var child in node.GetVisualChildren())
            {
                stack.Push((child, depth + 1));
            }
        }
    }

    private static ContentPresenter? SelectedHost(TabControl tabControl) =>
        tabControl.GetVisualDescendants().OfType<ContentPresenter>()
            .FirstOrDefault(presenter => presenter.Name == "PART_SelectedContentHost")
        ?? tabControl.GetVisualDescendants().OfType<ContentPresenter>().LastOrDefault();

    /// <summary>按视觉顺序收集有文字的可见 <see cref="TextBlock"/>（前序深度优先，跳过表格行）。</summary>
    private static List<TextBlock> Collect(Visual root)
    {
        var result = new List<TextBlock>();
        var stack = new Stack<(Visual Node, int Depth)>();
        stack.Push((root, 0));
        var visited = 0;

        while (stack.Count > 0 && result.Count < MaxLines && visited < MaxVisited)
        {
            var (node, depth) = stack.Pop();
            visited++;

            if (depth > 0 && node is TextBlock block && IsCandidate(block))
            {
                result.Add(block);
            }

            if (depth >= MaxDepth)
            {
                continue;
            }

            var children = node.GetVisualChildren().ToList();
            for (var index = children.Count - 1; index >= 0; index--)
            {
                stack.Push((children[index], depth + 1));
            }
        }

        return result;
    }

    private static bool IsCandidate(TextBlock block)
    {
        if (!block.IsVisible || block.Opacity <= 0)
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(block.Text) && (block.Inlines is null || block.Inlines.Count == 0))
        {
            return false;
        }

        // 表格行按行高低错落会像在抖动；单元格文字保持静态，由表格自身的 hover/选中反馈承担。
        return !block.GetVisualAncestors().OfType<DataGridRow>().Any();
    }
}