using CkanUI.Services.Abstractions;
using CkanUI.Services.Abstractions.Dtos;
using CkanUI.Services.Abstractions.Profiles;

namespace CkanUI.Plugin.Storage.Tests;

/// <summary>可编排的存储服务替身：记录每次调用，把结果原样交给页面。</summary>
internal sealed class FakeStorageService : IInstanceStorageService
{
    public List<bool> DedupDryRunCalls { get; } = [];

    public List<string> DedupInstanceCalls { get; } = [];

    public int ScanCalls { get; private set; }

    public InstanceStorageReport Report { get; set; } = new(
        Array.Empty<InstanceStorageRow>(),
        0,
        0,
        new DateTime(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc),
        Array.Empty<string>());

    public ProfileDedupReport DedupReport { get; set; } = new(
        "A",
        true,
        2,
        3,
        4096,
        Array.Empty<string>());

    public OperationResult<InstanceStorageReport>? ScanResult { get; set; }

    public OperationResult<ProfileDedupReport>? DedupResult { get; set; }

    public Task<OperationResult<InstanceStorageReport>> ScanAsync(CancellationToken ct = default)
    {
        ScanCalls++;
        return Task.FromResult(ScanResult ?? OperationResult<InstanceStorageReport>.Ok(Report));
    }

    public Task<OperationResult<ProfileDedupReport>> DeduplicateProfilesAsync(
        string instanceName,
        bool dryRun = true,
        CancellationToken ct = default)
    {
        DedupInstanceCalls.Add(instanceName);
        DedupDryRunCalls.Add(dryRun);
        var report = DedupReport with { InstanceName = instanceName, DryRun = dryRun };
        return Task.FromResult(DedupResult ?? OperationResult<ProfileDedupReport>.Ok(report));
    }
}

/// <summary>可编排的实例服务替身：只提供页面需要的列表与当前实例。</summary>
internal sealed class FakeInstanceService : IInstanceService
{
    private readonly List<InstanceInfo> _instances;

    public FakeInstanceService(params InstanceInfo[] instances)
    {
        _instances = [.. instances];
        Current = instances.FirstOrDefault();
    }

    public IReadOnlyList<InstanceInfo> Instances => _instances;

    public InstanceInfo? Current { get; private set; }

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

    public Task<OperationResult<IReadOnlyList<InstanceInfo>>> RefreshAsync(CancellationToken ct = default)
        => Task.FromResult(OperationResult<IReadOnlyList<InstanceInfo>>.Ok(_instances));

    public Task<OperationResult<InstanceInfo>> SwitchAsync(string instanceName, CancellationToken ct = default)
        => Task.FromResult(OperationResult<InstanceInfo>.Fail("测试替身不切换实例"));

    public Task<OperationResult<InstanceInfo>> AddAsync(string path, string? name = null, CancellationToken ct = default)
        => Task.FromResult(OperationResult<InstanceInfo>.Fail("测试替身不添加实例"));

    public Task<OperationResult<InstanceInfo>> CloneAsync(string sourceName, string newName, CancellationToken ct = default)
        => Task.FromResult(OperationResult<InstanceInfo>.Fail("测试替身不克隆实例"));

    public Task<OperationResult<bool>> RemoveAsync(string instanceName, CancellationToken ct = default)
        => Task.FromResult(OperationResult<bool>.Fail("测试替身不删除实例"));

    public Task<OperationResult<InstanceInfo>> RenameAsync(string instanceName, string newName, CancellationToken ct = default)
        => Task.FromResult(OperationResult<InstanceInfo>.Fail("测试替身不重命名实例"));

    public bool IsGameRunning(string instanceName = "") => false;
}
