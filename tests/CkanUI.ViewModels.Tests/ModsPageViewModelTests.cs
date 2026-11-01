using CkanUI.Services.Abstractions;
using CkanUI.Services.Abstractions.Dtos;
using CkanUI.ViewModels.Pages;
using CkanUI.ViewModels.Pages.Mods;
using NUnit.Framework;

namespace CkanUI.ViewModels.Tests;

/// <summary>模组页 VM（03 篇 §3/§10、04 篇 §2.1、06 篇 UI-02/VM-01）。</summary>
[TestFixture]
public sealed class ModsPageViewModelTests
{
    private FakeRepositoryService _repositories = null!;
    private FakeRegistryService _registry = null!;
    private FakeInstanceService _instances = null!;
    private RecordingErrorReporter _errors = null!;

    [SetUp]
    public void SetUp()
    {
        _repositories = new FakeRepositoryService();
        _registry = new FakeRegistryService();
        _instances = new FakeInstanceService { Current = ModsRowFactory.Instance() };
        _errors = new RecordingErrorReporter();
    }

    private ModsPageViewModel CreateViewModel(
        IModsDelayScheduler? scheduler = null,
        IModSearchParser? searchParser = null) =>
        new(_repositories, _registry, _instances, _errors, searchParser: searchParser ?? Search.Parser)
        {
            Scheduler = scheduler ?? new ImmediateDelayScheduler(),
        };

    private void SeedThreeRows()
    {
        _repositories.Current = ModsRowFactory.Repository(
            ModsRowFactory.Create("Alpha", "Alpha", "zoe", latestVersion: "1.0.0", downloadCount: 10),
            ModsRowFactory.Create("Bravo", "Bravo", "adam", latestVersion: "2.0.0", downloadCount: 500),
            ModsRowFactory.Create("Charlie", "Charlie", "mike", latestVersion: "3.0.0", downloadCount: 50));
    }

    [Test]
    public void NoInstance_ShowsNoInstanceEmptyState()
    {
        _instances.Current = null;
        var vm = CreateViewModel();

        vm.Activate();

        Assert.That(vm.Rows, Is.Empty);
        Assert.That(vm.ShowEmptyState, Is.True);
        Assert.That(vm.EmptyStateTitle, Is.EqualTo("未找到 KSP 实例"));
        Assert.That(vm.CanRefreshFromEmptyState, Is.False);
    }

    [Test]
    public void EmptyRepository_WithInstance_ShowsIndexEmptyState()
    {
        var vm = CreateViewModel();

        vm.Activate();

        Assert.That(vm.ShowEmptyState, Is.True);
        Assert.That(vm.EmptyStateTitle, Is.EqualTo("尚未加载模组索引"));
        Assert.That(vm.CanRefreshFromEmptyState, Is.True);
    }

    [Test]
    public void Activate_ProjectsRepositoryAndRegistry()
    {
        _repositories.Current = ModsRowFactory.Repository(
            ModsRowFactory.Create("A", "模组 A", latestVersion: "2.0.0"),
            ModsRowFactory.Create("B", "模组 B", latestVersion: "1.0.0"));
        _registry.Current = ModsRowFactory.Registry(
            ModsRowFactory.Installed("A", "1.0.0"),
            ModsRowFactory.Installed("LocalOnly", "0.1.0", auto: true));
        var vm = CreateViewModel();

        vm.Activate();

        Assert.That(vm.Rows, Has.Count.EqualTo(3));
        Assert.That(vm.TotalCount, Is.EqualTo(3));
        var installed = vm.Rows.Single(row => row.Identifier == "A");
        Assert.That(installed.IsInstalled, Is.True);
        Assert.That(installed.HasUpdate, Is.True);
        Assert.That(vm.Rows.Single(row => row.Identifier == "B").IsInstalled, Is.False);
    }

    [Test]
    public void DefaultCompatibilityFilter_IsCompatibleOnly()
    {
        _repositories.Current = ModsRowFactory.Repository(
            ModsRowFactory.Create("Ok", "可用模组"),
            ModsRowFactory.Create("Bad", "不兼容模组", isCompatible: false));
        var vm = CreateViewModel();
        vm.Activate();

        Assert.That(vm.CompatibilityFilter, Is.EqualTo(ModsCompatibilityFilter.CompatibleOnly));
        Assert.That(vm.Rows, Has.Count.EqualTo(1));

        vm.CompatibilityFilter = ModsCompatibilityFilter.All;

        Assert.That(vm.Rows, Has.Count.EqualTo(2));
        Assert.That(vm.CompatibilityFilterIndex, Is.EqualTo((int)ModsCompatibilityFilter.All));
    }

