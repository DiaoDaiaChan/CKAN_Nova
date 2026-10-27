using CkanUI.Services.Abstractions;
using CkanUI.Services.Abstractions.Dtos;
using CkanUI.ViewModels.Pages;
using NUnit.Framework;

namespace CkanUI.ViewModels.Tests;

/// <summary>下载统计页（12 篇 F-47 / 06 篇 UI-25）。</summary>
[TestFixture]
public sealed class DownloadStatsPageViewModelTests
{
    private static DownloadStatsReport Report(
        IReadOnlyList<DownloadStatsSlice>? slices = null,
        IReadOnlyList<DownloadStatsRow>? rows = null,
        int modules = 3,
        long downloads = 12,
        long cacheBytes = 50,
        bool unavailable = false) =>
        new(
            slices ?? new[] { new DownloadStatsSlice("Alpha", 8, 40, 66.7), new DownloadStatsSlice("Beta", 4, 10, 33.3) },
            rows ?? new[] { new DownloadStatsRow("alpha", "Alpha", 8, 40, true) },
            modules,
            downloads,
            cacheBytes,
            unavailable);

    private static DownloadStatsPageViewModel Page(DownloadStatsReport? report = null)
    {
        var service = new FakeStatsService { Result = OperationResult<DownloadStatsReport>.Ok(report ?? Report()) };
        return new DownloadStatsPageViewModel(service);
    }

    [Test]
    public void TitleIsChinese()
    {
        Assert.That(Page().Title, Is.EqualTo("下载统计"));
    }

    [Test]
    public async Task Refresh_CopiesSlicesRowsAndTotals()
    {
        var page = Page();
        await page.RefreshAsync(CancellationToken.None);

        Assert.That(page.Slices.Count, Is.EqualTo(2));
        Assert.That(page.Rows.Count, Is.EqualTo(1));
        Assert.That(page.ChartSlices.Count, Is.EqualTo(2));
        Assert.That(page.ModuleCount, Is.EqualTo(3));
        Assert.That(page.TotalDownloads, Is.EqualTo(12));
        Assert.That(page.CacheTotalBytes, Is.EqualTo(50));
        Assert.That(page.ErrorText, Is.Null);
        Assert.That(page.IsLoading, Is.False);
        Assert.That(page.ShowEmptyState, Is.False);
    }

    [Test]
    public async Task Refresh_FormatsTotals()
    {
        var page = Page();
        await page.RefreshAsync(CancellationToken.None);

        Assert.That(page.CacheTotalText, Is.EqualTo("50 B"));
        Assert.That(page.TotalDownloadsText, Is.EqualTo("12"));
        Assert.That(page.ModuleCountText, Is.EqualTo("3"));
    }

    [Test]
    public async Task Refresh_StampsTakenAt()
    {
        var page = Page();
        await page.RefreshAsync(CancellationToken.None);

        Assert.That(page.TakenAt, Is.Not.Null);
    }

    [Test]
    public async Task Failure_SetsErrorAndClears()
    {
        var page = new DownloadStatsPageViewModel(
            new FakeStatsService { Result = OperationResult<DownloadStatsReport>.Fail("仓库索引未加载。") });
        await page.RefreshAsync(CancellationToken.None);

        Assert.That(page.ErrorText, Is.EqualTo("仓库索引未加载。"));
        Assert.That(page.HasError, Is.True);
        Assert.That(page.StatusText, Is.EqualTo("仓库索引未加载。"));
        Assert.That(page.Slices, Is.Empty);
        Assert.That(page.Rows, Is.Empty);
        Assert.That(page.ModuleCount, Is.Zero);
    }

    [Test]
    public async Task EmptyReport_ShowsEmptyState()
    {
        var page = Page(Report(Array.Empty<DownloadStatsSlice>(), Array.Empty<DownloadStatsRow>(), 0, 0, 0));
        await page.RefreshAsync(CancellationToken.None);

        Assert.That(page.ShowEmptyState, Is.True);
    }

    [Test]
    public async Task UnavailableFlag_ExposedToView()
    {
        var page = Page(Report(unavailable: true));
        await page.RefreshAsync(CancellationToken.None);

        Assert.That(page.HasUnavailableDownloads, Is.True);
    }

    [Test]
    public async Task SetDimension_SendsRequestAndRefreshes()
    {
        var service = new FakeStatsService { Result = OperationResult<DownloadStatsReport>.Ok(Report()) };
        var page = new DownloadStatsPageViewModel(service);

        await page.SetDimensionCommand.ExecuteAsync(DownloadStatsDimension.Tags);

        Assert.That(page.Dimension, Is.EqualTo(DownloadStatsDimension.Tags));
        Assert.That(page.DimensionLabel, Is.EqualTo("标签"));
        Assert.That(service.Requests, Has.Count.EqualTo(1));
        Assert.That(service.Requests[0].Dimension, Is.EqualTo(DownloadStatsDimension.Tags));
    }

