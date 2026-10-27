using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CKAN.Configuration;
using CkanUI.Services.Abstractions;
using CkanUI.Services.Abstractions.Dtos;
using CkanUI.Services.Settings;
using CkanUI.TestSupport;
using NUnit.Framework;

namespace CkanUI.Services.Tests;

[TestFixture]
public sealed class PreferredHostsServiceTests
{
    private FakeConfiguration _config = null!;
    private TestSettingsService _settings = null!;

    [SetUp]
    public void SetUp()
    {
        _config = new FakeConfiguration();
        _settings = new TestSettingsService();
    }

    private PreferredHostsService CreateService() =>
        new PreferredHostsService(_config, _settings);

    [Test]
    public void Constructor_NullConfiguration_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => new PreferredHostsService(null!, _settings));
    }

    [Test]
    public void Constructor_NullSettingsService_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => new PreferredHostsService(_config, null!));
    }

    [Test]
    public async Task GetPreferredHostsAsync_EmptyConfig_ReturnsEmptyList()
    {
        _config.PreferredHosts = Array.Empty<string?>();
        var service = CreateService();

        var result = await service.GetPreferredHostsAsync();

        Assert.That(result, Is.Empty);
    }

    [Test]
    public async Task GetPreferredHostsAsync_PopulatedHosts_ReturnsOrderedDtos()
    {
        _config.PreferredHosts = new[] { "github.com", "", "spacedock.info" };
        var service = CreateService();

        var result = await service.GetPreferredHostsAsync();

        Assert.That(result, Has.Count.EqualTo(3));
        Assert.That(result[0].Host, Is.EqualTo("github.com"));
        Assert.That(result[0].Priority, Is.EqualTo(1));
        Assert.That(result[0].IsPlaceholder, Is.False);

        Assert.That(result[1].Host, Is.Null);
        Assert.That(result[1].Priority, Is.EqualTo(2));
        Assert.That(result[1].IsPlaceholder, Is.True);

        Assert.That(result[2].Host, Is.EqualTo("spacedock.info"));
        Assert.That(result[2].Priority, Is.EqualTo(3));
        Assert.That(result[2].IsPlaceholder, Is.False);
    }

    [Test]
    public async Task GetKnownHostsAsync_AlwaysIncludesDefaultKnownHosts()
    {
        var service = CreateService();

        var result = await service.GetKnownHostsAsync();

        Assert.That(result, Does.Contain("github.com"));
        Assert.That(result, Does.Contain("spacedock.info"));
        Assert.That(result, Does.Contain("archive.org"));
        Assert.That(result, Does.Contain("gitlab.com"));
    }

    [Test]
    public void SavePreferredHostsAsync_NullHosts_ThrowsArgumentNullException()
    {
        var service = CreateService();

        Assert.ThrowsAsync<ArgumentNullException>(async () =>
            await service.SavePreferredHostsAsync(null!));
    }

    [Test]
    public async Task SavePreferredHostsAsync_WritesToSettingsService_ReturnsOk()
    {
        var service = CreateService();
        var hostsToSave = new[] { "github.com", null, "spacedock.info" };

        var result = await service.SavePreferredHostsAsync(hostsToSave);

        Assert.That(result.IsSuccess, Is.True);
        Assert.That(_settings.SavedValues.ContainsKey(SettingKeys.PreferredHosts), Is.True);
        var wire = _settings.SavedValues[SettingKeys.PreferredHosts];
        Assert.That(wire, Does.Contain("github.com"));
        Assert.That(wire, Does.Contain("spacedock.info"));
        Assert.That(wire, Does.Contain("null"));
    }

    [Test]
    public async Task SavePreferredHostsAsync_WhenSettingsFails_ReturnsFailure()
    {
        _settings.SetResult = OperationResult<bool>.Fail("磁盘已满");
        var service = CreateService();

        var result = await service.SavePreferredHostsAsync(new[] { "github.com" });

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Message, Does.Contain("磁盘已满"));
    }

    [Test]
    public void GetPreferredHostsAsync_CancellationRequested_ThrowsOperationCanceledException()
    {
        var service = CreateService();
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        Assert.ThrowsAsync<OperationCanceledException>(async () =>
            await service.GetPreferredHostsAsync(cts.Token));
    }

    private sealed class TestSettingsService : ISettingsService
    {
        public Dictionary<string, string?> SavedValues { get; } = new(StringComparer.Ordinal);
        public OperationResult<bool> SetResult { get; set; } = OperationResult<bool>.Ok(true);

        public event Action<string, string?>? SettingChanged
        {
            add { }
            remove { }
        }

        public Task<OperationResult<string?>> GetAsync(string key, CancellationToken ct = default) =>
            Task.FromResult(OperationResult<string?>.Ok(SavedValues.TryGetValue(key, out var v) ? v : null));

        public Task<OperationResult<bool>> SetAsync(string key, string? value, CancellationToken ct = default)
        {
            if (SetResult.IsSuccess)
            {
                SavedValues[key] = value;
            }
            return Task.FromResult(SetResult);
        }

        public Task<OperationResult<IReadOnlyDictionary<string, string?>>> GetAllAsync(CancellationToken ct = default) =>
            Task.FromResult(OperationResult<IReadOnlyDictionary<string, string?>>.Ok(SavedValues));

        public Task<OperationResult<string?>> SetAndVerifyAsync(string key, string? value, CancellationToken ct = default)
        {
            if (SetResult.IsSuccess)
            {
                SavedValues[key] = value;
                return Task.FromResult(OperationResult<string?>.Ok(value));
            }
            return Task.FromResult(OperationResult<string?>.Fail(SetResult.Message ?? "Error"));
        }
    }
}
