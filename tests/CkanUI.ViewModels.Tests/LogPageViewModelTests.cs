using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using CkanUI.Services.Abstractions;
using CkanUI.Services.Abstractions.Logs;
using CkanUI.ViewModels.Pages.Logs;
using NUnit.Framework;

namespace CkanUI.ViewModels.Tests;

/// <summary>日志页视图模型（14 篇 §4.2 审查视图 / UI-LOG-01）。</summary>
[TestFixture]
public sealed class LogPageViewModelTests
{
    [Test]
    public void Title_IsChineseLogPage() => Assert.That(LogTestData.CreatePage().Page.Title, Is.EqualTo("日志"));

    [Test]
    public void DesignConstructor_WorksWithoutServices()
    {
        var page = new LogPageViewModel();

        page.Refresh();

        Assert.That(page.Rows, Is.Empty);
        Assert.That(page.ShowEmptyState, Is.True);
        Assert.That(page.EmptyHint, Does.Contain("缓冲区还没有日志"));
    }

    [Test]
    public void Refresh_ShowsNewestFirstAndCountsMatches()
    {
        var (store, _, page) = LogTestData.CreatePage();
        store.Append(LogTestData.Entry(LogLevel.Info, "first"));
        store.Append(LogTestData.Entry(LogLevel.Info, "second"));

        page.Refresh();

        Assert.That(page.Rows.Count, Is.EqualTo(2));
        Assert.That(page.Rows[0].Message, Is.EqualTo("first"), "列表按时间正序，最旧的一条在最上面");
        Assert.That(page.Rows[1].Message, Is.EqualTo("second"), "最新的一条在最下面");
        Assert.That(page.TotalMatches, Is.EqualTo(2));
        Assert.That(page.HasSelection, Is.True);
    }

    [Test]
    public void Refresh_CountsErrorsWarningsAndBadge()
    {
        var (store, _, page) = LogTestData.CreatePage();
        store.Append(LogTestData.Entry(LogLevel.Info, "ok"));
        store.Append(LogTestData.Entry(LogLevel.Error, "boom"));
        store.Append(LogTestData.Entry(LogLevel.Fatal, "dead"));
        store.Append(LogTestData.Entry(LogLevel.Warn, "careful"));

        page.Refresh();

        Assert.That(page.ErrorCount, Is.EqualTo(2), "Fatal 也算错误");
        Assert.That(page.WarningCount, Is.EqualTo(1));
        Assert.That(page.HasBadge, Is.True);
        Assert.That(page.BadgeText, Is.EqualTo("有 2 个错误"));
        Assert.That(page.StatusText, Does.Contain("命中 4 条"));
    }

    [Test]
    public void BadgeText_PrefersWarningsWhenNoErrors()
    {
        var (store, _, page) = LogTestData.CreatePage();
        store.Append(LogTestData.Entry(LogLevel.Warn, "careful"));

        page.Refresh();

        Assert.That(page.BadgeText, Is.EqualTo("有 1 个警告"));
    }

    [Test]
    public void BadgeText_IsQuietWhenNothingWrong()
    {
        var (store, _, page) = LogTestData.CreatePage();
        store.Append(LogTestData.Entry(LogLevel.Info, "ok"));

        page.Refresh();

        Assert.That(page.HasBadge, Is.False);
        Assert.That(page.BadgeText, Is.EqualTo("没有新的错误或警告"));
    }

    [Test]
    public void SearchText_FiltersRowsAndHighlightsHits()
    {
        var (store, _, page) = LogTestData.CreatePage();
        store.Append(LogTestData.Entry(LogLevel.Info, "found a needle here"));
        store.Append(LogTestData.Entry(LogLevel.Info, "nothing interesting"));

        page.Filters.SearchText = "needle";

        Assert.That(page.Rows.Count, Is.EqualTo(1));
        Assert.That(page.Rows[0].IsHighlighted, Is.True, "命中片段要在行里高亮");
        Assert.That(page.TotalMatches, Is.EqualTo(1));
    }

    [Test]
    public void ClearedFacets_ShowEmptyStateWithExplainingHint()
    {
        var (store, _, page) = LogTestData.CreatePage();
        store.Append(LogTestData.Entry(LogLevel.Info, "ok"));
        page.Refresh();

        foreach (var option in page.Filters.Levels)
        {
            option.IsSelected = false;
        }

        Assert.That(page.Rows, Is.Empty);
        Assert.That(page.SelectedRow, Is.Null);
        Assert.That(page.ShowEmptyState, Is.True);
        Assert.That(page.EmptyHint, Does.Contain("不命中"));
    }

