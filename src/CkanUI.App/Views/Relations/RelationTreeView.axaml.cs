using Avalonia.Controls;
using Avalonia.Input;
using CkanUI.ViewModels.Relations;

namespace CkanUI.App.Views.Relations;

/// <summary>关系树视图，将双击操作委托给 VM。</summary>
public partial class RelationTreeView : UserControl
{
    public RelationTreeView() => InitializeComponent();
    private void OnNodeDoubleTapped(object? sender,TappedEventArgs e) { if(sender is Control {DataContext:RelationNodeViewModel node} && DataContext is RelationTreeViewModel vm) vm.Navigate(node); }
}
