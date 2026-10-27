using Avalonia.Controls;
using CkanUI.ViewModels;

namespace CkanUI.App.Views;

/// <summary>
/// modpack 编辑器视图（06 篇 UI-24）：只做绑定与生命周期接线，业务全在
/// <see cref="CkanUI.ViewModels.Pages.ModPackEditorPageViewModel"/> 里。
/// </summary>
/// <remarks>视图挂到可视树时激活 VM，移出时取消激活；编辑器不订阅服务事件，因此两者都是幂等的空操作。</remarks>
public partial class ModPackEditorPageView : UserControl
{
    /// <summary>构造视图（ViewLocator 无参构造）。</summary>
    public ModPackEditorPageView()
    {
        InitializeComponent();
        AttachedToVisualTree += (_, _) => (DataContext as PageViewModel)?.Activate();
        DetachedFromVisualTree += (_, _) => (DataContext as PageViewModel)?.Deactivate();
    }
}
