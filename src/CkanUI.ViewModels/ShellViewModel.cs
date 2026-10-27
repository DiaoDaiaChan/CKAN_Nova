using CommunityToolkit.Mvvm.ComponentModel;

namespace CkanUI.ViewModels;

/// <summary>主窗口外壳（UI-01 骨架）。</summary>
public sealed partial class ShellViewModel : ObservableObject
{
    [ObservableProperty]
    private string _statusText = "就绪";

    [ObservableProperty]
    private string _title = "CKAN UI";
}
