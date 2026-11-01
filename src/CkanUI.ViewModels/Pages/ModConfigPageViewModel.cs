using System.ComponentModel;
using System.Windows.Input;
using CkanUI.Localization;
using CkanUI.Services.Abstractions;
using CkanUI.Services.Abstractions.ModConfig;
using CkanUI.ViewModels.Pages.ModConfig;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace CkanUI.ViewModels.Pages;

/// <summary>
/// Mod 配置页（M9 / UI-CFG-01 骨架 + CFG-04 编辑与写盘入口，15 篇 §5、§6）。
/// 左侧是"有适配器的 mod"卡片列表，右侧是选中 mod 的字段行（可编辑部分就地改），底部是改动条与写盘入口。
/// </summary>
/// <remarks>
/// 为什么这一页不碰文件：读盘合成模型在 <see cref="IModConfigService"/>，写盘唯一入口是 IAdapterWriter（CFG-05），
/// 页面只负责把草稿收集成 <see cref="ModConfigFieldChange"/> 并按文件投递，这样"写坏配置"的风险被限制在
/// 服务层的校验/快照/自校验里（工程约定 §7）。.bak 回滚另走 <see cref="IModConfigBackupRestorer"/>。
/// 两个确认位（游戏运行中、高风险字段）都用注入的 <see cref="IDialogService"/>，页面 VM 自己 new 对话框。
/// 译文统一走 Text(fallback, key) 文案层；构造注入的翻译委托作为「外部译文优先」的兼容入口保留，本层不引用本地化实现（铁律 3）。
/// </remarks>
public sealed partial class ModConfigPageViewModel : PageViewModel, IUnsavedChangesAware
{
    private readonly IModConfigService _config;
    private readonly IAdapterCatalog? _catalog;
    private readonly Func<string, string>? _translate;
    private readonly ModConfigFieldRendererRegistry _renderers;
    private readonly IAdapterWriter? _writer;
    private readonly IDialogService? _dialogs;
    private readonly IModConfigBackupRestorer _restorer;

    /// <summary>待保存的草稿（key = 文件路径 + 分隔符 + 键名；跨 mod 保留，切卡片不丢）。</summary>
    private readonly Dictionary<string, PendingEdit> _pending = new(StringComparer.Ordinal);

    /// <summary>上一次写盘产生的快照路径（"恢复上次快照"动作的原料）。</summary>
    private IReadOnlyList<string> _backups = [];

    private ModConfigInstanceModel? _model;

    /// <summary>adapter 导入端口（ADP-04，可空：宿主没注册就没有导入入口）。</summary>
    private IAdapterInstaller? _adapterInstaller;

    /// <summary>adapter 包选择器（可空：视图没接选择器就不显示导入按钮）。</summary>
    private Func<Task<string?>>? _adapterPicker;

    /// <summary>无参构造：供设计时预览与视图定位器反射使用（依赖是空实现）。</summary>
    public ModConfigPageViewModel()
        : this(new EmptyModConfigService())
    {
    }

    /// <summary>带服务注入的构造函数。</summary>
    /// <param name="config">配置读取服务（合成页面模型）。</param>
    /// <param name="catalog">适配器目录；为 null 时页面不展示适配器清单。</param>
    /// <param name="translate">译文委托（键为中文原文，缺省时原样返回）。</param>
    /// <param name="renderers">字段渲染器注册表；缺省用内置九种。</param>
    /// <param name="writer">写盘入口（IAdapterWriter）；为 null 时页面只读（保存会给出可读原因）。</param>
    /// <param name="dialogs">确认对话框服务；为 null 时任何需要确认的写盘一律取消。</param>
    /// <param name="restorer">.bak 快照恢复器；缺省用内置实现。</param>
    public ModConfigPageViewModel(
        IModConfigService config,
        IAdapterCatalog? catalog = null,
        Func<string, string>? translate = null,
        ModConfigFieldRendererRegistry? renderers = null,
        IAdapterWriter? writer = null,
        IDialogService? dialogs = null,
        IModConfigBackupRestorer? restorer = null)
    {
        _config = config ?? throw new ArgumentNullException(nameof(config));
        _catalog = catalog;
        _translate = translate;
        _renderers = renderers ?? new ModConfigFieldRendererRegistry();
        _writer = writer;
        _dialogs = dialogs;
        _restorer = restorer ?? new ModConfigBackupRestorer();
        StatusText = T("选择左侧的 mod 查看可配置项；没有适配器的 mod 只提供原始只读视图。", LocalizationKeys.ModConfigStatusHint);
    }

    /// <inheritdoc />
    public override string Title => T("Mod 配置", LocalizationKeys.ModConfigPageTitle);

    // ---- 状态 --------------------------------------------------------------

    /// <summary>有适配器的 mod 卡片。</summary>
    [ObservableProperty]
    private IReadOnlyList<ModConfigModCardViewModel> _mods = [];

    /// <summary>当前选中的卡片。</summary>
    [ObservableProperty]
    private ModConfigModCardViewModel? _selectedMod;

    /// <summary>选中 mod 的字段行（已按搜索/仅看已改过滤，并按分组聚合）。</summary>
    [ObservableProperty]
    private IReadOnlyList<ModConfigFieldGroupViewModel> _fieldGroups = [];

    /// <summary>当前实例加载到的适配器清单。</summary>
    [ObservableProperty]
    private IReadOnlyList<ModConfigAdapterSummaryViewModel> _adapters = [];

    /// <summary>搜索框内容（按键名/显示名/注释三路匹配）。</summary>
    [ObservableProperty]
    private string _searchText = string.Empty;

    /// <summary>只看已改项。</summary>
    [ObservableProperty]
    private bool _onlyModified;

