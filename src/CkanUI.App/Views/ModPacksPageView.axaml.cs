using Avalonia.Controls;
using CkanUI.ViewModels;

namespace CkanUI.App.Views;

/// <summary>
/// 导入导出页视图（03 篇 §6 V-11）：只做绑定与生命周期接线，业务全在
/// <see cref="CkanUI.ViewModels.Pages.ModPacksPageViewModel"/> 里。
/// </summary>
/// <remarks>视图挂到可视树时激活 VM（建立服务事件订阅），移出时取消激活（退订）。</remarks>
public partial class ModPacksPageView : UserControl
{
    /// <summary>构造视图（ViewLocator 无参构造）。</summary>
    public ModPacksPageView()
    {
        InitializeComponent();
        AttachedToVisualTree += (_, _) => (DataContext as PageViewModel)?.Activate();
        DetachedFromVisualTree += (_, _) => (DataContext as PageViewModel)?.Deactivate();
    }
}
