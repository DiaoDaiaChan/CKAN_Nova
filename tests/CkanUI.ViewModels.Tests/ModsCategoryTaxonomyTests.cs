using CkanUI.ModText;
using CkanUI.Services.Abstractions;
using CkanUI.Services.Abstractions.Dtos;
using CkanUI.ViewModels.Pages;
using NUnit.Framework;

namespace CkanUI.ViewModels.Tests;

/// <summary>
/// 模组 8 大核心分类（Taxonomy）在界面与视图模型的集成测试：
/// 胶囊初始化、数量统计、单选过滤、点击反选、清空筛选、行与详情徽章呈现。
/// </summary>
[TestFixture]
public sealed class ModsCategoryTaxonomyTests
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

    private ModsPageViewModel CreateViewModel() =>
        new(_repositories, _registry, _instances, _errors)
        {
            Scheduler = new ImmediateDelayScheduler(),
        };

    private void SeedCategorizedRows()
    {
        _repositories.Current = ModsRowFactory.Repository(
            ModsRowFactory.Create(
                "Scatterer",
                "Scatterer Atmospheric Scattering",
                tags: new[] { "graphics" },
                facts: new ModSearchFacts { Abstract = "Atmospheric scattering effects" }),
            ModsRowFactory.Create(
                "MechJeb2",
                "MechJeb 2 Autopilot",
                tags: new[] { "control", "convenience" },
                facts: new ModSearchFacts { Abstract = "Anatid Robotics MechJeb autopilot and tools" }),
            ModsRowFactory.Create(
                "Restock",
                "ReStock Parts",
                tags: new[] { "parts" },
                facts: new ModSearchFacts { Abstract = "High quality art revamp of stock parts" }),
            ModsRowFactory.Create(
                "OuterPlanetsMod",
                "Outer Planets Mod",
                tags: new[] { "planet-pack" },
                facts: new ModSearchFacts { Abstract = "Adds outer gas giants and moons based on real world" }),
            ModsRowFactory.Create(
                "FAR",
                "Ferram Aerospace Research Continued",
                tags: new[] { "physics" },
                facts: new ModSearchFacts { Abstract = "Aerodynamic simulation overhaul" }),
            ModsRowFactory.Create(
                "TACLS",
                "TAC Life Support",
                tags: new[] { "crewed", "resources" },
                facts: new ModSearchFacts { Abstract = "Life support system requiring food, water, oxygen" }),
            ModsRowFactory.Create(
                "BDArmory",
                "BDArmory Plus",
                tags: new[] { "combat" },
                facts: new ModSearchFacts { Abstract = "Missiles, guns, bombs, radars and combat AI" }),
            ModsRowFactory.Create(
                "ModuleManager",
                "Module Manager Core Patch",
                tags: new[] { "library" },
                facts: new ModSearchFacts { Abstract = "Modify KSP configs on the fly without changing files" }));
    }

    [Test]
    public void CategoryChips_Initialized_HasAll8Categories()
    {
        var vm = CreateViewModel();
        Assert.That(vm.CategoryChips, Has.Count.EqualTo(8));
        Assert.That(vm.CategoryChips.Select(c => c.Id), Is.EquivalentTo(ModTaxonomyEngine.Categories.Select(c => c.Id)));
        Assert.That(vm.HasCategoryFilter, Is.False);
        Assert.That(vm.SelectedCategory, Is.Null);
    }

    [Test]
    public void CategoryChips_WithRows_CalculatesAccurateCounts()
    {
        SeedCategorizedRows();
        var vm = CreateViewModel();
        vm.Activate();

        Assert.That(vm.Rows, Has.Count.EqualTo(8));

        var visualsChip = vm.CategoryChips.FirstOrDefault(c => c.Id == ModTaxonomyEngine.CatVisualsAudio);
        Assert.That(visualsChip, Is.Not.Null);
        Assert.That(visualsChip!.ModCount, Is.EqualTo(1));
        Assert.That(visualsChip.ButtonText, Does.Contain("(1)"));

        var toolsChip = vm.CategoryChips.FirstOrDefault(c => c.Id == ModTaxonomyEngine.CatToolsUtilities);
        Assert.That(toolsChip, Is.Not.Null);
        Assert.That(toolsChip!.ModCount, Is.EqualTo(1));

        var partsChip = vm.CategoryChips.FirstOrDefault(c => c.Id == ModTaxonomyEngine.CatPartsHardware);
        Assert.That(partsChip, Is.Not.Null);
        Assert.That(partsChip!.ModCount, Is.EqualTo(1));
    }

    [Test]
    public void ToggleCategoryFilter_FiltersRowsAndUpdatesChipState()
    {
        SeedCategorizedRows();
        var vm = CreateViewModel();
        vm.Activate();

        vm.ToggleCategoryFilter(ModTaxonomyEngine.CatVisualsAudio);

        Assert.That(vm.HasCategoryFilter, Is.True);
        Assert.That(vm.SelectedCategory, Is.EqualTo(ModTaxonomyEngine.CatVisualsAudio));
        Assert.That(vm.Rows, Has.Count.EqualTo(1));
        Assert.That(vm.Rows[0].Identifier, Is.EqualTo("Scatterer"));

        var activeChip = vm.CategoryChips.First(c => c.Id == ModTaxonomyEngine.CatVisualsAudio);
        Assert.That(activeChip.IsActive, Is.True);
        Assert.That(activeChip.StateText, Does.Contain("已选中分类"));

        var inactiveChip = vm.CategoryChips.First(c => c.Id == ModTaxonomyEngine.CatToolsUtilities);
        Assert.That(inactiveChip.IsActive, Is.False);
    }

    [Test]
    public void ToggleCategoryFilter_Twice_ReturnsToAll()
    {
        SeedCategorizedRows();
        var vm = CreateViewModel();
        vm.Activate();

        vm.ToggleCategoryFilter(ModTaxonomyEngine.CatVisualsAudio);
        Assert.That(vm.Rows, Has.Count.EqualTo(1));

        // 第二次点击相同分类胶囊 -> 取消选中
        vm.ToggleCategoryFilter(ModTaxonomyEngine.CatVisualsAudio);
        Assert.That(vm.HasCategoryFilter, Is.False);
        Assert.That(vm.SelectedCategory, Is.Null);
        Assert.That(vm.Rows, Has.Count.EqualTo(8));
    }

    [Test]
    public void ClearCategoryFilter_ResetsFilterAndRestoresAllRows()
    {
        SeedCategorizedRows();
        var vm = CreateViewModel();
        vm.Activate();

        vm.ToggleCategoryFilter(ModTaxonomyEngine.CatPartsHardware);
        Assert.That(vm.Rows, Has.Count.EqualTo(1));
        Assert.That(vm.Rows[0].Identifier, Is.EqualTo("Restock"));

        vm.ClearCategoryFilter();
        Assert.That(vm.HasCategoryFilter, Is.False);
        Assert.That(vm.Rows, Has.Count.EqualTo(8));
    }

    [Test]
    public void ClearFilters_ResetsCategoryFilterAlongWithOthers()
    {
        SeedCategorizedRows();
        var vm = CreateViewModel();
        vm.Activate();

        vm.ToggleCategoryFilter(ModTaxonomyEngine.CatPlanetsWorlds);
        Assert.That(vm.Rows, Has.Count.EqualTo(1));

        vm.ClearFilters();
        Assert.That(vm.HasCategoryFilter, Is.False);
        Assert.That(vm.SelectedCategory, Is.Null);
        Assert.That(vm.Rows, Has.Count.EqualTo(8));
    }

    [Test]
    public void Rows_And_Details_HaveCategoryBadges()
    {
        SeedCategorizedRows();
        var vm = CreateViewModel();
        var detailsVm = new CkanUI.ViewModels.Dialogs.ModDetailsViewModel(_repositories, _registry);
        vm.Details = detailsVm;
        vm.Activate();

        var scattererRow = vm.Rows.First(r => r.Identifier == "Scatterer");
        Assert.That(scattererRow.CategoryBadgeText, Does.Contain("视觉与音效美化"));
        Assert.That(scattererRow.Category.Icon, Is.EqualTo("🎨"));

        vm.SelectedRow = scattererRow;
        Assert.That(vm.Details, Is.Not.Null);
        Assert.That(vm.Details!.CategoryBadgeText, Does.Contain("视觉与音效美化"));
        Assert.That(vm.Details.CategoryDescription, Does.Contain("画质升级"));
    }
}
