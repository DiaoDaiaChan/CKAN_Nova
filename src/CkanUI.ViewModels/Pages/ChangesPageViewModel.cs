using CkanUI.Services.Abstractions;
using CkanUI.Services.Abstractions.Dtos;
using CkanUI.ViewModels.Pages.Changes;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace CkanUI.ViewModels.Pages;

/// <summary>
/// 变更集页（03 篇 §4 V-02 / 06 篇 UI-04·VM-02 / 04 篇 §2 <c>ChangesViewModel</c>）：
/// 摘要 + 明细表 + 冲突区 + 状态机驱动的主按钮。
/// </summary>
/// <remarks>
/// <para>分层约定（与 <see cref="ModsPageViewModel"/> 一致）：本类只做「拿请求 → 调服务 → 丢给纯函数投影 → 整体替换结果」，
/// 文案与格式化全部在 <see cref="ChangesFormatter"/> / <see cref="ChangeRowView"/>，可 headless 测试。</para>
/// <para>异常收口（05 篇）：所有服务调用都裹 try/catch，异常转成 <see cref="StatusText"/> 与 <see cref="ResultText"/>，
/// 并交给 <see cref="IErrorReporter"/>；VM 里没有 async void、没有 .Result/.Wait()。</para>
/// <para>依赖全部可选：<c>IChangeSetService</c>（INT-07）与 <c>IInstallerService</c>（INT-08）尚未实现时，
/// 页面仍能显示请求摘要、给出"安装器将在 M2 接入"的明确提示，不抛异常。</para>
/// </remarks>
public sealed partial class ChangesPageViewModel : PageViewModel, IChangeRequestSink
{
    /// <summary>没有安装器时的固定提示（03 篇 §4 主按钮 + 06 篇 M2 门禁）。</summary>
    public const string MissingInstallerText = "安装器将在 M2 接入（INT-08）：当前只能查看变更预览，还不能真正安装。";

    private readonly IChangeSetService? _changeSets;
    private readonly IInstallerService? _installer;
    private readonly IErrorReporter _errors;
    private readonly IOperationQueue? _queue;
    private readonly IDispatcher? _dispatcher;
    private readonly IResidualCleanupDialogService? _residualCleanup;
    private readonly IInstanceService? _instances;
    private readonly IModDialogService? _modDialogs;
    private readonly IUiSettingsStore? _settingsStore;
    private readonly IRepositoryService? _repository;

    private CancellationTokenSource? _applyCts;
    private int _buildVersion;
    private bool _subscribed;
    private bool _gameRunning;
    private bool _ignoreGameRunning;
    private bool _recommendationsHandled;

    /// <summary>设计器 / 无参构造：不连任何服务，只展示空状态。</summary>
    public ChangesPageViewModel()
        : this(null, null, null, null, null, null, null, null, null, null)
    {
    }

    /// <summary>依赖注入构造：所有依赖可选，缺哪个就退化到对应的降级文案。</summary>
    public ChangesPageViewModel(
        IChangeSetService? changeSets = null,
        IInstallerService? installer = null,
        IErrorReporter? errors = null,
        IOperationQueue? queue = null,
        IDispatcher? dispatcher = null,
        IResidualCleanupDialogService? residualCleanup = null,
        IInstanceService? instances = null,
        IModDialogService? modDialogs = null,
        IUiSettingsStore? settingsStore = null,
        IRepositoryService? repository = null)
    {
        _changeSets = changeSets;
        _installer = installer;
        _errors = errors ?? NullErrorReporter.Instance;
        _queue = queue;
        _dispatcher = dispatcher;
        _residualCleanup = residualCleanup;
        _instances = instances;
        _modDialogs = modDialogs;
        _settingsStore = settingsStore;
        _repository = repository;
    }

    /// <inheritdoc />
    public override string Title => "变更集";

    /// <summary>最近一次异步重建任务（测试用 await 等待其落定）。</summary>
    public Task PendingWork { get; private set; } = Task.CompletedTask;

    /// <summary>当前待计算的请求集合（由模组页或调用方通过 <see cref="SetRequests"/> 灌入）。</summary>
    public IReadOnlyCollection<ChangeRequest> Requests { get; private set; } = Array.Empty<ChangeRequest>();

    /// <inheritdoc />
    public event Action? RequestsApplied;

    // ---- 可观察状态 -------------------------------------------------------

    /// <summary>执行流状态（03 篇 §4.1）。</summary>
    [ObservableProperty]
    private ChangeExecutionState _state = ChangeExecutionState.Idle;

    /// <summary>当前变更集（整体替换，不做增量更新）。</summary>
    [ObservableProperty]
    private ChangeSetPreview _preview = ChangesFormatter.EmptyPreview;

    /// <summary>明细表行（整体替换）。</summary>
    [ObservableProperty]
    private IReadOnlyList<ChangeRowView> _rows = Array.Empty<ChangeRowView>();

    /// <summary>状态栏文本。</summary>
    [ObservableProperty]
    private string _statusText = ChangesFormatter.Summary(null);

    /// <summary>执行进度百分比（0–100）。</summary>
    [ObservableProperty]
    private double _executionPercent;

    /// <summary>执行结果明细节（失败清单 / 成功汇总）。</summary>
    [ObservableProperty]
    private string _resultText = string.Empty;

    /// <summary>最近一条错误上报的用户文案（错误条用，03 篇 §10）。</summary>
    [ObservableProperty]
    private string _lastErrorText = string.Empty;

