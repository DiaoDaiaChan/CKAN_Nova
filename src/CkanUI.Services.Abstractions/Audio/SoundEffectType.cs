namespace CkanUI.Services.Abstractions.Audio;

/// <summary>
/// 界面交互音效类型。
/// 涵盖选中、勾选、取消勾选、错误、警告、ok（成功）及轻量点击等。
/// </summary>
public enum SoundEffectType
{
    /// <summary>项选中、Tab 切换、页面导航。</summary>
    Select,

    /// <summary>复选框勾选、开关开启。</summary>
    Check,

    /// <summary>复选框取消勾选、开关关闭。</summary>
    Uncheck,

    /// <summary>操作失败、错误提示、异常提示。</summary>
    Error,

    /// <summary>警告提示、需要注意的操作。</summary>
    Warning,

    /// <summary>操作成功、确认保存、任务完成。</summary>
    Ok,

    /// <summary>普通按钮点击、轻微交互反馈。</summary>
    Click,

    /// <summary>鼠标划过交互控件、悬停聚焦。</summary>
    Hover
}
