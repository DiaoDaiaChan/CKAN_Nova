using CkanUI.Services.Abstractions;
using CkanUI.Services.Abstractions.Dtos;
using CkanUI.ViewModels.Navigation;
using CkanUI.ViewModels.Pages.Mods;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace CkanUI.ViewModels.Pages;

/// <summary>
/// 模组管理页（03 篇 §3 / 04 篇 §2 / 06 篇 UI-02·UI-03·VM-01）：工具条 + 左列表 + 右详情 + 状态栏。
/// </summary>
/// <remarks>
/// <para>分层约定：本类只做「取快照 → 丢给纯函数 → 整体替换结果」与命令，不做任何解析/排序逻辑，
/// 所有过滤排序搜索都在 <see cref="ModsListPipeline"/> 等纯函数里，可 headless 测试。</para>
/// <para>异常收口（05 篇）：所有服务调用与事件回调都裹 try/catch，异常转成 <see cref="StatusText"/>
/// 并交给 <see cref="IErrorReporter"/>，绝不让异常穿透到 UI 线程。</para>
/// <para>构造参数全部是 Abstractions 里已注册的接口；<c>IChangeSetService</c> 尚未实现（INT-07），
/// 声明为可选参数，缺失时退化为本地估算预览（<see cref="ModsChangePreviewBuilder"/>）。</para>
/// </remarks>
public sealed partial class ModsPageViewModel : PageViewModel
{
    /// <summary>搜索防抖时长。规格 03 篇 §3.4 允许 150ms、验收要求 ≤100ms，这里取 80ms 给调度留余量。</summary>
    public const int SearchDebounceMilliseconds = 80;

    /// <summary>列宽保存防抖时长（03 篇 §3.1 列设置 / UI-02）。拖拽列宽会连续触发，300ms 内只落一次盘。</summary>
    public const int ColumnWidthSaveDebounceMilliseconds = 300;

    /// <summary>
    /// 列宽键与默认值（单位 DIP）。键名即视图列标识，也是 <see cref="UiSettings.ColumnWidths"/> 的键：
    /// select / name / installed / latest / author / downloads / tags / size。
    /// </summary>
    public static readonly IReadOnlyDictionary<string, double> DefaultColumnWidths =
        new Dictionary<string, double>(StringComparer.Ordinal)
        {
            ["select"] = 32,
            ["name"] = 260,
            ["installed"] = 90,
            ["latest"] = 90,
            ["author"] = 140,
            ["downloads"] = 80,
            ["tags"] = 200,
            ["size"] = 70,
        };

    private readonly IRepositoryService _repositories;
    private readonly IRegistryService _registry;
    private readonly IInstanceService _instances;
    private readonly IErrorReporter _errors;
    private readonly IChangeSetService? _changeSets;
    private readonly IUiSettingsStore? _settings;
    private readonly IChangeRequestSink? _changeSink;
    private readonly INavigationService? _navigation;
    private readonly IModSearchParser? _searchParser;

    private readonly HashSet<string> _selected = new(StringComparer.OrdinalIgnoreCase);
    private IReadOnlyList<ModRowData> _all = Array.Empty<ModRowData>();
    private ModsQuery _query = ModsQuery.Empty;

    private CancellationTokenSource? _searchCts;
    private int _searchVersion;
    private CancellationTokenSource? _columnWidthsCts;
    private int _columnWidthsVersion;
    private bool _subscribed;

    /// <summary>默认构造：仅供设计器与既有单测（<c>new ModsPageViewModel()</c>）使用，不连任何服务。</summary>
    public ModsPageViewModel()
        : this(new EmptyRepositoryService(), new EmptyRegistryService(), new EmptyInstanceService(), new EmptyErrorReporter())
    {
    }

    /// <summary>依赖注入构造。</summary>
    public ModsPageViewModel(
        IRepositoryService repositories,
        IRegistryService registry,
        IInstanceService instances,
        IErrorReporter errors,
        IChangeSetService? changeSets = null,
        IUiSettingsStore? settings = null,
        IChangeRequestSink? changeSink = null,
        INavigationService? navigation = null,
        IModSearchParser? searchParser = null)
    {
        _repositories = repositories ?? throw new ArgumentNullException(nameof(repositories));
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        _instances = instances ?? throw new ArgumentNullException(nameof(instances));
        _errors = errors ?? throw new ArgumentNullException(nameof(errors));
        _changeSets = changeSets;
        _settings = settings;
        _changeSink = changeSink;
        _navigation = navigation;
        _searchParser = searchParser;

        RefreshDetail();
    }

    /// <inheritdoc />
    public override string Title => "模组";

    /// <summary>搜索防抖时长（测试可缩短；生产用默认 80ms）。</summary>
    public TimeSpan SearchDebounce { get; init; } = TimeSpan.FromMilliseconds(SearchDebounceMilliseconds);