    // ---- 派生只读属性（供界面绑定，XAML 里不写文案/格式串）---------------

    /// <summary>明细条目数。</summary>
    public int TotalCount => Preview.Items.Count;

    /// <summary>安装数。</summary>
    public int InstallCount => ChangesFormatter.Count(Preview).Install;

    /// <summary>升级数。</summary>
    public int UpgradeCount => ChangesFormatter.Count(Preview).Upgrade;

    /// <summary>降级数。</summary>
    public int DowngradeCount => ChangesFormatter.Count(Preview).Downgrade;

    /// <summary>卸载数。</summary>
    public int RemoveCount => ChangesFormatter.Count(Preview).Remove;

    /// <summary>替换数。</summary>
    public int ReplaceCount => ChangesFormatter.Count(Preview).Replace;

    /// <summary>是否有明细行。</summary>
    public bool HasRows => Rows.Count > 0;

    /// <summary>是否显示空状态占位（03 篇 §10）。</summary>
    public bool ShowEmptyState => !HasRows;

    /// <summary>摘要文本（03 篇 §4 摘要块）。</summary>
    public string SummaryText => ChangesFormatter.Summary(Preview);

    /// <summary>下载体积文本。</summary>
    public string DownloadText => ChangesFormatter.DownloadText(Preview.EstimatedDownloadBytes);

    /// <summary>是否存在冲突（有冲突时"开始"按钮必须禁用）。</summary>
    public bool HasConflicts => Preview.HasConflicts;

    /// <summary>冲突明细（逐条列出，红框展示）。</summary>
    public IReadOnlyList<string> Conflicts => Preview.Conflicts;

    /// <summary>冲突区标题（用文字说明冲突数量，不单靠颜色）。</summary>
    public string ConflictTitle => Preview.HasConflicts
        ? $"存在 {Preview.Conflicts.Count} 个冲突，解决后才能开始"
        : string.Empty;

    /// <summary>冲突区处理建议。</summary>
    public string ConflictHint =>
        "请在「模组」页取消勾选其中一个，或用右侧「移除」把冲突项移出本变更集，然后点「重新计算」。本页不会替你自动裁决。";

    /// <summary>是否有告警（不阻断执行，但要显式展示）。</summary>
    public bool HasWarnings => Preview.Warnings.Count > 0;

    /// <summary>告警明细。</summary>
    public IReadOnlyList<string> Warnings => Preview.Warnings;

    /// <summary>当前实例的游戏进程是否正在运行（12 篇 F-33）。</summary>
    public bool IsGameRunning => _gameRunning;

    /// <summary>是否要展示"游戏正在运行"横幅（用户已选择强制继续后不再展示）。</summary>
    public bool HasGameRunningWarning => _gameRunning && !_ignoreGameRunning;

    /// <summary>游戏运行横幅文案。</summary>
    public string GameRunningTitle => "游戏正在运行";

    /// <summary>"仍要执行"按钮文案。</summary>
    public string ApplyAnywayButtonText => "仍要执行";

    /// <summary>游戏运行横幅说明（03 篇 §4：运行中安装会与游戏争用文件）。</summary>
    public string GameRunningHint =>
        "当前实例的游戏进程还在运行，安装/卸载可能与游戏争用文件并导致模组损坏。建议先完全退出游戏；确实要继续请点「仍要执行」。";

    /// <summary>告警区标题。</summary>
    public string WarningTitle => Preview.Warnings.Count > 0
        ? $"{Preview.Warnings.Count} 条提示（不阻断执行）"
        : string.Empty;

    /// <summary>状态徽章文案。</summary>
    public string StateBadgeText => ChangesFormatter.StateBadge(State);

    /// <summary>状态说明文本（03 篇 §9：双通道，颜色之外必须有字）。</summary>
    public string StateText => ChangesFormatter.StateText(State, ExecutionPercent);

    /// <summary>是否正在执行（下载/安装）。</summary>
    public bool IsApplying => State == ChangeExecutionState.Applying;

    /// <summary>是否有后台计算/执行在进行（禁用会打架的按钮）。</summary>
    public bool IsBusy => State is ChangeExecutionState.Computing or ChangeExecutionState.Applying;

    /// <summary>进度文本。</summary>
    public string ProgressText => string.Create(
        System.Globalization.CultureInfo.CurrentCulture,
        $"{Math.Clamp(ExecutionPercent, 0, 100):0}%");

    /// <summary>是否有执行结果明细要展示。</summary>
    public bool HasResult => !string.IsNullOrWhiteSpace(ResultText);

    /// <summary>是否有错误文案要展示。</summary>
    public bool HasErrorText => !string.IsNullOrWhiteSpace(LastErrorText);

    /// <summary>是否具备开始执行的前提：有变更项、无冲突、不在计算/执行中、且没有刚执行完待刷新的结果。</summary>
    public bool CanApply => HasRows
                            && !HasConflicts
                            && !HasGameRunningWarning
                            && State is not (ChangeExecutionState.Computing
                                or ChangeExecutionState.Applying
                                or ChangeExecutionState.Done);

    /// <summary>是否可以取消（只有执行中可取消；下载中立即停，安装中等待回滚）。</summary>
    public bool CanCancel => State == ChangeExecutionState.Applying;

    /// <summary>是否可以重试执行（仅在失败状态且具备执行前提时允许重试）。</summary>
    public bool CanRetry => State == ChangeExecutionState.Failed
                           && HasRows
                           && !HasConflicts
                           && !HasGameRunningWarning;

