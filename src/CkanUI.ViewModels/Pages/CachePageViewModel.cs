using CkanUI.Localization;
using CkanUI.Services.Abstractions;
using CkanUI.Services.Abstractions.Dtos;
using CkanUI.ViewModels.Navigation;
using CkanUI.ViewModels.Pages.Cache;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace CkanUI.ViewModels.Pages;

/// <summary>下载缓存管理页（F-50 / F-40 / UI-19）：看占用、按上限清理、清空、从 ZIP 灌入、打开目录、重置上限。</summary>
public sealed partial class CachePageViewModel : PageViewModel
{
    /// <summary>推荐的默认缓存容量上限（2048 MiB = 2 GiB）。</summary>
    public const long DefaultRecommendedLimitBytes = 2048L * 1024L * 1024L;

    private readonly ICacheService _service;
    private readonly IErrorReporter _errors;
    private readonly IDialogService _dialogs;
    private readonly IChangeRequestSink? _changeSink;
    private readonly INavigationService? _navigation;
    private ICacheShellLauncher? _shellLauncher;

    /// <summary>设计时构造：全部能力用空实现，保证预览器不崩。</summary>
    public CachePageViewModel()
        : this(new EmptyCacheService(), new EmptyErrorReporter(), new EmptyDialogService())
    {
    }

    /// <summary>运行时构造。</summary>
    public CachePageViewModel(
        ICacheService service,
        IErrorReporter errors,
        IDialogService dialogs,
        IChangeRequestSink? changeSink = null,
        INavigationService? navigation = null)
    {
        ArgumentNullException.ThrowIfNull(service);
        ArgumentNullException.ThrowIfNull(errors);
        ArgumentNullException.ThrowIfNull(dialogs);
        _service = service;
        _errors = errors;
        _dialogs = dialogs;
        _changeSink = changeSink;
        _navigation = navigation;
    }

    /// <inheritdoc />
    public override string Title => Text("下载缓存", LocalizationKeys.NavRailCache);

    /// <summary>宿主注入的 ZIP 选择器；为 <c>null</c> 时「从 ZIP 灌入」会给出中文错误。</summary>
    public ICacheZipPicker? ZipPicker { get; set; }

    /// <summary>
    /// 打开目录的宿主外壳服务：由视图在 DataContext 就绪后注入（与 SettingsPageView / InstancesPageView 同一套先例），
    /// VM 因此不引用 Avalonia；未注入时按钮给出友好的降级提示而不是崩。
    /// </summary>
    public ICacheShellLauncher? ShellLauncher
    {
        get => _shellLauncher;
        set
        {
            if (ReferenceEquals(_shellLauncher, value))
            {
                return;
            }

            _shellLauncher = value;
            OnPropertyChanged();
        }
    }

    /// <summary>缓存目录路径。</summary>
    [ObservableProperty]
    private string? _cacheDirectory;

    /// <summary>当前条目列表。</summary>
    [ObservableProperty]
    private IReadOnlyList<CacheEntryRowViewModel> _entries = Array.Empty<CacheEntryRowViewModel>();

    /// <summary>条目数。</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(PurgeToLimitCommand))]
    [NotifyCanExecuteChangedFor(nameof(PurgeAllCommand))]
    [NotifyCanExecuteChangedFor(nameof(DeleteSelectedCommand))]
    private int _entryCount;

    /// <summary>缓存总字节数。</summary>
    [ObservableProperty]
    private long _totalBytes;

    /// <summary>容量上限字节数；<c>null</c> 或非正数表示不限制。</summary>
    [ObservableProperty]
    private long? _sizeLimitBytes;

    /// <summary>使用率百分比（未设上限时为 0）。</summary>
    [ObservableProperty]
    private double _usagePercent;

    /// <summary>勾选中的条目数。</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(DeleteSelectedCommand))]
    private int _selectedCount;

    /// <summary>状态栏文案。</summary>
    [ObservableProperty]
    private string _statusText = string.Empty;

    /// <summary>错误文案。</summary>
    [ObservableProperty]
    private string? _errorText;

    /// <summary>错误细节（异常文本等）。</summary>
    [ObservableProperty]
    private string? _errorDetail;

