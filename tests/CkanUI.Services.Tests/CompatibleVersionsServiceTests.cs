using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CKAN;
using CKAN.Games.KerbalSpaceProgram;
using CKAN.Versioning;
using CkanUI.Services.Abstractions;
using CkanUI.Services.Abstractions.Dtos;
using CkanUI.Services.Compatibility;
using CkanUI.TestSupport;
using NUnit.Framework;

namespace CkanUI.Services.Tests;

[TestFixture]
public sealed class CompatibleVersionsServiceTests
{
    private TempWorkspace _temp = null!;
    private GameInstanceManager _manager = null!;
    private GameInstance _instance = null!;
    private FakeRegistryService _registry = null!;
    private FakeRepositoryService _repository = null!;

    [SetUp]
    public void SetUp()
    {
        _temp = new TempWorkspace("compat-service-tests");
        _manager = new GameInstanceManager(new FakeUser(), new FakeConfiguration());
        var gameDir = _temp.Combine("KSP_root");
        Directory.CreateDirectory(gameDir);
        Directory.CreateDirectory(Path.Combine(gameDir, "GameData"));

        // 创建 anchor 文件与 readme.txt，使 GameInstance.Valid 为 true
        File.WriteAllText(Path.Combine(gameDir, "KSP_x64.exe"), "");
        File.WriteAllText(Path.Combine(gameDir, "readme.txt"), "Version 1.12.5\n");

        _instance = new GameInstance(new KerbalSpaceProgram(), gameDir, "TestInstance");
        _manager.AddInstance(_instance);
        _manager.SetCurrentInstance(_instance);

        _registry = new FakeRegistryService();
        _repository = new FakeRepositoryService();
    }

    [TearDown]
    public void TearDown() => _temp.Dispose();

    private CkanCompatibleVersionsService CreateService()
    {
        return new CkanCompatibleVersionsService(
            new Lazy<GameInstanceManager>(() => _manager),
            _registry,
            _repository);
    }

