namespace CkanUI.App.Views.Dialogs;

/// <summary>
/// 首次启动指引视图（两栏：左「走到哪」、右「这一步做什么」）。
/// </summary>
/// <remarks>
/// 为什么放宽 <see cref="DialogWindow"/> 的 760 上限：普通对话框是「一句话 + 一组按钮」，
/// 窄一点更聚焦；向导左侧要放七步的进度轨，760 会把右侧表单压到换行。
/// </remarks>
public sealed partial class FirstRunWizardView : DialogWindow
{
    /// <summary>初始化视图。</summary>
    public FirstRunWizardView()
    {
        InitializeComponent();

        MaxWidth = 1040;
        MaxHeight = 760;
        MinWidth = 880;
        MinHeight = 560;
    }
}
