using CkanUI.Localization;
using CkanUI.Services.Abstractions;
using CkanUI.Services.Abstractions.Dtos;
using CkanUI.ViewModels.Pages.Saves;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace CkanUI.ViewModels.Pages;

/// <summary>
/// 存档工作台页（13 篇 §7 / 03 篇 §14 / 06 篇 E1）：存档列表 + 选中项的诊断列表 + 校验与格式化。
/// </summary>
/// <remarks>
/// <para>分层约定：本类只做「取快照 → 投影成行 → 整体替换」与命令编排；解析、诊断、格式化全部在
/// <see cref="ISaveFileService"/> 里，VM 不碰文件系统，因此整页可 headless 测试。</para>
/// <para>不静默破坏数据（13 篇 L8/L9/L10）：默认命令写 <c>&lt;name&gt;.formatted.sfs</c>，原文件不动；
/// 就地重写必须先弹确认、由服务层写前快照再原子替换。</para>
/// <para>异常收口（05 篇）：服务、对话框、外壳动作全部裹 try/catch，异常转成错误条 + <see cref="IErrorReporter"/>。</para>
/// </remarks>
public sealed partial class SavesPageViewModel : PageViewModel
{
    private readonly IInstanceService _instances;
    private readonly ISaveFileService _saves;
    private readonly IErrorReporter _errors;
    private readonly IDialogService _dialogs;
    private bool _subscribed;

    /// <summary>校验缓存条数上限（存档数量级很小，够覆盖当前页可见范围）。</summary>
    private const int ReportCacheCapacity = 16;

    /// <summary>默认构造：仅供设计器与无容器场景使用，不连任何服务。</summary>
    public SavesPageViewModel()
        : this(new EmptyInstanceService(), new EmptySaveFileService(), new EmptyErrorReporter(), new EmptyDialogService())
    {
    }

    /// <summary>依赖注入构造：参数全部是 Abstractions 里已注册的接口。</summary>
    public SavesPageViewModel(
        IInstanceService instances,
        ISaveFileService saves,
        IErrorReporter errors,
        IDialogService dialogs)
    {
        _instances = instances ?? throw new ArgumentNullException(nameof(instances));
        _saves = saves ?? throw new ArgumentNullException(nameof(saves));
        _errors = errors ?? throw new ArgumentNullException(nameof(errors));
        _dialogs = dialogs ?? throw new ArgumentNullException(nameof(dialogs));
    }

    /// <inheritdoc />
    public override string Title => Text("存档", LocalizationKeys.NavRailSaves);

    /// <summary>外壳动作（在文件夹中显示）；未注入时按钮给出可读提示而不是崩溃。</summary>
    public ISavesShellLauncher? ShellLauncher { get; set; }

    // ---- 可观察状态 -------------------------------------------------------

    /// <summary>当前可见行（整体替换，不做增量更新；04 篇 §2.1）。</summary>
    [ObservableProperty]
    private IReadOnlyList<SaveRowViewModel> _rows = Array.Empty<SaveRowViewModel>();

    /// <summary>扫描到的全部行（含自带存档）；<see cref="Rows"/> 是它按「显示自带存档」开关投影出的可见集合。</summary>
    private IReadOnlyList<SaveRowViewModel> _allRows = Array.Empty<SaveRowViewModel>();

    /// <summary>当前选中存档（诊断列表与命令的作用对象）。</summary>
    [ObservableProperty]
    private SaveRowViewModel? _selectedRow;

    /// <summary>选中存档的诊断列表（校验后填充）。</summary>
    [ObservableProperty]
    private IReadOnlyList<SaveDiagnosticRowViewModel> _diagnostics = Array.Empty<SaveDiagnosticRowViewModel>();

    /// <summary>状态栏文本。</summary>
    [ObservableProperty]
    private string _statusText = "就绪";

    /// <summary>错误条主文案（03 篇 §8 D-03）；为空表示无错误。</summary>
    [ObservableProperty]
    private string? _errorText;

    /// <summary>错误条技术细节（可展开）。</summary>
    [ObservableProperty]
    private string? _errorDetail;

    /// <summary>是否有异步操作进行中（视图铺遮罩并禁用工具条）。</summary>
    [ObservableProperty]
    private bool _isBusy;