    /// <summary>防抖延时来源（生产为真实延时，测试注入可控实现）。</summary>
    public IModsDelayScheduler Scheduler { get; init; } = TaskModsDelayScheduler.Instance;

    /// <summary>最近一次防抖搜索任务（测试用 await 等待其落定）。</summary>
    public Task PendingSearch { get; private set; } = Task.CompletedTask;

    // ---- 可观察状态 -------------------------------------------------------

    /// <summary>搜索框文本（03 篇 §3.4，防抖 80ms 后生效）。</summary>
    [ObservableProperty]
    private string _searchText = string.Empty;

    /// <summary>当前可见行（整体替换，不做增量更新）。</summary>
    [ObservableProperty]
    private IReadOnlyList<ModsRowView> _rows = Array.Empty<ModsRowView>();

    /// <summary>当前选中行（单击选中，详情跟随；不影响勾选）。</summary>
    [ObservableProperty]
    private ModsRowView? _selectedRow;

    /// <summary>安装状态过滤器。</summary>
    [ObservableProperty]
    private ModsInstallFilter _installFilter = ModsInstallFilter.All;

    /// <summary>兼容性过滤器（默认仅兼容）。</summary>
    [ObservableProperty]
    private ModsCompatibilityFilter _compatibilityFilter = ModsCompatibilityFilter.CompatibleOnly;

    /// <summary>排序列（默认名称）。</summary>
    [ObservableProperty]
    private ModsSortColumn _sortColumn = ModsSortColumn.Name;

    /// <summary>是否升序。</summary>
    [ObservableProperty]
    private bool _sortAscending = true;

    /// <summary>状态栏文本。</summary>
    [ObservableProperty]
    private string _statusText = "就绪";

    /// <summary>是否有异步操作进行中。</summary>
    [ObservableProperty]
    private bool _isBusy;

    /// <summary>变更集预览（本地估算；接入 INT-07 后为权威结果）。</summary>
    [ObservableProperty]
    private ChangeSetPreview _changePreview = ModsChangePreviewBuilder.Empty;

    // 下拉框索引（ComboBox.SelectedIndex 直接绑定，避免 XAML 里放枚举转换器）。
    [ObservableProperty]
    private int _installFilterIndex;

    [ObservableProperty]
    private int _compatibilityFilterIndex = (int)ModsCompatibilityFilter.CompatibleOnly;

    [ObservableProperty]
    private int _sortColumnIndex;

    // ---- 派生只读属性（供界面绑定）----------------------------------------

    /// <summary>全量行数（不受过滤影响）。</summary>
    public int TotalCount => _all.Count;

    /// <summary>当前可见行数。</summary>
    public int VisibleCount => Rows.Count;

    /// <summary>已勾选数量（不受过滤影响，切过滤器不丢选择）。</summary>
    public int SelectedCount => _selected.Count;

    /// <summary>是否有可见行。</summary>
    public bool HasRows => Rows.Count > 0;

    /// <summary>是否显示空状态占位。</summary>
    public bool ShowEmptyState => Rows.Count == 0;

    /// <summary>是否有当前游戏实例。</summary>
    public bool HasInstance
    {
        get
        {
            try
            {
                return _instances.Current is not null;
            }
            catch (Exception ex)
            {
                _errors.Report(ex, "读取当前实例", "读取当前游戏实例失败。");
                return false;
            }
        }
    }

    /// <summary>当前实例名（无实例时为空串）。</summary>
    public string InstanceName
    {
        get
        {
            try
            {
                return _instances.Current?.Name ?? string.Empty;
            }
            catch (Exception ex)
            {
                _errors.Report(ex, "读取当前实例", "读取当前游戏实例失败。");
                return string.Empty;
            }
        }
    }

    /// <summary>计数摘要（状态栏左段）。</summary>
    public string CountText => $"显示 {ModsFormatter.Count(Rows.Count)} / {ModsFormatter.Count(_all.Count)} 个模组";

    /// <summary>选择与变更摘要（状态栏中段）。</summary>
    public string SelectionSummary
    {
        get
        {
            var installs = 0;
            var removals = 0;
            foreach (var item in ChangePreview.Items)
            {
                if (item.Action == ChangeAction.Remove)
                {
                    removals++;
                }
                else
                {
                    installs++;
                }
            }

            return $"已选 {SelectedCount} · 变更 +{installs}/-{removals}";
        }
    }

    /// <summary>下载体积估算（状态栏右段）。</summary>
    public string DownloadSummary => ChangePreview.IsEmpty
        ? "无需下载"
        : $"需下载 {ModsFormatter.Bytes(ChangePreview.EstimatedDownloadBytes)}";

