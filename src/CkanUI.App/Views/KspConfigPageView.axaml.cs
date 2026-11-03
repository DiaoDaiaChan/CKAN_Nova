using Avalonia.Controls;
using CkanUI.ViewModels.Pages;

namespace CkanUI.App.Views;

/// <summary>
/// KSP 原生配置视图 (settings.cfg 与官方 dictionary.cfg 本地化)。
/// </summary>
public partial class KspConfigPageView : UserControl
{
    public KspConfigPageView()
    {
        InitializeComponent();
        Loaded += OnLoaded;
    }

    private async void OnLoaded(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (DataContext is KspConfigPageViewModel vm && !vm.IsLoading && vm.FilteredItems.Count == 0)
        {
            await vm.LoadConfigAsync();
        }
    }
}
