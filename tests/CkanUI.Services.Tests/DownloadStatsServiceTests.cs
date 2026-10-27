using CkanUI.Services.Abstractions;
using CkanUI.Services.Abstractions.Dtos;
using CkanUI.Services.Stats;
using NUnit.Framework;

namespace CkanUI.Services.Tests;

/// <summary>下载统计聚合与取数（12 篇 F-47）。</summary>
[TestFixture]
public sealed class DownloadStatsServiceTests
{
    private static ModRowData Mod(string id, long downloads, bool installed = false, params string[] tags) =>
        new(id, id, null, null, null, downloads, tags, null, installed, false, true, false, false, false);

    private static CacheEntryInfo Cached(string id, long bytes) =>
        new(id + ".zip", bytes, DateTime.UtcNow, id, "1.0", false);

    private static DownloadStatsInput Input(
        IReadOnlyList<ModRowData> modules,
        IReadOnlyList<CacheEntryInfo>? cache = null,
        IReadOnlyDictionary<string, string>? repos = null) =>
        new(modules, cache ?? Array.Empty<CacheEntryInfo>(), repos);

    [Test]
    public void SlicesByModule_SortDescendingByDownloads()
    {
        var report = DownloadStatsAggregator.Compute(
            Input(new[] { Mod("a", 4), Mod("b", 6) }),
            new DownloadStatsRequest(DownloadStatsDimension.Mods));

        Assert.That(report.Slices.Select(s => s.Label), Is.EqualTo(new[] { "b", "a" }));
        Assert.That(report.Slices.Select(s => s.Percent), Is.EqualTo(new[] { 60d, 40d }));
    }

    [Test]
    public void PercentagesSumToHundred()
    {
        var report = DownloadStatsAggregator.Compute(
            Input(new[] { Mod("a", 1), Mod("b", 1), Mod("c", 2) }),
            new DownloadStatsRequest(DownloadStatsDimension.Mods));

        Assert.That(report.Slices.Sum(s => s.Percent), Is.EqualTo(100d).Within(0.001d));
    }

    [Test]
    public void ZeroDownloads_GroupedAsUnavailable()
    {
        var report = DownloadStatsAggregator.Compute(
            Input(new[] { Mod("a", 0), Mod("b", 0), Mod("c", 5) }),
            new DownloadStatsRequest(DownloadStatsDimension.Mods));

        Assert.That(report.Slices.Any(s => s.Label == DownloadStatsAggregator.UnavailableLabel), Is.True);
        Assert.That(report.Slices.Single(s => s.Label == DownloadStatsAggregator.UnavailableLabel).Unavailable, Is.True);
        Assert.That(report.Unavailable, Is.True);
    }

    [Test]
    public void TagDimension_MultiTagModuleCountsInEveryTag()
    {
        var report = DownloadStatsAggregator.Compute(
            Input(new[] { Mod("a", 5, false, "A", "B") }),
            new DownloadStatsRequest(DownloadStatsDimension.Tags));

        Assert.That(report.Slices.Count, Is.EqualTo(2));
        Assert.That(report.Slices.Sum(s => s.Count), Is.EqualTo(10));
    }

    [Test]
    public void TagDimension_ModuleWithoutTagsGoesToNoTagBucket()
    {
        var report = DownloadStatsAggregator.Compute(
            Input(new[] { Mod("a", 3) }),
            new DownloadStatsRequest(DownloadStatsDimension.Tags));

        Assert.That(report.Slices.Single().Label, Is.EqualTo(DownloadStatsAggregator.NoTagLabel));
    }

    [Test]
    public void InstalledDimension_SplitsByInstallState()
    {
        var report = DownloadStatsAggregator.Compute(
            Input(new[] { Mod("a", 2, installed: true), Mod("b", 3) }),
            new DownloadStatsRequest(DownloadStatsDimension.Installed));

        Assert.That(report.Slices.Select(s => s.Label),
            Is.EquivalentTo(new[] { DownloadStatsAggregator.InstalledLabel, DownloadStatsAggregator.NotInstalledLabel }));
        Assert.That(report.Slices.Single(s => s.Label == DownloadStatsAggregator.NotInstalledLabel).Count, Is.EqualTo(3));
    }

