using CkanUI.Localization;
using CkanUI.Services.Abstractions;
using CkanUI.Services.Abstractions.Dtos;
using CkanUI.ViewModels.Pages.InstallHistory;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace CkanUI.ViewModels.Pages;

/// <summary>
/// 安装历史页（F-44 / UI-23、12 篇 §2、04 篇 §5）：
/// 列出每次安装/卸载/升级的时间戳、当时的已装集合分组，并支持"从历史重装"。
/// </summary>
/// <remarks>
/// 页面只做编排与文案：落盘、依赖求解、安装都在服务层；重装一律走
/// <c>IInstallHistoryService.RestoreAsync</c>（其内部复用 <c>IChangeSetService</c> + <c>IInstallerService</c>），
/// 页面自己绝不拼安装逻辑（工程约定 §7）。
/// </remarks>
public sealed partial class InstallHistoryPageViewModel : PageViewModel
{
    private const string LoadContext = "读取安装历史";
    private const string PreviewContext = "预览从历史重装";
    private const string RestoreContext = "从历史重装";
    private const string DeleteContext = "删除安装历史记录";
    private const string ClearContext = "清空安装历史";

    private readonly IInstallHistoryService _history;
    private readonly IErrorReporter _errors;
    private readonly IDialogService? _dialogs;
    private readonly IInstallHistoryMaintenance? _maintenance;
    private readonly IProgress<OperationProgress> _restoreProgress;
    private InstallHistoryRestorePlan? _plan;

    /// <summary>无参构造：供设计时预览与 XAML 预览使用（依赖都是空实现）。</summary>
    public InstallHistoryPageViewModel()
        : this(new EmptyInstallHistoryService(), new EmptyErrorReporter())
    {
    }

    /// <summary>带服务注入的构造函数。</summary>
    /// <param name="history">安装历史服务。</param>
    /// <param name="errors">错误上报。</param>
    /// <param name="dialogs">确认对话框；为 null 时不提供删除/清空（破坏性操作必须先确认）。</param>
    public InstallHistoryPageViewModel(IInstallHistoryService history, IErrorReporter errors, IDialogService? dialogs = null)
    {
        _history = history ?? throw new ArgumentNullException(nameof(history));
        _errors = errors ?? throw new ArgumentNullException(nameof(errors));
        _dialogs = dialogs;

        // 能力探测而不是硬依赖：老实现/测试替身没实现维护接口时，页面自动隐藏删除入口。
        _maintenance = history as IInstallHistoryMaintenance;
        _restoreProgress = new InlineProgress<OperationProgress>(OnRestoreProgress);
        SetStatus(Text("选择一条历史记录，查看当时的已装集合，或从该时间点重装。", LocalizationKeys.InstallHistoryStatusSelectHint));
        OnPropertyChanged(nameof(CanEditHistory));
    }

    /// <inheritdoc />
    public override string Title => Text("安装历史", LocalizationKeys.NavRailInstallHistory);

    /// <summary>当前实例的说明文案。</summary>
    public string InstanceTitleText => _history.HasActiveInstance && _history.InstanceName is { Length: > 0 } name
        ? Text("当前实例：{0}（历史文件：<GameDir>/CKAN/install_history.jsonl）", LocalizationKeys.InstallHistoryInstanceTitle, name)
        : Text("当前没有选中的游戏实例，安装历史不可用。", LocalizationKeys.InstallHistoryInstanceMissing);

    // ---- 列表 --------------------------------------------------------------

    [ObservableProperty]
    private IReadOnlyList<InstallHistoryEntryRowViewModel> _entries = Array.Empty<InstallHistoryEntryRowViewModel>();

    [ObservableProperty]
    private InstallHistoryEntryRowViewModel? _selectedEntry;

    /// <summary>历史列表是否有行。</summary>
    public bool HasEntries => Entries.Count > 0;

    /// <summary>是否显示空状态。</summary>
    public bool ShowEmptyState => Entries.Count == 0 && !IsBusy;

    /// <summary>列表条数文案。</summary>
    public string CountText => Entries.Count == 0
        ? Text("暂无历史记录", LocalizationKeys.InstallHistoryCountNone)
        : Text(
            "共 {0} 条历史记录",
            LocalizationKeys.InstallHistoryCountSummary,
            Entries.Count);

