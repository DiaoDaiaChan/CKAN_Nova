using Avalonia.Controls;

namespace CkanUI.App.Views.Dialogs;

/// <summary>
/// 详情面板视图（03 篇 §3.1 / 06 篇 UI-03）：嵌在模组页右侧，不是窗口，因此是 <see cref="UserControl"/>。
/// 数据上下文为 <c>ModDetailsViewModel</c>，空状态与选中状态互斥显示。
/// </summary>
public sealed partial class ModDetailsView : UserControl
{
    /// <summary>初始化视图。</summary>
    public ModDetailsView() => InitializeComponent();
}