    /// <summary>预览里是否有冲突。</summary>
    public bool PreviewHasConflicts => ChangePreview.HasConflicts;

    /// <summary>是否存在待应用的变更（控制"应用变更"按钮可用性）。</summary>
    public bool HasPendingChanges => !ChangePreview.IsEmpty;

    /// <summary>当前生效的查询/过滤条件（空状态时以 chip 形式展示，可一键清除）。</summary>
    public IReadOnlyList<string> ActiveFilterChips
    {
        get
        {
            var chips = new List<string>(4);
            if (!string.IsNullOrWhiteSpace(SearchText))
            {
                chips.Add($"搜索：{SearchText.Trim()}");
            }

            if (InstallFilter != ModsInstallFilter.All)
            {
                chips.Add($"安装状态：{DescribeInstallFilter(InstallFilter)}");
            }

            if (CompatibilityFilter != ModsCompatibilityFilter.All)
            {
                chips.Add($"兼容性：{(CompatibilityFilter == ModsCompatibilityFilter.CompatibleOnly ? "仅兼容" : "仅不兼容")}");
            }

            foreach (var term in _query.UnsupportedTerms)
            {
                chips.Add($"无法解析：{term}");
            }

            return chips;
        }
    }

    /// <summary>空状态标题（03 篇 §10）。</summary>
    public string EmptyStateTitle
    {
        get
        {
            if (!HasInstance)
            {
                return "未找到 KSP 实例";
            }

            if (_all.Count == 0)
            {
                return "尚未加载模组索引";
            }

            return "没有匹配的模组";
        }
    }

    /// <summary>空状态说明（03 篇 §10）。</summary>
    public string EmptyStateHint
    {
        get
        {
            if (!HasInstance)
            {
                return "请先在「实例」页添加或选择一个 KSP 安装目录。";
            }

            if (_all.Count == 0)
            {
                return "仓库索引为空：点击「刷新仓库」联网获取索引，或检查网络与仓库地址。";
            }

            return "试着放宽过滤条件，或按 Esc 清空搜索。";
        }
    }

    /// <summary>空状态下是否给出"立即刷新"入口。</summary>
    public bool CanRefreshFromEmptyState => HasInstance;

    /// <summary>搜索里是否有 VM 无法判定的语法（提示用）。</summary>
    public bool HasUnsupportedQueryTerms => _query.HasUnsupportedTerms;

    // ---- 详情面板（03 篇 §3.1 右侧）---------------------------------------

    /// <summary>是否有选中的行。</summary>
    public bool HasSelectedRow => SelectedRow is not null;

    /// <summary>详情标题。</summary>
    public string DetailName => SelectedRow?.Name ?? "未选择模组";

    /// <summary>详情标识符。</summary>
    public string DetailIdentifier => SelectedRow?.Identifier ?? string.Empty;

    /// <summary>详情作者。</summary>
    public string DetailAuthor => SelectedRow?.AuthorText ?? "—";

    /// <summary>详情版本信息。</summary>
    public string DetailVersionText => SelectedRow is null
        ? "—"
        : ModsFormatter.Versions(SelectedRow.Data.InstalledVersion, SelectedRow.Data.LatestVersion);

    /// <summary>详情下载量。</summary>
    public string DetailDownloadText => SelectedRow is null ? "—" : SelectedRow.DownloadText;

    /// <summary>详情体积。</summary>
    public string DetailSizeText => SelectedRow?.SizeText ?? "—";

    /// <summary>详情标签。</summary>
    public string DetailTagsText => SelectedRow is null || SelectedRow.TagsText.Length == 0
        ? "无标签"
        : SelectedRow.TagsText;

    /// <summary>详情状态文本。</summary>
    public string DetailStatusText => SelectedRow?.StatusText ?? "—";

    /// <summary>详情里该模组是否已装入当前实例。</summary>
    public bool DetailIsInstalled => SelectedRow?.IsInstalled ?? false;

    // ---- 生命周期 ---------------------------------------------------------

    /// <inheritdoc />
    protected override void OnActivated()
    {
        Subscribe();
        RestoreColumnWidths();
        ReloadFromSnapshots();
    }

    /// <inheritdoc />
    protected override void OnDeactivated()
    {
        Unsubscribe();
        CancelPendingSearch();
        CancelPendingColumnWidthSave();
    }

    private void Subscribe()
    {
        if (_subscribed)
        {
            return;
        }

        _repositories.Updated += OnRepositoryUpdated;
        _registry.Changed += OnRegistryChanged;
        _instances.CurrentChanged += OnInstanceChanged;
        _subscribed = true;
    }

    private void Unsubscribe()
    {
        if (!_subscribed)
        {
            return;
        }

        _repositories.Updated -= OnRepositoryUpdated;
        _registry.Changed -= OnRegistryChanged;
        _instances.CurrentChanged -= OnInstanceChanged;
        _subscribed = false;
    }