    // ---- 删除 / 清空（F-44 剩余两项缺口的界面入口）-------------------------

    /// <summary>是否提供删除/清空能力：服务实现了维护接口、且宿主给了确认对话框。</summary>
    public bool CanEditHistory => _maintenance is not null && _dialogs is not null;

    /// <summary>删除按钮是否可用：能力具备、不忙、选中了某一条。</summary>
    public bool CanDeleteSelected => CanEditHistory && !IsBusy && SelectedEntry is not null;

    /// <summary>清空按钮是否可用：能力具备、不忙、列表非空。</summary>
    public bool CanClearHistory => CanEditHistory && !IsBusy && HasEntries;

    // ---- 选中项的分组 ------------------------------------------------------

    [ObservableProperty]
    private IReadOnlyList<InstallHistoryModuleRowViewModel> _installedModules = Array.Empty<InstallHistoryModuleRowViewModel>();

    [ObservableProperty]
    private IReadOnlyList<InstallHistoryModuleRowViewModel> _missingModules = Array.Empty<InstallHistoryModuleRowViewModel>();

    /// <summary>是否选中了历史记录。</summary>
    public bool HasSelection => SelectedEntry is not null;

    /// <summary>选中记录的标题文案。</summary>
    public string SelectionTitleText => SelectedEntry is { } row
        ? Text("{0}｜{1}｜{2}", LocalizationKeys.InstallHistorySelectionTitle, row.TimestampText, row.OperationText, row.OutcomeText)
        : Text("未选择历史记录", LocalizationKeys.InstallHistorySelectionNone);

    /// <summary>选中记录的分组统计。</summary>
    public string SelectionSummaryText => SelectedEntry is { } row
        ? Text("{0}（快照共 {1} 个模组）", LocalizationKeys.InstallHistorySelectionSummary, row.GroupCountText, row.SnapshotModuleCount)
        : Text("选中一条记录后显示当时的已装集合。", LocalizationKeys.InstallHistorySelectionSummaryNone);

    /// <summary>已装分组标题。</summary>
    public string InstalledGroupTitle => Text(
            "已安装（{0}）",
            LocalizationKeys.InstallHistoryGroupInstalled,
            InstalledModules.Count);

    /// <summary>未装分组标题。</summary>
    public string MissingGroupTitle => Text(
            "未安装（{0}）",
            LocalizationKeys.InstallHistoryGroupMissing,
            MissingModules.Count);

    /// <summary>已装分组是否有内容。</summary>
    public bool HasInstalledModules => InstalledModules.Count > 0;

    /// <summary>未装分组是否有内容。</summary>
    public bool HasMissingModules => MissingModules.Count > 0;

    // ---- 重装 --------------------------------------------------------------

    [ObservableProperty]
    private string _restorePreviewText = string.Empty;

    [ObservableProperty]
    private string _restoreResultText = string.Empty;

    /// <summary>重装预览是否可显示（重装前就能看到"将被变更的模组数量"）。</summary>
    public bool HasRestorePreview => RestorePreviewText.Length > 0;

    /// <summary>重装结果是否可显示。</summary>
    public bool HasRestoreResult => RestoreResultText.Length > 0;

    /// <summary>重装按钮是否可用：不忙、选中了带快照的历史记录。</summary>
    public bool CanRestore => !IsBusy && SelectedEntry is { CanRestore: true };

    /// <summary>
    /// 是否同时卸载快照之外的模组：开启后重装会把当前实例里多装的那些模组一并卸载，
    /// 使已装集合与选中时间点完全一致；默认关闭（只按快照里的模组算变更）。
    /// 该开关随请求一起带到服务层（<c>InstallHistoryRestoreRequest.RemoveExtras</c>），预览与执行都生效。
    /// </summary>
    [ObservableProperty]
    private bool _removeExtras;

