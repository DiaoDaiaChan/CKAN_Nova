using Avalonia.Controls;

namespace CkanUI.App.Views.Logs;

/// <summary>日志页（14 篇 §4.1 / 06 篇 UI-LOG-01）。视图只做布局，过滤/聚合/导出全在视图模型里。</summary>
public partial class LogPageView : UserControl
{
    /// <summary>构造并装载 XAML。</summary>
    public LogPageView() => InitializeComponent();
}
