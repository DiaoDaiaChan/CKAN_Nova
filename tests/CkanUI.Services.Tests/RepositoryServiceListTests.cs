using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using CkanUI.Services.Abstractions;
using CkanUI.Services.Abstractions.Dtos;
using CkanUI.Services.Repositories;
using CkanUI.TestSupport;
using CKAN;
using NUnit.Framework;

namespace CkanUI.Services.Tests;

/// <summary>
/// <see cref="RepositoryService"/> 的仓库列表契约（UI-06）：事件封送、错误语义与无实例分支。
/// 全部用假 <see cref="IRepositoryStore"/>，不联网。
/// </summary>
[TestFixture]
public sealed class RepositoryServiceListTests
{
    private GameInstanceManager _manager = null!;
    private RepositoryDataManager _repositoryData = null!;
    private FakeDispatcher _dispatcher = null!;
    private ChangeSetFakeErrorReporter _errors = null!;
    private FakeRepositoryStore _store = null!;
    private TempWorkspace _temp = null!;

    [SetUp]
    public void SetUp()
    {
        _manager = new GameInstanceManager(new FakeUser(), new FakeConfiguration());
        _temp = new TempWorkspace("repository-service-list");
        _repositoryData = new RepositoryDataManager(_temp.Path);
        _dispatcher = new FakeDispatcher();
        _errors = new ChangeSetFakeErrorReporter();
        _store = new FakeRepositoryStore();
    }

    [TearDown]
    public void TearDown() => _temp.Dispose();

    private RepositoryService CreateService(IRepositoryStore? store = null) =>
        new(_manager, _repositoryData, _dispatcher, _errors, new FakeUser(), store ?? _store);

    [Test]
    public async Task LoadRepositoriesAsync_ProjectsStoreRows()
    {
        _store.Seed(("B", "https://example.com/b.json", 1), ("A", "https://example.com/a.json", 0));
        using var service = CreateService();

        var result = await service.LoadRepositoriesAsync();

        Assert.That(result.IsSuccess, Is.True);
        Assert.That(result.Value!.Select(row => row.Name), Is.EqualTo(new[] { "A", "B" }));
        Assert.That(service.Repositories.Select(row => row.Name), Is.EqualTo(new[] { "A", "B" }));
    }

    [Test]
    public async Task LoadRepositoriesAsync_PublishesChangeThroughDispatcher()
    {
        _store.Seed(("A", "https://example.com/a.json", 0));
        _dispatcher = new FakeDispatcher(queuePosts: true);
        using var service = CreateService();
        IReadOnlyList<RepositoryEntryInfo>? published = null;
        service.RepositoriesChanged += rows => published = rows;

        await service.LoadRepositoriesAsync();

        Assert.That(published, Is.Null, "事件必须经 IDispatcher.Post 封送，不能在调用线程同步触发。");

        _dispatcher.PumpPosts();

        Assert.That(published, Is.Not.Null);
        Assert.That(published!.Select(row => row.Name), Is.EqualTo(new[] { "A" }));
    }

    [Test]
    public async Task AddRepositoryAsync_AppendsRow()
    {
        _store.Seed(("A", "https://example.com/a.json", 0));
        using var service = CreateService();

        var result = await service.AddRepositoryAsync(new RepositoryEditRequest("B", "https://example.com/b.json"));

        Assert.That(result.IsSuccess, Is.True);
        Assert.That(result.Value!.Select(row => row.Name), Is.EqualTo(new[] { "A", "B" }));
        Assert.That(service.Repositories.Count, Is.EqualTo(2));
    }

