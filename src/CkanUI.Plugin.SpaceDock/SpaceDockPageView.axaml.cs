using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace CkanUI.Plugin.SpaceDock;

/// <summary>SpaceDock 页视图：只处理回车触发搜索，其余交互全部走绑定。</summary>
public sealed partial class SpaceDockPageView : UserControl
{
    /// <summary>创建视图（ViewLocator 要求无参构造）。</summary>
    public SpaceDockPageView()
    {
        InitializeComponent();
    }

    private void OnSearchKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || DataContext is not SpaceDockPageViewModel viewModel)
        {
            return;
        }

        if (viewModel.SearchCommand.CanExecute(null))
        {
            viewModel.SearchCommand.Execute(null);
        }

        e.Handled = true;
    }
}