    private void OnRepositoryUpdated(RepositorySnapshot snapshot)
    {
        try
        {
            ReloadFromSnapshots();
        }
        catch (Exception ex)
        {
            _errors.Report(ex, "刷新模组列表", "更新模组列表时发生错误。");
            StatusText = "更新模组列表失败，请查看错误栏。";
        }
    }

    private void OnRegistryChanged(RegistrySnapshot snapshot)
    {
        try
        {
            ReloadFromSnapshots();
        }
        catch (Exception ex)
        {
            _errors.Report(ex, "刷新已装模组", "更新已安装模组时发生错误。");
            StatusText = "更新已安装模组失败，请查看错误栏。";
        }
    }

    private void OnInstanceChanged(InstanceInfo? instance)
    {
        try
        {
            // 切实例：清空选择集，避免把旧实例的勾选带到新实例（06 篇 M1 门禁"切实例不串台"）。
            _selected.Clear();
            SelectedRow = null;
            ReloadFromSnapshots();
        }
        catch (Exception ex)
        {
            _errors.Report(ex, "切换实例", "切换实例后刷新模组列表失败。");
            StatusText = "切换实例后刷新失败，请查看错误栏。";
        }
    }

    // ---- 核心动作 ---------------------------------------------------------

    /// <summary>从当前两份快照重建全量行并重新过滤（幂等，不做 IO）。</summary>
    public void ReloadFromSnapshots()
    {
        try
        {
            _all = ModsRowProjection.Merge(_repositories.Current, _registry.Current);
        }
        catch (Exception ex)
        {
            _errors.Report(ex, "读取模组快照", "读取模组数据时发生错误。");
            StatusText = "读取模组数据失败，请查看错误栏。";
            _all = Array.Empty<ModRowData>();
        }

        RebuildSelectionPreview();
        ApplyFilter();
    }

    /// <summary>按当前搜索/过滤/排序重建可见行（纯计算，可 headless 调用）。</summary>
    public void ApplyFilter()
    {
        // 语法解析委托给服务层（上游 ModSearch），本页只做条件匹配（12 篇 F-20/F-21）。
        // 没有接线解析器时不做任何过滤，但把每个词都标成"无法解析"，状态栏会如实提示。
        _query = _searchParser is null
            ? ModsQuery.Unparsed(SearchText)
            : ModsQuery.FromSpec(_searchParser.Parse(SearchText));

        IReadOnlyList<ModRowData> rows;
        try
        {
            rows = ModsListPipeline.Apply(_all, _query, InstallFilter, CompatibilityFilter, SortColumn, SortAscending);
        }
        catch (Exception ex)
        {
            _errors.Report(ex, "过滤模组列表", "过滤模组列表时发生错误。");
            rows = Array.Empty<ModRowData>();
        }

        var views = new List<ModsRowView>(rows.Count);
        foreach (var row in rows)
        {
            views.Add(ModsRowView.From(row with { IsSelected = _selected.Contains(row.Identifier) }));
        }

        Rows = views;

        // 选中行按标识符重新解析，避免整体替换后详情面板丢失。
        var selectedIdentifier = SelectedRow?.Identifier;
        SelectedRow = selectedIdentifier is null
            ? null
            : views.FirstOrDefault(view => string.Equals(view.Identifier, selectedIdentifier, StringComparison.OrdinalIgnoreCase));

        UpdateStatusText();
        RefreshDerived();
    }

    /// <summary>勾选或取消勾选一个模组（03 篇 §3.6）。</summary>
    public void ToggleSelection(string identifier)
    {
        if (string.IsNullOrWhiteSpace(identifier))
        {
            return;
        }

        if (!_selected.Remove(identifier))
        {
            _selected.Add(identifier);
        }

        RebuildSelectionPreview();

        // 只刷勾选状态，不重建整表：保留滚动位置（3,000 行滚动的流畅性）。
        Rows = Rows.Select(view => string.Equals(view.Identifier, identifier, StringComparison.OrdinalIgnoreCase)
            ? view.WithSelected(_selected.Contains(identifier))
            : view).ToList();

        UpdateStatusText();
        RefreshDerived();
    }

    /// <summary>清空选择集。</summary>
    public void ClearSelection()
    {
        if (_selected.Count == 0)
        {
            return;
        }

        _selected.Clear();
        RebuildSelectionPreview();
        Rows = Rows.Select(view => view.WithSelected(false)).ToList();
        UpdateStatusText();
        RefreshDerived();
    }

    /// <summary>清空搜索框（Esc）。</summary>
    public void ClearSearch()
    {
        CancelPendingSearch();
        SearchText = string.Empty;
        ApplyFilter();
    }

