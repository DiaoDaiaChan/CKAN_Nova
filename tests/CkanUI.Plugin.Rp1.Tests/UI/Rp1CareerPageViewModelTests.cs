using CkanUI.Plugin.Rp1.Career;
using CkanUI.Plugin.Rp1.Tests.Support;
using CkanUI.Plugin.Rp1.UI;
using CkanUI.Services.Abstractions;
using CkanUI.Services.Abstractions.Dtos;
using NUnit.Framework;

namespace CkanUI.Plugin.Rp1.Tests.UI;

[TestFixture]
public sealed class Rp1CareerPageViewModelTests
{
    [Test]
    public async Task Start_LoadsSavesAndShowsTheNewestTimeline()
    {
        using var game = new TempGame();
        var timeline = Timeline();
        var career = new FakeCareerService { Timeline = timeline };
        career.Saves.Add(new Rp1SaveSummary("Test", Path.Combine(game.Root, "saves", "Test"), Path.Combine(game.Root, "saves", "Test", "persistent.sfs"), DateTime.UtcNow, 123));
        var instances = new FakeInstanceService { Current = new InstanceInfo("Test Instance", game.Root, "1.12.5", false, false, null) };
        using var vm = new Rp1CareerPageViewModel(instances, career);

        vm.Start();
        await WaitUntil(() => vm.ShowContent && career.LoadCalls == 1);

        Assert.Multiple(() =>
        {
            Assert.That(vm.InstanceName, Is.EqualTo("Test Instance (1.12.5)"));
            Assert.That(vm.Saves, Has.Count.EqualTo(1));
            Assert.That(vm.SelectedSave?.Name, Is.EqualTo("Test"));
            Assert.That(vm.ContractCountText, Is.EqualTo("1"));
            Assert.That(vm.MilestoneCountText, Is.EqualTo("1"));
            Assert.That(vm.ProgramCountText, Is.EqualTo("1"));
            Assert.That(vm.CrewCountText, Is.EqualTo("1 / 0"));
            Assert.That(vm.VisibleEvents, Has.Count.EqualTo(4));
            Assert.That(vm.SelectedEvent?.Title, Is.EqualTo("Early Rocket Development"));
            Assert.That(vm.ShowEventDetail, Is.True);
        });
    }

    [Test]
    public async Task FiltersAndSearchNarrowTheTimelineWithoutLosingTheSelection()
    {
        using var game = new TempGame();
        var career = new FakeCareerService { Timeline = Timeline() };
        career.Saves.Add(new Rp1SaveSummary("Test", Path.Combine(game.Root, "saves", "Test"), Path.Combine(game.Root, "saves", "Test", "persistent.sfs"), DateTime.UtcNow, 123));
        var instances = new FakeInstanceService { Current = new InstanceInfo("Test", game.Root, "1.12.5", false, false, null) };
        using var vm = new Rp1CareerPageViewModel(instances, career);
        vm.Start();
        await WaitUntil(() => vm.ShowContent && vm.VisibleEvents.Count == 4);

        vm.SelectedFilter = "里程碑";
        Assert.That(vm.VisibleEvents.Select(item => item.Title), Is.EqualTo(new[] { "Karman Line" }));
        Assert.That(vm.SelectedEvent?.Title, Is.EqualTo("Karman Line"));

        vm.SelectedFilter = "全部";
        vm.SearchText = "Lunar";
        Assert.That(vm.VisibleEvents.Select(item => item.Title), Is.EqualTo(new[] { "First Lunar Impactor" }));
        Assert.That(vm.HasNoVisibleEvents, Is.False);

        vm.SearchText = "nothing-matches";
        Assert.That(vm.VisibleEvents, Is.Empty);
        Assert.That(vm.HasNoVisibleEvents, Is.True);
        Assert.That(vm.SelectedEvent, Is.Null);
    }

    [Test]
    public async Task NoInstanceShowsActionableEmptyState()
    {
        var career = new FakeCareerService { Timeline = Timeline() };
        using var vm = new Rp1CareerPageViewModel(new FakeInstanceService(), career);
        vm.Start();
        await WaitUntil(() => vm.ShowEmptyState);

        Assert.That(vm.EmptyTitle, Is.EqualTo("没有选择游戏实例"));
        Assert.That(vm.EmptyDetail, Does.Contain("实例"));
        Assert.That(career.ListCalls, Is.EqualTo(0));
    }