    /// <summary>是否正在读盘。</summary>
    [ObservableProperty]
    private bool _isBusy;

    /// <summary>是否正在写盘/回滚（写盘期间禁用保存与回滚按钮）。</summary>
    [ObservableProperty]
    private bool _isWriting;

    /// <summary>忙碌时的提示文案。</summary>
    [ObservableProperty]
    private string _busyText = string.Empty;

    /// <summary>状态栏文案。</summary>
    [ObservableProperty]
    private string _statusText = string.Empty;

    /// <summary>错误文案（失败时同时写进状态栏）。</summary>
    [ObservableProperty]
    private string? _errorText;

    /// <summary>当前实例是否正在跑 KSP（运行时写盘要二次确认）。</summary>
    [ObservableProperty]
    private bool _isGameRunning;

    /// <summary>实例摘要（"实例：默认｜游戏目录：…"）。</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasInstanceSummary))]
    private string _instanceSummaryText = string.Empty;

    /// <summary>是否有实例摘要（没有实例时页头不该留一个空胶囊）。</summary>
    public bool HasInstanceSummary => InstanceSummaryText.Length > 0;

    /// <summary>最近一次保存/回滚的逐文件失败说明（每项一行）。</summary>
    [ObservableProperty]
    private IReadOnlyList<string> _saveFailures = [];

    // ---- 派生文案 ----------------------------------------------------------

    /// <summary>是否有卡片。</summary>
    public bool HasMods => Mods.Count > 0;

    /// <summary>是否显示空状态。</summary>
    public bool ShowEmptyState => Mods.Count == 0 && !IsBusy;

    /// <summary>是否选中了 mod。</summary>
    public bool HasSelection => SelectedMod is not null;

    /// <summary>是否没有选中 mod（视图直接绑定它，避免在 XAML 里做布尔取反）。</summary>
    public bool IsSelectionEmpty => SelectedMod is null;

    /// <summary>是否有错误。</summary>
    public bool HasError => !string.IsNullOrEmpty(ErrorText);

    /// <summary>卡片数量文案。</summary>
    public string ModCountText => Mods.Count == 0 ?
            T("暂无已适配的 mod", LocalizationKeys.ModConfigModCountEmpty)
            : T("共 {0} 个已适配 mod", LocalizationKeys.ModConfigModCount, Mods.Count);

    /// <summary>无适配器提示（底部"⊘ N 个 mod 无适配器"）。</summary>
    public string AdapterlessText => _model is { AdapterlessModCount: > 0 } model
        ? "⊘ " + model.AdapterlessModCount + " 个 mod 无适配器"
        : string.Empty;

    /// <summary>是否显示无适配器提示。</summary>
    public bool HasAdapterless => AdapterlessText.Length > 0;

    /// <summary>KSP 运行中的顶部警示文案。</summary>
    public string GameRunningWarningText => IsGameRunning
        ? T("KSP 正在运行，写入需重启生效。", LocalizationKeys.ModConfigKspRunningHint)
        : string.Empty;

    /// <summary>是否显示运行中警示。</summary>
    public bool ShowGameRunningWarning => IsGameRunning;

    /// <summary>右侧标题："KSP Community Fixes v1.31.0"。</summary>
    public string HeaderTitleText => SelectedMod is { } card ? card.HeaderText : T("未选择 mod", LocalizationKeys.ModConfigNoModSelected);

    /// <summary>右侧副标题（状态 + 支持区间）。</summary>
    public string HeaderStateText => SelectedMod is { } card
        ? string.Join("｜", new[] { card.StateText, card.SupportedRangeText }.Where(part => part.Length > 0))
        : string.Empty;

    /// <summary>搜索框占位文案（含字段总数）。</summary>
    public string SearchPlaceholder =>
        T("搜索 {0} 项…", LocalizationKeys.ModConfigSearchPlaceholder, FieldTotal);

    /// <summary>选中 mod 的字段总数。</summary>
    public int FieldTotal => SelectedMod?.Snapshot.FieldCount ?? 0;

    /// <summary>当前筛选后的字段条数。</summary>
    public int VisibleFieldCount => FieldGroups.Sum(group => group.Rows.Count);

    /// <summary>筛选后条数文案。</summary>
    public string VisibleCountText => SelectedMod is null
        ? string.Empty
        :
          T("显示 {0} / {1} 项", LocalizationKeys.ModConfigVisibleCount, VisibleFieldCount, FieldTotal);

    /// <summary>待保存的草稿条数（跨 mod 累计）。</summary>
    public int PendingChangeCount => _pending.Count;

    /// <summary>是否有待保存的草稿。</summary>
    public bool HasPendingChanges => _pending.Count > 0;

    /// <summary>外壳底部「未保存」提示条：还有没写盘的配置草稿。</summary>
    bool IUnsavedChangesAware.HasUnsavedChanges => HasPendingChanges;

    /// <summary>提示条「保存更改」：把所有草稿写回 .cfg（逐文件原子写，失败保留草稿）。</summary>
    ICommand? IUnsavedChangesAware.SaveChangesCommand => SaveCommand;

    /// <summary>提示条「放弃修改」：清空全部草稿并重读磁盘。</summary>
    ICommand? IUnsavedChangesAware.DiscardChangesCommand => DiscardPendingCommand;

    /// <summary>底部改动条文案（"改动 N 项（待保存 M 项）"）。</summary>
    public string ModifiedBarText
    {
        get
        {
            var modified = SelectedMod is { HasModified: true } card
                ? T("改动 {0} 项", LocalizationKeys.ModConfigModifiedCount, card.Snapshot.ModifiedCount)
                : string.Empty;
            var pending = HasPendingChanges
                ? T("待保存 {0} 项", LocalizationKeys.ModConfigPendingCount, PendingChangeCount)
                : string.Empty;
            if (modified.Length == 0)
            {
                return pending;
            }

            return pending.Length == 0 ? modified : modified + "（" + pending + "）";
        }
    }

