using CkanUI.Services.Abstractions;
using CkanUI.Services.Abstractions.Dtos;

namespace CkanUI.ViewModels.Tests;

/// <summary>注册表服务替身：可换快照并手动触发 <c>Changed</c>。</summary>
public sealed class DialogsFakeRegistryService : IRegistryService
{
    /// <summary>当前快照（测试可直接赋值）。</summary>
    public RegistrySnapshot Current { get; set; } = RegistrySnapshot.Empty();

    /// <summary>快照被替换事件（由 <see cref="RaiseChanged"/> 触发）。</summary>
    public event Action<RegistrySnapshot>? Changed;

    /// <summary>触发一次快照替换。</summary>
    public void RaiseChanged(RegistrySnapshot snapshot)
    {
        Current = snapshot;
        Changed?.Invoke(snapshot);
    }

    /// <inheritdoc />
    public Task<OperationResult<RegistrySnapshot>> OpenAsync(InstanceInfo instance, CancellationToken ct = default) =>
        Task.FromResult(OperationResult<RegistrySnapshot>.Ok(Current));

    /// <inheritdoc />
    public Task<OperationResult<bool>> CloseAsync(CancellationToken ct = default) =>
        Task.FromResult(OperationResult<bool>.Ok(true));

    /// <inheritdoc />
    public Task<OperationResult<RegistrySnapshot>> RefreshAsync(CancellationToken ct = default) =>
        Task.FromResult(OperationResult<RegistrySnapshot>.Ok(Current));
}