    [Test]
    public void Search_IsDebounced_AndOnlyLastKeystrokeApplies()
    {
        SeedThreeRows();
        var scheduler = new ControlledDelayScheduler();
        var vm = CreateViewModel(scheduler);
        vm.Activate();

        vm.SearchText = "a";
        vm.SearchText = "alp";

        // 防抖期内不重建列表：两次输入都还没落定。
        Assert.That(scheduler.Requested, Has.Count.EqualTo(2));
        Assert.That(vm.Rows, Has.Count.EqualTo(3));

        scheduler.ReleaseAll();
        vm.PendingSearch.GetAwaiter().GetResult();

        Assert.That(vm.Rows, Has.Count.EqualTo(1));
        Assert.That(vm.Rows[0].Identifier, Is.EqualTo("Alpha"));
    }

    [Test]
    public void SearchDebounce_StaysWithinAcceptanceBudget()
    {
        // 06 篇 VM-01 验收：搜索延迟 ≤100ms。
        Assert.That(ModsPageViewModel.SearchDebounceMilliseconds, Is.LessThanOrEqualTo(100));
        Assert.That(CreateViewModel().SearchDebounce, Is.LessThanOrEqualTo(TimeSpan.FromMilliseconds(100)));
    }

    [Test]
    public void Deactivate_CancelsPendingSearch_WithoutApplying()
    {
        SeedThreeRows();
        var scheduler = new ControlledDelayScheduler();
        var vm = CreateViewModel(scheduler);
        vm.Activate();

        vm.SearchText = "alp";
        vm.Deactivate();
        scheduler.ReleaseAll();
        vm.PendingSearch.GetAwaiter().GetResult();

        Assert.That(vm.Rows, Has.Count.EqualTo(3));
    }

    [Test]
    public void UnsupportedSearchTerm_IsShownInStatusBar()
    {
        // 无法解析的词必须进 UnsupportedTerms 并在状态栏说明，不能静默忽略。
        _repositories.Current = ModsRowFactory.Repository(
            ModsRowFactory.Create("Alpha", "Alpha", "zoe", latestVersion: "1.0.0"));
        var vm = CreateViewModel();
        vm.Activate();

        vm.SearchText = "is:bogus";
        vm.PendingSearch.GetAwaiter().GetResult();

        Assert.That(vm.HasUnsupportedQueryTerms, Is.True);
        Assert.That(vm.StatusText, Does.Contain("当前无法解析：is:bogus"));
        Assert.That(vm.StatusText, Does.Contain("未知的状态值"));
        Assert.That(vm.ActiveFilterChips, Has.Some.EqualTo("无法解析：is:bogus"));
        // 无法解析的条件不参与过滤，行仍然可见。
        Assert.That(vm.Rows, Has.Count.EqualTo(1));
    }

    [Test]
    public void SupportedSyntax_FiltersRows_AndKeepsStatusBarClean()
    {
        _repositories.Current = ModsRowFactory.Repository(
            ModsRowFactory.Create("Alpha", "Alpha", "zoe", latestVersion: "1.0.0",
                facts: new ModSearchFacts { Licenses = new[] { "MIT" } }),
            ModsRowFactory.Create("Bravo", "Bravo", "adam", latestVersion: "2.0.0",
                facts: new ModSearchFacts { Licenses = new[] { "GPL-3.0" } }));
        var vm = CreateViewModel();
        vm.Activate();

        vm.SearchText = "lic:MIT";
        vm.PendingSearch.GetAwaiter().GetResult();

        Assert.That(vm.HasUnsupportedQueryTerms, Is.False);
        Assert.That(vm.StatusText, Does.Not.Contain("当前无法解析"));
        Assert.That(vm.Rows, Has.Count.EqualTo(1));
        Assert.That(vm.Rows[0].Identifier, Is.EqualTo("Alpha"));
    }

    [Test]
    public void ClearSearch_RestoresAllRows()
    {
        SeedThreeRows();
        var vm = CreateViewModel();
        vm.Activate();

        vm.SearchText = "alp";
        vm.PendingSearch.GetAwaiter().GetResult();
        Assert.That(vm.Rows, Has.Count.EqualTo(1));

        vm.ClearSearch();

        Assert.That(vm.SearchText, Is.Empty);
        Assert.That(vm.Rows, Has.Count.EqualTo(3));
    }

