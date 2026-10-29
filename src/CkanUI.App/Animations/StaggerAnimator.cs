using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Avalonia;
using Avalonia.Controls;
using Avalonia.VisualTree;

namespace CkanUI.App.Animations;

/// <summary>
/// 页面卡片错峰入场（第三阶段 MAT-03）：把「一级卡片依次从下方 +14px 处以临界阻尼滑入」这件事做成一次遍历，
/// 不需要每个页面写代码，也不需要改 19 个页面的根标签。
/// </summary>
/// <remarks>
/// <para>机制只有两步，全靠样式配合（见 FluentStyles 的 <c>Control.stagger-card</c> 与
/// <c>Control.stagger-card.stagger-prepare</c>）：</para>
/// <list type="number">
///   <item><description>给所有卡片挂 <c>stagger-prepare</c>：该类把 <c>Transitions</c> 置空并把
///   Opacity 写到 0、位移写到 +14px —— 因为过渡被禁用，这一步是「瞬间就位」，不会看到卡片自己飘下去。</description></item>
///   <item><description>每隔 25ms 摘掉一张卡片的 <c>stagger-prepare</c>：类一摘，基础样式的弹簧过渡重新生效，
///   卡片就从当前状态弹回原位。25ms 是「能看出先后、又不至于让用户觉得慢」的经验间隔。</description></item>
/// </list>
/// <para>被打断是常态（用户在导航栏连点）：调用方持有 CancellationToken，取消后本方法在 finally 里
/// 把所有卡片的 <c>stagger-prepare</c> 都摘掉。这一步不能省 —— 否则取消时还没轮到的卡片会一直停在
/// Opacity=0，整页看起来像少了几个区块。</para>
/// </remarks>
public static class StaggerAnimator
{
    /// <summary>卡片锚点类名（由视图静态写在 XAML 上）。</summary>
    public const string CardClass = "stagger-card";

    /// <summary>入场预备态类名（由本类在运行时挂/摘）。</summary>
    public const string PrepareClass = "stagger-prepare";

    /// <summary>两张卡片之间的间隔。</summary>
    public static readonly TimeSpan Step = TimeSpan.FromMilliseconds(25);

    /// <summary>等一帧：首帧必须先把「预备态」提交进渲染树，摘类时才有起点可插值。</summary>
    public static readonly TimeSpan Frame = TimeSpan.FromMilliseconds(16);

    /// <summary>
    /// 让 <paramref name="root"/> 下所有 <c>stagger-card</c> 依次入场。
    /// </summary>
    /// <param name="root">页面根控件（通常是外壳的 <c>PageHost</c>）。</param>
    /// <param name="cancellationToken">取消令牌：页面被切走时取消，正在等进行的那一张会立刻复位。</param>
    /// <returns>级联播放完毕（或被取消）时完成的任务。</returns>
    public static async Task PlayAsync(Visual? root, CancellationToken cancellationToken = default)
    {
        if (root is null)
        {
            return;
        }

        var cards = CollectCards(root);
        if (cards.Count == 0)
        {
            return;
        }

        foreach (var card in cards)
        {
            card.Classes.Add(PrepareClass);
        }

        try
        {
            await Task.Delay(Frame, cancellationToken).ConfigureAwait(true);

            foreach (var card in cards)
            {
                // 取消检查放在每次摘类之前：一帧最多放行一张，切页时最多再多闪一张卡。
                cancellationToken.ThrowIfCancellationRequested();
                card.Classes.Remove(PrepareClass);
                await Task.Delay(Step, cancellationToken).ConfigureAwait(true);
            }
        }
        catch (OperationCanceledException)
        {
            // 页面被切走属于正常路径：不记日志、不报错，交给 finally 复位。
        }
        finally
        {
            foreach (var card in cards)
            {
                card.Classes.Remove(PrepareClass);
            }
        }
    }

    /// <summary>
    /// 收集锚点卡片。按可视化树的自然顺序（深度优先、同级按声明顺序）返回，
    /// 也就是用户在页面上看到的从上到下顺序 —— 错峰必须与阅读顺序一致，否则看起来像乱闪。
    /// </summary>
    /// <param name="root">遍历起点。</param>
    /// <returns>卡片列表（可能为空）。</returns>
    public static IReadOnlyList<Control> CollectCards(Visual root)
    {
        ArgumentNullException.ThrowIfNull(root);

        return root.GetVisualDescendants()
            .OfType<Control>()
            .Where(control => control.Classes.Contains(CardClass))
            .ToList();
    }
}