    [Test]
    public void MoreThanEightGroups_MergedIntoOther()
    {
        var modules = Enumerable.Range(0, 20).Select(i => Mod("m" + i, i + 1)).ToArray();
        var report = DownloadStatsAggregator.Compute(
            Input(modules),
            new DownloadStatsRequest(DownloadStatsDimension.Mods));

        Assert.That(report.Slices.Count, Is.EqualTo(DownloadStatsAggregator.MaxSlices));
        Assert.That(report.Slices.Last().Label, Is.EqualTo(DownloadStatsAggregator.OtherLabel));
        Assert.That(report.Slices.Sum(s => s.Percent), Is.EqualTo(100d).Within(0.5d));
    }

    [Test]
    public void RepositoriesDimension_GroupsByRepositoryName()
    {
        var repos = new Dictionary<string, string>
        {
            ["a"] = "MainRepo",
            ["b"] = "MainRepo",
            ["c"] = "ExtraRepo",
        };
        var modules = new[] { Mod("a", 10), Mod("b", 20), Mod("c", 15), Mod("d", 5) };
        var report = DownloadStatsAggregator.Compute(
            Input(modules, repos: repos),
            new DownloadStatsRequest(DownloadStatsDimension.Repositories));

        Assert.That(report.Slices.Count, Is.EqualTo(3));
        Assert.That(report.Slices[0].Label, Is.EqualTo("MainRepo"));
        Assert.That(report.Slices[0].Count, Is.EqualTo(30));
        Assert.That(report.Slices[1].Label, Is.EqualTo("ExtraRepo"));
        Assert.That(report.Slices[1].Count, Is.EqualTo(15));
        Assert.That(report.Slices[2].Label, Is.EqualTo("未知仓库"));
        Assert.That(report.Slices[2].Count, Is.EqualTo(5));
    }

    [Test]
    public void CacheBytes_JoinedByIdentifier()
    {
        var report = DownloadStatsAggregator.Compute(
            Input(new[] { Mod("a", 1), Mod("b", 1) }, new[] { Cached("a", 900), Cached("b", 100) }),
            new DownloadStatsRequest(DownloadStatsDimension.Mods));

        Assert.That(report.CacheTotalBytes, Is.EqualTo(1000));
        Assert.That(report.Rows.Single(r => r.Identifier == "a").CacheBytes, Is.EqualTo(900));
    }

    [Test]
    public void CacheEntryWithoutIdentifier_Ignored()
    {
        var orphan = new CacheEntryInfo("junk.zip", 500, DateTime.UtcNow, null, null, false);
        var report = DownloadStatsAggregator.Compute(
            Input(new[] { Mod("a", 1) }, new[] { orphan }),
            new DownloadStatsRequest(DownloadStatsDimension.Mods));

        Assert.That(report.CacheTotalBytes, Is.Zero);
        Assert.That(report.Rows.Single().CacheBytes, Is.Zero);
    }

    [Test]
    public void SortByCacheAndName()
    {
        var input = Input(new[] { Mod("a", 9), Mod("b", 1) }, new[] { Cached("b", 800), Cached("a", 100) });

        var byCache = DownloadStatsAggregator.Compute(input, new DownloadStatsRequest(Sort: DownloadStatsSortColumn.Cache));
        var byName = DownloadStatsAggregator.Compute(input, new DownloadStatsRequest(Sort: DownloadStatsSortColumn.Name, Descending: false));

        Assert.That(byCache.Rows[0].Identifier, Is.EqualTo("b"));
        Assert.That(byName.Rows[0].Identifier, Is.EqualTo("a"));
    }

    [Test]
    public void TopLimit_TruncatesRowsButNotTotals()
    {
        var report = DownloadStatsAggregator.Compute(
            Input(new[] { Mod("a", 5), Mod("b", 4), Mod("c", 3) }),
            new DownloadStatsRequest(TopLimit: 2));

        Assert.That(report.Rows.Count, Is.EqualTo(2));
        Assert.That(report.ModuleCount, Is.EqualTo(3));
        Assert.That(report.TotalDownloads, Is.EqualTo(12));
    }