    [Test]
    public void ToggleSelection_BuildsChangePreview()
    {
        SeedThreeRows();
        var vm = CreateViewModel();
        vm.Activate();

        vm.ToggleSelection("Alpha");

        Assert.That(vm.SelectedCount, Is.EqualTo(1));
        Assert.That(vm.HasPendingChanges, Is.True);
        Assert.That(vm.ChangePreview.Items, Has.Count.EqualTo(1));
        Assert.That(vm.SelectionSummary, Does.Contain("已选 1"));
        Assert.That(vm.StatusText, Does.Contain("已选 1"));
        Assert.That(vm.ApplyChangesCommand.CanExecute(null), Is.True);

        vm.ToggleSelection("Alpha");

        Assert.That(vm.SelectedCount, Is.Zero);
        Assert.That(vm.HasPendingChanges, Is.False);
        Assert.That(vm.ApplyChangesCommand.CanExecute(null), Is.False);
    }
    [Test]
    public void ToggleSelection_UpdatesRowInPlaceWithoutReplacingCollection()
    {
        SeedThreeRows();
        var vm = CreateViewModel();
        vm.Activate();

        var rows = vm.Rows;
        var bravo = rows.First(r => r.Identifier == "Bravo");

        vm.ToggleSelection("Alpha");

        // 换掉整个集合会让 DataGrid 重建全部行容器并丢滚动位置；勾选必须就地替换。
        Assert.That(vm.Rows, Is.SameAs(rows));
        Assert.That(vm.Rows.First(r => r.Identifier == "Bravo"), Is.SameAs(bravo));
        Assert.That(vm.Rows.First(r => r.Identifier == "Alpha").IsSelected, Is.True);
    }

    [Test]
    public void ToggleSelection_PreservesSelectedRowAndDetail()
    {
        SeedThreeRows();
        var vm = CreateViewModel();
        vm.Activate();

        vm.SelectedRow = vm.Rows.First(r => r.Identifier == "Alpha");
        Assert.That(vm.SelectedRow, Is.Not.Null);
        Assert.That(vm.HasSelectedRow, Is.True);
        Assert.That(vm.DetailIdentifier, Is.EqualTo("Alpha"));

        vm.ToggleSelection("Alpha");

        Assert.That(vm.SelectedRow, Is.Not.Null);
        Assert.That(vm.SelectedRow!.Identifier, Is.EqualTo("Alpha"));
        Assert.That(vm.HasSelectedRow, Is.True);
        Assert.That(vm.DetailIdentifier, Is.EqualTo("Alpha"));
    }

    [Test]
    public void ClearSelection_PreservesSelectedRowAndDetail()
    {
        SeedThreeRows();
        var vm = CreateViewModel();
        vm.Activate();

        vm.ToggleSelection("Alpha");
        vm.SelectedRow = vm.Rows.First(r => r.Identifier == "Alpha");

        vm.ClearSelection();

        Assert.That(vm.SelectedRow, Is.Not.Null);
        Assert.That(vm.SelectedRow!.Identifier, Is.EqualTo("Alpha"));
        Assert.That(vm.HasSelectedRow, Is.True);
        Assert.That(vm.DetailIdentifier, Is.EqualTo("Alpha"));
    }

    [Test]
    public void Selection_SurvivesFilterChange_AndRowShowsChecked()
    {
        SeedThreeRows();
        var vm = CreateViewModel();
        vm.Activate();

        vm.ToggleSelection("Alpha");
        vm.InstallFilter = ModsInstallFilter.Installed;

        // Alpha 未安装，被过滤器隐藏，但选择集与变更集不清空（03 篇 §3.6）。
        Assert.That(vm.Rows, Is.Empty);
        Assert.That(vm.SelectedCount, Is.EqualTo(1));
        Assert.That(vm.ChangePreview.Items, Has.Count.EqualTo(1));

        vm.InstallFilter = ModsInstallFilter.All;

        Assert.That(vm.Rows.Single(row => row.Identifier == "Alpha").IsSelected, Is.True);
    }

    [Test]
    public void RefreshCommand_InvokesBothServices()
    {
        _repositories.RefreshResult = OperationResult<RepositorySnapshot>.Ok(
            ModsRowFactory.Repository(ModsRowFactory.Create("A", "模组 A")));
        _registry.RefreshResult = OperationResult<RegistrySnapshot>.Ok(ModsRowFactory.Registry());
        var vm = CreateViewModel();
        vm.Activate();

        vm.RefreshRepositoriesCommand.ExecuteAsync(null).GetAwaiter().GetResult();

        Assert.That(_repositories.RefreshCount, Is.EqualTo(1));
        Assert.That(_registry.RefreshCount, Is.EqualTo(1));
        Assert.That(vm.Rows, Has.Count.EqualTo(1));
        Assert.That(vm.StatusText, Does.Contain("已刷新"));
        Assert.That(vm.IsBusy, Is.False);
    }

