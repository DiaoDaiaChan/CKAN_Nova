using CkanUI.Services.Abstractions;
using CkanUI.Services.Abstractions.Dtos;
using CkanUI.Services.Lifecycle;
using NUnit.Framework;

namespace CkanUI.Services.Tests;

/// <summary>
/// 分层资源释放的编排测试（18 篇 §3/§7）：全部用替身，不联网、不启动游戏、不碰真实注册表。
/// 断言的是「档位决定跑到第几层」「每层独立成败」「L2 可逆」「取消不再继续」这四件事。
/// </summary>
[TestFixture]
public sealed class ResourceReleaseServiceTests
{
    private static readonly InstanceInfo Instance = new("测试实例", @"C:\games\ksp", "1.12.5", true, false, 3);

    [Test]
    public async Task Off_tier_attempts_no_layer()
    {
        var harness = new Harness();

        var result = await harness.Service.ReleaseAsync(ResourceReleaseTier.Off);

        Assert.That(result.IsSuccess, Is.True);
        var report = result.Value!;
        Assert.That(report.Layers, Has.Count.EqualTo(5));
        Assert.That(report.Layers.Any(layer => layer.Attempted), Is.False);
        Assert.That(harness.Registry.CloseCalls, Is.EqualTo(0));
        Assert.That(harness.Memory.HeapCalls, Is.EqualTo(0));
        Assert.That(harness.Memory.WorkingSetCalls, Is.EqualTo(0));
        Assert.That(harness.Service.IsReleased, Is.False);
    }

    [Test]
    public async Task Conservative_tier_runs_only_view_and_cache_layers()
    {
        var harness = new Harness();

        var report = (await harness.Service.ReleaseAsync(ResourceReleaseTier.Conservative)).Value!;

        Assert.That(report[ResourceReleaseLayer.View].Attempted, Is.True);
        Assert.That(report[ResourceReleaseLayer.Caches].Attempted, Is.True);
        Assert.That(report[ResourceReleaseLayer.CoreData].Attempted, Is.False);
        Assert.That(report[ResourceReleaseLayer.ManagedHeap].Attempted, Is.False);
        Assert.That(report[ResourceReleaseLayer.OsMemory].Attempted, Is.False);
        Assert.That(harness.Registry.CloseCalls, Is.EqualTo(0));
        Assert.That(harness.Repository.DropCalls, Is.EqualTo(0));
        Assert.That(report.ReleasedItemCount, Is.EqualTo(3), "视图 1 项 + 缓存 2 项");
        Assert.That(harness.View.TrimCalls, Is.EqualTo(1));
        Assert.That(harness.Cache.TrimCalls, Is.EqualTo(1));
    }

    [Test]
    public async Task Balanced_tier_includes_core_data_and_drops_the_repository_snapshot()
    {
        var harness = new Harness();

        var report = (await harness.Service.ReleaseAsync(ResourceReleaseTier.Balanced)).Value!;

        Assert.That(report[ResourceReleaseLayer.CoreData].Attempted, Is.True);
        Assert.That(report[ResourceReleaseLayer.CoreData].Succeeded, Is.True);
        Assert.That(report[ResourceReleaseLayer.CoreData].ReleasedItemCount, Is.EqualTo(2), "注册表 2 个已安装模组");
        Assert.That(harness.Registry.CloseCalls, Is.EqualTo(1));
        Assert.That(harness.Repository.DropCalls, Is.EqualTo(1));
        Assert.That(harness.Service.IsReleased, Is.True);
        Assert.That(harness.Memory.HeapCalls, Is.EqualTo(0), "均衡档不碰 L3/L4");
        Assert.That(report.IsSuccess, Is.True);
    }

    [Test]
    public async Task Aggressive_tier_runs_heap_and_working_set_layers()
    {
        var harness = new Harness();

        var report = (await harness.Service.ReleaseAsync(ResourceReleaseTier.Aggressive)).Value!;

        Assert.That(report[ResourceReleaseLayer.ManagedHeap].Attempted, Is.True);
        Assert.That(report[ResourceReleaseLayer.OsMemory].Attempted, Is.True);
        Assert.That(harness.Memory.HeapCalls, Is.EqualTo(1));
        Assert.That(harness.Memory.WorkingSetCalls, Is.EqualTo(1));
        Assert.That(report.Layers.Select(layer => layer.Attempted), Is.All.True);
    }

