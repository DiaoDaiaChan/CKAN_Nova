using Avalonia.Controls;
using CkanUI.ViewModels;

namespace CkanUI.App.Views;

/// <summary>
/// 仓库页视图（03 篇 §6 V-04 / 06 篇 UI-06）：只做绑定与生命周期接线，业务全在
/// <see cref="CkanUI.ViewModels.Pages.ReposPageViewModel"/> 里。
/// </summary>
/// <remarks>视图挂到可视树时激活 VM（建立服务事件订阅），移出时取消激活（退订）。</remarks>
public partial class ReposPageView : UserControl
{
    /// <summary>构造视图（ViewLocator 无参构造）。</summary>
    public ReposPageView()
    {
        InitializeComponent();
        AttachedToVisualTree += (_, _) => (DataContext as PageViewModel)?.Activate();
        DetachedFromVisualTree += (_, _) => (DataContext as PageViewModel)?.Deactivate();
    }
}