    /// <summary>遮罩文案。</summary>
    [ObservableProperty]
    private string _busyText = "正在读取存档…";

    /// <summary>当前选中项是否已经校验过（区分「尚未校验」与「校验通过」）。</summary>
    [ObservableProperty]
    private bool _isValidated;

    /// <summary>上一次格式化的结果文案。</summary>
    [ObservableProperty]
    private string? _lastOutcomeText;

    /// <summary>校验摘要（节点 / 键值 / 行 / 深度）。</summary>
    [ObservableProperty]
    private string _selectionStatsText = "—";

    /// <summary>是否把 KSP 自带的训练 / 场景存档一起列出来（工具条勾选项；默认收起）。</summary>
    [ObservableProperty]
    private bool _showStockSaves;

    /// <summary>上次列表对应的实例目录；同一实例再次进入时不清屏、不闪忙碌遮罩。</summary>
    private string? _loadedGameDir;

    /// <summary>校验结果缓存：同一文件未改动时不重复读盘解析（切页签会重放一次校验）。</summary>
    private readonly Dictionary<string, (long Length, DateTimeOffset WriteTime, SaveValidationReport Report)> _reportCache =
        new(StringComparer.OrdinalIgnoreCase);

    // ---- 派生状态 ---------------------------------------------------------

    /// <summary>当前实例信息。</summary>
    public InstanceInfo? Instance => _instances.Current;

    /// <summary>是否有当前实例。</summary>
    public bool HasInstance => _instances.Current is not null;

    /// <summary>当前实例摘要（工具条左侧）。</summary>
    public string InstanceText => _instances.Current is { } instance
        ? Text("当前实例：{0}", LocalizationKeys.SavesCurrentInstance, instance.DisplayName)
        : Text("当前实例：未选择", LocalizationKeys.SavesCurrentInstanceNone);

    /// <summary>存档目录（空态按钮与状态栏展示）。</summary>
    public string SavesDirectoryText => _instances.Current is { } instance
        ? _saves.GetSavesDirectory(instance.GameDir)
        : "—";

    /// <summary>列表是否非空。</summary>
    public bool HasRows => Rows.Count > 0;

    /// <summary>是否有选中行。</summary>
    public bool HasSelection => SelectedRow is not null;

    /// <summary>是否显示「这个实例还没有存档」空态（03 篇 §14.2）。</summary>
    /// <remarks>只按「目录里一个存档都没有」判定；被过滤器收起的自带存档另有 <see cref="ShowStockOnlyState"/>。</remarks>
    public bool ShowEmptyState => HasInstance && !HasRows && !IsBusy && _allRows.Count == 0;

    /// <summary>自带存档条数（<c>saves/training</c> 与 <c>saves/scenarios</c>）。</summary>
    public int StockCount => _allRows.Count(row => row.IsStock);

    /// <summary>是否有自带存档可切换（没有就不画那个勾选项）。</summary>
    public bool HasStockSaves => StockCount > 0;

    /// <summary>勾选项文案：「自带存档（{0}）」。</summary>
    public string StockToggleText => Text("自带存档（{0}）", LocalizationKeys.SavesStockToggle, StockCount);

    /// <summary>是否「有存档但全被过滤器收起」——这时不能报"这个实例还没有存档"。</summary>
    public bool ShowStockOnlyState => HasInstance && !IsBusy && _allRows.Count > 0 && !HasRows;

    /// <summary>被收起时的空态标题。</summary>
    public string StockOnlyTitle => Text("存档都被过滤器收起了", LocalizationKeys.SavesStockOnlyTitle);

    /// <summary>被收起时的空态说明。</summary>
    public string StockOnlyHint => Text("勾上「自带存档」就能看到它们。", LocalizationKeys.SavesStockOnlyHint);

    /// <summary>被收起时的空态按钮文案。</summary>
    public string StockOnlyActionText => Text("显示自带存档", LocalizationKeys.SavesStockOnlyAction);

    /// <summary>是否显示「没有选择实例」空态。</summary>
    public bool ShowNoInstanceState => !HasInstance;

    /// <summary>列表计数文案。</summary>
    public string CountText => Text("共 {0} 个存档", LocalizationKeys.SavesCountText, Rows.Count);

