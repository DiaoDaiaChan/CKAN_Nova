using CkanUI.Localization;
using CkanUI.Services.Abstractions;
using CkanUI.Services.Abstractions.Dtos;
using CkanUI.Services.Abstractions.Instances;
using CkanUI.Services.Abstractions.Profiles;
using CkanUI.ViewModels.Dialogs.InstanceDialogs;
using CkanUI.ViewModels.Pages.Instances;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace CkanUI.ViewModels.Pages;

/// <summary>
/// 游戏实例管理页（03 篇 §5 V-03 / 04 篇 §2 InstancesViewModel / 06 篇 UI-04·UI-05·UI-32·VM-04）：
/// 实例列表 + 右侧详情 + 内联表单 + 状态栏 + 切换遮罩。
/// </summary>
/// <remarks>
/// <para>分层约定：本类只做「取快照 → 丢给纯函数（<see cref="InstancesProjection"/>）→ 整体替换列表」与命令编排，
/// 列表排序/去重/目标目录推导都在纯函数里，校验在 <see cref="InstanceFormValidator"/> 里，因此整页可 headless 测试。</para>
/// <para>异常收口（05 篇）：所有服务调用、对话框调用、事件回调都裹 try/catch，异常转成
/// <see cref="ErrorText"/> + <see cref="IErrorReporter"/>，绝不让异常穿透到 UI 线程。</para>
/// <para>宿主能力（目录选择 / 路径探测 / 打开目录 / 启动游戏 / Steam 库扫描）以属性注入的纯接口形式提供，
/// 因为它们的实现属于 App 层（Avalonia 平台 API），而 VM 必须能由 DI 容器按类型构造。</para>
/// </remarks>
public sealed partial class InstancesPageViewModel : PageViewModel
{
    private readonly IInstanceService _instances;
    private readonly IDialogService _dialogs;
    private readonly IErrorReporter _errors;
    private bool _subscribed;

    private GameLaunchReleaseCoordinator? _releaseCoordinator;
    private IResourceReleaseService? _releaseService;
    private IInstanceLaunchExitSignal? _exitSignal;
    private Task? _releaseShutdown;

    // 本次启动的释放恢复是否已经做过一次：离开页面会先收尾一次，之后游戏真退出时不再重复恢复。
    private bool _releaseSettled;

    /// <summary>默认构造：仅供设计器与 <c>new InstancesPageViewModel()</c> 这类无容器场景使用，不连任何服务。</summary>
    public InstancesPageViewModel()
        : this(new EmptyInstanceService(), new EmptyDialogService(), new EmptyErrorReporter())
    {
    }

    /// <summary>依赖注入构造：参数全部是 Abstractions 里已注册的接口。</summary>
    public InstancesPageViewModel(IInstanceService instances, IDialogService dialogs, IErrorReporter errors)
    {
        _instances = instances ?? throw new ArgumentNullException(nameof(instances));
        _dialogs = dialogs ?? throw new ArgumentNullException(nameof(dialogs));
        _errors = errors ?? throw new ArgumentNullException(nameof(errors));

        Reload();
    }

    /// <inheritdoc />
    public override string Title => Text("实例", LocalizationKeys.InstancesTitle);

    // ---- 宿主注入（App 层实现，headless 测试塞假实现） --------------------

    /// <summary>目录选择器；未注入时「添加实例」会给出可读提示而不是崩溃。</summary>
    public IInstanceFolderPicker? FolderPicker { get; set; }

    /// <summary>路径探测器；未注入时不判断目录是否存在（列表不灰显）。</summary>
    public IInstancePathProbe? PathProbe { get; set; }

    /// <summary>外壳启动器（打开目录 / 启动游戏）。</summary>
    public IInstanceShellLauncher? ShellLauncher { get; set; }

    /// <summary>Steam 库探测来源。</summary>
    public ISteamLibraryLocator? SteamLibraries { get; set; }

    /// <summary>
    /// 启动命令的持久化（实例级 <c>CKAN/GUIConfig.json</c>，与老 GUI 共用，见 <see cref="LaunchCommandFile"/>）；
    /// 未注入时按默认命令启动、不提供保存。
    /// </summary>
    public IInstanceCommandStore? CommandStore { get; set; }

    /// <summary>单行文本输入通道（F-06 增 / 改一条命令）；未注入时给出可读提示而不是崩溃。</summary>
    public IInstanceTextPrompt? TextPrompt { get; set; }

    /// <summary>
    /// 启动后的分层资源释放（18 篇 §3/§7）。未注入时启动照常、只是不做释放——
    /// 容器里注册 <c>IResourceReleaseService</c> 之后由 App 层赋值即可。
    /// </summary>
    public IResourceReleaseService? ResourceRelease { get; set; }

    /// <summary>释放排程用的延时器（T3/T4/T5 的等待）；默认真实计时，测试换成可控时钟。</summary>
    public IInstancesDelayScheduler ReleaseScheduler { get; set; } = TaskInstancesDelayScheduler.Instance;

    /// <summary>UI 设置（取释放档位 <see cref="UiSettings.ReleaseTier"/>）；未注入时按均衡档。</summary>
    public IUiSettingsStore? UiSettings { get; set; }

    /// <summary>
    /// 实例配置档（整合包切换）：未注入时启动照常、不做任何切换。
    /// </summary>
    /// <remarks>
    /// 启动前切换而不是启动后：GameData 与 CKAN/registry.json 必须在对游戏进程可见之前就换好，
    /// 否则进去的是上一个整合包的世界。切换服务自己负责释放注册表锁，这里只管把失败挡在启动之前。
    /// </remarks>
    public IInstanceProfileService? Profiles { get; set; }

    /// <summary>
    /// 游戏时长自动累计（18 篇 §5 / 12 篇 F-48）：启动成功时开始计时、游戏退出时结算。
    /// 未注入时启动/退出照常，只是不自动累计（页面仍可手工补录）。
    /// </summary>
    public IPlayTimeTracker? PlayTime { get; set; }

    /// <summary>最近一次释放排程的任务（T3/T4/T5 时间轴），供测试等待；没有排程时为 null。</summary>
    public Task? PendingRelease => _releaseCoordinator?.Pending;

    /// <summary>最近一次「游戏退出 → 恢复资源」的任务，供测试等待；没有发生过时为 null。</summary>
    public Task? PendingReleaseShutdown => _releaseShutdown;

    // ---- 可观察状态 -------------------------------------------------------

    /// <summary>当前可见行（整体替换，不做增量更新；04 篇 §2.1）。</summary>
    [ObservableProperty]
    private IReadOnlyList<InstanceRowViewModel> _rows = Array.Empty<InstanceRowViewModel>();

    /// <summary>当前选中行（详情面板与命令的作用对象）。</summary>
    [ObservableProperty]
    private InstanceRowViewModel? _selectedRow;

    /// <summary>状态栏文本。</summary>
    [ObservableProperty]
    private string _statusText = "就绪";

    /// <summary>错误条主文案（03 篇 §8 D-03）；为空表示无错误。</summary>
    [ObservableProperty]
    private string? _errorText;

    /// <summary>错误条技术细节（可展开）。</summary>
    [ObservableProperty]
    private string? _errorDetail;

    /// <summary>是否有异步操作进行中（切换实例时视图铺全屏遮罩并禁用操作）。</summary>
    [ObservableProperty]
    private bool _isBusy;

    /// <summary>遮罩文案。</summary>
    [ObservableProperty]
    private string _busyText = "正在切换实例…";

    /// <summary>当前打开的内联表单（新增 / 克隆 / 重命名）；null 表示无表单。</summary>
    [ObservableProperty]
    private InstanceFormViewModel? _form;

    /// <summary>当前打开的启动命令编辑面板（F-06）；null 表示无面板。</summary>
    [ObservableProperty]
    private LaunchCommandsPanelViewModel? _commandsPanel;

    // ---- 启动状态机（18 篇 §4，E6） --------------------------------------

    /// <summary>当前启动阶段：空闲 / 准备（先还注册表锁）/ 启动 / 运行 / 恢复。</summary>
    [ObservableProperty]
    private GameLaunchPhase _launchPhase;

    /// <summary>
    /// 是否处于精简模式：注册表与仓库快照已被卸载（L2），进程里只剩界面与内存中的行数据。
    /// 游戏运行期间保持这个状态，退出或点「恢复数据」后解除。
    /// </summary>
    [ObservableProperty]
    private bool _isTrimMode;

    // ---- 派生状态 ---------------------------------------------------------

    /// <summary>列表是否非空。</summary>
    public bool HasRows => Rows.Count > 0;

    /// <summary>是否显示空状态（03 篇 §10：还没有游戏实例 + 主按钮「添加实例」）。</summary>
    public bool ShowEmptyState => !HasRows;

    /// <summary>列表计数文案。</summary>
    public string CountText => Text("共 {0} 个实例", LocalizationKeys.InstancesCount, Rows.Count);

    /// <summary>是否有错误。</summary>
    public bool HasError => !string.IsNullOrEmpty(ErrorText);

