using System.Collections.ObjectModel;
using System.Globalization;

using CkanUI.Services.Abstractions;
using CkanUI.Services.Abstractions.Dtos;
using CkanUI.Services.Abstractions.Profiles;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace CkanUI.Plugin.Storage.UI;

/// <summary>
/// 「存储」页：把多个实例的 GameData / CKAN / 配置档占用摊在一张表上，并对单个实例的配置档做硬链接去重。
/// </summary>
/// <remarks>
/// <para><b>依赖注入</b>：宿主用 <c>ActivatorUtilities.CreateInstance</c> 构造插件页，所以只暴露一个收
/// <see cref="IServiceProvider"/> 的构造函数；服务缺失时页面照常打开并给出明确空态，而不是整页构造失败。</para>
/// <para><b>去重两段式</b>：<see cref="DeduplicateCommand"/> 只跑 dry-run，拿到报告后进入确认态；必须再点
/// <see cref="ConfirmDedupCommand"/> 才以 dryRun=false 实跑。页面不弹系统对话框，确认条由
/// <see cref="IsDedupConfirmationOpen"/> 驱动，宿主与测试都能用 <see cref="SetDedupConfirmation"/> 复现这一态。</para>
/// <para><b>取不到的数是「—」不是 0</b>：剩余空间、配置档体积在服务层允许为 null，页面照抄这个语义，
/// 免得把「没量到」显示成「不占空间」。</para>
/// </remarks>
public sealed partial class StoragePageViewModel : ObservableObject, IPluginPage, IDisposable
{
    private const string NotAvailable = "—";

    private readonly IInstanceStorageService? _storage;
    private readonly IInstanceService? _instances;

    private InstanceStorageReport? _report;
    private ProfileDedupReport? _pendingDedup;
    private InstanceStorageRowViewModel? _selectedRow;
    private bool _started;
    private bool _disposed;

    /// <summary>宿主构造入口：从容器里取存储服务与实例服务。</summary>
    /// <param name="services">宿主 DI 容器。</param>
    public StoragePageViewModel(IServiceProvider services)
        : this(
            services is null ? null : services.GetService(typeof(IInstanceStorageService)) as IInstanceStorageService,
            services is null ? null : services.GetService(typeof(IInstanceService)) as IInstanceService)
    {
    }

    /// <summary>测试与宿主 DI 入口；两个服务都允许为 null，页面会明确显示缺什么。</summary>
    /// <param name="storage">多实例存储服务。</param>
    /// <param name="instances">实例服务；只用于判断「有没有实例」与默认实例名。</param>
    public StoragePageViewModel(IInstanceStorageService? storage, IInstanceService? instances)
    {
        _storage = storage;
        _instances = instances;

        if (_instances is not null)
        {
            _instances.CurrentChanged += OnCurrentChanged;
            _instances.InstancesChanged += OnInstancesChanged;
        }

        if (_storage is null)
        {
            ApplyServiceMissingState();
        }
        else if (HasAnyInstance())
        {
            ApplyPendingState();
        }
        else
        {
            ApplyNoInstanceState();
        }
    }

    /// <summary>单测入口：不必经过 DI 容器。</summary>
    /// <param name="storage">存储服务；null 表示宿主没注册。</param>
    /// <param name="instances">实例服务；null 表示宿主没注册。</param>
    /// <returns>视图模型实例。</returns>
    public static StoragePageViewModel CreateForTests(
        IInstanceStorageService? storage,
        IInstanceService? instances = null)
        => new(storage, instances);

    /// <summary>每个实例一行。</summary>
    public ObservableCollection<InstanceStorageRowViewModel> Rows { get; } = [];