    /// <summary>是否正在忙。</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RefreshCommand))]
    [NotifyCanExecuteChangedFor(nameof(PurgeToLimitCommand))]
    [NotifyCanExecuteChangedFor(nameof(PurgeAllCommand))]
    [NotifyCanExecuteChangedFor(nameof(ImportZipCommand))]
    [NotifyCanExecuteChangedFor(nameof(DeleteSelectedCommand))]
    [NotifyCanExecuteChangedFor(nameof(OpenCacheFolderCommand))]
    [NotifyCanExecuteChangedFor(nameof(ResetLimitCommand))]
    private bool _isBusy;

    /// <summary>忙碌遮罩文案。</summary>
    [ObservableProperty]
    private string _busyText = string.Empty;

    /// <summary>是否空闲（视图用来反灰按钮）。</summary>
    public bool IsIdle => !IsBusy;

    /// <summary>是否有错误。</summary>
    public bool HasError => !string.IsNullOrEmpty(ErrorText);

    /// <summary>是否有错误细节。</summary>
    public bool HasErrorDetail => !string.IsNullOrEmpty(ErrorDetail);

    /// <summary>刷新按钮可用性。</summary>
    public bool CanRefresh => !IsBusy;

    /// <summary>清理按钮可用性（没有条目时禁用）。</summary>
    public bool CanPurge => !IsBusy && EntryCount > 0;

    /// <summary>「从 ZIP 灌入」可用性（空缓存也能灌）。</summary>
    public bool CanImportZip => !IsBusy;

    /// <summary>「删除选中」可用性（没勾选时禁用）。</summary>
    public bool CanDeleteSelected => !IsBusy && SelectedCount > 0;

    /// <summary>打开缓存目录可用性。</summary>
    public bool CanOpenCacheFolder => !IsBusy;

    /// <summary>重置上限可用性。</summary>
    public bool CanResetLimit => !IsBusy;

    /// <summary>是否显示空状态。</summary>
    public bool ShowEmptyState => !IsBusy && EntryCount == 0;

    /// <summary>是否有容量上限。</summary>
    public bool HasLimit => SizeLimitBytes is { } limit && limit > 0;

    /// <summary>容量上限文案。</summary>
    public string LimitText => CacheFormat.FormatLimit(SizeLimitBytes);

    /// <summary>合计占用文案。</summary>
    public string TotalText => CacheFormat.FormatSize(TotalBytes);

    /// <summary>使用率文案。</summary>
    public string UsageText => HasLimit ? CacheFormat.FormatPercent(UsagePercent) : Text("未设上限", LocalizationKeys.CacheNoLimit);

    /// <summary>容量条右侧的单行摘要（上限 + 已用），免掉多段内联绑定。</summary>
    public string CapacityText => Text("上限 {0}  ·  已用 {1}", LocalizationKeys.CacheCapacityText, LimitText, UsageText);

    /// <summary>条目数文案。</summary>
    public string EntryCountText => EntryCount == 0 ? Text("缓存是空的", LocalizationKeys.CacheEntriesEmpty) : Text("共 {0} 个条目", LocalizationKeys.CacheEntriesCount, EntryCount);

    /// <summary>勾选摘要文案。</summary>
    public string SelectedText => SelectedCount == 0 ? Text("未选中条目", LocalizationKeys.CacheNoSelection) : Text("已选中 {0} 个条目", LocalizationKeys.CacheSelectionText, SelectedCount);

    /// <inheritdoc />
    protected override void OnActivated()
    {
        if (!IsBusy)
        {
            _ = RefreshAsync();
        }
    }

    /// <summary>重新读取统计与条目。</summary>
    [RelayCommand(CanExecute = nameof(CanRefreshCore))]
    private Task RefreshAsync() => RunAsync(LoadCoreAsync, "读取下载缓存", Text("正在读取下载缓存…", LocalizationKeys.CacheBusyLoading));

    /// <summary>按容量上限清理（删最久未修改的条目，跳过使用中的条目）。</summary>
    [RelayCommand(CanExecute = nameof(CanPurgeCore))]
    private Task PurgeToLimitAsync() => RunAsync(async () =>
    {
        var result = await _service.PurgeToLimitAsync();
        if (!result.IsSuccess || result.Value is null)
        {
            SetError(CacheErrorText.Compose(result.Message, Text("按上限清理下载缓存失败。", LocalizationKeys.CacheErrorPurgeToLimit), result.Retryable), result.Detail);
            return;
        }

        var purge = result.Value;
        await LoadCoreAsync();
        if (!HasError) SetStatus(Describe("已按上限清理", purge));
    }, "按上限清理下载缓存", Text("正在清理下载缓存…", LocalizationKeys.CacheBusyTrimming));

