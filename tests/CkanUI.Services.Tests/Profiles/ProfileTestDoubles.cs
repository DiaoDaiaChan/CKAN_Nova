using CkanUI.Services.Abstractions;
using CkanUI.Services.Abstractions.Dtos;
using CkanUI.Services.Dedup;

namespace CkanUI.Services.Tests.Profiles;

/// <summary>测试替身：只实现配置档/存储服务真正用到的成员，其余明确抛错。</summary>
internal sealed class ProfileFakeInstanceService : IInstanceService
{
    private readonly List<InstanceInfo> _instances = new();

    public ProfileFakeInstanceService(params InstanceInfo[] instances) => _instances.AddRange(instances);

    public IReadOnlyList<InstanceInfo> Instances => _instances;

    public InstanceInfo? Current { get; set; }

    public bool GameRunning { get; set; }

    public string? LastGameRunningQuery { get; private set; }

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

    public bool IsGameRunning(string instanceName = "")
    {
        LastGameRunningQuery = instanceName;
        return GameRunning;
    }

    public Task<OperationResult<IReadOnlyList<InstanceInfo>>> RefreshAsync(CancellationToken ct = default)
        => Task.FromResult(OperationResult<IReadOnlyList<InstanceInfo>>.Ok(_instances.ToArray()));

    public Task<OperationResult<InstanceInfo>> SwitchAsync(string instanceName, CancellationToken ct = default)
        => throw new NotImplementedException();

    public Task<OperationResult<InstanceInfo>> AddAsync(string path, string? name = null, CancellationToken ct = default)
        => throw new NotImplementedException();

    public Task<OperationResult<InstanceInfo>> CloneAsync(string sourceName, string newName, CancellationToken ct = default)
        => throw new NotImplementedException();

    public Task<OperationResult<bool>> RemoveAsync(string instanceName, CancellationToken ct = default)
        => throw new NotImplementedException();

    public Task<OperationResult<InstanceInfo>> RenameAsync(string instanceName, string newName, CancellationToken ct = default)
        => throw new NotImplementedException();
}

/// <summary>记录 Close/Refresh 调用的注册表替身。</summary>
internal sealed class ProfileFakeRegistryService : IRegistryService
{
    public RegistrySnapshot Current { get; } = RegistrySnapshot.Empty("Test");

    public int CloseCalls { get; private set; }

    public int RefreshCalls { get; private set; }

    public bool CloseSucceeds { get; set; } = true;

    public bool RefreshSucceeds { get; set; } = true;

    public event Action<RegistrySnapshot>? Changed
    {
        add { }
        remove { }
    }

    public Task<OperationResult<RegistrySnapshot>> OpenAsync(InstanceInfo instance, CancellationToken ct = default)
        => throw new NotImplementedException();

    public Task<OperationResult<bool>> CloseAsync(CancellationToken ct = default)
    {
        CloseCalls++;
        return Task.FromResult(CloseSucceeds
            ? OperationResult<bool>.Ok(true)
            : OperationResult<bool>.Fail("测试故意让关闭失败。"));
    }

    public Task<OperationResult<RegistrySnapshot>> RefreshAsync(CancellationToken ct = default)
    {
        RefreshCalls++;
        return Task.FromResult(RefreshSucceeds
            ? OperationResult<RegistrySnapshot>.Ok(Current)
            : OperationResult<RegistrySnapshot>.Fail("测试故意让刷新失败。"));
    }
}

/// <summary>委托平台实现并计数的硬链接器。</summary>
internal sealed class ProfileCountingLinker : IDedupLinker
{
    private readonly IDedupLinker _inner;

    public ProfileCountingLinker(IDedupLinker inner) => _inner = inner;

    public int CreateCalls { get; private set; }

    public DedupPlatformSupport Support => _inner.Support;

    public bool TryGetIdentity(string path, out DedupFileIdentity identity)
        => _inner.TryGetIdentity(path, out identity);

    public void CreateLink(string linkPath, string existingPath)
    {
        CreateCalls++;
        _inner.CreateLink(linkPath, existingPath);
    }
}