    /// <summary>空状态标题（03 篇 §10）。</summary>
    public string EmptyStateTitle => "还没有待应用的变更";

    /// <summary>空状态说明。</summary>
    public string EmptyStateHint
    {
        get
        {
            if (Requests.Count > 0 && _changeSets is null)
            {
                return "变更集服务尚未接入（INT-07），本页暂时无法解析依赖与冲突。";
            }

            if (Requests.Count > 0)
            {
                return "变更集为空：所有请求项都已是最新状态，或被安装过滤器排除。";
            }

            return "请先在「模组」页勾选要安装或移除的模组，变更集结果会自动出现在这里。";
        }
    }

    // ---- 固定标签（03 篇 §11：XAML 不写字面文案）-------------------------

    /// <summary>列头：模组。</summary>
    public string ColumnNameHeader => ChangesLabels.ColumnName;

    /// <summary>列头：动作。</summary>
    public string ColumnActionHeader => ChangesLabels.ColumnAction;

    /// <summary>列头：版本变化。</summary>
    public string ColumnVersionHeader => ChangesLabels.ColumnVersion;

    /// <summary>列头：来源。</summary>
    public string ColumnOriginHeader => ChangesLabels.ColumnOrigin;

    /// <summary>列头：体积。</summary>
    public string ColumnSizeHeader => ChangesLabels.ColumnSize;

    /// <summary>列头：行操作。</summary>
    public string ColumnOperationsHeader => ChangesLabels.ColumnOperations;

    /// <summary>主按钮文案。</summary>
    public string ApplyButtonText => ChangesLabels.ApplyButton;

    /// <summary>取消按钮文案。</summary>
    public string CancelButtonText => ChangesLabels.CancelButton;

    /// <summary>重新计算按钮文案。</summary>
    public string RecomputeButtonText => ChangesLabels.RecomputeButton;

    /// <summary>行内移除按钮文案。</summary>
    public string RemoveItemButtonText => ChangesLabels.RemoveItemButton;

    /// <summary>执行结果区标题。</summary>
    public string ResultTitle => ChangesLabels.ResultTitle;

    /// <summary>错误区标题。</summary>
    public string ErrorTitle => ChangesLabels.ErrorTitle;

    /// <summary>重试按钮文案。</summary>
    public string RetryButtonText => ChangesLabels.RetryButton;

    /// <summary>查看冲突详情按钮文案。</summary>
    public string ShowConflictDetailsButtonText => ChangesLabels.ShowConflictDetailsButton;

    /// <summary>取消语义提示（03 篇 §4.1）。</summary>
    public string CancelHint => ChangesLabels.CancelHint;

    /// <summary>冲突区无障碍名称。</summary>
    public string ConflictRegionName => ChangesLabels.ConflictRegionName;

    /// <summary>状态栏无障碍名称。</summary>
    public string StatusRegionName => ChangesLabels.StatusRegionName;

    /// <summary>页面标题（覆盖基类默认空串）。</summary>
    public string PageTitle => Title;

    // ---- 生命周期 ---------------------------------------------------------

    /// <inheritdoc />
    protected override void OnActivated()
    {
        Subscribe();
        RefreshGameRunning();
        if (State is not (ChangeExecutionState.Applying or ChangeExecutionState.Done))
        {
            RebuildFromRequests();
        }
    }

    /// <inheritdoc />
    protected override void OnDeactivated()
    {
        Unsubscribe();
    }

    private void Subscribe()
    {
        if (_subscribed)
        {
            return;
        }

        _errors.Reported += OnErrorReported;
        _subscribed = true;
    }

    private void Unsubscribe()
    {
        if (!_subscribed)
        {
            return;
        }

        _errors.Reported -= OnErrorReported;
        _subscribed = false;
    }

    private void OnErrorReported(ErrorEntry entry)
    {
        LastErrorText = entry?.UserMessage ?? string.Empty;
        OnPropertyChanged(nameof(HasErrorText));
    }

    // ---- 对外入口 ---------------------------------------------------------

    /// <summary>灌入待计算的请求集合（去重，保留顺序），并立即重算。</summary>
    public void SetRequests(IReadOnlyCollection<ChangeRequest>? requests)
    {
        _recommendationsHandled = false;
        var list = new List<ChangeRequest>(requests?.Count ?? 0);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (requests is not null)
        {
            foreach (var request in requests)
            {
                if (request is null || string.IsNullOrWhiteSpace(request.Identifier))
                {
                    continue;
                }

                if (seen.Add(request.Identifier))
                {
                    list.Add(request);
                }
            }
        }

        Requests = list;
        RebuildFromRequests();
    }

    /// <summary>直接装载一份已算好的预览（供调用方复用 <c>BuildLocal</c> 结果或测试注入）。</summary>
    public void LoadPreview(ChangeSetPreview? preview)
    {
        _recommendationsHandled = false;
        Preview = preview ?? ChangesFormatter.EmptyPreview;
        ExecutionPercent = 0;
        ResultText = string.Empty;
        State = Preview.Items.Count == 0 ? ChangeExecutionState.Idle : ChangeExecutionState.Ready;
        StatusText = Preview.HasConflicts
            ? ConflictTitle
            : ChangesFormatter.Summary(Preview);
        RefreshDerived();
    }

    /// <summary>按当前请求集合重算变更集（结果落在 <see cref="PendingWork"/> 上，便于测试等待）。</summary>
    public void RebuildFromRequests()
    {
        PendingWork = RebuildCoreAsync();
    }