    /// <summary>清空下载缓存（保留正在使用的条目）。</summary>
    [RelayCommand(CanExecute = nameof(CanPurgeCore))]
    private Task PurgeAllAsync() => RunAsync(async () =>
    {
        var confirmed = await _dialogs.ConfirmAsync(
            Text("清空下载缓存", LocalizationKeys.CachePurgeTitle),
            Text("将删除 {0} 个缓存条目（{1}）。", LocalizationKeys.CachePurgeBody, EntryCount, TotalText),
            Text("正在被已安装模组使用的条目会被跳过，不会影响游戏运行。", LocalizationKeys.CachePurgeHint));
        if (!confirmed)
        {
            SetStatus(Text("已取消。", LocalizationKeys.CacheStatusCancelled));
            return;
        }

        var result = await _service.PurgeAllAsync();
        if (!result.IsSuccess || result.Value is null)
        {
            SetError(CacheErrorText.Compose(result.Message, Text("清空下载缓存失败。", LocalizationKeys.CacheErrorPurgeAll), result.Retryable), result.Detail);
            return;
        }

        var purge = result.Value;
        await LoadCoreAsync();
        if (!HasError) SetStatus(Describe("已清空下载缓存", purge));
    }, "清空下载缓存", Text("正在清空下载缓存…", LocalizationKeys.CacheBusyPurgingAll));

    /// <summary>从本地 ZIP 灌入缓存（校验 CKAN 压缩包后复制进缓存目录）。</summary>
    [RelayCommand(CanExecute = nameof(CanImportZipCore))]
    private Task ImportZipAsync() => RunAsync(async () =>
    {
        var picker = ZipPicker;
        if (picker is null)
        {
            SetError(Text("当前宿主没有提供文件选择能力，无法从 ZIP 灌入。", LocalizationKeys.CacheErrorNoFilePicker), null);
            return;
        }

        var path = await picker.PickZipFileAsync();
        if (string.IsNullOrWhiteSpace(path))
        {
            SetStatus(Text("已取消。", LocalizationKeys.CacheStatusCancelled));
            return;
        }

        var result = await _service.ImportZipAsync(path);
        if (!result.IsSuccess || result.Value is null)
        {
            SetError(CacheErrorText.Compose(result.Message, Text("从 ZIP 灌入下载缓存失败。", LocalizationKeys.CacheErrorImportZip), result.Retryable), result.Detail);
            return;
        }

        var entry = result.Value;
        await LoadCoreAsync();
        if (!HasError) SetStatus(Text("已灌入 {0}（{1}）。", LocalizationKeys.CacheStatusImported, entry.ModuleLabel, CacheFormat.FormatSize(entry.SizeBytes)));

        if (!entry.IsReferenced && !string.IsNullOrWhiteSpace(entry.Identifier))
        {
            var confirmed = await _dialogs.ConfirmAsync(
                Text("安装模组", LocalizationKeys.CacheInstallModTitle),
                Text("已成功将 {0} 导入缓存。是否立即前往变更集安装此模组？", LocalizationKeys.CacheInstallModBody, entry.ModuleLabel));
            if (confirmed)
            {
                _changeSink?.SetRequests(new[] { new ChangeRequest(entry.Identifier, ChangeAction.Install) });
                if (_navigation is not null)
                {
                    await _navigation.NavigateAsync("changes");
                }
            }
        }
    }, "从 ZIP 灌入下载缓存", Text("正在校验并复制 ZIP…", LocalizationKeys.CacheBusyImportingZip));

