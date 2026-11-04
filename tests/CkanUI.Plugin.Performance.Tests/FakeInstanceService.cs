using CkanUI.Services.Abstractions;
using CkanUI.Services.Abstractions.Dtos;

namespace CkanUI.Plugin.Performance.Tests;

/// <summary>只需要「当前实例指向哪个游戏目录」的实例服务；其余成员不该被页面调用。</summary>
internal sealed class FakeInstanceService : IInstanceService
{
    public IReadOnlyList<InstanceInfo> Instances { get; set; } = Array.Empty<InstanceInfo>();

    public InstanceInfo? Current { get; set; }

    public event Action? InstancesChanged;

    public event Action<InstanceInfo?>? CurrentChanged;

    public void RaiseCurrentChanged(InstanceInfo? current)
    {
        Current = current;
        CurrentChanged?.Invoke(current);
    }

    public void RaiseInstancesChanged() => InstancesChanged?.Invoke();

    public Task<OperationResult<IReadOnlyList<InstanceInfo>>> RefreshAsync(CancellationToken ct = default)
        => throw new NotSupportedException();

    public Task<OperationResult<InstanceInfo>> SwitchAsync(string instanceName, CancellationToken ct = default)
        => throw new NotSupportedException();

    public Task<OperationResult<InstanceInfo>> AddAsync(string path, string? name = null, CancellationToken ct = default)
        => throw new NotSupportedException();

    public Task<OperationResult<InstanceInfo>> CloneAsync(string sourceName, string newName, CancellationToken ct = default)
        => throw new NotSupportedException();

    public Task<OperationResult<bool>> RemoveAsync(string instanceName, CancellationToken ct = default)
        => throw new NotSupportedException();

    public Task<OperationResult<InstanceInfo>> RenameAsync(string instanceName, string newName, CancellationToken ct = default)
        => throw new NotSupportedException();
}
