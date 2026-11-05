using CkanUI.Localization;
using System.Collections.ObjectModel;
using CkanUI.Services.Abstractions;
using CkanUI.Services.Abstractions.Dtos;
using CkanUI.ViewModels.Navigation;
using CkanUI.ViewModels.Pages.Stats;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace CkanUI.ViewModels.Pages;

/// <summary>
/// 下载统计页（12 篇 F-47 / 06 篇 UI-25）：饼图按维度分组，明细表可切排序字段。
/// 全页只读，唯一的写操作是「刷新」重新取一次快照。
/// </summary>
public sealed partial class DownloadStatsPageViewModel : PageViewModel, IActivatable
{
    /// <summary>明细表的行数上限；0 表示不限。</summary>
    public const int RowLimit = 200;

    /// <summary>建行批次：每批之间让出一帧，避免一次性给 200 行建容器排成一条长帧。</summary>
    private const int RowBatchSize = 8;

    /// <summary>首屏行数：换数据时先把这些行立刻上屏，剩下的再分批补。</summary>
    private const int FirstScreenRows = 20;

    /// <summary>上次灌进表格的数据签名；签名没变就复用已有行，不重建。</summary>
    private string? _rowsSignature;

    private readonly IDownloadStatsService _service;
    private readonly IChangeRequestSink? _changeSink;
    private readonly INavigationService? _navigation;
    private readonly Func<string, object?>? _pageResolver;

    /// <summary>无参构造用于设计器与占位视图。</summary>
    public DownloadStatsPageViewModel()
        : this(new EmptyStatsService())
    {
    }

    /// <summary>基础构造。</summary>
    public DownloadStatsPageViewModel(IDownloadStatsService service)
        : this(service, null, null, null)
    {
    }

    /// <summary>兼容构造：支持从下载统计直接装模组。</summary>
    public DownloadStatsPageViewModel(
        IDownloadStatsService service,
        IChangeRequestSink? changeSink,
        INavigationService? navigation)
        : this(service, changeSink, navigation, null)
    {
    }

    /// <summary>完整构造：支持从下载统计直接装模组、加入变更集与定位模组主页。</summary>
    public DownloadStatsPageViewModel(
        IDownloadStatsService service,
        IChangeRequestSink? changeSink = null,
        INavigationService? navigation = null,
        Func<string, object?>? pageResolver = null)
    {
        _service = service ?? throw new ArgumentNullException(nameof(service));
        _changeSink = changeSink;
        _navigation = navigation;
        _pageResolver = pageResolver;
        RefreshCommand = new AsyncRelayCommand(() => RefreshAsync(CancellationToken.None));
        SetDimensionCommand = new AsyncRelayCommand<DownloadStatsDimension>(SetDimensionAsync);
        SwitchDimensionCommand = SetDimensionCommand;
        SetSortCommand = new AsyncRelayCommand<DownloadStatsSortColumn>(SetSortAsync);
        InstallModCommand = new AsyncRelayCommand<object?>(InstallModAsync);
        OpenModCommand = new AsyncRelayCommand<object?>(OpenModAsync);
        AddToChangesetCommand = new RelayCommand<object?>(AddToChangeset);
    }

    /// <inheritdoc />
    public override string Title => Text("下载统计", LocalizationKeys.NavRailDownloadStats);

    /// <summary>饼图图例。</summary>
    public ObservableCollection<DownloadStatsSliceViewModel> Slices { get; } = new();

    /// <summary>明细表行。</summary>
    public ObservableCollection<DownloadStatsRowViewModel> Rows { get; } = new();

    /// <summary>图表数据点（DonutChart 绑这个）。</summary>
    public IReadOnlyList<DownloadStatsSlice> ChartSlices { get; private set; } = Array.Empty<DownloadStatsSlice>();

    /// <summary>当前分组维度。</summary>
    [ObservableProperty]
    private DownloadStatsDimension _dimension = DownloadStatsDimension.Mods;

    /// <summary>当前排序字段。</summary>
    [ObservableProperty]
    private DownloadStatsSortColumn _sort = DownloadStatsSortColumn.Downloads;

    /// <summary>是否降序。</summary>
    [ObservableProperty]
    private bool _descending = true;

    /// <summary>是否正在取数。</summary>
    [ObservableProperty]
    private bool _isLoading;

    /// <summary>是否繁忙（与 IsLoading 同步）。</summary>
    public bool IsBusy => IsLoading;

    /// <summary>当前选中的行。</summary>
    [ObservableProperty]
    private DownloadStatsRowViewModel? _selectedRow;