    /// <summary>重装按钮不可用时的解释文案。</summary>
    public string RestoreHintText
    {
        get
        {
            if (SelectedEntry is not { } row)
            {
                return Text("先在左侧选中一条历史记录。", LocalizationKeys.InstallHistoryRestoreHintNoSelection);
            }

            if (!row.HasSnapshot)
            {
                return Text("这条历史没有已装集合快照，无法重装。", LocalizationKeys.InstallHistoryRestoreHintNoSnapshot);
            }

            if (row.SnapshotModuleCount == 0)
            {
                return Text("该时间点的快照里没有任何模组，无需重装。", LocalizationKeys.InstallHistoryRestoreHintEmptySnapshot);
            }

            return RemoveExtras
                ? Text("重装会把当前实例的已装集合改成该时间点的样子，并卸载快照之外的模组（这些模组会被删除）。", LocalizationKeys.InstallHistoryRestoreHintRemoveExtras)
                : Text("重装会把当前实例的已装集合改成该时间点的样子（不会卸载快照之外的模组）。", LocalizationKeys.InstallHistoryRestoreHintKeepExtras);
        }
    }

    /// <summary>重装预览：将被变更的模组数量（按当前注册表算，选中即更新）。</summary>
    public string BuildRestorePreview(InstallHistoryEntryRowViewModel row)
    {
        ArgumentNullException.ThrowIfNull(row);
        if (!row.HasSnapshot || row.SnapshotModuleCount == 0)
        {
            return string.Empty;
        }

        // 分组按"当前是否装着"分（与旧 GUI 一致）：版本不同的模组仍在已装分组里，但同样是待变更项。
        var reinstall = row.MissingModules.Count;
        var changeVersion = row.InstalledModules.Count(module => module.IsChanged);
        var parts = new List<string>
        {
            Text(
            "该时间点共 {0} 个模组",
            LocalizationKeys.InstallHistoryPreviewSnapshot,
            row.SnapshotModuleCount),
        };
        parts.Add(Text(
            "已装 {0} 个",
            LocalizationKeys.InstallHistoryPreviewInstalled,
            row.InstalledCount));
        parts.Add(Text(
            "需变更 {0} 个（新装 {1}，换版本 {2}）",
            LocalizationKeys.InstallHistoryPreviewChanges,
            reinstall + changeVersion,
            reinstall,
            changeVersion));
        var text = string.Join("｜", parts) + "。";

        // 勾选「同时卸载快照之外的模组」后，本地这份按快照估的数字会偏小：真正要卸载哪些由服务端算。
        return RemoveExtras
            ? text + Text("已勾选同时卸载快照之外的模组，实际还会卸载不在该快照里的模组。", LocalizationKeys.InstallHistoryPreviewRemoveExtrasNote)
            : text;
    }

    // ---- 状态 --------------------------------------------------------------

    [ObservableProperty]
    private string _statusText = string.Empty;

    [ObservableProperty]
    private string? _errorText;

    [ObservableProperty]
    private string? _errorDetail;

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private string _busyText = string.Empty;

    [ObservableProperty]
    private string _progressText = string.Empty;

    [ObservableProperty]
    private double _progressPercent;

    /// <summary>是否有错误文案。</summary>
    public bool HasError => !string.IsNullOrEmpty(ErrorText);

    /// <summary>是否有错误细节。</summary>
    public bool HasErrorDetail => !string.IsNullOrEmpty(ErrorDetail);

    /// <summary>是否空闲。</summary>
    public bool IsIdle => !IsBusy;

    /// <summary>是否有进度文案。</summary>
    public bool HasProgress => ProgressText.Length > 0;

    /// <summary>重新读取安装历史。</summary>
    [RelayCommand]
    private Task LoadAsync() => RunAsync(
        () => LoadCoreAsync(SelectedEntry?.Timestamp),
        LoadContext,
        Text("正在读取安装历史…", LocalizationKeys.InstallHistoryBusyLoad));

