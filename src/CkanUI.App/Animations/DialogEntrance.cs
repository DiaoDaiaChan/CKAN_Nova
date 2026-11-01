using Avalonia.Controls;

namespace CkanUI.App.Animations;

/// <summary>
/// 对话框正文入场：窗口整体淡入之后，块级元素与文字各做一段纯位移的错峰。
/// </summary>
/// <remarks>
/// 不压不透明度：窗口自己有关键帧淡入，正文再淡一次会变成"淡两次"，中间那段反而更闷
/// （与 <see cref="MainWindow"/> 整页只淡一次的口径一致）。
/// 关闭档两个编排内部都会直接返回，这里不重复判断。
/// </remarks>
public static class DialogEntrance
{
    /// <summary>对已经成形的对话框内容跑一遍块级与文字编排。</summary>
    public static void Play(Window dialog)
    {
        if (dialog is null || MotionBudget.IsCollapsed)
        {
            return;
        }

        EntranceChoreographer.Play(dialog, includeOpacity: false);
        TextRevealChoreographer.Play(dialog, includeOpacity: false);

        // 对话框里的长列表同样一页放不下：挂上滚动入场（内部幂等，多处调用不会叠加订阅）。
        ScrollRevealMotion.Attach(dialog);
        FeedArrivalMotion.Attach(dialog);
    }
}
