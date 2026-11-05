using System.Collections.ObjectModel;
using System.Globalization;
using Avalonia.Media;
using CkanUI.Plugin.Rp1.Career;
using CkanUI.Ui.Charts;
using CkanUI.Services.Abstractions;
using CkanUI.Services.Abstractions.Dtos;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace CkanUI.Plugin.Rp1.UI;

/// <summary>
/// 「RP-1 生涯日志」页：读取当前实例的 RP-1 存档，把合同、里程碑、项目、人员与预算周期摊成时间线。
/// </summary>
/// <remarks>
/// 页面只通过 <see cref="IRp1CareerService"/> 读存档，自己不做文件/目录操作。
/// 解析 46 MB 的 persistent.sfs 可能要几百毫秒，所以服务调用一律放到线程池，界面只负责展示。
/// </remarks>
public sealed partial class Rp1CareerPageViewModel : ObservableObject, IPluginPage, IDisposable
{
    private readonly IInstanceService? _instances;
    private readonly IRp1CareerService? _career;
    // 曲线填充色住在 CareerSeriesBuilder 里（那一层是纯函数，能脱离视图单测）。

    private readonly List<Rp1CareerEvent> _allEvents = [];
    private IReadOnlyList<Rp1CareerPeriod> _periods = [];
    private IReadOnlyList<ChartSeries> _careerSeries = Array.Empty<ChartSeries>();
    private bool _hasCareerSeries;
    private string _careerSeriesSummary = string.Empty;
    private string _careerMetricKey = "funds";

    private Rp1SaveSummary? _selectedSave;
    private Rp1CareerEvent? _selectedEvent;
    private bool _started;
    private bool _disposed;
    private bool _suppressSelection;

    /// <summary>宿主 DI 的入口：只注册 IInstanceService 时，生涯服务由插件自己造。</summary>
    public Rp1CareerPageViewModel(IInstanceService instances)
        : this(instances, new Rp1CareerService())
    {
    }

    /// <summary>测试入口；两个依赖都允许为 null，页面会明确显示缺什么而不是构造失败。</summary>
    public Rp1CareerPageViewModel(IInstanceService? instances, IRp1CareerService? career)
    {
        _instances = instances;
        _career = career;

        if (_instances is not null)
        {
            _instances.CurrentChanged += OnCurrentChanged;
            _instances.InstancesChanged += OnInstancesChanged;
        }
    }

    public static Rp1CareerPageViewModel CreateForTests(IRp1CareerService? career, IInstanceService? instances = null)
        => new(instances, career);

    public ObservableCollection<Rp1SaveSummary> Saves { get; } = [];

    public ObservableCollection<Rp1CareerEvent> VisibleEvents { get; } = [];

    public ObservableCollection<Rp1CareerPeriodRow> Periods { get; } = [];

    /// <summary>曲线指标切换（资金 / 科技 / 信心 / 声望）。</summary>
    public ObservableCollection<Rp1CareerMetricOption> CareerMetricOptions { get; } =
    [
        new("funds", "资金", "Money", true),
        new("science", "科技", "Science", false),
        new("confidence", "信心", "Confidence", false),
        new("reputation", "声望", "Reputation", false),
    ];

    /// <summary>
    /// 生涯曲线（全量预算周期，按游戏内时间正序；横轴是期序号而不是日期——
    /// 期与期之间的真实间隔在存档里不等长，等距画点会假造节奏）。
    /// </summary>
    public IReadOnlyList<ChartSeries> CareerSeries
    {
        get => _careerSeries;
        private set
        {
            if (SetProperty(ref _careerSeries, value))
            {
                OnPropertyChanged(nameof(HasCareerSeries));
            }
        }
    }

    /// <summary>是否有可画的生涯曲线。</summary>
    public bool HasCareerSeries => _hasCareerSeries && _careerSeries.Count > 0;

    /// <summary>曲线数据说明（期数、取值口径、单位）。</summary>
    public string CareerSeriesSummary
    {
        get => _careerSeriesSummary;
        private set => SetProperty(ref _careerSeriesSummary, value);
    }

    /// <summary>当前曲线指标键（funds / science / confidence / reputation）。</summary>
    public string CareerMetricKey
    {
        get => _careerMetricKey;
        private set
        {
            if (SetProperty(ref _careerMetricKey, value))
            {
                OnPropertyChanged(nameof(CareerSeriesSuffix));
            }
        }
    }