    [Test]
    public void ShowOnlyErrorsCommand_KeepsErrorLevelsOnly()
    {
        var (store, _, page) = LogTestData.CreatePage();
        store.Append(LogTestData.Entry(LogLevel.Info, "ok"));
        store.Append(LogTestData.Entry(LogLevel.Warn, "careful"));
        store.Append(LogTestData.Entry(LogLevel.Error, "boom"));
        page.Refresh();

        page.ShowOnlyErrorsCommand.Execute(null);

        Assert.That(page.Rows.Count, Is.EqualTo(1));
        Assert.That(page.Rows.All(row => row.IsError), Is.True);
    }

    [Test]
    public void ToggleAggregateCommand_FoldsIdenticalFingerprints()
    {
        var (store, _, page) = LogTestData.CreatePage();
        store.Append(LogTestData.Entry(LogLevel.Error, "download failed for 1234"));
        store.Append(LogTestData.Entry(LogLevel.Error, "download failed for 5678"));
        page.Refresh();

        page.ToggleAggregateCommand.Execute(null);

        Assert.That(page.IsAggregated, Is.True);
        Assert.That(page.Groups.Count, Is.EqualTo(1));
        Assert.That(page.Groups[0].Count, Is.EqualTo(2));
        Assert.That(page.Groups[0].CountText, Is.EqualTo("×2"));
        Assert.That(page.AggregateHint, Does.Contain("前 20 组"));
    }

    [Test]
    public void Timeline_HasScaledBarsPerBucket()
    {
        var (store, _, page) = LogTestData.CreatePage();
        store.Append(LogTestData.Entry(LogLevel.Error, "boom 1"));
        store.Append(LogTestData.Entry(LogLevel.Error, "boom 2"));
        store.Append(LogTestData.Entry(LogLevel.Info, "ok"));

        page.Refresh();

        Assert.That(page.Timeline, Is.Not.Empty);
        Assert.That(page.Timeline.Any(bar => bar.HasError), Is.True);
        Assert.That(
            page.Timeline.All(bar => bar.BarHeight >= 2.0 && bar.BarHeight <= LogTimelineBarViewModel.MaxBarHeight),
            Is.True);
    }

    [Test]
    public void FindNextAndPrevious_CycleThroughRows()
    {
        var (store, _, page) = LogTestData.CreatePage();
        store.Append(LogTestData.Entry(LogLevel.Info, "a"));
        store.Append(LogTestData.Entry(LogLevel.Info, "b"));
        store.Append(LogTestData.Entry(LogLevel.Info, "c"));
        page.Refresh();

        page.FindNextCommand.Execute(null);
        Assert.That(page.CurrentMatchOrdinal, Is.EqualTo(1));
        Assert.That(page.Rows[0].IsCurrentMatch, Is.True);

        page.FindNextCommand.Execute(null);
        Assert.That(page.CurrentMatchOrdinal, Is.EqualTo(2));

        page.FindPreviousCommand.Execute(null);
        Assert.That(page.CurrentMatchOrdinal, Is.EqualTo(1));

        page.FindPreviousCommand.Execute(null);
        Assert.That(page.CurrentMatchOrdinal, Is.EqualTo(3), "到头要绕回最后一条");
        Assert.That(page.Rows[2].IsCurrentMatch, Is.True);
        Assert.That(page.Rows[0].IsCurrentMatch, Is.False, "同一时刻只能有一个当前命中");
    }

    [Test]
    public void FindNext_IsNoOpOnEmptyRows()
    {
        var (_, _, page) = LogTestData.CreatePage();

        page.Refresh();
        page.FindNextCommand.Execute(null);

        Assert.That(page.CurrentMatchOrdinal, Is.Zero);
    }

    [Test]
    public void Clear_EmptiesBufferAndKeepsHintingAboutSequence()
    {
        var (store, _, page) = LogTestData.CreatePage();
        store.Append(LogTestData.Entry(LogLevel.Info, "ok"));
        page.Refresh();

        page.ClearCommand.Execute(null);

        Assert.That(store.Count, Is.Zero);
        Assert.That(page.Rows, Is.Empty);
        Assert.That(page.Message, Does.Contain("已清空缓冲区"));
        Assert.That(page.StatusText, Does.Contain("已清空缓冲区"));
    }

    [Test]
    public void SelectedRow_FeedsDetailPanelAndObjectLinks()
    {
        var (store, _, page) = LogTestData.CreatePage();
        store.Append(LogTestData.Entry(
            LogLevel.Error,
            "failed mod:RealChute",
            LogFamily.Download,
            detail: "System.IO.IOException: boom"));
        page.Refresh();

        var row = page.Rows[0];
        page.SelectedRow = row;

        Assert.That(page.HasSelection, Is.True);
        Assert.That(page.SelectedMessage, Is.EqualTo("failed mod:RealChute"));
        Assert.That(page.SelectedDetail, Does.Contain("IOException"));
        Assert.That(page.SelectedHeader, Does.Contain("错误"));
        Assert.That(page.SelectedHeader, Does.Contain("下载"));
        Assert.That(page.SelectedSourceText, Is.EqualTo("log4net:CKAN.Test"));
        Assert.That(page.SelectedLinks.Any(link => link.Kind == LogLinkKind.Mod), Is.True);
    }

