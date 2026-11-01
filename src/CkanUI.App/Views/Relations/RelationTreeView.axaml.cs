using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using CkanUI.ViewModels.Relations;

namespace CkanUI.App.Views.Relations;

/// <summary>
/// 依赖关系视图（F-28 / UI-18）：支持可视化星轨关系网与层级树双模式交互切换。
/// 双击模组节点可快速跳转定位。
/// </summary>
public partial class RelationTreeView : UserControl
{
    public RelationTreeView() => InitializeComponent();

    private void OnNodeDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (sender is Control { DataContext: RelationNodeViewModel node } && DataContext is RelationTreeViewModel vm)
        {
            vm.Navigate(node);
        }
    }

    private void OnZoomInClick(object? sender, RoutedEventArgs e)
    {
        this.FindControl<RelationNetworkControl>("NetworkControl")?.ZoomIn();
    }

    private void OnZoomOutClick(object? sender, RoutedEventArgs e)
    {
        this.FindControl<RelationNetworkControl>("NetworkControl")?.ZoomOut();
    }

    private void OnResetViewClick(object? sender, RoutedEventArgs e)
    {
        this.FindControl<RelationNetworkControl>("NetworkControl")?.ResetView();
    }
}