    [Test]
    public async Task SwitchDimension_Repositories_UpdatesLabel()
    {
        var service = new FakeStatsService { Result = OperationResult<DownloadStatsReport>.Ok(Report()) };
        var page = new DownloadStatsPageViewModel(service);

        await page.SwitchDimensionCommand.ExecuteAsync(DownloadStatsDimension.Repositories);

        Assert.That(page.Dimension, Is.EqualTo(DownloadStatsDimension.Repositories));
        Assert.That(page.DimensionLabel, Is.EqualTo("仓库"));
    }

    [Test]
    public async Task SetDimension_SameValue_DoesNotRefetch()
    {
        var service = new FakeStatsService { Result = OperationResult<DownloadStatsReport>.Ok(Report()) };
        var page = new DownloadStatsPageViewModel(service);

        await page.SetDimensionCommand.ExecuteAsync(DownloadStatsDimension.Mods);

        Assert.That(service.Requests, Is.Empty);
    }

    [Test]
    public async Task SetSort_SameColumnFlipsDirection()
    {
        var service = new FakeStatsService { Result = OperationResult<DownloadStatsReport>.Ok(Report()) };
        var page = new DownloadStatsPageViewModel(service);

        await page.SetSortCommand.ExecuteAsync(DownloadStatsSortColumn.Downloads);
        await page.SetSortCommand.ExecuteAsync(DownloadStatsSortColumn.Downloads);

        Assert.That(page.Sort, Is.EqualTo(DownloadStatsSortColumn.Downloads));
        Assert.That(service.Requests.Select(r => r.Descending), Is.EqualTo(new[] { false, true }));
        Assert.That(page.Descending, Is.True, "两次翻转回到降序。");
    }

    [Test]
    public async Task SetSort_NewColumnStartsDescending()
    {
        var service = new FakeStatsService { Result = OperationResult<DownloadStatsReport>.Ok(Report()) };
        var page = new DownloadStatsPageViewModel(service);

        await page.SetSortCommand.ExecuteAsync(DownloadStatsSortColumn.Cache);

        Assert.That(page.Sort, Is.EqualTo(DownloadStatsSortColumn.Cache));
        Assert.That(page.Descending, Is.True);
    }

    [Test]
    public async Task Refresh_RequestsRowLimit()
    {
        var service = new FakeStatsService { Result = OperationResult<DownloadStatsReport>.Ok(Report()) };
        var page = new DownloadStatsPageViewModel(service);

        await page.RefreshAsync(CancellationToken.None);

        Assert.That(service.Requests.Single().TopLimit, Is.EqualTo(DownloadStatsPageViewModel.RowLimit));
    }

    [Test]
    public async Task Activate_TriggersRefresh()
    {
        var service = new FakeStatsService { Result = OperationResult<DownloadStatsReport>.Ok(Report()) };
        var page = new DownloadStatsPageViewModel(service);

        page.Activate();
        await Task.Delay(50);

        Assert.That(service.Requests, Has.Count.GreaterThanOrEqualTo(1));
    }

    [Test]
    public async Task ServiceThrows_SetsErrorText()
    {
        var page = new DownloadStatsPageViewModel(new ThrowingStatsService());
        await page.RefreshAsync(CancellationToken.None);

        Assert.That(page.ErrorText, Does.StartWith("下载统计失败："));
        Assert.That(page.HasError, Is.True);
        Assert.That(page.IsLoading, Is.False);
    }

    [Test]
    public async Task DefaultConstructor_UsesEmptyServiceFallback()
    {
        var page = new DownloadStatsPageViewModel();
        await page.RefreshAsync(CancellationToken.None);

        Assert.That(page.ModuleCount, Is.Zero);
        Assert.That(page.HasError, Is.True);
        Assert.That(page.ErrorText, Is.EqualTo("下载统计服务不可用。"));
        Assert.That(page.ChartSlices, Is.Empty);
    }

    private sealed class FakeStatsService : IDownloadStatsService
    {
        public OperationResult<DownloadStatsReport> Result { get; set; } =
            OperationResult<DownloadStatsReport>.Ok(DownloadStatsReport.Empty);

        public List<DownloadStatsRequest> Requests { get; } = new();

        public Task<OperationResult<DownloadStatsReport>> ComputeAsync(
            DownloadStatsRequest request,
            CancellationToken ct = default)
        {
            Requests.Add(request);
            return Task.FromResult(Result);
        }
    }

    private sealed class ThrowingStatsService : IDownloadStatsService
    {
        public Task<OperationResult<DownloadStatsReport>> ComputeAsync(
            DownloadStatsRequest request,
            CancellationToken ct = default) =>
            throw new InvalidOperationException("快照已释放");
    }
}
