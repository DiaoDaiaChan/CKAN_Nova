using CkanUI.Services.Abstractions;
using CkanUI.Services.Abstractions.Dtos;

namespace CkanUI.ViewModels.Tests;

/// <summary>仓库服务替身：可换快照并手动触发 <c>Updated</c>，用于验证"快照整体替换后自动重取"。</summary>
public sealed class DialogsFakeRepositoryService : IRepositoryService
{
    /// <summary>当前快照（测试可直接赋值）。</summary>
    public RepositorySnapshot Current { get; set; } = RepositorySnapshot.Empty;

    /// <summary>索引刷新完成事件（由 <see cref="RaiseUpdated"/> 触发）。</summary>
    public event Action<RepositorySnapshot>? Updated;

    /// <summary>进度事件：本套件不关心，用显式访问器避免"事件从未使用"告警。</summary>
    public event Action<OperationProgress>? RefreshProgress
    {
        add => _ = value;
        remove => _ = value;
    }

    /// <summary>触发一次快照替换。</summary>
    public void RaiseUpdated(RepositorySnapshot snapshot)
    {
        Current = snapshot;
        Updated?.Invoke(snapshot);
    }

    /// <inheritdoc />
    public Task<OperationResult<RepositorySnapshot>> LoadCachedAsync(CancellationToken ct = default) =>
        Task.FromResult(OperationResult<RepositorySnapshot>.Ok(Current));

    /// <inheritdoc />
    public Task<OperationResult<RepositorySnapshot>> RefreshAsync(CancellationToken ct = default) =>
        Task.FromResult(OperationResult<RepositorySnapshot>.Ok(Current));
}