    /// <summary>删除勾选中的条目。</summary>
    [RelayCommand(CanExecute = nameof(CanDeleteSelectedCore))]
    private Task DeleteSelectedAsync() => RunAsync(async () =>
    {
        var selected = Entries.Where(row => row.IsSelected).ToList();
        if (selected.Count == 0)
        {
            SetStatus(Text("未选中条目。", LocalizationKeys.CacheStatusNoSelection));
            return;
        }

        var confirmed = await _dialogs.ConfirmAsync(
            Text("删除缓存条目", LocalizationKeys.CacheDeleteTitle),
            Text("将删除选中的 {0} 个条目。", LocalizationKeys.CacheDeleteBody, selected.Count),
            Text("正在被已安装模组使用的条目无法删除。", LocalizationKeys.CacheDeleteHint));
        if (!confirmed)
        {
            SetStatus(Text("已取消。", LocalizationKeys.CacheStatusCancelled));
            return;
        }

        var failures = new List<string>();
        foreach (var row in selected)
        {
            var result = await _service.DeleteEntryAsync(row.FileName);
            if (!result.IsSuccess)
            {
                failures.Add(CacheErrorText.Compose(result.Message, Text("删除失败。", LocalizationKeys.CacheErrorDelete), result.Retryable));
            }
        }

        await LoadCoreAsync();
        if (failures.Count > 0)
        {
            SetError(Text("有 {0} 个条目没能删除：{1}", LocalizationKeys.CacheErrorDeletePartial, failures.Count, failures[0]), string.Join(Environment.NewLine, failures));
            return;
        }

        if (!HasError) SetStatus(Text("已删除 {0} 个条目。", LocalizationKeys.CacheStatusDeleted, selected.Count));
    }, "删除选中的缓存条目", Text("正在删除缓存条目…", LocalizationKeys.CacheBusyDeleting));

    /// <summary>在文件管理器中打开下载缓存目录。</summary>
    [RelayCommand(CanExecute = nameof(CanOpenCacheFolderCore))]
    private Task OpenCacheFolderAsync() => RunAsync(async () =>
    {
        if (_shellLauncher is null)
        {
            SetStatus(Text("当前宿主没有提供打开目录的外壳能力。", LocalizationKeys.CacheErrorNoShell));
            return;
        }

        var directory = CacheDirectory;
        if (string.IsNullOrWhiteSpace(directory))
        {
            var dirResult = await _service.GetCacheDirectoryAsync();
            directory = dirResult.IsSuccess ? dirResult.Value : null;
        }

        if (string.IsNullOrWhiteSpace(directory))
        {
            SetError(Text("下载缓存目录为空，无法打开。", LocalizationKeys.CacheErrorDirEmpty), Text("未配置下载缓存目录且无法获取默认目录。", LocalizationKeys.CacheErrorDirUnset));
            return;
        }

        var result = await _shellLauncher.OpenFolderAsync(directory.Trim());
        if (result.IsSuccess)
        {
            SetStatus(Text("已打开下载缓存目录。", LocalizationKeys.CacheStatusDirOpened));
        }
        else
        {
            SetError(CacheErrorText.Compose(result.Message, Text("打开下载缓存目录失败。", LocalizationKeys.CacheErrorOpenDir), result.Retryable), result.Detail);
        }
    }, "打开下载缓存目录", Text("正在打开缓存目录…", LocalizationKeys.CacheBusyOpeningDir));

    /// <summary>重置缓存容量上限（弹出选项：重置为默认推荐 2048 MiB、设为无限制、取消）。</summary>
    [RelayCommand(CanExecute = nameof(CanResetLimitCore))]
    private Task ResetLimitAsync() => RunAsync(async () =>
    {
        var options = new[]
        {
            Text("重置为默认推荐上限（2048 MiB）", LocalizationKeys.CacheResetToDefault),
            Text("设为无限制", LocalizationKeys.CacheSetUnlimited)
        };

        var selectedIndex = await _dialogs.SelectAsync(
            Text("重置容量上限", LocalizationKeys.CacheResetLimitTitle),
            Text("请选择如何重置下载缓存容量上限：", LocalizationKeys.CacheResetLimitPrompt),
            options,
            defaultIndex: 0);

        if (selectedIndex < 0 || selectedIndex >= options.Length)
        {
            SetStatus(Text("已取消。", LocalizationKeys.CacheStatusCancelled));
            return;
        }

        long? targetLimit = selectedIndex == 0 ? DefaultRecommendedLimitBytes : null;
        var result = await _service.SetSizeLimitBytesAsync(targetLimit);
        if (!result.IsSuccess)
        {
            SetError(CacheErrorText.Compose(result.Message, Text("重置缓存容量上限失败。", LocalizationKeys.CacheErrorResetLimit), result.Retryable), result.Detail);
            return;
        }

        await LoadCoreAsync();
        if (!HasError)
        {
            SetStatus(targetLimit.HasValue
                ? Text("已将缓存容量上限重置为 {0}。", LocalizationKeys.CacheStatusLimitReset, CacheFormat.FormatSize(targetLimit.Value))
                : Text("已将缓存容量上限设为无限制。", LocalizationKeys.CacheStatusLimitUnlimited));
        }
    }, "重置缓存容量上限", Text("正在重置容量上限…", LocalizationKeys.CacheBusyResettingLimit));

