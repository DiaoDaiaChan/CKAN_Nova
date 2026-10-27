using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using CKAN;
using CKAN.Configuration;
using CKAN.Games;
using CkanUI.Services.Abstractions;
using CkanUI.Services.Abstractions.Dtos;
using CkanUI.Services.Settings;
using CkanUI.TestSupport;
using NUnit.Framework;

namespace CkanUI.Services.Tests;

[TestFixture]
public class InstallFiltersServiceTests
{
    private FakeConfiguration _configuration = null!;
    private FakeSettingsService _settingsService = null!;

    [SetUp]
    public void Setup()
    {
        _configuration = new FakeConfiguration();
        _settingsService = new FakeSettingsService();
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
    public async Task LoadAsync_WhenSettingsServiceHasJson_ParsesAndDeduplicates()
    {
        var json = JsonSerializer.Serialize(new[] { "Thumbs.db", "thumbs.db", "custom/*.txt" });
        await _settingsService.SetAsync(InstallFiltersService.SettingsKey, json);

        var service = new InstallFiltersService(null, _settingsService, null);
        var result = await service.LoadAsync();

        Assert.That(result, Has.Count.EqualTo(2));
        Assert.That(result, Contains.Item("Thumbs.db"));
        Assert.That(result, Contains.Item("custom/*.txt"));
    }

    [Test]
    public async Task SaveAsync_SanitizesAndPersistsToSettings()
    {
        var service = new InstallFiltersService(_configuration, _settingsService, null);

        var input = new[] { "  .pdb ", ".git", ".PDB", "  ", "new_pattern" };
        await service.SaveAsync(input);

        var savedJson = (await _settingsService.GetAsync(InstallFiltersService.SettingsKey)).Value;
        Assert.That(savedJson, Is.Not.Null);

        var list = JsonSerializer.Deserialize<string[]>(savedJson!);
        Assert.That(list, Has.Length.EqualTo(3));
        Assert.That(list, Contains.Item(".pdb"));
        Assert.That(list, Contains.Item(".git"));
        Assert.That(list, Contains.Item("new_pattern"));
    }

    [Test]
    public void SaveAsync_NullFilters_ThrowsArgumentNullException()
    {
        var service = new InstallFiltersService();
        Assert.ThrowsAsync<ArgumentNullException>(async () => await service.SaveAsync(null!));
    }

    private sealed class FakeSettingsService : ISettingsService
    {
        private readonly Dictionary<string, string?> _data = new(StringComparer.OrdinalIgnoreCase);

        public event Action<string, string?>? SettingChanged;

        public Task<OperationResult<string?>> GetAsync(string key, CancellationToken ct = default)
        {
            _data.TryGetValue(key, out var val);
            return Task.FromResult(OperationResult<string?>.Ok(val));
        }

        public Task<OperationResult<IReadOnlyDictionary<string, string?>>> GetAllAsync(CancellationToken ct = default)
        {
            return Task.FromResult(OperationResult<IReadOnlyDictionary<string, string?>>.Ok((IReadOnlyDictionary<string, string?>)_data));
        }

        public Task<OperationResult<bool>> SetAsync(string key, string? value, CancellationToken ct = default)
        {
            _data[key] = value;
            SettingChanged?.Invoke(key, value);
            return Task.FromResult(OperationResult<bool>.Ok(true));
        }

        public Task<OperationResult<string?>> SetAndVerifyAsync(string key, string? value, CancellationToken ct = default)
        {
            _data[key] = value;
            SettingChanged?.Invoke(key, value);
            return Task.FromResult(OperationResult<string?>.Ok(value));
        }
    }
}