    [Test]
    public async Task AddRepositoryAsync_InvalidUri_FailsWithoutRetry_AndKeepsRows()
    {
        _store.Seed(("A", "https://example.com/a.json", 0));
        using var service = CreateService();
        await service.LoadRepositoriesAsync();

        var result = await service.AddRepositoryAsync(new RepositoryEditRequest("B", "ftp://example.com/b.json"));

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Retryable, Is.False);
        Assert.That(result.Message, Is.EqualTo("仓库地址必须是 http 或 https 开头的完整链接。"));
        Assert.That(service.Repositories.Count, Is.EqualTo(1));
        Assert.That(_errors.Entries, Is.Empty);
    }

    [Test]
    public async Task RemoveRepositoryAsync_LastRepository_Fails()
    {
        _store.Seed(("A", "https://example.com/a.json", 0));
        using var service = CreateService();

        var result = await service.RemoveRepositoryAsync("A");

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Message, Is.EqualTo("至少要保留一个仓库，不能删除最后一个。"));
        Assert.That(result.Retryable, Is.False);
    }

    [Test]
    public async Task MoveRepositoryAsync_SwapsPriorities()
    {
        _store.Seed(("A", "https://example.com/a.json", 0), ("B", "https://example.com/b.json", 1));
        using var service = CreateService();

        var result = await service.MoveRepositoryAsync("A", 1);

        Assert.That(result.IsSuccess, Is.True);
        Assert.That(result.Value!.Select(row => row.Name), Is.EqualTo(new[] { "B", "A" }));
        Assert.That(result.Value![1].Priority, Is.EqualTo(1));
    }

    [Test]
    public async Task RefreshRepositoryAsync_EmptyName_Fails()
    {
        using var service = CreateService();

        var result = await service.RefreshRepositoryAsync("   ");

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Message, Is.EqualTo("请先选择一个仓库。"));
    }

    [Test]
    public async Task RefreshRepositoryAsync_WithoutInstance_FailsWithoutRetry()
    {
        _store.Seed(("A", "https://example.com/a.json", 0));
        using var service = CreateService();

        var result = await service.RefreshRepositoryAsync("A");

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Message, Is.EqualTo("还没有选择游戏实例，无法刷新仓库索引。"));
        Assert.That(result.Retryable, Is.False);
        Assert.That(_store.RefreshCount, Is.Zero, "没有实例时不应触达存储层。");
    }

    [Test]
    public async Task RestoreDefaultRepositoriesAsync_WithoutOfficialList_FailsRetryable()
    {
        _store.Seed(("A", "https://example.com/a.json", 0));
        _store.OfficialDefaults = null;
        using var service = CreateService();

        var result = await service.RestoreDefaultRepositoriesAsync();

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Retryable, Is.True);
    }

    [Test]
    public async Task DefaultStore_WithoutInstance_LoadsEmptyList()
    {
        // 不注入 store：走 RegistryRepositoryStore 默认实现，必须能在无实例时安全降级（DI 不带额外参数）。
        using var service = CreateService(new RegistryRepositoryStore(_manager, _repositoryData, new FakeUser()));

        var result = await service.LoadRepositoriesAsync();

        Assert.That(result.IsSuccess, Is.True);
        Assert.That(result.Value, Is.Empty);
    }

    [Test]
    public async Task Ctor_WithDefaultStore_DoesNotThrow_AndKeepsSnapshot()
    {
        using var service = new RepositoryService(_manager, _repositoryData, _dispatcher, _errors, new FakeUser());

        var loaded = await service.LoadRepositoriesAsync();
        var cached = await service.LoadCachedAsync();

        Assert.That(loaded.IsSuccess, Is.True);
        Assert.That(service.Repositories, Is.Empty);
        Assert.That(cached.IsSuccess, Is.True);
        Assert.That(cached.Value!.Available, Is.Empty);
    }

    [Test]
    public void Ctor_NullDependency_Throws()
    {
        Assert.Throws<ArgumentNullException>(() =>
            new RepositoryService(null!, _repositoryData, _dispatcher, _errors, new FakeUser()));
        Assert.Throws<ArgumentNullException>(() =>
            new RepositoryService(_manager, null!, _dispatcher, _errors, new FakeUser()));
        Assert.Throws<ArgumentNullException>(() =>
            new RepositoryService(_manager, _repositoryData, null!, _errors, new FakeUser()));
        Assert.Throws<ArgumentNullException>(() =>
            new RepositoryService(_manager, _repositoryData, _dispatcher, null!, new FakeUser()));
        Assert.Throws<ArgumentNullException>(() =>
            new RepositoryService(_manager, _repositoryData, _dispatcher, _errors, null!));
    }
}
