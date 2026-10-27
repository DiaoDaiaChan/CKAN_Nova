using CkanUI.Services.Abstractions;
using CkanUI.Services.Abstractions.Dtos;
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
    public override string Title => "实例";

    // ---- 宿主注入（App 层实现，headless 测试塞假实现） --------------------

    /// <summary>目录选择器；未注入时「添加实例」会给出可读提示而不是崩溃。</summary>
    public IInstanceFolderPicker? FolderPicker { get; set; }

    /// <summary>路径探测器；未注入时不判断目录是否存在（列表不灰显）。</summary>
    public IInstancePathProbe? PathProbe { get; set; }

    /// <summary>外壳启动器（打开目录 / 启动游戏）。</summary>
    public IInstanceShellLauncher? ShellLauncher { get; set; }

    /// <summary>Steam 库探测来源。</summary>
    public ISteamLibrarySource? SteamLibraries { get; set; }

    /// <summary>
    /// 启动命令的持久化（实例级 <c>CKAN/GUIConfig.json</c>，与老 GUI 共用，见 <see cref="LaunchCommandFile"/>）；
    /// 未注入时按默认命令启动、不提供保存。
    /// </summary>
    public IInstanceCommandStore? CommandStore { get; set; }

    /// <summary>单行文本输入通道（F-06 增 / 改一条命令）；未注入时给出可读提示而不是崩溃。</summary>
    public IInstanceTextPrompt? TextPrompt { get; set; }

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

    // ---- 派生状态 ---------------------------------------------------------

    /// <summary>列表是否非空。</summary>
    public bool HasRows => Rows.Count > 0;

    /// <summary>是否显示空状态（03 篇 §10：还没有游戏实例 + 主按钮「添加实例」）。</summary>
    public bool ShowEmptyState => !HasRows;

    /// <summary>列表计数文案。</summary>
    public string CountText => $"共 {Rows.Count} 个实例";

    /// <summary>是否有错误。</summary>
    public bool HasError => !string.IsNullOrEmpty(ErrorText);

    /// <summary>是否有错误技术细节。</summary>
    public bool HasErrorDetail => !string.IsNullOrEmpty(ErrorDetail);

    /// <summary>是否有表单打开。</summary>
    public bool HasForm => Form is not null;

    /// <summary>是否空闲（用于禁用工具条按钮，避免并发操作）。</summary>
    public bool IsIdle => !IsBusy;

    /// <summary>空状态标题。</summary>
    public string EmptyStateTitle => "还没有游戏实例";

    /// <summary>空状态说明。</summary>
    public string EmptyStateHint =>
        "添加一个 KSP 安装目录即可开始：CKAN 会读取其中的版本号与已装模组数量，不会修改任何游戏文件。";

    /// <summary>空状态主按钮文案。</summary>
    public string EmptyStateActionText => "添加实例";

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
    public string CurrentSummary => CurrentRow is { } row ? $"当前实例：{row.DisplayName}" : "当前实例：未选择";

    /// <summary>是否有选中行。</summary>
    public bool HasSelection => SelectedRow is not null;

    /// <summary>详情面板标题。</summary>
    public string SelectionTitle => SelectedRow?.DisplayName ?? "未选择实例";

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
                parts.Add("当前实例");
            }

            if (row.IsDefault)
            {
                parts.Add("默认实例");
            }

            return parts.Count == 0 ? "普通实例" : string.Join(" · ", parts);
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
        "按该实例的启动命令启动游戏；有多条命令时会先让你选一条。";

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
        BusyText = "正在扫描实例…";
        try
        {
            var result = await _instances.RefreshAsync();
            if (!result.IsSuccess)
            {
                SetError(InstancesErrorText.Compose(result.Message, "刷新实例列表失败", result.Retryable), result.Detail);
            }
            else
            {
                StatusText = "实例列表已刷新。";
            }
        }
        catch (Exception ex)
        {
            OnException(ex, "刷新实例列表");
        }
        finally
        {
            IsBusy = false;
            Reload();
        }
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
            SetError("请先选择一个实例。", null);
            return;
        }

        if (IsBusy)
        {
            return;
        }

        ClearError();
        if (row.IsCurrent)
        {
            StatusText = $"「{row.Name}」已经是当前实例。";
            return;
        }

        if (row.IsLocked)
        {
            // 锁定 = 有别的进程持有该实例的注册表锁：重试没有意义，必须让用户先关掉占用方。
            SetError(
                $"实例「{row.Name}」被其他进程锁定（不可重试）：请先关闭占用该目录的程序，再点「刷新」后重试。",
                row.GameDir);
            return;
        }

        IsBusy = true;
        BusyText = "正在切换实例…";
        try
        {
            var result = await _instances.SwitchAsync(row.Name);
            if (result.IsSuccess)
            {
                StatusText = $"已切换到「{result.Value?.Name ?? row.Name}」。";
            }
            else
            {
                SetError(InstancesErrorText.Compose(result.Message, "切换实例失败", result.Retryable), result.Detail);
                StatusText = "切换失败，仍停留在原实例。";
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
            SetError("当前环境没有可用的目录选择器，无法添加实例。", null);
            return;
        }

        string? picked;
        try
        {
            picked = await picker.PickFolderAsync("选择 KSP 安装目录", CurrentRow?.GameDir);
        }
        catch (Exception ex)
        {
            OnException(ex, "选择实例目录");
            return;
        }

        if (string.IsNullOrWhiteSpace(picked))
        {
            StatusText = "已取消选择目录。";
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
            SetError($"该目录已经是实例列表中的实例：{path}", null);
            return;
        }

        if (PathProbe is { } probe && !SafeProbe(probe, path))
        {
            SetError($"目录不存在：{path}（不可重试）", null);
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
            SetError("请先选择要克隆的实例。", null);
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
            SetError("请先选择要重命名的实例。", null);
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
        StatusText = "已取消。";
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
        BusyText = "正在添加实例…";
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
            StatusText = $"已添加实例「{result.Value?.Name ?? name}」。";
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
                    "目标目录已存在",
                    $"克隆目标目录「{destination}」已经存在，继续会把源实例的文件覆盖进去。是否继续？",
                    destination);
            }
            catch (Exception ex)
            {
                OnException(ex, "确认克隆目标目录");
                return;
            }

            if (!proceed)
            {
                StatusText = "已取消克隆：目标目录已存在。";
                return;
            }
        }

        IsBusy = true;
        BusyText = "正在克隆实例（可能需要几分钟）…";
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
            StatusText = $"已克隆为「{result.Value?.Name ?? newName}」。";
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
                "重命名实例",
                $"确定把实例「{sourceName}」重命名为「{newName}」吗？只会修改实例记录，不会改名游戏目录。",
                form.SourcePath);
        }
        catch (Exception ex)
        {
            OnException(ex, "确认重命名实例");
            return;
        }

        if (!confirmed)
        {
            StatusText = "已取消重命名。";
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
            SetError("请先选择要遗忘的实例。", null);
            return;
        }

        if (IsBusy)
        {
            return;
        }

        ClearError();
        if (row.IsCurrent)
        {
            SetError("不能遗忘当前正在使用的实例：请先切换到其他实例。", row.GameDir);
            return;
        }

        bool confirmed;
        try
        {
            confirmed = await _dialogs.ConfirmAsync(
                "遗忘实例",
                $"确定要遗忘实例「{row.Name}」吗？只会从实例列表中移除记录，不会删除游戏目录里的任何文件。",
                row.GameDir);
        }
        catch (Exception ex)
        {
            OnException(ex, "确认遗忘实例");
            return;
        }

        if (!confirmed)
        {
            StatusText = "已取消遗忘。";
            return;
        }

        IsBusy = true;
        BusyText = "正在遗忘实例…";
        try
        {
            var result = await _instances.RemoveAsync(row.Name);
            if (!result.IsSuccess)
            {
                SetError(InstancesErrorText.Compose(result.Message, "遗忘实例失败", result.Retryable), result.Detail);
                return;
            }

            StatusText = $"已遗忘实例「{row.Name}」，游戏目录未做任何改动。";
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
            SetError("请先选择一个实例。", null);
            return;
        }

        ClearError();
        if (ShellLauncher is not { } launcher)
        {
            SetError("当前环境没有可用的外壳启动器，无法打开目录。", null);
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

            StatusText = $"已在文件管理器中打开：{row.GameDir}";
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
            SetError("请先选择一个实例。", null);
            return;
        }

        ClearError();
        if (row.IsLocked)
        {
            SetError($"实例「{row.Name}」被其他进程占用（可能游戏已在运行），不能重复启动。", row.GameDir);
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
            SetError($"实例「{row.Name}」没有可用的启动命令，请先编辑启动命令。", row.GameDir);
            return;
        }

        var command = available[0];
        if (available.Count > 1)
        {
            var index = await _dialogs
                .SelectAsync($"启动「{row.Name}」", "该实例有多条启动命令，请选择要使用的一条：", available, 0)
                .ConfigureAwait(true);
            if (index < 0 || index >= available.Count)
            {
                StatusText = "已取消启动。";
                return;
            }

            command = available[index];
        }

        if (ShellLauncher is not { } launcher)
        {
            // TODO(18 篇 / E6)：启动流程需要先释放注册表锁并进入精简模式，等 E6 就绪后在此接入。
            StatusText = "启动游戏尚未接入（TODO：18 篇启动流程 / E6 资源释放）；可先用「打开目录」自行启动。";
            return;
        }

        try
        {
            var result = await launcher.LaunchGameAsync(row.GameDir, command).ConfigureAwait(true);
            if (!result.IsSuccess)
            {
                SetError(InstancesErrorText.Compose(result.Message, "启动游戏失败", result.Retryable), result.Detail);
                return;
            }

            StatusText = $"已启动「{row.Name}」：{command}";
        }
        catch (Exception ex)
        {
            OnException(ex, "启动游戏");
        }
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
            SetError("请先选择要编辑启动命令的实例。", null);
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
        StatusText = "已取消编辑启动命令。";
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
            panel.ValidationError = "启动命令存储尚未接入，无法保存。";
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
            StatusText = $"已保存「{row.Name}」的启动命令（{commands.Count} 条）。";
        }
        catch (Exception ex)
        {
            OnException(ex, "保存启动命令");
            panel.ValidationError = "保存启动命令时出错，详见错误栏。";
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
            SetError("Steam 库探测尚未接入：请使用「添加实例」手动选择目录。", null);
            return;
        }

        IsBusy = true;
        BusyText = "正在扫描 Steam 库…";
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
                SetError("未在 Steam 库中找到新的 KSP 安装目录。", null);
                return;
            }

            var chosen = candidates[0];
            if (candidates.Count > 1)
            {
                var index = await _dialogs.SelectAsync("从 Steam 库导入", "请选择要导入的 KSP 安装目录：", candidates, 0);
                if (index < 0 || index >= candidates.Count)
                {
                    StatusText = "已取消导入。";
                    return;
                }

                chosen = candidates[index];
            }

            var confirmed = await _dialogs.ConfirmAsync(
                "导入实例",
                $"要把「{chosen}」添加为游戏实例吗？不会修改该目录中的任何文件。",
                chosen);
            if (!confirmed)
            {
                StatusText = "已取消导入。";
                return;
            }

            var added = await _instances.AddAsync(chosen);
            if (!added.IsSuccess)
            {
                SetError(InstancesErrorText.Compose(added.Message, "导入实例失败", added.Retryable), added.Detail);
                return;
            }

            StatusText = $"已从 Steam 库导入实例「{added.Value?.Name ?? chosen}」。";
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