    /// <summary>空态标题（文案固定，与 03 篇 §14.2 一致）。</summary>
    public string EmptyStateTitle => Text("这个实例还没有存档", LocalizationKeys.SavesEmptyTitle);

    /// <summary>空态说明。</summary>
    public string EmptyStateHint => Text("在游戏里保存一次就会出现在这里。", LocalizationKeys.SavesEmptyHint);

    /// <summary>空态按钮文案。</summary>
    public string EmptyStateActionText => Text("打开 saves/ 目录", LocalizationKeys.SavesEmptyAction);

    /// <summary>没有实例时的说明。</summary>
    public string NoInstanceStateHint => Text("存档属于某个游戏实例：请先在「实例」页选择或添加一个实例。", LocalizationKeys.SavesNoInstanceHint);

    /// <summary>详情面板标题。</summary>
    public string SelectionTitle => SelectedRow?.SlotName ?? Text("未选择存档", LocalizationKeys.SavesNoSelection);

    /// <summary>详情面板路径。</summary>
    public string SelectionPath => SelectedRow?.FilePath ?? "—";

    /// <summary>详情面板副标题（标题 / 模式 / 版本）。</summary>
    public string SelectionSummary => SelectedRow is { } row
        ? $"{row.TitleText} · {row.ModeText} · {row.VersionText} · {row.SceneText}"
        : "—";

    /// <summary>详情面板时间行。</summary>
    public string SelectionTimeText => SelectedRow is { } row
        ? $"{row.UniversalTimeText}（UT {row.UniversalTimeRaw}）"
        : "—";

    /// <summary>诊断条数。</summary>
    public int DiagnosticCount => Diagnostics.Count;

    /// <summary>错误级诊断条数。</summary>
    public int ErrorCount => Diagnostics.Count(d => d.IsError);

    /// <summary>警告级诊断条数。</summary>
    public int WarningCount => Diagnostics.Count(d => d.IsWarning);

    /// <summary>是否有诊断。</summary>
    public bool HasDiagnostics => Diagnostics.Count > 0;

    /// <summary>是否显示「校验通过、没有发现问题」（区分未校验）。</summary>
    public bool ShowDiagnosticsEmpty => IsValidated && !HasDiagnostics;

    /// <summary>校验通过时的说明文案。</summary>
    public string DiagnosticsEmptyText => Text("没有发现问题：结构完整，大括号配对，缩进与换行也合规。", LocalizationKeys.SavesDiagnosticsEmpty);

    /// <summary>诊断列表标题文案。</summary>
    public string DiagnosticsHeader => !IsValidated
        ? Text("尚未校验", LocalizationKeys.SavesNotValidated)
        : ErrorCount > 0
            ? Text("发现 {0} 个错误 / {1} 个警告", LocalizationKeys.SavesDiagnosticsErrors, ErrorCount, WarningCount)
            : WarningCount > 0
                ? Text("校验通过，{0} 个警告", LocalizationKeys.SavesDiagnosticsWarnings, WarningCount)
                : Text("校验通过，没有发现问题", LocalizationKeys.SavesDiagnosticsPassed);

    /// <summary>是否已校验且无错误（就地重写的前置条件）。</summary>
    public bool IsValidationClean => IsValidated && ErrorCount == 0;

    /// <summary>是否有错误。</summary>
    public bool HasError => !string.IsNullOrEmpty(ErrorText);

    /// <summary>是否有错误技术细节。</summary>
    public bool HasErrorDetail => !string.IsNullOrEmpty(ErrorDetail);

    /// <summary>是否空闲（工具条按钮的通用前置条件）。</summary>
    public bool IsIdle => !IsBusy;

    /// <summary>选中项是否可格式化。</summary>
    public bool CanFormatSelection => SelectedRow?.CanFormat == true;

    /// <summary>是否有上一次格式化结果。</summary>
    public bool HasLastOutcome => !string.IsNullOrEmpty(LastOutcomeText);

    // ---- 命令 -------------------------------------------------------------

    /// <summary>重新枚举存档目录。</summary>
    [RelayCommand(CanExecute = nameof(CanRefresh))]
    private Task RefreshAsync() => RefreshCoreAsync(null);

    /// <summary>命令可用性：空闲即可刷新。</summary>
    private bool CanRefresh() => !IsBusy;