    [Test]
    public void SelectedRow_ClearedShowsPlaceholderHeader()
    {
        var (store, _, page) = LogTestData.CreatePage();
        store.Append(LogTestData.Entry(LogLevel.Info, "ok"));
        page.Refresh();

        page.SelectedRow = null;

        Assert.That(page.HasSelection, Is.False);
        Assert.That(page.SelectedHeader, Is.EqualTo("未选中日志行"));
        Assert.That(page.SelectedLinks, Is.Empty);
    }

    [Test]
    public async Task Export_WithoutBundleService_ExplainsItIsNotWired()
    {
        var (_, _, page) = LogTestData.CreatePage();

        var ok = await page.ExportBundleAsync("bundle.zip");

        Assert.That(ok, Is.False);
        Assert.That(page.Message, Does.Contain("未接线"));
    }

    [Test]
    public async Task Export_WithBlankPath_AsksForLocation()
    {
        var (_, _, page) = LogTestData.CreatePage(bundle: new FakeLogBundleService());

        var ok = await page.ExportBundleAsync("   ");

        Assert.That(ok, Is.False);
        Assert.That(page.Message, Does.Contain("保存位置"));
    }

    [Test]
    public async Task Export_OnSuccess_RemembersPathAndReportsSummary()
    {
        var bundle = new FakeLogBundleService();
        var (_, _, page) = LogTestData.CreatePage(bundle: bundle);

        var ok = await page.ExportBundleAsync("bundle.zip");

        Assert.That(ok, Is.True);
        Assert.That(page.ExportedPath, Is.EqualTo("bundle.zip"));
        Assert.That(page.Message, Does.Contain("已导出诊断包"));
        Assert.That(page.IsBusy, Is.False);
        Assert.That(bundle.Requests, Has.Count.EqualTo(1));
        Assert.That(bundle.Requests[0].OutputPath, Is.EqualTo("bundle.zip"));
    }

    [Test]
    public async Task Export_OnFailure_ReportsServiceMessage()
    {
        var bundle = new FakeLogBundleService
        {
            Handler = _ => OperationResult<LogBundleResult>.Fail("诊断包含敏感信息，已拒绝输出"),
        };
        var (_, _, page) = LogTestData.CreatePage(bundle: bundle);

        var ok = await page.ExportBundleAsync("bundle.zip");

        Assert.That(ok, Is.False);
        Assert.That(page.Message, Is.EqualTo("诊断包含敏感信息，已拒绝输出"));
        Assert.That(page.ExportedPath, Is.Null);
    }

    [Test]
    public async Task Export_OnException_ReportsWrappedMessage()
    {
        var bundle = new FakeLogBundleService
        {
            Handler = _ => throw new IOException("磁盘已满"),
        };
        var (_, _, page) = LogTestData.CreatePage(bundle: bundle);

        var ok = await page.ExportBundleAsync("bundle.zip");

        Assert.That(ok, Is.False);
        Assert.That(page.Message, Does.Contain("导出诊断包失败"));
        Assert.That(page.Message, Does.Contain("磁盘已满"));
    }

    [Test]
    public void RequestAiAnalysis_WithoutBundle_OpensPreviewImmediately()
    {
        var (_, _, page) = LogTestData.CreatePage();
        string? payload = null;
        page.AiAnalysisRequested += (_, path) => payload = path;

        page.RequestAiAnalysisCommand.Execute(null);

        Assert.That(page.IsAiPanelOpen, Is.True);
        Assert.That(page.Ai.IsOpen, Is.True);
        Assert.That(payload, Is.EqualTo(string.Empty));
        Assert.That(page.Ai.PreviewText, Does.Contain("日志通道"));
    }

    [Test]
    public async Task RequestAiAnalysis_AfterExport_RaisesEventWithBundlePath()
    {
        var (_, _, page) = LogTestData.CreatePage(bundle: new FakeLogBundleService());
        await page.ExportBundleAsync("bundle.zip");
        string? payload = null;
        page.AiAnalysisRequested += (_, path) => payload = path;

        page.RequestAiAnalysisCommand.Execute(null);

        Assert.That(payload, Is.EqualTo("bundle.zip"));
        Assert.That(page.IsAiPanelOpen, Is.True);
    }