    /// <summary>是否有改动（磁盘上已改的 + 还没保存的草稿），决定 diff 条是否可见。</summary>
    public bool HasChanges => HasPendingChanges || SelectedMod is { HasModified: true };

    /// <summary>保存按钮文案（"保存 N 项改动"）。</summary>
    public string SaveButtonText => PendingChangeCount == 0 ? T("保存改动", LocalizationKeys.ModConfigSaveButton) : T("保存 {0} 项改动", LocalizationKeys.ModConfigSaveButtonPending, PendingChangeCount);

    /// <summary>底部写盘说明（写前快照 + 写后自校验 + 部分失败不丢输入）。</summary>
    public string WriteNoteText => T("保存时逐文件写盘，失败只报该文件。", LocalizationKeys.ModConfigWriteNote);

    /// <summary>是否显示底部动作条（有改动、有快照或上次保存失败时都要显示）。</summary>
    public bool ShowActionBar => HasChanges || HasBackups || HasSaveFailures;

    /// <summary>是否有可回滚的快照。</summary>
    public bool HasBackups => _backups.Count > 0;

    /// <summary>回滚按钮文案（"恢复上次快照（N 个文件）"）。</summary>
    public string RestoreButtonText => HasBackups
        ? T("恢复上次快照（{0} 个文件）", LocalizationKeys.ModConfigRestoreButtonWithCount, _backups.Count)
        : T("恢复上次快照", LocalizationKeys.ModConfigRestoreLastSnapshot);

    /// <summary>快照说明（没有快照时为空串）。</summary>
    public string BackupHintText => HasBackups
        ? T("上一次写盘前的快照：{0}", LocalizationKeys.ModConfigBackupHint,
              string.Join("、", _backups.Select(Path.GetFileName)))
        : string.Empty;

    /// <summary>是否有保存/回滚的逐文件失败说明。</summary>
    public bool HasSaveFailures => SaveFailures.Count > 0;

    /// <summary>失败列表标题。</summary>
    public string SaveFailureTitle => T("以下文件没有写成，改动已保留在页面上：", LocalizationKeys.ModConfigSaveFailureTitle);

    /// <summary>选中卡片最需要提示的一条告警。</summary>
    public string SelectedWarningText => SelectedMod?.WarningText ?? string.Empty;

    /// <summary>是否显示选中卡片的告警。</summary>
    public bool HasSelectedWarning => SelectedWarningText.Length > 0;

    /// <summary>未选中时的提示文案。</summary>
    public string NoSelectionHintText => T("选中一个 mod 后，这里会显示它的可配置项。", LocalizationKeys.ModConfigNoSelectionHint);

    /// <summary>左侧列表为空时的提示文案。</summary>
    public string EmptyModsHintText => T("未找到带配置适配器的 Mod。", LocalizationKeys.ModConfigNoSelectionHint);

    /// <summary>是否有适配器清单。</summary>
    public bool HasAdapters => Adapters.Count > 0;

    /// <summary>适配器清单标题。</summary>
    public string AdapterListTitle =>
        T("已加载 {0} 个适配器", LocalizationKeys.ModConfigAdapterListTitle, Adapters.Count);

    /// <summary>是否可用筛选（有选中 mod 才需要）。</summary>
    public bool CanFilter => HasSelection;

    // ---- 命令 --------------------------------------------------------------

    /// <summary>读盘并重建页面（页面激活时自动执行一次，"刷新"按钮也走这里）。</summary>
    [RelayCommand(CanExecute = nameof(CanLoad))]
    private Task LoadAsync() => LoadCoreAsync(forceReload: true);