    /// <summary>是否有错误技术细节。</summary>
    public bool HasErrorDetail => !string.IsNullOrEmpty(ErrorDetail);

    /// <summary>是否有表单打开。</summary>
    public bool HasForm => Form is not null;

    /// <summary>是否空闲（用于禁用工具条按钮，避免并发操作）。</summary>
    public bool IsIdle => !IsBusy;

    /// <summary>启动阶段的可读文案（状态栏徽标）。</summary>
    public string LaunchPhaseText => LaunchPhase switch
    {
        GameLaunchPhase.Preparing => Text("准备启动…", LocalizationKeys.InstancesPhasePreparing),
        GameLaunchPhase.Starting => Text("启动中…", LocalizationKeys.InstancesPhaseStarting),
        GameLaunchPhase.Running => Text("游戏运行中", LocalizationKeys.InstancesPhaseRunning),
        GameLaunchPhase.Restoring => Text("恢复数据…", LocalizationKeys.InstancesPhaseRestoring),
        _ => Text("就绪", LocalizationKeys.CommonStatusReady),
    };

    /// <summary>启动阶段是否不是「空闲」（状态栏徽标据此显示）。</summary>
    public bool IsLaunchActive => LaunchPhase != GameLaunchPhase.Idle;

    /// <summary>是否正在启动或恢复（禁用启动按钮、显示忙碌态）。</summary>
    public bool IsLaunchBusy =>
        LaunchPhase is GameLaunchPhase.Preparing or GameLaunchPhase.Starting or GameLaunchPhase.Restoring;

    /// <summary>精简模式徽标文案；不在精简模式时为空串（徽标据此隐藏）。</summary>
    public string TrimModeText => IsTrimMode ? Text("精简模式", LocalizationKeys.InstancesTrimMode) : string.Empty;

    /// <summary>精简模式说明（悬停提示：为什么此刻列表不刷新、注册表不在手上）。</summary>
    public string TrimModeHint =>
        Text("已卸载注册表与仓库快照以让出启动资源；游戏退出或点「恢复数据」后恢复。", LocalizationKeys.InstancesTrimModeHint);

    /// <summary>是否可以手动恢复数据（精简模式且没有正在进行的启动/恢复）。</summary>
    public bool CanRestoreTrimMode => IsTrimMode && !IsLaunchBusy;

    /// <summary>空状态标题。</summary>
    public string EmptyStateTitle => Text("还没有游戏实例", LocalizationKeys.InstancesEmptyTitle);

    /// <summary>空状态说明。</summary>
    public string EmptyStateHint =>
        Text("添加一个 KSP 安装目录即可开始。", LocalizationKeys.InstancesEmptyHint);

    /// <summary>空状态主按钮文案。</summary>
    public string EmptyStateActionText => Text("添加实例", LocalizationKeys.InstancesEmptyAction);

    /// <summary>当前实例行。</summary>
    public InstanceRowViewModel? CurrentRow
    {
        get
        {
            foreach (var row in Rows)
            {
                if (row.IsCurrent)
                {
                    return row;
                }
            }

            return null;
        }
    }

    /// <summary>当前实例摘要（状态栏左侧）。</summary>
    public string CurrentSummary => CurrentRow is { } row ? Text("当前实例：{0}", LocalizationKeys.InstancesCurrentSummary, row.DisplayName) : Text("当前实例：未选择", LocalizationKeys.InstancesCurrentNone);

    /// <summary>是否有选中行。</summary>
    public bool HasSelection => SelectedRow is not null;

    /// <summary>详情面板标题。</summary>
    public string SelectionTitle => SelectedRow?.DisplayName ?? Text("未选择实例", LocalizationKeys.InstancesSelectionNone);

    /// <summary>详情面板路径。</summary>
    public string SelectionPath => SelectedRow?.GameDir ?? "—";

    /// <summary>详情面板版本。</summary>
    public string SelectionVersionText => SelectedRow?.VersionText ?? "—";

    /// <summary>详情面板模组数。</summary>
    public string SelectionModuleText => SelectedRow?.ModuleCountText ?? "—";

    /// <summary>详情面板锁定状态。</summary>
    public string SelectionLockText => SelectedRow?.LockText ?? "—";

    /// <summary>详情面板角色（当前/默认）。</summary>
    public string SelectionRoleText
    {
        get
        {
            if (SelectedRow is not { } row)
            {
                return "—";
            }

            var parts = new List<string>(2);
            if (row.IsCurrent)
            {
                parts.Add(Text("当前实例", LocalizationKeys.InstancesBadgeCurrent));
            }

            if (row.IsDefault)
            {
                parts.Add(Text("默认实例", LocalizationKeys.InstancesBadgeDefault));
            }

            return parts.Count == 0 ? Text("普通实例", LocalizationKeys.InstancesBadgePlain) : string.Join(" · ", parts);
        }
    }

    /// <summary>详情面板路径状态（目录不存在时提示重新定位）。</summary>
    public string SelectionPathStatus => SelectedRow?.PathStatusText ?? "—";

    /// <summary>「设为当前」是否可用。</summary>
    public bool CanSwitchSelected => SelectedRow?.CanSwitch == true;

    /// <summary>「遗忘」是否可用（当前实例不可遗忘）。</summary>
    public bool CanForgetSelected => SelectedRow?.CanForget == true;

    /// <summary>是否有可操作的选中行（打开目录 / 启动游戏 / 克隆 / 重命名）。</summary>
    public bool CanOpenSelected => SelectedRow is not null;

    /// <summary>是否打开了启动命令编辑面板。</summary>
    public bool HasCommandsPanel => CommandsPanel is not null;

    /// <summary>「编辑启动命令」是否可用（需要选中行且没有别的面板在开）。</summary>
    public bool CanEditCommands => SelectedRow is not null && !HasCommandsPanel;

    /// <summary>「启动游戏」按钮的悬浮提示。</summary>
    public string LaunchToolTip =>
        Text("按该实例的启动命令启动游戏；有多条命令时会先让你选一条。", LocalizationKeys.InstancesLaunchHint);

    // ---- 生命周期与刷新 ---------------------------------------------------

    /// <summary>激活页面：订阅服务事件并刷新列表（06 篇 M1 门禁⑤：切换后数据不串台）。</summary>
    protected override void OnActivated()
    {
        base.OnActivated();
        if (!_subscribed)
        {
            _instances.InstancesChanged += OnInstancesChanged;
            _instances.CurrentChanged += OnCurrentChanged;
            _subscribed = true;
        }

        Reload();
    }

    /// <summary>停用页面：退订事件，避免后台事件继续驱动已离开的页面。</summary>
    protected override void OnDeactivated()
    {
        base.OnDeactivated();
        if (_subscribed)
        {
            _instances.InstancesChanged -= OnInstancesChanged;
            _instances.CurrentChanged -= OnCurrentChanged;
            _subscribed = false;
        }

        // 离开页面也要收尾：取消后续释放层并恢复数据，否则 L2 之后列表会是空的。
        // 但只要还有未闭合的计时会话，就保留退出订阅：游戏通常在用户离开此页之后才退出，
        // 退订会让结算信号彻底丢失，整段时长会在下次启动时被丢弃（宁可晚一点写盘，不要丢数据）。
        if (PlayTime?.ActiveInstanceName is null)
        {
            DetachExitSignal();
        }

        _releaseShutdown = StopReleaseScheduleAsync();
    }

    /// <summary>重新投影实例列表并保持选中项（名称不变则选中项不变）。</summary>
    public void Reload()
    {
        var previousName = SelectedRow?.Name;
        try
        {
            var snapshot = _instances.Instances;
            var current = _instances.Current;
            var probe = PathProbe;
            Rows = InstancesProjection.Project(
                snapshot,
                current?.Name,
                probe is null ? null : probe.DirectoryExists);
        }
        catch (Exception ex)
        {
            Rows = Array.Empty<InstanceRowViewModel>();
            OnException(ex, "刷新实例列表");
        }

        SelectedRow = ResolveSelection(previousName);
    }

    /// <summary>命令：刷新（F5 / 工具条按钮）。</summary>
    [RelayCommand]
    private async Task RefreshAsync()
    {
        if (IsBusy)
        {
            return;
        }

        ClearError();
        IsBusy = true;
        BusyText = Text("正在扫描实例…", LocalizationKeys.InstancesBusyScanning);
        var offThread = !string.Equals(Environment.GetEnvironmentVariable("CKANUI_INSTANCES_SYNC"), "1", StringComparison.Ordinal);
        try
        {
            var result = offThread
                ? await Task.Run(() => _instances.RefreshAsync()).ConfigureAwait(true)
                : await _instances.RefreshAsync().ConfigureAwait(true);
            if (!result.IsSuccess)
            {
                SetError(InstancesErrorText.Compose(result.Message, "刷新实例列表失败", result.Retryable), result.Detail);
            }
            else
            {
                StatusText = Text("实例列表已刷新。", LocalizationKeys.InstancesStatusRefreshed);
            }
        }
        catch (Exception ex)
        {
            OnException(ex, "刷新实例列表");
        }
        finally
        {
            IsBusy = false;
            if (offThread)
            {
                await ReloadOffThreadAsync().ConfigureAwait(true);
            }
            else
            {
                Reload();
            }
        }
    }

