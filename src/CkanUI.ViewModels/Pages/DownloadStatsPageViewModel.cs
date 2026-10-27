using System.Collections.ObjectModel;
using CkanUI.Services.Abstractions;
using CkanUI.Services.Abstractions.Dtos;
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

    private readonly IDownloadStatsService _service;

    /// <summary>无参构造用于设计器与占位视图。</summary>
    public DownloadStatsPageViewModel()
        : this(new EmptyStatsService())
    {
    }

    /// <summary>正常构造。</summary>
    public DownloadStatsPageViewModel(IDownloadStatsService service)
    {
        _service = service ?? throw new ArgumentNullException(nameof(service));
        RefreshCommand = new AsyncRelayCommand(() => RefreshAsync(CancellationToken.None));
        SetDimensionCommand = new AsyncRelayCommand<DownloadStatsDimension>(SetDimensionAsync);
        SwitchDimensionCommand = SetDimensionCommand;
        SetSortCommand = new AsyncRelayCommand<DownloadStatsSortColumn>(SetSortAsync);
    }

    /// <inheritdoc />
    public override string Title => "下载统计";

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

    /// <summary>是否有错误。</summary>
    public bool HasError => !string.IsNullOrEmpty(ErrorText);

    /// <summary>状态文本（有错误显错误，否则显总计摘要）。</summary>
    public string StatusText => ErrorText ?? $"共 {ModuleCount} 个模组，{TotalDownloadsText} 次下载";

    /// <summary>当前分组维度的中文展示名。</summary>
    public string DimensionLabel => Dimension switch
    {
        DownloadStatsDimension.Tags => "标签",
        DownloadStatsDimension.Installed => "安装状态",
        DownloadStatsDimension.Repositories => "仓库",
        _ => "模组",
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
    public string UnavailableHint => "部分模组没有下载量数据（多为本地或刚入库的模组），这些行按 0 计。";

    /// <summary>刷新。</summary>
    public IAsyncRelayCommand RefreshCommand { get; }

    /// <summary>切换分组维度。</summary>
    public IAsyncRelayCommand<DownloadStatsDimension> SetDimensionCommand { get; }

    /// <summary>切换维度别名命令。</summary>
    public IAsyncRelayCommand<DownloadStatsDimension> SwitchDimensionCommand { get; }

    /// <summary>切换排序字段（再次点同一字段即翻转方向）。</summary>
    public IAsyncRelayCommand<DownloadStatsSortColumn> SetSortCommand { get; }

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

    /// <summary>取数并刷新界面状态。</summary>
    public async Task RefreshAsync(CancellationToken ct)
    {
        IsLoading = true;
        ErrorText = null;
        try
        {
            var request = new DownloadStatsRequest(Dimension, RowLimit, Sort, Descending);
            var result = await _service.ComputeAsync(request, ct).ConfigureAwait(true);
            if (!result.IsSuccess || result.Value is null)
            {
                ErrorText = result.Message ?? "无法读取下载统计。";
                Clear();
                return;
            }

            var report = result.Value;
            Slices.Clear();
            foreach (var slice in report.Slices)
            {
                Slices.Add(DownloadStatsSliceViewModel.From(slice));
            }

            ChartSlices = report.Slices;
            OnPropertyChanged(nameof(ChartSlices));

            Rows.Clear();
            foreach (var row in report.Rows)
            {
                Rows.Add(DownloadStatsRowViewModel.From(row));
            }

            ModuleCount = report.ModuleCount;
            TotalDownloads = report.TotalDownloads;
            CacheTotalBytes = report.CacheTotalBytes;
            HasUnavailableDownloads = report.Unavailable;
            TakenAt = DateTime.Now;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            ErrorText = "下载统计失败：" + ex.Message;
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
        Slices.Clear();
        Rows.Clear();
        ChartSlices = Array.Empty<DownloadStatsSlice>();
        OnPropertyChanged(nameof(ChartSlices));
        ModuleCount = 0;
        TotalDownloads = 0;
        CacheTotalBytes = 0;
        HasUnavailableDownloads = false;
    }

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