    /// <summary>清空请求集合并回到空状态。</summary>
    public void ClearRequests()
    {
        _recommendationsHandled = false;
        Requests = Array.Empty<ChangeRequest>();
        Preview = ChangesFormatter.EmptyPreview;
        ExecutionPercent = 0;
        ResultText = string.Empty;
        State = ChangeExecutionState.Idle;
        StatusText = ChangesFormatter.Summary(null);
        RefreshDerived();
    }

    // ---- 核心流程 ---------------------------------------------------------

    private async Task RebuildCoreAsync()
    {
        var version = ++_buildVersion;

        if (Requests.Count == 0)
        {
            Preview = ChangesFormatter.EmptyPreview;
            ExecutionPercent = 0;
            ResultText = string.Empty;
            State = ChangeExecutionState.Idle;
            StatusText = ChangesFormatter.Summary(null);
            RefreshDerived();
            return;
        }

        if (_changeSets is null)
        {
            Preview = ChangesFormatter.EmptyPreview;
            ExecutionPercent = 0;
            State = ChangeExecutionState.Idle;
            StatusText = "变更集服务尚未接入（INT-07），暂时只能看到请求数量，无法解析依赖与冲突。";
            RefreshDerived();
            return;
        }

        State = ChangeExecutionState.Computing;
        StatusText = ChangesFormatter.StateText(ChangeExecutionState.Computing, 0);
        RefreshDerived();

        try
        {
            var result = await _changeSets.BuildAsync(Requests).ConfigureAwait(true);
            if (version != _buildVersion)
            {
                // 期间又发起了新的计算，丢弃这次结果。
                return;
            }

            if (result.IsSuccess && result.Value is not null)
            {
                Preview = result.Value;
                ExecutionPercent = 0;
                ResultText = string.Empty;
                State = ChangeExecutionState.Ready;
                StatusText = Preview.HasConflicts
                    ? $"变更集已算出，但有 {Preview.Conflicts.Count} 个冲突需要先解决。"
                    : "变更集已算出，可以开始。";
            }
            else
            {
                Preview = ChangesFormatter.EmptyPreview;
                State = ChangeExecutionState.Failed;
                StatusText = result.Message ?? "计算变更集失败，请查看错误栏。";
                _errors.Report("计算变更集", StatusText, result.Detail);
            }
        }
        catch (Exception ex)
        {
            if (version != _buildVersion)
            {
                return;
            }

            Preview = ChangesFormatter.EmptyPreview;
            State = ChangeExecutionState.Failed;
            StatusText = "计算变更集失败，请查看错误栏。";
            _errors.Report(ex, "计算变更集", "计算变更集时发生错误。");
        }
        finally
        {
            RefreshDerived();
        }
    }

    private void CancelPendingApply()
    {
        try
        {
            _applyCts?.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // 已释放，忽略。
        }
        finally
        {
            _applyCts?.Dispose();
            _applyCts = null;
        }
    }

    private void OnProgressReported(OperationProgress progress)
    {
        if (progress is null)
        {
            return;
        }

        if (_dispatcher is { } dispatcher && !dispatcher.IsOnUiThread)
        {
            dispatcher.Post(() => OnProgressReported(progress));
            return;
        }

        ExecutionPercent = Math.Clamp(progress.Percent, 0, 100);
        StatusText = string.IsNullOrWhiteSpace(progress.Message)
            ? string.Create(
                System.Globalization.CultureInfo.CurrentCulture,
                $"{progress.Title}… {ExecutionPercent:0}%")
            : progress.Message!;
        OnPropertyChanged(nameof(StateText));
        OnPropertyChanged(nameof(ProgressText));
    }

    private async Task ApplyInstallResultAsync(OperationResult<InstallReport> result)
    {
        if (!result.IsSuccess || result.Value is not { } report)
        {
            State = ChangeExecutionState.Failed;
            StatusText = result.Message ?? "应用变更失败，请查看错误栏。";
            ResultText = ChangesFormatter.Lines(result.Detail is null ? null : new[] { result.Detail });
            _errors.Report("应用变更集", StatusText, result.Detail);
            return;
        }

        ExecutionPercent = 100;
        ResultText = ChangesFormatter.Lines(BuildResultLines(report));
        if (report.IsFullSuccess)
        {
            State = ChangeExecutionState.Done;
            StatusText = ChangesFormatter.InstallResult(report);
            Requests = Array.Empty<ChangeRequest>();
            RequestsApplied?.Invoke();

            // 卸载收尾（12 篇 F-43）：只有后端报了残留目录、且宿主给了对话框与当前实例才弹。
            await ShowResidualCleanupAsync(report).ConfigureAwait(true);
            return;
        }

        State = ChangeExecutionState.Failed;
        StatusText = ChangesFormatter.InstallResult(report);
        _errors.Report("应用变更集", StatusText, ResultText);
    }

    /// <summary>卸载成功后询问是否清理残留目录（12 篇 F-43）；任何缺失都静默跳过，不影响安装结果。</summary>
    private async Task ShowResidualCleanupAsync(InstallReport report)
    {
        if (report.ResidualDirectories.Count == 0
            || _residualCleanup is not { } dialog
            || _instances?.Current is not { } instance)
        {
            return;
        }

        try
        {
            var choice = await dialog.ShowAsync(instance, report.ResidualDirectories).ConfigureAwait(true);
            if (choice == ResidualCleanupChoice.Delete)
            {
                StatusText = StatusText + " 已清理残留目录。";
            }
        }
        catch (Exception ex)
        {
            // 残留清理只是收尾动作，弹框失败不能把已成功的安装结果改写成失败。
            _errors.Report(ex, "清理残留目录", "清理残留目录时发生错误，卸载本身已完成。");
        }
    }

