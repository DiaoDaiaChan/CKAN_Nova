using System.Collections.ObjectModel;
using System.ComponentModel;
using CkanUI.Services.Abstractions.Logs;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace CkanUI.ViewModels.Pages.Logs;

/// <summary>
/// 日志页与底部抽屉共用的过滤条（14 篇 §4.2①）：级别 / 日志族 / 采集来源三段多选，外加一个带语法前缀的搜索框。
/// 为什么要单独抽一个视图模型：页面的主列表、聚合视图、时间轴、徽标都读同一份 <see cref="LogQuery"/>，
/// 分散在页面里改会互相打架；这里只负责「勾选状态 → 查询」的唯一映射。
/// </summary>
public sealed partial class LogFilterViewModel : ObservableObject
{
    private readonly LogLevelOptionViewModel[] _levelOptions;
    private readonly LogFamilyOptionViewModel[] _familyOptions;
    private readonly LogSourceOptionViewModel[] _sourceOptions;

    /// <summary>批量改勾选时挂起逐项通知：一次「切通道」要改 7 个勾选，逐项触发会刷新 7 次列表。</summary>
    private bool _suspended;

    /// <summary>按规格默认值构造：级别 Info+、族全选、源 log4net/user/exception/child。</summary>
    public LogFilterViewModel()
    {
        _levelOptions = Enum.GetValues<LogLevel>()
            .Select(level => new LogLevelOptionViewModel(
                level,
                LogGlyphs.Glyph(level),
                // 默认 Info+：User 是 IUser 通道而不是严重度，按 Info 严重度对待所以一并打开，
                // 否则用户看到的「安装完成/下载失败」这类人话提示默认就被过滤掉了。
                level != LogLevel.Trace && level != LogLevel.Debug))
            .ToArray();

        _familyOptions = Enum.GetValues<LogFamily>()
            .Select(family => new LogFamilyOptionViewModel(family, isSelected: true))
            .ToArray();

        _sourceOptions = Enum.GetValues<LogSourceKind>()
            .Select(kind => new LogSourceOptionViewModel(
                kind,
                // 规格 §4.2① 只要 log4net/user/exception/child 四路；
                // 界面事件（Ui）与旧日志导入（Legacy）默认关掉，避免刷新时被自己的界面噪音刷屏。
                kind is LogSourceKind.Log4Net or LogSourceKind.User or LogSourceKind.Exception or LogSourceKind.Child))
            .ToArray();

        ClearSearchCommand = new RelayCommand(() => SearchText = string.Empty);
        ResetCommand = new RelayCommand(Reset);

        Levels = new ObservableCollection<LogLevelOptionViewModel>(_levelOptions);
        Families = new ObservableCollection<LogFamilyOptionViewModel>(_familyOptions);
        Sources = new ObservableCollection<LogSourceOptionViewModel>(_sourceOptions);

        foreach (var option in _levelOptions.Cast<LogFacetOptionViewModel>()
                     .Concat(_familyOptions)
                     .Concat(_sourceOptions))
        {
            option.PropertyChanged += OnOptionChanged;
        }
    }

    /// <summary>级别选项。</summary>
    public ObservableCollection<LogLevelOptionViewModel> Levels { get; }

    /// <summary>日志族选项。</summary>
    public ObservableCollection<LogFamilyOptionViewModel> Families { get; }

    /// <summary>来源选项。</summary>
    public ObservableCollection<LogSourceOptionViewModel> Sources { get; }

    /// <summary>搜索框原文（支持 族:net / 级别:error / mod:xxx 前缀语法）。</summary>
    [ObservableProperty]
    private string _searchText = string.Empty;

    /// <summary>过滤条件变化（任何勾选或搜索文本变化）。</summary>
    public event EventHandler? Changed;

    /// <summary>勾选的级别。</summary>
    public IReadOnlyList<LogLevel> SelectedLevels => _levelOptions.Where(o => o.IsSelected).Select(o => o.Level).ToArray();

    /// <summary>勾选的日志族。</summary>
    public IReadOnlyList<LogFamily> SelectedFamilies => _familyOptions.Where(o => o.IsSelected).Select(o => o.Family).ToArray();

    /// <summary>勾选的来源。</summary>
    public IReadOnlyList<LogSourceKind> SelectedSources => _sourceOptions.Where(o => o.IsSelected).Select(o => o.Kind).ToArray();

    /// <summary>
    /// 某一段一个都没勾：此时按「没有命中」处理。
    /// 为什么要显式区分：<see cref="LogQuery"/> 里空集合的语义是「不限」，若直接把空集合传下去，
    /// 用户取消全部勾选反而会看到全部日志，与直觉相反。
    /// </summary>
    public bool MatchesNothing =>
        SelectedLevels.Count == 0 || SelectedFamilies.Count == 0 || SelectedSources.Count == 0;

    /// <summary>
    /// 是否仍是规格默认值（视图用来决定「重置」按钮是否可用）。
    /// 为什么不能拿「勾选数 == 选项总数」当默认：规格里级别默认只勾 Info 及以上、源默认只勾四路采集，
    /// 与「全选」本来就不是一回事，否则构造完立刻就是「非默认」。
    /// </summary>
    public bool IsDefault => !MatchesNothing && SearchText.Length == 0
        && _levelOptions.All(option => option.IsSelected == (option.Level != LogLevel.Trace && option.Level != LogLevel.Debug))
        && _familyOptions.All(option => option.IsSelected)
        && _sourceOptions.All(option => option.IsSelected == (option.Kind is LogSourceKind.Log4Net
            or LogSourceKind.User
            or LogSourceKind.Exception
            or LogSourceKind.Child));

