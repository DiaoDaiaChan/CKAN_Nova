using CKAN;

namespace CkanUI.Services.Repositories;

/// <summary>单个仓库索引刷新的结果（把 Core 的 <c>UpdateResult</c> 收窄成 UI 需要区分的三种情况）。</summary>
public enum RepositoryUpdateOutcome
{
    /// <summary>索引有更新。</summary>
    Updated,

    /// <summary>索引已是最新（ETag 未变）。</summary>
    NoChanges,

    /// <summary>没有找到该仓库。</summary>
    NotFound,

    /// <summary>刷新失败（网络/解析异常，具体原因由实现方记录）。</summary>
    Failed,
}

/// <summary>
/// 仓库列表的存储端口（06 篇 UI-06）：读、整体写、刷新单个仓库。
/// </summary>
/// <remarks>
/// <para>把这层抽出来有两个目的：让仓库列表的校验/排序/投影成为可离线单测的纯逻辑
/// （<see cref="RepositoryCatalog"/>），以及把 Core 的注册表事务与网络刷新挡在服务之外。</para>
/// <para>实现必须是「无实例时可用性为假、所有写操作抛异常」的语义，
/// 由 <see cref="RepositoryCatalog"/> 统一转成用户可读的失败结果。</para>
/// </remarks>
public interface IRepositoryStore
{
    /// <summary>是否已绑定当前游戏实例（注册表按实例存放）。</summary>
    bool IsAvailable { get; }

    /// <summary>该实例的默认仓库名（用于标记「默认」）；无实例时为 <c>null</c>。</summary>
    string? DefaultRepositoryName { get; }

    /// <summary>读取当前仓库列表（按 <c>priority</c> 升序）。</summary>
    IReadOnlyList<RepositoryEntry> Read();

    /// <summary>
    /// 整体替换仓库列表并保存注册表。<paramref name="entries"/> 的顺序即新优先级（下标 0 最优先）。
    /// </summary>
    /// <remarks>实现应保留同名仓库的附加字段（<c>x_mirror</c>/<c>x_comment</c>）；保存失败直接抛异常。</remarks>
    void Write(IReadOnlyList<RepositoryEntry> entries);

    /// <summary>读取官方默认仓库列表（<c>RepositoryList.DefaultRepositories</c>，会联网）；失败返回 <c>null</c>。</summary>
    IReadOnlyList<RepositoryEntry>? ReadOfficialDefaults();

    /// <summary>刷新单个仓库的索引（会联网）；不抛异常，失败用 <see cref="RepositoryUpdateOutcome.Failed"/> 表示。</summary>
    RepositoryUpdateOutcome Refresh(string name);

    /// <summary>该仓库索引已过期多久；<c>null</c> 表示未知或仍然新鲜（Core 的 <c>LastUpdate</c> 用 Zero 表达）。</summary>
    TimeSpan? AgeOf(string name);

    /// <summary>按名字取 Core 的仓库对象（只读用途）；不存在返回 <c>null</c>。</summary>
    Repository? Find(string name);
}
