using CkanUI.Services.Abstractions.Dtos;

namespace CkanUI.Services.Abstractions.Ai;

/// <summary>AI 看到的宿主上下文（一次快照，短小，别把 3000 行仓库塞给模型）。</summary>
/// <param name="InstanceName">当前实例名。</param>
/// <param name="GameDir">实例游戏目录；没有实例时为空。</param>
/// <param name="KspVersion">实例的 KSP 版本；未知时为空。</param>
/// <param name="InstalledCount">已安装模组数（注册表口径）。</param>
/// <param name="AvailableCount">仓库索引里的可选模组数。</param>
/// <param name="RepositoryUpdatedAt">仓库索引更新时间；无索引时为空。</param>
/// <param name="RepositoryFromCache">索引是否来自本地缓存（没联网刷新过）。</param>
public sealed record AiBridgeContext(
    string InstanceName,
    string? GameDir,
    string? KspVersion,
    int InstalledCount,
    int AvailableCount,
    DateTime? RepositoryUpdatedAt,
    bool RepositoryFromCache);

/// <summary>搜索结果里的一行（只给模型看有用的字段，不含 CKAN 类型）。</summary>
public sealed record AiModSummary(
    string Identifier,
    string Name,
    string? Author,
    string? Abstract,
    string? InstalledVersion,
    string? LatestVersion,
    long DownloadCount,
    bool IsInstalled,
    bool IsCompatible,
    bool HasUpdate,
    IReadOnlyList<string> Tags);

/// <summary>一个模组的详情（回答「这个 mod 是干什么的」用）。</summary>
public sealed record AiModDetail(
    string Identifier,
    string Name,
    string? Author,
    string? Abstract,
    string? Description,
    IReadOnlyList<string> Licenses,
    string? Homepage,
    string? InstalledVersion,
    string? LatestVersion,
    long DownloadCount,
    IReadOnlyList<string> Tags,
    bool IsInstalled,
    bool IsCompatible,
    IReadOnlyList<string> DependsOn,
    IReadOnlyList<string> Recommends,
    IReadOnlyList<string> ConflictsWith);

/// <summary>一条已安装模组。</summary>
public sealed record AiInstalledMod(string Identifier, string Version, bool IsAutoInstalled);

/// <summary>可导航页面（给「帮我打开模组页」这类指令用）。</summary>
public sealed record AiPageRef(string Key, string Title);

/// <summary>
/// AI 与 CKAN 之间的桥：只读查询走快照（不联网、不阻塞 UI），导航是唯一会动界面的动作。
/// </summary>
/// <remarks>
/// 放在 Abstractions 里的理由和插件页契约一致：插件工程不引用 App，桥的实现住在 App（它拿得到
/// <c>INavigationService</c>），插件只认这个接口。所有方法都不抛异常——AI 是外部输入，坏参数不能把界面打崩。
/// </remarks>
public interface IAiCkanBridge
{
    /// <summary>当前实例与仓库的概况。</summary>
    AiBridgeContext GetContext();

    /// <summary>按关键词搜索仓库（name / identifier / author / abstract / tags 命中，按相关度与下载量排序）。</summary>
    IReadOnlyList<AiModSummary> SearchMods(string? query, int limit = 10);

    /// <summary>按标识符取详情；找不到返回 <c>null</c>。</summary>
    AiModDetail? GetModDetail(string identifier);

    /// <summary>列出已安装模组（最多 <paramref name="limit"/> 条）。</summary>
    IReadOnlyList<AiInstalledMod> ListInstalled(int limit = 50);

    /// <summary>可导航的页面键与标题。</summary>
    IReadOnlyList<AiPageRef> ListPages();

    /// <summary>导航到某个页面键；键不存在时返回失败而不是把界面留在空白上。</summary>
    Task<OperationResult<bool>> NavigateAsync(string pageKey, CancellationToken cancellationToken = default);
}