    /// <summary>清空全部搜索与过滤条件（空状态里的"清除过滤"按钮）。</summary>
    public void ClearFilters()
    {
        CancelPendingSearch();
        SearchText = string.Empty;
        InstallFilterIndex = (int)ModsInstallFilter.All;
        CompatibilityFilterIndex = (int)ModsCompatibilityFilter.All;
        ApplyFilter();
    }

    /// <summary>切换升/降序。</summary>
    public void ToggleSortDirection()
    {
        SortAscending = !SortAscending;
    }

    /// <summary>按列名切换排序列（列头点击）：同一列再点一次翻转方向。</summary>
    public void SortByColumn(ModsSortColumn column)
    {
        if (SortColumn == column)
        {
            SortAscending = !SortAscending;
            return;
        }

        SortColumn = column;
        SortAscending = column is ModsSortColumn.Name or ModsSortColumn.Identifier or ModsSortColumn.Author;
    }

    private void RebuildSelectionPreview()
    {
        try
        {
            ChangePreview = ModsChangePreviewBuilder.Build(_all, _selected);
        }
        catch (Exception ex)
        {
            _errors.Report(ex, "生成变更预览", "生成变更预览时发生错误。");
            ChangePreview = ModsChangePreviewBuilder.Empty;
        }
    }

    private void UpdateStatusText()
    {
        var installs = 0;
        var removals = 0;
        foreach (var item in ChangePreview.Items)
        {
            if (item.Action == ChangeAction.Remove)
            {
                removals++;
            }
            else
            {
                installs++;
            }
        }

        var line = ModsFormatter.StatusLine(Rows.Count, _all.Count, _selected.Count, installs, removals);
        StatusText = _query.HasUnsupportedTerms
            ? $"{line} · {_query.UnsupportedMessage}"
            : line;
    }

    private void RefreshDerived()
    {
        OnPropertyChanged(nameof(TotalCount));
        OnPropertyChanged(nameof(VisibleCount));
        OnPropertyChanged(nameof(SelectedCount));
        OnPropertyChanged(nameof(HasRows));
        OnPropertyChanged(nameof(ShowEmptyState));
        OnPropertyChanged(nameof(HasInstance));
        OnPropertyChanged(nameof(InstanceName));
        OnPropertyChanged(nameof(CountText));
        OnPropertyChanged(nameof(SelectionSummary));
        OnPropertyChanged(nameof(DownloadSummary));
        OnPropertyChanged(nameof(PreviewHasConflicts));
        OnPropertyChanged(nameof(HasPendingChanges));
        OnPropertyChanged(nameof(ActiveFilterChips));
        OnPropertyChanged(nameof(EmptyStateTitle));
        OnPropertyChanged(nameof(EmptyStateHint));
        OnPropertyChanged(nameof(CanRefreshFromEmptyState));
        OnPropertyChanged(nameof(HasUnsupportedQueryTerms));
        RefreshDetail();
        ApplyChangesCommand.NotifyCanExecuteChanged();
    }

    private void RefreshDetail()
    {
        OnPropertyChanged(nameof(HasSelectedRow));
        OnPropertyChanged(nameof(DetailName));
        OnPropertyChanged(nameof(DetailIdentifier));
        OnPropertyChanged(nameof(DetailAuthor));
        OnPropertyChanged(nameof(DetailVersionText));
        OnPropertyChanged(nameof(DetailDownloadText));
        OnPropertyChanged(nameof(DetailSizeText));
        OnPropertyChanged(nameof(DetailTagsText));
        OnPropertyChanged(nameof(DetailStatusText));
        OnPropertyChanged(nameof(DetailIsInstalled));
    }

    private static string DescribeInstallFilter(ModsInstallFilter filter) => filter switch
    {
        ModsInstallFilter.Installed => "已装",
        ModsInstallFilter.NotInstalled => "未装",
        ModsInstallFilter.Upgradeable => "有更新",
        _ => "全部",
    };

    // ---- 列宽持久化（UI-02：列定义 / 列宽记忆）--------------------------

    /// <summary>当前生效的列宽（键见 <see cref="DefaultColumnWidths"/>，单位 DIP）。视图 Loaded 时套用到各列。</summary>
    [ObservableProperty]
    private IReadOnlyDictionary<string, double> _columnWidths = DefaultColumnWidths;

    /// <summary>最近一次列宽防抖保存任务（测试可 await 其落定）。</summary>
    public Task PendingColumnWidthSave { get; private set; } = Task.CompletedTask;