    /// <summary>
    /// 与 <see cref="Reload"/> 等价，但把逐实例的磁盘探测（目录存在性）挪到线程池，
    /// 回 UI 线程只做一次集合赋值 —— 实例多、磁盘慢时刷新按钮不再冻住几百毫秒。
    /// </summary>
    private async Task ReloadOffThreadAsync()
    {
        var previousName = SelectedRow?.Name;
        IReadOnlyList<InstanceRowViewModel> rows;
        try
        {
            var snapshot = _instances.Instances;
            var current = _instances.Current;
            var probe = PathProbe;
            rows = await Task.Run(() => InstancesProjection.Project(
                snapshot,
                current?.Name,
                probe is null ? null : probe.DirectoryExists)).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            rows = Array.Empty<InstanceRowViewModel>();
            OnException(ex, "刷新实例列表");
        }

        Rows = rows;
        SelectedRow = ResolveSelection(previousName);
    }

    // ---- 切换实例 ---------------------------------------------------------

    /// <summary>命令：把选中实例设为当前实例。</summary>
    [RelayCommand]
    private Task SwitchAsync() => SwitchToAsync(SelectedRow);

    /// <summary>
    /// 切换实例（02 篇 §5：DisposeAll → SetCurrentInstance → 重新 Instance，全部由服务层保证）。
    /// 锁定检测失败属于「不可重试」错误，且必须保持原实例不变（03 篇 §5 异常态）。
    /// </summary>
    public async Task SwitchToAsync(InstanceRowViewModel? row)
    {
        row ??= SelectedRow;
        if (row is null)
        {
            SetError(Text("请先选择一个实例。", LocalizationKeys.InstancesErrorSelectFirst), null);
            return;
        }

        if (IsBusy)
        {
            return;
        }

        ClearError();
        if (row.IsCurrent)
        {
            StatusText = Text("「{0}」已经是当前实例。", LocalizationKeys.InstancesStatusAlreadyCurrent, row.Name);
            return;
        }

        if (row.IsLocked)
        {
            // 锁定 = 有别的进程持有该实例的注册表锁：重试没有意义，必须让用户先关掉占用方。
            SetError(
                Text("实例「{0}」被其他进程锁定（不可重试）：请先关闭占用该目录的程序，再点「刷新」后重试。", LocalizationKeys.InstancesErrorSwitchLocked, row.Name),
                row.GameDir);
            return;
        }

        IsBusy = true;
        BusyText = Text("正在切换实例…", LocalizationKeys.InstancesBusySwitching);
        try
        {
            var result = await _instances.SwitchAsync(row.Name);
            if (result.IsSuccess)
            {
                StatusText = Text("已切换到「{0}」。", LocalizationKeys.InstancesStatusSwitched, result.Value?.Name ?? row.Name);
            }
            else
            {
                SetError(InstancesErrorText.Compose(result.Message, "切换实例失败", result.Retryable), result.Detail);
                StatusText = Text("切换失败，仍停留在原实例。", LocalizationKeys.InstancesStatusSwitchFailed);
            }
        }
        catch (Exception ex)
        {
            OnException(ex, "切换实例");
        }
        finally
        {
            IsBusy = false;
            Reload();
        }
    }

    // ---- 添加 / 克隆 / 重命名 --------------------------------------------

    /// <summary>命令：添加实例（先选目录，再在内联表单里确认名称）。</summary>
    [RelayCommand]
    private async Task AddAsync()
    {
        if (IsBusy)
        {
            return;
        }

        ClearError();
        if (FolderPicker is not { } picker)
        {
            SetError(Text("当前环境没有可用的目录选择器，无法添加实例。", LocalizationKeys.InstancesErrorNoFolderPicker), null);
            return;
        }

        string? picked;
        try
        {
            picked = await picker.PickFolderAsync(Text("选择 KSP 安装目录", LocalizationKeys.InstancesPickFolderTitle), CurrentRow?.GameDir);
        }
        catch (Exception ex)
        {
            OnException(ex, "选择实例目录");
            return;
        }

        if (string.IsNullOrWhiteSpace(picked))
        {
            StatusText = Text("已取消选择目录。", LocalizationKeys.InstancesStatusFolderPickCancelled);
            return;
        }

        AddFromPath(picked.Trim());
    }

    /// <summary>用给定目录打开「添加实例」表单（也供首次向导复用）。</summary>
    public void AddFromPath(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ClearError();

        if (InstancesProjection.ContainsPath(SafeSnapshot(), path))
        {
            SetError(Text("该目录已经是实例列表中的实例：{0}", LocalizationKeys.InstancesErrorDuplicatePath, path), null);
            return;
        }

        if (PathProbe is { } probe && !SafeProbe(probe, path))
        {
            SetError(Text("目录不存在：{0}（不可重试）", LocalizationKeys.InstancesErrorDirectoryMissing, path), null);
            return;
        }

        var form = new InstanceFormViewModel(InstanceFormMode.Add, null, path)
        {
            // 名称默认取目录名，路径必须一并回填：提交时表单会用 Path 调 AddAsync，漏填等于把空路径发给服务层。
            Name = Path.GetFileName(Path.TrimEndingDirectorySeparator(path)),
            Path = path,
        };
        OpenForm(form);
    }

    /// <summary>命令：克隆选中实例（弹表单填新名称）。</summary>
    [RelayCommand]
    private void BeginClone()
    {
        if (SelectedRow is not { } row)
        {
            SetError(Text("请先选择要克隆的实例。", LocalizationKeys.InstancesErrorSelectToClone), null);
            return;
        }

        ClearError();
        OpenForm(new InstanceFormViewModel(InstanceFormMode.Clone, row.Name, row.GameDir));
    }

    /// <summary>命令：重命名选中实例（弹表单填新名称）。</summary>
    [RelayCommand]
    private void BeginRename()
    {
        if (SelectedRow is not { } row)
        {
            SetError(Text("请先选择要重命名的实例。", LocalizationKeys.InstancesErrorSelectToRename), null);
            return;
        }

        ClearError();
        OpenForm(new InstanceFormViewModel(InstanceFormMode.Rename, row.Name, row.GameDir) { Name = row.Name });
    }

    /// <summary>打开表单并接线（提交交回本页，取消即关闭）。</summary>
    public void OpenForm(InstanceFormViewModel form)
    {
        ArgumentNullException.ThrowIfNull(form);
        form.SetExistingNames(Rows.Select(static r => r.Name));
        form.SubmitRequested = () => SubmitFormAsync(form);
        form.CancelRequested = CloseForm;
        Form = form;
    }

    /// <summary>关闭表单（取消）。</summary>
    public void CloseForm()
    {
        Form = null;
        StatusText = Text("已取消。", LocalizationKeys.InstancesStatusCancelled);
    }

    /// <summary>表单提交入口：按模式分发到添加 / 克隆 / 重命名。</summary>
    public async Task SubmitFormAsync(InstanceFormViewModel form)
    {
        ArgumentNullException.ThrowIfNull(form);
        if (IsBusy)
        {
            return;
        }

        switch (form.Mode)
        {
            case InstanceFormMode.Add:
                await AddInstanceAsync(form);
                break;
            case InstanceFormMode.Clone:
                await CloneInstanceAsync(form);
                break;
            default:
                await RenameInstanceAsync(form);
                break;
        }
    }

    private async Task AddInstanceAsync(InstanceFormViewModel form)
    {
        var path = form.Path.Trim();
        var name = form.Name.Trim();
        IsBusy = true;
        BusyText = Text("正在添加实例…", LocalizationKeys.InstancesBusyAdding);
        try
        {
            var result = await _instances.AddAsync(path, name);
            if (!result.IsSuccess)
            {
                SetError(InstancesErrorText.Compose(result.Message, "添加实例失败", result.Retryable), result.Detail);
                return;
            }

            CloseForm();
            ClearError();
            StatusText = Text("已添加实例「{0}」。", LocalizationKeys.InstancesStatusAdded, result.Value?.Name ?? name);
        }
        catch (Exception ex)
        {
            OnException(ex, "添加实例");
        }
        finally
        {
            IsBusy = false;
            Reload();
        }
    }

