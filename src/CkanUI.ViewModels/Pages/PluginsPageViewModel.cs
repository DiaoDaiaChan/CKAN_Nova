using CkanUI.Localization;
using CkanUI.Services.Abstractions;
using CkanUI.Services.Abstractions.Dtos;
using CkanUI.ViewModels.Pages.Plugins;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace CkanUI.ViewModels.Pages;

/// <summary>
/// 插件页（12 篇 F-17 / 06 篇 UI-33）：Active / Dormant 两栏 + 激活/停用/重载/卸载/新增。
/// </summary>
/// <remarks>
/// <para>两栏口径对齐原版 <c>PluginsDialog</c> 的两个 <c>ListBox</c>：激活的插件在左栏，加载但没激活的在右栏。
/// 选中状态用两个 <c>SelectedItem</c> 而不是行内 <c>IsSelected</c>：原版就是单选列表，
/// 而且「同一时刻只有一个按钮组有效」正是单选的语义。</para>
/// <para>与服务的交互是「拉取」而不是订阅 <see cref="IPluginService.Changed"/>：所有状态变化都源自本页命令，
/// 命令结束后重新读一遍快照即可；订阅事件会让服务层可能在工作线程上回调，直接改 <c>ObservableCollection</c>
/// 会踩跨线程集合异常。事件仍然保留给未来的宿主级监听者（例如 Shell 刷新导航项）。</para>
/// </remarks>
public sealed partial class PluginsPageViewModel : PageViewModel
{
    private readonly IPluginService _service;
    private readonly IErrorReporter _errors;
    private bool _loaded;

    /// <summary>设计时构造：全部能力用空实现，保证预览器不崩。</summary>
    public PluginsPageViewModel()
        : this(new EmptyPluginService(), new EmptyErrorReporter())
    {
    }

    /// <summary>运行时构造。</summary>
    /// <param name="service">插件服务。</param>
    /// <param name="errors">错误上报。</param>
    public PluginsPageViewModel(IPluginService service, IErrorReporter errors)
    {
        ArgumentNullException.ThrowIfNull(service);
        ArgumentNullException.ThrowIfNull(errors);
        _service = service;
        _errors = errors;
        PluginsDirectory = service.PluginsDirectory;
    }

    /// <inheritdoc />
    public override string Title => Text("插件", LocalizationKeys.NavRailPlugins);

    /// <summary>宿主注入的 dll 选择器；为 <c>null</c> 时「新增」会给出中文错误。</summary>
    public IPluginFilePicker? FilePicker { get; set; }

    /// <summary>左栏：已激活的插件。</summary>
    [ObservableProperty]
    private IReadOnlyList<PluginRowViewModel> _activePlugins = Array.Empty<PluginRowViewModel>();

    /// <summary>右栏：已加载但未激活的插件。</summary>
    [ObservableProperty]
    private IReadOnlyList<PluginRowViewModel> _dormantPlugins = Array.Empty<PluginRowViewModel>();

    /// <summary>左栏选中项。</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(DeactivateCommand))]
    [NotifyCanExecuteChangedFor(nameof(ReloadCommand))]
    [NotifyCanExecuteChangedFor(nameof(UnloadCommand))]
    private PluginRowViewModel? _selectedActive;

    /// <summary>右栏选中项。</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ActivateCommand))]
    [NotifyCanExecuteChangedFor(nameof(ReloadCommand))]
    [NotifyCanExecuteChangedFor(nameof(UnloadCommand))]
    private PluginRowViewModel? _selectedDormant;

    /// <summary>插件目录（页脚展示，便于用户把 dll 直接丢进去）。</summary>
    [ObservableProperty]
    private string _pluginsDirectory = string.Empty;

    /// <summary>加载失败的 dll 摘要（每行一条中文原因）。</summary>
    [ObservableProperty]
    private IReadOnlyList<string> _failureTexts = Array.Empty<string>();

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
    [NotifyCanExecuteChangedFor(nameof(AddNewCommand))]
    [NotifyCanExecuteChangedFor(nameof(ActivateCommand))]
    [NotifyCanExecuteChangedFor(nameof(DeactivateCommand))]
    [NotifyCanExecuteChangedFor(nameof(ReloadCommand))]
    [NotifyCanExecuteChangedFor(nameof(UnloadCommand))]
    private bool _isBusy;

    /// <summary>忙碌遮罩文案。</summary>
    [ObservableProperty]
    private string _busyText = string.Empty;

