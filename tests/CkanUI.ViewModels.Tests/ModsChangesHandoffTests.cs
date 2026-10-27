using CkanUI.Services.Abstractions;
using CkanUI.Services.Abstractions.Dtos;
using CkanUI.ViewModels.Navigation;
using CkanUI.ViewModels.Pages;
using CkanUI.ViewModels.Pages.Mods;
using NUnit.Framework;

namespace CkanUI.ViewModels.Tests;

/// <summary>模组页 → 变更集页的交接（UI-04 交接 / IChangeRequestSink）。</summary>
[TestFixture]
public sealed class ModsChangesHandoffTests
{
    private FakeRepositoryService _repositories = null!;
    private FakeRegistryService _registry = null!;
    private FakeInstanceService _instances = null!;
    private RecordingErrorReporter _errors = null!;
    private RecordingSink _sink = null!;
    private RecordingNavigation _navigation = null!;

    [SetUp]
    public void SetUp()
    {
        _repositories = new FakeRepositoryService();
        _registry = new FakeRegistryService();
        _instances = new FakeInstanceService { Current = ModsRowFactory.Instance() };
        _errors = new RecordingErrorReporter();
        _sink = new RecordingSink();
        _navigation = new RecordingNavigation();
    }

    private ModsPageViewModel CreateViewModel() =>
        new(_repositories, _registry, _instances, _errors, changeSink: _sink, navigation: _navigation)
        {
            Scheduler = new ImmediateDelayScheduler(),
        };

    private void SeedRows()
    {
        _repositories.Current = ModsRowFactory.Repository(
            ModsRowFactory.Create("Alpha", "Alpha", "zoe", latestVersion: "1.0.0", downloadCount: 10),
            ModsRowFactory.Create("Bravo", "Bravo", "adam", latestVersion: "2.0.0", downloadCount: 500));
    }

    [Test]
    public async Task ReviewChanges_PushesSelectionToSinkAndNavigates()
    {
        SeedRows();
        var vm = CreateViewModel();
        vm.Activate();
        vm.ToggleSelection("Alpha");

        await vm.ReviewChangesCommand.ExecuteAsync(null);

        Assert.That(_sink.Calls, Is.EqualTo(1));
        Assert.That(_sink.Last, Is.Not.Null);
        Assert.That(_sink.Last!.Select(r => r.Identifier), Is.EqualTo(new[] { "Alpha" }));
        Assert.That(_navigation.Requested, Is.EqualTo(new[] { "changes" }));
    }

    [Test]
    public async Task ReviewChanges_WithNoSelection_DoesNotNavigate()
    {
        SeedRows();
        var vm = CreateViewModel();
        vm.Activate();

        await vm.ReviewChangesCommand.ExecuteAsync(null);

        Assert.That(_sink.Calls, Is.Zero);
        Assert.That(_navigation.Requested, Is.Empty);
        Assert.That(vm.StatusText, Is.EqualTo("请先勾选要变更的模组。"));
    }

    [Test]
    public async Task ReviewChanges_WithoutNavigation_KeepsSinkButReportsStatus()
    {
        SeedRows();
        var vm = new ModsPageViewModel(_repositories, _registry, _instances, _errors, changeSink: _sink)
        {
            Scheduler = new ImmediateDelayScheduler(),
        };
        vm.Activate();
        vm.ToggleSelection("Bravo");

        await vm.ReviewChangesCommand.ExecuteAsync(null);

        Assert.That(_sink.Calls, Is.EqualTo(1));
        Assert.That(vm.StatusText, Is.EqualTo("当前没有可用的导航服务，无法打开变更集页。"));
    }

    [Test]
    public void BuildChangeRequests_CarriesTargetVersion()
    {
        SeedRows();
        var vm = CreateViewModel();
        vm.Activate();
        vm.ToggleSelection("Alpha");
        vm.ToggleSelection("Bravo");

        var requests = vm.BuildChangeRequests();

        Assert.That(requests.Count, Is.EqualTo(2));
        Assert.That(requests.Select(r => r.Identifier), Is.EquivalentTo(new[] { "Alpha", "Bravo" }));
        Assert.That(requests.All(r => r.TargetVersion is not null), Is.True);
    }

    [Test]
    public void ChangesPageViewModel_IsSink_AndDeduplicatesRequests()
    {
        IChangeRequestSink sink = new ChangesPageViewModel();
        sink.SetRequests(new[]
        {
            new ChangeRequest("Alpha", ChangeAction.Install, "1.0.0"),
            new ChangeRequest("alpha", ChangeAction.Install, "1.0.0"),
            new ChangeRequest("  ", ChangeAction.Install),
        });

        Assert.That(((ChangesPageViewModel)sink).Requests.Select(r => r.Identifier), Is.EqualTo(new[] { "Alpha" }));

        sink.SetRequests(null);
        Assert.That(((ChangesPageViewModel)sink).Requests, Is.Empty);
    }

    private sealed class RecordingSink : IChangeRequestSink
    {
        public int Calls { get; private set; }

        public IReadOnlyCollection<ChangeRequest>? Last { get; private set; }

        public void SetRequests(IReadOnlyCollection<ChangeRequest>? requests)
        {
            Calls++;
            Last = requests;
        }
    }

    private sealed class RecordingNavigation : INavigationService
    {
        public object? CurrentPage => null;

        public string CurrentKey => "mods";

        public List<string> Requested { get; } = new();

        public event Action<object?, string>? Navigated;

        public Task<bool> NavigateAsync(string pageKey, object? parameter = null, CancellationToken ct = default)
        {
            Requested.Add(pageKey);
            Navigated?.Invoke(null, pageKey);
            return Task.FromResult(true);
        }
    }
}
