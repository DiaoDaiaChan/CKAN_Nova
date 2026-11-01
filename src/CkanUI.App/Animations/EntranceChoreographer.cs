using System.Collections.Generic;
using System.Linq;

using Avalonia;
using Avalonia.Controls;
using Avalonia.VisualTree;

namespace CkanUI.App.Animations;

/// <summary>
/// 换页/换区时的入场编排：一级元素错峰上浮，幅度与节拍由 <see cref="MotionBudget"/> 决定。
/// </summary>
/// <remarks>
/// 排队由弹簧通道自己的 hold 承担，不用 <c>Task.Delay</c>——定时器与渲染帧不同步，
/// 而且被下一次换页打断时会留下孤儿任务继续改控件。
/// </remarks>
public static class EntranceChoreographer
{
    /// <summary>最多编排几个元素；再多就不是错峰，是拥挤。</summary>
    public const int MaxTargets = 8;

    /// <summary>错峰节拍（毫秒）。</summary>
    public const int StepMilliseconds = 30;

    /// <summary>完整档的上浮起点（像素）。</summary>
    public const double OffsetPixels = 24d;

    /// <summary>精简档的上浮起点（像素）。</summary>
    public const double ReducedOffsetPixels = 5d;

    /// <summary>低于这个数量就继续往下一层补，避免只有两个元素在错峰。</summary>
    private const int MinTargets = 3;

    private const int MaxDepth = 6;

    /// <summary>对 <paramref name="root"/> 的首个"多子级层"做错峰入场；返回被编排的元素数。</summary>
    /// <param name="root">页面或区域的根。</param>
    /// <param name="includeOpacity">是否同时压不透明度（整页已经淡入时传 false，避免两层一起淡）。</param>
    public static int Play(Visual? root, bool includeOpacity = true)
    {
        if (root is null || MotionBudget.IsCollapsed)
        {
            return 0;
        }

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
            var control = targets[index];
            var spring = TransformSpring.For(control, SpringSpec.Glide);
            var hold = index * step;

            if (includeOpacity)
            {
                var rest = control.Opacity > 0 ? control.Opacity : 1d;
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

    /// <summary>把所有已编排元素的弹簧立刻落位（换页被打断时用，避免遗留半截动画）。</summary>
    public static void SnapAll(Visual? root)
    {
        if (root is null)
        {
            return;
        }

        foreach (var control in Collect(root))
        {
            var spring = TransformSpring.For(control, SpringSpec.Glide);
            spring.MoveTo();
        }
    }

    private static List<Control> Collect(Visual? root)
    {
        var targets = new List<Control>();
        var current = root;

        for (var depth = 0; depth < MaxDepth && current is not null; depth++)
        {
            var children = VisibleChildren(current);
            if (children.Count == 0)
            {
                break;
            }

            if (children.Count >= 2)
            {
                foreach (var child in children)
                {
                    if (targets.Count >= MaxTargets)
                    {
                        break;
                    }

                    if (!targets.Contains(child))
                    {
                        targets.Add(child);
                    }
                }

                if (targets.Count >= MinTargets)
                {
                    break;
                }
            }

            // 首层只有"头部 + 内容"这类两子级结构时，继续沿内容最厚的那支往下补，
            // 否则整页错峰只有两个元素在动，观感上等于没有编排。
            current = PickRichest(children);
        }

        return targets;
    }

    private static List<Control> VisibleChildren(Visual parent) =>
        parent.GetVisualChildren()
            .OfType<Control>()
            .Where(child => child.IsVisible)
            .ToList();

    private static Control PickRichest(List<Control> children)
    {
        var best = children[0];
        var bestScore = Score(best);

        for (var index = 1; index < children.Count; index++)
        {
            var score = Score(children[index]);
            if (score > bestScore)
            {
                best = children[index];
                bestScore = score;
            }
        }

        return best;

        static int Score(Control control)
        {
            var nested = control.GetVisualChildren().OfType<Control>().Count(child => child.IsVisible);
            var area = control.Bounds.Width * control.Bounds.Height;

            // 子级多者优先，其次面积大者——头部条的容器通常两者都小。
            return (nested * 1_000_000) + (int)Math.Min(area, 999_999d);
        }
    }
}
