namespace CkanUI.Services.Abstractions.Dtos;

/// <summary>
/// 仓库列表的一行快照（03 篇 §6 V-04 / 04 篇 §2 / 06 篇 UI-06）。
/// </summary>
/// <remarks>
/// <para>只读投影：页面每次整体替换列表，不做增量更新（04 篇 §2.1）。</para>
/// <para>CKAN 的 <c>Repository</c> 没有「启用/禁用」开关，顺序即优先级（<c>priority</c> 小的先合并、覆盖大的），
/// 因此这里只暴露 <see cref="Priority"/>，不虚构 <c>Enabled</c> 字段。</para>
/// </remarks>
/// <param name="Name">仓库名（注册表里的 key，唯一）。</param>
/// <param name="Uri">仓库索引地址（绝对 URI）。</param>
/// <param name="Priority">优先级，越小越优先（0 最高）。</param>
/// <param name="LastUpdatedAt">索引上次成功刷新的时间；未知时为 <c>null</c>。</param>
/// <param name="IsStale">是否超过 <c>RepositoryDataManager.TimeTillStale</c>（3 天）。</param>
/// <param name="IsVeryStale">是否超过 <c>RepositoryDataManager.TimeTillVeryStale</c>（14 天）。</param>
/// <param name="IsDefault">是否为该游戏实例的默认仓库（<c>Repository.DefaultGameRepo</c>）。</param>
/// <param name="HasFailure">最近一次刷新是否失败。</param>
/// <param name="FailureMessage">失败原因（仅 <see cref="HasFailure"/> 为真时有意义）。</param>
public sealed record RepositoryEntryInfo(
    string Name,
    string Uri,
    int Priority,
    DateTime? LastUpdatedAt,
    bool IsStale,
    bool IsVeryStale,
    bool IsDefault,
    bool HasFailure,
    string? FailureMessage = null)
{
    /// <summary>空列表（默认实现与「还没有游戏实例」分支共用，避免每次新建数组）。</summary>
    public static IReadOnlyList<RepositoryEntryInfo> Empty { get; } = Array.Empty<RepositoryEntryInfo>();
}
