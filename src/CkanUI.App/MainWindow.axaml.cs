using Avalonia.Controls;
using CkanUI.ViewModels;

namespace CkanUI.App;

/// <summary>主窗口（03 篇 §1 外壳）。导航只做命令转发，状态全部在 ShellViewModel 里。</summary>
public sealed partial class MainWindow : Window
{
    public MainWindow() => InitializeComponent();

    private void OnNavSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (NavList.SelectedItem is NavItem item && DataContext is ShellViewModel shell)
        {
            // 命令本身是异步的且自带异常收口，这里不等待；状态栏由 ShellViewModel 更新。
            shell.NavigateCommand.Execute(item.Key);
        }
    }
}
