using CkanUI.Services.Abstractions;
using CkanUI.Services.Abstractions.Dtos;
using CkanUI.ViewModels.Pages;
using CkanUI.ViewModels.Pages.Mods;
using NUnit.Framework;

namespace CkanUI.ViewModels.Tests;

/// <summary>
/// F-54 版本显示格式与「可升级项置顶」：行投影只做纯格式，开关来自 ckan-ui.json（UiSettings）。
/// </summary>
[TestFixture]
public sealed class ModsVersionDisplayTests
{
    private FakeRepositoryService _repositories = null!;
    private FakeRegistryService _registry = null!;
    private FakeInstanceService _instances = null!;
    private RecordingErrorReporter _errors = null!;
    private FakeUiSettingsStore _settings = null!;

    [SetUp]
    public void SetUp()
    {
        _repositories = new FakeRepositoryService();
        _registry = new FakeRegistryService();
        _instances = new FakeInstanceService { Current = ModsRowFactory.Instance() };
        _errors = new RecordingErrorReporter();
        _settings = new FakeUiSettingsStore();
    }

    private ModsPageViewModel CreateViewModel(UiSettings? settings = null)
    {
        if (settings is not null)
        {
            _settings.With(settings);
        }

        return new ModsPageViewModel(_repositories, _registry, _instances, _errors, null, _settings)
        {
            Scheduler = new ImmediateDelayScheduler(),
        };
    }

    [Test]
    public void Row_WithoutFlags_KeepsRawVersionText()
    {
        var row = ModsRowView.From(ModsRowFactory.Create("a", installedVersion: "1:2.0.0", latestVersion: "v3.0.0"));

        Assert.That(row.InstalledVersionText, Is.EqualTo("1:2.0.0"));
        Assert.That(row.LatestVersionText, Is.EqualTo("v3.0.0"));
    }

    [Test]
    public void Row_HideEpochAndVPrefix_StripsPrefixesOnly()
    {
        var row = ModsRowView.From(
            ModsRowFactory.Create("a", installedVersion: "1:2.0.0", latestVersion: "v3.0.0"),
            hideVersionEpoch: true,
            hideVersionVPrefix: true);

        Assert.That(row.InstalledVersionText, Is.EqualTo("2.0.0"));
        Assert.That(row.LatestVersionText, Is.EqualTo("3.0.0"));
    }

    [Test]
    public void Row_HideVPrefix_DoesNotStripBareV()
    {
        var row = ModsRowView.From(ModsRowFactory.Create("a", latestVersion: "version-2"), hideVersionVPrefix: true);

        Assert.That(row.LatestVersionText, Is.EqualTo("version-2"));
    }

    [Test]
    public void Row_MissingVersion_StillShowsDash()
    {
        var row = ModsRowView.From(ModsRowFactory.Create("a", installedVersion: null), hideVersionEpoch: true);

        Assert.That(row.InstalledVersionText, Is.EqualTo("—"));
    }

    [Test]
    public void Page_AppliesDisplayFlagsFromUiSettings()
    {
        _repositories.Current = ModsRowFactory.Repository(
            ModsRowFactory.Create("a", installedVersion: "1:2.0.0", latestVersion: "v3.0.0"));
        var vm = CreateViewModel(new UiSettings { HideVersionEpoch = true, HideVersionVPrefix = true });

        vm.Activate();

        Assert.That(vm.Rows, Has.Count.EqualTo(1));
        Assert.That(vm.Rows[0].InstalledVersionText, Is.EqualTo("2.0.0"));
        Assert.That(vm.Rows[0].LatestVersionText, Is.EqualTo("3.0.0"));
    }

    [Test]
    public void Page_AutoSortUpdates_PutsUpdateRowsFirstByName()
    {
        _repositories.Current = ModsRowFactory.Repository(
            ModsRowFactory.Create("alpha", name: "Alpha", installedVersion: "1.0.0", latestVersion: "1.0.0"),
            ModsRowFactory.Create("zulu", name: "Zulu", installedVersion: "1.0.0", latestVersion: "2.0.0"));
        var vm = CreateViewModel(new UiSettings { AutoSortUpdates = true });

        vm.Activate();

        Assert.That(vm.Rows.Select(row => row.Name), Is.EqualTo(new[] { "Zulu", "Alpha" }));
    }

    [Test]
    public void Page_AutoSortUpdatesOff_KeepsNameOrder()
    {
        _repositories.Current = ModsRowFactory.Repository(
            ModsRowFactory.Create("alpha", name: "Alpha", installedVersion: "1.0.0", latestVersion: "1.0.0"),
            ModsRowFactory.Create("zulu", name: "Zulu", installedVersion: "1.0.0", latestVersion: "2.0.0"));
        var vm = CreateViewModel(new UiSettings { AutoSortUpdates = false });

        vm.Activate();

        Assert.That(vm.Rows.Select(row => row.Name), Is.EqualTo(new[] { "Alpha", "Zulu" }));
    }

    [Test]
    public void Page_AutoSortUpdates_DoesNotOverrideExplicitColumnSort()
    {
        _repositories.Current = ModsRowFactory.Repository(
            ModsRowFactory.Create("alpha", name: "Alpha", installedVersion: "1.0.0", latestVersion: "1.0.0"),
            ModsRowFactory.Create("zulu", name: "Zulu", installedVersion: "1.0.0", latestVersion: "2.0.0"));
        var vm = CreateViewModel(new UiSettings { AutoSortUpdates = true });
        vm.Activate();

        vm.SortByColumn(ModsSortColumn.DownloadCount);

        Assert.That(vm.Rows.Select(row => row.Name), Is.EquivalentTo(new[] { "Alpha", "Zulu" }));
    }
}