    [Test]
    public async Task MissingRp1ShowsInstallHint()
    {
        using var game = new TempGame(withRp1: false);
        var career = new FakeCareerService { Timeline = Timeline() };
        var instances = new FakeInstanceService { Current = new InstanceInfo("Test", game.Root, "1.12.5", false, false, null) };
        using var vm = new Rp1CareerPageViewModel(instances, career);
        vm.Start();
        await WaitUntil(() => vm.ShowEmptyState);

        Assert.That(vm.EmptyTitle, Is.EqualTo("这个实例没有安装 RP-1"));
        Assert.That(career.ListCalls, Is.EqualTo(0));
    }

    [Test]
    public async Task LoadFailureIsReportedAndDoesNotPretendToHaveContent()
    {
        using var game = new TempGame();
        var career = new FakeCareerService
        {
            LoadResult = OperationResult<Rp1CareerTimeline>.Fail("坏存档"),
        };
        career.Saves.Add(new Rp1SaveSummary("Test", Path.Combine(game.Root, "saves", "Test"), Path.Combine(game.Root, "saves", "Test", "persistent.sfs"), DateTime.UtcNow, 123));
        var instances = new FakeInstanceService { Current = new InstanceInfo("Test", game.Root, "1.12.5", false, false, null) };
        using var vm = new Rp1CareerPageViewModel(instances, career);
        vm.Start();
        await WaitUntil(() => vm.ShowEmptyState && career.LoadCalls == 1);

        Assert.That(vm.EmptyTitle, Is.EqualTo("读不出这个存档"));
        Assert.That(vm.EmptyDetail, Does.Contain("坏存档"));
        Assert.That(vm.ShowContent, Is.False);
    }

    [Test]
    public async Task SwitchingInstanceReloadsTheSaveList()
    {
        using var first = new TempGame();
        using var second = new TempGame();
        var career = new FakeCareerService { Timeline = Timeline() };
        career.Saves.Add(new Rp1SaveSummary("Test", Path.Combine(first.Root, "saves", "Test"), Path.Combine(first.Root, "saves", "Test", "persistent.sfs"), DateTime.UtcNow, 123));
        var instances = new FakeInstanceService { Current = new InstanceInfo("First", first.Root, "1.12.5", false, false, null) };
        using var vm = new Rp1CareerPageViewModel(instances, career);
        vm.Start();
        await WaitUntil(() => career.ListCalls == 1);

        instances.RaiseCurrentChanged(new InstanceInfo("Second", second.Root, "1.12.5", false, false, null));
        await WaitUntil(() => career.ListCalls == 2);

        Assert.That(vm.InstanceName, Is.EqualTo("Second (1.12.5)"));
        Assert.That(vm.InstanceGameDir, Is.EqualTo(second.Root));
    }

