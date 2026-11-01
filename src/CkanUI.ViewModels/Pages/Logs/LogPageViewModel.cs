using CkanUI.Localization;
using System.Collections.ObjectModel;
using CkanUI.Services.Abstractions;
using CkanUI.Services.Abstractions.Logs;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace CkanUI.ViewModels.Pages.Logs;

/// <summary>
/// 日志页（14 篇 §4.1 / 06 篇 UI-LOG-01）：全屏审查工作区，左侧虚拟化列表（或聚合视图），右侧详情面板，右缘时间轴。
/// 数据全部来自内存环形缓冲，查询是同步的，所以刷新也同步做——只有「导出诊断包」是异步 IO。
/// 为什么默认跟随末尾：排障时用户最关心「刚刚又出现了什么」，若不跟随会反复手动滚到底。
/// </summary>
public sealed partial class LogPageViewModel : PageViewModel, IActivatable
{
    /// <summary>列表一次取多少行（虚拟化列表的实际渲染量与查询量都按它算）。</summary>
    public const int RowLimit = 500;

    /// <summary>时间轴桶数。</summary>
    public const int TimelineBucketCount = 60;

    /// <summary>聚合视图最多显示多少组。</summary>
    public const int TopFingerprintCount = 20;

    private readonly ILogStore _store;
    private readonly ILogQueryService _query;
    private readonly ILogBundleService? _bundle;

    /// <summary>构造时捕获的同步上下文：log4net appender 来自任意工作线程，刷新必须切回 UI 线程。</summary>
    private readonly SynchronizationContext? _sync;

    private int _currentMatchIndex = -1;
    private bool _isRefreshing;
    private bool _needsRefresh;

    /// <summary>无参构造用于设计器与占位视图。</summary>
    public LogPageViewModel()
        : this(new EmptyLogStore(), new EmptyLogQueryService())
    {
    }

    /// <summary>正常构造（<paramref name="bundle"/> 为 null 时导出按钮会提示未接线）。</summary>
    public LogPageViewModel(ILogStore store, ILogQueryService query, ILogBundleService? bundle = null)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _query = query ?? throw new ArgumentNullException(nameof(query));
        _bundle = bundle;
        _sync = SynchronizationContext.Current;

        Filters = new LogFilterViewModel();
        Filters.Changed += OnFiltersChanged;