    /// <summary>曲线 Y 轴单位后缀。</summary>
    public string CareerSeriesSuffix
    {
        get => _careerMetricKey switch
        {
            "science" => " 点",
            "confidence" => string.Empty,
            "reputation" => string.Empty,
            _ => " 资金",
        };
    }

    public IReadOnlyList<string> FilterOptions { get; } = ["全部", "里程碑", "合同", "项目", "人员"];

    [ObservableProperty]
    private string _instanceName = "未选择实例";

    [ObservableProperty]
    private string _instanceGameDir = "—";

    [ObservableProperty]
    private int _selectedTabIndex;

    [ObservableProperty]
    private string _selectedFilter = "全部";

    public bool IsFilterAll => SelectedFilter == "全部";
    public bool IsFilterMilestone => SelectedFilter == "里程碑";
    public bool IsFilterContract => SelectedFilter == "合同";
    public bool IsFilterProgram => SelectedFilter == "项目";
    public bool IsFilterCrew => SelectedFilter == "人员";

    public string TotalEventCountText => _allEvents.Count.ToString(CultureInfo.InvariantCulture);
    public string AllEventsCountText => _allEvents.Count.ToString(CultureInfo.InvariantCulture);
    public string CrewEventsCountText => _allEvents.Count(e => e.Kind is Rp1EventKind.CrewRetired or Rp1EventKind.CrewLost).ToString(CultureInfo.InvariantCulture);

    public bool HasPreviousEvent => SelectedEvent is not null && VisibleEvents.IndexOf(SelectedEvent) > 0;
    public bool HasNextEvent => SelectedEvent is not null && VisibleEvents.IndexOf(SelectedEvent) >= 0 && VisibleEvents.IndexOf(SelectedEvent) < VisibleEvents.Count - 1;