    [Test]
    public async Task InteractiveFiltersAndNavigationWorkAsExpected()
    {
        using var game = new TempGame();
        var career = new FakeCareerService { Timeline = Timeline() };
        career.Saves.Add(new Rp1SaveSummary("Test", Path.Combine(game.Root, "saves", "Test"), Path.Combine(game.Root, "saves", "Test", "persistent.sfs"), DateTime.UtcNow, 123));
        var instances = new FakeInstanceService { Current = new InstanceInfo("Test", game.Root, "1.12.5", false, false, null) };
        using var vm = new Rp1CareerPageViewModel(instances, career);
        vm.Start();
        await WaitUntil(() => vm.ShowContent && vm.VisibleEvents.Count == 4);

        // Filter chips command
        vm.SelectFilter("里程碑");
        Assert.Multiple(() =>
        {
            Assert.That(vm.SelectedFilter, Is.EqualTo("里程碑"));
            Assert.That(vm.IsFilterMilestone, Is.True);
            Assert.That(vm.IsFilterAll, Is.False);
            Assert.That(vm.VisibleEvents.Count, Is.EqualTo(1));
            Assert.That(vm.VisibleEvents[0].Title, Is.EqualTo("Karman Line"));
            Assert.That(vm.VisibleEvents[0].IsMilestone, Is.True);
            Assert.That(vm.VisibleEvents[0].GlyphName, Is.EqualTo("Milestone"));
            Assert.That(vm.VisibleEvents[0].NodeClass, Is.EqualTo("milestone"));
        });

        // FilterByStat command
        vm.FilterByStat("Contract");
        Assert.Multiple(() =>
        {
            Assert.That(vm.SelectedFilter, Is.EqualTo("合同"));
            Assert.That(vm.IsFilterContract, Is.True);
            Assert.That(vm.VisibleEvents.Count, Is.EqualTo(1));
            Assert.That(vm.VisibleEvents[0].Title, Is.EqualTo("First Lunar Impactor"));
        });

        // Open metric curve switches tab
        vm.OpenMetricCurve("science");
        Assert.Multiple(() =>
        {
            Assert.That(vm.SelectedTabIndex, Is.EqualTo(1));
            Assert.That(vm.CareerMetricKey, Is.EqualTo("science"));
        });

        // Reset filter to All and test navigation
        vm.SelectFilter("全部");
        Assert.That(vm.VisibleEvents.Count, Is.EqualTo(4));
        vm.JumpToEarliest();
        Assert.That(vm.SelectedEvent?.Title, Is.EqualTo("Early Rocket Development"));

        vm.SelectNextEvent();
        Assert.That(vm.SelectedEvent?.Title, Is.EqualTo("Karman Line"));

        vm.SelectPreviousEvent();
        Assert.That(vm.SelectedEvent?.Title, Is.EqualTo("Early Rocket Development"));

        vm.JumpToLatest();
        Assert.That(vm.SelectedEvent?.Title, Is.EqualTo("Jeb 退役"));

        vm.JumpToMilestone();
        Assert.That(vm.SelectedEvent?.Title, Is.EqualTo("Karman Line"));
    }

    [Test]
    public async Task TimelineAndCareerCurveBidirectionalLinkageWorks()
    {
        using var game = new TempGame();
        var periods = new[]
        {
            new Rp1CareerPeriod(0, 0, 300, "1951-01-01 00:05", 1000, 10, 5, 10, 20, 0, 100),
            new Rp1CareerPeriod(1, 300, 600, "1951-01-01 00:10", 1500, 15, 8, 12, 25, 0, 200),
            new Rp1CareerPeriod(2, 600, 1000, "1951-01-01 00:16", 2000, 20, 12, 15, 30, 0, 300),
        };
        var events = new[]
        {
            new Rp1CareerEvent(100, "1951-01-01 00:01", Rp1EventKind.ProgramStart, "Early Rocket Development", null, "Sounding rockets.", "SuborbRocketDev", "项目"),
            new Rp1CareerEvent(450, "1951-01-01 00:07", Rp1EventKind.Milestone, "Karman Line", "探空1号", "ROCKET REACHES SPACE", "KarmanLineMilestone", "里程碑"),
            new Rp1CareerEvent(700, "1951-01-01 00:11", Rp1EventKind.CrewRetired, "Jeb 退役", "人员", null, "Jeb", "退役"),
        };
        var timeline = new Rp1CareerTimeline(
            "Test",
            Path.Combine(Path.GetTempPath(), "Test", "persistent.sfs"),
            Path.GetTempPath(),
            "4.7.0.0",
            "1.12.5",
            5000,
            "1951-01-01 01:23",
            true,
            events,
            periods,
            1, 1, 1, 1, 0, 1000, 10, 10, 20, []);

        var career = new FakeCareerService { Timeline = timeline };
        career.Saves.Add(new Rp1SaveSummary("Test", Path.Combine(game.Root, "saves", "Test"), Path.Combine(game.Root, "saves", "Test", "persistent.sfs"), DateTime.UtcNow, 123));
        var instances = new FakeInstanceService { Current = new InstanceInfo("Test", game.Root, "1.12.5", false, false, null) };
        using var vm = new Rp1CareerPageViewModel(instances, career);
        vm.Start();
        await WaitUntil(() => vm.ShowContent && vm.VisibleEvents.Count == 3);

        // 1. Initial selection: first event (Ut=100) -> links to period 0
        Assert.Multiple(() =>
        {
            Assert.That(vm.SelectedEvent?.Title, Is.EqualTo("Early Rocket Development"));
            Assert.That(vm.LinkedPeriodIndex, Is.EqualTo(0));
            Assert.That(vm.LinkedPeriod?.Index, Is.EqualTo(0));
            Assert.That(vm.HasLinkedPeriod, Is.True);
            Assert.That(vm.Periods.Single(r => r.Period.Index == 0).IsSelected, Is.True);
            Assert.That(vm.Periods.Single(r => r.Period.Index == 1).IsSelected, Is.False);
            Assert.That(vm.LinkedEventLabel, Does.Contain("Early Rocket Development"));
        });

        // 2. Select next event: Karman Line (Ut=450) -> links to period 1
        vm.SelectNextEvent();
        Assert.Multiple(() =>
        {
            Assert.That(vm.SelectedEvent?.Title, Is.EqualTo("Karman Line"));
            Assert.That(vm.LinkedPeriodIndex, Is.EqualTo(1));
            Assert.That(vm.LinkedPeriod?.Index, Is.EqualTo(1));
            Assert.That(vm.Periods.Single(r => r.Period.Index == 1).IsSelected, Is.True);
            Assert.That(vm.Periods.Single(r => r.Period.Index == 0).IsSelected, Is.False);
        });

        // 3. Open career curve for current event switches tab and sets scope
        vm.OpenCareerCurveForCurrentEvent();
        Assert.Multiple(() =>
        {
            Assert.That(vm.SelectedTabIndex, Is.EqualTo(1));
            Assert.That(vm.PeriodScope, Is.EqualTo("linked"));
            Assert.That(vm.IsLinkedPeriodScope, Is.True);
        });

        // 4. Reverse link: jump to period 2 from curve point click
        vm.JumpToPeriodIndex(2);
        Assert.Multiple(() =>
        {
            Assert.That(vm.LinkedPeriodIndex, Is.EqualTo(2));
            Assert.That(vm.LinkedPeriod?.Index, Is.EqualTo(2));
            Assert.That(vm.SelectedEvent?.Title, Is.EqualTo("Jeb 退役"));
            Assert.That(vm.Periods.Single(r => r.Period.Index == 2).IsSelected, Is.True);
        });

        // 5. Jump back to timeline tab from curve status
        vm.JumpToSelectedEvent();
        Assert.That(vm.SelectedTabIndex, Is.EqualTo(0));

        // 6. Test period row selection directly
        var row0 = vm.Periods.Single(r => r.Period.Index == 0);
        vm.SelectPeriod(row0);
        Assert.Multiple(() =>
        {
            Assert.That(vm.LinkedPeriodIndex, Is.EqualTo(0));
            Assert.That(vm.SelectedEvent?.Title, Is.EqualTo("Early Rocket Development"));
        });

        // 7. Test period scope switching
        vm.SetPeriodScope("all");
        Assert.That(vm.IsAllPeriodScope, Is.True);
        vm.SetPeriodScope("recent");
        Assert.That(vm.IsRecentPeriodScope, Is.True);
    }