    private static List<string> BuildResultLines(InstallReport report)
    {
        var lines = new List<string>(report.Installed.Count + report.Removed.Count + report.Failed.Count + 1);
        if (report.Failed.Count > 0)
        {
            lines.Add("失败的模组：");
            foreach (var identifier in report.Failed)
            {
                lines.Add($"· {identifier}");
            }
        }

        if (report.Warnings.Count > 0)
        {
            lines.Add("提示：");
            foreach (var warning in report.Warnings)
            {
                lines.Add($"· {warning}");
            }
        }

        if (report.Installed.Count > 0)
        {
            lines.Add($"已安装：{string.Join("、", report.Installed)}");
        }

        if (report.Removed.Count > 0)
        {
            lines.Add($"已移除：{string.Join("、", report.Removed)}");
        }

        if (lines.Count == 0)
        {
            lines.Add("没有需要报告的变化。");
        }

        return lines;
    }

    private void RefreshDerived()
    {
        OnPropertyChanged(nameof(TotalCount));
        OnPropertyChanged(nameof(InstallCount));
        OnPropertyChanged(nameof(UpgradeCount));
        OnPropertyChanged(nameof(DowngradeCount));
        OnPropertyChanged(nameof(RemoveCount));
        OnPropertyChanged(nameof(ReplaceCount));
        OnPropertyChanged(nameof(HasRows));
        OnPropertyChanged(nameof(ShowEmptyState));
        OnPropertyChanged(nameof(SummaryText));
        OnPropertyChanged(nameof(DownloadText));
        OnPropertyChanged(nameof(HasConflicts));
        OnPropertyChanged(nameof(Conflicts));
        OnPropertyChanged(nameof(ConflictTitle));
        OnPropertyChanged(nameof(ConflictHint));
        OnPropertyChanged(nameof(HasWarnings));
        OnPropertyChanged(nameof(Warnings));
        OnPropertyChanged(nameof(WarningTitle));
        OnPropertyChanged(nameof(StateBadgeText));
        OnPropertyChanged(nameof(StateText));
        OnPropertyChanged(nameof(IsApplying));
        OnPropertyChanged(nameof(IsBusy));
        OnPropertyChanged(nameof(ProgressText));
        OnPropertyChanged(nameof(HasResult));
        OnPropertyChanged(nameof(HasErrorText));
        OnPropertyChanged(nameof(CanApply));
        OnPropertyChanged(nameof(CanCancel));
        OnPropertyChanged(nameof(CanRetry));
        OnPropertyChanged(nameof(EmptyStateTitle));
        OnPropertyChanged(nameof(EmptyStateHint));
        OnPropertyChanged(nameof(IsGameRunning));
        OnPropertyChanged(nameof(HasGameRunningWarning));

        ApplyCommand.NotifyCanExecuteChanged();
        ApplyAnywayCommand.NotifyCanExecuteChanged();
        CancelCommand.NotifyCanExecuteChanged();
        RetryCommand.NotifyCanExecuteChanged();
        ShowConflictDetailsCommand.NotifyCanExecuteChanged();
    }

    partial void OnPreviewChanged(ChangeSetPreview value)
    {
        Rows = ChangeRowView.Project(value);
        RefreshDerived();
    }

    partial void OnStateChanged(ChangeExecutionState value) => RefreshDerived();

    partial void OnExecutionPercentChanged(double value)
    {
        OnPropertyChanged(nameof(ProgressText));
        OnPropertyChanged(nameof(StateText));
    }

    partial void OnResultTextChanged(string value) => OnPropertyChanged(nameof(HasResult));

    partial void OnLastErrorTextChanged(string value) => OnPropertyChanged(nameof(HasErrorText));

    // ---- 命令 -------------------------------------------------------------

    /// <summary>重新计算变更集（含依赖解析与冲突检测）。</summary>
    [RelayCommand]
    private Task RecomputeAsync()
    {
        _recommendationsHandled = false;
        return RebuildCoreAsync();
    }

    /// <summary>游戏正在运行时仍然强制继续（用户已明确选择）。</summary>
    [RelayCommand(CanExecute = nameof(HasGameRunningWarning))]
    private async Task ApplyAnywayAsync()
    {
        _ignoreGameRunning = true;
        RefreshDerived();
        await ApplyAsync().ConfigureAwait(true);
    }

    /// <summary>重新探测游戏进程是否在运行（只在页面激活与执行结束后探测，避免轮询开销）。</summary>
    private void RefreshGameRunning()
    {
        bool running = false;
        try
        {
            running = _instances?.IsGameRunning() ?? false;
        }
        catch (Exception ex)
        {
            _errors.Report(ex, "探测游戏进程", "无法判断游戏是否正在运行，已按未运行处理。");
        }

        if (running == _gameRunning)
        {
            return;
        }

        _gameRunning = running;
        _ignoreGameRunning = false;
        RefreshDerived();
    }