    [ObservableProperty]
    private string _searchText = string.Empty;

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    private string _errorMessage = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasStatusMessage))]
    private string _statusMessage = string.Empty;

    [ObservableProperty]
    private string _emptyTitle = "正在读取生涯日志…";

    [ObservableProperty]
    private string _emptyDetail = string.Empty;

    [ObservableProperty]
    private bool _showEmptyState = true;

    [ObservableProperty]
    private bool _showContent;

    [ObservableProperty]
    private string _saveCountText = "0 个存档";

    [ObservableProperty]
    private string _eventCountText = "0 条事件";

    [ObservableProperty]
    private string _rp1VersionText = "—";

    [ObservableProperty]
    private string _kspVersionText = "—";

    [ObservableProperty]
    private string _currentDateText = "—";

    [ObservableProperty]
    private string _savePathText = "—";

    [ObservableProperty]
    private string _contractCountText = "0";

    [ObservableProperty]
    private string _milestoneCountText = "0";

    [ObservableProperty]
    private string _programCountText = "0";

    [ObservableProperty]
    private string _crewRetiredCountText = "0";

    [ObservableProperty]
    private string _crewLostCountText = "0";

    [ObservableProperty]
    private string _crewCountText = "0 / 0";

    [ObservableProperty]
    private string _currentFundsText = "—";

    [ObservableProperty]
    private string _currentScienceText = "—";

    [ObservableProperty]
    private string _currentConfidenceText = "—";

    [ObservableProperty]
    private string _currentReputationText = "—";

    [ObservableProperty]
    private string _periodCountText = "0 期";

    [ObservableProperty]
    private string _diagnosticsText = string.Empty;

    [ObservableProperty]
    private bool _hasDiagnostics;

    [ObservableProperty]
    private int _linkedPeriodIndex = -1;

    [ObservableProperty]
    private Rp1CareerPeriod? _linkedPeriod;

    [ObservableProperty]
    private string? _linkedEventLabel;

    [ObservableProperty]
    private string _periodScope = "recent";

    public bool HasLinkedPeriod => LinkedPeriod is not null;

    public bool IsRecentPeriodScope => PeriodScope == "recent";
    public bool IsLinkedPeriodScope => PeriodScope == "linked";
    public bool IsAllPeriodScope => PeriodScope == "all";

    public string LinkedPeriodText => LinkedPeriod is null
        ? string.Empty
        : $"第 {LinkedPeriod.Index + 1} 期 ({LinkedPeriod.DateText})";

    public string LinkedPeriodFundsText => LinkedPeriod?.Funds is null
        ? "—"
        : FormatMoney(LinkedPeriod.Funds);

    public string LinkedPeriodScienceText => LinkedPeriod?.ScienceEarned is null
        ? "—"
        : LinkedPeriod.ScienceEarned.Value.ToString("0.0", CultureInfo.InvariantCulture);

    public string LinkedPeriodConfidenceText => LinkedPeriod?.Confidence is null
        ? "—"
        : LinkedPeriod.Confidence.Value.ToString("0", CultureInfo.InvariantCulture);

    public string LinkedPeriodReputationText => LinkedPeriod?.Reputation is null
        ? "—"
        : LinkedPeriod.Reputation.Value.ToString("0.0", CultureInfo.InvariantCulture);

    public bool HasError => ErrorMessage.Length > 0;

    public bool HasStatusMessage => StatusMessage.Length > 0;

    public bool HasNoVisibleEvents => ShowContent && VisibleEvents.Count == 0;

    public bool ShowEventDetail => SelectedEvent is not null;

    public bool ShowEventPlaceholder => ShowContent && SelectedEvent is null;

    public string SelectedDetail => SelectedEvent?.Detail is { Length: > 0 } detail
        ? detail
        : SelectedEvent?.Subtitle ?? "这个事件没有更多说明。";

    public string SelectedMeta
    {
        get
        {
            if (SelectedEvent is null)
            {
                return string.Empty;
            }

            var parts = new List<string>();
            if (SelectedEvent.InternalName.Length > 0)
            {
                parts.Add(SelectedEvent.InternalName);
            }

            if (SelectedEvent.Meta is { Length: > 0 } meta)
            {
                parts.Add(meta);
            }

            return string.Join(" · ", parts);
        }
    }

    /// <summary>当前存档；切换时自动重读时间线。刷新期间由 <see cref="_suppressSelection"/> 抑制重复读取。</summary>
    public Rp1SaveSummary? SelectedSave
    {
        get => _selectedSave;
        set
        {
            if (!SetProperty(ref _selectedSave, value) || _suppressSelection || _disposed)
            {
                return;
            }

            _ = LoadSelectedAsync();
        }
    }

    public Rp1CareerEvent? SelectedEvent
    {
        get => _selectedEvent;
        set
        {
            if (SetProperty(ref _selectedEvent, value))
            {
                OnPropertyChanged(nameof(ShowEventDetail));
                OnPropertyChanged(nameof(ShowEventPlaceholder));
                OnPropertyChanged(nameof(SelectedDetail));
                OnPropertyChanged(nameof(SelectedMeta));
                OnPropertyChanged(nameof(HasPreviousEvent));
                OnPropertyChanged(nameof(HasNextEvent));
                UpdateLinkedPeriod(value);
            }
        }
    }

    public void Start()
    {
        if (_started || _disposed)
        {
            return;
        }

        _started = true;
        ApplyInstance(_instances?.Current);
        _ = RefreshAsync();
    }

    public void Stop()
    {
        _started = false;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _started = false;

        if (_instances is not null)
        {
            _instances.CurrentChanged -= OnCurrentChanged;
            _instances.InstancesChanged -= OnInstancesChanged;
        }
    }

    [RelayCommand]
    private async Task RefreshAsync()
    {
        if (_disposed || IsBusy)
        {
            return;
        }

        if (_career is null)
        {
            SetEmpty("生涯服务没有接上", "插件内部错误：没有创建 RP-1 生涯服务。");
            return;
        }

        var instance = _instances?.Current;
        ApplyInstance(instance);
        if (instance is null || string.IsNullOrWhiteSpace(instance.GameDir))
        {
            SetEmpty("没有选择游戏实例", "先在「实例」里选一个装了 RP-1 的 KSP 实例，再回来看生涯日志。");
            return;
        }

        var rp1Directory = Path.Combine(instance.GameDir, "GameData", "RP-1");
        if (!Directory.Exists(rp1Directory))
        {
            SetEmpty("这个实例没有安装 RP-1", "找不到 GameData/RP-1。这个页面读的是 RP-1 的存档场景，不是原版生涯。");
            return;
        }

        IsBusy = true;
        ErrorMessage = string.Empty;
        StatusMessage = "正在扫描存档…";
        try
        {
            var previousName = SelectedSave?.Name;
            var result = await Task.Run(() => _career.ListSavesAsync(instance.GameDir)).ConfigureAwait(true);
            if (!result.IsSuccess || result.Value is null)
            {
                SetEmpty("读不到存档目录", result.Message ?? "saves 目录不可读。");
                return;
            }

            Saves.Clear();
            foreach (var save in result.Value)
            {
                Saves.Add(save);
            }

            SaveCountText = $"{Saves.Count} 个存档";
            if (Saves.Count == 0)
            {
                SetEmpty("没有找到存档", $"在 {Path.Combine(instance.GameDir, "saves")} 下没有 persistent.sfs。先创建一个生涯存档。");
                return;
            }

            _suppressSelection = true;
            SelectedSave = Saves.FirstOrDefault(save => string.Equals(save.Name, previousName, StringComparison.OrdinalIgnoreCase))
                           ?? Saves[0];
            _suppressSelection = false;

            ShowEmptyState = false;
            ShowContent = true;
            StatusMessage = "已找到 " + Saves.Count + " 个存档。";
        }
        catch (Exception ex)
        {
            SetEmpty("扫描存档失败", ex.Message);
        }
        finally
        {
            IsBusy = false;
        }

        if (SelectedSave is not null)
        {
            await LoadSelectedAsync().ConfigureAwait(true);
        }
    }

    private async Task LoadSelectedAsync()
    {
        if (_disposed || IsBusy || SelectedSave is null)
        {
            return;
        }

        if (_career is null)
        {
            SetEmpty("生涯服务没有接上", "插件内部错误：没有创建 RP-1 生涯服务。");
            return;
        }

        var instance = _instances?.Current;
        if (instance is null || string.IsNullOrWhiteSpace(instance.GameDir))
        {
            SetEmpty("没有选择游戏实例", "先在「实例」里选一个装了 RP-1 的 KSP 实例。");
            return;
        }

        var save = SelectedSave;
        IsBusy = true;
        ErrorMessage = string.Empty;
        StatusMessage = "正在读取 " + save.Name + " …";
        try
        {
            var result = await Task.Run(() => _career.LoadAsync(
                instance.GameDir,
                save.Name,
                instance.Name,
                instance.GameVersion)).ConfigureAwait(true);

            if (!result.IsSuccess || result.Value is null)
            {
                SetEmpty("读不出这个存档", result.Message ?? "persistent.sfs 解析失败。");
                return;
            }

            ApplyTimeline(result.Value);
        }
        catch (Exception ex)
        {
            SetEmpty("读取生涯日志失败", ex.Message);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void ApplyTimeline(Rp1CareerTimeline timeline)
    {
        _allEvents.Clear();
        _allEvents.AddRange(timeline.Events);

        Rp1VersionText = timeline.Rp1Version is { Length: > 0 } version ? version : "未知";
        KspVersionText = timeline.GameVersion is { Length: > 0 } ksp ? ksp : "未知";
        CurrentDateText = timeline.CurrentDateText;
        SavePathText = timeline.SavePath;
        ContractCountText = timeline.ContractCount.ToString(CultureInfo.InvariantCulture);
        MilestoneCountText = timeline.MilestoneCount.ToString(CultureInfo.InvariantCulture);
        ProgramCountText = timeline.ProgramCount.ToString(CultureInfo.InvariantCulture);
        CrewRetiredCountText = timeline.CrewRetiredCount.ToString(CultureInfo.InvariantCulture);
        CrewLostCountText = timeline.CrewLostCount.ToString(CultureInfo.InvariantCulture);
        CrewCountText = timeline.CrewRetiredCount.ToString(CultureInfo.InvariantCulture) + " / " + timeline.CrewLostCount.ToString(CultureInfo.InvariantCulture);
        CurrentFundsText = FormatMoney(timeline.CurrentFunds);
        CurrentScienceText = FormatNumber(timeline.CurrentScience, "0.0");
        CurrentConfidenceText = FormatNumber(timeline.CurrentConfidence, "0");
        CurrentReputationText = FormatNumber(timeline.CurrentReputation, "0.0");
        PeriodCountText = timeline.Periods.Count + " 期";
        UpdateMetricOptionValues();

        _periods = timeline.Periods.OrderBy(static period => period.StartUt).ToArray();
        PopulatePeriods();
        BuildCareerSeries();

        HasDiagnostics = timeline.Diagnostics.Count > 0;
        DiagnosticsText = HasDiagnostics ? string.Join("\n", timeline.Diagnostics) : string.Empty;

        ShowEmptyState = false;
        ShowContent = true;
        StatusMessage = timeline.Events.Count == 0
            ? "存档读到了，但还没有生涯事件。"
            : $"已读取 {timeline.Events.Count} 条事件。";
        ApplyFilter();
        UpdateLinkedPeriod(SelectedEvent);
    }

    private void BuildCareerSeries()
    {
        var result = CareerSeriesBuilder.Build(_periods, CareerMetricKey, CurrentMetricLabel());
        _hasCareerSeries = result.HasData;
        CareerSeries = result.Series;
        CareerSeriesSummary = result.Summary;
        OnPropertyChanged(nameof(HasCareerSeries));
    }

    private void UpdateMetricOptionValues()
    {
        foreach (var option in CareerMetricOptions)
        {
            option.ValueText = option.Key switch
            {
                "funds" => CurrentFundsText,
                "science" => CurrentScienceText,
                "confidence" => CurrentConfidenceText,
                "reputation" => CurrentReputationText,
                _ => string.Empty,
            };
        }
    }

    private string CurrentMetricLabel()
        => CareerMetricOptions.FirstOrDefault(option => option.Key == CareerMetricKey)?.Label ?? "指标";

    [RelayCommand]
    private void SelectCareerMetric(string? key)
    {
        if (string.IsNullOrWhiteSpace(key) || key == _careerMetricKey)
        {
            return;
        }

        CareerMetricKey = key;
        foreach (var option in CareerMetricOptions)
        {
            option.IsSelected = string.Equals(option.Key, key, StringComparison.Ordinal);
        }

        BuildCareerSeries();
    }

    private void ApplyFilter()
    {
        if (!ShowContent)
        {
            return;
        }

        var kind = SelectedFilter switch
        {
            "里程碑" => Rp1EventKind.Milestone,
            "合同" => Rp1EventKind.Contract,
            "项目" => (Rp1EventKind?)null,
            "人员" => (Rp1EventKind?)null,
            _ => (Rp1EventKind?)null,
        };

        var query = SearchText.Trim();
        VisibleEvents.Clear();

        foreach (var item in _allEvents)
        {
            if (kind is not null && item.Kind != kind)
            {
                continue;
            }

            if (SelectedFilter == "项目" && item.Kind is not (Rp1EventKind.ProgramStart or Rp1EventKind.ProgramCompleted))
            {
                continue;
            }

            if (SelectedFilter == "人员" && item.Kind is not (Rp1EventKind.CrewRetired or Rp1EventKind.CrewLost))
            {
                continue;
            }

            if (query.Length > 0 && !Matches(item, query))
            {
                continue;
            }

            VisibleEvents.Add(item);
        }

        EventCountText = $"{VisibleEvents.Count} / {_allEvents.Count} 条事件";
        OnPropertyChanged(nameof(HasNoVisibleEvents));
        OnPropertyChanged(nameof(TotalEventCountText));
        OnPropertyChanged(nameof(AllEventsCountText));
        OnPropertyChanged(nameof(CrewEventsCountText));

        if (SelectedEvent is null || !VisibleEvents.Contains(SelectedEvent))
        {
            SelectedEvent = VisibleEvents.FirstOrDefault();
        }
        else
        {
            OnPropertyChanged(nameof(HasPreviousEvent));
            OnPropertyChanged(nameof(HasNextEvent));
        }
    }

    private static bool Matches(Rp1CareerEvent item, string query)
        => item.Title.Contains(query, StringComparison.OrdinalIgnoreCase)
           || item.Subtitle?.Contains(query, StringComparison.OrdinalIgnoreCase) == true
           || item.Detail?.Contains(query, StringComparison.OrdinalIgnoreCase) == true
           || item.InternalName.Contains(query, StringComparison.OrdinalIgnoreCase);

    private void SetEmpty(string title, string detail)
    {
        EmptyTitle = title;
        EmptyDetail = detail;
        ShowEmptyState = true;
        ShowContent = false;
        StatusMessage = string.Empty;
        _allEvents.Clear();
        VisibleEvents.Clear();
        Periods.Clear();
        _periods = [];
        _hasCareerSeries = false;
        CareerSeries = Array.Empty<ChartSeries>();
        CareerSeriesSummary = "还没有读到存档，不画曲线";
        LinkedPeriodIndex = -1;
        LinkedPeriod = null;
        LinkedEventLabel = null;
        NotifyLinkedPeriodProperties();
        SelectedEvent = null;
        EventCountText = "0 条事件";
        foreach (var option in CareerMetricOptions)
        {
            option.ValueText = string.Empty;
        }
        OnPropertyChanged(nameof(HasNoVisibleEvents));
        OnPropertyChanged(nameof(HasCareerSeries));
        OnPropertyChanged(nameof(TotalEventCountText));
        OnPropertyChanged(nameof(HasPreviousEvent));
        OnPropertyChanged(nameof(HasNextEvent));
    }

    private void ApplyInstance(InstanceInfo? instance)
    {
        InstanceName = instance?.DisplayName ?? "未选择实例";
        InstanceGameDir = instance?.GameDir is { Length: > 0 } dir ? dir : "—";
    }

    private void OnCurrentChanged(InstanceInfo? instance)
    {
        ApplyInstance(instance);
        if (_started)
        {
            _ = RefreshAsync();
        }
    }

    private void OnInstancesChanged()
    {
        if (_started)
        {
            _ = RefreshAsync();
        }
    }

    partial void OnSearchTextChanged(string value) => ApplyFilter();

    partial void OnSelectedFilterChanged(string value)
    {
        OnPropertyChanged(nameof(IsFilterAll));
        OnPropertyChanged(nameof(IsFilterMilestone));
        OnPropertyChanged(nameof(IsFilterContract));
        OnPropertyChanged(nameof(IsFilterProgram));
        OnPropertyChanged(nameof(IsFilterCrew));
        ApplyFilter();
    }

    [RelayCommand]
    public void SelectFilter(string? filter)
    {
        if (string.IsNullOrWhiteSpace(filter)) return;
        var normalized = filter switch
        {
            "Milestone" => "里程碑",
            "Contract" => "合同",
            "Program" => "项目",
            "Crew" => "人员",
            "All" => "全部",
            _ => filter,
        };
        SelectedFilter = normalized;
    }

    [RelayCommand]
    public void FilterByStat(string? filter)
    {
        SelectedTabIndex = 0;
        if (string.IsNullOrWhiteSpace(filter)) return;
        var normalized = filter switch
        {
            "Milestone" => "里程碑",
            "Contract" => "合同",
            "Program" => "项目",
            "Crew" => "人员",
            "All" => "全部",
            _ => filter,
        };
        SelectedFilter = normalized;
    }

    [RelayCommand]
    public void OpenMetricCurve(string? metricKey)
    {
        SelectedTabIndex = 1;
        if (!string.IsNullOrWhiteSpace(metricKey))
        {
            SelectCareerMetric(metricKey);
        }
    }

    [RelayCommand]
    public void SelectPreviousEvent()
    {
        if (SelectedEvent is null || VisibleEvents.Count == 0) return;
        var index = VisibleEvents.IndexOf(SelectedEvent);
        if (index > 0)
        {
            SelectedEvent = VisibleEvents[index - 1];
        }
    }

    [RelayCommand]
    public void SelectNextEvent()
    {
        if (SelectedEvent is null || VisibleEvents.Count == 0) return;
        var index = VisibleEvents.IndexOf(SelectedEvent);
        if (index >= 0 && index + 1 < VisibleEvents.Count)
        {
            SelectedEvent = VisibleEvents[index + 1];
        }
    }

    [RelayCommand]
    public void JumpToEarliest()
    {
        if (VisibleEvents.Count > 0)
        {
            SelectedEvent = VisibleEvents[0];
        }
    }

    [RelayCommand]
    public void JumpToLatest()
    {
        if (VisibleEvents.Count > 0)
        {
            SelectedEvent = VisibleEvents[^1];
        }
    }

    [RelayCommand]
    public void JumpToMilestone()
    {
        var milestones = VisibleEvents.Where(e => e.Kind == Rp1EventKind.Milestone).ToList();
        if (milestones.Count == 0) return;
        if (SelectedEvent is null)
        {
            SelectedEvent = milestones[0];
            return;
        }
        var index = milestones.IndexOf(SelectedEvent);
        if (index >= 0 && index + 1 < milestones.Count)
        {
            SelectedEvent = milestones[index + 1];
        }
        else
        {
            SelectedEvent = milestones[0];
        }
    }

    [RelayCommand]
    public void JumpToPeriodIndex(int periodIndex)
    {
        if (_periods.Count == 0 || periodIndex < 0 || periodIndex >= _periods.Count)
        {
            return;
        }

        var period = _periods[periodIndex];
        LinkedPeriodIndex = periodIndex;
        LinkedPeriod = period;

        var matched = FindEventInOrNearPeriod(period);
        if (matched is not null)
        {
            _selectedEvent = matched;
            OnPropertyChanged(nameof(SelectedEvent));
            OnPropertyChanged(nameof(ShowEventDetail));
            OnPropertyChanged(nameof(ShowEventPlaceholder));
            OnPropertyChanged(nameof(SelectedDetail));
            OnPropertyChanged(nameof(SelectedMeta));
            OnPropertyChanged(nameof(HasPreviousEvent));
            OnPropertyChanged(nameof(HasNextEvent));
            LinkedEventLabel = $"{matched.DateText} · {matched.Title}";
        }

        UpdatePeriodRowsSelection();
        NotifyLinkedPeriodProperties();
        if (PeriodScope == "linked")
        {
            PopulatePeriods();
        }
    }

    [RelayCommand]
    public void SelectPeriod(Rp1CareerPeriodRow? row)
    {
        if (row is null) return;
        var idx = row.Period.Index;
        if (idx < 0 || idx >= _periods.Count || !ReferenceEquals(_periods[idx], row.Period))
        {
            idx = -1;
            for (var i = 0; i < _periods.Count; i++)
            {
                if (ReferenceEquals(_periods[i], row.Period) || _periods[i].Index == row.Period.Index)
                {
                    idx = i;
                    break;
                }
            }
        }

        if (idx >= 0)
        {
            JumpToPeriodIndex(idx);
        }
    }

    public Rp1CareerEvent? FindEventInOrNearPeriod(Rp1CareerPeriod period)
    {
        var pool = VisibleEvents.Count > 0 ? (IReadOnlyList<Rp1CareerEvent>)VisibleEvents : _allEvents;
        if (pool.Count == 0) return null;

        Rp1CareerEvent? bestInside = null;
        foreach (var evt in pool)
        {
            if (evt.Ut is { } evtUt && evtUt >= period.StartUt && evtUt <= period.EndUt)
            {
                if (evt.IsMilestone) return evt;
                bestInside ??= evt;
            }
        }
        if (bestInside is not null) return bestInside;

        if (VisibleEvents.Count > 0 && _allEvents.Count > 0)
        {
            foreach (var evt in _allEvents)
            {
                if (evt.Ut is { } evtUt && evtUt >= period.StartUt && evtUt <= period.EndUt)
                {
                    if (evt.IsMilestone) return evt;
                    bestInside ??= evt;
                }
            }
            if (bestInside is not null) return bestInside;
        }

        Rp1CareerEvent? closest = null;
        var minDiff = double.MaxValue;
        foreach (var evt in pool)
        {
            if (evt.Ut is not { } evtUt) continue;
            var diff = Math.Abs(evtUt - period.StartUt);
            if (diff < minDiff)
            {
                minDiff = diff;
                closest = evt;
            }
        }
        return closest;
    }

    [RelayCommand]
    public void OpenCareerCurveForCurrentEvent()
    {
        SelectedTabIndex = 1;
        if (LinkedPeriodIndex >= 0)
        {
            SetPeriodScope("linked");
        }
    }

    [RelayCommand]
    public void JumpToSelectedEvent()
    {
        SelectedTabIndex = 0;
    }

    [RelayCommand]
    public void SetPeriodScope(string? scope)
    {
        PeriodScope = scope switch
        {
            "linked" => "linked",
            "all" => "all",
            _ => "recent",
        };
        OnPropertyChanged(nameof(IsRecentPeriodScope));
        OnPropertyChanged(nameof(IsLinkedPeriodScope));
        OnPropertyChanged(nameof(IsAllPeriodScope));
        PopulatePeriods();
    }

    public void UpdateLinkedPeriod(Rp1CareerEvent? evt)
    {
        if (evt?.Ut is not { } ut || _periods.Count == 0)
        {
            LinkedPeriodIndex = -1;
            LinkedPeriod = null;
            LinkedEventLabel = null;
            UpdatePeriodRowsSelection();
            NotifyLinkedPeriodProperties();
            return;
        }

        var idx = FindPeriodIndexForUt(ut);
        LinkedPeriodIndex = idx;
        LinkedPeriod = idx >= 0 && idx < _periods.Count ? _periods[idx] : null;
        LinkedEventLabel = $"{evt.DateText} · {evt.Title}";
        UpdatePeriodRowsSelection();
        NotifyLinkedPeriodProperties();
        if (PeriodScope == "linked")
        {
            PopulatePeriods();
        }
    }

    public int FindPeriodIndexForUt(double ut)
    {
        if (_periods.Count == 0) return -1;
        if (ut <= _periods[0].StartUt) return 0;
        if (ut >= _periods[^1].StartUt) return _periods.Count - 1;

        var low = 0;
        var high = _periods.Count - 1;
        var best = 0;
        while (low <= high)
        {
            var mid = (low + high) / 2;
            if (_periods[mid].StartUt <= ut)
            {
                best = mid;
                low = mid + 1;
            }
            else
            {
                high = mid - 1;
            }
        }
        return best;
    }

    private void PopulatePeriods()
    {
        Periods.Clear();
        if (_periods.Count == 0) return;

        IEnumerable<Rp1CareerPeriod> source = PeriodScope switch
        {
            "linked" when LinkedPeriodIndex >= 0 =>
                _periods.Skip(Math.Max(0, LinkedPeriodIndex - 5))
                        .Take(11)
                        .Reverse(),
            "all" => _periods.Reverse(),
            _ => _periods.TakeLast(12).Reverse(),
        };

        foreach (var period in source)
        {
            var row = new Rp1CareerPeriodRow(period)
            {
                IsSelected = LinkedPeriodIndex >= 0 && period.Index == LinkedPeriodIndex
            };
            Periods.Add(row);
        }
    }

    private void UpdatePeriodRowsSelection()
    {
        foreach (var row in Periods)
        {
            row.IsSelected = LinkedPeriodIndex >= 0 && row.Period.Index == LinkedPeriodIndex;
        }
    }

    private void NotifyLinkedPeriodProperties()
    {
        OnPropertyChanged(nameof(HasLinkedPeriod));
        OnPropertyChanged(nameof(LinkedPeriodText));
        OnPropertyChanged(nameof(LinkedPeriodFundsText));
        OnPropertyChanged(nameof(LinkedPeriodScienceText));
        OnPropertyChanged(nameof(LinkedPeriodConfidenceText));
        OnPropertyChanged(nameof(LinkedPeriodReputationText));
    }

    private static string FormatMoney(double? value)
        => value is null ? "—" : value.Value.ToString("N0", CultureInfo.InvariantCulture);

    private static string FormatNumber(double? value, string format)
        => value is null ? "—" : value.Value.ToString(format, CultureInfo.InvariantCulture);
}

/// <summary>预算周期行：数字先格式化好再交给视图，支持选中状态与时间线联动。</summary>
public sealed partial class Rp1CareerPeriodRow : ObservableObject
{
    private bool _isSelected;

    public Rp1CareerPeriodRow(Rp1CareerPeriod period)
    {
        Period = period;
    }

    public Rp1CareerPeriod Period { get; }

    public bool IsSelected
    {
        get => _isSelected;
        set => SetProperty(ref _isSelected, value);
    }

    public string DateText => Period.DateText;

    public string FundsText => Period.Funds is null ? "—" : Period.Funds.Value.ToString("N0", CultureInfo.InvariantCulture);

    public string ScienceText => Period.ScienceEarned is null ? "—" : Period.ScienceEarned.Value.ToString("0.0", CultureInfo.InvariantCulture);

    public string ConfidenceText => Period.Confidence is null ? "—" : Period.Confidence.Value.ToString("0", CultureInfo.InvariantCulture);

    public string ReputationText => Period.Reputation is null ? "—" : Period.Reputation.Value.ToString("0.0", CultureInfo.InvariantCulture);

    public string CrewLostText => Period.CrewLost <= 0 ? string.Empty : $"损失 {Period.CrewLost} 人";

    public bool HasCrewLost => Period.CrewLost > 0;
}

/// <summary>曲线指标切换项（视图里是一排分段按钮）。</summary>
public sealed partial class Rp1CareerMetricOption : ObservableObject
{
    public Rp1CareerMetricOption(string key, string label, string glyph, bool isSelected, string valueText = "")
    {
        Key = key;
        Label = label;
        Glyph = glyph;
        _isSelected = isSelected;
        _valueText = valueText;
    }

    /// <summary>指标键（funds / science / confidence / reputation）。</summary>
    public string Key { get; }

    /// <summary>显示名。</summary>
    public string Label { get; }

    /// <summary>图标名（见 CkanUI.Ui.Icons.GlyphCatalog）。</summary>
    public string Glyph { get; }

    [ObservableProperty]
    private bool _isSelected;

    [ObservableProperty]
    private string _valueText = string.Empty;
}