    /// <summary>校验选中的存档（只读，不写盘）。</summary>
    [RelayCommand(CanExecute = nameof(CanValidate))]
    private async Task ValidateAsync()
    {
        if (SelectedRow is not { } row)
        {
            return;
        }

        // 用户显式点「校验」：必须真读一遍，不能贴缓存结果。
        await ValidateCoreAsync(row, preferCache: false).ConfigureAwait(true);
    }

    /// <summary>命令可用性：必须选中一行且不忙。</summary>
    private bool CanValidate() => !IsBusy && SelectedRow is not null;

    /// <summary>格式化到新文件 <c>&lt;name&gt;.formatted.sfs</c>（原文件不动，默认动作）。</summary>
    [RelayCommand(CanExecute = nameof(CanFormatCopy))]
    private Task FormatCopyAsync() => FormatCoreAsync(SaveFormatMode.NewFile);

    /// <summary>命令可用性：选中且不是备份文件。</summary>
    private bool CanFormatCopy() => !IsBusy && SelectedRow?.CanFormat == true;

    /// <summary>就地重写（确认 → 写前快照 → 原子替换）。</summary>
    [RelayCommand(CanExecute = nameof(CanFormatInPlace))]
    private async Task FormatInPlaceAsync()
    {
        if (SelectedRow is not { } row)
        {
            return;
        }

        bool confirmed;
        try
        {
            confirmed = await _dialogs.ConfirmAsync(
                Text("就地重写存档", LocalizationKeys.SavesRewriteTitle),
                Text("会先为「{0}」写一份写前快照，再就地重写该文件。内容语义（键、值、节点顺序）保持不变，只有缩进、分隔符与换行会被规范化。确定继续？", LocalizationKeys.SavesRewriteBody, row.SlotName),
                row.FilePath).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            OnException(ex, "确认就地重写存档");
            return;
        }

        if (!confirmed)
        {
            StatusText = Text("已取消就地重写，原文件未改动。", LocalizationKeys.SavesStatusRewriteCancelled);
            return;
        }

        await FormatCoreAsync(SaveFormatMode.Overwrite).ConfigureAwait(true);
    }

    /// <summary>命令可用性：必须已校验且没有错误级诊断（拒绝"先写后看"）。</summary>
    private bool CanFormatInPlace() => !IsBusy && SelectedRow?.CanFormat == true && IsValidationClean;

    /// <summary>在文件管理器中显示选中存档（无选中时打开 saves/ 目录）。</summary>
    [RelayCommand(CanExecute = nameof(CanOpenFolder))]
    private async Task OpenFolderAsync()
    {
        var directory = SelectedRow is { } row
            ? Path.GetDirectoryName(row.FilePath)
            : (_instances.Current is null ? null : SavesDirectoryText);

        if (string.IsNullOrWhiteSpace(directory))
        {
            SetError(Text("当前没有选择游戏实例，无法打开 saves/ 目录。", LocalizationKeys.SavesErrorNoInstanceForDir), null);
            return;
        }

        if (ShellLauncher is not { } launcher)
        {
            SetError(Text("当前环境不支持打开文件夹（宿主未注入外壳启动器）。", LocalizationKeys.SavesErrorNoLauncher), directory);
            return;
        }

        try
        {
            var result = await launcher.OpenFolderAsync(directory!).ConfigureAwait(true);
            if (!result.IsSuccess)
            {
                SetError(SavesErrorText.Compose(result.Message, Text("打开存档目录失败。", LocalizationKeys.SavesErrorOpenDir), result.Retryable), result.Detail);
                return;
            }

            StatusText = Text("已在文件管理器中打开：{0}", LocalizationKeys.SavesStatusDirOpened, directory);
        }
        catch (Exception ex)
        {
            OnException(ex, "打开存档目录");
        }
    }

    /// <summary>命令可用性：有实例或有选中行。</summary>
    private bool CanOpenFolder() => !IsBusy && (SelectedRow is not null || HasInstance);

    /// <summary>关闭错误条。</summary>
    [RelayCommand]
    private void ClearError()
    {
        ErrorText = null;
        ErrorDetail = null;
        StatusText = Text("就绪", LocalizationKeys.SavesStatusReady);
    }

