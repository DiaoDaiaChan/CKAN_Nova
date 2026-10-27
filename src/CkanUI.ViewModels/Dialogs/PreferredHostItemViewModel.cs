using CommunityToolkit.Mvvm.ComponentModel;

namespace CkanUI.ViewModels.Dialogs;

/// <summary>
/// 首选主机列表项 ViewModel（F-15 / UI-30）。
/// </summary>
public sealed partial class PreferredHostItemViewModel : ObservableObject
{
    [ObservableProperty]
    private string? _host;

    [ObservableProperty]
    private int _priority;

    [ObservableProperty]
    private bool _isPlaceholder;

    public PreferredHostItemViewModel(string? host, int priority, bool isPlaceholder = false)
    {
        _host = host;
        _priority = priority;
        _isPlaceholder = isPlaceholder;
    }

    /// <summary>
    /// 显示标签。占位符显示为友好文案。
    /// </summary>
    public string DisplayText => IsPlaceholder || string.IsNullOrWhiteSpace(Host)
        ? "<其余所有主机 / All Other Hosts>"
        : Host!;

    partial void OnHostChanged(string? value)
    {
        OnPropertyChanged(nameof(DisplayText));
    }

    partial void OnIsPlaceholderChanged(bool value)
    {
        OnPropertyChanged(nameof(DisplayText));
    }
}