    /// <summary>操作提示文本（如加入变更集通知）；为 null 时显示总计摘要。</summary>
    [ObservableProperty]
    private string? _statusMessage;

    partial void OnStatusMessageChanged(string? value) => OnPropertyChanged(nameof(StatusText));

    /// <summary>是否有错误。</summary>
    public bool HasError => !string.IsNullOrEmpty(ErrorText);

    /// <summary>状态文本（有错误显错误，有操作提示显提示，否则显总计摘要）。</summary>
    public string StatusText => ErrorText
        ?? StatusMessage
        ?? Text("共 {0} 个模组，{1} 次下载", LocalizationKeys.DownloadStatsStatusSummary, ModuleCount, TotalDownloadsText);

    /// <summary>当前分组维度的中文展示名。</summary>
    public string DimensionLabel => Dimension switch
    {
        DownloadStatsDimension.Tags => Text("标签", LocalizationKeys.DownloadStatsDimensionTags),
        DownloadStatsDimension.Installed => Text("安装状态", LocalizationKeys.DownloadStatsDimensionInstalled),
        DownloadStatsDimension.Repositories => Text("仓库", LocalizationKeys.DownloadStatsDimensionRepositories),
        _ => Text("模组", LocalizationKeys.DownloadStatsDimensionMods),
    };

    /// <summary>错误说明；成功时为 null。</summary>
    [ObservableProperty]
    private string? _errorText;

    /// <summary>参与统计的模组数。</summary>
    [ObservableProperty]
    private int _moduleCount;

    /// <summary>下载量合计。</summary>
    [ObservableProperty]
    private long _totalDownloads;

    /// <summary>缓存占用合计。</summary>
    [ObservableProperty]
    private long _cacheTotalBytes;

    /// <summary>最近一次取数时间。</summary>
    [ObservableProperty]
    private DateTime? _takenAt;

    /// <summary>是否存在下载量不可用的模组。</summary>
    [ObservableProperty]
    private bool _hasUnavailableDownloads;

    /// <summary>空状态：已取过数、没有错误、也没有任何模组。</summary>
    public bool ShowEmptyState => !IsLoading && ErrorText is null && ModuleCount == 0;


    /// <summary>下载量合计文本。</summary>
    public string TotalDownloadsText => DownloadStatsFormat.Count(TotalDownloads);

    /// <summary>缓存占用合计文本。</summary>
    public string CacheTotalText => DownloadStatsFormat.Bytes(CacheTotalBytes);

    /// <summary>模组数文本。</summary>
    public string ModuleCountText => DownloadStatsFormat.Count(ModuleCount);

    /// <summary>提示条：有模组下载量不可用时说明来源。</summary>
    public string UnavailableHint => Text("部分模组没有下载量数据。", LocalizationKeys.DownloadStatsUnavailableHint);

    /// <summary>刷新。</summary>
    public IAsyncRelayCommand RefreshCommand { get; }

    /// <summary>切换分组维度。</summary>
    public IAsyncRelayCommand<DownloadStatsDimension> SetDimensionCommand { get; }

    /// <summary>切换维度别名命令。</summary>
    public IAsyncRelayCommand<DownloadStatsDimension> SwitchDimensionCommand { get; }

    /// <summary>切换排序字段（再次点同一字段即翻转方向）。</summary>
    public IAsyncRelayCommand<DownloadStatsSortColumn> SetSortCommand { get; }

    /// <summary>从下载统计页安装模组（将变更请求灌入变更集并跳转）。</summary>
    public IAsyncRelayCommand<object?> InstallModCommand { get; }

    /// <summary>打开模组主页（定位并选中该模组）。</summary>
    public IAsyncRelayCommand<object?> OpenModCommand { get; }

    /// <summary>方便地将模组加入待变更集。</summary>
    public IRelayCommand<object?> AddToChangesetCommand { get; }

    /// <inheritdoc />
    protected override void OnActivated()
    {
        _ = RefreshAsync(CancellationToken.None);
    }

    private async Task SetDimensionAsync(DownloadStatsDimension dimension)
    {
        if (Dimension == dimension)
        {
            return;
        }

        Dimension = dimension;
        OnPropertyChanged(nameof(DimensionLabel));
        await RefreshAsync(CancellationToken.None).ConfigureAwait(true);
    }

    private async Task SetSortAsync(DownloadStatsSortColumn sort)
    {
        if (Sort == sort)
        {
            Descending = !Descending;
        }
        else
        {
            Sort = sort;
            Descending = true;
        }

        await RefreshAsync(CancellationToken.None).ConfigureAwait(true);
    }

