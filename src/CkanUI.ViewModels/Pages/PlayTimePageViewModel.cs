using System.Collections.ObjectModel;
using System.Globalization;
using CkanUI.Services.Abstractions;
using CkanUI.Services.Abstractions.Dtos;
using CkanUI.ViewModels.Pages.PlayTime;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace CkanUI.ViewModels.Pages;

/// <summary>实例累计游戏时长列表；编辑提交时校验并立即持久化。</summary>
public sealed partial class PlayTimePageViewModel : PageViewModel
{
    private readonly IPlayTimeService _service;
    public PlayTimePageViewModel(IPlayTimeService service) => _service = service ?? throw new ArgumentNullException(nameof(service));
    public override string Title => "游戏时长";
    public ObservableCollection<PlayTimeRowViewModel> Entries { get; } = new();
    [ObservableProperty] private string? _errorText;
    public bool ShowEmptyState => Entries.Count == 0;

    protected override void OnActivated()
    {
        base.OnActivated();
        Refresh();
    }

    [RelayCommand]
    private void Refresh()
    {
        Entries.Clear();
        foreach (var item in _service.Load()) Entries.Add(new PlayTimeRowViewModel(item.InstanceName, item.Hours));
        OnPropertyChanged(nameof(ShowEmptyState));
        ErrorText = null;
    }

    [RelayCommand]
    private void BeginEdit(PlayTimeRowViewModel? row)
    {
        if (row is null) return;
        row.EditText = row.Hours.ToString("0", CultureInfo.CurrentCulture);
        row.IsEditing = true;
        ErrorText = null;
    }

    [RelayCommand]
    private void CancelEdit(PlayTimeRowViewModel? row) { if (row is not null) row.IsEditing = false; ErrorText = null; }

    [RelayCommand]
    private void CommitEdit(PlayTimeRowViewModel? row)
    {
        if (row is null) return;
        if (!double.TryParse(row.EditText, NumberStyles.Number, CultureInfo.CurrentCulture, out var hours) || !double.IsFinite(hours) || hours < 0)
        { ErrorText = "请输入大于或等于零的有效小时数。"; return; }
        if (!_service.Save(row.InstanceName, hours)) { ErrorText = "游戏时长保存失败，请检查文件权限后重试。"; return; }
        row.Hours = Math.Round(hours, 0, MidpointRounding.AwayFromZero);
        row.IsEditing = false; ErrorText = null;
    }
}
