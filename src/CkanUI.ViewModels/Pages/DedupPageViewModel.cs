using System.Collections.ObjectModel;
using System.ComponentModel;
using CkanUI.Services.Abstractions;
using CkanUI.Services.Abstractions.Dtos;
using CkanUI.ViewModels.Pages.Dedup;
using CkanUI.ViewModels.Pages.Unmanaged;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace CkanUI.ViewModels.Pages;

/// <summary>
/// 跨实例去重页（规格 F-41 / UI-21 / INT-09）：扫描多个实例 GameData 里内容相同的文件，
/// 算出可释放空间，并在用户显式确认后把它们改成硬链接。
/// </summary>
/// <remarks>
/// 写盘口径：默认 **预演**（<see cref="DryRun"/> = true，只校验不落盘）；关闭预演后必须先经过两次确认，
/// 第二次确认说明「多个实例共用同一份数据」的后果，用户点过两次才会真正建链。
/// </remarks>
public sealed partial class DedupPageViewModel : PageViewModel
{
    private readonly IInstanceDedupService _service;
    private readonly IInstanceService _instances;
    private readonly IErrorReporter _errors;
    private readonly IDialogService _dialogs;

    private DedupScanReport? _lastScan;
    private bool _subscribed;
    private bool _scanned;

    /// <summary>设计时（XAML 预览）用的空实现。</summary>
    public DedupPageViewModel()
        : this(new EmptyDedupService(), new EmptyInstanceService(), new EmptyErrorReporter(), new EmptyDialogService())
    {
    }

    /// <summary>运行时构造：去重服务 + 实例清单 + 错误上报 + 模态确认。</summary>
    public DedupPageViewModel(
        IInstanceDedupService service,
        IInstanceService instances,
        IErrorReporter errors,
        IDialogService dialogs)
    {
        ArgumentNullException.ThrowIfNull(service);
        ArgumentNullException.ThrowIfNull(instances);
        ArgumentNullException.ThrowIfNull(errors);
        ArgumentNullException.ThrowIfNull(dialogs);
        _service = service;
        _instances = instances;
        _errors = errors;
        _dialogs = dialogs;
        ReloadInstances();
    }

    /// <inheritdoc />
    public override string Title => "跨实例去重";

    /// <summary>可参与扫描的实例（默认全选）。</summary>
    public ObservableCollection<DedupInstanceOptionViewModel> Instances { get; } = new();

    /// <summary>扫描结果：重复内容分组。</summary>
    [ObservableProperty]
    private IReadOnlyList<DedupGroupRowViewModel> _groups = Array.Empty<DedupGroupRowViewModel>();

    /// <summary>当前选中的分组（右侧展示组内文件清单）。</summary>
    [ObservableProperty]
    private DedupGroupRowViewModel? _selectedGroup;

    /// <summary>执行结果明细（预演/已建链/跳过/失败/回滚）。</summary>
    [ObservableProperty]
    private IReadOnlyList<DedupResultRowViewModel> _results = Array.Empty<DedupResultRowViewModel>();

    /// <summary>是否预演（默认开启：只校验、不写盘）。</summary>
    [ObservableProperty]
    private bool _dryRun = true;

    /// <summary>参与扫描的最小文件尺寸（KiB），默认 128 KiB（旧 GUI 的 MinDedupFileSize）。</summary>
    [ObservableProperty]
    private int _minimumFileSizeKiB = 128;

    /// <summary>状态条文案。</summary>
    [ObservableProperty]
    private string _statusText = "选择要比较的实例，然后点「扫描」。";

    /// <summary>错误条正文；为空表示无错误。</summary>
    [ObservableProperty]
    private string? _errorText;

    /// <summary>错误详情（技术细节，可折叠展示）。</summary>
    [ObservableProperty]
    private string? _errorDetail;

    /// <summary>是否忙碌（扫描/执行中）。</summary>
    [ObservableProperty]
    private bool _isBusy;

    /// <summary>忙碌遮罩文案。</summary>
    [ObservableProperty]
    private string _busyText = string.Empty;

    /// <summary>重复内容组数。</summary>
    [ObservableProperty]
    private int _groupCount;

    /// <summary>参与去重的重复文件数。</summary>
    [ObservableProperty]
    private int _duplicateFileCount;

    /// <summary>可释放字节数。</summary>
    [ObservableProperty]
    private long _reclaimableBytes;

    /// <summary>被跳过的条目数（太小/受管/链接/读不到等）。</summary>
    [ObservableProperty]
    private int _skippedCount;

    /// <summary>已勾选的实例数。</summary>
    [ObservableProperty]
    private int _selectedInstanceCount;

