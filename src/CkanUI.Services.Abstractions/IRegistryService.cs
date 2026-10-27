using CkanUI.Services.Abstractions.Dtos;

namespace CkanUI.Services.Abstractions;

/// <summary>包装 Core 的 RegistryManager（02 篇 §5/§6）：只读快照 + 变化事件。</summary>
public interface IRegistryService
{
    RegistrySnapshot Current { get; }

    /// <summary>快照被替换。已在 UI 线程触发。</summary>
    event Action<RegistrySnapshot>? Changed;

    Task<OperationResult<RegistrySnapshot>> OpenAsync(InstanceInfo instance, CancellationToken ct = default);

    /// <summary>释放注册表锁（RegistryManager.DisposeAll）。切实例前必须先调用。</summary>
    Task<OperationResult<bool>> CloseAsync(CancellationToken ct = default);

    Task<OperationResult<RegistrySnapshot>> RefreshAsync(CancellationToken ct = default);
}