    /// <summary>开始执行（03 篇 §4 主按钮；冲突时命令不可执行）。</summary>
    [RelayCommand(CanExecute = nameof(CanApply))]
    private async Task ApplyAsync()
    {
        if (!CanApply)
        {
            return;
        }

        if (_installer is null)
        {
            StatusText = MissingInstallerText;
            return;
        }

        // F-35: 推荐/建议/支持项交互式选择介入（变更集确认/提交前）
        if (!_recommendationsHandled)
        {
            var suppress = _settingsStore?.Current.SuppressRecommendations ?? false;
            if (!suppress && _modDialogs is not null)
            {
                var candidates = CollectRecommendationCandidates();
                if (candidates.Count > 0)
                {
                    var selection = await PromptRecommendationsAsync(candidates, suppress).ConfigureAwait(true);
                    if (selection is null)
                    {
                        // 若用户取消，则中止当前安装变更并给出明确状态提示
                        State = ChangeExecutionState.Ready;
                        StatusText = "已取消推荐模组选择，未执行任何操作。";
                        ResultText = string.Empty;
                        RefreshDerived();
                        return;
                    }

                    _recommendationsHandled = true;
                    await ApplyRecommendationSelectionAsync(selection).ConfigureAwait(true);

                    // 若合并推荐项后产生了冲突，停止应用并提示用户
                    if (HasConflicts || !CanApply)
                    {
                        return;
                    }
                }
            }
        }

        _applyCts = new CancellationTokenSource();
        var token = _applyCts.Token;

        State = ChangeExecutionState.Applying;
        ExecutionPercent = 0;
        ResultText = string.Empty;
        StatusText = ChangesFormatter.StateText(ChangeExecutionState.Applying, 0);
        RefreshDerived();

        try
        {
            var progress = new Progress<OperationProgress>(OnProgressReported);
            var result = await _installer.ApplyAsync(Preview, progress, token).ConfigureAwait(true);
            await ApplyInstallResultAsync(result).ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
            State = ChangeExecutionState.Cancelled;
            StatusText = ChangesFormatter.StateText(ChangeExecutionState.Cancelled, ExecutionPercent);
            ResultText = "本次事务已回滚，游戏目录未被改动。";
        }
        catch (Exception ex)
        {
            State = ChangeExecutionState.Failed;
            StatusText = "应用变更时发生错误，请查看错误栏。";
            ResultText = ex.Message;
            _errors.Report(ex, "应用变更集", "应用变更时发生错误。");
        }
        finally
        {
            _applyCts?.Dispose();
            _applyCts = null;
            RefreshDerived();
        }
    }

    /// <summary>执行失败后重新尝试应用变更集（F-37 闭环）。</summary>
    [RelayCommand(CanExecute = nameof(CanRetry))]
    private async Task RetryAsync()
    {
        if (CanRetry)
        {
            _recommendationsHandled = false;
            await ApplyAsync().ConfigureAwait(true);
        }
    }

    /// <summary>请求取消：下载中立即停，安装中由 Core 等当前模组完成后回滚（03 篇 §4.1 取消语义）。</summary>
    [RelayCommand(CanExecute = nameof(CanCancel))]
    private void Cancel()
    {
        if (!CanCancel)
        {
            return;
        }

        try
        {
            _applyCts?.Cancel();
            StatusText = "正在取消…（安装阶段的取消会触发事务回滚）";
        }
        catch (ObjectDisposedException)
        {
            StatusText = "任务已结束，无需取消。";
        }
    }

    /// <summary>把一项移出当前变更集（冲突区/明细表的应急出口），本地去掉后不重新解析依赖。</summary>
    [RelayCommand]
    private void RemoveItem(ChangeRowView? row)
    {
        if (row is null || Preview.Items.Count == 0)
        {
            return;
        }

        var remaining = new List<ChangeItemDto>(Preview.Items.Count);
        foreach (var item in Preview.Items)
        {
            if (!string.Equals(item.Identifier, row.Identifier, StringComparison.OrdinalIgnoreCase))
            {
                remaining.Add(item);
            }
        }

        if (remaining.Count == Preview.Items.Count)
        {
            return;
        }

        var bytes = Math.Max(0, Preview.EstimatedDownloadBytes - (row.Data.DownloadBytes ?? 0));
        Preview = new ChangeSetPreview(remaining, Preview.Conflicts, bytes, Preview.Warnings);
        ExecutionPercent = 0;
        State = remaining.Count == 0 ? ChangeExecutionState.Idle : ChangeExecutionState.Ready;
        StatusText = remaining.Count == 0
            ? ChangesFormatter.Summary(null)
            : $"已从变更集移除 {row.Name}（点「重新计算」可恢复权威结果）。";
        RefreshDerived();
    }

    /// <summary>弹窗展示结构化的冲突明细（F-36 / UI-17）。</summary>
    [RelayCommand(CanExecute = nameof(HasConflicts))]
    public async Task ShowConflictDetailsAsync()
    {
        if (!HasConflicts)
        {
            return;
        }

        if (_modDialogs is null)
        {
            StatusText = "模组对话框服务未注入，无法展示冲突详情。";
            return;
        }

        var entries = Conflicts
            .Select(c => new ConflictEntry(Description: c))
            .ToList();

        var request = new ConflictDetailsRequest(
            Message: ConflictHint ?? "检测到模组冲突",
            Conflicts: entries,
            IsBlocking: true);

        try
        {
            await _modDialogs.ShowConflictDetailsAsync(request).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            _errors.Report(ex, "展示冲突详情", "展示冲突详情时发生错误。");
        }
    }