    /// <summary>
    /// 读盘并重建页面。<paramref name="forceReload"/> 为 false 时允许服务层复用上一次结果——
    /// 页面每次激活都重扫适配器源文件（真实实例上约 90ms）没有必要，注册表变化会让缓存自己失效。
    /// </summary>
    private async Task LoadCoreAsync(bool forceReload)
    {
        if (IsBusy)
        {
            return;
        }

        IsBusy = true;
        BusyText = T("正在读取 mod 配置文件…", LocalizationKeys.ModConfigBusyLoading);
        ErrorText = null;
        try
        {
            LoadAdapters();
            var result = await _config.LoadAsync(string.Empty, forceReload).ConfigureAwait(true);
            if (!result.IsSuccess || result.Value is not { } model)
            {
                ApplyModelCore(null, null);
                if (result.Message?.Contains("没有选中的游戏实例") == true)
                {
                    StatusText = result.Message;
                }
                else
                {
                    SetError(result.Message ?? T("读取 mod 配置失败。", LocalizationKeys.ModConfigErrorLoad), result.Detail);
                }
                return;
            }

            ApplyModelCore(model, null);
        }
        catch (Exception ex)
        {
            ApplyModelCore(null, null);
            // 页面是最后一道防线：服务层约定返回 OperationResult，这里兜住意外异常，别让异常穿到 UI 线程外。
            SetError(T("读取 mod 配置失败：{0}", LocalizationKeys.ModConfigErrorLoadDetail, ex.Message), ex.GetType().Name);
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>加载条件：不忙。</summary>
    public bool CanLoad => !IsBusy;

    /// <summary>保存整页草稿（Ctrl+S / 底部"保存"按钮）。</summary>
    [RelayCommand(CanExecute = nameof(CanSave))]
    private async Task SaveAsync() => await SaveEditsAsync(_pending.Values.ToArray()).ConfigureAwait(true);

    /// <summary>
    /// 保存条件：有草稿、不忙。
    /// 刻意不把"有没有写盘入口"算进条件：没有 <see cref="IAdapterWriter"/> 时按钮仍可点，
    /// 点下去给一条可读错误（"当前环境没有写盘入口"），比一个沉默禁用的按钮更容易排查。
    /// </summary>
    public bool CanSave => HasPendingChanges && !IsBusy && !IsWriting;

    /// <summary>提交单行（Enter 键）：只写这一行所在的文件。</summary>
    /// <param name="row">要提交的字段行。</param>
    [RelayCommand]
    private async Task SaveRowAsync(ModConfigFieldRowViewModel? row)
    {
        if (row is null || !row.HasPendingChange)
        {
            SetStatus(T("这一行没有需要保存的改动。", LocalizationKeys.ModConfigStatusRowNoChanges));
            return;
        }

        await SaveEditsAsync([new PendingEdit(row.FilePath, row.Key, row.WireValue, row.Field.Risk)]).ConfigureAwait(true);
    }

    /// <summary>丢弃所有还没保存的草稿。</summary>
    [RelayCommand(CanExecute = nameof(CanDiscardPending))]
    private void DiscardPending()
    {
        var count = _pending.Count;
        foreach (var row in FieldGroups.SelectMany(group => group.Rows))
        {
            row.ResetEdit();
        }

        _pending.Clear();
        SetSaveFailures([]);
        NotifyEditState();
        SetStatus(T("已放弃 {0} 项未保存的改动。", LocalizationKeys.ModConfigStatusDiscarded, count));
    }

    /// <summary>是否有可放弃的草稿。</summary>
    public bool CanDiscardPending => HasPendingChanges && !IsWriting;

    /// <summary>把上一次写盘前的快照恢复回去（保存失败/写坏后的回滚入口）。</summary>
    [RelayCommand(CanExecute = nameof(CanRestoreBackups))]
    private async Task RestoreBackupsAsync()
    {
        if (!HasBackups)
        {
            SetStatus(T("没有可恢复的快照：只有保存过一次之后才有 .bak 快照。", LocalizationKeys.ModConfigStatusNoSnapshot));
            return;
        }

        if (!await ConfirmAsync(
                T("恢复上次快照", LocalizationKeys.ModConfigRestoreLastSnapshot),
                T("将把 {0} 个配置文件回滚到上一次写盘前的 .bak 快照，页面上还没保存的输入会被丢弃。继续吗？",
                    LocalizationKeys.ModConfigRestoreConfirmBody, _backups.Count))
            .ConfigureAwait(true))
        {
            SetStatus(T("已取消恢复快照。", LocalizationKeys.ModConfigStatusRestoreCancelled));
            return;
        }

        IsWriting = true;
        try
        {
            var result = await _restorer.RestoreAsync(_backups).ConfigureAwait(true);
            if (!result.IsSuccess || result.Value is not { } value)
            {
                SetError(result.Message ?? T("恢复快照失败。", LocalizationKeys.ModConfigErrorRestore), result.Detail);
                return;
            }

            _backups = [];
            _pending.Clear();
            SetSaveFailures(value.Failures);
            await RefreshModelAsync().ConfigureAwait(true);
            if (value.Failures.Count > 0)
            {
                SetError(
                    T("已恢复 {0} 个文件，但还有 {1} 个没有恢复。", LocalizationKeys.ModConfigErrorRestorePartial,
                        value.RestoredPaths.Count, value.Failures.Count),
                    null);
            }
            else
            {
                SetStatus(T("已恢复 {0} 个文件到上一次写盘前的快照。", LocalizationKeys.ModConfigStatusRestored,
                value.RestoredPaths.Count));
            }
        }
        catch (Exception ex)
        {
            SetError(T("恢复快照失败：{0}", LocalizationKeys.ModConfigErrorRestoreDetail, ex.Message), ex.GetType().Name);
        }
        finally
        {
            IsWriting = false;
        }
    }

    /// <summary>回滚条件：有快照、不忙。</summary>
    public bool CanRestoreBackups => HasBackups && !IsBusy && !IsWriting;

    /// <summary>重读适配器目录（用户往用户目录塞了新 adapter 后不用重启）。</summary>
    [RelayCommand]
    private void ReloadAdapters()
    {
        _catalog?.Reload();
        LoadAdapters();
        SetStatus(_catalog is null
            ? T("没有配置适配器目录，适配器清单为空。", LocalizationKeys.ModConfigEmptyNoAdapterDir)
            : T("已重新加载 {0} 个适配器。", LocalizationKeys.ModConfigStatusAdaptersReloaded, Adapters.Count));
    }

    /// <summary>
    /// 导入一个 adapter 包（本地 .json / .zip）：安装器做校验 + 原子替换 + 写 installed.json 清单，
    /// 成功后热重扫适配器目录并刷新页面（新 adapter 立刻可用，不用重启）。
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanImportAdapter))]
    private async Task ImportAdapterAsync()
    {
        if (_adapterInstaller is not { } installer || _adapterPicker is not { } picker || IsBusy)
        {
            return;
        }

        string? picked;
        try
        {
            picked = await picker().ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            SetError(T("选择 adapter 包时出错：{0}", LocalizationKeys.ModConfigErrorPickAdapter, ex.Message), null);
            return;
        }

        if (string.IsNullOrWhiteSpace(picked))
        {
            return;
        }

        IsBusy = true;
        BusyText = T("正在导入 adapter…", LocalizationKeys.ModConfigBusyImporting);
        ErrorText = null;
        try
        {
            var result = await installer.InstallAsync(AdapterPackageSource.FromFile(picked)).ConfigureAwait(true);
            if (!result.IsSuccess || result.Value is not { } installed)
            {
                SetError(result.Message ?? T("导入 adapter 失败。", LocalizationKeys.ModConfigErrorImport), result.Detail);
                return;
            }

            _catalog?.Reload();
            LoadAdapters();
            SetStatus(installed.ReplacedExisting
                ? T("已更新 adapter「{0}」（sha256 {1}）。", LocalizationKeys.ModConfigStatusAdapterReplaced,
                    installed.Manifest.Name, installed.Hash.ShortSha256)
                : T("已导入 adapter「{0}」（sha256 {1}）。", LocalizationKeys.ModConfigStatusAdapterImported,
                    installed.Manifest.Name, installed.Hash.ShortSha256));
        }
        catch (OperationCanceledException)
        {
            SetStatus(T("已取消导入。", LocalizationKeys.ModConfigStatusImportCancelled));
        }
        catch (Exception ex)
        {
            SetError(T("导入 adapter 失败：{0}", LocalizationKeys.ModConfigErrorImportDetail, ex.Message), null);
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>清空搜索。</summary>
    [RelayCommand]
    private void ClearSearch() => SearchText = string.Empty;

    /// <summary>切换"仅看已改"。</summary>
    [RelayCommand]
    private void ToggleOnlyModified() => OnlyModified = !OnlyModified;

    /// <summary>选中某个 mod 卡片（视图绑定用）。</summary>
    /// <param name="card">卡片；null 表示清空选择。</param>
    [RelayCommand]
    public void SelectMod(ModConfigModCardViewModel? card) => SelectedMod = card;

    // ---- 数据装配 ----------------------------------------------------------

    /// <summary>路径字段的「浏览…」通道（视图层接到系统的目录选择器；为空时按钮不出现）。</summary>
    /// <remarks>
    /// 用委托而不是接口，是为了让 ViewModels 层不依赖任何平台类型（Avalonia 的 StorageProvider 在 App 层）。
    /// 注入时机早于首次 Load，所以行对象构造时就能拿到；运行期换实现只需重建分组（RebuildGroups）。
    /// </remarks>
    public Func<ModConfigFieldRowViewModel, Task<string?>>? PathPicker { get; set; }

    /// <summary>
    /// adapter 导入端口（ADP-04）；为 null 时页面不提供「导入 adapter…」入口。
    /// </summary>
    /// <remarks>
    /// 只依赖 Abstractions 里的 <see cref="IAdapterInstaller"/>，不碰 Services 实现，所以本层仍可单测；
    /// 装完必须 <see cref="IAdapterCatalog.Reload"/>，否则新 adapter 要等下次启动才认。
    /// </remarks>
    public IAdapterInstaller? AdapterInstaller
    {
        get => _adapterInstaller;
        set
        {
            if (ReferenceEquals(_adapterInstaller, value))
            {
                return;
            }

            _adapterInstaller = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(CanImportAdapter));
            ImportAdapterCommand.NotifyCanExecuteChanged();
        }
    }

    /// <summary>adapter 包选择器（视图层接系统文件选择器：.json 或 .zip）；为 null 时按钮不出现。</summary>
    public Func<Task<string?>>? AdapterPackagePicker
    {
        get => _adapterPicker;
        set
        {
            if (ReferenceEquals(_adapterPicker, value))
            {
                return;
            }

            _adapterPicker = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(CanImportAdapter));
            ImportAdapterCommand.NotifyCanExecuteChanged();
        }
    }

    /// <summary>是否显示「导入 adapter…」入口（端口与选择器都就位才算）。</summary>
    public bool CanImportAdapter => _adapterInstaller is not null && _adapterPicker is not null;

    /// <summary>把服务层模型装进页面（null 表示没有模型/加载失败）。</summary>
    /// <param name="model">实例模型。</param>
    public void ApplyModel(ModConfigInstanceModel? model) => ApplyModelCore(model, null);

    /// <summary>按当前筛选条件重建字段分组（未保存的草稿会重新铺回编辑器）。</summary>
    public void RebuildGroups()
    {
        if (SelectedMod is not { } card)
        {
            FieldGroups = [];
            NotifyFilterState();
            return;
        }

        var rows = new List<ModConfigFieldRowViewModel>();
        foreach (var file in card.Snapshot.Files)
        {
            foreach (var field in file.Fields)
            {
                var row = new ModConfigFieldRowViewModel(field, _renderers.Resolve(field.Type), file.IsWritable, file.FilePath, file.ReadonlyReason)
                {
                    PathPicker = PathPicker,
                };
                if (!row.Matches(SearchText))
                {
                    continue;
                }

                if (OnlyModified && !row.IsModified)
                {
                    continue;
                }

                // 重读模型（保存/刷新）后把还没保存的草稿铺回去，别让用户的输入白白丢掉。
                if (_pending.TryGetValue(PendingKey(row.FilePath, row.Key), out var pending))
                {
                    row.ApplyDraft(pending.NewValue);
                }

                row.PropertyChanged += OnRowPropertyChanged;
                rows.Add(row);
            }
        }

        FieldGroups = rows
            .GroupBy(row => row.GroupTitle, StringComparer.Ordinal)
            .Select(group => new ModConfigFieldGroupViewModel(group.Key, group.ToArray()))
            .ToArray();
        NotifyFilterState();
    }

    private void ApplyModelCore(ModConfigInstanceModel? model, string? preferredAdapterId)
    {
        _model = model;
        if (model is null)
        {
            Mods = [];
            SelectedMod = null;
            IsGameRunning = false;
            InstanceSummaryText = string.Empty;
            RebuildGroups();
            NotifyModelState();
            return;
        }

        var cards = model.Mods.Select(snapshot => new ModConfigModCardViewModel(snapshot)).ToArray();
        Mods = cards;
        IsGameRunning = model.IsGameRunning;
        InstanceSummaryText = model.InstanceName.Length > 0
            ? T("实例：{0}｜游戏目录：{1}", LocalizationKeys.ModConfigInstanceSummary,
                model.InstanceName, model.GameDir)
            : T("游戏目录：{0}", LocalizationKeys.ModConfigGameDirSummary, model.GameDir);

        // 刷新时优先停在原来那张卡上（用户刚在这页改过东西）；首次加载按"可编辑且有改动 → 可编辑 → 第一张"。
        var preferred = preferredAdapterId is null
            ? null
            : cards.FirstOrDefault(card => string.Equals(card.AdapterId, preferredAdapterId, StringComparison.Ordinal));
        SelectedMod = preferred
            ?? cards.FirstOrDefault(card => card.IsEditable && card.HasModified)
            ?? cards.FirstOrDefault(card => card.IsEditable)
            ?? cards.FirstOrDefault();

        SetStatus(model.Mods.Count == 0
            ? T("这个实例里没有已适配的 mod：可以把 adapter.json 放进用户目录后重试。", LocalizationKeys.ModConfigEmptyNoAdaptedMod)
            : T("读取完成：{0}。", LocalizationKeys.ModConfigStatusLoaded, ModCountText));
        NotifyModelState();
    }

    private void LoadAdapters()
    {
        if (_catalog is null)
        {
            Adapters = [];
            return;
        }

        Adapters = _catalog.Adapters
            .Select(adapter => new ModConfigAdapterSummaryViewModel(adapter))
            .ToArray();
    }

    // ---- 写盘 --------------------------------------------------------------

    private async Task SaveEditsAsync(IReadOnlyList<PendingEdit> edits)
    {
        SetSaveFailures([]);
        if (edits.Count == 0)
        {
            SetStatus(T("没有需要保存的改动。", LocalizationKeys.ModConfigStatusNoChanges));
            return;
        }

        if (_writer is not { } writer)
        {
            SetError(T("当前环境没有写盘入口（IAdapterWriter），已取消保存。", LocalizationKeys.ModConfigErrorNoWriter), null);
            return;
        }

        if (_model is not { } model)
        {
            SetError(T("还没有读到实例模型，已取消保存。", LocalizationKeys.ModConfigErrorNoModel), null);
            return;
        }

        var groups = GroupByFile(edits);
        if (groups.Count == 0)
        {
            SetError(T("这些改动找不到所属的 mod，已取消保存。", LocalizationKeys.ModConfigErrorOrphanEdits), string.Join("、", edits.Select(edit => edit.Key).Distinct(StringComparer.Ordinal)));
            return;
        }

        // 确认一：游戏运行中要重启 KSP 才生效。
        var confirmedGameRunning = false;
        if (IsGameRunning)
        {
            if (!await ConfirmAsync(
                    T("KSP 正在运行", LocalizationKeys.ModConfigKspRunningTitle),
                    T("KSP 正在运行：现在写入的配置要等游戏重启后才会生效，运行中的游戏还可能覆盖这次写入。仍然保存吗？",
                        LocalizationKeys.ModConfigKspRunningConfirmBody))
                .ConfigureAwait(true))
            {
                SetStatus(T("已取消保存：KSP 正在运行时的写盘需要确认。", LocalizationKeys.ModConfigStatusCancelKsp));
                return;
            }

            confirmedGameRunning = true;
        }

        // 确认二：高风险字段（adapter 声明的 risk=high）再问一次。
        var confirmedRiskHigh = false;
        var riskyKeys = edits
            .Where(edit => edit.Risk == ModConfigFieldRisk.High)
            .Select(edit => edit.Key)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        if (riskyKeys.Length > 0)
        {
            if (!await ConfirmAsync(
                    T("确认修改高风险字段", LocalizationKeys.ModConfigConfirmHighRiskTitle),
                    T("这些字段被适配器标记为高风险，改错可能让 mod 行为异常甚至进不了游戏：{0}。确认写入吗？",
                        LocalizationKeys.ModConfigConfirmHighRiskBody, string.Join("、", riskyKeys)))
                .ConfigureAwait(true))
            {
                SetStatus(T("已取消保存：高风险字段的改动需要二次确认。", LocalizationKeys.ModConfigStatusCancelHighRisk));
                return;
            }

            confirmedRiskHigh = true;
        }

        IsWriting = true;
        var failures = new List<string>();
        var writtenFiles = new List<string>();
        var backups = new List<string>();
        var writtenChanges = 0;
        var restartCount = 0;
        try
        {
            foreach (var group in groups)
            {
                var request = new AdapterWriteRequest(
                    model.InstanceName,
                    model.GameDir,
                    group.AdapterId,
                    group.Changes,
                    ConfirmedRiskHigh: confirmedRiskHigh,
                    ConfirmedGameRunning: confirmedGameRunning);
                OperationResult<AdapterWriteResult> result;
                try
                {
                    result = await writer.WriteAsync(request).ConfigureAwait(true);
                }
                catch (Exception ex)
                {
                    // 写盘器约定返回 OperationResult；这里兜住意外异常，别让一个文件的异常打断其余文件。
                    result = OperationResult<AdapterWriteResult>.Fail(T("写入配置失败：{0}", LocalizationKeys.ModConfigErrorWriteDetail, ex.Message), ex.GetType().Name);
                }

                if (!result.IsSuccess)
                {
                    failures.Add(RelativeName(group.FilePath) + "：" + (result.Message ?? T("写入失败。", LocalizationKeys.ModConfigErrorWrite))
                        + (string.IsNullOrWhiteSpace(result.Detail) ? string.Empty : "（" + result.Detail + "）"));
                    continue;
                }

                writtenFiles.Add(group.FilePath);
                writtenChanges += result.Value?.Changes.Count ?? group.Changes.Count;
                restartCount += result.Value?.RequiresRestartCount ?? 0;
                if (result.Value is { } value)
                {
                    backups.AddRange(value.BackupPaths);
                }
            }
        }
        finally
        {
            IsWriting = false;
        }

        // 写成功的文件从待保存里摘掉；失败的文件保留草稿，用户可以改完再存。
        foreach (var id in _pending
                     .Where(pair => writtenFiles.Contains(pair.Value.FilePath, StringComparer.Ordinal))
                     .Select(pair => pair.Key)
                     .ToArray())
        {
            _pending.Remove(id);
        }

        foreach (var row in FieldGroups.SelectMany(group => group.Rows))
        {
            if (writtenFiles.Contains(row.FilePath, StringComparer.Ordinal))
            {
                row.MarkSaved();
            }
        }

        _backups = backups;
        SetSaveFailures(failures);
        NotifyEditState();

        // 先重读（拿磁盘真值 + 把失败文件的草稿铺回去），再覆盖状态文案，免得被"读取完成"冲掉。
        await RefreshModelAsync().ConfigureAwait(true);

        if (failures.Count == 0)
        {
            var summary = T("已保存 {0} 项改动（{1} 个文件）", LocalizationKeys.ModConfigStatusSaved,
                writtenChanges, writtenFiles.Count);
            if (restartCount > 0)
            {
                summary += "；" + restartCount + " 项需要重启 KSP 后生效";
            }

            ErrorText = null;
            SetStatus(summary + "。");
        }
        else
        {
            SetError(
                writtenFiles.Count == 0
                    ? T("保存失败：这次没有任何文件写成，改动已保留。", LocalizationKeys.ModConfigErrorSaveNothing)
                    : T("部分文件保存失败：{0} / {1} 个文件没写成，改动已保留。", LocalizationKeys.ModConfigErrorSavePartial,
                        failures.Count, writtenFiles.Count + failures.Count),
                null);
        }
    }

    private IReadOnlyList<PendingWriteGroup> GroupByFile(IReadOnlyList<PendingEdit> edits)
    {
        var owners = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var card in Mods)
        {
            foreach (var file in card.Snapshot.Files)
            {
                owners[file.FilePath] = card.AdapterId;
            }
        }

        var groups = new List<PendingWriteGroup>();
        var index = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var edit in edits)
        {
            if (!owners.TryGetValue(edit.FilePath, out var adapterId))
            {
                continue;
            }

            var id = adapterId + '\u0000' + edit.FilePath;
            if (!index.TryGetValue(id, out var position))
            {
                position = groups.Count;
                index[id] = position;
                groups.Add(new PendingWriteGroup(adapterId, edit.FilePath, []));
            }

            groups[position].Changes.Add(new ModConfigFieldChange(edit.FilePath, edit.Key, edit.NewValue));
        }

        return groups;
    }

    private async Task RefreshModelAsync()
    {
        try
        {
            var result = await _config.LoadAsync(string.Empty, forceReload: true).ConfigureAwait(true);
            if (result.IsSuccess && result.Value is { } model)
            {
                ApplyModelCore(model, SelectedMod?.AdapterId);
            }
            else
            {
                SetError(result.Message ?? T("重读 mod 配置失败。", LocalizationKeys.ModConfigErrorReload), result.Detail);
            }
        }
        catch (Exception ex)
        {
            SetError(T("重读 mod 配置失败：{0}", LocalizationKeys.ModConfigErrorReloadDetail, ex.Message), ex.GetType().Name);
        }
    }

    // ---- 草稿收集 ----------------------------------------------------------

    private void OnRowPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (sender is not ModConfigFieldRowViewModel row
            || !string.Equals(e.PropertyName, nameof(ModConfigFieldRowViewModel.EditValue), StringComparison.Ordinal))
        {
            return;
        }

        var id = PendingKey(row.FilePath, row.Key);
        if (row.HasPendingChange)
        {
            _pending[id] = new PendingEdit(row.FilePath, row.Key, row.WireValue, row.Field.Risk);
        }
        else
        {
            _pending.Remove(id);
        }

        NotifyEditState();
    }

    private void SetSaveFailures(IReadOnlyList<string> failures)
    {
        SaveFailures = failures;
        OnPropertyChanged(nameof(HasSaveFailures));
        OnPropertyChanged(nameof(ShowActionBar));
    }

    private static string PendingKey(string filePath, string key) => filePath + '\u0000' + key;

    private static string RelativeName(string filePath)
    {
        var name = Path.GetFileName(filePath);
        return string.IsNullOrEmpty(name) ? filePath : name;
    }

    private async Task<bool> ConfirmAsync(string title, string message)
    {
        if (_dialogs is not { } dialogs)
        {
            // 没有对话框服务时宁可不动磁盘：写盘是不可逆操作，不能靠"默认同意"蒙混过去。
            SetError(T("当前环境没有确认对话框，已取消这次写盘。", LocalizationKeys.ModConfigErrorNoDialog), "IDialogService");
            return false;
        }

        return await dialogs.ConfirmAsync(title, message).ConfigureAwait(true);
    }

    private void SetStatus(string message) => StatusText = message;

    private void SetError(string message, string? detail)
    {
        ErrorText = string.IsNullOrWhiteSpace(detail) ? message : message + "（" + detail + "）";
        StatusText = ErrorText ?? message;
    }

    /// <summary>
    /// 本页唯一的文案出口：构造注入的外部译文优先（仍按中文原文调用，保持旧参数的兼容），
    /// 否则交给统一的 Text(fallback, key) 文案层。
    /// </summary>
    private string T(string fallback, string key, params object?[] args)
    {
        if (_translate is not null)
        {
            var translated = _translate(fallback);
            return args.Length == 0 ? translated : string.Format(translated, args);
        }

        return args.Length == 0 ? Text(fallback, key) : Text(fallback, key, args);
    }

    private void NotifyModelState()
    {
        OnPropertyChanged(nameof(HasMods));
        OnPropertyChanged(nameof(ShowEmptyState));
        OnPropertyChanged(nameof(ModCountText));
        OnPropertyChanged(nameof(AdapterlessText));
        OnPropertyChanged(nameof(HasAdapterless));
        OnPropertyChanged(nameof(GameRunningWarningText));
        OnPropertyChanged(nameof(ShowGameRunningWarning));
        OnPropertyChanged(nameof(AdapterListTitle));
        OnPropertyChanged(nameof(HasAdapters));
        OnPropertyChanged(nameof(CanFilter));
        LoadCommand.NotifyCanExecuteChanged();
    }

    private void NotifyFilterState()
    {
        OnPropertyChanged(nameof(HasSelection));
        OnPropertyChanged(nameof(IsSelectionEmpty));
        OnPropertyChanged(nameof(HeaderTitleText));
        OnPropertyChanged(nameof(HeaderStateText));
        OnPropertyChanged(nameof(SearchPlaceholder));
        OnPropertyChanged(nameof(FieldTotal));
        OnPropertyChanged(nameof(VisibleFieldCount));
        OnPropertyChanged(nameof(VisibleCountText));
        OnPropertyChanged(nameof(SelectedWarningText));
        OnPropertyChanged(nameof(HasSelectedWarning));
        OnPropertyChanged(nameof(CanFilter));
        OnPropertyChanged(nameof(HasAdapters));
        NotifyEditState();
    }

    private void NotifyEditState()
    {
        OnPropertyChanged(nameof(PendingChangeCount));
        OnPropertyChanged(nameof(HasPendingChanges));
        OnPropertyChanged(nameof(ModifiedBarText));
        OnPropertyChanged(nameof(HasChanges));
        OnPropertyChanged(nameof(SaveButtonText));
        OnPropertyChanged(nameof(ShowActionBar));
        OnPropertyChanged(nameof(HasBackups));
        OnPropertyChanged(nameof(RestoreButtonText));
        OnPropertyChanged(nameof(BackupHintText));
        OnPropertyChanged(nameof(CanSave));
        OnPropertyChanged(nameof(CanDiscardPending));
        OnPropertyChanged(nameof(CanRestoreBackups));
        SaveCommand.NotifyCanExecuteChanged();
        DiscardPendingCommand.NotifyCanExecuteChanged();
        RestoreBackupsCommand.NotifyCanExecuteChanged();
    }

    /// <inheritdoc />
    protected override void OnActivated()
    {
        base.OnActivated();

        // 进页面就要看到最新状态，但不重扫磁盘：注册表变化会由服务层缓存失效，用户要强制重读走「刷新」。
        _ = LoadCoreAsync(forceReload: false);
    }

    partial void OnSelectedModChanged(ModConfigModCardViewModel? value) => RebuildGroups();

    partial void OnSearchTextChanged(string value) => RebuildGroups();

    partial void OnOnlyModifiedChanged(bool value) => RebuildGroups();

    partial void OnIsBusyChanged(bool value)
    {
        OnPropertyChanged(nameof(CanLoad));
        OnPropertyChanged(nameof(ShowEmptyState));
        OnPropertyChanged(nameof(CanSave));
        OnPropertyChanged(nameof(CanRestoreBackups));
        LoadCommand.NotifyCanExecuteChanged();
        SaveCommand.NotifyCanExecuteChanged();
        RestoreBackupsCommand.NotifyCanExecuteChanged();
    }

    partial void OnIsWritingChanged(bool value) => NotifyEditState();

    partial void OnErrorTextChanged(string? value) => OnPropertyChanged(nameof(HasError));

    partial void OnModsChanged(IReadOnlyList<ModConfigModCardViewModel> value)
    {
        OnPropertyChanged(nameof(HasMods));
        OnPropertyChanged(nameof(ShowEmptyState));
        OnPropertyChanged(nameof(ModCountText));
    }

    partial void OnAdaptersChanged(IReadOnlyList<ModConfigAdapterSummaryViewModel> value)
    {
        OnPropertyChanged(nameof(HasAdapters));
        OnPropertyChanged(nameof(AdapterListTitle));
    }

    partial void OnIsGameRunningChanged(bool value)
    {
        OnPropertyChanged(nameof(GameRunningWarningText));
        OnPropertyChanged(nameof(ShowGameRunningWarning));
    }

    /// <summary>一条还没保存的草稿。</summary>
    private sealed record PendingEdit(string FilePath, string Key, string NewValue, ModConfigFieldRisk Risk);

    /// <summary>按"适配器 + 文件"归并的一组写盘请求。</summary>
    private sealed record PendingWriteGroup(string AdapterId, string FilePath, List<ModConfigFieldChange> Changes);

    /// <summary>设计时/预览用的空实现：什么都没装，页面显示空状态而不是崩。</summary>
    private sealed class EmptyModConfigService : IModConfigService
    {
        public Task<OperationResult<ModConfigInstanceModel>> LoadAsync(string instanceName, bool forceReload = false, CancellationToken cancellationToken = default)
            => Task.FromResult(OperationResult<ModConfigInstanceModel>.Fail("设计时预览：没有可用的 mod 配置服务。"));
    }
}