    [Test]
    public async Task Unsupported_platform_layer_is_reported_as_skipped_not_failed()
    {
        var harness = new Harness();
        harness.Memory.WorkingSetResult = MemoryTrimResult.Unsupported("未接入 psapi");

        var report = (await harness.Service.ReleaseAsync(ResourceReleaseTier.Aggressive)).Value!;

        Assert.That(report[ResourceReleaseLayer.OsMemory].Attempted, Is.False);
        Assert.That(report[ResourceReleaseLayer.OsMemory].Detail, Does.Contain("未接入"));
        Assert.That(report.IsSuccess, Is.True, "平台未接入不算失败");
    }

    [Test]
    public async Task Failing_core_data_layer_does_not_block_the_heap_layers()
    {
        var harness = new Harness();
        harness.Registry.CloseResult = OperationResult<bool>.Fail("注册表被占用。");

        var report = (await harness.Service.ReleaseAsync(ResourceReleaseTier.Aggressive)).Value!;

        Assert.That(report[ResourceReleaseLayer.CoreData].Attempted, Is.True);
        Assert.That(report[ResourceReleaseLayer.CoreData].Succeeded, Is.False);
        Assert.That(report.FailedLayers.Select(layer => layer.Layer), Does.Contain(ResourceReleaseLayer.CoreData));
        Assert.That(report[ResourceReleaseLayer.ManagedHeap].Succeeded, Is.True, "L2 失败不阻断 L3");
        Assert.That(report[ResourceReleaseLayer.OsMemory].Succeeded, Is.True, "L2 失败不阻断 L4");
        Assert.That(harness.Repository.DropCalls, Is.EqualTo(0), "关闭失败时不能丢快照");
        Assert.That(harness.Service.IsReleased, Is.False);
        Assert.That(report.IsSuccess, Is.False);
    }

    [Test]
    public async Task Throwing_participant_fails_its_layer_only()
    {
        var harness = new Harness();
        harness.View.Throw = true;

        var report = (await harness.Service.ReleaseAsync(ResourceReleaseTier.Conservative)).Value!;

        Assert.That(report[ResourceReleaseLayer.View].Attempted, Is.True);
        Assert.That(report[ResourceReleaseLayer.View].Succeeded, Is.False);
        Assert.That(report[ResourceReleaseLayer.View].Detail, Does.Contain("视图炸了"));
        Assert.That(report[ResourceReleaseLayer.Caches].Succeeded, Is.True);
    }

    [Test]
    public async Task Restore_reopens_the_registry_and_reloads_the_repository_snapshot()
    {
        var harness = new Harness();
        await harness.Service.ReleaseAsync(ResourceReleaseTier.Balanced);
        Assert.That(harness.Service.IsReleased, Is.True);

        var restore = await harness.Service.RestoreAsync();

        Assert.That(restore.IsSuccess, Is.True);
        Assert.That(harness.Registry.OpenCalls, Is.EqualTo(1));
        Assert.That(harness.Registry.LastOpened?.Name, Is.EqualTo(Instance.Name));
        Assert.That(harness.Repository.LoadCachedCalls, Is.EqualTo(1));
        Assert.That(harness.Service.IsReleased, Is.False);
    }

    [Test]
    public async Task Restore_without_release_is_a_noop()
    {
        var harness = new Harness();

        var restore = await harness.Service.RestoreAsync();

        Assert.That(restore.IsSuccess, Is.True);
        Assert.That(harness.Registry.OpenCalls, Is.EqualTo(0));
        Assert.That(harness.Repository.LoadCachedCalls, Is.EqualTo(0));
    }

    [Test]
    public async Task Restore_failure_keeps_the_released_state_for_a_retry()
    {
        var harness = new Harness();
        await harness.Service.ReleaseAsync(ResourceReleaseTier.Balanced);
        harness.Registry.OpenResult = OperationResult<RegistrySnapshot>.Fail("注册表锁拿不到。");

        var restore = await harness.Service.RestoreAsync();

        Assert.That(restore.IsSuccess, Is.False);
        Assert.That(restore.Retryable, Is.True);
        Assert.That(harness.Service.IsReleased, Is.True, "恢复失败仍处于已释放态，允许重试");
    }

    [Test]
    public async Task Cancelled_token_skips_every_layer()
    {
        var harness = new Harness();
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        var report = (await harness.Service.ReleaseAsync(ResourceReleaseTier.Aggressive, null, cts.Token)).Value!;

        Assert.That(report.Cancelled, Is.True);
        Assert.That(report.Layers.Any(layer => layer.Attempted), Is.False);
        Assert.That(harness.View.TrimCalls, Is.EqualTo(0));
        Assert.That(harness.Registry.CloseCalls, Is.EqualTo(0));
        Assert.That(harness.Memory.HeapCalls, Is.EqualTo(0));
    }