    /// <summary>是否空闲。</summary>
    public bool IsIdle => !IsBusy;

    /// <summary>是否有错误。</summary>
    public bool HasError => !string.IsNullOrEmpty(ErrorText);

    /// <summary>是否有错误细节。</summary>
    public bool HasErrorDetail => !string.IsNullOrEmpty(ErrorDetail);

    /// <summary>是否有加载失败的 dll。</summary>
    public bool HasFailures => FailureTexts.Count > 0;

    /// <summary>左栏是否为空（视图显示空状态）。</summary>
    public bool HasNoActive => ActivePlugins.Count == 0;

    /// <summary>右栏是否为空。</summary>
    public bool HasNoDormant => DormantPlugins.Count == 0;

    /// <summary>左栏摘要。</summary>
    public string ActiveSummary => ActivePlugins.Count == 0 ? Text("没有激活的插件", LocalizationKeys.PluginsActiveNone) : Text("已激活 {0} 个", LocalizationKeys.PluginsActiveCount, ActivePlugins.Count);

    /// <summary>右栏摘要。</summary>
    public string DormantSummary => DormantPlugins.Count == 0 ? Text("没有待激活的插件", LocalizationKeys.PluginsDormantNone) : Text("未激活 {0} 个", LocalizationKeys.PluginsDormantCount, DormantPlugins.Count);

    /// <summary>当前选中项（两栏互斥，先看左栏）。</summary>
    public PluginRowViewModel? CurrentSelection => SelectedActive ?? SelectedDormant;

    /// <summary>「扫描目录」可用性。</summary>
    public bool CanRefresh => !IsBusy;

    /// <summary>「新增」可用性（没有选择器时仍可点，点了会报错说明原因）。</summary>
    public bool CanAddNew => !IsBusy;

    /// <summary>「激活」可用性：必须选中右栏的插件。</summary>
    public bool CanActivate => !IsBusy && SelectedDormant is not null;

    /// <summary>「停用」可用性：必须选中左栏的插件。</summary>
    public bool CanDeactivate => !IsBusy && SelectedActive is not null;

    /// <summary>「重载」可用性：任选一栏即可（重载保持原激活状态）。</summary>
    public bool CanReload => !IsBusy && CurrentSelection is not null;

    /// <summary>「卸载」可用性：任选一栏即可（激活的会先停用）。</summary>
    public bool CanUnload => !IsBusy && CurrentSelection is not null;

    /// <inheritdoc />
    protected override void OnActivated()
    {
        if (_loaded) return;
        _loaded = true;
        _ = RefreshAsync();
    }

    /// <summary>扫描插件目录（不激活任何插件）。</summary>
    [RelayCommand(CanExecute = nameof(CanRefreshCore))]
    private Task RefreshAsync() => RunAsync(
        async () =>
        {
            var result = await _service.DiscoverAsync().ConfigureAwait(true);
            ApplyState();
            if (!result.IsSuccess)
            {
                SetError(result.Message ?? Text("扫描插件目录失败。", LocalizationKeys.PluginsErrorScanFailed), result.Detail);
                return;
            }

            SetStatus(Text("已扫描插件目录：激活 {0} 个，未激活 {1} 个。", LocalizationKeys.PluginsStatusScanned, ActivePlugins.Count, DormantPlugins.Count));
        },
        "扫描插件目录",
        Text("正在扫描插件目录…", LocalizationKeys.PluginsBusyScanning));

    /// <summary>新增插件（选 dll → 复制进插件目录 → 加载为未激活）。</summary>
    [RelayCommand(CanExecute = nameof(CanAddNewCore))]
    private Task AddNewAsync() => RunAsync(
        async () =>
        {
            var picker = FilePicker;
            if (picker is null)
            {
                SetError(Text("当前视图没有提供文件选择器，无法新增插件。", LocalizationKeys.PluginsErrorNoFilePicker), null);
                return;
            }

            var path = await picker.PickPluginFileAsync().ConfigureAwait(true);
            if (string.IsNullOrWhiteSpace(path))
            {
                return;
            }

            var result = await _service.AddFromPathAsync(path).ConfigureAwait(true);
            ApplyState();
            ReportLoadResult(
                result,
                "已新增插件「{0}」。",
                LocalizationKeys.PluginsAdded,
                "已新增插件失败。",
                LocalizationKeys.PluginsAddedFailed);
        },
        "新增插件",
        Text("正在新增插件…", LocalizationKeys.PluginsBusyAdding));

