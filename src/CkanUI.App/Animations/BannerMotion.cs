using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;

using Avalonia;
using Avalonia.Controls;
using Avalonia.VisualTree;

namespace CkanUI.App.Animations;

/// <summary>
/// 状态提示条（<c>Border.CkanInfoBanner</c>）出现时的入场：淡入 + 轻微上浮。
/// </summary>
/// <remarks>
/// <para>提示条由 <c>IsVisible</c> 绑定控制显隐，样式里的 <c>DoubleTransition Opacity</c> 因此从来没机会跑——
/// 控件从"不在树上"到"在树上"是硬切换，透明度根本没有中间帧。这里监听 <c>IsVisible</c> 变 true 的那一刻，
/// 先把不透明度压到 0、上浮几像素，再交给弹簧回到正常状态。</para>
/// <para>订阅不持有页面：处理器是静态方法，控件销毁后没有反向引用；用 <see cref="ConditionalWeakTable{TKey,TValue}"/>
/// 保证同一个提示条只订阅一次（页面切来切去、同一控件被重复扫描时不会叠加动画）。</para>
/// </remarks>
public static class BannerMotion
{
    /// <summary>完整档的上浮起点（像素）。</summary>
    public const double OffsetPixels = 8d;

    /// <summary>精简档的上浮起点（像素）。</summary>
    public const double ReducedOffsetPixels = 3d;

    private const int MaxDepth = 10;
    private const int MaxVisited = 2400;

    private static readonly ConditionalWeakTable<Control, object> Hooked = new();

    /// <summary>给 <paramref name="root"/> 里的提示条挂入场动画；已经可见的立刻入场。返回命中的提示条数量。</summary>
    public static int Attach(Visual? root)
    {
        if (root is null)
        {
            return 0;
        }

        var banners = Collect(root);
        foreach (var banner in banners)
        {
            var firstTime = false;
            Hooked.GetValue(banner, _ =>
            {
                firstTime = true;
                banner.PropertyChanged += OnBannerPropertyChanged;
                return banner;
            });

            if (firstTime && banner.IsVisible)
            {
                Animate(banner);
            }
        }

        return banners.Count;
    }

    /// <summary>提示条从隐藏变可见时入场；已经在树上但仅切换 <c>IsVisible</c> 的场景也走这里。</summary>
    private static void OnBannerPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property != Visual.IsVisibleProperty || e.NewValue is not true || sender is not Control banner)
        {
            return;
        }

        Animate(banner);
    }

    private static void Animate(Control banner)
    {
        if (MotionBudget.IsCollapsed)
        {
            return;
        }

        // 样式给提示条挂了 DoubleTransition(Opacity)：不摘掉的话，逐帧写不透明度会被过渡再平滑一次，
        // 起点永远落不下去（读出来还是 1）。提示条本身没有可交互状态，摘掉过渡不影响其它观感。
        banner.Transitions = null;

        var offset = MotionBudget.AllowsPageChoreography ? OffsetPixels : ReducedOffsetPixels;
        var rest = banner.Opacity > 0 ? banner.Opacity : 1d;
        var spring = TransformSpring.For(banner, SpringSpec.Glide);
        spring.MoveTo(y: offset, opacity: 0, snap: true);
        spring.MoveTo(y: 0, opacity: rest);
    }

    private static List<Control> Collect(Visual root)
    {
        var result = new List<Control>();
        var stack = new Stack<(Visual Node, int Depth)>();
        stack.Push((root, 0));
        var visited = 0;

        while (stack.Count > 0 && visited < MaxVisited)
        {
            var (node, depth) = stack.Pop();
            visited++;

            if (depth > 0 && node is Control control && control.Classes.Contains("CkanInfoBanner"))
            {
                result.Add(control);
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
}
