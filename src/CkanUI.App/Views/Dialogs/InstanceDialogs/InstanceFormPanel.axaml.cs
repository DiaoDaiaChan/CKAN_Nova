using Avalonia.Controls;

namespace CkanUI.App.Views.Dialogs.InstanceDialogs;

/// <summary>
/// 实例表单面板（新增 / 克隆 / 重命名共用，03 篇 §8 D-06）。
/// </summary>
/// <remarks>
/// 视图只做渲染：字段绑定到 <c>InstanceFormViewModel</c>，提交/取消由该视图模型的命令处理，
/// 真正的服务调用在 <c>InstancesPageViewModel</c> 里。面板既可作为页面内联覆盖层使用，
/// 也可以在通用模态宿主就绪后原样放进对话框窗口，避免两份实现漂移。
/// </remarks>
public partial class InstanceFormPanel : UserControl
{
    /// <summary>构造面板。</summary>
    public InstanceFormPanel()
    {
        InitializeComponent();
    }
}
