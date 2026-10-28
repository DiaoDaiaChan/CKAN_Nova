using Avalonia.Controls;

namespace CkanUI.App.Views.Mods;

/// <summary>
/// 组合搜索面板视图（12 篇 F-20/F-21）。
/// DataContext 由宿主页绑定到 <c>ModsSearchPanelViewModel</c>，这里不做任何代码后置逻辑。
/// </summary>
public partial class ModsSearchPanelView : UserControl
{
    /// <summary>初始化面板视图。</summary>
    public ModsSearchPanelView() => InitializeComponent();
}