    private async Task CloneInstanceAsync(InstanceFormViewModel form)
    {
        var sourceName = form.SourceName ?? string.Empty;
        var newName = form.Name.Trim();
        var sourceDir = form.SourcePath ?? SelectedRow?.GameDir;
        var destination = InstancesProjection.CloneDestination(sourceDir, newName);

        if (PathProbe is { } probe && SafeProbe(probe, destination))
        {
            bool proceed;
            try
            {
                proceed = await _dialogs.ConfirmAsync(
                    Text("目标目录已存在", LocalizationKeys.InstancesCloneConflictTitle),
                    Text("克隆目标目录「{0}」已经存在，继续会把源实例的文件覆盖进去。是否继续？", LocalizationKeys.InstancesCloneConflictBody, destination),
                    destination);
            }
            catch (Exception ex)
            {
                OnException(ex, "确认克隆目标目录");
                return;
            }

            if (!proceed)
            {
                StatusText = Text("已取消克隆：目标目录已存在。", LocalizationKeys.InstancesStatusCloneCancelled);
                return;
            }
        }

        IsBusy = true;
        BusyText = Text("正在克隆实例（可能需要几分钟）…", LocalizationKeys.InstancesBusyCloning);
        try
        {
            var result = await _instances.CloneAsync(sourceName, newName);
            if (!result.IsSuccess)
            {
                SetError(InstancesErrorText.Compose(result.Message, "克隆实例失败", result.Retryable), result.Detail);
                return;
            }

            CloseForm();
            ClearError();
            StatusText = Text("已克隆为「{0}」。", LocalizationKeys.InstancesStatusCloned, result.Value?.Name ?? newName);
        }
        catch (Exception ex)
        {
            OnException(ex, "克隆实例");
        }
        finally
        {
            IsBusy = false;
            Reload();
        }
    }

    private async Task RenameInstanceAsync(InstanceFormViewModel form)
    {
        var sourceName = form.SourceName ?? string.Empty;
        var newName = form.Name.Trim();

        bool confirmed;
        try
        {
            confirmed = await _dialogs.ConfirmAsync(
                Text("重命名实例", LocalizationKeys.InstancesRenameTitle),
                Text("确定把实例「{0}」重命名为「{1}」吗？只会修改实例记录，不会改名游戏目录。", LocalizationKeys.InstancesRenameBody, sourceName, newName),
                form.SourcePath);
        }
        catch (Exception ex)
        {
            OnException(ex, "确认重命名实例");
            return;
        }

        if (!confirmed)
        {
            StatusText = Text("已取消重命名。", LocalizationKeys.InstancesStatusRenameCancelled);
            return;
        }

        // 契约冻结：IInstanceService 目前没有 RenameAsync（只有 Add/Clone/Remove/Switch/Refresh）。
        // 不擅自用「Remove + Add」模拟——Add 失败会让实例记录丢失，风险不可接受，因此这里给出阻塞说明。
        CloseForm();
        SetError(
            "重命名需要 IInstanceService.RenameAsync，该契约尚未提供（已列入待 Lead 决策）；本次未修改任何记录。",
            "IInstanceService（src/CkanUI.Services.Abstractions/IInstanceService.cs）冻结，只暴露 Instances/Current/RefreshAsync/SwitchAsync/AddAsync/CloneAsync/RemoveAsync。");
    }

    // ---- 遗忘 / 打开目录 / 启动游戏 / Steam 导入 --------------------------

    /// <summary>命令：遗忘选中实例（危险操作，必须二次确认）。</summary>
    [RelayCommand]
    private Task ForgetAsync() => ForgetSelectedAsync(SelectedRow);

    /// <summary>遗忘实例：只移除记录，不动磁盘文件；当前实例不可遗忘。</summary>
    public async Task ForgetSelectedAsync(InstanceRowViewModel? row)
    {
        row ??= SelectedRow;
        if (row is null)
        {
            SetError(Text("请先选择要遗忘的实例。", LocalizationKeys.InstancesErrorSelectToForget), null);
            return;
        }

        if (IsBusy)
        {
            return;
        }

        ClearError();
        if (row.IsCurrent)
        {
            SetError(Text("不能遗忘当前正在使用的实例：请先切换到其他实例。", LocalizationKeys.InstancesErrorForgetCurrent), row.GameDir);
            return;
        }

        bool confirmed;
        try
        {
            confirmed = await _dialogs.ConfirmAsync(
                Text("遗忘实例", LocalizationKeys.InstancesForgetTitle),
                Text("确定要遗忘实例「{0}」吗？只会从实例列表中移除记录，不会删除游戏目录里的任何文件。", LocalizationKeys.InstancesForgetBody, row.Name),
                row.GameDir);
        }
        catch (Exception ex)
        {
            OnException(ex, "确认遗忘实例");
            return;
        }

        if (!confirmed)
        {
            StatusText = Text("已取消遗忘。", LocalizationKeys.InstancesStatusForgetCancelled);
            return;
        }

        IsBusy = true;
        BusyText = Text("正在遗忘实例…", LocalizationKeys.InstancesBusyForgetting);
        try
        {
            var result = await _instances.RemoveAsync(row.Name);
            if (!result.IsSuccess)
            {
                SetError(InstancesErrorText.Compose(result.Message, "遗忘实例失败", result.Retryable), result.Detail);
                return;
            }

            StatusText = Text("已遗忘实例「{0}」，游戏目录未做任何改动。", LocalizationKeys.InstancesStatusForgotten, row.Name);
        }
        catch (Exception ex)
        {
            OnException(ex, "遗忘实例");
        }
        finally
        {
            IsBusy = false;
            Reload();
        }
    }

    /// <summary>命令：在文件管理器中打开选中实例的目录。</summary>
    [RelayCommand]
    private Task OpenFolderAsync() => OpenSelectedFolderAsync(SelectedRow);

    /// <summary>打开实例目录。</summary>
    public async Task OpenSelectedFolderAsync(InstanceRowViewModel? row)
    {
        row ??= SelectedRow;
        if (row is null)
        {
            SetError(Text("请先选择一个实例。", LocalizationKeys.InstancesErrorSelectFirst), null);
            return;
        }

        ClearError();
        if (ShellLauncher is not { } launcher)
        {
            SetError(Text("当前环境没有可用的外壳启动器，无法打开目录。", LocalizationKeys.InstancesErrorNoShellLauncher), null);
            return;
        }

        try
        {
            var result = await launcher.OpenFolderAsync(row.GameDir);
            if (!result.IsSuccess)
            {
                SetError(InstancesErrorText.Compose(result.Message, "打开目录失败", result.Retryable), result.Detail);
                return;
            }

            StatusText = Text("已在文件管理器中打开：{0}", LocalizationKeys.InstancesStatusOpenedFolder, row.GameDir);
        }
        catch (Exception ex)
        {
            OnException(ex, "打开实例目录");
        }
    }

    /// <summary>命令：启动选中实例的游戏。</summary>
    [RelayCommand]
    private Task LaunchAsync() => LaunchSelectedAsync(SelectedRow);

    /// <summary>
    /// 启动游戏（F-05）：取当前实例的启动命令列表 —— 只有一条就直接用；多条则让用户选一条（取消不启动），
    /// 然后把「完整命令行 + 游戏目录」交给 <see cref="IInstanceShellLauncher"/>。
    /// </summary>
    /// <remarks>
    /// 进程创建在 App 层（外壳启动器）里做，VM 不碰 <c>Process.Start</c>；真正的启动状态机
    /// （DisposeAll → 启动 → 精简模式）属于 18 篇/E6，接入时替换 <see cref="IInstanceShellLauncher"/> 实现即可。
    /// </remarks>
    public async Task LaunchSelectedAsync(InstanceRowViewModel? row)
    {
        row ??= SelectedRow;
        if (row is null)
        {
            SetError(Text("请先选择一个实例。", LocalizationKeys.InstancesErrorSelectFirst), null);
            return;
        }

        ClearError();
        if (row.IsLocked)
        {
            SetError(Text("实例「{0}」被其他进程占用（可能游戏已在运行），不能重复启动。", LocalizationKeys.InstancesErrorBusyLaunching, row.Name), row.GameDir);
            return;
        }

        var commands = await ResolveCommandsAsync(row).ConfigureAwait(true);
        if (!commands.IsSuccess)
        {
            SetError(InstancesErrorText.Compose(commands.Message, "读取启动命令失败", commands.Retryable), commands.Detail);
            return;
        }

        var available = commands.Value ?? Array.Empty<string>();
        if (available.Count == 0)
        {
            SetError(Text("实例「{0}」没有可用的启动命令，请先编辑启动命令。", LocalizationKeys.InstancesErrorNoLaunchCommand, row.Name), row.GameDir);
            return;
        }

        var command = available[0];
        if (available.Count > 1)
        {
            var index = await _dialogs
                .SelectAsync(Text("启动「{0}」", LocalizationKeys.InstancesLaunchDialogTitle, row.Name), Text("该实例有多条启动命令，请选择要使用的一条：", LocalizationKeys.InstancesLaunchDialogBody), available, 0)
                .ConfigureAwait(true);
            if (index < 0 || index >= available.Count)
            {
                StatusText = Text("已取消启动。", LocalizationKeys.InstancesStatusLaunchCancelled);
                return;
            }

            command = available[index];
        }

        if (ShellLauncher is not { } launcher)
        {
            // 没有外壳启动器（设计时宿主 / 受限环境）就拉不起进程：不假装「已启动」，也不动注册表锁。
            StatusText = Text("启动游戏尚未接入（未注入外壳启动器）；可先用「打开目录」自行启动。", LocalizationKeys.InstancesErrorLauncherMissing);
            return;
        }

        try
        {
            // 18 篇 §4 T0 的前置动作：先把 CKAN/registry.locked 还回去，再拉进程。
            // 放锁必须发生在启动之前——游戏侧工具看到锁会以为 CKAN 正在改写实例。
            LaunchPhase = GameLaunchPhase.Preparing;

            // 启动配置档（整合包切换）：必须在放锁之前完成——切换会自己关掉注册表再换目录、换完重开。
            if (Profiles is { } profiles)
            {
                var applied = await profiles.ApplyLaunchProfileAsync(row.Name).ConfigureAwait(true);
                if (!applied.IsSuccess)
                {
                    LaunchPhase = GameLaunchPhase.Idle;
                    SetError(InstancesErrorText.ComposeStep("切换启动配置档失败", applied.Message, applied.Retryable), applied.Detail);
                    return;
                }
            }

            await ReleaseRegistryLockBeforeLaunchAsync().ConfigureAwait(true);

            LaunchPhase = GameLaunchPhase.Starting;
            var result = await launcher.LaunchGameAsync(row.GameDir, command).ConfigureAwait(true);
            if (!result.IsSuccess)
            {
                // 进程没起来：立刻把刚放掉的锁收回来，别把界面停在精简模式。
                await RollbackPreLaunchReleaseAsync().ConfigureAwait(true);
                LaunchPhase = GameLaunchPhase.Idle;
                SetError(InstancesErrorText.Compose(result.Message, "启动游戏失败", result.Retryable), result.Detail);
                return;
            }

            LaunchPhase = GameLaunchPhase.Running;
            StatusText = Text("已启动「{0}」：{1}", LocalizationKeys.InstancesStatusLaunched, row.Name, command);

            // 计时必须在排程之前开始：排程会决定是否订阅退出信号，而计时依赖同一个信号结算。
            PlayTime?.BeginSession(row.Name);
            StartReleaseSchedule();
        }
        catch (Exception ex)
        {
            await RollbackPreLaunchReleaseAsync().ConfigureAwait(true);
            LaunchPhase = GameLaunchPhase.Idle;
            OnException(ex, "启动游戏");
        }
    }