    /// <summary>
    /// 从设置恢复列宽：以默认值为底叠加已保存的合法值，同步到 <see cref="ColumnWidths"/> 并返回结果。
    /// 无设置服务、已存为空或读取抛异常时一律退回默认列宽（不抛、不弹错，界面照常可用）。
    /// </summary>
    public IReadOnlyDictionary<string, double> RestoreColumnWidths()
    {
        var restored = DefaultColumnWidths;
        if (_settings is not null)
        {
            try
            {
                var stored = _settings.Current.ColumnWidths;
                if (stored is { Count: > 0 })
                {
                    var merged = new Dictionary<string, double>(DefaultColumnWidths, StringComparer.Ordinal);
                    foreach (var pair in stored)
                    {
                        if (IsSaneColumnWidth(pair.Value))
                        {
                            merged[pair.Key] = pair.Value;
                        }
                    }

                    restored = merged;
                }
            }
            catch (Exception ex)
            {
                _errors.Report(ex, "读取列宽", "读取列宽设置失败，已使用默认列宽。");
                restored = DefaultColumnWidths;
            }
        }

        ColumnWidths = restored;
        return restored;
    }

    /// <summary>
    /// 记录列宽变更：<see cref="ColumnWidthSaveDebounceMilliseconds"/> 防抖后合并进
    /// <see cref="UiSettings.ColumnWidths"/> 落盘。连续拖拽只写一次；无设置服务时整体退化为 no-op。
    /// </summary>
    public void SaveColumnWidths(IReadOnlyDictionary<string, double> widths)
    {
        var snapshot = SanitizeColumnWidths(widths);
        if (_settings is null || snapshot.Count == 0)
        {
            return;
        }

        CancelPendingColumnWidthSave();

        var cts = new CancellationTokenSource();
        _columnWidthsCts = cts;
        var version = ++_columnWidthsVersion;
        PendingColumnWidthSave = DelayThenSaveColumnWidthsAsync(snapshot, version, cts.Token);
    }

    /// <summary>跳过防抖立即保存列宽（供测试与窗口关闭时调用）。返回是否真的写成功。</summary>
    public bool PersistColumnWidths(IReadOnlyDictionary<string, double> widths)
    {
        var snapshot = SanitizeColumnWidths(widths);
        if (_settings is null || snapshot.Count == 0)
        {
            return false;
        }

        try
        {
            // 与已存的列宽合并，避免「只调一列」时把其它列的记忆抹掉。
            var merged = new Dictionary<string, double>(StringComparer.Ordinal);
            var existing = _settings.Current.ColumnWidths;
            if (existing is not null)
            {
                foreach (var pair in existing)
                {
                    if (IsSaneColumnWidth(pair.Value))
                    {
                        merged[pair.Key] = pair.Value;
                    }
                }
            }

            foreach (var pair in snapshot)
            {
                merged[pair.Key] = pair.Value;
            }

            var result = _settings.Save(_settings.Current with { ColumnWidths = merged });
            if (result.IsSuccess)
            {
                ColumnWidths = merged;
                return true;
            }

            // 落盘失败不打断交互（规格 05 篇：异常/失败不得穿透 UI）。
            _errors.Report("保存列宽", result.Message ?? "写入设置文件失败。");
            return false;
        }
        catch (Exception ex)
        {
            _errors.Report(ex, "保存列宽", "保存列宽时发生错误。");
            return false;
        }
    }

    private async Task DelayThenSaveColumnWidthsAsync(
        IReadOnlyDictionary<string, double> widths,
        int version,
        CancellationToken cancellationToken)
    {
        try
        {
            await Scheduler
                .DelayAsync(TimeSpan.FromMilliseconds(ColumnWidthSaveDebounceMilliseconds), cancellationToken)
                .ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
            return;
        }
        catch (Exception ex)
        {
            _errors.Report(ex, "列宽防抖", "保存列宽时发生错误。");
            return;
        }

        if (cancellationToken.IsCancellationRequested || version != _columnWidthsVersion)
        {
            // 期间又有列宽变化，交给最新一次调度。
            return;
        }

        PersistColumnWidths(widths);
    }

    private void CancelPendingColumnWidthSave()
    {
        _columnWidthsCts?.Cancel();
        _columnWidthsCts?.Dispose();
        _columnWidthsCts = null;
    }

    private static Dictionary<string, double> SanitizeColumnWidths(IReadOnlyDictionary<string, double>? widths)
    {
        var clean = new Dictionary<string, double>(StringComparer.Ordinal);
        if (widths is null)
        {
            return clean;
        }

        foreach (var pair in widths)
        {
            if (!string.IsNullOrWhiteSpace(pair.Key) && IsSaneColumnWidth(pair.Value))
            {
                clean[pair.Key] = pair.Value;
            }
        }

        return clean;
    }

    /// <summary>列宽合法性：有限数、非负、不超过 4000 DIP（防脏数据把界面撑坏）。</summary>
    private static bool IsSaneColumnWidth(double value) => double.IsFinite(value) && value is >= 0 and <= 4000;

