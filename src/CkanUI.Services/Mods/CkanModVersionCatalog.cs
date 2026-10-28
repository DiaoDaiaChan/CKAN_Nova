using CKAN;
using CKAN.Versioning;
using CkanUI.Services.Abstractions;
using CkanUI.Services.Abstractions.Mods;

namespace CkanUI.Services.Mods;

/// <summary>
/// 基于 Core 注册表/仓库索引的版本目录实现（F-30）。
/// </summary>
/// <remarks>
/// <para>数据源是 <c>RegistryManager.Instance(instance, repositoryData).registry.AvailableByIdentifier</c>：
/// 它跨越所有已加载仓库返回同一标识符的全部 <c>CkanModule</c>（每个版本一条），
/// 都是内存里的对象图，不联网、也不碰磁盘。</para>
/// <para>投影规则集中在 <see cref="Project"/>：纯函数、无 IO，便于单测直接喂 <c>CkanModule</c> 断言。</para>
/// </remarks>
public sealed class CkanModVersionCatalog : IModVersionCatalog
{
    private readonly GameInstanceManager _manager;
    private readonly RepositoryDataManager _repositoryData;

    /// <summary>构造。</summary>
    /// <param name="manager">当前实例来源。</param>
    /// <param name="repositoryData">仓库数据缓存（Core 的 RepositoryDataManager 单例）。</param>
    public CkanModVersionCatalog(GameInstanceManager manager, RepositoryDataManager repositoryData)
    {
        _manager = manager ?? throw new ArgumentNullException(nameof(manager));
        _repositoryData = repositoryData ?? throw new ArgumentNullException(nameof(repositoryData));
    }

    /// <inheritdoc />
    public Task<OperationResult<IReadOnlyList<ModVersionOption>>> GetVersionsAsync(
        string identifier,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(identifier))
        {
            return Task.FromResult(OperationResult<IReadOnlyList<ModVersionOption>>.Fail("缺少模组标识符。"));
        }

        if (cancellationToken.IsCancellationRequested)
        {
            return Task.FromResult(OperationResult<IReadOnlyList<ModVersionOption>>.Fail("操作已取消。"));
        }

        var instance = _manager.CurrentInstance;
        if (instance is null)
        {
            return Task.FromResult(OperationResult<IReadOnlyList<ModVersionOption>>.Fail(
                "尚未选择游戏实例，无法读取可选版本。"));
        }

        try
        {
            var registry = RegistryManager.Instance(instance, _repositoryData).registry;
            var options = Project(
                registry.AvailableByIdentifier(identifier),
                registry.GetInstalledVersion(identifier),
                instance.VersionCriteria());
            return Task.FromResult(OperationResult<IReadOnlyList<ModVersionOption>>.Ok(options));
        }
        catch (ModuleNotFoundKraken)
        {
            // 索引里没有这个模组（例如只在本地装了、仓库已下架）：空列表而不是失败，
            // 详情页据此显示「暂无版本记录」。
            return Task.FromResult(OperationResult<IReadOnlyList<ModVersionOption>>.Ok(
                Array.Empty<ModVersionOption>()));
        }
        catch (Exception ex)
        {
            return Task.FromResult(OperationResult<IReadOnlyList<ModVersionOption>>.Fail(
                "读取可选版本失败：" + ex.Message,
                retryable: true));
        }
    }

    /// <summary>
    /// 把一组 <c>CkanModule</c> 投影成版本选项，按版本号降序（最新在前）。
    /// </summary>
    /// <param name="modules">同一标识符的候选模块。</param>
    /// <param name="installed">当前实例已装的该模组；未装时为 <c>null</c>。</param>
    /// <param name="criteria">当前实例的游戏版本条件（用于兼容性判定）。</param>
    public static IReadOnlyList<ModVersionOption> Project(
        IEnumerable<CkanModule>? modules,
        CkanModule? installed,
        GameVersionCriteria criteria)
    {
        if (modules is null)
        {
            return Array.Empty<ModVersionOption>();
        }

        var list = modules
            .Where(module => module is not null)
            .OrderByDescending(module => module.version)
            .Select(module => ToOption(module, installed, criteria))
            .ToList();
        return list;
    }

    private static ModVersionOption ToOption(CkanModule module, CkanModule? installed, GameVersionCriteria criteria)
        => new(
            Version: module.version?.ToString() ?? string.Empty,
            GameCompatibility: DescribeGameCompatibility(module),
            ReleaseDate: ToOffset(module.release_date),
            DownloadSizeBytes: module.download_size > 0 ? module.download_size : null,
            DownloadUrl: module.download is { Count: > 0 } downloads ? downloads[0]?.ToString() : null,
            IsInstalled: IsSameVersion(installed, module),
            IsCompatible: module.IsCompatible(criteria),
            IsUnstable: module.release_status is { } status && status != ReleaseStatus.stable);

    private static bool IsSameVersion(CkanModule? installed, CkanModule module)
        => installed?.version is { } installedVersion
           && module.version is { } moduleVersion
           && installedVersion == moduleVersion;

    /// <summary>
    /// 游戏兼容范围文案：精确版本 → <c>KSP 1.12.3</c>；区间 → <c>KSP 1.10 – 1.12.3</c>；
    /// 单边 → <c>KSP ≥ 1.10</c> / <c>KSP ≤ 1.12</c>；未声明 → <c>null</c>（展示层写「未知兼容」）。
    /// </summary>
    private static string? DescribeGameCompatibility(CkanModule module)
    {
        if (module.ksp_version is { } exact)
        {
            return "KSP " + exact;
        }

        var min = module.ksp_version_min;
        var max = module.ksp_version_max;
        if (min is not null && max is not null)
        {
            return "KSP " + min + " – " + max;
        }

        if (min is not null)
        {
            return "KSP ≥ " + min;
        }

        return max is not null ? "KSP ≤ " + max : null;
    }

    private static DateTimeOffset? ToOffset(DateTime? value)
        => value is { } date ? new DateTimeOffset(DateTime.SpecifyKind(date, DateTimeKind.Utc)) : null;
}
