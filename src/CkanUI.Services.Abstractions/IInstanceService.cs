using CkanUI.Services.Abstractions.Dtos;

namespace CkanUI.Services.Abstractions;

/// <summary>
/// 包装 Core 的 GameInstanceManager（02 篇 §5）。切换实例的完整序列由实现内部保证，
/// 调用方只需要 await 一个方法；事件已在 UI 线程封送。
/// </summary>
public interface IInstanceService
{
    IReadOnlyList<InstanceInfo> Instances { get; }

    InstanceInfo? Current { get; }

    /// <summary>实例列表变化（增删改）。已在 UI 线程触发。</summary>
    event Action? InstancesChanged;

    /// <summary>当前实例变化。已在 UI 线程触发。</summary>
    event Action<InstanceInfo?>? CurrentChanged;

    Task<OperationResult<IReadOnlyList<InstanceInfo>>> RefreshAsync(CancellationToken ct = default);

    /// <summary>切换实例：释放旧注册表锁 → SetCurrentInstance → 取新锁 → 重建依赖服务。</summary>
    Task<OperationResult<InstanceInfo>> SwitchAsync(string instanceName, CancellationToken ct = default);

    Task<OperationResult<InstanceInfo>> AddAsync(string path, string? name = null, CancellationToken ct = default);

    Task<OperationResult<InstanceInfo>> CloneAsync(string sourceName, string newName, CancellationToken ct = default);

    Task<OperationResult<bool>> RemoveAsync(string instanceName, CancellationToken ct = default);
}
