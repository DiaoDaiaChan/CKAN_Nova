using System.Text;

namespace CkanUI.Plugin.Kos.Tests.UI;

using CkanUI.Plugin.Kos.Scripts;
using CkanUI.Services.Abstractions;

/// <summary>
/// 只实现页面用到的实例服务成员：Current、Instances 与两个事件。
/// 其余方法抛 NotSupportedException，页面若偷偷切换/刷新实例会当场炸掉。
/// </summary>
internal sealed class FakeInstanceService : IInstanceService
{
    public IReadOnlyList<CkanUI.Services.Abstractions.Dtos.InstanceInfo> Instances { get; set; }
        = Array.Empty<CkanUI.Services.Abstractions.Dtos.InstanceInfo>();

    public CkanUI.Services.Abstractions.Dtos.InstanceInfo? Current { get; set; }

    public event Action? InstancesChanged;

    public event Action<CkanUI.Services.Abstractions.Dtos.InstanceInfo?>? CurrentChanged;

    public void RaiseInstancesChanged() => InstancesChanged?.Invoke();

    public void RaiseCurrentChanged(CkanUI.Services.Abstractions.Dtos.InstanceInfo? current)
    {
        Current = current;
        CurrentChanged?.Invoke(current);
    }

    public Task<OperationResult<IReadOnlyList<CkanUI.Services.Abstractions.Dtos.InstanceInfo>>> RefreshAsync(
        CancellationToken ct = default) => throw new NotSupportedException();

    public Task<OperationResult<CkanUI.Services.Abstractions.Dtos.InstanceInfo>> SwitchAsync(
        string instanceName,
        CancellationToken ct = default) => throw new NotSupportedException();

    public Task<OperationResult<CkanUI.Services.Abstractions.Dtos.InstanceInfo>> AddAsync(
        string path,
        string? name = null,
        CancellationToken ct = default) => throw new NotSupportedException();

    public Task<OperationResult<CkanUI.Services.Abstractions.Dtos.InstanceInfo>> CloneAsync(
        string sourceName,
        string newName,
        CancellationToken ct = default) => throw new NotSupportedException();

    public Task<OperationResult<bool>> RemoveAsync(string instanceName, CancellationToken ct = default)
        => throw new NotSupportedException();

    public Task<OperationResult<CkanUI.Services.Abstractions.Dtos.InstanceInfo>> RenameAsync(
        string instanceName,
        string newName,
        CancellationToken ct = default) => throw new NotSupportedException();
}