    /// <summary>当前平台是否支持硬链接（不支持时执行按钮不可用并给出说明）。</summary>
    [ObservableProperty]
    private bool _isSupported = true;

    /// <summary>是否有错误。</summary>
    public bool HasError => !string.IsNullOrEmpty(ErrorText);

    /// <summary>是否空闲（没有正在进行的操作）。</summary>
    public bool IsIdle => !IsBusy;

    /// <summary>是否展示错误详情折叠区。</summary>
    public bool HasErrorDetail => !string.IsNullOrEmpty(ErrorDetail);

    /// <summary>结果表是否非空。</summary>
    public bool HasGroups => Groups.Count > 0;

    /// <summary>是否有选中的分组。</summary>
    public bool HasSelectedGroup => SelectedGroup is not null;

    /// <summary>选中分组的文件清单。</summary>
    public IReadOnlyList<DedupFileRowViewModel> SelectedGroupFiles =>
        SelectedGroup?.Files ?? Array.Empty<DedupFileRowViewModel>();

    /// <summary>执行结果是否非空。</summary>
    public bool HasResults => Results.Count > 0;

    /// <summary>能否扫描（勾了实例且不忙）。</summary>
    public bool CanScan => !IsBusy && SelectedInstanceCount > 0;

    /// <summary>能否执行（有结果、不忙、平台支持）。</summary>
    public bool CanExecute => !IsBusy && GroupCount > 0 && IsSupported;

    /// <summary>是否展示「没有重复内容」空态。</summary>
    public bool ShowEmptyState => _scanned && !IsBusy && !HasError && GroupCount == 0;

    /// <summary>空态标题。</summary>
    public string EmptyStateTitle => "没有发现可去重的重复内容";

    /// <summary>空态说明。</summary>
    public string EmptyStateHint =>
        $"只比较 {MinimumFileSizeKiB} KiB 以上、非受管、非符号链接的文件；小于这个尺寸的文件去重收益低，也更容易被用户事后编辑。";

    /// <summary>平台不支持时的提示。</summary>
    public string UnsupportedHint => "当前平台不支持创建硬链接，只能查看重复内容，不能执行去重。";

    /// <summary>统计条文案。</summary>
    public string StatsText =>
        $"重复：{GroupCount} 组、{DuplicateFileCount} 个文件，可释放 {UnmanagedFormat.FormatSize(ReclaimableBytes)}；跳过 {SkippedCount} 项";

    /// <summary>实例勾选提示。</summary>
    public string SelectionText => $"已选 {SelectedInstanceCount} / {Instances.Count} 个实例";

    /// <summary>执行按钮文案（预演与真写盘分开表述）。</summary>
    public string ExecuteText => DryRun ? "预演去重" : "执行去重";

    /// <summary>预演/写盘开关说明。</summary>
    public string DryRunHint => DryRun
        ? "预演：只校验并列出将要改成硬链接的文件，不改动磁盘。"
        : "写盘：确认后会把重复文件改成硬链接（内容不变，但多个实例将共用同一份数据）。";

    /// <summary>执行前二次确认文案（界面直接展示，让用户先看到后果）。</summary>
    public string ConfirmHintText => DryRun
        ? "预演不会写盘，可以直接执行。"
        : $"将把 {DuplicateFileCount} 个文件改成硬链接，可释放 {UnmanagedFormat.FormatSize(ReclaimableBytes)}。硬链接后多个实例共用同一份数据：删除或改动任一实例的文件都会影响其它实例。";

    /// <summary>勾选/取消全部实例。</summary>
    [RelayCommand]
    private void ToggleAll()
    {
        var target = SelectedInstanceCount < Instances.Count;
        foreach (var option in Instances)
        {
            option.IsSelected = target;
        }
    }

    /// <summary>重新读取实例清单。</summary>
    [RelayCommand]
    private void RefreshInstances() => ReloadInstances();

    /// <summary>关闭错误条。</summary>
    [RelayCommand]
    private void ClearError()
    {
        ErrorText = null;
        ErrorDetail = null;
    }

    /// <summary>扫描所选实例的重复内容。</summary>
    [RelayCommand]
    private async Task ScanAsync()
    {
        if (!CanScan)
        {
            return;
        }

        await RunAsync(async () =>
        {
            var request = new DedupScanRequest(
                Instances.Where(option => option.IsSelected).Select(option => option.Name).ToArray())
            {
                MinimumFileSizeBytes = Math.Max(0, MinimumFileSizeKiB) * 1024L,
            };

            var result = await _service.ScanAsync(request).ConfigureAwait(true);
            if (!result.IsSuccess || result.Value is null)
            {
                SetError(UnmanagedErrorText.Compose(result.Message, "扫描失败。", result.Retryable), result.Detail);
                StatusText = "扫描失败。";
                return;
            }

            ApplyScan(result.Value);
        }, "跨实例去重扫描", "正在扫描重复内容…").ConfigureAwait(true);
    }