    /// <summary>
    /// 收集待推荐模组候选列表（来自 Preview.Items 中的 Recommendation 项，以及正在安装模组的 Recommends/Suggests/Supports）。
    /// </summary>
    private IReadOnlyList<RecommendationCandidate> CollectRecommendationCandidates()
    {
        var result = new Dictionary<string, RecommendationCandidate>(StringComparer.OrdinalIgnoreCase);

        // 1. 从当前变更预览中提取 Origin 为 Recommendation 的项
        foreach (var item in Preview.Items.Where(i => i.Origin == ChangeOrigin.Recommendation))
        {
            result[item.Identifier] = new RecommendationCandidate(
                Identifier: item.Identifier,
                Name: item.Name,
                Kind: RecommendationKind.Recommended,
                Description: null,
                IsSelectedByDefault: true);
        }

        // 2. 若注入了仓库服务，从待安装/变更的主模组扫描其 Recommends / Suggests / Supports
        if (_repository is not null)
        {
            var available = _repository.Current.Available;
            if (available.Count > 0)
            {
                var availableMap = available.ToDictionary(m => m.Identifier, StringComparer.OrdinalIgnoreCase);

                var targetInstallIds = Preview.Items
                    .Where(i => i.Action == ChangeAction.Install && i.Origin != ChangeOrigin.Recommendation)
                    .Select(i => i.Identifier)
                    .Concat(Requests.Where(r => r.Action == ChangeAction.Install).Select(r => r.Identifier))
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);

                var alreadyInPreview = Preview.Items
                    .Select(i => i.Identifier)
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);

                foreach (var installId in targetInstallIds)
                {
                    if (!availableMap.TryGetValue(installId, out var modRow))
                    {
                        continue;
                    }

                    // Recommends
                    if (modRow.Facts.Recommends is { Count: > 0 } recs)
                    {
                        foreach (var recId in recs)
                        {
                            if (!alreadyInPreview.Contains(recId) && !result.ContainsKey(recId))
                            {
                                var name = availableMap.TryGetValue(recId, out var recMod) ? recMod.Name : recId;
                                var desc = availableMap.TryGetValue(recId, out var rMod) ? (rMod.Facts.Abstract ?? rMod.Facts.Description) : null;
                                result[recId] = new RecommendationCandidate(
                                    Identifier: recId,
                                    Name: name,
                                    Kind: RecommendationKind.Recommended,
                                    Description: desc,
                                    RelatedIdentifiers: [installId],
                                    IsSelectedByDefault: true);
                            }
                        }
                    }

                    // Suggests
                    if (modRow.Facts.Suggests is { Count: > 0 } sugs)
                    {
                        foreach (var sugId in sugs)
                        {
                            if (!alreadyInPreview.Contains(sugId) && !result.ContainsKey(sugId))
                            {
                                var name = availableMap.TryGetValue(sugId, out var sugMod) ? sugMod.Name : sugId;
                                var desc = availableMap.TryGetValue(sugId, out var sMod) ? (sMod.Facts.Abstract ?? sMod.Facts.Description) : null;
                                result[sugId] = new RecommendationCandidate(
                                    Identifier: sugId,
                                    Name: name,
                                    Kind: RecommendationKind.Suggested,
                                    Description: desc,
                                    RelatedIdentifiers: [installId],
                                    IsSelectedByDefault: false);
                            }
                        }
                    }

                    // Supports
                    if (modRow.Facts.Supports is { Count: > 0 } sups)
                    {
                        foreach (var supId in sups)
                        {
                            if (!alreadyInPreview.Contains(supId) && !result.ContainsKey(supId))
                            {
                                var name = availableMap.TryGetValue(supId, out var supMod) ? supMod.Name : supId;
                                var desc = availableMap.TryGetValue(supId, out var pMod) ? (pMod.Facts.Abstract ?? pMod.Facts.Description) : null;
                                result[supId] = new RecommendationCandidate(
                                    Identifier: supId,
                                    Name: name,
                                    Kind: RecommendationKind.SupportedBy,
                                    Description: desc,
                                    RelatedIdentifiers: [installId],
                                    IsSelectedByDefault: false);
                            }
                        }
                    }
                }
            }
        }

        return result.Values.ToList();
    }

    private async Task ApplyRecommendationSelectionAsync(RecommendationSelection selection)
    {
        var selectedSet = selection.Identifiers.ToHashSet(StringComparer.OrdinalIgnoreCase);

        // 如果用户在弹窗中取消勾选了原本在 Preview 中的 Recommendation 项，则将其剔除
        var existingRecsToRemove = Preview.Items
            .Where(i => i.Origin == ChangeOrigin.Recommendation && !selectedSet.Contains(i.Identifier))
            .Select(i => i.Identifier)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var currentRequests = Requests
            .Where(r => !existingRecsToRemove.Contains(r.Identifier))
            .ToList();

        bool changed = existingRecsToRemove.Count > 0;

        foreach (var id in selection.Identifiers)
        {
            if (!currentRequests.Any(r => string.Equals(r.Identifier, id, StringComparison.OrdinalIgnoreCase)))
            {
                currentRequests.Add(new ChangeRequest(id, ChangeAction.Install));
                changed = true;
            }
        }

        if (changed)
        {
            Requests = currentRequests;
            if (_changeSets is not null && Requests.Count > 0)
            {
                await RebuildCoreAsync().ConfigureAwait(true);
            }
            else
            {
                var items = Preview.Items
                    .Where(i => !existingRecsToRemove.Contains(i.Identifier))
                    .ToList();

                foreach (var id in selection.Identifiers)
                {
                    if (!items.Any(i => string.Equals(i.Identifier, id, StringComparison.OrdinalIgnoreCase)))
                    {
                        items.Add(new ChangeItemDto(id, id, ChangeAction.Install, null, null, ChangeOrigin.Recommendation, 0));
                    }
                }

                Preview = new ChangeSetPreview(items, Preview.Conflicts, Preview.EstimatedDownloadBytes, Preview.Warnings);
                State = Preview.Items.Count == 0 ? ChangeExecutionState.Idle : ChangeExecutionState.Ready;
                RefreshDerived();
            }
        }
    }

    /// <summary>
    /// 若处于变更预览状态且有来自推荐的候选（ChangeOrigin.Recommendation），
    /// 或调用方主动请求，向用户弹窗挑选推荐项（F-35 / UI-15）。
    /// </summary>
    public async Task<RecommendationSelection?> PromptRecommendationsAsync(
        IReadOnlyList<RecommendationCandidate>? candidates = null,
        bool suppressRecommendations = false)
    {
        if (_modDialogs is null)
        {
            return null;
        }

        IReadOnlyList<RecommendationCandidate> actualCandidates;
        if (candidates is { Count: > 0 })
        {
            actualCandidates = candidates;
        }
        else
        {
            actualCandidates = CollectRecommendationCandidates();
            if (actualCandidates.Count == 0)
            {
                return null;
            }
        }

        var request = new RecommendationDialogRequest(
            Message: "检测到推荐、建议或支持项，请选择是否追加安装：",
            Candidates: actualCandidates,
            SuppressRecommendations: suppressRecommendations);

        try
        {
            var selection = await _modDialogs.SelectRecommendationsAsync(request).ConfigureAwait(true);
            if (selection is not null && _settingsStore is not null && selection.SuppressRecommendations != _settingsStore.Current.SuppressRecommendations)
            {
                var updated = _settingsStore.Current with { SuppressRecommendations = selection.SuppressRecommendations };
                _settingsStore.Save(updated);
            }

            return selection;
        }
        catch (Exception ex)
        {
            _errors.Report(ex, "选择推荐模组", "选择推荐模组对话框发生错误。");
            return null;
        }
    }

    /// <summary>
    /// 当安装或依赖解析遇到虚拟依赖时，通过弹窗请求用户选择具体实现模组（F-34 / UI-16）。
    /// 用户选择特定模组后，将其指定为目标安装项；若用户取消，则中止当前安装变更并给出明确状态提示。
    /// </summary>
    public async Task<VirtualModuleChoice?> SelectVirtualModuleAsync(VirtualModuleRequest request)
    {
        if (request is null)
        {
            throw new ArgumentNullException(nameof(request));
        }

        if (_modDialogs is null)
        {
            StatusText = "模组对话框服务未注入，无法选择虚拟模块。";
            return null;
        }

        try
        {
            var choice = await _modDialogs.SelectVirtualModuleAsync(request).ConfigureAwait(true);
            if (choice is not null)
            {
                // 用户选择特定模组后，将其指定为目标安装项并合并到当前请求集
                var currentRequests = Requests.ToList();
                if (!currentRequests.Any(r => string.Equals(r.Identifier, choice.Identifier, StringComparison.OrdinalIgnoreCase)))
                {
                    currentRequests.Add(new ChangeRequest(choice.Identifier, ChangeAction.Install));
                    Requests = currentRequests;
                }

                if (_changeSets is not null && Requests.Count > 0)
                {
                    await RebuildCoreAsync().ConfigureAwait(true);
                }
                else
                {
                    var items = Preview.Items.ToList();
                    if (!items.Any(i => string.Equals(i.Identifier, choice.Identifier, StringComparison.OrdinalIgnoreCase)))
                    {
                        items.Add(new ChangeItemDto(choice.Identifier, choice.Name, ChangeAction.Install, null, null, ChangeOrigin.UserRequested, 0));
                    }
                    Preview = new ChangeSetPreview(items, Preview.Conflicts, Preview.EstimatedDownloadBytes, Preview.Warnings);
                    State = Preview.Items.Count == 0 ? ChangeExecutionState.Idle : ChangeExecutionState.Ready;
                    RefreshDerived();
                }

                StatusText = $"已选择「{choice.Name}」作为虚拟模组提供者。";
            }
            else
            {
                // 若用户取消，则中止当前安装变更并给出明确状态提示
                State = ChangeExecutionState.Ready;
                StatusText = "已取消选择虚拟模组提供者，未执行任何操作。";
                ResultText = string.Empty;
                RefreshDerived();
            }

            return choice;
        }
        catch (Exception ex)
        {
            _errors.Report(ex, "选择虚拟模组", "选择虚拟模组提供者时发生错误。");
            return null;
        }
    }

    /// <summary>
    /// 当安装或依赖解析遇到虚拟依赖时介入处理并尝试继续应用（F-34）。
    /// </summary>
    public async Task<bool> ResolveVirtualModuleAndApplyAsync(VirtualModuleRequest request)
    {
        var choice = await SelectVirtualModuleAsync(request).ConfigureAwait(true);
        if (choice is null)
        {
            return false;
        }

        if (CanApply)
        {
            await ApplyAsync().ConfigureAwait(true);
            return State == ChangeExecutionState.Done;
        }

        return false;
    }

    // ---- 无服务时的空实现（设计器 / 无参构造）-----------------------------

    private sealed class NullErrorReporter : IErrorReporter
    {
        public static NullErrorReporter Instance { get; } = new();

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
}