    private bool CanRefreshCore() => CanRefresh;

    private bool CanPurgeCore() => CanPurge;

    private bool CanImportZipCore() => CanImportZip;

    private bool CanDeleteSelectedCore() => CanDeleteSelected;

    private bool CanOpenCacheFolderCore() => CanOpenCacheFolder;

    private bool CanResetLimitCore() => CanResetLimit;

    private async Task LoadCoreAsync()
    {
        var snapshot = await _service.GetSnapshotAsync();
        if (!snapshot.IsSuccess || snapshot.Value is null)
        {
            SetError(CacheErrorText.Compose(snapshot.Message, Text("读取下载缓存统计失败。", LocalizationKeys.CacheErrorLoadStats), snapshot.Retryable), snapshot.Detail);
            return;
        }

        var entries = await _service.ListEntriesAsync();
        if (!entries.IsSuccess || entries.Value is null)
        {
            SetError(CacheErrorText.Compose(entries.Message, Text("读取下载缓存条目失败。", LocalizationKeys.CacheErrorLoadEntries), entries.Retryable), entries.Detail);
            return;
        }

        ApplySnapshot(snapshot.Value);
        Entries = entries.Value.Select(entry => new CacheEntryRowViewModel(entry, OnRowSelectionChanged)).ToList();
        EntryCount = Entries.Count;
        SelectedCount = 0;
        SetStatus(Text("已读取 {0} 个缓存条目。", LocalizationKeys.CacheStatusLoaded, EntryCount));
    }

    private void ApplySnapshot(CacheSnapshot snapshot)
    {
        TotalBytes = snapshot.TotalBytes;
        SizeLimitBytes = snapshot.SizeLimitBytes;
        UsagePercent = snapshot.UsagePercent;
        EntryCount = snapshot.EntryCount;
        CacheDirectory = snapshot.CacheDirectory;
        OnPropertyChanged(nameof(HasLimit));
        OnPropertyChanged(nameof(LimitText));
        OnPropertyChanged(nameof(TotalText));
        OnPropertyChanged(nameof(UsageText));
        OnPropertyChanged(nameof(CapacityText));
        OnPropertyChanged(nameof(EntryCountText));
        OnPropertyChanged(nameof(ShowEmptyState));
    }

    private void OnRowSelectionChanged()
    {
        SelectedCount = Entries.Count(row => row.IsSelected);
        OnPropertyChanged(nameof(SelectedText));
    }

