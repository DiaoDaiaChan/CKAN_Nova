using Avalonia.Controls;

namespace CkanUI.App.Views.Operations;

/// <summary>
/// 操作队列浮层视图（03 篇 §1 / 06 篇 UI-08）。
/// 只做布局与绑定，进度推算、聚合、取消全部在 OperationQueueViewModel 里。
/// </summary>
/// <remarks>
/// 宿主方式：放到 MainWindow 最上层 Grid 的右下角（<c>HorizontalAlignment=Right VerticalAlignment=Bottom</c>），
/// 或用 <c>Panel.ZIndex</c> 覆盖在内容之上；本视图自带 16 的外边距与空队列折叠，宿主不需要再判断可见性。
/// 不复用 ViewLocator：它只匹配 PageViewModel，浮层不是页面（Lead 在 MainWindow 直接实例化即可）。
/// </remarks>
public sealed partial class OperationQueueOverlayView : UserControl
{
    /// <summary>初始化视图。</summary>
    public OperationQueueOverlayView() => InitializeComponent();
}