    private static async Task WaitUntil(Func<bool> condition, int timeoutMs = 5000)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        while (DateTime.UtcNow < deadline)
        {
            if (condition())
            {
                return;
            }

            await Task.Delay(20);
        }

        Assert.Fail("等待页面状态超时。");
    }

    private static Rp1CareerTimeline Timeline()
        => new(
            "Test",
            Path.Combine(Path.GetTempPath(), "Test", "persistent.sfs"),
            Path.GetTempPath(),
            "4.7.0.0",
            "1.12.5",
            5000,
            "1951-01-01 01:23",
            true,
            [
                new Rp1CareerEvent(100, "1951-01-01 00:01", Rp1EventKind.ProgramStart, "Early Rocket Development", null, "Sounding rockets.", "SuborbRocketDev", "项目"),
                new Rp1CareerEvent(450, "1951-01-01 00:07", Rp1EventKind.Milestone, "Karman Line", "探空1号", "ROCKET REACHES SPACE", "KarmanLineMilestone", "里程碑"),
                new Rp1CareerEvent(500, "1951-01-01 00:08", Rp1EventKind.Contract, "First Lunar Impactor", null, "Impact the Moon.", "LunarImpactor", "合同"),
                new Rp1CareerEvent(700, "1951-01-01 00:11", Rp1EventKind.CrewRetired, "Jeb 退役", "人员", null, "Jeb", "退役"),
            ],
            [new Rp1CareerPeriod(0, 0, 1000, "1951-01-01 00:16", 1000, 10, 5, 10, 20, 0, 100)],
            1,
            1,
            1,
            1,
            0,
            1000,
            10,
            10,
            20,
            []);
}
