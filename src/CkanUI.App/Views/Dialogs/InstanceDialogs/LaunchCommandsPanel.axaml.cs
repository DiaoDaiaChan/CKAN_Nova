using Avalonia.Controls;

namespace CkanUI.App.Views.Dialogs.InstanceDialogs;

/// <summary>
/// 启动命令编辑面板（F-06，对齐上游 <c>GameCommandLineOptionsDialog</c>）。
/// </summary>
/// <remarks>
/// 视图只做渲染：列表与按钮全部绑定到 <c>LaunchCommandsPanelViewModel</c>，
/// 文本输入由页面注入的 <c>IInstanceTextPrompt</c> 弹 <c>TextInputDialogView</c>，
/// 落盘由 <c>InstancesPageViewModel.SaveCommandsAsync</c> 负责，这里不碰文件系统。
/// </remarks>
public partial class LaunchCommandsPanel : UserControl
{
    /// <summary>构造面板。</summary>
    public LaunchCommandsPanel()
    {
        InitializeComponent();
    }
}