    private async Task OpenModAsync(object? parameter)
    {
        string? identifier = ResolveIdentifier(parameter) ?? SelectedRow?.Identifier;
        if (string.IsNullOrWhiteSpace(identifier))
        {
            return;
        }

        if (_navigation is not null)
        {
            await _navigation.NavigateAsync("mods").ConfigureAwait(true);
        }

        var modsPage = (_pageResolver?.Invoke("mods") ?? _navigation?.CurrentPage) as ModsPageViewModel;
        if (modsPage is not null)
        {
            await modsPage.ShowIdentifierAsync(identifier).ConfigureAwait(true);
        }
    }

    private void AddToChangeset(object? parameter)
    {
        string? identifier = ResolveIdentifier(parameter);
        if (string.IsNullOrWhiteSpace(identifier))
        {
            return;
        }

        IReadOnlyCollection<ChangeRequest>? existing = (_changeSink as ChangesPageViewModel)?.Requests;
        if (existing is not null)
        {
            if (existing.Any(r => string.Equals(r.Identifier, identifier, StringComparison.OrdinalIgnoreCase)))
            {
                StatusMessage = Text("模组「{0}」已在待变更集中。", LocalizationKeys.DownloadStatsAlreadyInChangeset, identifier);
                return;
            }

            var updated = existing
                .Where(r => !string.Equals(r.Identifier, identifier, StringComparison.OrdinalIgnoreCase))
                .Append(new ChangeRequest(identifier, ChangeAction.Install))
                .ToArray();

            _changeSink?.SetRequests(updated);
        }
        else
        {
            _changeSink?.SetRequests(new[] { new ChangeRequest(identifier, ChangeAction.Install) });
        }

        StatusMessage = Text("已将模组「{0}」加入待变更集。", LocalizationKeys.DownloadStatsAddedToChangeset, identifier);
    }

    private async Task InstallModAsync(object? parameter)
    {
        string? identifier = ResolveIdentifier(parameter);
        if (string.IsNullOrWhiteSpace(identifier))
        {
            return;
        }

        AddToChangeset(parameter);

        if (_navigation is not null)
        {
            await _navigation.NavigateAsync("changes").ConfigureAwait(true);
        }
    }

    private static string? ResolveIdentifier(object? parameter) => parameter switch
    {
        DownloadStatsRowViewModel row => row.IsInstalled ? null : row.Identifier,
        DownloadStatsRow rowDto => rowDto.IsInstalled ? null : rowDto.Identifier,
        string id when !string.IsNullOrWhiteSpace(id) => id,
        _ => null
    };