        RefreshCommand = new RelayCommand(Refresh);
        ToggleAggregateCommand = new RelayCommand(() => IsAggregated = !IsAggregated);
        ClearCommand = new RelayCommand(Clear);
        ShowOnlyErrorsCommand = new RelayCommand(() => Filters.ShowOnlyErrors());
        ResetFiltersCommand = new RelayCommand(() => Filters.Reset());
        ToggleFollowTailCommand = new RelayCommand(() => IsFollowTail = !IsFollowTail);
        FindNextCommand = new RelayCommand(() => MoveMatch(1));
        FindPreviousCommand = new RelayCommand(() => MoveMatch(-1));
        ExportBundleCommand = new AsyncRelayCommand<string>(outputPath => ExportBundleAsync(outputPath, CancellationToken.None));
        ExportDefaultCommand = new AsyncRelayCommand(() => ExportBundleAsync(BuildDefaultBundlePath(DateTime.Now), CancellationToken.None));
        RequestAiAnalysisCommand = new RelayCommand(RequestAiAnalysis);
        ActivateLinkCommand = new RelayCommand<LogObjectLink>(ActivateLink);
        DismissMessageCommand = new RelayCommand(() => Message = null);
    }

    /// <inheritdoc />
    public override string Title => Text("日志", LocalizationKeys.NavRailLogs);

    /// <summary>三段过滤条 + 搜索框。</summary>
    public LogFilterViewModel Filters { get; }

    /// <summary>主列表行（新→旧或旧→新，取决于跟随末尾）。</summary>
    public ObservableCollection<LogRowViewModel> Rows { get; } = new();

    /// <summary>聚合视图的分组。</summary>
    public ObservableCollection<LogFingerprintGroupViewModel> Groups { get; } = new();

    /// <summary>右缘时间轴柱。</summary>
    public ObservableCollection<LogTimelineBarViewModel> Timeline { get; } = new();

    /// <summary>是否聚合视图（连续相同指纹折叠 ×N）。</summary>
    [ObservableProperty]
    private bool _isAggregated;

    /// <summary>是否跟随末尾（新日志自动滚到最新、并显示最新一行）。</summary>
    [ObservableProperty]
    private bool _isFollowTail = true;

    /// <summary>跟随末尾开关显示文案。</summary>
    public string FollowTailText => IsFollowTail ? "跟随末尾：开" : "跟随末尾：关";

    /// <summary>聚合/顺序视图切换显示文案。</summary>
    public string AggregatedText => IsAggregated ? "视图：聚合" : "视图：顺序";

    /// <summary>是否正在导出诊断包。</summary>
    [ObservableProperty]
    private bool _isBusy;

    /// <summary>提示信息（导出结果、过滤提示、未接线说明）；null 表示没有。</summary>
    [ObservableProperty]
    private string? _message;

    /// <summary>当前选中的行（右侧详情面板的数据源）。</summary>
    [ObservableProperty]
    private LogRowViewModel? _selectedRow;

    /// <summary>当前命中在列表里的序号（F3 跳转，从 1 开始；0 表示还没跳转）。</summary>
    [ObservableProperty]
    private int _currentMatchOrdinal;

    /// <summary>命中总条数（可能大于 <see cref="RowLimit"/>）。</summary>
    [ObservableProperty]
    private int _totalMatches;

    /// <summary>错误条数（Error + Fatal）。</summary>
    [ObservableProperty]
    private int _errorCount;

    /// <summary>警告条数。</summary>
    [ObservableProperty]
    private int _warningCount;

    /// <summary>最近一次导出的诊断包路径。</summary>
    [ObservableProperty]
    private string? _exportedPath;

    /// <summary>AI 分析面板是否展开（真正的 AI 调用在 E9 接线，这里只负责状态）。</summary>
    [ObservableProperty]
    private bool _isAiPanelOpen;

    /// <summary>请求 AI 分析（携带导出包路径）；AI 未接线时由外部忽略。</summary>
    public event EventHandler<string>? AiAnalysisRequested;

    /// <summary>用户点击了日志行里的可联动对象（mod / instance / URL / 安装任务），由外壳负责导航。</summary>
    public event EventHandler<LogObjectLink>? ObjectLinkActivated;

    /// <summary>刷新列表。</summary>
    public IRelayCommand RefreshCommand { get; }

    /// <summary>切换聚合视图。</summary>
    public IRelayCommand ToggleAggregateCommand { get; }

    /// <summary>清空缓冲（序号不回退，证据行引用仍然唯一）。</summary>
    public IRelayCommand ClearCommand { get; }

    /// <summary>仅看错误。</summary>
    public IRelayCommand ShowOnlyErrorsCommand { get; }

    /// <summary>恢复默认过滤。</summary>
    public IRelayCommand ResetFiltersCommand { get; }

    /// <summary>切换跟随末尾。</summary>
    public IRelayCommand ToggleFollowTailCommand { get; }

    /// <summary>下一个命中（F3）。</summary>
    public IRelayCommand FindNextCommand { get; }

    /// <summary>上一个命中（Shift+F3）。</summary>
    public IRelayCommand FindPreviousCommand { get; }

    /// <summary>导出诊断包（参数是目标 zip 路径，由视图的文件选择器提供）。</summary>
    public IAsyncRelayCommand<string> ExportBundleCommand { get; }

    /// <summary>
    /// 导出诊断包到默认位置（与 log4net 日志目录同级，文件名带时间戳）。
    /// 为什么给一个不弹框的默认路径：「排障时点一下就出包」比每次都选目录更重要；
    /// 需要文件选择器时由外壳改调 <see cref="ExportBundleCommand"/> 并传入用户选的路径。
    /// </summary>
    public IAsyncRelayCommand ExportDefaultCommand { get; }

    /// <summary>默认诊断包路径：%LocalAppData%\CKAN\logs\ckanui-bundle-yyyyMMdd-HHmmss.zip。</summary>
    public static string BuildDefaultBundlePath(DateTime nowLocal) => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "CKAN",
        "logs",
        string.Create(
            System.Globalization.CultureInfo.InvariantCulture,
            $"ckanui-bundle-{nowLocal:yyyyMMdd-HHmmss}.zip"));

    /// <summary>请 AI 分析。</summary>
    public IRelayCommand RequestAiAnalysisCommand { get; }

    /// <summary>激活行内联动对象。</summary>
    public IRelayCommand<LogObjectLink> ActivateLinkCommand { get; }

    /// <summary>关掉提示条。</summary>
    public IRelayCommand DismissMessageCommand { get; }

    /// <summary>是否有选中行。</summary>
    public bool HasSelection => SelectedRow is not null;

    /// <summary>详情面板里的原文（含 Detail 异常栈）。</summary>
    public string? SelectedDetail => SelectedRow?.Detail;

    /// <summary>详情面板里的消息正文。</summary>
    public string SelectedMessage => SelectedRow?.Message ?? string.Empty;

    /// <summary>详情面板标题（序号 + 时间 + 级别 + 族）。</summary>
    public string SelectedHeader => SelectedRow is null
        ? Text("未选中日志行", LocalizationKeys.LogsNoSelection)
        : string.Create(
            System.Globalization.CultureInfo.InvariantCulture,
            $"#{SelectedRow.Seq} {SelectedRow.Entry.TimeLocal:yyyy-MM-dd HH:mm:ss.fff} {SelectedRow.LevelLabel} · {SelectedRow.FamilyLabel}");

    /// <summary>详情面板里的来源行。</summary>
    public string SelectedSourceText => SelectedRow?.SourceText ?? string.Empty;

    /// <summary>选中行里的可联动对象。</summary>
    public IReadOnlyList<LogObjectLink> SelectedLinks => SelectedRow is null
        ? Array.Empty<LogObjectLink>()
        : LogObjectLinks.DetectAll(SelectedRow.Message + "\n" + (SelectedRow.Detail ?? string.Empty));

    /// <summary>是否有临时提示消息。</summary>
    public bool HasMessage => !string.IsNullOrEmpty(Message);

    /// <summary>状态条文本。</summary>
    public string StatusText => Message ?? Text("命中 {0} 条（显示 {1}）· 错误 {2} · 警告 {3}", LocalizationKeys.LogsSummaryHits, TotalMatches, Rows.Count, ErrorCount, WarningCount);

    /// <summary>徽标文案（底部抽屉与页面共用）：有错误先报错误。</summary>
    public string BadgeText => ErrorCount > 0
        ? Text("有 {0} 个错误", LocalizationKeys.LogsSummaryErrors, ErrorCount)
        : WarningCount > 0
            ? Text("有 {0} 个警告", LocalizationKeys.LogsSummaryWarnings, WarningCount)
            : Text("没有新的错误或警告", LocalizationKeys.LogsSummaryClean);

    /// <summary>是否显示徽标（有错误或警告才显示）。</summary>
    public bool HasBadge => ErrorCount > 0 || WarningCount > 0;

    /// <summary>空状态：没有错误、没有提示、也没有行。</summary>
    public bool ShowEmptyState => !IsBusy && Rows.Count == 0;

    /// <summary>空状态文案（区分「过滤太严」与「确实没有日志」）。</summary>
    public string EmptyHint => Filters.MatchesNothing
        ? Text("当前过滤不命中，试试恢复默认。", LocalizationKeys.LogsEmptyFiltered)
        : Text("缓冲区还没有日志，采集到后自动出现。", LocalizationKeys.LogsEmptyNoLogs);

    /// <summary>聚合视图的提示。</summary>
    public string AggregateHint => IsAggregated
        ? Text("按「级别 + 族 + 消息骨架」折叠，取前 {0} 组", LocalizationKeys.LogsAggregatedHint, TopFingerprintCount)
        : Text("已按事件顺序展开", LocalizationKeys.LogsExpandedHint);

    /// <inheritdoc />
    protected override void OnActivated()
    {
        _store.Changed += OnStoreChanged;
        Refresh();
    }

    /// <inheritdoc />
    protected override void OnDeactivated()
    {
        _store.Changed -= OnStoreChanged;
    }

    /// <summary>按当前过滤条件重算列表、聚合与时间轴。</summary>
    public void Refresh()
    {
        if (_isRefreshing)
        {
            _needsRefresh = true;
            return;
        }

        _isRefreshing = true;
        try
        {
            var query = Filters.BuildQuery(RowLimit);
            if (Filters.MatchesNothing)
            {
                Rows.Replace(Array.Empty<LogRowViewModel>());
                Groups.Replace(Array.Empty<LogFingerprintGroupViewModel>());
                Timeline.Replace(Array.Empty<LogTimelineBarViewModel>());
                TotalMatches = 0;
                ErrorCount = 0;
                WarningCount = 0;
                SelectedRow = null;
                ResetMatchCursor();
                return;
            }

            var result = _query.Query(query);
            var aggregate = _query.Aggregate(query, TimelineBucketCount, TopFingerprintCount);
            var keyword = query.NormalizedText;

            var rows = new List<LogRowViewModel>(result.Events.Count);
            foreach (var entry in result.Events)
            {
                var row = new LogRowViewModel(entry);
                if (keyword.Length > 0)
                {
                    row.IsHighlighted = row.Matches(keyword);
                }

                rows.Add(row);
            }

            Rows.Replace(rows);
            Groups.Replace(aggregate.TopFingerprints.Select(group => new LogFingerprintGroupViewModel(group)));

            var maxTotal = aggregate.Timeline.Count == 0 ? 1 : aggregate.Timeline.Max(bucket => bucket.Total);
            Timeline.Replace(aggregate.Timeline.Select(bucket => new LogTimelineBarViewModel(bucket, maxTotal)));

            TotalMatches = result.TotalMatches;
            ErrorCount = aggregate.ErrorCount;
            WarningCount = aggregate.WarningCount;
            ResetMatchCursor();

            // 跟随末尾时选最新一行；否则尽量保持用户原来选中的那条（按 Seq 找回），
            // 这样点开详情面板去看异常栈时，后台每来一条新日志都不会把面板内容冲掉。
            SelectedRow = rows.Count == 0 ? null : PickSelection(rows);
        }
        catch (Exception ex)
        {
            Message = Text("日志读取失败：{0}", LocalizationKeys.LogsErrorReadFailed, ex.Message);
            Rows.Replace(Array.Empty<LogRowViewModel>());
            Groups.Replace(Array.Empty<LogFingerprintGroupViewModel>());
            Timeline.Replace(Array.Empty<LogTimelineBarViewModel>());
        }
        finally
        {
            _isRefreshing = false;
            RefreshDerived();
            if (_needsRefresh)
            {
                _needsRefresh = false;
                Refresh();
            }
        }
    }

    /// <summary>清空缓冲。</summary>
    public void Clear()
    {
        _store.Clear();
        Message = Text("已清空缓冲区（序号不回退，旧日志里的证据行引用仍然唯一）。", LocalizationKeys.LogsStatusCleared);
        Refresh();
    }

    /// <summary>导出诊断包；返回是否成功（视图用它决定要不要弹结果框）。</summary>
    public async Task<bool> ExportBundleAsync(string? outputPath, CancellationToken ct = default)
    {
        if (_bundle is null)
        {
            Message = Text("诊断包服务未接线，请让外壳注入 ILogBundleService。", LocalizationKeys.LogsErrorNoBundleService);
            return false;
        }

        if (string.IsNullOrWhiteSpace(outputPath))
        {
            Message = Text("请先选择诊断包的保存位置。", LocalizationKeys.LogsErrorNoBundlePath);
            return false;
        }

        IsBusy = true;
        Message = null;
        try
        {
            var result = await _bundle.CreateAsync(LogBundleRequest.For(outputPath), ct).ConfigureAwait(true);
            if (!result.IsSuccess || result.Value is null)
            {
                Message = result.Message ?? Text("导出诊断包失败。", LocalizationKeys.LogsErrorBundleFailed);
                ExportedPath = null;
                return false;
            }

            ExportedPath = result.Value.ZipPath;
            Message = Text("已导出诊断包：{0} 行日志、{1} 条错误，共 {2} 字节。", LocalizationKeys.LogsStatusBundleExported, result.Value.TailLineCount, result.Value.ErrorLineCount, result.Value.ByteLength);
            return true;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            Message = Text("导出诊断包失败：{0}", LocalizationKeys.LogsErrorBundleDetail, ex.Message);
            return false;
        }
        finally
        {
            IsBusy = false;
        }
    }

    partial void OnSelectedRowChanged(LogRowViewModel? value)
    {
        OnPropertyChanged(nameof(HasSelection));
        OnPropertyChanged(nameof(SelectedDetail));
        OnPropertyChanged(nameof(SelectedMessage));
        OnPropertyChanged(nameof(SelectedHeader));
        OnPropertyChanged(nameof(SelectedSourceText));
        OnPropertyChanged(nameof(SelectedLinks));
    }

    partial void OnMessageChanged(string? value)
    {
        OnPropertyChanged(nameof(StatusText));
        OnPropertyChanged(nameof(HasMessage));
    }

    partial void OnIsAggregatedChanged(bool value)
    {
        OnPropertyChanged(nameof(AggregateHint));
        OnPropertyChanged(nameof(AggregatedText));
    }

    partial void OnIsFollowTailChanged(bool value)
    {
        OnPropertyChanged(nameof(FollowTailText));
    }

    partial void OnIsBusyChanged(bool value) => OnPropertyChanged(nameof(ShowEmptyState));

    partial void OnErrorCountChanged(int value) => RaiseBadgeChanged();

    partial void OnWarningCountChanged(int value) => RaiseBadgeChanged();

    partial void OnTotalMatchesChanged(int value) => OnPropertyChanged(nameof(StatusText));

    private void RaiseBadgeChanged()
    {
        OnPropertyChanged(nameof(BadgeText));
        OnPropertyChanged(nameof(HasBadge));
        OnPropertyChanged(nameof(StatusText));
    }

    private void RefreshDerived()
    {
        OnPropertyChanged(nameof(HasSelection));
        OnPropertyChanged(nameof(SelectedDetail));
        OnPropertyChanged(nameof(SelectedMessage));
        OnPropertyChanged(nameof(SelectedHeader));
        OnPropertyChanged(nameof(SelectedSourceText));
        OnPropertyChanged(nameof(SelectedLinks));
        OnPropertyChanged(nameof(StatusText));
        OnPropertyChanged(nameof(BadgeText));
        OnPropertyChanged(nameof(HasBadge));
        OnPropertyChanged(nameof(ShowEmptyState));
        OnPropertyChanged(nameof(EmptyHint));
        OnPropertyChanged(nameof(AggregateHint));
    }

    private LogRowViewModel PickSelection(List<LogRowViewModel> rows)
    {
        if (!IsFollowTail && SelectedRow is { } current)
        {
            foreach (var row in rows)
            {
                if (row.Seq == current.Seq)
                {
                    return row;
                }
            }
        }

        return rows[0];
    }

    private void ResetMatchCursor()
    {
        _currentMatchIndex = -1;
        CurrentMatchOrdinal = 0;
    }

    private void MoveMatch(int direction)
    {
        if (Rows.Count == 0)
        {
            return;
        }

        var index = _currentMatchIndex < 0
            ? direction > 0 ? 0 : Rows.Count - 1
            : (_currentMatchIndex + direction + Rows.Count) % Rows.Count;

        if (_currentMatchIndex >= 0 && _currentMatchIndex < Rows.Count)
        {
            Rows[_currentMatchIndex].IsCurrentMatch = false;
        }

        _currentMatchIndex = index;
        Rows[index].IsCurrentMatch = true;
        SelectedRow = Rows[index];
        CurrentMatchOrdinal = index + 1;
    }

    private void RequestAiAnalysis()
    {
        if (ExportedPath is null)
        {
            Message = Text("请先导出诊断包：AI 分析只读打包并脱敏后的内容，不会直接读本机路径。", LocalizationKeys.LogsErrorNeedBundleFirst);
            return;
        }

        IsAiPanelOpen = true;
        AiAnalysisRequested?.Invoke(this, ExportedPath);
    }

    private void ActivateLink(LogObjectLink? link)
    {
        if (link is null)
        {
            return;
        }

        ObjectLinkActivated?.Invoke(this, link);
    }

    private void OnFiltersChanged(object? sender, EventArgs args) => Refresh();

    private int _refreshRequested;
    private int _refreshRunning;

    private void OnStoreChanged(object? sender, LogStoreChangedEventArgs args)
    {
        ScheduleRefresh();
    }

    private void ScheduleRefresh()
    {
        Interlocked.Exchange(ref _refreshRequested, 1);
        if (Interlocked.CompareExchange(ref _refreshRunning, 1, 0) != 0)
        {
            return;
        }

        var sync = _sync;
        if (sync is null)
        {
            RunCoalescedRefresh();
            return;
        }

        sync.Post(static state => ((LogPageViewModel)state!).RunCoalescedRefresh(), this);
    }

    private void RunCoalescedRefresh()
    {
        try
        {
            Interlocked.Exchange(ref _refreshRequested, 0);
            Refresh();
        }
        finally
        {
            Interlocked.Exchange(ref _refreshRunning, 0);
            if (Volatile.Read(ref _refreshRequested) != 0 && Interlocked.CompareExchange(ref _refreshRunning, 1, 0) == 0)
            {
                var sync = _sync;
                if (sync is null)
                {
                    RunCoalescedRefresh();
                }
                else
                {
                    sync.Post(static state => ((LogPageViewModel)state!).RunCoalescedRefresh(), this);
                }
            }
        }
    }

    /// <summary>设计器占位用的空缓冲。</summary>
    private sealed class EmptyLogStore : ILogStore
    {
        private readonly LogSnapshot _empty = LogSnapshot.Empty;

        public LogStoreOptions Options => LogStoreOptions.Default;

        public int Count => 0;

        public long LastSeq => 0;

        public long Version => 0;

        public event EventHandler<LogStoreChangedEventArgs>? Changed
        {
            add { }
            remove { }
        }

        public LogEvent Append(LogEvent draft) => draft;

        public int AppendRange(IEnumerable<LogEvent> drafts) => 0;

        public LogSnapshot Snapshot() => _empty;

        public void Clear()
        {
        }
    }

    /// <summary>设计器占位用的空查询服务。</summary>
    private sealed class EmptyLogQueryService : ILogQueryService
    {
        public LogQueryResult Query(LogQuery query) => LogQueryResult.Empty;

        public LogAggregateResult Aggregate(LogQuery query, int timelineBuckets = 60, int topFingerprints = 10) =>
            LogAggregateResult.Empty;

        public IReadOnlyList<LogFingerprintGroup> Fingerprints(LogQuery query, int take = 10) =>
            Array.Empty<LogFingerprintGroup>();

        public LogSnapshot Snapshot() => LogSnapshot.Empty;
    }
}
