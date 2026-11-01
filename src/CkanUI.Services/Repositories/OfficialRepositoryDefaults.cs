using CkanUI.Services.Abstractions.Dtos;

namespace CkanUI.Services.Repositories;

/// <summary>
/// 内置的官方 CKAN 仓库（离线常量）。
/// </summary>
/// <remarks>
/// <para>Core 的官方列表只能联网取（<c>RepositoryList.DefaultRepositories</c> 会下载
/// <c>repositories.json</c>），所以全新安装后仓库列表是空的：模组页拿不到任何索引，
/// 用户要先点「恢复为官方默认仓库列表」才能看到东西。这里把官方主仓库做成离线常量，
/// 仓库列表为空时由 <see cref="RepositoryCatalog.EnsureDefaults"/> 直接补上，之后由后台刷新去真正下载索引。</para>
/// <para>只写主仓库，不写镜像：两个源指向同一份 CKAN-meta，同时启用会让首次下载翻倍。
/// 名字必须与官方 <c>repositories.json</c> 第一条一致，Core 的「默认仓库」标记按名字匹配。</para>
/// </remarks>
public static class OfficialRepositoryDefaults
{
    /// <summary>官方主仓库名（与 <c>KSP-CKAN/repositories.json</c> 第一条同名）。</summary>
    public const string PrimaryName = "KSP-default";

    /// <summary>官方主仓库的索引地址。</summary>
    public const string PrimaryUri = "https://github.com/KSP-CKAN/CKAN-meta/archive/master.tar.gz";

    /// <summary>仓库列表为空时要写入的那一条（下标即优先级）。</summary>
    public static IReadOnlyList<RepositoryEntry> Seed { get; } = [new(PrimaryName, PrimaryUri, 0)];

    /// <summary>当前列表为空（首装、或刚加的实例还没有仓库）时才需要补默认源。</summary>
    public static bool ShouldSeed(IReadOnlyList<RepositoryEntryInfo>? repositories)
        => repositories is null || repositories.Count == 0;
}