    [Test]
    public void RefreshCommand_SwallowsException_IntoStatusText()
    {
        _repositories.RefreshException = new InvalidOperationException("走网失败");
        var vm = CreateViewModel();
        vm.Activate();

        Assert.DoesNotThrow(() => vm.RefreshRepositoriesCommand.ExecuteAsync(null).GetAwaiter().GetResult());

        Assert.That(_errors.Recent, Has.Count.EqualTo(1));
        Assert.That(vm.StatusText, Does.Contain("刷新失败"));
        Assert.That(vm.IsBusy, Is.False);
    }

    [Test]
    public void RefreshFailure_KeepsOldDataAndReportsMessage()
    {
        SeedThreeRows();
        _repositories.RefreshResult = OperationResult<RepositorySnapshot>.Fail("仓库地址不可达");
        var vm = CreateViewModel();
        vm.Activate();

        vm.RefreshRepositoriesCommand.ExecuteAsync(null).GetAwaiter().GetResult();

        Assert.That(vm.StatusText, Does.Contain("仓库地址不可达"));
        Assert.That(vm.Rows, Has.Count.EqualTo(3));
    }

    [Test]
    public void RegistryChanged_RebuildsRows()
    {
        _repositories.Current = ModsRowFactory.Repository(ModsRowFactory.Create("A", "模组 A", latestVersion: "2.0.0"));
        var vm = CreateViewModel();
        vm.Activate();
        Assert.That(vm.Rows[0].IsInstalled, Is.False);

        _registry.RaiseChanged(ModsRowFactory.Registry(ModsRowFactory.Installed("A", "1.0.0")));

        Assert.That(vm.Rows[0].IsInstalled, Is.True);
        Assert.That(vm.Rows[0].HasUpdate, Is.True);
    }

    [Test]
    public void InstanceChanged_ClearsSelectionSoPagesDoNotLeak()
    {
        SeedThreeRows();
        var vm = CreateViewModel();
        vm.Activate();
        vm.ToggleSelection("Alpha");
        vm.SelectedRow = vm.Rows[0];

        _instances.RaiseCurrentChanged(ModsRowFactory.Instance("另一个实例"));

        Assert.That(vm.SelectedCount, Is.Zero);
        Assert.That(vm.SelectedRow, Is.Null);
        Assert.That(vm.ChangePreview.IsEmpty, Is.True);
        Assert.That(vm.InstanceName, Is.EqualTo("另一个实例"));
    }

    [Test]
    public void SortByColumn_TogglesDirectionForSameColumn()
    {
        SeedThreeRows();
        var vm = CreateViewModel();
        vm.Activate();

        vm.SortByColumn(ModsSortColumn.DownloadCount);

        Assert.That(vm.SortColumn, Is.EqualTo(ModsSortColumn.DownloadCount));
        Assert.That(vm.SortAscending, Is.False);
        Assert.That(vm.Rows[0].Identifier, Is.EqualTo("Bravo"));
        Assert.That(vm.SortColumnIndex, Is.EqualTo((int)ModsSortColumn.DownloadCount));

        vm.SortByColumn(ModsSortColumn.DownloadCount);

        Assert.That(vm.SortAscending, Is.True);
        Assert.That(vm.Rows[0].Identifier, Is.EqualTo("Alpha"));
    }

    [Test]
    public void NoMatches_ShowsNoMatchEmptyStateWithChips()
    {
        SeedThreeRows();
        var vm = CreateViewModel();
        vm.Activate();

        vm.SearchText = "zzz";
        vm.PendingSearch.GetAwaiter().GetResult();

        Assert.That(vm.EmptyStateTitle, Is.EqualTo("没有匹配的模组"));
        Assert.That(vm.ActiveFilterChips, Has.Some.Contains("zzz"));

        vm.ResetFiltersCommand.Execute(null);

        Assert.That(vm.Rows, Has.Count.EqualTo(3));
        Assert.That(vm.ActiveFilterChips, Is.Empty);
    }

    [Test]
    public void DetailPanel_FollowsSelectedRow()
    {
        SeedThreeRows();
        var vm = CreateViewModel();
        vm.Activate();

        Assert.That(vm.HasSelectedRow, Is.False);
        Assert.That(vm.DetailName, Is.EqualTo("未选择模组"));

        vm.SelectedRow = vm.Rows[0];

        Assert.That(vm.HasSelectedRow, Is.True);
        Assert.That(vm.DetailIdentifier, Is.EqualTo("Alpha"));
        Assert.That(vm.DetailAuthor, Is.EqualTo("zoe"));
        Assert.That(vm.DetailStatusText, Is.EqualTo("未安装"));
    }
}