    [Test]
    public void Constructor_NullManager_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() =>
            new CkanCompatibleVersionsService(null!));
    }

    [Test]
    public async Task LoadAsync_NoInstance_ReturnsEmptySnapshot()
    {
        var emptyManager = new GameInstanceManager(new FakeUser(), new FakeConfiguration());
        var service = new CkanCompatibleVersionsService(
            new Lazy<GameInstanceManager>(() => emptyManager));

        var snapshot = await service.LoadAsync();

        Assert.That(snapshot.InstanceName, Is.EqualTo("None"));
        Assert.That(snapshot.RealVersion, Is.EqualTo("Unknown"));
        Assert.That(snapshot.Items, Is.Empty);
    }

    [Test]
    public async Task LoadAsync_WithRealVersion_IncludesRealVersionFirstAndLocked()
    {
        var service = CreateService();

        var snapshot = await service.LoadAsync();

        Assert.That(snapshot.InstanceName, Is.EqualTo("TestInstance"));
        Assert.That(snapshot.RealVersion, Is.EqualTo("1.12.5"));
        Assert.That(snapshot.Items, Is.Not.Empty);

        var firstItem = snapshot.Items[0];
        Assert.That(firstItem.VersionString, Is.EqualTo("1.12.5"));
        Assert.That(firstItem.IsRealVersion, Is.True);
        Assert.That(firstItem.IsSelected, Is.True);
        Assert.That(firstItem.IsWildcard, Is.False);
    }

    [Test]
    public async Task LoadAsync_WithConfiguredCompatibleVersions_MarksThemSelected()
    {
        _instance.SetCompatibleVersions(new List<GameVersion>
        {
            GameVersion.Parse("1.12"),
            GameVersion.Parse("1.11.2")
        });

        var service = CreateService();
        var snapshot = await service.LoadAsync();

        var item112 = snapshot.Items.FirstOrDefault(i => i.VersionString == "1.12");
        Assert.That(item112, Is.Not.Null);
        Assert.That(item112!.IsSelected, Is.True);
        Assert.That(item112.IsWildcard, Is.True);

        var item1112 = snapshot.Items.FirstOrDefault(i => i.VersionString == "1.11.2");
        Assert.That(item1112, Is.Not.Null);
        Assert.That(item1112!.IsSelected, Is.True);
        Assert.That(item1112.IsRealVersion, Is.False);
    }

    [Test]
    public async Task LoadAsync_ExtractsMajorWildcardsCorrectly()
    {
        var service = CreateService();
        var snapshot = await service.LoadAsync();

        // 验证通配符版本格式不带 $ 前缀，且正确提取
        var wildcards = snapshot.Items.Where(i => i.IsWildcard).ToList();
        Assert.That(wildcards, Is.Not.Empty);
        Assert.That(wildcards.All(w => !w.VersionString.StartsWith("$")), Is.True);
        Assert.That(wildcards.Any(w => w.VersionString == "1.12"), Is.True);
    }

    [Test]
    public async Task LoadAsync_NamedInstanceLookup_FindsCorrectInstance()
    {
        var service = CreateService();

        var snapshot = await service.LoadAsync("TestInstance");

        Assert.That(snapshot.InstanceName, Is.EqualTo("TestInstance"));
        Assert.That(snapshot.RealVersion, Is.EqualTo("1.12.5"));
    }

    [Test]
    public async Task LoadAsync_NamedInstanceNotFound_ReturnsEmptySnapshot()
    {
        var service = CreateService();

        var snapshot = await service.LoadAsync("NonExistentInstance");

        Assert.That(snapshot.InstanceName, Is.EqualTo("NonExistentInstance"));
        Assert.That(snapshot.RealVersion, Is.EqualTo("Unknown"));
        Assert.That(snapshot.Items, Is.Empty);
    }

    [Test]
    public void SaveAsync_NonExistentInstance_ThrowsInvalidOperationException()
    {
        var service = CreateService();

        Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await service.SaveAsync(new[] { "1.12" }, "NonExistentInstance"));
    }

    [Test]
    public async Task SaveAsync_ValidVersions_UpdatesInstanceCompatibleVersions()
    {
        var service = CreateService();

        await service.SaveAsync(new[] { "1.12", "1.11.2", "1.10" });

        var compats = _instance.CompatibleVersions.Select(v => v.ToString()).ToList();
        Assert.That(compats, Does.Contain("1.12"));
        Assert.That(compats, Does.Contain("1.11.2"));
        Assert.That(compats, Does.Contain("1.10"));
    }

    [Test]
    public async Task SaveAsync_InvalidVersionStrings_AreFilteredOutGracefully()
    {
        var service = CreateService();

        await service.SaveAsync(new[] { "1.12", "invalid-version-string", "   ", "" });

        var compats = _instance.CompatibleVersions.Select(v => v.ToString()).ToList();
        Assert.That(compats, Is.EquivalentTo(new[] { "1.12" }));
    }

    [Test]
    public async Task SaveAsync_TriggerRefreshTrue_RefreshesRegistryAndRepository()
    {
        var service = CreateService();

        await service.SaveAsync(new[] { "1.12" }, triggerRefresh: true);

        Assert.That(_registry.RefreshCount, Is.EqualTo(1));
        Assert.That(_repository.LoadCachedCount, Is.EqualTo(1));
    }

    [Test]
    public async Task SaveAsync_TriggerRefreshFalse_DoesNotRefreshRegistryOrRepository()
    {
        var service = CreateService();

        await service.SaveAsync(new[] { "1.12" }, triggerRefresh: false);

        Assert.That(_registry.RefreshCount, Is.EqualTo(0));
        Assert.That(_repository.LoadCachedCount, Is.EqualTo(0));
    }

    [Test]
    public void LoadAsync_CancellationRequested_ThrowsOperationCanceledException()
    {
        var service = CreateService();
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        Assert.ThrowsAsync<OperationCanceledException>(async () =>
            await service.LoadAsync(cancellationToken: cts.Token));
    }

    [Test]
    public void SaveAsync_CancellationRequested_ThrowsOperationCanceledException()
    {
        var service = CreateService();
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        Assert.ThrowsAsync<OperationCanceledException>(async () =>
            await service.SaveAsync(new[] { "1.12" }, cancellationToken: cts.Token));
    }

    private sealed class FakeRegistryService : IRegistryService
    {
        public int RefreshCount { get; private set; }
        public RegistrySnapshot Current { get; set; } = RegistrySnapshot.Empty("");

#pragma warning disable CS0067
        public event Action<RegistrySnapshot>? Changed;
#pragma warning restore CS0067

        public Task<OperationResult<RegistrySnapshot>> OpenAsync(InstanceInfo instance, CancellationToken ct = default) =>
            Task.FromResult(OperationResult<RegistrySnapshot>.Ok(Current));

        public Task<OperationResult<bool>> CloseAsync(CancellationToken ct = default) =>
            Task.FromResult(OperationResult<bool>.Ok(true));

        public Task<OperationResult<RegistrySnapshot>> RefreshAsync(CancellationToken ct = default)
        {
            RefreshCount++;
            return Task.FromResult(OperationResult<RegistrySnapshot>.Ok(Current));
        }
    }

    private sealed class FakeRepositoryService : IRepositoryService
    {
        public int LoadCachedCount { get; private set; }
        public RepositorySnapshot Current { get; set; } = RepositorySnapshot.Empty;

#pragma warning disable CS0067
        public event Action<RepositorySnapshot>? Updated;
        public event Action<OperationProgress>? RefreshProgress;
#pragma warning restore CS0067

        public Task<OperationResult<RepositorySnapshot>> LoadCachedAsync(CancellationToken ct = default)
        {
            LoadCachedCount++;
            return Task.FromResult(OperationResult<RepositorySnapshot>.Ok(Current));
        }

        public Task<OperationResult<RepositorySnapshot>> RefreshAsync(CancellationToken ct = default) =>
            Task.FromResult(OperationResult<RepositorySnapshot>.Ok(Current));
    }
}
