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

    /// <summary>
    /// 联网刷新仓库索引（F-12）。
    /// <paramref name="skipETags"/> 为 true 时忽略本地 ETag 缓存（Ctrl 强制重下所有仓库）。
    /// </summary>
    Task<OperationResult<RepositorySnapshot>> RefreshAsync(bool skipETags, CancellationToken ct = default)
        => RefreshAsync(ct);

    /// <summary>
    /// 丢掉内存里的仓库快照（18 篇 §3 L2）：只清引用，不碰磁盘缓存，也不联网。
    /// 用于「启动游戏后释放资源」——37.9MB 的仓库 JSON 解析出的对象图是进程里最大的一块，
    /// 丢掉之后下次进入模组页会用 <see cref="LoadCachedAsync"/> 从本地缓存重建（R-59）。
    /// 默认实现是空操作：只关心索引快照、内存里本来就不留东西的实现者无需改动。
    /// </summary>
    void DropCachedSnapshot()
    {
    }

    // ---- 仓库列表的读取与编辑（06 篇 UI-06 / 03 篇 §6 V-04） ------------------
    //
    // 全部成员都带默认实现：只关心索引快照的实现者（模组页替身、只读降级实现）不必被迫实现，
    // 因此这次扩展是纯增量——既有实现（含测试替身）无需改动即可继续编译。

    /// <summary>当前仓库列表（按优先级升序）；未知/未加载时为空列表。</summary>
    IReadOnlyList<RepositoryEntryInfo> Repositories => RepositoryEntryInfo.Empty;

    /// <summary>仓库列表发生变化（新增/删除/移动/恢复默认/刷新失败标记）。已在 UI 线程触发。</summary>
    event Action<IReadOnlyList<RepositoryEntryInfo>>? RepositoriesChanged
    {
        add { }
        remove { }
    }

    /// <summary>读取注册表里的仓库列表（不联网，也不刷新索引）。</summary>
    Task<OperationResult<IReadOnlyList<RepositoryEntryInfo>>> LoadRepositoriesAsync(CancellationToken ct = default) =>
        Task.FromResult(OperationResult<IReadOnlyList<RepositoryEntryInfo>>.Ok(Repositories));

    /// <summary>新增仓库（追加到末尾，优先级最低）。</summary>
    Task<OperationResult<IReadOnlyList<RepositoryEntryInfo>>> AddRepositoryAsync(
        RepositoryEditRequest request,
        CancellationToken ct = default) =>
        Task.FromResult(Unsupported());

    /// <summary>移除仓库；最后一个仓库不允许移除。</summary>
    Task<OperationResult<IReadOnlyList<RepositoryEntryInfo>>> RemoveRepositoryAsync(
        string name,
        CancellationToken ct = default) =>
        Task.FromResult(Unsupported());

    /// <summary>按 <paramref name="delta"/> 调整优先级（-1 上移、+1 下移），越界时原样返回。</summary>
    Task<OperationResult<IReadOnlyList<RepositoryEntryInfo>>> MoveRepositoryAsync(
        string name,
        int delta,
        CancellationToken ct = default) =>
        Task.FromResult(Unsupported());

    /// <summary>恢复为官方默认仓库列表（需要联网取列表，离线时返回可重试失败）。</summary>
    Task<OperationResult<IReadOnlyList<RepositoryEntryInfo>>> RestoreDefaultRepositoriesAsync(
        CancellationToken ct = default) =>
        Task.FromResult(Unsupported());

    /// <summary>只刷新单个仓库的索引（其余仓库不动）。</summary>
    Task<OperationResult<IReadOnlyList<RepositoryEntryInfo>>> RefreshRepositoryAsync(
        string name,
        CancellationToken ct = default) =>
        Task.FromResult(Unsupported());

    /// <summary>默认实现给「不支持编辑仓库列表」的实现者的统一失败结果。</summary>
    private static OperationResult<IReadOnlyList<RepositoryEntryInfo>> Unsupported() =>
        OperationResult<IReadOnlyList<RepositoryEntryInfo>>.Fail("当前仓库服务不支持编辑仓库列表。");
}
