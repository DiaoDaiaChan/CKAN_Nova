using CkanUI.Services.Abstractions;
using CkanUI.Services.Abstractions.Dtos;

namespace CkanUI.Plugin.Soundtrack.Tests.UI;

/// <summary>
/// 只实现页面用到的实例服务成员：Current、Instances 与两个事件。
/// </summary>
/// <remarks>
/// 其余方法一律抛 NotSupportedException 而不是返回空结果：页面如果偷偷调用了切换/刷新实例，
/// 用例会直接炸掉并指出越权的那一行，而不是留下一个“看起来通过”的空壳。
/// </remarks>
internal sealed class FakeInstanceService : IInstanceService
{
    public IReadOnlyList<InstanceInfo> Instances { get; set; } = Array.Empty<InstanceInfo>();

    public InstanceInfo? Current { get; set; }

    public event Action? InstancesChanged;

    public event Action<InstanceInfo?>? CurrentChanged;

    public void RaiseInstancesChanged() => InstancesChanged?.Invoke();

    public void RaiseCurrentChanged(InstanceInfo? current)
    {
        Current = current;
        CurrentChanged?.Invoke(current);
    }

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
