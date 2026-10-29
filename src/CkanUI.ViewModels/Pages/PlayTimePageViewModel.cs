using System.Collections.ObjectModel;
using System.Globalization;
using CkanUI.Localization;
using CkanUI.Services.Abstractions;
using CkanUI.Services.Abstractions.Dtos;
using CkanUI.ViewModels.Pages.PlayTime;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace CkanUI.ViewModels.Pages;

/// <summary>实例累计游戏时长列表；编辑提交时校验并立即持久化，选中一行可以重置它的累计时长。</summary>
public sealed partial class PlayTimePageViewModel : PageViewModel
{
    private readonly IPlayTimeService _service;
    private readonly IDialogService? _dialogs;

    /// <param name="service">时长读写服务。</param>
    /// <param name="dialogs">
    /// 二次确认用的对话框。宿主没给时「重置时长」直接不可用——清零是破坏性操作，
    /// 没有「用户确认」这一环就宁可不给入口，也不静默改数据（与安装历史页的删除/清空同一条规矩）。
    /// </param>
    public PlayTimePageViewModel(IPlayTimeService service, IDialogService? dialogs = null)
    {
        _service = service ?? throw new ArgumentNullException(nameof(service));
        _dialogs = dialogs;
    }

    public override string Title => Text("游戏时长", LocalizationKeys.NavRailPlayTime);

    public ObservableCollection<PlayTimeRowViewModel> Entries { get; } = new();

    [ObservableProperty] private string? _errorText;

    [ObservableProperty] private string _statusText = string.Empty;

    [ObservableProperty] private PlayTimeRowViewModel? _selectedEntry;

    public bool ShowEmptyState => Entries.Count == 0;

    /// <summary>是否选中了某个实例。</summary>
    public bool HasSelection => SelectedEntry is not null;

    /// <summary>是否有状态栏文案（重置成功/取消后给一句回执，空字符串时不占位）。</summary>
    public bool HasStatus => StatusText.Length > 0;

    /// <summary>「重置时长」是否可用：选中了某个实例，且宿主提供了确认对话框。</summary>
    public bool CanReset => SelectedEntry is not null && _dialogs is not null;

    protected override void OnActivated()
    {
        base.OnActivated();
        Refresh();
    }

    [RelayCommand]
    private void Refresh()
    {
        // 重建行对象后选中项会丢：按实例名接回来，重置完用户还能看到刚被清零的那一行。
        var selectedName = SelectedEntry?.InstanceName;
        Entries.Clear();
        foreach (var item in _service.Load()) Entries.Add(new PlayTimeRowViewModel(item.InstanceName, item.Hours));
        SelectedEntry = selectedName is null
            ? null
            : Entries.FirstOrDefault(entry => string.Equals(entry.InstanceName, selectedName, StringComparison.Ordinal));
        OnPropertyChanged(nameof(ShowEmptyState));
        ErrorText = null;
    }

    /// <summary>把选中实例的累计时长清零：先二次确认，再走服务的重置，成功后刷新列表并给出状态栏文案。</summary>
    [RelayCommand(CanExecute = nameof(CanReset))]
    private async Task Reset()
    {
        if (SelectedEntry is not { } row)
        {
            ErrorText = Text("请先选中一行再重置。", LocalizationKeys.PlayTimeErrorNoSelection);
            return;
        }

        if (_dialogs is not { } dialogs)
        {
            ErrorText = Text("当前环境没有确认对话框，已取消这次操作。", LocalizationKeys.PlayTimeErrorNoDialog);
            return;
        }

        StatusText = string.Empty;
        try
        {
            var confirmed = await dialogs.ConfirmAsync(
                Text("重置「{0}」的游玩时长？", LocalizationKeys.PlayTimeResetConfirmTitle, row.InstanceName),
                Text("将把「{0}」的累计游玩时长清零（手动补录的与会话自动累计的一起清），重置后无法撤销。", LocalizationKeys.PlayTimeResetConfirmBody, row.InstanceName));
            if (!confirmed)
            {
                StatusText = Text("已取消重置。", LocalizationKeys.PlayTimeStatusResetCancelled);
                return;
            }

            var result = await _service.ResetAsync(row.InstanceName);
            if (!result.IsSuccess)
            {
                var failure = string.IsNullOrWhiteSpace(result.Message)
                    ? Text("重置「{0}」的游玩时长失败，请稍后重试。", LocalizationKeys.PlayTimeErrorResetFailed, row.InstanceName)
                    : result.Message!;
                var message = result.Retryable
                    ? Text("{0}（可重试）", LocalizationKeys.PlayTimeRetryableSuffix, failure)
                    : failure;
                ErrorText = message;
                StatusText = message;
                return;
            }

            Refresh();
            StatusText = Text("已重置「{0}」的时长。", LocalizationKeys.PlayTimeStatusReset, row.InstanceName);
            ErrorText = null;
        }
        catch (Exception ex)
        {
            // 服务层约定不抛异常，这里只是最后一道兜底：宁可在错误栏里说清楚，也不让异常穿透到 UI 线程。
            var message = Text("重置「{0}」的游玩时长时出现异常：{1}", LocalizationKeys.PlayTimeErrorResetException, row.InstanceName, ex.Message);
            ErrorText = message;
            StatusText = message;
        }
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
        { ErrorText = Text("请输入大于或等于零的有效小时数。", LocalizationKeys.PlayTimeErrorHoursInvalid); return; }
        if (!_service.Save(row.InstanceName, hours)) { ErrorText = Text("游戏时长保存失败，请检查文件权限后重试。", LocalizationKeys.PlayTimeErrorSaveFailed); return; }
        row.Hours = Math.Round(hours, 0, MidpointRounding.AwayFromZero);
        row.IsEditing = false; ErrorText = null;
    }

    partial void OnStatusTextChanged(string value) => OnPropertyChanged(nameof(HasStatus));

    partial void OnSelectedEntryChanged(PlayTimeRowViewModel? value)
    {
        OnPropertyChanged(nameof(HasSelection));
        OnPropertyChanged(nameof(CanReset));
        ResetCommand.NotifyCanExecuteChanged();
    }
}