    /// <summary>执行（预演或真写盘）去重。</summary>
    [RelayCommand]
    private async Task ExecuteAsync()
    {
        if (!CanExecute || _lastScan is null)
        {
            return;
        }

        if (!DryRun && !await ConfirmWriteAsync().ConfigureAwait(true))
        {
            StatusText = "已取消执行，未改动磁盘。";
            return;
        }

        await RunAsync(async () =>
        {
            var request = DedupExecuteRequest.From(_lastScan, DryRun);
            var result = await _service.ExecuteAsync(request).ConfigureAwait(true);
            if (!result.IsSuccess || result.Value is null)
            {
                SetError(UnmanagedErrorText.Compose(result.Message, "去重失败。", result.Retryable), result.Detail);
                StatusText = "去重失败。";
                return;
            }

            ApplyExecute(result.Value);
        }, "跨实例去重执行", DryRun ? "正在预演去重…" : "正在建立硬链接…").ConfigureAwait(true);
    }

    /// <inheritdoc />
    protected override void OnActivated()
    {
        if (!_subscribed)
        {
            _instances.InstancesChanged += OnInstancesChanged;
            _instances.CurrentChanged += OnCurrentChanged;
            _subscribed = true;
        }

        ReloadInstances();
    }

    /// <inheritdoc />
    protected override void OnDeactivated()
    {
        if (_subscribed)
        {
            _instances.InstancesChanged -= OnInstancesChanged;
            _instances.CurrentChanged -= OnCurrentChanged;
            _subscribed = false;
        }
    }

    /// <summary>写盘前的两次确认：第一次讲清操作，第二次讲清后果。</summary>
    private async Task<bool> ConfirmWriteAsync()
    {
        var first = await _dialogs.ConfirmAsync(
            "执行跨实例去重",
            $"即将把 {DuplicateFileCount} 个文件改成硬链接，可释放 {UnmanagedFormat.FormatSize(ReclaimableBytes)}。",
            "只会处理 GameData 内、非受管、非符号链接、同卷且至少两份内容相同的文件。").ConfigureAwait(true);
        if (!first)
        {
            return false;
        }

        return await _dialogs.ConfirmAsync(
            "再次确认去重",
            "硬链接后多个实例共用同一份数据，删除或改动任一实例的文件都会影响其它实例。确定继续？",
            "内容不会改变，但这份文件不再是独立副本；CKAN 卸载某个实例的模组时会删掉受管文件——受管路径已被排除，所以不会被本操作牵连。").ConfigureAwait(true);
    }

    private void ApplyScan(DedupScanReport report)
    {
        _lastScan = report;
        _scanned = true;
        Groups = report.Groups.Select(DedupGroupRowViewModel.From).ToArray();
        GroupCount = Groups.Count;
        DuplicateFileCount = report.DuplicateFileCount;
        ReclaimableBytes = report.ReclaimableBytes;
        SkippedCount = report.SkippedCount;
        IsSupported = report.Support == DedupPlatformSupport.Supported;
        SelectedGroup = Groups.FirstOrDefault();
        Results = Array.Empty<DedupResultRowViewModel>();
        SetStatus(report.HasDuplicates
            ? $"扫描完成：{report.InstanceNames.Count} 个实例、{GroupCount} 组重复内容，可释放 {UnmanagedFormat.FormatSize(ReclaimableBytes)}；跳过 {SkippedCount} 项。"
            : $"扫描完成：{report.InstanceNames.Count} 个实例，没有发现重复内容。");
        OnPropertyChanged(nameof(StatsText));
        OnPropertyChanged(nameof(SelectionText));
        OnPropertyChanged(nameof(ConfirmHintText));
        OnPropertyChanged(nameof(ShowEmptyState));
        OnPropertyChanged(nameof(HasGroups));
        OnPropertyChanged(nameof(CanExecute));
        OnPropertyChanged(nameof(CanScan));
    }