    // ---- 防抖搜索（VM-01 验收：搜索延迟 ≤100ms）--------------------------

    private void CancelPendingSearch()
    {
        _searchCts?.Cancel();
        _searchCts?.Dispose();
        _searchCts = null;
    }

    private void ScheduleSearch()
    {
        CancelPendingSearch();

        var cts = new CancellationTokenSource();
        _searchCts = cts;
        var version = ++_searchVersion;
        PendingSearch = DelayThenApplyAsync(version, cts.Token);
    }

    private async Task DelayThenApplyAsync(int version, CancellationToken cancellationToken)
    {
        try
        {
            await Scheduler.DelayAsync(SearchDebounce, cancellationToken).ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
            return;
        }
        catch (Exception ex)
        {
            _errors.Report(ex, "搜索防抖", "搜索时发生错误。");
            return;
        }

        if (cancellationToken.IsCancellationRequested || version != _searchVersion)
        {
            // 期间又有输入，交给最新一次调度。
            return;
        }

        ApplyFilter();
    }

    partial void OnSearchTextChanged(string value)
    {
        ScheduleSearch();
        RefreshDerived();
    }

    partial void OnSelectedRowChanged(ModsRowView? value) => RefreshDetail();

    partial void OnInstallFilterChanged(ModsInstallFilter value)
    {
        var index = (int)value;
        if (InstallFilterIndex != index)
        {
            InstallFilterIndex = index;
        }

        ApplyFilter();
    }

    partial void OnInstallFilterIndexChanged(int value)
    {
        if (value is >= (int)ModsInstallFilter.All and <= (int)ModsInstallFilter.Upgradeable)
        {
            InstallFilter = (ModsInstallFilter)value;
        }
    }

    partial void OnCompatibilityFilterChanged(ModsCompatibilityFilter value)
    {
        var index = (int)value;
        if (CompatibilityFilterIndex != index)
        {
            CompatibilityFilterIndex = index;
        }

        ApplyFilter();
    }

    partial void OnCompatibilityFilterIndexChanged(int value)
    {
        if (value is >= (int)ModsCompatibilityFilter.All
            and <= (int)ModsCompatibilityFilter.IncompatibleOnly)
        {
            CompatibilityFilter = (ModsCompatibilityFilter)value;
        }
    }

    partial void OnSortColumnChanged(ModsSortColumn value)
    {
        var index = (int)value;
        if (SortColumnIndex != index)
        {
            SortColumnIndex = index;
        }

        ApplyFilter();
    }

    partial void OnSortColumnIndexChanged(int value)
    {
        if (value is >= (int)ModsSortColumn.Name and <= (int)ModsSortColumn.IsInstalled)
        {
            SortColumn = (ModsSortColumn)value;
        }
    }

    partial void OnSortAscendingChanged(bool value) => ApplyFilter();

    // ---- 命令 -------------------------------------------------------------

    /// <summary>刷新仓库索引与已装模组（Ctrl+R）。</summary>
    [RelayCommand]
    private async Task RefreshRepositoriesAsync()
    {
        if (IsBusy)
        {
            return;
        }

        IsBusy = true;
        StatusText = "正在刷新仓库索引…";
        try
        {
            var refreshed = await _repositories.RefreshAsync().ConfigureAwait(true);
            var registry = await _registry.RefreshAsync().ConfigureAwait(true);

            ReloadFromSnapshots();

            if (!refreshed.IsSuccess)
            {
                StatusText = refreshed.Message ?? "刷新仓库索引失败，已保留旧数据。";
                return;
            }

            if (!registry.IsSuccess)
            {
                StatusText = registry.Message ?? "刷新已安装模组失败。";
                return;
            }

            StatusText = $"已刷新：{ModsFormatter.Count(_all.Count)} 个模组";
        }
        catch (Exception ex)
        {
            _errors.Report(ex, "刷新仓库索引", "刷新模组索引时发生错误。");
            StatusText = "刷新失败，请查看错误栏。";
        }
        finally
        {
            IsBusy = false;
            RefreshDerived();
        }
    }

    /// <summary>清空搜索框（Esc / 按钮）。</summary>
    [RelayCommand]
    private void ClearSearchText() => ClearSearch();

    /// <summary>清空全部过滤条件（空状态按钮）。</summary>
    [RelayCommand]
    private void ResetFilters() => ClearFilters();

    /// <summary>切换升降序。</summary>
    [RelayCommand]
    private void ToggleSort() => ToggleSortDirection();

    /// <summary>勾选/取消勾选一行（行内复选框与 Space 键）。</summary>
    [RelayCommand]
    private void ToggleRowSelection(ModsRowView? row)
    {
        if (row is null)
        {
            return;
        }

        ToggleSelection(row.Identifier);
    }