    /// <summary>取数并刷新界面状态。</summary>
    public async Task RefreshAsync(CancellationToken ct)
    {
        IsLoading = true;
        ErrorText = null;
        // 首进不抢首帧：Task.Yield 只是回到 Normal 优先级，渲染还排在它后面；
        // 用一次 1ms 的延时把控制权交回调度器，骨架屏与空表头这一帧才真的上屏。
        await Task.Delay(1);
        try
        {
            var request = new DownloadStatsRequest(Dimension, RowLimit, Sort, Descending);
            var result = await _service.ComputeAsync(request, ct).ConfigureAwait(true);
            if (!result.IsSuccess || result.Value is null)
            {
                ErrorText = result.Message ?? Text("无法读取下载统计。", LocalizationKeys.DownloadStatsErrorLoad);
                Clear();
                return;
            }

            var report = result.Value;
            var signature = BuildRowsSignature(request, report);
            if (Rows.Count > 0 && string.Equals(signature, _rowsSignature, StringComparison.Ordinal))
            {
                // 数据签名没变：表格里已经是这批行，直接复用，既不清空也不重灌。
                // 重进页面时走这条路——首进成本不会再被搬进第二进。
                TakenAt = DateTime.Now;
                return;
            }

            _rowsSignature = signature;
            Slices.Clear();
            foreach (var slice in report.Slices)
            {
                Slices.Add(DownloadStatsSliceViewModel.From(slice));
            }

            ChartSlices = report.Slices;
            OnPropertyChanged(nameof(ChartSlices));

            ModuleCount = report.ModuleCount;
            TotalDownloads = report.TotalDownloads;
            CacheTotalBytes = report.CacheTotalBytes;
            HasUnavailableDownloads = report.Unavailable;
            TakenAt = DateTime.Now;

            // 行容器是这张表最贵的部分（每行 30+ 个可视节点）：先把视图模型建好，再按批灌进表格，
            // 每批之间让出一帧——首进就不会出现「一次给 200 行建容器」的几百毫秒长帧。
            Rows.Clear();
            var maxDownloads = report.Rows.Count == 0 ? 0 : report.Rows.Max(row => row.DownloadCount);
            var rows = new List<DownloadStatsRowViewModel>(report.Rows.Count);
            foreach (var row in report.Rows)
            {
                rows.Add(DownloadStatsRowViewModel.From(row, maxDownloads));
            }

            // 换数据时先把首屏填满（用户马上看到内容），剩下的再按批补——第一帧只付首屏的钱。
            var filled = Math.Min(rows.Count, FirstScreenRows);
            for (var k = 0; k < filled; k++)
            {
                Rows.Add(rows[k]);
            }

            if (filled < rows.Count)
            {
                // 首屏之后必须等真正的布局跑过一遍：表格只有量到有限高度才会虚拟化，
                // 若 200 行在第一次布局前就全到齐，它会一次性把所有行实体化（1724 节点 / 秒级长帧）。
                await Task.Delay(200);
            }

            for (var i = filled; i < rows.Count; i += RowBatchSize)
            {
                var end = Math.Min(i + RowBatchSize, rows.Count);
                for (var j = i; j < end; j++)
                {
                    Rows.Add(rows[j]);
                }

                if (end < rows.Count)
                {
                    // 批间让出整整一帧（1ms 在机器忙时可能连一次布局都排不上，
                    // 那样 12 批会在同一次布局里合流，又变回「200 行一次实体化」）。
                    await Task.Delay(16);
                }
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            ErrorText = Text("下载统计失败：{0}", LocalizationKeys.DownloadStatsErrorException, ex.Message);
            Clear();
        }
        finally
        {
            IsLoading = false;
            RefreshDerived();
        }
    }

    private void Clear()
    {
        _rowsSignature = null;
        Slices.Clear();
        Rows.Clear();
        ChartSlices = Array.Empty<DownloadStatsSlice>();
        OnPropertyChanged(nameof(ChartSlices));
        ModuleCount = 0;
        TotalDownloads = 0;
        CacheTotalBytes = 0;
        HasUnavailableDownloads = false;
    }

    /// <summary>行集合的数据签名：请求口径 + 几个汇总量 + 行数，变了才需要重灌。</summary>
    private static string BuildRowsSignature(DownloadStatsRequest request, DownloadStatsReport report)
        => string.Join('|',
            request.Dimension.ToString(),
            request.Sort.ToString(),
            request.Descending ? "1" : "0",
            report.ModuleCount.ToString(System.Globalization.CultureInfo.InvariantCulture),
            report.TotalDownloads.ToString(System.Globalization.CultureInfo.InvariantCulture),
            report.CacheTotalBytes.ToString(System.Globalization.CultureInfo.InvariantCulture),
            report.Unavailable ? "1" : "0",
            report.Rows.Count.ToString(System.Globalization.CultureInfo.InvariantCulture));

    private void RefreshDerived()
    {
        OnPropertyChanged(nameof(IsBusy));
        OnPropertyChanged(nameof(HasError));
        OnPropertyChanged(nameof(StatusText));
        OnPropertyChanged(nameof(DimensionLabel));
        OnPropertyChanged(nameof(ShowEmptyState));
        OnPropertyChanged(nameof(TotalDownloadsText));
        OnPropertyChanged(nameof(CacheTotalText));
        OnPropertyChanged(nameof(ModuleCountText));
    }

    partial void OnTotalDownloadsChanged(long value) => OnPropertyChanged(nameof(TotalDownloadsText));

    partial void OnCacheTotalBytesChanged(long value) => OnPropertyChanged(nameof(CacheTotalText));

    partial void OnModuleCountChanged(int value)
    {
        OnPropertyChanged(nameof(ModuleCountText));
        OnPropertyChanged(nameof(ShowEmptyState));
        OnPropertyChanged(nameof(StatusText));
    }

    partial void OnIsLoadingChanged(bool value)
    {
        OnPropertyChanged(nameof(IsBusy));
        OnPropertyChanged(nameof(ShowEmptyState));
    }

    partial void OnErrorTextChanged(string? value)
    {
        OnPropertyChanged(nameof(HasError));
        OnPropertyChanged(nameof(StatusText));
        OnPropertyChanged(nameof(ShowEmptyState));
    }

    private sealed class EmptyStatsService : IDownloadStatsService
    {
    }
}