    /// <summary>激活右栏选中的插件。</summary>
    [RelayCommand(CanExecute = nameof(CanActivateCore))]
    private Task ActivateAsync() => RunAsync(
        async () =>
        {
            var row = SelectedDormant;
            if (row is null) return;

            var result = await _service.ActivateAsync(row.Id).ConfigureAwait(true);
            ApplyState();
            ReportLoadResult(
                result,
                "已激活插件「{0}」。",
                LocalizationKeys.PluginsActivated,
                "已激活插件失败。",
                LocalizationKeys.PluginsActivateFailed);
        },
        "激活插件",
        Text("正在激活插件…", LocalizationKeys.PluginsBusyActivating));

    /// <summary>停用左栏选中的插件。</summary>
    [RelayCommand(CanExecute = nameof(CanDeactivateCore))]
    private Task DeactivateAsync() => RunAsync(
        async () =>
        {
            var row = SelectedActive;
            if (row is null) return;

            var result = await _service.DeactivateAsync(row.Id).ConfigureAwait(true);
            ApplyState();
            if (result.IsSuccess)
            {
                SetStatus(Text("已停用插件「{0}」。", LocalizationKeys.PluginsStatusDeactivated, row.DisplayName));
            }
            else
            {
                SetError(result.Message ?? Text("停用插件失败。", LocalizationKeys.PluginsErrorDeactivateFailed), result.Detail);
            }
        },
        "停用插件",
        Text("正在停用插件…", LocalizationKeys.PluginsBusyDeactivating));

    /// <summary>重载选中插件（先卸旧再装新，保持原激活状态）。</summary>
    [RelayCommand(CanExecute = nameof(CanReloadCore))]
    private Task ReloadAsync() => RunAsync(
        async () =>
        {
            var row = CurrentSelection;
            if (row is null) return;

            var result = await _service.ReloadAsync(row.Id).ConfigureAwait(true);
            ApplyState();
            ReportLoadResult(
                result,
                "已重载插件「{0}」。",
                LocalizationKeys.PluginsReloaded,
                "已重载插件失败。",
                LocalizationKeys.PluginsReloadFailed);
        },
        "重载插件",
        Text("正在重载插件…", LocalizationKeys.PluginsBusyReloading));

    /// <summary>卸载选中插件（dll 留在插件目录里，下次扫描会重新出现）。</summary>
    [RelayCommand(CanExecute = nameof(CanUnloadCore))]
    private Task UnloadAsync() => RunAsync(
        async () =>
        {
            var row = CurrentSelection;
            if (row is null) return;

            var result = await _service.UnloadAsync(row.Id).ConfigureAwait(true);
            ApplyState();
            if (result.IsSuccess)
            {
                SetStatus(Text("已卸载插件「{0}」。dll 仍在插件目录里，下次扫描会重新出现。", LocalizationKeys.PluginsStatusUninstalled, row.DisplayName));
            }
            else
            {
                SetError(result.Message ?? Text("卸载插件失败。", LocalizationKeys.PluginsErrorUninstallFailed), result.Detail);
            }
        },
        "卸载插件",
        Text("正在卸载插件…", LocalizationKeys.PluginsBusyUninstalling));

    /// <summary>关闭错误条。</summary>
    [RelayCommand]
    private void ClearError()
    {
        ErrorText = null;
        ErrorDetail = null;
    }

    private bool CanRefreshCore() => CanRefresh;

    private bool CanAddNewCore() => CanAddNew;

    private bool CanActivateCore() => CanActivate;

    private bool CanDeactivateCore() => CanDeactivate;

    private bool CanReloadCore() => CanReload;

    private bool CanUnloadCore() => CanUnload;

    private void ApplyState()
    {
        ActivePlugins = _service.Active.Select(info => new PluginRowViewModel(info)).ToArray();
        DormantPlugins = _service.Dormant.Select(info => new PluginRowViewModel(info)).ToArray();
        FailureTexts = _service.Failures
            .Select(info => info.Name + "：" + (info.FailureReason ?? Text("加载失败", LocalizationKeys.PluginsLoadFailedLabel)))
            .ToArray();
        PluginsDirectory = _service.PluginsDirectory;
        SelectedActive = null;
        SelectedDormant = null;

        OnPropertyChanged(nameof(HasFailures));
        OnPropertyChanged(nameof(HasNoActive));
        OnPropertyChanged(nameof(HasNoDormant));
        OnPropertyChanged(nameof(ActiveSummary));
        OnPropertyChanged(nameof(DormantSummary));
        OnPropertyChanged(nameof(CurrentSelection));
        OnPropertyChanged(nameof(CanReload));
        OnPropertyChanged(nameof(CanUnload));
    }