    private async Task RunAsync(Func<Task> action, string context, string busyText)
    {
        IsBusy = true;
        BusyText = busyText;
        ErrorText = null;
        ErrorDetail = null;
        try
        {
            await action();
        }
        catch (Exception ex)
        {
            _errors.Report(ex, context);
            SetError(CacheErrorText.FromException(ex, Text("下载缓存操作失败。", LocalizationKeys.CacheErrorOperation)), ex.Message);
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

    private static string Describe(string prefix, CachePurgeResult purge)
    {
        var text = prefix + "：删除 " + purge.DeletedCount + " 个条目，释放 " + CacheFormat.FormatSize(purge.FreedBytes) + "。";
        return purge.SkippedCount > 0 ? text + "跳过 " + purge.SkippedCount + " 个正在使用的条目。" : text;
    }

    /// <summary>关闭错误条。</summary>
    [RelayCommand]
    private void ClearError()
    {
        ErrorText = null;
        ErrorDetail = null;
    }

    partial void OnErrorTextChanged(string? value)
    {
        OnPropertyChanged(nameof(HasError));
        OnPropertyChanged(nameof(HasErrorDetail));
    }

    partial void OnErrorDetailChanged(string? value) => OnPropertyChanged(nameof(HasErrorDetail));

    partial void OnIsBusyChanged(bool value)
    {
        OnPropertyChanged(nameof(IsIdle));
        OnPropertyChanged(nameof(ShowEmptyState));
        OnPropertyChanged(nameof(CanRefresh));
        OnPropertyChanged(nameof(CanPurge));
        OnPropertyChanged(nameof(CanImportZip));
        OnPropertyChanged(nameof(CanDeleteSelected));
        OnPropertyChanged(nameof(CanOpenCacheFolder));
        OnPropertyChanged(nameof(CanResetLimit));
    }

    partial void OnEntryCountChanged(int value)
    {
        OnPropertyChanged(nameof(ShowEmptyState));
        OnPropertyChanged(nameof(EntryCountText));
        OnPropertyChanged(nameof(CanPurge));
    }

    partial void OnTotalBytesChanged(long value) => OnPropertyChanged(nameof(TotalText));

    partial void OnSizeLimitBytesChanged(long? value)
    {
        OnPropertyChanged(nameof(HasLimit));
        OnPropertyChanged(nameof(LimitText));
        OnPropertyChanged(nameof(UsageText));
        OnPropertyChanged(nameof(CapacityText));
    }

    partial void OnUsagePercentChanged(double value)
    {
        OnPropertyChanged(nameof(UsageText));
        OnPropertyChanged(nameof(CapacityText));
    }

    partial void OnSelectedCountChanged(int value)
    {
        OnPropertyChanged(nameof(SelectedText));
        OnPropertyChanged(nameof(CanDeleteSelected));
    }

    private sealed class EmptyCacheService : ICacheService
    {
        public Task<OperationResult<CacheSnapshot>> GetSnapshotAsync(CancellationToken ct = default)
            => Task.FromResult(OperationResult<CacheSnapshot>.Ok(new CacheSnapshot(0, 0, null, null)));

        public Task<OperationResult<IReadOnlyList<CacheEntryInfo>>> ListEntriesAsync(CancellationToken ct = default)
            => Task.FromResult(OperationResult<IReadOnlyList<CacheEntryInfo>>.Ok(Array.Empty<CacheEntryInfo>()));

        public Task<OperationResult<CachePurgeResult>> PurgeToLimitAsync(IProgress<OperationProgress>? progress = null, CancellationToken ct = default)
            => Task.FromResult(OperationResult<CachePurgeResult>.Fail("设计时未提供实现。", null, false));

        public Task<OperationResult<CachePurgeResult>> PurgeAllAsync(IProgress<OperationProgress>? progress = null, CancellationToken ct = default)
            => Task.FromResult(OperationResult<CachePurgeResult>.Fail("设计时未提供实现。", null, false));

        public Task<OperationResult<CacheEntryInfo>> ImportZipAsync(string sourceZipPath, CancellationToken ct = default)
            => Task.FromResult(OperationResult<CacheEntryInfo>.Fail("设计时未提供实现。", null, false));

        public Task<OperationResult> DeleteEntryAsync(string fileName, CancellationToken ct = default)
            => Task.FromResult(OperationResult.Fail("设计时未提供实现。", null, false));

        public Task<OperationResult<string?>> GetCacheDirectoryAsync(CancellationToken ct = default)
            => Task.FromResult(OperationResult<string?>.Ok(null));

        public Task<OperationResult> SetSizeLimitBytesAsync(long? limitBytes, CancellationToken ct = default)
            => Task.FromResult(OperationResult.Ok());
    }

    private sealed class EmptyErrorReporter : IErrorReporter
    {
        public event Action<ErrorEntry>? Reported
        {
            add { }
            remove { }
        }

        public IReadOnlyList<ErrorEntry> Recent => Array.Empty<ErrorEntry>();

        public void Report(Exception exception, string context, string? userMessage = null) { }

        public void Report(string context, string userMessage, string? detail = null) { }
    }

    private sealed class EmptyDialogService : IDialogService
    {
        public Task<bool> ConfirmAsync(string title, string message, string? detail = null) => Task.FromResult(false);

        public Task<int> SelectAsync(string title, string message, IReadOnlyList<string> options, int defaultIndex = 0) => Task.FromResult(-1);

        public Task ShowMessageAsync(string title, string message) => Task.CompletedTask;

        public Task ShowErrorAsync(string title, string message, string? detail = null) => Task.CompletedTask;

        public Task<OperationResult<bool>> RunWithProgressAsync(string title, Func<IProgress<double>, CancellationToken, Task> work)
            => Task.FromResult(OperationResult<bool>.Fail("设计时未提供实现。", null, false));
    }
}
