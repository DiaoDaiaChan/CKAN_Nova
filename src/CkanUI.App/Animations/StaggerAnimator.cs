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

    /// <summary>水平向右切页签时卡片的附加预备态类名。</summary>
    public const string PrepareRightClass = "stagger-prepare-right";

    /// <summary>水平向左切页签时卡片的附加预备态类名。</summary>
    public const string PrepareLeftClass = "stagger-prepare-left";

    /// <summary>整页或页签内容舞台类名（挂在 PageHost 或 TabControl 内容承载器上）。</summary>
    public const string StageClass = "tab-stage";

    /// <summary>舞台从右侧滑入的预备态类名（切到右边的页签）。</summary>
    public const string StagePrepareRightClass = "tab-prepare-right";

    /// <summary>舞台从左侧滑入的预备态类名（切到左边的页签）。</summary>
    public const string StagePrepareLeftClass = "tab-prepare-left";

    /// <summary>舞台从下方升起的预备态类名（侧栏跨组切换）。</summary>
    public const string StagePrepareUpClass = "tab-prepare-up";

    /// <summary>页签下划线指示条从左侧追赶滑入的预备态类名。</summary>
    public const string IndicatorFromLeftClass = "tab-indicator-from-left";

    /// <summary>页签下划线指示条从右侧追赶滑入的预备态类名。</summary>
    public const string IndicatorFromRightClass = "tab-indicator-from-right";

    private static int _stageGlideGeneration;

    /// <summary>两张卡片之间的间隔。</summary>
    public static readonly TimeSpan Step = TimeSpan.FromMilliseconds(25);

    /// <summary>等一帧：首帧必须先把「预备态」提交进渲染树，摘类时才有起点可插值。</summary>
    public static readonly TimeSpan Frame = TimeSpan.FromMilliseconds(16);

    /// <summary>
    /// 使用合成器 VSync 双帧回调驱动整块页面/页签舞台（<paramref name="stage"/>）做临界阻尼平滑位移：
    /// 不拆散内部卡片、不调用强制重排、不依赖 Windows 15.6ms 计时器，彻底消除高刷屏下的闪烁与布局抖动。
    /// </summary>
    /// <param name="stage">舞台控件（如 <c>PageHost</c> 或 <c>PART_SelectedContentHost</c>）。</param>
    /// <param name="direction">切换方向：&gt;0 向右切页签，&lt;0 向左切页签，0 跨组切换。</param>
    public static void PlayStageGlide(Control? stage, int direction = 0)
    {
        if (stage is null)
        {
            return;
        }

        if (Application.Current?.Resources.TryGetResource(Theming.MotionHost.SpringSoftKey, null, out var durationObj) == true &&
            durationObj is TimeSpan ts && ts <= TimeSpan.Zero)
        {
            return;
        }

        if (TopLevel.GetTopLevel(stage) is not { } topLevel)
        {
            return;
        }

        if (!stage.Classes.Contains(StageClass))
        {
            stage.Classes.Add(StageClass);
        }

        stage.Classes.Remove(StagePrepareRightClass);
        stage.Classes.Remove(StagePrepareLeftClass);
        stage.Classes.Remove(StagePrepareUpClass);

        var prepClass = direction > 0
            ? StagePrepareRightClass
            : direction < 0
                ? StagePrepareLeftClass
                : StagePrepareUpClass;

        var generation = ++_stageGlideGeneration;
        stage.Classes.Add(prepClass);

        // 第一拍 RequestAnimationFrame 让当前帧把「Transitions=null + 初始微偏移」提交给合成器；
        // 第二拍在紧邻的下一个 VSync 帧摘除预备类，由临界阻尼弹簧平滑插值归零，无过冲、无跳帧。
        topLevel.RequestAnimationFrame(_ =>
        {
            if (generation != _stageGlideGeneration)
            {
                return;
            }

            topLevel.RequestAnimationFrame(_ =>
            {
                if (generation != _stageGlideGeneration)
                {
                    return;
                }

                stage.Classes.Remove(StagePrepareRightClass);
                stage.Classes.Remove(StagePrepareLeftClass);
                stage.Classes.Remove(StagePrepareUpClass);
            });
        });
    }

    /// <summary>
    /// 让 <paramref name="root"/> 舞台与下属所有 <c>stagger-card</c> 按方向平滑入场。
    /// </summary>
    /// <param name="root">页面根控件（通常是外壳的 <c>PageHost</c> 或 <c>TabControl</c> 的内容宿主）。</param>
    /// <param name="cancellationToken">取消令牌：页面被切走时取消，正在等待的卡片与舞台会立刻复位。</param>
    /// <param name="direction">页签切换方向：&gt;0 表示切向右侧页签，&lt;0 表示切向左侧页签，0 表示侧栏跨组切换。</param>
    /// <param name="activeIndicator">当前激活页签的底部指示条（可选，传入时按同方向做追赶滑入）。</param>
    /// <returns>级联播放完毕（或被取消）时完成的任务。</returns>
    public static async Task PlayAsync(
        Visual? root,
        CancellationToken cancellationToken = default,
        int direction = 0,
        Control? activeIndicator = null)
    {
        if (root is null)
        {
            return;
        }

        if (Application.Current?.Resources.TryGetResource(Theming.MotionHost.SpringSoftKey, null, out var durationObj) == true &&
            durationObj is TimeSpan ts && ts <= TimeSpan.Zero)
        {
            return;
        }

        var rootControl = root as Control;
        if (rootControl is not null && !rootControl.Classes.Contains(StageClass))
        {
            rootControl.Classes.Add(StageClass);
        }

        var cards = CollectCards(root);
        if (cards.Count == 0 && rootControl is null && activeIndicator is null)
        {
            return;
        }

        if (rootControl is not null)
        {
            rootControl.Classes.Remove(StagePrepareRightClass);
            rootControl.Classes.Remove(StagePrepareLeftClass);
            rootControl.Classes.Remove(StagePrepareUpClass);
            var stagePrep = direction > 0
                ? StagePrepareRightClass
                : direction < 0
                    ? StagePrepareLeftClass
                    : StagePrepareUpClass;
            rootControl.Classes.Add(stagePrep);
        }

        if (activeIndicator is not null && direction != 0)
        {
            activeIndicator.Classes.Remove(IndicatorFromLeftClass);
            activeIndicator.Classes.Remove(IndicatorFromRightClass);
            activeIndicator.Classes.Add(direction > 0 ? IndicatorFromLeftClass : IndicatorFromRightClass);
        }

        foreach (var card in cards)
        {
            card.Classes.Remove(PrepareRightClass);
            card.Classes.Remove(PrepareLeftClass);
            card.Classes.Add(PrepareClass);
            if (direction > 0)
            {
                card.Classes.Add(PrepareRightClass);
            }
            else if (direction < 0)
            {
                card.Classes.Add(PrepareLeftClass);
            }
        }

        try
        {
            await Task.Delay(Frame, cancellationToken).ConfigureAwait(true);

            cancellationToken.ThrowIfCancellationRequested();

            if (activeIndicator is not null)
            {
                activeIndicator.Classes.Remove(IndicatorFromLeftClass);
                activeIndicator.Classes.Remove(IndicatorFromRightClass);
            }

            if (rootControl is not null)
            {
                rootControl.Classes.Remove(StagePrepareRightClass);
                rootControl.Classes.Remove(StagePrepareLeftClass);
                rootControl.Classes.Remove(StagePrepareUpClass);
            }

            foreach (var card in cards)
            {
                // 取消检查放在每次摘类之前：一帧最多放行一张，切页时最多再多闪一张卡。
                cancellationToken.ThrowIfCancellationRequested();
                card.Classes.Remove(PrepareClass);
                card.Classes.Remove(PrepareRightClass);
                card.Classes.Remove(PrepareLeftClass);
                await Task.Delay(Step, cancellationToken).ConfigureAwait(true);
            }
        }
        catch (OperationCanceledException)
        {
            // 页面被切走属于正常路径：不记日志、不报错，交给 finally 复位。
        }
        finally
        {
            if (activeIndicator is not null)
            {
                activeIndicator.Classes.Remove(IndicatorFromLeftClass);
                activeIndicator.Classes.Remove(IndicatorFromRightClass);
            }

            if (rootControl is not null)
            {
                rootControl.Classes.Remove(StagePrepareRightClass);
                rootControl.Classes.Remove(StagePrepareLeftClass);
                rootControl.Classes.Remove(StagePrepareUpClass);
            }

            foreach (var card in cards)
            {
                card.Classes.Remove(PrepareClass);
                card.Classes.Remove(PrepareRightClass);
                card.Classes.Remove(PrepareLeftClass);
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
