using System.Collections.ObjectModel;
using CkanUI.Services.Abstractions.Logs;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace CkanUI.ViewModels.Pages.Logs;

/// <summary>
/// 底部日志抽屉（14 篇 §4.1）：所有页面可用、默认收起、只显示最近 200 行，收起时用徽标提示有多少错误/警告。
/// 为什么徽标只算「上次打开之后」的新增：常驻的 3 条历史警告如果把徽标钉死在「有 3 个警告」，
/// 用户就再也得不到「又出新问题」的信号。
/// </summary>
public sealed partial class LogDrawerViewModel : ObservableObject
{
    /// <summary>抽屉里的行数上限。</summary>
    public const int RecentRowLimit = 200;

    private readonly ILogStore _store;
    private readonly ILogQueryService _query;
    private readonly SynchronizationContext? _sync;
    private int _acknowledgedErrors;
    private int _acknowledgedWarnings;
    private bool _attached;

    /// <summary>无参构造用于设计器。</summary>
    public LogDrawerViewModel()
        : this(NullStore.Instance, NullQuery.Instance)
    {
    }

    /// <summary>正常构造：直接挂到缓冲与查询服务上（不依赖日志页实例，便于外壳独立实例化）。</summary>
    public LogDrawerViewModel(ILogStore store, ILogQueryService query, IKspLogTailService? gameLog = null)
        : this(store, query, new LogPageViewModel(
            store ?? throw new ArgumentNullException(nameof(store)),
            query ?? throw new ArgumentNullException(nameof(query)),
            gameLog: gameLog))
    {
    }

    private LogDrawerViewModel(ILogStore store, ILogQueryService query, LogPageViewModel page)
    {
        _store = store;
        _query = query;
        Page = page;
        _sync = SynchronizationContext.Current;

        ToggleCommand = new RelayCommand(Toggle);
        OpenCommand = new RelayCommand(Open);
        CloseCommand = new RelayCommand(Close);
        RefreshCommand = new RelayCommand(Refresh);
        OpenPageCommand = new RelayCommand(OpenPage);
    }

    /// <summary>抽屉背后的日志页视图模型（徽标与过滤状态都读它）。</summary>
    public LogPageViewModel Page { get; }

    /// <summary>抽屉里显示的最近若干行。</summary>
    public ObservableCollection<LogRowViewModel> RecentRows { get; } = new();

    /// <summary>是否展开。</summary>
    [ObservableProperty]
    private bool _isOpen;

    /// <summary>上次打开之后新增的错误数。</summary>
    [ObservableProperty]
    private int _newErrorCount;

    /// <summary>上次打开之后新增的警告数。</summary>
    [ObservableProperty]
    private int _newWarningCount;

    /// <summary>切换展开/收起。</summary>
    public IRelayCommand ToggleCommand { get; }

    /// <summary>展开（上拉半屏）。</summary>
    public IRelayCommand OpenCommand { get; }

    /// <summary>收起。</summary>
    public IRelayCommand CloseCommand { get; }

    /// <summary>刷新最近行与徽标计数。</summary>
    public IRelayCommand RefreshCommand { get; }

    /// <summary>请求跳到日志页（外壳负责导航）。</summary>
    public IRelayCommand OpenPageCommand { get; }

    /// <summary>「打开日志页」事件，由外壳导航到日志页。</summary>
    public event EventHandler? OpenPageRequested;

    /// <summary>是否有新错误或新警告（收起时显示徽标）。</summary>
    public bool HasBadge => NewErrorCount > 0 || NewWarningCount > 0;

    /// <summary>徽标是否可见：只在收起时提示，展开后用户已经看到了。</summary>
    public bool IsBadgeVisible => !IsOpen && HasBadge;

    /// <summary>徽标文案。</summary>
    public string BadgeText => NewErrorCount > 0
        ? string.Create(System.Globalization.CultureInfo.InvariantCulture, $"有 {NewErrorCount} 个错误")
        : NewWarningCount > 0
            ? string.Create(System.Globalization.CultureInfo.InvariantCulture, $"有 {NewWarningCount} 个警告")
            : string.Empty;