    [Test]
    public async Task Cancelling_midway_stops_before_the_next_layer()
    {
        var harness = new Harness();
        using var cts = new CancellationTokenSource();
        harness.Registry.OnClose = () => cts.Cancel();

        var report = (await harness.Service.ReleaseAsync(ResourceReleaseTier.Aggressive, null, cts.Token)).Value!;

        Assert.That(report[ResourceReleaseLayer.CoreData].Succeeded, Is.True);
        Assert.That(report[ResourceReleaseLayer.ManagedHeap].Attempted, Is.False, "取消后不再继续 L3");
        Assert.That(report[ResourceReleaseLayer.OsMemory].Attempted, Is.False);
        Assert.That(report.Cancelled, Is.True);
        Assert.That(harness.Memory.HeapCalls, Is.EqualTo(0));
    }

    [Test]
    public async Task Repeated_calls_skip_the_layers_already_released_in_this_launch()
    {
        var harness = new Harness();
        await harness.Service.ReleaseAsync(ResourceReleaseTier.Conservative);

        var report = (await harness.Service.ReleaseAsync(ResourceReleaseTier.Balanced)).Value!;

        Assert.That(report[ResourceReleaseLayer.View].Attempted, Is.False, "T3 已跑过 L0");
        Assert.That(report[ResourceReleaseLayer.Caches].Attempted, Is.False, "T3 已跑过 L1");
        Assert.That(report[ResourceReleaseLayer.CoreData].Attempted, Is.True, "T4 只补 L2");
        Assert.That(harness.View.TrimCalls, Is.EqualTo(1));
        Assert.That(harness.Registry.CloseCalls, Is.EqualTo(1));
    }

    [Test]
    public async Task Release_raises_the_released_event_once_per_call()
    {
        var harness = new Harness();
        var reports = new List<ResourceReleaseReport>();
        harness.Service.Released += reports.Add;

        await harness.Service.ReleaseAsync(ResourceReleaseTier.Conservative);
        await harness.Service.ReleaseAsync(ResourceReleaseTier.Balanced);

        Assert.That(reports, Has.Count.EqualTo(2));
        Assert.That(reports[1][ResourceReleaseLayer.CoreData].Attempted, Is.True);
    }

    [Test]
    public async Task Progress_is_reported_for_every_layer()
    {
        var harness = new Harness();
        var updates = new List<OperationProgress>();

        await harness.Service.ReleaseAsync(ResourceReleaseTier.Balanced, new SyncProgress(updates.Add));

        Assert.That(updates, Is.Not.Empty);
        Assert.That(updates.Select(update => update.TaskId), Is.All.EqualTo(IResourceReleaseService.ProgressTaskId));
        Assert.That(updates[^1].State, Is.EqualTo(OperationState.Succeeded));
    }

    /// <summary>同步转发的进度接收器（<see cref="Progress{T}"/> 会切到线程池，断言会抢跑）。</summary>
    private sealed class SyncProgress : IProgress<OperationProgress>
    {
        private readonly Action<OperationProgress> _onReport;

        public SyncProgress(Action<OperationProgress> onReport) => _onReport = onReport;

        public void Report(OperationProgress value) => _onReport(value);
    }

    /// <summary>被测服务 + 全部替身。</summary>
    private sealed class Harness
    {
        public Harness()
        {
            Service = new ResourceReleaseService(Registry, Repository, Instances, Memory, [View], [Cache]);
        }

        public FakeRegistryService Registry { get; } = new();

        public FakeRepositoryService Repository { get; } = new();

        public FakeInstanceService Instances { get; } = new();

        public FakePlatformMemory Memory { get; } = new();

        public FakeViewParticipant View { get; } = new();

        public FakeCacheParticipant Cache { get; } = new();

        public ResourceReleaseService Service { get; }
    }

    private sealed class FakeViewParticipant : IViewResourceParticipant
    {
        public string Name => "视图替身";

        public bool Throw { get; set; }

        public int TrimCalls { get; private set; }

        public int TrimViewResources()
        {
            TrimCalls++;
            if (Throw)
            {
                throw new InvalidOperationException("视图炸了");
            }

            return 1;
        }
    }

    private sealed class FakeCacheParticipant : IAppCacheParticipant
    {
        public string Name => "缓存替身";

        public int TrimCalls { get; private set; }

        public int TrimAppCache()
        {
            TrimCalls++;
            return 2;
        }
    }

    private sealed class FakePlatformMemory : IPlatformMemory
    {
        public int HeapCalls { get; private set; }

        public int WorkingSetCalls { get; private set; }

        public MemoryTrimResult WorkingSetResult { get; set; } = new(true, 1, 1024, null);