    /// <summary>清空搜索框。</summary>
    public IRelayCommand ClearSearchCommand { get; }

    /// <summary>恢复规格默认值。</summary>
    public IRelayCommand ResetCommand { get; }

    /// <summary>把查询描述成人话（过滤条尾部提示）。</summary>
    public string Description => MatchesNothing ? "当前过滤条件不会命中任何日志" : Describe(BuildQuery());

    /// <summary>
    /// 中文描述要自己写一遍：<see cref="LogSearchSyntax.Describe"/> 在抽象层，只能给出枚举名（"级别:error"），
    /// 直接贴到界面上是半截英文；界面文案必须走 <see cref="LogGlyphs"/> 的中文标签。
    /// </summary>
    private static string Describe(LogQuery query)
    {
        var parts = new List<string>(5);

        if (query.NormalizedText.Length > 0)
        {
            parts.Add("搜索「" + query.NormalizedText + "」");
        }

        if (query.Levels is { Count: > 0 } levels)
        {
            parts.Add("级别:" + string.Join("/", levels.OrderBy(level => level).Select(LogGlyphs.LevelLabel)));
        }

        if (query.Families is { Count: > 0 } families)
        {
            parts.Add("族:" + string.Join("/", families.Select(LogGlyphs.FamilyLabel)));
        }

        if (query.Kinds is { Count: > 0 } kinds)
        {
            parts.Add("源:" + string.Join("/", kinds.Select(LogGlyphs.SourceLabel)));
        }

        if (query.Tags is { Count: > 0 } tags)
        {
            parts.AddRange(tags.Select(pair => pair.Key + ":" + pair.Value));
        }

        return parts.Count == 0 ? "全部日志" : string.Join(" · ", parts);
    }

    /// <summary>按当前勾选与搜索文本生成查询；全选的一段不下推（等价于不限）。</summary>
    public LogQuery BuildQuery(int take = 500)
    {
        var query = LogSearchSyntax.Parse(SearchText) with { Take = take };
        return query with
        {
            Levels = Narrow(SelectedLevels, query.Levels, _levelOptions.Length),
            Families = Narrow(SelectedFamilies, query.Families, _familyOptions.Length),
            Kinds = Narrow(SelectedSources, query.Kinds, _sourceOptions.Length),
        };
    }

    /// <summary>
    /// 合并「过滤条勾选」与「搜索框前缀语法」两处条件。
    /// 为什么不能简单覆盖：规格里 <c>级别:error</c> 是搜索框语法，同时过滤条的勾选也在生效，
    /// 所以勾选已经收窄时要取交集。交集为空说明两处条件互斥，此时以用户刚敲进搜索框的条件为准——
    /// 不能返回空集合，因为 <see cref="LogQuery"/> 里空集合的语义是「不限」，返回空反而会把全部日志放开。
    /// </summary>
    private static IReadOnlyCollection<T>? Narrow<T>(
        IReadOnlyList<T> selected,
        IReadOnlyCollection<T>? parsed,
        int optionCount)
        where T : struct
    {
        if (parsed is null || parsed.Count == 0)
        {
            return selected.Count == optionCount ? null : selected;
        }

        if (selected.Count == optionCount)
        {
            return parsed;
        }

        var intersected = selected.Where(parsed.Contains).ToArray();
        return intersected.Length > 0 ? intersected : parsed;
    }

    /// <summary>
    /// 「界面日志 / 游戏日志」两档切换时只勾选该档的来源。
    /// 为什么是一刀切而不是叠加：两个通道的行格式与排障语境完全不同，混在一起看等于没分档。
    /// </summary>
    public void ApplySources(params LogSourceKind[] kinds)
    {
        _suspended = true;
        try
        {
            foreach (var option in _sourceOptions)
            {
                option.IsSelected = kinds.Contains(option.Kind);
            }
        }
        finally
        {
            _suspended = false;
        }

        RaiseChanged();
    }

    /// <summary>只保留错误（工具栏「仅错误」）。</summary>
    public void ShowOnlyErrors()
    {
        foreach (var option in _levelOptions)
        {
            option.IsSelected = LogLevels.IsError(option.Level);
        }

        foreach (var option in _sourceOptions)
        {
            option.IsSelected = true;
        }
    }

    /// <summary>恢复默认值（与构造时的勾选一致）。</summary>
    public void Reset()
    {
        foreach (var option in _levelOptions)
        {
            option.IsSelected = option.Level != LogLevel.Trace && option.Level != LogLevel.Debug;
        }

        foreach (var option in _familyOptions)
        {
            option.IsSelected = true;
        }

        foreach (var option in _sourceOptions)
        {
            option.IsSelected = option.Kind is LogSourceKind.Log4Net or LogSourceKind.User
                or LogSourceKind.Exception or LogSourceKind.Child;
        }

        SearchText = string.Empty;
    }

    private void OnOptionChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName is nameof(LogFacetOptionViewModel.IsSelected) && !_suspended)
        {
            RaiseChanged();
        }
    }

    private void RaiseChanged()
    {
        OnPropertyChanged(nameof(MatchesNothing));
        OnPropertyChanged(nameof(IsDefault));
        OnPropertyChanged(nameof(Description));
        Changed?.Invoke(this, EventArgs.Empty);
    }

    partial void OnSearchTextChanged(string value) => RaiseChanged();
}