    /// <summary>
    /// 启动成功之后按 18 篇 §4 的 T3/T4/T5 排程释放（0s / +10s / +30s），并订阅游戏退出信号。
    /// 档位取自 <see cref="UiSettings"/>，关闭档不排程——用户明确关掉释放时连释放计时器都不起；
    /// 但游戏退出信号仍会订阅，因为游戏时长自动累计（F-48）要靠它结算。
    /// </summary>
    private void StartReleaseSchedule()
    {
        // 新一次启动开启新的收尾周期：上一次退出/离开页面已经把资源恢复过了。
        _releaseSettled = false;

        var tier = UiSettings?.Current.ReleaseTier ?? ResourceReleaseTier.Balanced;
        var release = ResourceRelease;

        // 退出信号服务两件事：分层释放与游戏时长自动累计（F-48）。
        // 只要其中任一项在场就必须订阅，否则「没注入释放服务」会连带让计时永不了结。
        var schedulesRelease = release is not null && tier != ResourceReleaseTier.Off;
        if (schedulesRelease || PlayTime is not null)
        {
            AttachExitSignal();
        }
        else
        {
            DetachExitSignal();
        }

        // 这里再判一次（而不是只依赖 schedulesRelease）：给编译器做流分析，也挡住两处判断日后各自演化。
        if (release is null || tier == ResourceReleaseTier.Off)
        {
            return;
        }

        // 服务实例变了（换了容器/换了替身）就重建编排，避免把旧服务的新报告写到新状态上。
        if (_releaseCoordinator is null || !ReferenceEquals(_releaseService, release))
        {
            _releaseCoordinator = new GameLaunchReleaseCoordinator(release, ReleaseScheduler);
            _releaseService = release;
        }

        _releaseCoordinator.Begin(tier);
    }

    private void AttachExitSignal()
    {
        if (ShellLauncher is not IInstanceLaunchExitSignal signal || ReferenceEquals(_exitSignal, signal))
        {
            return;
        }

        DetachExitSignal();
        _exitSignal = signal;
        signal.GameExited += OnGameExited;
    }

    private void DetachExitSignal()
    {
        if (_exitSignal is { } signal)
        {
            signal.GameExited -= OnGameExited;
        }

        _exitSignal = null;
    }

    /// <summary>游戏退出：取消后续释放层并恢复被 L2 丢掉的注册表与仓库快照（18 篇 §4/§6）。</summary>
    private void OnGameExited(string gameDir)
    {
        DetachExitSignal();

        // 先结算时长：写的是本地账本，与后面的释放恢复是否成功无关。
        PlayTime?.EndSession();
        _releaseShutdown = StopReleaseScheduleAsync();
    }

    /// <summary>停止排程并恢复数据；失败时走统一错误出口（可重试），不抛异常。</summary>
    private async Task StopReleaseScheduleAsync()
    {
        // 离开页面与游戏退出都可能触发收尾，服务层的 RestoreAsync 只该被调用一次。
        if (_releaseSettled)
        {
            return;
        }

        _releaseSettled = true;
        LaunchPhase = GameLaunchPhase.Restoring;

        if (_releaseCoordinator is not { } coordinator)
        {
            // 没有排程也可能释放过（启动前只跑了 L2 就失败/离开页面）：照样得把锁收回来。
            await RollbackPreLaunchReleaseAsync().ConfigureAwait(true);
            LaunchPhase = GameLaunchPhase.Idle;
            return;
        }

        try
        {
            var restore = await coordinator.EndAsync().ConfigureAwait(true);
            if (!restore.IsSuccess)
            {
                // 恢复失败要留住精简模式并提供出口（18 篇 §6：恢复失败必须有出口，不能只剩一句报错）。
                _releaseSettled = false;
                LaunchPhase = GameLaunchPhase.Idle;
                SetError(
                    InstancesErrorText.Compose(restore.Message, "恢复资源失败", restore.Retryable),
                    restore.Detail);
                return;
            }

            IsTrimMode = false;
            LaunchPhase = GameLaunchPhase.Idle;

            if (coordinator.LastReport is { } report && report.ReleasedItemCount > 0)
            {
                StatusText = Text("游戏已退出，已恢复数据（本次释放 {0} 项）。", LocalizationKeys.InstancesStatusRestored, report.ReleasedItemCount);
            }
        }
        catch (Exception ex)
        {
            // 异常同样留住精简模式：数据还在外面，不能假装恢复成功。
            _releaseSettled = false;
            LaunchPhase = GameLaunchPhase.Idle;
            OnException(ex, "恢复资源");
        }
    }

    /// <summary>
    /// 18 篇 §4 T0 的前置动作：启动游戏前只跑 L2（放掉注册表锁、丢掉仓库快照），让游戏侧看到干净的实例目录。
    /// </summary>
    /// <remarks>
    /// <para>成功即进入精简模式；之后的 T3/T4/T5 排程会因为「该层已执行过」自动跳过 L2。</para>
    /// <para>失败<b>不阻断启动</b>：游戏本身能跑，只是游戏侧工具可能把 <c>registry.locked</c> 当成
    /// 「CKAN 正在写实例」。关闭档（<see cref="ResourceReleaseTier.Off"/>）连锁都不动。</para>
    /// </remarks>
    private async Task ReleaseRegistryLockBeforeLaunchAsync()
    {
        if (ResourceRelease is not { } release)
        {
            return;
        }

        if ((UiSettings?.Current.ReleaseTier ?? ResourceReleaseTier.Balanced) == ResourceReleaseTier.Off)
        {
            return;
        }

        try
        {
            var result = await release.ReleaseCoreDataAsync().ConfigureAwait(true);
            if (result.IsSuccess)
            {
                IsTrimMode = true;
                return;
            }

            StatusText = Text("注册表锁未能释放（{0}），仍继续启动。", LocalizationKeys.InstancesStatusLockReleaseFailedDetail, result.Message);
        }
        catch (Exception ex)
        {
            // 服务没实现该入口（接口默认实现直接返回失败）或抛异常：都只是拿不到精简模式。
            _errors.Report(ex, "释放注册表锁");
            StatusText = Text("注册表锁未能释放，仍继续启动。", LocalizationKeys.InstancesStatusLockReleaseFailed);
        }
    }

    /// <summary>把启动前放掉的注册表锁立刻收回来（进程没起来、或没排程就收尾的回滚路径）。</summary>
    private async Task RollbackPreLaunchReleaseAsync()
    {
        if (!IsTrimMode)
        {
            return;
        }

        try
        {
            bool restored;
            if (_releaseCoordinator is { } coordinator)
            {
                restored = (await coordinator.EndAsync().ConfigureAwait(true)).IsSuccess;
            }
            else if (ResourceRelease is { } release)
            {
                restored = (await release.RestoreAsync().ConfigureAwait(true)).IsSuccess;
            }
            else
            {
                restored = false;
            }

            if (restored)
            {
                IsTrimMode = false;
            }
        }
        catch (Exception ex)
        {
            _errors.Report(ex, "恢复资源");
        }
    }

