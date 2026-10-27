using CommunityToolkit.Mvvm.ComponentModel;

namespace CkanUI.ViewModels.Pages.PlayTime;

/// <summary>可编辑的游戏时长行。</summary>
public sealed partial class PlayTimeRowViewModel(string instanceName, double hours) : ObservableObject
{
    public string InstanceName { get; } = instanceName;
    [ObservableProperty] private double _hours = hours;
    [ObservableProperty] private string _editText = string.Empty;
    [ObservableProperty] private bool _isEditing;
}
