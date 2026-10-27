using Avalonia.Controls;

namespace CkanUI.App.Views;

/// <summary>
/// 跨实例去重页视图（规格 F-41 / 03 篇 UI-21）：只做 ViewLocator 需要的无参构造。
/// </summary>
/// <remarks>业务全部在 <c>DedupPageViewModel</c> 里；本页不需要额外的宿主能力
/// （硬链接由 Services 层通过平台 API 完成），因此没有 DataContext 接线代码。</remarks>
public partial class DedupPageView : UserControl
{
    /// <summary>构造视图（ViewLocator 无参构造，因此这里不能有参数）。</summary>
    public DedupPageView() => InitializeComponent();
}
