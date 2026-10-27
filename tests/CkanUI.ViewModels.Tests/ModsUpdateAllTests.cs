using CkanUI.Services.Abstractions;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CkanUI.Services.Abstractions.Dtos;
using CkanUI.ViewModels.Navigation;
using CkanUI.ViewModels.Pages;
using NUnit.Framework;

namespace CkanUI.ViewModels.Tests;

[TestFixture]
public sealed class ModsUpdateAllTests
{
    private FakeRepositoryService _repositories = null!;
    private FakeRegistryService _registry = null!;
    private FakeInstanceService _instances = null!;
    private FakeErrorReporter _errors = null!;
    private RecordingSink _sink = null!;
    private RecordingNavigation _nav = null!;

    [SetUp]
    public void SetUp()
    {
        _repositories = new FakeRepositoryService();
        _registry = new FakeRegistryService();
        _instances = new FakeInstanceService();
        _errors = new FakeErrorReporter();
        _sink = new RecordingSink();
        _nav = new RecordingNavigation();
    }

    private ModsPageViewModel CreateViewModel(params ModRowData[] mods)
    {
        _repositories.Current = ModsRowFactory.Repository(mods);
        _instances.Current = ModsRowFactory.Instance();
        var vm = new ModsPageViewModel(_repositories, _registry, _instances, _errors, null, null, _sink, _nav)
        {
            Scheduler = new ImmediateDelayScheduler()
        };
        vm.Activate();
        return vm;
    }

    [Test]
    public void HasUpdates_ReflectsWhetherAnyModHasUpdate()
    {
        var mod1 = ModsRowFactory.Create("ModA", installedVersion: "1.0", latestVersion: "2.0");
        var mod2 = ModsRowFactory.Create("ModB", installedVersion: "1.0", latestVersion: "1.0");

        var vm = CreateViewModel(mod1, mod2);

        Assert.That(vm.HasUpdates, Is.True);
    }

    [Test]
    public void UpdateAll_NoUpdatableMods_Disabled()
    {
        var mod = ModsRowFactory.Create("ModA", installedVersion: "1.0", latestVersion: "1.0");
        var vm = CreateViewModel(mod);

        Assert.That(vm.HasUpdates, Is.False);
        Assert.That(vm.UpdateAllCommand.CanExecute(null), Is.False);
    }

    [Test]
    public void UpdateAll_AddsAllUpdatableModsToSelection()
    {
        var mod1 = ModsRowFactory.Create("ModA", installedVersion: "1.0", latestVersion: "2.0");
        var mod2 = ModsRowFactory.Create("ModB", installedVersion: "1.0", latestVersion: "1.0");
        var mod3 = ModsRowFactory.Create("ModC", installedVersion: "0.9", latestVersion: "1.1");

        var vm = CreateViewModel(mod1, mod2, mod3);

        vm.UpdateAllCommand.Execute(null);

        Assert.That(vm.SelectedCount, Is.EqualTo(2));
        var requests = vm.BuildChangeRequests();
        Assert.That(requests.Count, Is.EqualTo(2));
        Assert.That(requests.Select(r => r.Identifier), Is.EquivalentTo(new[] { "ModA", "ModC" }));
        Assert.That(vm.StatusText, Does.Contain("2 个有更新的模组"));
    }

    [Test]
    public async Task ApplyChanges_WithSelection_DelegatesToReviewChangesAndNavigates()
    {
        var mod = ModsRowFactory.Create("ModA", installedVersion: "1.0", latestVersion: "2.0");
        var vm = CreateViewModel(mod);

        vm.UpdateAllCommand.Execute(null);
        await vm.ApplyChangesCommand.ExecuteAsync(null);

        Assert.That(_sink.Last, Is.Not.Null);
        Assert.That(_sink.Last!.Count, Is.EqualTo(1));
        Assert.That(_nav.Requested, Contains.Item("changes"));
    }

    [Test]
    public void ApplyChanges_WithoutSelection_CommandDisabled()
    {
        var mod = ModsRowFactory.Create("ModA", installedVersion: "1.0", latestVersion: "1.0");
        var vm = CreateViewModel(mod);

        Assert.That(vm.ApplyChangesCommand.CanExecute(null), Is.False);
        Assert.That(_nav.Requested, Is.Empty);
    }

    private sealed class RecordingSink : IChangeRequestSink
    {
        public IReadOnlyCollection<ChangeRequest>? Last { get; private set; }

        public void SetRequests(IReadOnlyCollection<ChangeRequest>? requests) => Last = requests;
    }

    private sealed class RecordingNavigation : INavigationService
    {
        public object? CurrentPage => null;
        public string CurrentKey => "mods";
        public List<string> Requested { get; } = new();

        public event Action<object?, string>? Navigated
        {
            add { }
            remove { }
        }

        public Task<bool> NavigateAsync(string pageKey, object? parameter = null, CancellationToken ct = default)
        {
            Requested.Add(pageKey);
            return Task.FromResult(true);
        }
    }
}