    /// <summary>空态里的「显示自带存档」：直接把开关打开（等价于点工具条那个勾选项）。</summary>
    [RelayCommand]
    private void RevealStockSaves() => ShowStockSaves = true;

    // ---- 生命周期 ---------------------------------------------------------

    /// <inheritdoc />
    protected override void OnActivated()
    {
        if (!_subscribed)
        {
            _instances.CurrentChanged += OnCurrentChanged;
            _subscribed = true;
        }

        // 页面首次进入时自动拉一次列表；失败由 RefreshCoreAsync 内部收口。
        _ = RefreshCoreAsync(null);
    }

    /// <inheritdoc />
    protected override void OnDeactivated()
    {
        if (!_subscribed)
        {
            return;
        }

        _instances.CurrentChanged -= OnCurrentChanged;
        _subscribed = false;
    }

    private void OnCurrentChanged(InstanceInfo? instance)
    {
        try
        {
            OnPropertyChanged(nameof(Instance));
            OnPropertyChanged(nameof(InstanceText));
            OnPropertyChanged(nameof(SavesDirectoryText));
            OnPropertyChanged(nameof(HasInstance));
            OnPropertyChanged(nameof(ShowNoInstanceState));
            RaiseCommandStates();

            // 切实例后旧行全部失效：整体重新枚举（13 篇 §7.1）。
            _ = RefreshCoreAsync(null);
        }
        catch (Exception ex)
        {
            OnException(ex, "切换实例后刷新存档");
        }
    }

    // ---- 业务编排 ---------------------------------------------------------

    private async Task RefreshCoreAsync(string? preferPath, bool keepOutcome = false)
    {
        if (IsBusy)
        {
            return;
        }

        ClearErrorState();
        if (!keepOutcome)
        {
            // 格式化后的结果文案要留住（FormatCoreAsync 用 keepOutcome: true 重读列表）。
            LastOutcomeText = null;
            OnPropertyChanged(nameof(HasLastOutcome));
        }

        OnPropertyChanged(nameof(InstanceText));
        OnPropertyChanged(nameof(SavesDirectoryText));
        OnPropertyChanged(nameof(HasInstance));
        OnPropertyChanged(nameof(ShowNoInstanceState));
        RaiseCommandStates();

        if (_instances.Current is not { } instance)
        {
            _allRows = Array.Empty<SaveRowViewModel>();
            Rows = Array.Empty<SaveRowViewModel>();
            SelectedRow = null;
            ResetValidation();
            StatusText = Text("当前没有选择游戏实例：请先在「实例」页选择或添加一个实例。", LocalizationKeys.SavesStatusNoInstance);
            return;
        }

        // 同一个实例再次进入时列表已经有内容：不闪忙碌遮罩，否则每次切页签都要白屏一下。
        var keepVisible = _loadedGameDir is not null
            && string.Equals(_loadedGameDir, instance.GameDir, StringComparison.OrdinalIgnoreCase)
            && _allRows.Count > 0;
        if (!keepVisible)
        {
            IsBusy = true;
            BusyText = Text("正在读取存档…", LocalizationKeys.SavesBusyLoading);
        }
        try
        {
            var result = await _saves.ListSavesAsync(instance.GameDir).ConfigureAwait(true);
            if (!result.IsSuccess || result.Value is null)
            {
                _allRows = Array.Empty<SaveRowViewModel>();
                Rows = Array.Empty<SaveRowViewModel>();
                SelectedRow = null;
                ResetValidation();
                SetError(SavesErrorText.Compose(result.Message, Text("读取存档列表失败。", LocalizationKeys.SavesErrorLoad), result.Retryable), result.Detail);
                return;
            }

            var rows = result.Value.Select(SaveRowViewModel.From).ToArray();
            _allRows = rows;
            ApplyRowFilter(preferPath);
            ResetValidation();
            // 注意：这条状态句随后会被「选中即校验」的状态句覆盖，所以收起数量不放在这里，
            // 而是体现在工具条勾选项的计数（自带存档（N））与页头徽标（共 M 个存档）上。
            StatusText = rows.Length == 0
                ? Text("「{0}」的 saves/ 目录里没有存档。", LocalizationKeys.SavesStatusEmpty, instance.DisplayName)
                : Text("已读取 {0} 个存档。", LocalizationKeys.SavesStatusLoaded, rows.Length);
        }
        catch (Exception ex)
        {
            _allRows = Array.Empty<SaveRowViewModel>();
            Rows = Array.Empty<SaveRowViewModel>();
            SelectedRow = null;
            ResetValidation();
            OnException(ex, "读取存档列表");
        }
        finally
        {
            _loadedGameDir = instance.GameDir;
            IsBusy = false;
        }

        // 选中行变化时若正忙则跳过了自动校验，这里补一次（03 篇 §14.3：选中即看诊断）。
        KickValidate();
    }

