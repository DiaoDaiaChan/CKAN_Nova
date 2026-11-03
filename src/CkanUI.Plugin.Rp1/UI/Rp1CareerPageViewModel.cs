using System.Collections.ObjectModel;
using System.Globalization;
using CkanUI.Plugin.Rp1.Career;
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
    private readonly List<Rp1CareerEvent> _allEvents = [];

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

    public IReadOnlyList<string> FilterOptions { get; } = ["全部", "里程碑", "合同", "项目", "人员"];

    [ObservableProperty]
    private string _instanceName = "未选择实例";

    [ObservableProperty]
    private string _instanceGameDir = "—";

    [ObservableProperty]
    private string _selectedFilter = "全部";

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

        Periods.Clear();
        foreach (var period in timeline.Periods.OrderByDescending(period => period.StartUt).Take(12))
        {
            Periods.Add(new Rp1CareerPeriodRow(period));
        }

        HasDiagnostics = timeline.Diagnostics.Count > 0;
        DiagnosticsText = HasDiagnostics ? string.Join("\n", timeline.Diagnostics) : string.Empty;

        ShowEmptyState = false;
        ShowContent = true;
        StatusMessage = timeline.Events.Count == 0
            ? "存档读到了，但还没有生涯事件。"
            : $"已读取 {timeline.Events.Count} 条事件。";
        ApplyFilter();
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

        if (SelectedEvent is null || !VisibleEvents.Contains(SelectedEvent))
        {
            SelectedEvent = VisibleEvents.FirstOrDefault();
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
        SelectedEvent = null;
        EventCountText = "0 条事件";
        OnPropertyChanged(nameof(HasNoVisibleEvents));
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

    partial void OnSelectedFilterChanged(string value) => ApplyFilter();

    private static string FormatMoney(double? value)
        => value is null ? "—" : value.Value.ToString("N0", CultureInfo.InvariantCulture);

    private static string FormatNumber(double? value, string format)
        => value is null ? "—" : value.Value.ToString(format, CultureInfo.InvariantCulture);
}

/// <summary>预算周期行：时间线右侧只展示最近 12 期，数字先格式化好再交给视图。</summary>
public sealed class Rp1CareerPeriodRow
{
    public Rp1CareerPeriodRow(Rp1CareerPeriod period)
    {
        Period = period;
    }

    public Rp1CareerPeriod Period { get; }

    public string DateText => Period.DateText;

    public string FundsText => Period.Funds is null ? "—" : Period.Funds.Value.ToString("N0", CultureInfo.InvariantCulture);

    public string ScienceText => Period.ScienceEarned is null ? "—" : Period.ScienceEarned.Value.ToString("0.0", CultureInfo.InvariantCulture);

    public string ConfidenceText => Period.Confidence is null ? "—" : Period.Confidence.Value.ToString("0", CultureInfo.InvariantCulture);

    public string ReputationText => Period.Reputation is null ? "—" : Period.Reputation.Value.ToString("0.0", CultureInfo.InvariantCulture);

    public string CrewLostText => Period.CrewLost <= 0 ? string.Empty : $"损失 {Period.CrewLost} 人";

    public bool HasCrewLost => Period.CrewLost > 0;
}