    /// <summary>
    /// 删除当前选中的那一条历史（连同它的快照）；必须先经确认对话框，删完重新读盘以重算统计。
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanDeleteSelected))]
    private Task DeleteSelectedAsync() => RunAsync(
        async () =>
        {
            if (SelectedEntry is not { } row)
            {
                SetError(Text("请先选中一条历史记录。", LocalizationKeys.InstallHistoryErrorNoSelection), null);
                return;
            }

            if (_maintenance is not { } maintenance)
            {
                SetError(Text("当前安装历史服务不支持删除。", LocalizationKeys.InstallHistoryErrorDeleteUnsupported), null);
                return;
            }

            if (!await ConfirmAsync(
                    Text("删除这条历史？", LocalizationKeys.InstallHistoryConfirmDeleteTitle),
                    Text("将删除 {0}｜{1} 这一条历史及其已装集合快照，删除后无法撤销。", LocalizationKeys.InstallHistoryConfirmDeleteMessage, row.TimestampText, row.OperationText)))
            {
                return;
            }

            var deleted = await maintenance.DeleteAsync(row.Timestamp);
            if (!deleted.IsSuccess)
            {
                SetError(InstallHistoryText.Compose(deleted.Message, Text("删除安装历史失败。", LocalizationKeys.InstallHistoryErrorDeleteFailed), deleted.Retryable), deleted.Detail);
                return;
            }

            await LoadCoreAsync(null);
            SetStatus(deleted.Value == 0
                ? Text("这条历史已经不存在（列表已刷新）。", LocalizationKeys.InstallHistoryStatusGone)
                : Text(
                "已删除 1 条历史记录（{0} 行）。",
                LocalizationKeys.InstallHistoryStatusDeleted,
                deleted.Value));
        },
        DeleteContext,
        Text("正在删除安装历史…", LocalizationKeys.InstallHistoryBusyDelete));

    /// <summary>清空当前实例的全部安装历史；必须先经确认对话框。</summary>
    [RelayCommand(CanExecute = nameof(CanClearHistory))]
    private Task ClearHistoryAsync() => RunAsync(
        async () =>
        {
            if (_maintenance is not { } maintenance)
            {
                SetError(Text("当前安装历史服务不支持清空。", LocalizationKeys.InstallHistoryErrorClearUnsupported), null);
                return;
            }

            if (!await ConfirmAsync(
                    Text("清空安装历史？", LocalizationKeys.InstallHistoryConfirmClearTitle),
                    Text("将删除当前实例的全部安装历史（{0}），清空后无法撤销。", LocalizationKeys.InstallHistoryConfirmClearMessage, CountText)))
            {
                return;
            }

            var cleared = await maintenance.ClearAsync();
            if (!cleared.IsSuccess)
            {
                SetError(InstallHistoryText.Compose(cleared.Message, Text("清空安装历史失败。", LocalizationKeys.InstallHistoryErrorClearFailed), cleared.Retryable), cleared.Detail);
                return;
            }

            await LoadCoreAsync(null);
            SetStatus(cleared.Value == 0
                ? Text("安装历史本来就是空的。", LocalizationKeys.InstallHistoryStatusAlreadyEmpty)
                : Text(
                "已清空安装历史（{0} 行）。",
                LocalizationKeys.InstallHistoryStatusCleared,
                cleared.Value));
        },
        ClearContext,
        Text("正在清空安装历史…", LocalizationKeys.InstallHistoryBusyClear));

    /// <summary>读盘并重建列表（删除/清空后也要走这里，统计文案才跟着变）。</summary>
    private async Task LoadCoreAsync(DateTimeOffset? preferredTimestamp)
    {
        var result = await _history.LoadAsync();
        if (!result.IsSuccess || result.Value is not { } entries)
        {
            SetError(InstallHistoryText.Compose(result.Message, Text("读取安装历史失败。", LocalizationKeys.InstallHistoryErrorLoadFailed), result.Retryable), result.Detail);
            return;
        }

        ApplyEntries(entries, preferredTimestamp);
        SetStatus(Entries.Count == 0
            ? Text("还没有历史记录：装/卸/升级任意模组后会自动记一条。", LocalizationKeys.InstallHistoryStatusEmptyHistory)
            : Text(
            "读取完成：{0}。",
            LocalizationKeys.InstallHistoryStatusLoaded,
            CountText));
    }

    /// <summary>
    /// 破坏性操作的确认闸门：没有对话框就没有「用户确认」这一环，一律不执行——
    /// 静默删数据的代价远大于少一个功能。
    /// </summary>
    private async Task<bool> ConfirmAsync(string title, string message)
    {
        if (_dialogs is not { } dialogs)
        {
            SetError(Text("当前环境没有确认对话框，已取消这次操作。", LocalizationKeys.InstallHistoryErrorNoDialogs), null);
            return false;
        }

        return await dialogs.ConfirmAsync(title, message);
    }

    /// <summary>把当前实例的已装集合回退到选中历史的时间点。</summary>
    [RelayCommand(CanExecute = nameof(CanRestore))]
    private Task RestoreAsync() => RunAsync(
        async () =>
        {
            if (SelectedEntry is not { } row)
            {
                SetError(Text("请先选中一条历史记录。", LocalizationKeys.InstallHistoryErrorNoSelection), null);
                return;
            }

            RestoreResultText = string.Empty;
            _plan = null;

            var preview = await _history.PreviewRestoreAsync(
                new InstallHistoryRestoreRequest(row.Timestamp) { RemoveExtras = RemoveExtras });
            if (!preview.IsSuccess || preview.Value is not { } plan)
            {
                RestorePreviewText = string.Empty;
                OnPropertyChanged(nameof(HasRestorePreview));
                SetError(InstallHistoryText.Compose(preview.Message, Text("预览重装失败。", LocalizationKeys.InstallHistoryErrorPreviewFailed), preview.Retryable), preview.Detail);
                return;
            }

            if (plan.HasConflicts)
            {
                RestorePreviewText = DescribePlan(plan);
                OnPropertyChanged(nameof(HasRestorePreview));
                SetError(Text(
                "重装目标存在依赖冲突，已中止：{0}",
                LocalizationKeys.InstallHistoryErrorConflicts,
                string.Join("；", plan.Conflicts)), null);
                return;
            }

            if (plan.IsEmpty)
            {
                RestorePreviewText = Text("该时间点与当前已装集合一致，无需重装。", LocalizationKeys.InstallHistoryPreviewNoChange);
                OnPropertyChanged(nameof(HasRestorePreview));
                SetStatus(Text("无需重装：当前已装集合已经与该时间点一致。", LocalizationKeys.InstallHistoryStatusAlreadyMatches));
                return;
            }

            _plan = plan;
            RestorePreviewText = DescribePlan(plan);
            OnPropertyChanged(nameof(HasRestorePreview));
            ProgressPercent = 0;
            ProgressText = Text("正在从历史重装…", LocalizationKeys.InstallHistoryBusyRestoring);

            var restore = await _history.RestoreAsync(
                new InstallHistoryRestoreRequest(row.Timestamp) { RemoveExtras = RemoveExtras },
                _restoreProgress);
            if (!restore.IsSuccess || restore.Value is not { } restored)
            {
                ProgressText = string.Empty;
                SetError(InstallHistoryText.Compose(restore.Message, Text("从历史重装失败，可重试。", LocalizationKeys.InstallHistoryErrorRestoreFailed), restore.Retryable), restore.Detail);
                return;
            }

            ProgressPercent = 100;
            ProgressText = Text("重装结束。", LocalizationKeys.InstallHistoryProgressDone);
            RestoreResultText = DescribeResult(restored);
            SetStatus(restored.IsFullSuccess
                ? Text(
                "重装完成：安装 {0} 个，卸载 {1} 个。",
                LocalizationKeys.InstallHistoryStatusRestored,
                restored.Installed.Count,
                restored.Removed.Count)
                : Text(
                "重装结束：成功 {0} 个，失败 {1} 个。",
                LocalizationKeys.InstallHistoryStatusRestorePartial,
                restored.Installed.Count,
                restored.Failed.Count));
        },
        RestoreContext,
        Text("正在从历史重装…", LocalizationKeys.InstallHistoryBusyRestoring));

    /// <summary>按服务返回的条目重建列表，并尽量保留原来的选中项。</summary>
    public void ApplyEntries(IReadOnlyList<InstallHistoryEntry> entries, DateTimeOffset? preferredTimestamp = null)
    {
        ArgumentNullException.ThrowIfNull(entries);
        var rows = entries.Select(entry => new InstallHistoryEntryRowViewModel(entry)).ToArray();
        Entries = rows;

        var target = preferredTimestamp is { } stamp
            ? rows.FirstOrDefault(row => row.Timestamp == stamp)
            : null;
        SelectedEntry = target ?? rows.FirstOrDefault();
        OnPropertyChanged(nameof(InstanceTitleText));
        OnPropertyChanged(nameof(CountText));
        OnPropertyChanged(nameof(HasEntries));
        OnPropertyChanged(nameof(ShowEmptyState));
        NotifySelectionState();
    }

    private void NotifySelectionState()
    {
        var row = SelectedEntry;
        InstalledModules = row?.InstalledModules ?? Array.Empty<InstallHistoryModuleRowViewModel>();
        MissingModules = row?.MissingModules ?? Array.Empty<InstallHistoryModuleRowViewModel>();
        RestorePreviewText = row is null ? string.Empty : BuildRestorePreview(row);
        RestoreResultText = string.Empty;
        _plan = null;

        OnPropertyChanged(nameof(HasSelection));
        OnPropertyChanged(nameof(SelectionTitleText));
        OnPropertyChanged(nameof(SelectionSummaryText));
        OnPropertyChanged(nameof(InstalledGroupTitle));
        OnPropertyChanged(nameof(MissingGroupTitle));
        OnPropertyChanged(nameof(HasInstalledModules));
        OnPropertyChanged(nameof(HasMissingModules));
        OnPropertyChanged(nameof(HasRestorePreview));
        OnPropertyChanged(nameof(HasRestoreResult));
        OnPropertyChanged(nameof(RestoreHintText));
        OnPropertyChanged(nameof(CanRestore));
        RestoreCommand.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(CanDeleteSelected));
        DeleteSelectedCommand.NotifyCanExecuteChanged();
    }

    private string DescribePlan(InstallHistoryRestorePlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        var parts = new List<string>
        {
            Text(
            "重装将变更 {0} 个模组",
            LocalizationKeys.InstallHistoryPlanChanges,
            plan.ChangeCount),
        };
        if (plan.InstallCount > 0)
        {
            parts.Add(Text(
            "新装 {0}",
            LocalizationKeys.InstallHistoryPlanInstall,
            plan.InstallCount));
        }

        if (plan.UpgradeCount > 0)
        {
            parts.Add(Text(
            "换版本 {0}",
            LocalizationKeys.InstallHistoryPlanUpgrade,
            plan.UpgradeCount));
        }

        if (plan.RemoveCount > 0)
        {
            parts.Add(Text(
            "卸载 {0}",
            LocalizationKeys.InstallHistoryPlanRemove,
            plan.RemoveCount));
        }

        if (plan.EstimatedDownloadBytes > 0)
        {
            parts.Add(Text(
            "预计下载 {0} MB",
            LocalizationKeys.InstallHistoryPlanDownload,
            (plan.EstimatedDownloadBytes / (1024d * 1024d)).ToString("0.0")));
        }

        return string.Join("，", parts) + "。";
    }

    private string DescribeResult(InstallHistoryRestoreResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        var parts = new List<string>
        {
            Text(
            "重装结果：安装 {0} 个，卸载 {1} 个，失败 {2} 个",
            LocalizationKeys.InstallHistoryResultSummary,
            result.Installed.Count,
            result.Removed.Count,
            result.Failed.Count),
        };
        if (result.Failed.Count > 0)
        {
            parts.Add(Text(
            "失败清单：{0}",
            LocalizationKeys.InstallHistoryResultFailedList,
            string.Join("、", result.Failed)));
        }

        if (result.Warnings.Count > 0)
        {
            parts.Add(Text(
            "警告：{0}",
            LocalizationKeys.InstallHistoryResultWarnings,
            string.Join("；", result.Warnings)));
        }

        return string.Join("；", parts) + "。";
    }

    private void OnRestoreProgress(OperationProgress progress) => ApplyProgress(progress, Text("正在从历史重装…", LocalizationKeys.InstallHistoryBusyRestoring));

    private void ApplyProgress(OperationProgress progress, string fallback)
    {
        if (progress is null)
        {
            return;
        }

        ProgressPercent = Math.Clamp(progress.Percent, 0, 100);
        ProgressText = string.IsNullOrWhiteSpace(progress.Message)
            ? (string.IsNullOrWhiteSpace(progress.Title) ? fallback : progress.Title)
            : progress.Message!;
        if (progress.State == OperationState.Failed)
        {
            SetError(ProgressText, null);
        }
    }

    private async Task RunAsync(Func<Task> body, string context, string busyText)
    {
        if (IsBusy)
        {
            return;
        }

        IsBusy = true;
        BusyText = busyText;
        ErrorText = null;
        ErrorDetail = null;
        try
        {
            await body();
        }
        catch (Exception ex)
        {
            _errors.Report(ex, context);
            SetError(InstallHistoryText.FromException(ex, Text("安装历史操作失败。", LocalizationKeys.InstallHistoryErrorOperationFailed)), ex.Message);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void SetStatus(string message) => StatusText = message;

    private void SetError(string message, string? detail)
    {
        ErrorText = message;
        ErrorDetail = detail;
        StatusText = message;
    }

    /// <inheritdoc />
    protected override void OnActivated()
    {
        base.OnActivated();
        OnPropertyChanged(nameof(InstanceTitleText));
        LoadCommand.Execute(null);
    }

    partial void OnEntriesChanged(IReadOnlyList<InstallHistoryEntryRowViewModel> value)
    {
        OnPropertyChanged(nameof(HasEntries));
        OnPropertyChanged(nameof(ShowEmptyState));
        OnPropertyChanged(nameof(CountText));
        OnPropertyChanged(nameof(CanClearHistory));
        OnPropertyChanged(nameof(CanDeleteSelected));
        ClearHistoryCommand.NotifyCanExecuteChanged();
        DeleteSelectedCommand.NotifyCanExecuteChanged();
    }

    partial void OnSelectedEntryChanged(InstallHistoryEntryRowViewModel? value) => NotifySelectionState();

    partial void OnIsBusyChanged(bool value)
    {
        OnPropertyChanged(nameof(CanRestore));
        OnPropertyChanged(nameof(ShowEmptyState));
        OnPropertyChanged(nameof(IsIdle));
        OnPropertyChanged(nameof(CanDeleteSelected));
        OnPropertyChanged(nameof(CanClearHistory));
        RestoreCommand.NotifyCanExecuteChanged();
        DeleteSelectedCommand.NotifyCanExecuteChanged();
        ClearHistoryCommand.NotifyCanExecuteChanged();
    }

    partial void OnProgressTextChanged(string value) => OnPropertyChanged(nameof(HasProgress));

    partial void OnErrorTextChanged(string? value) => OnPropertyChanged(nameof(HasError));

    partial void OnErrorDetailChanged(string? value) => OnPropertyChanged(nameof(HasErrorDetail));

    partial void OnRestorePreviewTextChanged(string value) => OnPropertyChanged(nameof(HasRestorePreview));

    partial void OnRestoreResultTextChanged(string value) => OnPropertyChanged(nameof(HasRestoreResult));

    partial void OnRemoveExtrasChanged(bool value)
    {
        // 提示语与本地预览都跟这个开关的语义绑定：勾上就要说清「会删文件」。
        OnPropertyChanged(nameof(RestoreHintText));
        if (SelectedEntry is { } row)
        {
            RestorePreviewText = BuildRestorePreview(row);
        }
    }

    partial void OnInstalledModulesChanged(IReadOnlyList<InstallHistoryModuleRowViewModel> value)
    {
        OnPropertyChanged(nameof(InstalledGroupTitle));
        OnPropertyChanged(nameof(HasInstalledModules));
    }

    partial void OnMissingModulesChanged(IReadOnlyList<InstallHistoryModuleRowViewModel> value)
    {
        OnPropertyChanged(nameof(MissingGroupTitle));
        OnPropertyChanged(nameof(HasMissingModules));
    }

    // ---- 无参构造用的空实现（与 ModPacksPageViewModel 同构） ----

    private sealed class EmptyInstallHistoryService : IInstallHistoryService
    {
    }

    private sealed class EmptyErrorReporter : IErrorReporter
    {
        public event Action<ErrorEntry>? Reported
        {
            add { }
            remove { }
        }

        public IReadOnlyList<ErrorEntry> Recent => Array.Empty<ErrorEntry>();

        public void Report(Exception exception, string context, string? userMessage = null)
        {
        }

        public void Report(string context, string userMessage, string? detail = null)
        {
        }
    }

    private sealed class InlineProgress<T>(Action<T> callback) : IProgress<T>
    {
        private readonly Action<T> _callback = callback ?? throw new ArgumentNullException(nameof(callback));

        public void Report(T value) => _callback(value);
    }
}
