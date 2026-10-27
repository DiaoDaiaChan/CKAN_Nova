using CkanUI.Services.Abstractions.Dtos;

namespace CkanUI.Services.Abstractions;

/// <summary>包装 Core 的 RepositoryDataManager（02 篇 §6）：索引快照 + 刷新。</summary>
public interface IRepositoryService
{
    RepositorySnapshot Current { get; }

    /// <summary>索引刷新完成。已在 UI 线程触发。</summary>
    event Action<RepositorySnapshot>? Updated;

    /// <summary>刷新进度（下载索引、解析仓库）。已在 UI 线程触发。</summary>
    event Action<OperationProgress>? RefreshProgress;

    /// <summary>只读本地缓存，不联网。启动时先调用以尽快出列表。</summary>
    Task<OperationResult<RepositorySnapshot>> LoadCachedAsync(CancellationToken ct = default);

    /// <summary>联网刷新；失败时保留旧快照并返回可重试错误。</summary>
    Task<OperationResult<RepositorySnapshot>> RefreshAsync(CancellationToken ct = default);
}
