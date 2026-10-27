using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using CKAN;
using CkanUI.Services.Settings;
using NSubstitute;
using NUnit.Framework;

namespace CkanUI.Services.Tests;

[TestFixture]
public class InstallFiltersServiceTests
{
    private IConfiguration _configuration = null!;
    private ISettingsService _settingsService = null!;
    private IGameInstanceService _instanceService = null!;
    private GameInstance _fakeInstance = null!;

    [SetUp]
    public void Setup()
    {
        _configuration = Substitute.For<IConfiguration>();
        _settingsService = Substitute.For<ISettingsService>();
        _instanceService = Substitute.For<IGameInstanceService>();

        var game = Substitute.For<IGame>();
        game.ShortName.Returns("KSP");

        _fakeInstance = new GameInstance(game, "TestInstance", "C:/fake/path", false);
        _instanceService.Current.Returns(_fakeInstance);
    }

    [Test]
    public void GetDefaultFilters_ReturnsNonEmptyPresets()
    {
        var service = new InstallFiltersService();
        var defaults = service.GetDefaultFilters();

        Assert.That(defaults, Is.Not.Null);
        Assert.That(defaults, Has.Count.GreaterThanOrEqualTo(5));
        Assert.That(defaults, Contains.Item(".pdb"));
        Assert.That(defaults, Contains.Item(".git"));
    }

    [Test]
    public async Task LoadAsync_WhenNoConfiguredFilters_ReturnsDefaults()
    {
        var service = new InstallFiltersService();
        var filters = await service.LoadAsync();

        Assert.That(filters, Is.EquivalentTo(service.GetDefaultFilters()));
    }

    [Test]
    public async Task LoadAsync_WhenCoreHasFilters_ReturnsCoreFiltersSanitized()
    {
        _configuration.GetGlobalInstallFilters(Arg.Any<IGame>())
            .Returns(new[] { "  *.pdb  ", ".git", ".pdb", "" });

        var service = new InstallFiltersService(_configuration, _settingsService, _instanceService);
        var result = await service.LoadAsync();

        Assert.That(result, Has.Count.EqualTo(2));
        Assert.That(result, Contains.Item("*.pdb"));
        Assert.That(result, Contains.Item(".git"));
    }

    [Test]
    public async Task LoadAsync_WhenSettingsServiceHasJson_ParsesAndDeduplicates()
    {
        var json = JsonSerializer.Serialize(new[] { "Thumbs.db", "thumbs.db", "custom/*.txt" });
        _settingsService.GetAsync<string>(InstallFiltersService.SettingsKey, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<string?>(json));

        var service = new InstallFiltersService(null, _settingsService, null);
        var result = await service.LoadAsync();

        Assert.That(result, Has.Count.EqualTo(2));
        Assert.That(result, Contains.Item("Thumbs.db"));
        Assert.That(result, Contains.Item("custom/*.txt"));
    }

    [Test]
    public async Task SaveAsync_SanitizesAndPersistsToBothCoreAndSettings()
    {
        var service = new InstallFiltersService(_configuration, _settingsService, _instanceService);

        var input = new[] { "  .pdb ", ".git", ".PDB", "  ", "new_pattern" };
        await service.SaveAsync(input);

        _configuration.Received(1).SetGlobalInstallFilters(
            _fakeInstance.Game,
            Arg.Is<string[]>(arr => arr.Length == 3 && arr.Contains(".pdb") && arr.Contains(".git") && arr.Contains("new_pattern")));

        await _settingsService.Received(1).SetAsync(
            InstallFiltersService.SettingsKey,
            Arg.Is<string>(s => s.Contains(".pdb") && s.Contains(".git") && s.Contains("new_pattern")),
            Arg.Any<CancellationToken>());
    }

    [Test]
    public void SaveAsync_NullFilters_ThrowsArgumentNullException()
    {
        var service = new InstallFiltersService();
        Assert.ThrowsAsync<ArgumentNullException>(async () => await service.SaveAsync(null!));
    }
}