    /// <summary>抽屉标题。</summary>
    public string Title => string.Create(
        System.Globalization.CultureInfo.InvariantCulture,
        $"日志（最近 {RecentRows.Count} 行）");

    /// <summary>挂到缓冲变更通知上（外壳在窗口加载时调用一次）。</summary>
    public void Attach()
    {
        if (_attached)
        {
            return;
        }

        _store.Changed += OnStoreChanged;
        _attached = true;
        Refresh();
    }

    /// <summary>摘掉缓冲变更通知。</summary>
    public void Detach()
    {
        if (!_attached)
        {
            return;
        }

        _store.Changed -= OnStoreChanged;
        _attached = false;
    }

    /// <summary>重算最近行与徽标。</summary>
    public void Refresh()
    {
        var query = Page.Filters.BuildQuery(RecentRowLimit);
        var rows = new List<LogRowViewModel>();
        var errorCount = 0;
        var warningCount = 0;

        if (!Page.Filters.MatchesNothing)
        {
            foreach (var entry in _query.Query(query).Events)
            {
                rows.Add(new LogRowViewModel(entry));
            }

            var aggregate = _query.Aggregate(query, 1, 0);
            errorCount = aggregate.ErrorCount;
            warningCount = aggregate.WarningCount;
        }

        RecentRows.Replace(rows);
        NewErrorCount = Math.Max(0, errorCount - _acknowledgedErrors);
        NewWarningCount = Math.Max(0, warningCount - _acknowledgedWarnings);
        OnPropertyChanged(nameof(Title));
    }

    /// <summary>展开抽屉；<see cref="OnIsOpenChanged"/> 会把当前计数标记为「已看过」并清零徽标。</summary>
    public void Open()
    {
        Refresh();
        IsOpen = true;
    }

    /// <summary>收起抽屉。</summary>
    public void Close() => IsOpen = false;

    private void Toggle()
    {
        if (IsOpen)
        {
            Close();
        }
        else
        {
            Open();
        }
    }

    private void OpenPage() => OpenPageRequested?.Invoke(this, EventArgs.Empty);

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

        sync.Post(static state => ((LogDrawerViewModel)state!).RunCoalescedRefresh(), this);
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
                    sync.Post(static state => ((LogDrawerViewModel)state!).RunCoalescedRefresh(), this);
                }
            }
        }
    }

    partial void OnIsOpenChanged(bool value)
    {
        OnPropertyChanged(nameof(IsBadgeVisible));
        if (value)
        {
            _acknowledgedErrors += NewErrorCount;
            _acknowledgedWarnings += NewWarningCount;
            NewErrorCount = 0;
            NewWarningCount = 0;
        }
    }

    partial void OnNewErrorCountChanged(int value) => RaiseBadgeChanged();

    partial void OnNewWarningCountChanged(int value) => RaiseBadgeChanged();

    private void RaiseBadgeChanged()
    {
        OnPropertyChanged(nameof(HasBadge));
        OnPropertyChanged(nameof(IsBadgeVisible));
        OnPropertyChanged(nameof(BadgeText));
    }

    /// <summary>设计器占位：空缓冲。</summary>
    private sealed class NullStore : ILogStore
    {
        public static readonly NullStore Instance = new();

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

        public LogSnapshot Snapshot() => LogSnapshot.Empty;

        public void Clear()
        {
        }
    }

    /// <summary>设计器占位：空查询服务。</summary>
    private sealed class NullQuery : ILogQueryService
    {
        public static readonly NullQuery Instance = new();

        public LogQueryResult Query(LogQuery query) => LogQueryResult.Empty;

        public LogAggregateResult Aggregate(LogQuery query, int timelineBuckets = 60, int topFingerprints = 10) =>
            LogAggregateResult.Empty;

        public IReadOnlyList<LogFingerprintGroup> Fingerprints(LogQuery query, int take = 10) =>
            Array.Empty<LogFingerprintGroup>();

        public LogSnapshot Snapshot() => LogSnapshot.Empty;
    }
}