    /// <summary>重新计算变更预览：有 IChangeSetService 时取权威结果，否则用本地估算。</summary>
    [RelayCommand]
    private async Task PreviewChangesAsync()
    {
        if (_selected.Count == 0 || _changeSets is null)
        {
            RebuildSelectionPreview();
            return;
        }

        try
        {
            var requests = BuildChangeRequests();
            var result = await _changeSets.BuildAsync(requests).ConfigureAwait(true);
            if (result.IsSuccess && result.Value is not null)
            {
                ChangePreview = result.Value;
            }
            else
            {
                StatusText = result.Message ?? "计算变更集失败。";
            }
        }
        catch (Exception ex)
        {
            _errors.Report(ex, "计算变更集", "计算变更集时发生错误。");
            StatusText = "计算变更集失败，请查看错误栏。";
        }
        finally
        {
            RefreshDerived();
        }
    }

    /// <summary>
    /// 「去变更集」：把当前勾选翻译成请求，灌进变更集页再导航过去（UI-04 交接）。
    /// 没有 sink 或导航时退化为只算一次预览，不影响勾选状态。
    /// </summary>
    [RelayCommand]
    private async Task ReviewChangesAsync()
    {
        var requests = BuildChangeRequests();
        if (requests.Count == 0)
        {
            StatusText = "请先勾选要变更的模组。";
            return;
        }

        _changeSink?.SetRequests(requests);

        if (_navigation is null)
        {
            StatusText = "当前没有可用的导航服务，无法打开变更集页。";
            return;
        }

        try
        {
            if (!await _navigation.NavigateAsync("changes").ConfigureAwait(true))
            {
                StatusText = "无法打开变更集页。";
            }
        }
        catch (Exception ex)
        {
            _errors.Report(ex, "打开变更集页", "打开变更集页时发生错误。");
            StatusText = "打开变更集页失败，请查看错误栏。";
        }
    }

    /// <summary>把勾选行翻译成 Core 无关的变更请求；无 IChangeSetService 时也可用（本地预览同款口径）。</summary>
    public List<ChangeRequest> BuildChangeRequests()
    {
        var requests = new List<ChangeRequest>(_selected.Count);
        foreach (var identifier in _selected)
        {
            var row = _all.FirstOrDefault(r => string.Equals(r.Identifier, identifier, StringComparison.OrdinalIgnoreCase));
            if (row.Identifier is null)
            {
                continue;
            }

            requests.Add(new ChangeRequest(identifier, ModsChangePreviewBuilder.ResolveAction(row), row.LatestVersion));
        }

        return requests;
    }

    /// <summary>应用变更（M1 仅占位：安装器在 M2 接入，见 INT-08）。</summary>
    [RelayCommand(CanExecute = nameof(HasPendingChanges))]
    private void ApplyChanges()
    {
        StatusText = "变更应用将在 M2 接入安装器后可用（INT-08）。";
    }

    // ---- 无服务时的空实现（设计器 / 无参构造）-----------------------------

    private sealed class EmptyRepositoryService : IRepositoryService
    {
        public RepositorySnapshot Current { get; } = RepositorySnapshot.Empty;

        public event Action<RepositorySnapshot>? Updated
        {
            add { }
            remove { }
        }

        public event Action<OperationProgress>? RefreshProgress
        {
            add { }
            remove { }
        }

        public Task<OperationResult<RepositorySnapshot>> LoadCachedAsync(CancellationToken ct = default) =>
            Task.FromResult(OperationResult<RepositorySnapshot>.Ok(RepositorySnapshot.Empty));

        public Task<OperationResult<RepositorySnapshot>> RefreshAsync(CancellationToken ct = default) =>
            Task.FromResult(OperationResult<RepositorySnapshot>.Fail("尚未连接仓库服务。"));
    }

    private sealed class EmptyRegistryService : IRegistryService
    {
        public RegistrySnapshot Current { get; } = RegistrySnapshot.Empty();

        public event Action<RegistrySnapshot>? Changed
        {
            add { }
            remove { }
        }

        public Task<OperationResult<RegistrySnapshot>> OpenAsync(InstanceInfo instance, CancellationToken ct = default) =>
            Task.FromResult(OperationResult<RegistrySnapshot>.Fail("尚未连接注册表服务。"));

        public Task<OperationResult<bool>> CloseAsync(CancellationToken ct = default) =>
            Task.FromResult(OperationResult<bool>.Ok(true));

        public Task<OperationResult<RegistrySnapshot>> RefreshAsync(CancellationToken ct = default) =>
            Task.FromResult(OperationResult<RegistrySnapshot>.Fail("尚未连接注册表服务。"));
    }

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