    /// <summary>
    /// 命令：手动恢复数据（精简模式下可用）。游戏还在运行也能点——恢复只是把注册表与仓库快照读回来，
    /// 让界面回到常态内存；代价是后续排程随之取消（游戏退出时不再重复恢复）。
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanRestoreTrimMode))]
    private async Task RestoreTrimModeAsync()
    {
        ClearError();
        StatusText = Text("正在恢复数据…", LocalizationKeys.InstancesBusyRestoring);

        // 复用收尾链路（取消后续排程 + RestoreAsync + 统一错误出口），避免出现第二条恢复路径。
        var task = StopReleaseScheduleAsync();
        _releaseShutdown = task;
        await task.ConfigureAwait(true);
    }

    /// <summary>命令：编辑选中实例的启动命令（F-06）。</summary>
    [RelayCommand]
    private Task EditCommandsAsync() => EditSelectedCommandsAsync(SelectedRow);

    /// <summary>
    /// 打开启动命令编辑面板（F-06）。已保存过就用保存的，否则用默认命令（<see cref="LaunchCommandDefaults"/>）。
    /// </summary>
    public async Task EditSelectedCommandsAsync(InstanceRowViewModel? row)
    {
        row ??= SelectedRow;
        if (row is null)
        {
            SetError(Text("请先选择要编辑启动命令的实例。", LocalizationKeys.InstancesErrorSelectToEditCommands), null);
            return;
        }

        if (HasCommandsPanel)
        {
            return;
        }

        ClearError();
        var resolved = await ResolveCommandsAsync(row).ConfigureAwait(true);
        if (!resolved.IsSuccess)
        {
            SetError(InstancesErrorText.Compose(resolved.Message, "读取启动命令失败", resolved.Retryable), resolved.Detail);
            return;
        }

        var defaults = CommandStore?.DefaultsFor(row.GameDir) ?? LaunchCommandDefaults.ForThisMachine(row.GameDir);
        OpenCommandsPanel(row, new LaunchCommandsPanelViewModel(
            row.Name,
            resolved.Value ?? defaults,
            defaults));
    }

    /// <summary>打开启动命令面板并接线（保存交回本页，取消即关闭）；同时关掉内联表单避免两层遮罩叠加。</summary>
    public void OpenCommandsPanel(InstanceRowViewModel row, LaunchCommandsPanelViewModel panel)
    {
        ArgumentNullException.ThrowIfNull(row);
        ArgumentNullException.ThrowIfNull(panel);

        panel.TextPrompt ??= TextPrompt;
        panel.CancelRequested = CloseCommandsPanel;
        panel.SubmitRequested = commands => SaveCommandsAsync(row, panel, commands);
        Form = null;
        CommandsPanel = panel;
    }

    /// <summary>关闭启动命令面板（取消，不落盘）。</summary>
    public void CloseCommandsPanel()
    {
        CommandsPanel = null;
        StatusText = Text("已取消编辑启动命令。", LocalizationKeys.InstancesStatusEditCommandsCancelled);
    }

    /// <summary>保存启动命令面板的编辑结果（落盘到实例的 <c>CKAN/GUIConfig.json</c>）。</summary>
    public async Task SaveCommandsAsync(
        InstanceRowViewModel row,
        LaunchCommandsPanelViewModel panel,
        IReadOnlyList<string> commands)
    {
        ArgumentNullException.ThrowIfNull(row);
        ArgumentNullException.ThrowIfNull(panel);
        ArgumentNullException.ThrowIfNull(commands);

        if (CommandStore is not { } store)
        {
            panel.ValidationError = Text("启动命令存储尚未接入，无法保存。", LocalizationKeys.InstancesErrorCommandStoreMissing);
            return;
        }

        try
        {
            var result = await store.WriteAsync(row.GameDir, commands).ConfigureAwait(true);
            if (!result.IsSuccess)
            {
                panel.ValidationError = InstancesErrorText.Compose(result.Message, "保存启动命令失败", result.Retryable);
                return;
            }

            CommandsPanel = null;
            StatusText = Text("已保存「{0}」的启动命令（{1} 条）。", LocalizationKeys.InstancesStatusCommandsSaved, row.Name, commands.Count);
        }
        catch (Exception ex)
        {
            OnException(ex, "保存启动命令");
            panel.ValidationError = Text("保存启动命令时出错，详见错误栏。", LocalizationKeys.InstancesErrorSaveCommandsFailed);
        }
    }

    /// <summary>取实例的生效启动命令：已保存 → 用它；未保存 / 空 → 用默认命令。</summary>
    public async Task<OperationResult<IReadOnlyList<string>>> ResolveCommandsAsync(InstanceRowViewModel row)
    {
        ArgumentNullException.ThrowIfNull(row);

        if (CommandStore is not { } store)
        {
            return OperationResult<IReadOnlyList<string>>.Ok(
                LaunchCommandDefaults.ForThisMachine(row.GameDir));
        }

        try
        {
            var read = await store.ReadAsync(row.GameDir).ConfigureAwait(true);
            if (!read.IsSuccess)
            {
                return OperationResult<IReadOnlyList<string>>.Fail(read.Message ?? "读取启动命令失败。", read.Detail, read.Retryable);
            }

            if (read.Value is { Count: > 0 } saved)
            {
                return OperationResult<IReadOnlyList<string>>.Ok(saved);
            }

            return OperationResult<IReadOnlyList<string>>.Ok(store.DefaultsFor(row.GameDir));
        }
        catch (Exception ex)
        {
            OnException(ex, "读取启动命令");
            return OperationResult<IReadOnlyList<string>>.Fail("读取启动命令时出错", ex.Message);
        }
    }

    /// <summary>命令：从 Steam 库导入实例（自动查找，06 篇 UI-32）。</summary>
    [RelayCommand]
    private async Task ImportFromSteamAsync()
    {
        if (IsBusy)
        {
            return;
        }

        ClearError();
        if (SteamLibraries is not { } source)
        {
            SetError(Text("Steam 库探测尚未接入：请使用「添加实例」手动选择目录。", LocalizationKeys.InstancesErrorSteamMissing), null);
            return;
        }

        IsBusy = true;
        BusyText = Text("正在扫描 Steam 库…", LocalizationKeys.InstancesBusyScanningSteam);
        try
        {
            var found = await source.DiscoverGameDirsAsync();
            if (!found.IsSuccess)
            {
                SetError(InstancesErrorText.Compose(found.Message, "扫描 Steam 库失败", found.Retryable), found.Detail);
                return;
            }

            var candidates = InstancesProjection.FilterNewPaths(found.Value, SafeSnapshot());
            if (candidates.Count == 0)
            {
                SetError(Text("未在 Steam 库中找到新的 KSP 安装目录。", LocalizationKeys.InstancesStatusSteamNoneFound), null);
                return;
            }

            var chosen = candidates[0];
            if (candidates.Count > 1)
            {
                var index = await _dialogs.SelectAsync(Text("从 Steam 库导入", LocalizationKeys.InstancesSteamDialogTitle), Text("请选择要导入的 KSP 安装目录：", LocalizationKeys.InstancesSteamDialogBody), candidates, 0);
                if (index < 0 || index >= candidates.Count)
                {
                    StatusText = Text("已取消导入。", LocalizationKeys.InstancesStatusImportCancelled);
                    return;
                }

                chosen = candidates[index];
            }

            var confirmed = await _dialogs.ConfirmAsync(
                Text("导入实例", LocalizationKeys.InstancesImportTitle),
                Text("要把「{0}」添加为游戏实例吗？不会修改该目录中的任何文件。", LocalizationKeys.InstancesImportBody, chosen),
                chosen);
            if (!confirmed)
            {
                StatusText = Text("已取消导入。", LocalizationKeys.InstancesStatusImportCancelled);
                return;
            }

            var added = await _instances.AddAsync(chosen);
            if (!added.IsSuccess)
            {
                SetError(InstancesErrorText.Compose(added.Message, "导入实例失败", added.Retryable), added.Detail);
                return;
            }

            StatusText = Text("已从 Steam 库导入实例「{0}」。", LocalizationKeys.InstancesStatusImported, added.Value?.Name ?? chosen);
        }
        catch (Exception ex)
        {
            OnException(ex, "从 Steam 库导入");
        }
        finally
        {
            IsBusy = false;
            Reload();
        }
    }

    /// <summary>命令：关闭错误条。</summary>
    [RelayCommand]
    private void ClearError()
    {
        ErrorText = null;
        ErrorDetail = null;
    }

    // ---- 自检与处置（12 篇 F-08） -----------------------------------------

    /// <summary>
    /// 实例自检与处置服务（12 篇 F-08）：锁占用探测 / 强制解锁 / 坏注册表隔离 / 版本兼容建议。
    /// 未注入时「自检」给出可读提示而不是崩溃——容器里注册 <c>IInstanceRepairService</c> 之后由 App 层赋值即可。
    /// </summary>
    public IInstanceRepairService? Repair { get; set; }

    /// <summary>最近一次自检结果；为空表示还没自检过或自检失败。</summary>
    [ObservableProperty]
    private InstanceRepairDiagnosis? _diagnosis;

    /// <summary>命中的问题清单（整体替换，视图逐条显示标题与修复建议）。</summary>
    [ObservableProperty]
    private IReadOnlyList<InstanceIssue> _repairIssues = Array.Empty<InstanceIssue>();

    /// <summary>升级建议（版本不满足时的可读指引，取各问题的修复建议拼接）；没有建议时为空。</summary>
    [ObservableProperty]
    private string? _repairAdvice;

    /// <summary>是否已有自检结果（视图据此显示「处置」区）。</summary>
    public bool HasDiagnosis => Diagnosis is not null;

    /// <summary>是否有需要处理的问题。</summary>
    public bool HasRepairIssues => RepairIssues.Count > 0;

    /// <summary>能否强制解锁：锁存在且游戏进程没在跑（服务层会再校验一次，这里只提前灰显按钮）。</summary>
    public bool CanForceUnlock => Diagnosis?.CanForceUnlock == true;

    /// <summary>能否隔离 registry.json：确实存在一个解析不了的注册表文件。</summary>
    public bool CanQuarantineRegistry => Diagnosis?.HasCorruptedRegistry == true;

    /// <summary>自检摘要（一行中文，同时写进状态栏）。</summary>
    public string DiagnosisSummary
    {
        get
        {
            if (Diagnosis is not { } diagnosis)
            {
                return Text("尚未自检。", LocalizationKeys.InstancesDiagnosisNotRun);
            }

            if (diagnosis.IsHealthy)
            {
                return Text("实例「{0}」自检通过：锁与注册表都正常。", LocalizationKeys.InstancesDiagnosisPassed, diagnosis.InstanceName);
            }

            var first = diagnosis.Issues.Count > 0 ? diagnosis.Issues[0].Title : Text("存在问题", LocalizationKeys.InstancesDiagnosisIssue);
            return Text("实例「{0}」发现 {1} 个问题：{2}。", LocalizationKeys.InstancesDiagnosisIssues, diagnosis.InstanceName, diagnosis.Issues.Count, first);
        }
    }

    /// <summary>命令：对当前选中实例做一次自检（锁占用 / 注册表可解析 / 版本兼容）。</summary>
    [RelayCommand]
    private Task DiagnoseAsync() => DiagnoseInstanceAsync(SelectedRow);

    /// <summary>对指定实例自检（headless 单测直接调用，不必绕命令）。</summary>
    public async Task DiagnoseInstanceAsync(InstanceRowViewModel? row)
    {
        if (IsBusy)
        {
            return;
        }

        var target = ResolveRepairTarget(row, Text("自检", LocalizationKeys.InstancesActionSelfCheck));
        if (target is null)
        {
            return;
        }

        ClearError();
        IsBusy = true;
        BusyText = Text("正在自检实例…", LocalizationKeys.InstancesBusySelfChecking);
        try
        {
            await ApplyDiagnosisAsync(target);
        }
        catch (Exception ex)
        {
            OnException(ex, "实例自检");
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>命令：强制解锁当前实例（仅游戏进程确实没在跑时才允许，见 12 篇 F-08）。</summary>
    [RelayCommand]
    private Task ForceUnlockAsync() => ForceUnlockInstanceAsync(SelectedRow);

    /// <summary>强制解锁指定实例的占用标记（headless 单测直接调用）。</summary>
    public async Task ForceUnlockInstanceAsync(InstanceRowViewModel? row)
    {
        if (IsBusy)
        {
            return;
        }

        var target = ResolveRepairTarget(row, Text("强制解锁", LocalizationKeys.InstancesActionForceUnlock));
        if (target is null)
        {
            return;
        }

        if (Diagnosis is not { } diagnosis
            || !string.Equals(diagnosis.InstanceName, target.InstanceName, StringComparison.Ordinal))
        {
            SetError(Text("请先对实例「{0}」点一次「自检」，确认游戏没有在运行后再强制解锁。", LocalizationKeys.InstancesErrorSelfCheckFirstUnlock, target.InstanceName), target.GameDir);
            return;
        }

        if (!diagnosis.CanForceUnlock)
        {
            SetError(
                diagnosis.IsGameRunning
                    ? Text("游戏正在运行，无法解锁实例「{0}」。请先退出游戏再重试。（不可重试）", LocalizationKeys.InstancesErrorUnlockRunning, diagnosis.InstanceName)
                    : Text("实例「{0}」当前没有被锁定，无需解锁。（不可重试）", LocalizationKeys.InstancesErrorUnlockNotLocked, diagnosis.InstanceName),
                diagnosis.GameDir);
            return;
        }

        try
        {
            var confirmed = await _dialogs.ConfirmAsync(
                Text("强制解锁实例", LocalizationKeys.InstancesUnlockTitle),
                Text("会删掉实例「{0}」的占用标记（CKAN/registry.locked），不会动注册表、模组或存档。要继续吗？", LocalizationKeys.InstancesUnlockBody, diagnosis.InstanceName),
                diagnosis.GameDir);
            if (!confirmed)
            {
                StatusText = Text("已取消解锁。", LocalizationKeys.InstancesStatusUnlockCancelled);
                return;
            }

            IsBusy = true;
            BusyText = Text("正在强制解锁…", LocalizationKeys.InstancesBusyUnlocking);
            ClearError();

            var result = await Repair!.ForceUnlockAsync(target, "实例页手工强制解锁");
            if (!result.IsSuccess || result.Value is null)
            {
                SetError(InstancesErrorText.Compose(result.Message, "强制解锁失败。", result.Retryable), result.Detail);
                return;
            }

            // 解锁是个「状态已变」的动作：立刻复核一次，避免详情区还挂着旧的 🔒。
            // 复核会把状态栏刷成自检摘要，所以处置结果要在复核之后再写回状态栏。
            await ApplyDiagnosisAsync(target);
            StatusText = result.Value.Message;
        }
        catch (Exception ex)
        {
            OnException(ex, "强制解锁实例");
        }
        finally
        {
            IsBusy = false;
            Reload();
        }
    }

    /// <summary>命令：把解析不了的 registry.json 改名隔离（保留文件内容，绝不删除用户数据）。</summary>
    [RelayCommand]
    private Task QuarantineRegistryAsync() => QuarantineInstanceRegistryAsync(SelectedRow);

    /// <summary>隔离指定实例的坏注册表（headless 单测直接调用）。</summary>
    public async Task QuarantineInstanceRegistryAsync(InstanceRowViewModel? row)
    {
        if (IsBusy)
        {
            return;
        }

        var target = ResolveRepairTarget(row, Text("隔离注册表", LocalizationKeys.InstancesActionIsolateRegistry));
        if (target is null)
        {
            return;
        }

        if (Diagnosis is not { } diagnosis
            || !string.Equals(diagnosis.InstanceName, target.InstanceName, StringComparison.Ordinal))
        {
            SetError(Text("请先对实例「{0}」点一次「自检」，确认注册表已损坏后再隔离。", LocalizationKeys.InstancesErrorSelfCheckFirstIsolate, target.InstanceName), target.GameDir);
            return;
        }

        if (!diagnosis.HasCorruptedRegistry)
        {
            SetError(
                Text("实例「{0}」的 registry.json 当前可以正常解析，无需隔离。（不可重试）", LocalizationKeys.InstancesErrorIsolateHealthy, diagnosis.InstanceName),
                diagnosis.RegistryPath);
            return;
        }

        try
        {
            var confirmed = await _dialogs.ConfirmAsync(
                Text("隔离损坏的注册表", LocalizationKeys.InstancesIsolateTitle),
                Text("会把解析不了的 registry.json 改名成 registry.json.corrupt-<时间戳> 留在原目录，CKAN 下次启动会重建一个空注册表（模组文件不会被删除）。要继续吗？", LocalizationKeys.InstancesIsolateBody),
                diagnosis.RegistryPath);
            if (!confirmed)
            {
                StatusText = Text("已取消隔离。", LocalizationKeys.InstancesStatusIsolateCancelled);
                return;
            }

            IsBusy = true;
            BusyText = Text("正在隔离注册表…", LocalizationKeys.InstancesBusyIsolating);
            ClearError();

            var result = await Repair!.QuarantineRegistryAsync(target);
            if (!result.IsSuccess || result.Value is null)
            {
                SetError(InstancesErrorText.Compose(result.Message, "隔离注册表失败。", result.Retryable), result.Detail);
                return;
            }

            // 隔离同样要先复核（坏注册表已改名，复核应当通过），再把隔离结果写回状态栏。
            await ApplyDiagnosisAsync(target);
            StatusText = result.Value.Message;
        }
        catch (Exception ex)
        {
            OnException(ex, "隔离注册表");
        }
        finally
        {
            IsBusy = false;
            Reload();
        }
    }

    private InstanceRepairTarget? ResolveRepairTarget(InstanceRowViewModel? row, string action)
    {
        if (row is null)
        {
            SetError(Text("请先选择一个实例，再点「{0}」。", LocalizationKeys.InstancesErrorSelectFirstThenAction, action), null);
            return null;
        }

        if (Repair is null)
        {
            SetError(Text("实例自检功能尚未接线：请在容器里注册 IInstanceRepairService 后重试。", LocalizationKeys.InstancesErrorRepairMissing), null);
            return null;
        }

        return new InstanceRepairTarget(row.Name, row.GameDir, row.GameVersion);
    }

    private async Task ApplyDiagnosisAsync(InstanceRepairTarget target)
    {
        var result = await Repair!.DiagnoseAsync(target);
        if (!result.IsSuccess || result.Value is null)
        {
            ResetDiagnosis();
            SetError(InstancesErrorText.Compose(result.Message, "实例自检失败。", result.Retryable), result.Detail);
            return;
        }

        var diagnosis = result.Value;
        Diagnosis = diagnosis;
        RepairIssues = diagnosis.Issues;
        RepairAdvice = ComposeRepairAdvice(diagnosis);
        // 自检「不健康」不是失败：状态栏给出摘要、问题清单逐条渲染，错误条留给真正失败的动作。
        StatusText = DiagnosisSummary;
    }

    private void ResetDiagnosis()
    {
        Diagnosis = null;
        RepairIssues = Array.Empty<InstanceIssue>();
        RepairAdvice = null;
    }

    private static string? ComposeRepairAdvice(InstanceRepairDiagnosis diagnosis)
    {
        var hints = new List<string>();
        foreach (var issue in diagnosis.Issues)
        {
            if (!string.IsNullOrWhiteSpace(issue.FixHint))
            {
                hints.Add(issue.FixHint!.Trim());
            }
        }

        return hints.Count == 0 ? null : string.Join(" ", hints);
    }

    // ---- 内部工具 ---------------------------------------------------------

    private InstanceRowViewModel? ResolveSelection(string? previousName)
    {
        if (!string.IsNullOrEmpty(previousName))
        {
            foreach (var row in Rows)
            {
                if (string.Equals(row.Name, previousName, StringComparison.Ordinal))
                {
                    return row;
                }
            }
        }

        return CurrentRow ?? (Rows.Count > 0 ? Rows[0] : null);
    }

    private IReadOnlyList<InstanceInfo> SafeSnapshot()
    {
        try
        {
            return _instances.Instances;
        }
        catch (Exception ex)
        {
            OnException(ex, "读取实例列表");
            return Array.Empty<InstanceInfo>();
        }
    }

    private static bool SafeProbe(IInstancePathProbe probe, string path)
    {
        try
        {
            return probe.DirectoryExists(path);
        }
        catch (Exception)
        {
            // 探测失败按「存在」处理：宁可少提示一句，也不要误报目录不存在。
            return true;
        }
    }

    private void SetError(string message, string? detail)
    {
        ErrorText = message;
        ErrorDetail = detail;
        StatusText = message;
    }

    private void OnException(Exception exception, string context)
    {
        _errors.Report(exception, context);
        var message = InstancesErrorText.FromException(exception, context);
        ErrorText = message;
        ErrorDetail = exception.ToString();
        StatusText = message;
    }

    private void OnInstancesChanged() => Reload();

    private void OnCurrentChanged(InstanceInfo? info) => Reload();

    // ---- 派生属性通知 -----------------------------------------------------

    partial void OnLaunchPhaseChanged(GameLaunchPhase value)
    {
        OnPropertyChanged(nameof(LaunchPhaseText));
        OnPropertyChanged(nameof(IsLaunchActive));
        OnPropertyChanged(nameof(IsLaunchBusy));
        OnPropertyChanged(nameof(CanRestoreTrimMode));
        RestoreTrimModeCommand.NotifyCanExecuteChanged();
    }

    partial void OnIsTrimModeChanged(bool value)
    {
        OnPropertyChanged(nameof(TrimModeText));
        OnPropertyChanged(nameof(CanRestoreTrimMode));
        RestoreTrimModeCommand.NotifyCanExecuteChanged();
    }

    partial void OnRowsChanged(IReadOnlyList<InstanceRowViewModel> value)
    {
        OnPropertyChanged(nameof(HasRows));
        OnPropertyChanged(nameof(ShowEmptyState));
        OnPropertyChanged(nameof(CountText));
        OnPropertyChanged(nameof(CurrentRow));
        OnPropertyChanged(nameof(CurrentSummary));
    }

    partial void OnSelectedRowChanged(InstanceRowViewModel? value)
    {
        OnPropertyChanged(nameof(HasSelection));
        OnPropertyChanged(nameof(SelectionTitle));
        OnPropertyChanged(nameof(SelectionPath));
        OnPropertyChanged(nameof(SelectionVersionText));
        OnPropertyChanged(nameof(SelectionModuleText));
        OnPropertyChanged(nameof(SelectionLockText));
        OnPropertyChanged(nameof(SelectionRoleText));
        OnPropertyChanged(nameof(SelectionPathStatus));
        OnPropertyChanged(nameof(CanSwitchSelected));
        OnPropertyChanged(nameof(CanForgetSelected));
        OnPropertyChanged(nameof(CanOpenSelected));
        OnPropertyChanged(nameof(CanEditCommands));

        // 换到别的实例后旧的诊断结论作废（同实例的刷新保留，见 Reload 会重建行快照）。
        if (Diagnosis is not null && !string.Equals(Diagnosis.InstanceName, value?.Name, StringComparison.Ordinal))
        {
            ResetDiagnosis();
        }
    }

    partial void OnDiagnosisChanged(InstanceRepairDiagnosis? value)
    {
        OnPropertyChanged(nameof(HasDiagnosis));
        OnPropertyChanged(nameof(CanForceUnlock));
        OnPropertyChanged(nameof(CanQuarantineRegistry));
        OnPropertyChanged(nameof(DiagnosisSummary));
    }

    partial void OnRepairIssuesChanged(IReadOnlyList<InstanceIssue> value)
    {
        OnPropertyChanged(nameof(HasRepairIssues));
        OnPropertyChanged(nameof(DiagnosisSummary));
    }

    partial void OnErrorTextChanged(string? value) => OnPropertyChanged(nameof(HasError));

    partial void OnErrorDetailChanged(string? value) => OnPropertyChanged(nameof(HasErrorDetail));

    partial void OnIsBusyChanged(bool value) => OnPropertyChanged(nameof(IsIdle));

    partial void OnFormChanged(InstanceFormViewModel? value) => OnPropertyChanged(nameof(HasForm));

    partial void OnCommandsPanelChanged(LaunchCommandsPanelViewModel? value)
    {
        OnPropertyChanged(nameof(HasCommandsPanel));
        OnPropertyChanged(nameof(CanEditCommands));
    }

    // ---- 无容器场景的空实现（设计器 / 单测 new 出页面时不连服务） --------

    private sealed class EmptyInstanceService : IInstanceService
    {
        public IReadOnlyList<InstanceInfo> Instances { get; } = Array.Empty<InstanceInfo>();

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

        public Task<OperationResult<IReadOnlyList<InstanceInfo>>> RefreshAsync(CancellationToken ct = default) =>
            Task.FromResult(OperationResult<IReadOnlyList<InstanceInfo>>.Ok(Array.Empty<InstanceInfo>()));

        public Task<OperationResult<InstanceInfo>> SwitchAsync(string instanceName, CancellationToken ct = default) =>
            Task.FromResult(OperationResult<InstanceInfo>.Fail("尚未连接实例服务。"));

        public Task<OperationResult<InstanceInfo>> AddAsync(string path, string? name = null, CancellationToken ct = default) =>
            Task.FromResult(OperationResult<InstanceInfo>.Fail("尚未连接实例服务。"));

        public Task<OperationResult<InstanceInfo>> CloneAsync(string sourceName, string newName, CancellationToken ct = default) =>
            Task.FromResult(OperationResult<InstanceInfo>.Fail("尚未连接实例服务。"));

        public Task<OperationResult<bool>> RemoveAsync(string instanceName, CancellationToken ct = default) =>
            Task.FromResult(OperationResult<bool>.Fail("尚未连接实例服务。"));
        public Task<OperationResult<InstanceInfo>> RenameAsync(string instanceName, string newName, CancellationToken ct = default) =>
            Task.FromResult(OperationResult<InstanceInfo>.Fail("尚未连接实例服务。"));
    }

    private sealed class EmptyDialogService : IDialogService
    {
        public Task<bool> ConfirmAsync(string title, string message, string? detail = null) =>
            Task.FromResult(false);

        public Task<int> SelectAsync(string title, string message, IReadOnlyList<string> options, int defaultIndex = 0) =>
            Task.FromResult(-1);

        public Task ShowMessageAsync(string title, string message) => Task.CompletedTask;

        public Task ShowErrorAsync(string title, string message, string? detail = null) => Task.CompletedTask;

        public Task<OperationResult<bool>> RunWithProgressAsync(
            string title,
            Func<IProgress<double>, CancellationToken, Task> work) =>
            Task.FromResult(OperationResult<bool>.Fail("尚未连接对话框服务。"));
    }

    private sealed class EmptyErrorReporter : IErrorReporter
    {
        public event Action<ErrorEntry>? Reported
        {
            add { }
            remove { }
        }

        public IReadOnlyList<ErrorEntry> Recent { get; } = Array.Empty<ErrorEntry>();

        public void Report(Exception exception, string context, string? userMessage = null)
        {
        }

        public void Report(string context, string userMessage, string? detail = null)
        {
        }
    }
}
