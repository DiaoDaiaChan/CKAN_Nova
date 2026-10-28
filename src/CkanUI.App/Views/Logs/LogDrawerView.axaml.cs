using Avalonia.Controls;

namespace CkanUI.App.Views.Logs;

/// <summary>底部日志抽屉（14 篇 §4.1 / 06 篇 UI-LOG-01）。收起时只留头条与徽标，展开显示最近 200 行。</summary>
public partial class LogDrawerView : UserControl
{
    /// <summary>构造并装载 XAML。</summary>
    public LogDrawerView() => InitializeComponent();
}
