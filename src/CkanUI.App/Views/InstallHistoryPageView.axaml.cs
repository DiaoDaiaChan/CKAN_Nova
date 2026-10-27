using Avalonia.Controls;
using CkanUI.ViewModels;

namespace CkanUI.App.Views;

/// <summary>
/// 安装历史页视图（03 篇 §6 / 12 篇 F-44 / UI-23）：只做绑定与生命周期接线，
/// 业务全在 <see cref="CkanUI.ViewModels.Pages.InstallHistoryPageViewModel"/> 里。
/// </summary>
/// <remarks>视图挂到可视树时激活 VM（首次读取历史），移出时取消激活。</remarks>
public partial class InstallHistoryPageView : UserControl
{
    /// <summary>构造视图（ViewLocator 无参构造）。</summary>
    public InstallHistoryPageView()
    {
        InitializeComponent();
        AttachedToVisualTree += (_, _) => (DataContext as PageViewModel)?.Activate();
        DetachedFromVisualTree += (_, _) => (DataContext as PageViewModel)?.Deactivate();
    }
}