    [Test]
    public void ActivateLinkCommand_RaisesObjectLinkEvent()
    {
        var (_, _, page) = LogTestData.CreatePage();
        LogObjectLink? activated = null;
        page.ObjectLinkActivated += (_, link) => activated = link;
        var link = new LogObjectLink(LogLinkKind.Url, "https://example.com", "打开链接");

        page.ActivateLinkCommand.Execute(link);

        Assert.That(activated, Is.SameAs(link));
    }

    [Test]
    public void DismissMessageCommand_ClearsMessage()
    {
        var (_, _, page) = LogTestData.CreatePage();
        page.Message = "已导出诊断包";

        page.DismissMessageCommand.Execute(null);

        Assert.That(page.Message, Is.Null);
    }

    [Test]
    public void Refresh_WhenNotFollowingTail_KeepsSelectedRowBySequence()
    {
        var (store, _, page) = LogTestData.CreatePage();
        store.Append(LogTestData.Entry(LogLevel.Info, "first"));
        store.Append(LogTestData.Entry(LogLevel.Info, "second"));
        page.Refresh();

        page.IsFollowTail = false;
        var pinned = page.Rows[1];
        page.SelectedRow = pinned;

        store.Append(LogTestData.Entry(LogLevel.Info, "third"));
        page.Refresh();

        Assert.That(page.Rows[^1].Message, Is.EqualTo("third"));
        Assert.That(page.SelectedRow!.Seq, Is.EqualTo(pinned.Seq), "不跟随时不要被后台新日志抢走详情面板");
    }

    [Test]
    public void Activate_SubscribesToStoreChangesAndDeactivateUnsubscribes()
    {
        var (store, _, page) = LogTestData.CreatePage();
        page.Activate();

        store.Append(LogTestData.Entry(LogLevel.Error, "boom"));
        Assert.That(page.Rows.Count, Is.EqualTo(1), "激活后新日志要自动出现在列表里");

        page.Deactivate();
        store.Append(LogTestData.Entry(LogLevel.Error, "ignored"));
        Assert.That(page.Rows.Count, Is.EqualTo(1), "离开页面后不该继续刷新");
    }

    [Test]
    public void Reactivating_WithoutNewEntries_ReusesTheRowsAlreadyBuilt()
    {
        var (store, _, page) = LogTestData.CreatePage();
        store.Append(LogTestData.Entry(LogLevel.Error, "boom"));
        page.Activate();
        var firstRow = page.Rows[0];

        page.Deactivate();
        page.Activate();

        Assert.That(page.Rows.Count, Is.EqualTo(1));
        Assert.That(page.Rows[0], Is.SameAs(firstRow), "存储版本没变时不该重建列表（§42）");

        store.Append(LogTestData.Entry(LogLevel.Warn, "new one"));
        Assert.That(page.Rows.Count, Is.EqualTo(2), "有新事件时照旧自动刷新");
    }

    [Test]
    public void Refresh_AppendsNewRowsWithoutRebuildingTheList()
    {
        var (store, _, page) = LogTestData.CreatePage();
        store.Append(LogTestData.Entry(LogLevel.Info, "first"));
        page.Refresh();
        var firstRow = page.Rows[0];
        var actions = new System.Collections.Generic.List<System.Collections.Specialized.NotifyCollectionChangedAction>();
        page.Rows.CollectionChanged += (_, e) => actions.Add(e.Action);

        store.Append(LogTestData.Entry(LogLevel.Info, "second"));
        page.Refresh();

        Assert.That(page.Rows[0], Is.SameAs(firstRow), "只有尾部新增时不能重建整张表：Reset 会让虚拟化列表滚回顶部");
        Assert.That(actions, Does.Not.Contain(System.Collections.Specialized.NotifyCollectionChangedAction.Reset));
        Assert.That(actions, Does.Contain(System.Collections.Specialized.NotifyCollectionChangedAction.Add));
        Assert.That(page.Rows[^1].Message, Is.EqualTo("second"));
    }

    [Test]
    public void BuildDefaultBundlePath_UsesLogDirectoryAndTimestamp()
    {
        var path = LogPageViewModel.BuildDefaultBundlePath(new DateTime(2026, 3, 7, 14, 5, 9, DateTimeKind.Local));

        Assert.That(Path.GetFileName(path), Is.EqualTo("ckanui-bundle-20260307-140509.zip"));
        Assert.That(path, Does.Contain("CKAN"));
    }

    [Test]
    public void Constants_MatchDocumentedBudgets()
    {
        Assert.That(LogPageViewModel.RowLimit, Is.EqualTo(500));
        Assert.That(LogPageViewModel.TimelineBucketCount, Is.EqualTo(60));
        Assert.That(LogPageViewModel.TopFingerprintCount, Is.EqualTo(20));
    }
}
