using System;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using CkanUI.App.Views.Dialogs;
using CkanUI.ViewModels;
using CkanUI.ViewModels.Dialogs;

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

    /// <summary>打开关于框（12 篇 F-55）；外部链接交给 Avalonia 的 Launcher，失败由 VM 显示提示。</summary>
    private async void OnAboutClick(object? sender, RoutedEventArgs e)
    {
        var top = TopLevel.GetTopLevel(this);
        Func<string, Task<bool>> openExternal = async url =>
        {
            if (top is null || !Uri.TryCreate(url, UriKind.Absolute, out var uri))
            {
                return false;
            }

            return await top.Launcher.LaunchUriAsync(uri);
        };

        var viewModel = new AboutDialogViewModel(AboutInfoFactory.Create(), openExternal);
        var window = new AboutDialogView { DataContext = viewModel };

        if (IsVisible)
        {
            await window.ShowDialog(this);
        }
        else
        {
            window.Show();
        }
    }
}