    [Test]
    public void ZeroTopLimit_MeansUnlimited()
    {
        var report = DownloadStatsAggregator.Compute(
            Input(new[] { Mod("a", 5), Mod("b", 4), Mod("c", 3) }),
            new DownloadStatsRequest(TopLimit: 0));

        Assert.That(report.Rows.Count, Is.EqualTo(3));
    }

    [Test]
    public void EmptyInput_ReportsZeroEverythingWithoutSlices()
    {
        var report = DownloadStatsAggregator.Compute(DownloadStatsInput.Empty, new DownloadStatsRequest());

        Assert.That(report.Slices, Is.Empty);
        Assert.That(report.Rows, Is.Empty);
        Assert.That(report.ModuleCount, Is.Zero);
        Assert.That(report.TotalDownloads, Is.Zero);
        Assert.That(report.CacheTotalBytes, Is.Zero);
        Assert.That(report.Unavailable, Is.False);
    }

    [Test]
    public async Task Service_CombinesSnapshotsAndMarksInstalled()
    {
        var repositories = new FakeRepositoryService
        {
            Current = new RepositorySnapshot(
                new[] { Mod("a", 7, installed: true), Mod("b", 3, installed: true) },
                new[] { "default" },
                DateTime.UtcNow,
                false),
        };
        var registry = new FakeRegistryService
        {
            Current = new RegistrySnapshot(
                "Test",
                null,
                new Dictionary<string, InstalledModuleInfo>(StringComparer.OrdinalIgnoreCase)
                {
                    ["a"] = new("a", "1.0", false, "MainRepo", null),
                },
                new HashSet<string>(),
                DateTime.UtcNow),
        };
        var cache = new FakeCacheService { Entries = new[] { Cached("b", 40) } };

        var service = new DownloadStatsService(repositories, cache, registry);
        var result = await service.ComputeAsync(new DownloadStatsRequest(DownloadStatsDimension.Repositories));

        Assert.That(result.IsSuccess, Is.True);
        var report = result.Value!;
        Assert.That(report.Rows.Single(r => r.Identifier == "a").IsInstalled, Is.True);
        Assert.That(report.Rows.Single(r => r.Identifier == "b").IsInstalled, Is.False);
        Assert.That(report.CacheTotalBytes, Is.EqualTo(40));
        Assert.That(service.CurrentInput.Modules.Count, Is.EqualTo(2));
        // a 映射到 MainRepo，b 映射到快照的 default 仓库
        Assert.That(report.Slices.Any(s => s.Label == "MainRepo"), Is.True);
        Assert.That(report.Slices.Any(s => s.Label == "default"), Is.True);
    }

    [Test]
    public async Task Service_NoInstance_ReturnsEmptyReportWithoutThrowing()
    {
        var instanceService = new FakeInstanceService { Current = null };
        var repositories = new FakeRepositoryService
        {
            Current = new RepositorySnapshot(new[] { Mod("a", 10) }, new[] { "repo" }, DateTime.UtcNow, false)
        };
        var service = new DownloadStatsService(instanceService, repositories, new FakeCacheService(), new FakeRegistryService());

        var result = await service.ComputeAsync(new DownloadStatsRequest());

        Assert.That(result.IsSuccess, Is.True);
        Assert.That(result.Value!.ModuleCount, Is.Zero);
        Assert.That(service.CurrentInput.Modules, Is.Empty);
    }

    [Test]
    public async Task Service_Cancellation_PropagatesCancellation()
    {
        var service = new DownloadStatsService(new FakeRepositoryService(), new FakeCacheService(), new FakeRegistryService());
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        Assert.ThrowsAsync<OperationCanceledException>(async () =>
            await service.ComputeAsync(new DownloadStatsRequest(), cts.Token));
    }

    [Test]
    public async Task Service_CacheFailure_FallsBackToEmptyCache()
    {
        var service = new DownloadStatsService(
            new FakeRepositoryService { Current = new RepositorySnapshot(new[] { Mod("a", 5) }, Array.Empty<string>(), null, true) },
            new FakeCacheService { Fail = true },
            new FakeRegistryService());

        var result = await service.ComputeAsync(new DownloadStatsRequest());

        Assert.That(result.IsSuccess, Is.True);
        Assert.That(result.Value!.CacheTotalBytes, Is.Zero);
        Assert.That(result.Value.Rows.Single().DownloadCount, Is.EqualTo(5));
    }