    private void ApplyExecute(DedupExecuteReport report)
    {
        Results = report.Results.Select(DedupResultRowViewModel.From).ToArray();
        if (report.DryRun)
        {
            SetStatus($"预演完成：将把 {report.PlannedCount} 个文件改成硬链接，可释放 {UnmanagedFormat.FormatSize(report.FreedBytes)}；跳过 {report.SkippedCount} 项。未改动磁盘。");
        }
        else
        {
            // 真写盘后结果表已不成立，清空并要求重新扫描，避免重复执行。
            _lastScan = null;
            Groups = Array.Empty<DedupGroupRowViewModel>();
            GroupCount = 0;
            DuplicateFileCount = 0;
            ReclaimableBytes = 0;
            SelectedGroup = null;
            SetStatus(report.HasFailures
                ? $"已建立 {report.LinkedCount} 个硬链接，释放 {UnmanagedFormat.FormatSize(report.FreedBytes)}；失败 {report.FailedCount} 个、回滚 {report.RolledBackCount} 个。重新扫描可查看剩余重复。"
                : $"已建立 {report.LinkedCount} 个硬链接，释放 {UnmanagedFormat.FormatSize(report.FreedBytes)}。重新扫描可查看剩余重复。");
        }

        OnPropertyChanged(nameof(HasResults));
        OnPropertyChanged(nameof(HasGroups));
        OnPropertyChanged(nameof(StatsText));
        OnPropertyChanged(nameof(ConfirmHintText));
        OnPropertyChanged(nameof(ShowEmptyState));
        OnPropertyChanged(nameof(CanExecute));
    }

    private void ReloadInstances()
    {
        var current = _instances.Current?.Name;
        // 只记住用户手动取消的实例：新发现的实例默认勾选（默认全选参与比较）。
        var unselected = Instances
            .Where(option => !option.IsSelected)
            .Select(option => option.Name)
            .ToHashSet(StringComparer.Ordinal);

        foreach (var option in Instances)
        {
            option.PropertyChanged -= OnOptionChanged;
        }

        Instances.Clear();
        var all = _instances.Instances
            .OrderByDescending(instance => string.Equals(instance.Name, current, StringComparison.Ordinal))
            .ThenBy(instance => instance.Name, StringComparer.Ordinal)
            .ToArray();
        foreach (var instance in all)
        {
            var option = new DedupInstanceOptionViewModel(
                instance,
                string.Equals(instance.Name, current, StringComparison.Ordinal));
            option.IsSelected = !unselected.Contains(instance.Name);

            option.PropertyChanged += OnOptionChanged;
            Instances.Add(option);
        }

        SelectedInstanceCount = Instances.Count(option => option.IsSelected);
        OnPropertyChanged(nameof(SelectionText));
        OnPropertyChanged(nameof(CanScan));
    }