    /// <summary>选中的实例行；配置档明细与去重目标都跟着它走。</summary>
    public InstanceStorageRowViewModel? SelectedRow
    {
        get => _selectedRow;
        set
        {
            if (ReferenceEquals(_selectedRow, value))
            {
                return;
            }

            if (_selectedRow is not null)
            {
                _selectedRow.IsSelected = false;
            }

            _selectedRow = value;
            if (_selectedRow is not null)
            {
                _selectedRow.IsSelected = true;
            }

            OnPropertyChanged();
            OnPropertyChanged(nameof(HasSelection));
            OnPropertyChanged(nameof(SelectedTitle));
            OnPropertyChanged(nameof(SelectedProfiles));
            OnPropertyChanged(nameof(SelectedProfilesEmptyText));
            OnPropertyChanged(nameof(HasProfiles));
            OnPropertyChanged(nameof(HasProfilesPlaceholder));
            NotifyCommandStates();
        }
    }

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    private string _errorMessage = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasStatusMessage))]
    private string _statusMessage = string.Empty;

    [ObservableProperty]
    private string _totalText = NotAvailable;

    [ObservableProperty]
    private string _profileTotalText = NotAvailable;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasDiagnostics))]
    private string _diagnosticsText = string.Empty;

    [ObservableProperty]
    private string _emptyTitle = "正在统计磁盘占用…";

    [ObservableProperty]
    private string _emptyDetail = string.Empty;

    [ObservableProperty]
    private bool _showEmptyState = true;

    [ObservableProperty]
    private bool _showContent;

    [ObservableProperty]
    private string _takenAtText = NotAvailable;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasDedupReport))]
    private string _dedupReportText = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanConfirmDedup))]
    private bool _isDedupConfirmationOpen;

    [ObservableProperty]
    private string _dedupConfirmText = string.Empty;

    [ObservableProperty]
    private bool _showDedupConfirmation;

    [ObservableProperty]
    private string _rowCountText = "0 个实例";

    /// <summary>是否有错误提示。</summary>
    public bool HasError => ErrorMessage.Length > 0;

    /// <summary>是否有成功/进度提示。</summary>
    public bool HasStatusMessage => StatusMessage.Length > 0;

    /// <summary>服务是否给了诊断（有目录没量到）。</summary>
    public bool HasDiagnostics => DiagnosticsText.Length > 0;

    /// <summary>去重报告是否有内容。</summary>
    public bool HasDedupReport => DedupReportText.Length > 0;

    /// <summary>是否选中了实例。</summary>
    public bool HasSelection => _selectedRow is not null;

    /// <summary>右侧明细标题。</summary>
    public string SelectedTitle => _selectedRow is null ? "未选择实例" : _selectedRow.Name;

    /// <summary>选中实例的配置档明细。</summary>
    public IReadOnlyList<ProfileStorageRowViewModel> SelectedProfiles
        => _selectedRow?.Profiles ?? Array.Empty<ProfileStorageRowViewModel>();

    /// <summary>选中实例是否有配置档明细可列。</summary>
    public bool HasProfiles => _selectedRow?.HasProfiles ?? false;

    /// <summary>明细区是否显示占位文案（没选中实例，或该实例没有配置档）。</summary>
    public bool HasProfilesPlaceholder => !HasProfiles;

    /// <summary>选中实例没有配置档时的占位文案。</summary>
    public string SelectedProfilesEmptyText => _selectedRow is null
        ? "在左侧选一个实例，这里列出它的配置档体积。"
        : "这个实例的 CKANProfiles 下还没有配置档。";

    /// <summary>命令是否可点（页面不忙）。</summary>
    public bool CanInteract => !IsBusy;

    /// <summary>确认去重是否可点：必须先有 dry-run 报告。</summary>
    public bool CanConfirmDedup => ShowDedupConfirmation && !IsBusy;

    /// <summary>视图首次挂到可视树时调用：只在第一次自动扫描，重新挂载不重复跑。</summary>
    public void Start()
    {
        if (_disposed || _started)
        {
            return;
        }

        _started = true;
        _ = ScanAsync();
    }

    /// <summary>视图离开可视树：没有定时器也没有后台任务，什么都不用停。</summary>
    public void Stop()
    {
    }

    /// <summary>扫描全部实例的磁盘占用。</summary>
    [RelayCommand(CanExecute = nameof(CanScan))]
    public async Task ScanAsync()
    {
        if (_disposed || IsBusy)
        {
            return;
        }

        if (_storage is null)
        {
            ApplyServiceMissingState();
            return;
        }

        IsBusy = true;
        ErrorMessage = string.Empty;
        StatusMessage = string.Empty;
        try
        {
            var result = await _storage.ScanAsync().ConfigureAwait(true);
            if (!result.IsSuccess || result.Value is null)
            {
                ShowError($"统计失败：{result.Message ?? "服务没有给出原因"}");
                ApplyScanFailedState(result.Message);
                return;
            }

            ApplyReport(result.Value);
        }
        catch (Exception ex)
        {
            // 服务契约不抛异常，页面仍是最后一道防线：不拿上一次的行冒充本次结果。
            ShowError($"统计失败：{ex.Message}");
            ApplyScanFailedState(ex.Message);
        }
        finally
        {
            IsBusy = false;
            NotifyCommandStates();
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        if (_instances is not null)
        {
            _instances.CurrentChanged -= OnCurrentChanged;
            _instances.InstancesChanged -= OnInstancesChanged;
        }
    }

    /// <summary>字节数转人读文字：B/KB/MB/GB，保留一位小数；取不到（null）与负数都显示「—」。</summary>
    /// <param name="bytes">字节数。</param>
    /// <returns>格式化结果。</returns>
    public static string BytesText(long bytes)
    {
        if (bytes < 0)
        {
            return NotAvailable;
        }

        if (bytes < 1024)
        {
            return bytes.ToString(CultureInfo.InvariantCulture) + " B";
        }

        if (bytes < 1024L * 1024)
        {
            return Format((double)bytes / 1024, "KB");
        }

        if (bytes < 1024L * 1024 * 1024)
        {
            return Format((double)bytes / (1024 * 1024), "MB");
        }

        return Format((double)bytes / (1024L * 1024 * 1024), "GB");
    }

    /// <summary>可空版本：null 显示「—」，绝不显示 0。</summary>
    /// <param name="bytes">字节数或 null。</param>
    /// <returns>格式化结果。</returns>
    public static string BytesText(long? bytes) => bytes is null ? NotAvailable : BytesText(bytes.Value);

    private static string Format(double value, string unit)
        => value.ToString("0.0", CultureInfo.InvariantCulture) + " " + unit;

    /// <summary>去重第一步：只跑 dry-run，把报告摆出来等用户确认，绝不写盘。</summary>
    [RelayCommand(CanExecute = nameof(CanDeduplicate))]
    public async Task DeduplicateAsync()
    {
        if (_disposed || IsBusy || _storage is null)
        {
            return;
        }

        var instanceName = ResolveInstanceName();
        if (instanceName is null)
        {
            ShowError("先在左侧选中一个实例，再去重它的配置档。");
            return;
        }

        IsBusy = true;
        ErrorMessage = string.Empty;
        StatusMessage = string.Empty;
        try
        {
            var result = await _storage.DeduplicateProfilesAsync(instanceName, dryRun: true).ConfigureAwait(true);
            if (!result.IsSuccess || result.Value is null)
            {
                ShowError($"预估失败：{result.Message ?? "服务没有给出原因"}");
                return;
            }

            SetDedupConfirmation(result.Value);
        }
        catch (Exception ex)
        {
            ShowError($"预估失败：{ex.Message}");
        }
        finally
        {
            IsBusy = false;
            NotifyCommandStates();
        }
    }

    /// <summary>去重第二步：用户确认后才以 dryRun=false 实跑。</summary>
    [RelayCommand(CanExecute = nameof(CanConfirmDedup))]
    public async Task ConfirmDedupAsync()
    {
        if (_disposed || IsBusy || _storage is null || _pendingDedup is null)
        {
            return;
        }

        var instanceName = _pendingDedup.InstanceName;
        IsBusy = true;
        ErrorMessage = string.Empty;
        StatusMessage = string.Empty;
        try
        {
            var result = await _storage.DeduplicateProfilesAsync(instanceName, dryRun: false).ConfigureAwait(true);
            if (!result.IsSuccess || result.Value is null)
            {
                // 失败时保留确认条：用户还能看到预估，也能再试或取消。
                ShowError($"合并失败：{result.Message ?? "服务没有给出原因"}");
                return;
            }

            ApplyDedupReport(result.Value);
            ShowDedupConfirmation = false;
            ShowStatus(BuildDedupSummary(result.Value, confirmed: true));
        }
        catch (Exception ex)
        {
            ShowError($"合并失败：{ex.Message}");
        }
        finally
        {
            IsBusy = false;
            NotifyCommandStates();
        }
    }

    /// <summary>取消去重：不改磁盘，也不清掉已经展示的报告。</summary>
    [RelayCommand]
    public void CancelDedup()
    {
        if (!ShowDedupConfirmation)
        {
            return;
        }

        _pendingDedup = null;
        DedupConfirmText = string.Empty;
        ShowDedupConfirmation = false;
        ShowStatus("已取消合并，没有写盘。");
    }

    /// <summary>
    /// 进入/退出确认态：把 dry-run 报告摆给用户，同时记住待实跑的实例名。
    /// 宿主与测试都走这一个入口，避免各自拼一份确认文案。
    /// </summary>
    /// <param name="report">dry-run 报告；null 表示退出确认态。</param>
    public void SetDedupConfirmation(ProfileDedupReport? report)
    {
        _pendingDedup = report;
        if (report is null)
        {
            DedupConfirmText = string.Empty;
            ShowDedupConfirmation = false;
            return;
        }

        ApplyDedupReport(report);
        DedupConfirmText = report.GroupCount == 0
            ? $"「{report.InstanceName}」没有可合并的重复文件（至少 128 KiB 的同内容文件才会进组）。继续只会再扫一遍，不会写盘。"
            : $"「{report.InstanceName}」有 {report.GroupCount} 组重复、{report.DuplicateFileCount} 个文件，合并后可释放 {BytesText(report.ReclaimableBytes)}。"
              + "确认后同内容文件会改成硬链接：内容不变，但删除其中一条路径会同时影响另一份。";
        ShowDedupConfirmation = true;
        ShowStatus(BuildDedupSummary(report, confirmed: false));
    }

    partial void OnIsBusyChanged(bool value)
    {
        OnPropertyChanged(nameof(CanInteract));
        NotifyCommandStates();
    }

    partial void OnShowDedupConfirmationChanged(bool value)
    {
        IsDedupConfirmationOpen = value;
        OnPropertyChanged(nameof(CanConfirmDedup));
        NotifyCommandStates();
    }

    private bool CanScan() => !IsBusy && _storage is not null;

    private bool CanDeduplicate() => !IsBusy && _storage is not null && ResolveInstanceName() is not null;

    private void ApplyReport(InstanceStorageReport report)
    {
        _report = report;

        // 选中项尽量保留：重新扫描后行对象是新的，按实例名接回去，用户不会每扫一次就丢选中。
        var previousName = _selectedRow?.Name;
        Rows.Clear();
        foreach (var row in report.Instances)
        {
            Rows.Add(new InstanceStorageRowViewModel(row));
        }

        var selection = Rows.FirstOrDefault(row =>
                previousName is not null && string.Equals(row.Name, previousName, StringComparison.OrdinalIgnoreCase))
            ?? Rows.FirstOrDefault(row => string.Equals(row.Name, ResolveInstanceName(), StringComparison.OrdinalIgnoreCase))
            ?? Rows.FirstOrDefault();
        SelectedRow = selection;

        TotalText = BytesText(report.TotalBytes);
        ProfileTotalText = BytesText(report.TotalProfileBytes);
        TakenAtText = report.TakenAtUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
        DiagnosticsText = report.Diagnostics.Count == 0
            ? string.Empty
            : string.Join("\n", report.Diagnostics);
        RowCountText = report.Instances.Count == 1 ? "1 个实例" : $"{report.Instances.Count} 个实例";

        if (report.IsEmpty)
        {
            ApplyNoInstanceState();
            return;
        }

        ShowContent = true;
        ShowEmptyState = false;
        EmptyTitle = string.Empty;
        EmptyDetail = string.Empty;
        NotifyCommandStates();
    }

    private void ApplyPendingState()
    {
        ShowContent = false;
        ShowEmptyState = true;
        EmptyTitle = "正在统计磁盘占用…";
        EmptyDetail = "页面会扫一遍全部实例的 GameData、CKAN 目录与 CKANProfiles，体积大的实例要等几秒。";
        NotifyCommandStates();
    }

    private void ApplyNoInstanceState()
    {
        Rows.Clear();
        SelectedRow = null;
        _report = null;
        TotalText = NotAvailable;
        ProfileTotalText = NotAvailable;
        TakenAtText = NotAvailable;
        RowCountText = "0 个实例";
        DiagnosticsText = string.Empty;
        ShowContent = false;
        ShowEmptyState = true;
        EmptyTitle = "还没有游戏实例";
        EmptyDetail = "在「实例」页添加一个 KSP 实例后回到这里，页面才有 GameData、CKAN 目录和配置档可以统计。";
        NotifyCommandStates();
    }

    private void ApplyServiceMissingState()
    {
        Rows.Clear();
        SelectedRow = null;
        _report = null;
        TotalText = NotAvailable;
        ProfileTotalText = NotAvailable;
        TakenAtText = NotAvailable;
        RowCountText = "0 个实例";
        DiagnosticsText = string.Empty;
        DedupReportText = string.Empty;
        ShowContent = false;
        ShowEmptyState = true;
        EmptyTitle = "存储服务未就绪";
        EmptyDetail = "页面没有拿到 IInstanceStorageService，暂时无法统计磁盘占用。这通常是宿主注册缺失。";
        NotifyCommandStates();
    }

    private void ApplyScanFailedState(string? reason)
    {
        // 扫描失败时清掉旧行：拿上一次的字节数冒充本次结果，比空表更误导。
        Rows.Clear();
        SelectedRow = null;
        _report = null;
        TotalText = NotAvailable;
        ProfileTotalText = NotAvailable;
        RowCountText = "0 个实例";
        ShowContent = false;
        ShowEmptyState = true;
        EmptyTitle = "统计失败";
        EmptyDetail = $"扫描实例磁盘占用时出错：{reason ?? "服务没有给出原因"}。可以再点一次「扫描」。";
        NotifyCommandStates();
    }

    private void ApplyDedupReport(ProfileDedupReport report)
    {
        var lines = new List<string>
        {
            report.DryRun
                ? $"预估（未写盘）：{report.InstanceName}"
                : $"已合并：{report.InstanceName}",
            $"重复组 {report.GroupCount} 组 · 文件 {report.DuplicateFileCount} 个 · 可释放 {BytesText(report.ReclaimableBytes)}",
        };

        if (report.Warnings.Count > 0)
        {
            lines.AddRange(report.Warnings);
        }

        DedupReportText = string.Join("\n", lines);
    }

    private static string BuildDedupSummary(ProfileDedupReport report, bool confirmed)
    {
        if (report.GroupCount == 0)
        {
            return confirmed
                ? $"「{report.InstanceName}」没有可合并的重复文件。"
                : $"「{report.InstanceName}」没有可合并的重复文件，不需要实跑。";
        }

        return confirmed
            ? $"「{report.InstanceName}」已合并 {report.GroupCount} 组、{report.DuplicateFileCount} 个文件，释放 {BytesText(report.ReclaimableBytes)}。"
            : $"「{report.InstanceName}」预估可合并 {report.GroupCount} 组、{report.DuplicateFileCount} 个文件，释放 {BytesText(report.ReclaimableBytes)}。确认前不会写盘。";
    }

    private string? ResolveInstanceName()
    {
        if (_selectedRow is not null)
        {
            return _selectedRow.Name;
        }

        var current = _instances?.Current;
        return string.IsNullOrWhiteSpace(current?.Name) ? null : current.Name;
    }

    private bool HasAnyInstance()
        => (_instances?.Instances.Count ?? 0) > 0 || _instances?.Current is not null;

    private void OnCurrentChanged(InstanceInfo? current)
    {
        if (_disposed)
        {
            return;
        }

        // 实例切换不改磁盘事实，只影响去重默认目标：选中行仍以列表为准，这里只刷新命令可用性。
        NotifyCommandStates();
        if (!ShowContent && _report is null && current is not null)
        {
            _ = ScanAsync();
        }
    }

    private void OnInstancesChanged()
    {
        if (_disposed)
        {
            return;
        }

        if (_report is null && HasAnyInstance())
        {
            ApplyPendingState();
            NotifyCommandStates();
        }
    }

    private void ShowStatus(string message)
    {
        StatusMessage = message;
        ErrorMessage = string.Empty;
    }

    private void ShowError(string message)
    {
        ErrorMessage = message;
        StatusMessage = string.Empty;
    }

    private void NotifyCommandStates()
    {
        ScanCommand.NotifyCanExecuteChanged();
        DeduplicateCommand.NotifyCanExecuteChanged();
        ConfirmDedupCommand.NotifyCanExecuteChanged();
    }
}