    /// <summary>把插件操作结果写进状态栏或错误栏：成功、失败各用一句整句文案（占位符 {0} 是插件名）。</summary>
    private void ReportLoadResult(PluginLoadResult result, string okFallback, string okKey, string failFallback, string failKey)
    {
        if (result.IsSuccess)
        {
            var name = result.Plugin?.DisplayName ?? Text("插件", LocalizationKeys.PluginsFallbackName);
            SetStatus(result.Message ?? Text(okFallback, okKey, name));
            return;
        }

        SetError(result.Message ?? Text(failFallback, failKey), result.Detail);
    }

    private async Task RunAsync(Func<Task> action, string context, string busyText)
    {
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
            SetError(context + "失败：" + ex.Message, ex.ToString());
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

    partial void OnSelectedActiveChanged(PluginRowViewModel? value)
    {
        if (value is not null && SelectedDormant is not null)
        {
            SelectedDormant = null;
        }

        OnPropertyChanged(nameof(CurrentSelection));
        OnPropertyChanged(nameof(CanReload));
        OnPropertyChanged(nameof(CanUnload));
    }

    partial void OnSelectedDormantChanged(PluginRowViewModel? value)
    {
        if (value is not null && SelectedActive is not null)
        {
            SelectedActive = null;
        }

        OnPropertyChanged(nameof(CurrentSelection));
        OnPropertyChanged(nameof(CanReload));
        OnPropertyChanged(nameof(CanUnload));
    }

    partial void OnErrorTextChanged(string? value) => OnPropertyChanged(nameof(HasError));

    partial void OnErrorDetailChanged(string? value) => OnPropertyChanged(nameof(HasErrorDetail));

    partial void OnIsBusyChanged(bool value)
    {
        OnPropertyChanged(nameof(IsIdle));
        OnPropertyChanged(nameof(CanRefresh));
        OnPropertyChanged(nameof(CanAddNew));
        OnPropertyChanged(nameof(CanActivate));
        OnPropertyChanged(nameof(CanDeactivate));
        OnPropertyChanged(nameof(CanReload));
        OnPropertyChanged(nameof(CanUnload));
    }

    private sealed class EmptyPluginService : IPluginService
    {
        public string PluginsDirectory => string.Empty;

        public IReadOnlyList<PluginInfo> Active => Array.Empty<PluginInfo>();

        public IReadOnlyList<PluginInfo> Dormant => Array.Empty<PluginInfo>();

        public IReadOnlyList<PluginInfo> Failures => Array.Empty<PluginInfo>();

        public event EventHandler? Changed
        {
            add { }
            remove { }
        }

        public Task<OperationResult<IReadOnlyList<PluginInfo>>> DiscoverAsync(CancellationToken ct = default)
            => Task.FromResult(OperationResult<IReadOnlyList<PluginInfo>>.Ok(Array.Empty<PluginInfo>()));

        public Task<PluginLoadResult> LoadAsync(string pluginPath, bool activate = false, CancellationToken ct = default)
            => Task.FromResult(PluginLoadResult.Fail(PluginLoadFailure.LoadFailed, "设计时未提供实现。"));

        public Task<PluginLoadResult> ActivateAsync(string pluginId, CancellationToken ct = default)
            => Task.FromResult(PluginLoadResult.Fail(PluginLoadFailure.LoadFailed, "设计时未提供实现。"));

        public Task<OperationResult> DeactivateAsync(string pluginId, CancellationToken ct = default)
            => Task.FromResult(OperationResult.Fail("设计时未提供实现。"));

        public Task<PluginLoadResult> ReloadAsync(string pluginId, CancellationToken ct = default)
            => Task.FromResult(PluginLoadResult.Fail(PluginLoadFailure.LoadFailed, "设计时未提供实现。"));

        public Task<OperationResult> UnloadAsync(string pluginId, CancellationToken ct = default)
            => Task.FromResult(OperationResult.Fail("设计时未提供实现。"));

        public Task<PluginLoadResult> AddFromPathAsync(string sourcePath, bool activate = false, CancellationToken ct = default)
            => Task.FromResult(PluginLoadResult.Fail(PluginLoadFailure.LoadFailed, "设计时未提供实现。"));
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
}
