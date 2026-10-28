using Avalonia.Controls;
using CkanUI.ViewModels;

namespace CkanUI.App.Views;

/// <summary>
/// Mod 配置页视图（M9 / UI-CFG-01 骨架）：只做绑定与生命周期接线，
/// 业务全在 <see cref="CkanUI.ViewModels.Pages.ModConfigPageViewModel"/> 里。
/// </summary>
/// <remarks>视图挂到可视树时激活 VM（每次进页面重读配置），移出时取消激活。</remarks>
public partial class ModConfigPageView : UserControl
{
    /// <summary>构造视图（ViewLocator 无参构造）。</summary>
    public ModConfigPageView()
    {
        InitializeComponent();
        AttachedToVisualTree += (_, _) => (DataContext as PageViewModel)?.Activate();
        DetachedFromVisualTree += (_, _) => (DataContext as PageViewModel)?.Deactivate();
    }
}