        public MemoryTrimResult CollectManagedHeap()
        {
            HeapCalls++;
            return new MemoryTrimResult(true, 1, 2048, null);
        }

        public MemoryTrimResult TrimWorkingSet()
        {
            WorkingSetCalls++;
            return WorkingSetResult;
        }
    }

    private sealed class FakeRegistryService : IRegistryService
    {
        public RegistrySnapshot Current { get; private set; } = new(
            Instance.Name,
            "v1.36",
            new Dictionary<string, InstalledModuleInfo>
            {
                ["A"] = new("A", "1.0", false, null, null),
                ["B"] = new("B", "2.0", true, null, null),
            },
            new HashSet<string>(),
            DateTime.UtcNow);

        public event Action<RegistrySnapshot>? Changed
        {
            add { }
            remove { }
        }

        public int OpenCalls { get; private set; }

        public int CloseCalls { get; private set; }

        public InstanceInfo? LastOpened { get; private set; }

        public Action? OnClose { get; set; }

        public OperationResult<bool> CloseResult { get; set; } = OperationResult<bool>.Ok(true);

        public OperationResult<RegistrySnapshot> OpenResult { get; set; } = OperationResult<RegistrySnapshot>.Ok(RegistrySnapshot.Empty(Instance.Name));

        public Task<OperationResult<RegistrySnapshot>> OpenAsync(InstanceInfo instance, CancellationToken ct = default)
        {
            OpenCalls++;
            LastOpened = instance;
            return Task.FromResult(OpenResult);
        }

        public Task<OperationResult<bool>> CloseAsync(CancellationToken ct = default)
        {
            CloseCalls++;
            if (CloseResult.IsSuccess)
            {
                Current = RegistrySnapshot.Empty(Instance.Name);
            }

            OnClose?.Invoke();
            return Task.FromResult(CloseResult);
        }

        public Task<OperationResult<RegistrySnapshot>> RefreshAsync(CancellationToken ct = default) =>
            Task.FromResult(OperationResult<RegistrySnapshot>.Ok(Current));
    }

    private sealed class FakeRepositoryService : IRepositoryService
    {
        public RepositorySnapshot Current { get; private set; } = RepositorySnapshot.Empty;

        public event Action<RepositorySnapshot>? Updated
        {
            add { }
            remove { }
        }

        public event Action<OperationProgress>? RefreshProgress
        {
            add { }
            remove { }
        }

        public int DropCalls { get; private set; }

        public int LoadCachedCalls { get; private set; }

        public Task<OperationResult<RepositorySnapshot>> LoadCachedAsync(CancellationToken ct = default)
        {
            LoadCachedCalls++;
            return Task.FromResult(OperationResult<RepositorySnapshot>.Ok(Current));
        }

        public Task<OperationResult<RepositorySnapshot>> RefreshAsync(CancellationToken ct = default) =>
            Task.FromResult(OperationResult<RepositorySnapshot>.Ok(Current));

        public void DropCachedSnapshot()
        {
            DropCalls++;
            Current = RepositorySnapshot.Empty;
        }
    }

    private sealed class FakeInstanceService : IInstanceService
    {
        public IReadOnlyList<InstanceInfo> Instances { get; } = [Instance];

        public InstanceInfo? Current => Instance;

        public event Action? InstancesChanged
        {
            add { }
            remove { }
        }

        public event Action<InstanceInfo?>? CurrentChanged
        {
            add { }
            remove { }
        }

        public Task<OperationResult<IReadOnlyList<InstanceInfo>>> RefreshAsync(CancellationToken ct = default) =>
            Task.FromResult(OperationResult<IReadOnlyList<InstanceInfo>>.Ok(Instances));

        public Task<OperationResult<InstanceInfo>> SwitchAsync(string instanceName, CancellationToken ct = default) =>
            Task.FromResult(OperationResult<InstanceInfo>.Ok(Instance));

        public Task<OperationResult<InstanceInfo>> AddAsync(string path, string? name = null, CancellationToken ct = default) =>
            Task.FromResult(OperationResult<InstanceInfo>.Ok(Instance));

        public Task<OperationResult<InstanceInfo>> CloneAsync(string sourceName, string newName, CancellationToken ct = default) =>
            Task.FromResult(OperationResult<InstanceInfo>.Ok(Instance));

        public Task<OperationResult<bool>> RemoveAsync(string instanceName, CancellationToken ct = default) =>
            Task.FromResult(OperationResult<bool>.Ok(true));

        public Task<OperationResult<InstanceInfo>> RenameAsync(string instanceName, string newName, CancellationToken ct = default) =>
            Task.FromResult(OperationResult<InstanceInfo>.Ok(Instance));
    }
}
