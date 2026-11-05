using CkanUI.Services.Abstractions.Dtos;
using CkanUI.ViewModels.Dialogs;
using CkanUI.ViewModels.Pages;
using CkanUI.ViewModels.Pages.Mods;
using NUnit.Framework;

namespace CkanUI.ViewModels.Tests;

/// <summary>
/// 联动模组的三个落点：列表管线过滤、行徽章、详情面板说明。
/// 只测纯函数与投影，不起任何服务 I/O。
/// </summary>
[TestFixture]
public sealed class ModsCompanionSurfaceTests
{
    private static readonly ModRowData Kos = ModsRowFactory.Create("kOS", "kOS: Scriptable Autopilot System");
    private static readonly ModRowData MechJeb = ModsRowFactory.Create("MechJeb2", "MechJeb 2");
    private static readonly ModRowData Rp1 = ModsRowFactory.Create("RP-1", "Realistic Progression One");

    private static readonly ModRowData[] All = { Kos, MechJeb, Rp1 };

    [Test]
    public void Pipeline_CompanionOnly_KeepsOnlyLinkedRows()
    {
        var rows = ModsListPipeline.Apply(
            All, Search.Parse(null), ModsInstallFilter.All, ModsCompatibilityFilter.All,
            ModsSortColumn.Name, true, companionOnly: true);

        Assert.That(rows.Select(row => row.Identifier), Is.EquivalentTo(new[] { "kOS", "RP-1" }));
    }

    [Test]
    public void Pipeline_DefaultKeepsEveryRowSoSwitchingBackRestoresTheList()
    {
        var rows = ModsListPipeline.Apply(
            All, Search.Parse(null), ModsInstallFilter.All, ModsCompatibilityFilter.All,
            ModsSortColumn.Name, true);

        Assert.That(rows, Has.Count.EqualTo(3));
    }

    [Test]
    public void RowView_ShowsCompanionBadgeOnlyForLinkedMods()
    {
        var kos = new ModsRowView(Kos);
        var mechJeb = new ModsRowView(MechJeb);

        Assert.That(kos.ShowCompanionBadge, Is.True);
        Assert.That(kos.CompanionTip, Does.Contain("kOS 脚本"));
        Assert.That(mechJeb.ShowCompanionBadge, Is.False);
        Assert.That(mechJeb.CompanionTip, Is.Empty);
    }

    [Test]
    public void Details_ExplainsWhichPluginUsesTheModAndRecommendsInstallWhenMissing()
    {
        var repository = new DialogsFakeRepositoryService
        {
            Current = new RepositorySnapshot(All, new[] { "default" }, DateTime.UtcNow, false),
        };
        var vm = new ModDetailsViewModel(repository, new DialogsFakeRegistryService());

        vm.Select("kOS");

        Assert.That(vm.HasCompanionLink, Is.True);
        Assert.That(vm.CompanionPluginName, Is.EqualTo("kOS 脚本"));
        Assert.That(vm.CompanionBenefit, Does.Contain("Archive"));
        Assert.That(vm.CompanionHint, Does.StartWith("推荐安装"));
    }

    private static ModsPageViewModel CreatePage(
        IReadOnlyList<ModRowData> repositoryRows,
        IReadOnlyList<InstalledModuleInfo>? installed = null)
    {
        var repositories = new FakeRepositoryService
        {
            Current = ModsRowFactory.Repository(repositoryRows.ToArray()),
        };
        var registry = new FakeRegistryService
        {
            Current = ModsRowFactory.Registry((installed ?? Array.Empty<InstalledModuleInfo>()).ToArray()),
        };
        var instances = new FakeInstanceService { Current = ModsRowFactory.Instance() };
        var vm = new ModsPageViewModel(repositories, registry, instances, new RecordingErrorReporter());
        vm.ReloadFromSnapshots(force: true);
        return vm;
    }

