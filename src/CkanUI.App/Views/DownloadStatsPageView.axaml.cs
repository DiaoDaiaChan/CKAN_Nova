using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using CkanUI.ViewModels.Pages;
using CkanUI.ViewModels.Pages.Stats;

namespace CkanUI.App.Views;

/// <summary>下载统计页（12 篇 F-47 / 06 篇 UI-25）。</summary>
public partial class DownloadStatsPageView : UserControl
{
    /// <summary>构造并装载 XAML。</summary>
    public DownloadStatsPageView() => InitializeComponent();

    /// <summary>双击行直接跳转到模组主页/详情。</summary>
    private void OnRowDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (DataContext is not DownloadStatsPageViewModel vm)
        {
            return;
        }

        var target = (e.Source as Visual)?.DataContext as DownloadStatsRowViewModel
            ?? (sender as DataGrid)?.SelectedItem as DownloadStatsRowViewModel
            ?? vm.SelectedRow;

        if (target is not null && vm.OpenModCommand.CanExecute(target))
        {
            vm.OpenModCommand.Execute(target);
        }
    }
}