    private void OnOptionChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(DedupInstanceOptionViewModel.IsSelected))
        {
            return;
        }

        SelectedInstanceCount = Instances.Count(option => option.IsSelected);
        OnPropertyChanged(nameof(SelectionText));
        OnPropertyChanged(nameof(CanScan));
    }

    private void OnInstancesChanged() => ReloadInstances();

    private void OnCurrentChanged(InstanceInfo? instance) => ReloadInstances();

    private async Task RunAsync(Func<Task> action, string context, string busyText)
    {
        ArgumentNullException.ThrowIfNull(action);
        IsBusy = true;
        BusyText = busyText;
        ErrorText = null;
        ErrorDetail = null;
        try
        {
            await action().ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            _errors.Report(ex, context);
            SetError(UnmanagedErrorText.FromException(ex, "去重操作失败。"), ex.Message);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void SetStatus(string text)
    {
        StatusText = text;
        ErrorText = null;
        ErrorDetail = null;
    }

    private void SetError(string text, string? detail)
    {
        ErrorText = text;
        ErrorDetail = detail;
    }

    partial void OnGroupsChanged(IReadOnlyList<DedupGroupRowViewModel> value)
    {
        OnPropertyChanged(nameof(HasGroups));
        OnPropertyChanged(nameof(ShowEmptyState));
        OnPropertyChanged(nameof(CanExecute));
    }

    partial void OnSelectedGroupChanged(DedupGroupRowViewModel? value)
    {
        OnPropertyChanged(nameof(HasSelectedGroup));
        OnPropertyChanged(nameof(SelectedGroupFiles));
    }

    partial void OnResultsChanged(IReadOnlyList<DedupResultRowViewModel> value) => OnPropertyChanged(nameof(HasResults));

    partial void OnDryRunChanged(bool value)
    {
        OnPropertyChanged(nameof(ExecuteText));
        OnPropertyChanged(nameof(DryRunHint));
        OnPropertyChanged(nameof(ConfirmHintText));
    }

    partial void OnMinimumFileSizeKiBChanged(int value) => OnPropertyChanged(nameof(EmptyStateHint));

    partial void OnIsBusyChanged(bool value)
    {
        OnPropertyChanged(nameof(IsIdle));
        OnPropertyChanged(nameof(CanScan));
        OnPropertyChanged(nameof(CanExecute));
        OnPropertyChanged(nameof(ShowEmptyState));
    }

    partial void OnIsSupportedChanged(bool value)
    {
        OnPropertyChanged(nameof(CanExecute));
        OnPropertyChanged(nameof(ShowEmptyState));
    }

    partial void OnGroupCountChanged(int value) => OnPropertyChanged(nameof(StatsText));

    partial void OnDuplicateFileCountChanged(int value)
    {
        OnPropertyChanged(nameof(StatsText));
        OnPropertyChanged(nameof(ConfirmHintText));
    }

    partial void OnReclaimableBytesChanged(long value)
    {
        OnPropertyChanged(nameof(StatsText));
        OnPropertyChanged(nameof(ConfirmHintText));
    }

    partial void OnSkippedCountChanged(int value) => OnPropertyChanged(nameof(StatsText));

    partial void OnSelectedInstanceCountChanged(int value)
    {
        OnPropertyChanged(nameof(SelectionText));
        OnPropertyChanged(nameof(CanScan));
    }

    partial void OnErrorTextChanged(string? value)
    {
        OnPropertyChanged(nameof(HasError));
        OnPropertyChanged(nameof(ShowEmptyState));
    }

    partial void OnErrorDetailChanged(string? value) => OnPropertyChanged(nameof(HasErrorDetail));

    private sealed class EmptyDedupService : IInstanceDedupService
    {
        public Task<OperationResult<DedupScanReport>> ScanAsync(DedupScanRequest request, CancellationToken ct = default)
            => Task.FromResult(OperationResult<DedupScanReport>.Ok(DedupScanReport.Empty(DedupPlatformSupport.Supported)));

        public Task<OperationResult<DedupExecuteReport>> ExecuteAsync(DedupExecuteRequest request, CancellationToken ct = default)
            => Task.FromResult(OperationResult<DedupExecuteReport>.Ok(
                DedupExecuteReport.Empty(request.DryRun, DedupPlatformSupport.Supported)));
    }

    private sealed class EmptyInstanceService : IInstanceService
    {
        public IReadOnlyList<InstanceInfo> Instances => Array.Empty<InstanceInfo>();

        public InstanceInfo? Current => null;

        public event Action? InstancesChanged
        {
            add { }
            remove { }
        }

        public event Action<InstanceInfo?>? CurrentChanged
        {
            add { }
            remove { }
        }

        public Task<OperationResult<IReadOnlyList<InstanceInfo>>> RefreshAsync(CancellationToken ct = default)
            => Task.FromResult(OperationResult<IReadOnlyList<InstanceInfo>>.Ok(Array.Empty<InstanceInfo>()));

        public Task<OperationResult<InstanceInfo>> SwitchAsync(string instanceName, CancellationToken ct = default)
            => Task.FromResult(OperationResult<InstanceInfo>.Fail("设计时未提供实现。", null, false));

        public Task<OperationResult<InstanceInfo>> AddAsync(string path, string? name = null, CancellationToken ct = default)
            => Task.FromResult(OperationResult<InstanceInfo>.Fail("设计时未提供实现。", null, false));

        public Task<OperationResult<InstanceInfo>> CloneAsync(string sourceName, string newName, CancellationToken ct = default)
            => Task.FromResult(OperationResult<InstanceInfo>.Fail("设计时未提供实现。", null, false));

        public Task<OperationResult<bool>> RemoveAsync(string instanceName, CancellationToken ct = default)
            => Task.FromResult(OperationResult<bool>.Fail("设计时未提供实现。", null, false));

        public Task<OperationResult<InstanceInfo>> RenameAsync(string instanceName, string newName, CancellationToken ct = default)
            => Task.FromResult(OperationResult<InstanceInfo>.Fail("设计时未提供实现。", null, false));
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

    private sealed class EmptyDialogService : IDialogService
    {
        public Task<bool> ConfirmAsync(string title, string message, string? detail = null) => Task.FromResult(false);

        public Task<int> SelectAsync(string title, string message, IReadOnlyList<string> options, int defaultIndex = 0)
            => Task.FromResult(-1);

        public Task ShowMessageAsync(string title, string message) => Task.CompletedTask;

        public Task ShowErrorAsync(string title, string message, string? detail = null) => Task.CompletedTask;

        public Task<OperationResult<bool>> RunWithProgressAsync(
            string title, Func<IProgress<double>, CancellationToken, Task> work)
            => Task.FromResult(OperationResult<bool>.Fail("设计时未提供实现。", null, false));
    }
}