    [Test]
    public void CompanionPanel_ListsEveryCatalogEntryEvenWhenRepositoryIndexIsEmpty()
    {
        var vm = CreatePage(Array.Empty<ModRowData>());

        Assert.That(vm.ShowEmptyState, Is.True, "前提：索引为空时主表格走空态");
        Assert.That(vm.HasCompanionItems, Is.True, "联动清单是静态表，不吃仓库快照");
        Assert.That(vm.CompanionItems, Has.Count.EqualTo(ModsCompanionCatalog.All.Count));
        Assert.That(vm.CompanionItems.Select(item => item.ModIdentifier),
            Is.EquivalentTo(ModsCompanionCatalog.All.Select(link => link.ModIdentifier)));
        Assert.That(vm.CompanionItems.All(item => item.CanAddToChanges), Is.False, "索引没加载就不该给一键加入");
        Assert.That(vm.CompanionItems[0].StatusText, Is.EqualTo("仓库索引未加载"));
        Assert.That(vm.CompanionSummaryText, Does.Contain("共 6 个"));
    }

    [Test]
    public void CompanionPanel_ReportsInstalledAndAvailableFromSnapshot()
    {
        var vm = CreatePage(
            new[] { ModsRowFactory.Create("kOS", "kOS"), ModsRowFactory.Create("RP-1", "RP-1") },
            new[] { ModsRowFactory.Installed("RP-1", "1.0.0") });

        var kos = vm.CompanionItems.Single(item => item.ModIdentifier == "kOS");
        var rp1 = vm.CompanionItems.Single(item => item.ModIdentifier == "RP-1");
        var soundtrack = vm.CompanionItems.Single(item => item.ModIdentifier == "Soundtrack");

        Assert.That(kos.StatusText, Is.EqualTo("可安装"));
        Assert.That(kos.CanAddToChanges, Is.True);
        Assert.That(rp1.IsInstalled, Is.True);
        Assert.That(rp1.CanAddToChanges, Is.False);
        Assert.That(soundtrack.IsMissing, Is.True, "CKAN 之外的渠道分发要如实说明");
        Assert.That(soundtrack.StatusText, Is.EqualTo("不在 CKAN 仓库"));
        Assert.That(vm.CompanionSummaryText, Does.Contain("已装 1 个"));
    }

    [Test]
    public void AddCompanionToChanges_AddsTheModAndExplainsWhichPluginItPowers()
    {
        var vm = CreatePage(new[] { ModsRowFactory.Create("kOS", "kOS") });

        vm.AddCompanionToChanges("kOS");

        Assert.That(vm.SelectedCount, Is.EqualTo(1));
        Assert.That(vm.StatusText, Does.Contain("kOS"));
        Assert.That(vm.StatusText, Does.Contain("kOS 脚本"));
    }

    [Test]
    public void AddCompanionToChanges_DoesNothingForInstalledOrUnknownMods()
    {
        var vm = CreatePage(
            new[] { ModsRowFactory.Create("RP-1", "RP-1") },
            new[] { ModsRowFactory.Installed("RP-1", "1.0.0") });

        vm.AddCompanionToChanges("RP-1");
        Assert.That(vm.SelectedCount, Is.Zero);
        Assert.That(vm.StatusText, Does.Contain("已安装"));

        vm.AddCompanionToChanges("Soundtrack");
        Assert.That(vm.SelectedCount, Is.Zero);
        Assert.That(vm.StatusText, Does.Contain("不在当前仓库索引"));
    }

    [Test]
    public void CompanionOnly_TogglesPanelAndCloseCommandClearsIt()
    {
        var vm = CreatePage(Array.Empty<ModRowData>());

        Assert.That(vm.CompanionOnly, Is.False);
        Assert.That(vm.ActiveFilterChips, Has.No.Member("联动模组"));

        vm.CompanionOnly = true;
        Assert.That(vm.ActiveFilterChips, Has.Member("联动模组"));

        vm.CloseCompanionPanelCommand.Execute(null);

        Assert.That(vm.CompanionOnly, Is.False);
        Assert.That(vm.ActiveFilterChips, Has.No.Member("联动模组"));
    }

    [Test]
    public void Details_StaysSilentForModsWithoutPluginLink()
    {
        var repository = new DialogsFakeRepositoryService
        {
            Current = new RepositorySnapshot(All, new[] { "default" }, DateTime.UtcNow, false),
        };
        var vm = new ModDetailsViewModel(repository, new DialogsFakeRegistryService());

        vm.Select("MechJeb2");

        Assert.That(vm.HasCompanionLink, Is.False);
        Assert.That(vm.CompanionPluginName, Is.Empty);
        Assert.That(vm.CompanionHint, Is.Empty);
    }
}
