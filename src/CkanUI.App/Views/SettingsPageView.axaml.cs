using Avalonia.Controls;

namespace CkanUI.App.Views;

/// <summary>设置页视图（03 篇 §7 / 06 篇 UI-07）。只做布局与绑定，逻辑全部在 SettingsPageViewModel。</summary>
public sealed partial class SettingsPageView : UserControl
{
    /// <summary>初始化视图（ViewLocator 通过无参构造创建）。</summary>
    public SettingsPageView() => InitializeComponent();
}