    [Test]
    public async Task Service_NoSnapshot_ReturnsEmptyReport()
    {
        var service = new DownloadStatsService(new FakeRepositoryService(), new FakeCacheService(), new FakeRegistryService());

        var result = await service.ComputeAsync(new DownloadStatsRequest());

        Assert.That(result.IsSuccess, Is.True);
        Assert.That(result.Value!.ModuleCount, Is.Zero);
    }

    private sealed class FakeInstanceService : IInstanceService
    {
        public InstanceInfo? Current { get; set; }
        public IReadOnlyList<InstanceInfo> Instances => Current is not null ? new[] { Current } : Array.Empty<InstanceInfo>();
#pragma warning disable CS0067
        public event Action? InstancesChanged;
        public event Action<InstanceInfo?>? CurrentChanged;
#pragma warning restore CS0067
        public Task<OperationResult<IReadOnlyList<InstanceInfo>>> RefreshAsync(CancellationToken ct = default) =>
            Task.FromResult(OperationResult<IReadOnlyList<InstanceInfo>>.Ok(Instances));
        public Task<OperationResult<InstanceInfo>> SwitchAsync(string instanceName, CancellationToken ct = default) =>
            throw new NotImplementedException();
        public Task<OperationResult<InstanceInfo>> AddAsync(string path, string? name = null, CancellationToken ct = default) =>
            throw new NotImplementedException();
        public Task<OperationResult<InstanceInfo>> CloneAsync(string sourceName, string newName, CancellationToken ct = default) =>
            throw new NotImplementedException();
        public Task<OperationResult<bool>> RemoveAsync(string instanceName, CancellationToken ct = default) =>
            throw new NotImplementedException();
        public Task<OperationResult<InstanceInfo>> RenameAsync(string instanceName, string newName, CancellationToken ct = default) =>
            throw new NotImplementedException();
    }

    private sealed class FakeRepositoryService : IRepositoryService
    {
        public RepositorySnapshot Current { get; set; } = RepositorySnapshot.Empty;

#pragma warning disable CS0067
        public event Action<RepositorySnapshot>? Updated;
        public event Action<OperationProgress>? RefreshProgress;
#pragma warning restore CS0067

        public Task<OperationResult<RepositorySnapshot>> LoadCachedAsync(CancellationToken ct = default) =>
            Task.FromResult(OperationResult<RepositorySnapshot>.Ok(Current));

        public Task<OperationResult<RepositorySnapshot>> RefreshAsync(CancellationToken ct = default) =>
            Task.FromResult(OperationResult<RepositorySnapshot>.Ok(Current));
    }

    private sealed class FakeCacheService : ICacheService
    {
        public IReadOnlyList<CacheEntryInfo> Entries { get; set; } = Array.Empty<CacheEntryInfo>();

        public bool Fail { get; set; }

        public Task<OperationResult<IReadOnlyList<CacheEntryInfo>>> ListEntriesAsync(CancellationToken ct = default) =>
            Task.FromResult(Fail
                ? OperationResult<IReadOnlyList<CacheEntryInfo>>.Fail("缓存读取失败。")
                : OperationResult<IReadOnlyList<CacheEntryInfo>>.Ok(Entries));
    }

    private sealed class FakeRegistryService : IRegistryService
    {
        public RegistrySnapshot Current { get; set; } = RegistrySnapshot.Empty("");

#pragma warning disable CS0067
        public event Action<RegistrySnapshot>? Changed;
#pragma warning restore CS0067

        public Task<OperationResult<RegistrySnapshot>> OpenAsync(InstanceInfo instance, CancellationToken ct = default) =>
            Task.FromResult(OperationResult<RegistrySnapshot>.Ok(Current));

        public Task<OperationResult<bool>> CloseAsync(CancellationToken ct = default) =>
            Task.FromResult(OperationResult<bool>.Ok(true));

        public Task<OperationResult<RegistrySnapshot>> RefreshAsync(CancellationToken ct = default) =>
            Task.FromResult(OperationResult<RegistrySnapshot>.Ok(Current));
    }
}
