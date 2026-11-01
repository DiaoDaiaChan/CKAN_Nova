using Avalonia.Controls;

namespace CkanUI.App.Animations;

/// <summary>
/// 对话框正文入场：窗口整体淡入之后，块级元素与文字各做一段纯位移的错峰，再把各层状态动效接上。
/// </summary>
/// <remarks>
/// <para>不压不透明度：窗口自己有关键帧淡入，正文再淡一次会变成"淡两次"，中间那段反而更闷
/// （与 <see cref="MainWindow"/> 整页只淡一次的口径一致）。</para>
/// <para>对话框是独立窗口，页面那套 `PlayPageReveal` 的接管一个都跑不到：模组详情里的 `view-layer` TabControl、
/// 提示条、可点卡片、浮层都得在这里补挂一次，否则同一套控件在主窗口有动效、弹进对话框就没了。</para>
/// </remarks>
public static class DialogEntrance
{
    /// <summary>对已经成形的对话框内容跑一遍块级与文字编排，并接上状态动效。</summary>
    public static void Play(Window dialog)
    {
        if (dialog is null || MotionBudget.IsCollapsed)
        {
            return;
        }

        EntranceChoreographer.Play(dialog, includeOpacity: false);
        TextRevealChoreographer.Play(dialog, includeOpacity: false);

        // 对话框里的长列表同样一页放不下：挂上滚动入场与数据到达（内部幂等，多处调用不会叠加订阅）。
        ScrollRevealMotion.Attach(dialog);
        FeedArrivalMotion.Attach(dialog);

        // 状态动效：主窗口有的这里也要有（各自幂等，迟到的控件由 MotionScan 补扫）。
        SurfaceMotion.Attach(dialog);
        ViewLayerMotion.Attach(dialog);
        BannerMotion.Attach(dialog);
        HoverLiftMotion.Attach(dialog);
    }
}