    private async Task ValidateCoreAsync(SaveRowViewModel row, bool preferCache = true)
    {
        if (IsBusy)
        {
            return;
        }

        // 同一份存档（路径 + 列表里的大小与写入时间都没变）已经校验过：直接贴回上次结果，
        // 不重读整份 sfs、不解析、不闪忙碌遮罩 —— 切页签回到本页会走到这里。
        if (preferCache && TryGetCachedReport(row, out var cached))
        {
            ClearErrorState();
            ApplyReport(cached);
            StatusText = $"{row.SlotName}：{DiagnosticsHeader}";
            return;
        }

        ClearErrorState();
        IsBusy = true;
        BusyText = Text("正在校验存档…", LocalizationKeys.SavesBusyValidating);
        try
        {
            var result = await _saves.ValidateAsync(row.FilePath).ConfigureAwait(true);
            if (!result.IsSuccess || result.Value is null)
            {
                ResetValidation();
                SetError(SavesErrorText.Compose(result.Message, Text("校验存档失败。", LocalizationKeys.SavesErrorValidate), result.Retryable), result.Detail);
                return;
            }

            ApplyReport(result.Value);
            RememberReport(row, result.Value);
            StatusText = $"{row.SlotName}：{DiagnosticsHeader}";
        }
        catch (Exception ex)
        {
            ResetValidation();
            OnException(ex, "校验存档");
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task FormatCoreAsync(SaveFormatMode mode)
    {
        if (SelectedRow is not { } row || IsBusy)
        {
            return;
        }

        ClearErrorState();
        LastOutcomeText = null;
        OnPropertyChanged(nameof(HasLastOutcome));
        IsBusy = true;
        BusyText = mode == SaveFormatMode.Overwrite ? Text("正在写前快照并就地重写…", LocalizationKeys.SavesBusyRewriting) : Text("正在写出格式化副本…", LocalizationKeys.SavesBusyWritingCopy);
        try
        {
            var result = await _saves.FormatAsync(new SaveFormatRequest(row.FilePath, mode)).ConfigureAwait(true);
            if (!result.IsSuccess || result.Value is null)
            {
                SetError(SavesErrorText.Compose(result.Message, Text("格式化存档失败。", LocalizationKeys.SavesErrorFormat), result.Retryable), result.Detail);
                return;
            }

            var outcome = result.Value;
            if (outcome.Refused)
            {
                SetError(outcome.RefusalReason ?? Text("格式化被拒绝，原文件未改动。", LocalizationKeys.SavesErrorRefused), outcome.Preview);
                return;
            }

            LastOutcomeText = DescribeOutcome(outcome);
            OnPropertyChanged(nameof(HasLastOutcome));
            StatusText = LastOutcomeText;
        }
        catch (Exception ex)
        {
            OnException(ex, "格式化存档");
            return;
        }
        finally
        {
            IsBusy = false;
        }

        // 写完重新枚举（新文件要出现在列表里），并保持选中同一个文件。
        await RefreshCoreAsync(row.FilePath, keepOutcome: true).ConfigureAwait(true);
    }

    // 说明：这个方法要出文案，所以从 static 改成实例方法，才能走 Text(fallback, key)。
    private string DescribeOutcome(SaveFormatOutcome outcome)
    {
        var stats = Text(
            "重排 {0} 行、补分隔符 {1} 处、统一换行 {2} 处",
            LocalizationKeys.SavesFormatStats,
            outcome.Stats.ReindentedLines,
            outcome.Stats.NormalizedSeparators,
            outcome.Stats.NormalizedLineEndings);
        var size = $"{SaveTimeText.FileSize(outcome.BytesBefore)} → {SaveTimeText.FileSize(outcome.BytesAfter)}";

        if (!outcome.WroteFile)
        {
            return Text("格式化完成但未写盘：{0}。", LocalizationKeys.SavesStatusNotWritten, stats);
        }

        var name = Path.GetFileName(outcome.WrittenPath ?? string.Empty);
        return outcome.BackupPath is null
            ? Text("已写出 {0}（原文件未改动，{1}，{2}）。", LocalizationKeys.SavesStatusWrittenCopy, name, size, stats)
            : Text(
                "已就地重写 {0}，写前快照：{1}（{2}，{3}）。",
                LocalizationKeys.SavesStatusRewritten,
                name,
                Path.GetFileName(outcome.BackupPath),
                size,
                stats);
    }

    private void ApplyReport(SaveValidationReport report)
    {
        Diagnostics = report.Diagnostics.Select(SaveDiagnosticRowViewModel.From).ToArray();
        IsValidated = true;
        SelectionStatsText = Text("节点 {0} / 键值 {1} / 行 {2} / 最大深度 {3}", LocalizationKeys.SavesSelectionStats, report.NodeCount, report.PairCount, report.LineCount, report.MaxDepth);
    }

    private void KickValidate()
    {
        if (IsBusy || SelectedRow is not { } row)
        {
            return;
        }

        // 校验是只读操作，可以直接跟着选中走；异常在 ValidateCoreAsync 里已收口。
        _ = ValidateCoreAsync(row);
    }

    /// <summary>按「显示自带存档」开关把 <see cref="_allRows"/> 投影成 <see cref="Rows"/>。</summary>
    /// <param name="preferPath">刷新后尽量保留的选中项路径；为空时沿用首行。</param>
    private void ApplyRowFilter(string? preferPath)
    {
        var visible = ShowStockSaves
            ? _allRows
            : _allRows.Where(row => !row.IsStock).ToArray();

        Rows = visible;
        SelectedRow = Pick(visible, preferPath);
    }

    private static SaveRowViewModel? Pick(IReadOnlyList<SaveRowViewModel> rows, string? preferPath)
    {
        if (rows.Count == 0)
        {
            return null;
        }

        if (!string.IsNullOrEmpty(preferPath))
        {
            foreach (var row in rows)
            {
                if (string.Equals(row.FilePath, preferPath, StringComparison.OrdinalIgnoreCase))
                {
                    return row;
                }
            }
        }

        return rows[0];
    }

    // ---- 状态维护 ---------------------------------------------------------


    /// <summary>按「路径 + 列表里的大小与写入时间」判断上次校验结果是否仍然适用。</summary>
    /// <param name="row">存档行。</param>
    /// <param name="report">命中时的报告。</param>
    /// <returns>命中返回 true。</returns>
    /// <remarks>
    /// 依据行自带的 SizeBytes / LastWriteTime（列表快照就有），不打文件系统：
    /// 每次刷新列表都会得到新的行，文件被改写时这两个值必然变化，缓存自然失效。
    /// </remarks>
    private bool TryGetCachedReport(SaveRowViewModel row, out SaveValidationReport report)
    {
        report = null!;
        if (!_reportCache.TryGetValue(row.FilePath, out var entry))
        {
            return false;
        }

        if (entry.Length != row.SizeBytes || entry.WriteTime != row.LastWriteTime)
        {
            _reportCache.Remove(row.FilePath);
            return false;
        }

        report = entry.Report;
        return true;
    }

    /// <summary>记住本次校验结果，供下次切回同一存档时直接贴回。</summary>
    /// <param name="row">存档行。</param>
    /// <param name="report">校验报告。</param>
    private void RememberReport(SaveRowViewModel row, SaveValidationReport report)
    {
        if (_reportCache.Count >= ReportCacheCapacity)
        {
            _reportCache.Clear();
        }

        _reportCache[row.FilePath] = (row.SizeBytes, row.LastWriteTime, report);
    }

    private void ResetValidation()
    {
        Diagnostics = Array.Empty<SaveDiagnosticRowViewModel>();
        IsValidated = false;
        SelectionStatsText = "—";
    }

    private void SetError(string message, string? detail)
    {
        ErrorText = message;
        ErrorDetail = detail;
        StatusText = message;
    }

    private void ClearErrorState()
    {
        ErrorText = null;
        ErrorDetail = null;
    }

    private void OnException(Exception exception, string context)
    {
        _errors.Report(exception, context);
        var message = SavesErrorText.FromException(exception, context);
        ErrorText = message;
        ErrorDetail = exception.ToString();
        StatusText = message;
    }

    private void RaiseCommandStates()
    {
        RefreshCommand.NotifyCanExecuteChanged();
        ValidateCommand.NotifyCanExecuteChanged();
        FormatCopyCommand.NotifyCanExecuteChanged();
        FormatInPlaceCommand.NotifyCanExecuteChanged();
        OpenFolderCommand.NotifyCanExecuteChanged();
    }

    // ---- 属性变更连带通知 -------------------------------------------------

    partial void OnRowsChanged(IReadOnlyList<SaveRowViewModel> value)
    {
        OnPropertyChanged(nameof(HasRows));
        OnPropertyChanged(nameof(CountText));
        OnPropertyChanged(nameof(ShowEmptyState));
        OnPropertyChanged(nameof(ShowStockOnlyState));
        OnPropertyChanged(nameof(StockCount));
        OnPropertyChanged(nameof(HasStockSaves));
        OnPropertyChanged(nameof(StockToggleText));
    }

    partial void OnShowStockSavesChanged(bool value)
    {
        // 勾选状态一变就重投影：隐藏中的行如果正被选中，Pick 会退回可见集合的首行，
        // 否则诊断面板会停留在一个已经不在列表里的存档上。
        ApplyRowFilter(SelectedRow?.FilePath);
        OnPropertyChanged(nameof(ShowStockOnlyState));
    }

    partial void OnSelectedRowChanged(SaveRowViewModel? value)
    {
        OnPropertyChanged(nameof(HasSelection));
        OnPropertyChanged(nameof(CanFormatSelection));
        OnPropertyChanged(nameof(SelectionTitle));
        OnPropertyChanged(nameof(SelectionPath));
        OnPropertyChanged(nameof(SelectionSummary));
        OnPropertyChanged(nameof(SelectionTimeText));
        ResetValidation();
        LastOutcomeText = null;
        OnPropertyChanged(nameof(HasLastOutcome));
        RaiseCommandStates();

        // 选中即校验（诊断列表跟着选中行走）。
        KickValidate();
    }

    partial void OnDiagnosticsChanged(IReadOnlyList<SaveDiagnosticRowViewModel> value)
    {
        OnPropertyChanged(nameof(DiagnosticCount));
        OnPropertyChanged(nameof(ErrorCount));
        OnPropertyChanged(nameof(WarningCount));
        OnPropertyChanged(nameof(HasDiagnostics));
        OnPropertyChanged(nameof(ShowDiagnosticsEmpty));
        OnPropertyChanged(nameof(DiagnosticsHeader));
        OnPropertyChanged(nameof(IsValidationClean));
        RaiseCommandStates();
    }

    partial void OnIsValidatedChanged(bool value)
    {
        OnPropertyChanged(nameof(ShowDiagnosticsEmpty));
        OnPropertyChanged(nameof(DiagnosticsHeader));
        OnPropertyChanged(nameof(IsValidationClean));
        RaiseCommandStates();
    }

    partial void OnIsBusyChanged(bool value)
    {
        OnPropertyChanged(nameof(IsIdle));
        OnPropertyChanged(nameof(ShowEmptyState));
        RaiseCommandStates();
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

    private sealed class EmptySaveFileService : ISaveFileService
    {
        public string GetSavesDirectory(string gameDir) => Path.Combine(gameDir, "saves");

        public Task<OperationResult<IReadOnlyList<SaveGameInfo>>> ListSavesAsync(string gameDir, CancellationToken ct = default) =>
            Task.FromResult(OperationResult<IReadOnlyList<SaveGameInfo>>.Ok(Array.Empty<SaveGameInfo>()));

        public Task<OperationResult<SaveValidationReport>> ValidateAsync(string filePath, CancellationToken ct = default) =>
            Task.FromResult(OperationResult<SaveValidationReport>.Fail("尚未连接存档服务。"));

        public Task<OperationResult<SaveFormatOutcome>> FormatAsync(SaveFormatRequest request, CancellationToken ct = default) =>
            Task.FromResult(OperationResult<SaveFormatOutcome>.Fail("尚未连接存档服务。"));
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
