using CkanUI.Services.Abstractions;
using CkanUI.Services.Abstractions.Dtos;

namespace CkanUI.Services.Stats;

/// <summary>
/// 下载统计的取数与装配：从当前游戏实例或已装模组、仓库索引快照与缓存清单聚合。
/// 若未选择实例或快照为空则返回空快照，绝不抛出异常。
/// </summary>
public sealed class DownloadStatsService : IDownloadStatsService
{
    private readonly IInstanceService? _instances;
    private readonly IRepositoryService _repositories;
    private readonly ICacheService _cache;
    private readonly IRegistryService _registry;

    /// <summary>完整注入构造（包含 IInstanceService）。</summary>
    public DownloadStatsService(
        IInstanceService? instances,
        IRepositoryService repositories,
        ICacheService cache,
        IRegistryService registry)
    {
        _instances = instances;
        _repositories = repositories ?? throw new ArgumentNullException(nameof(repositories));
        _cache = cache ?? throw new ArgumentNullException(nameof(cache));
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
    }

    /// <summary>兼容既有三参数构造（无 IInstanceService 时退化为仅查当前注册表/仓库快照）。</summary>
    public DownloadStatsService(
        IRepositoryService repositories,
        ICacheService cache,
        IRegistryService registry)
        : this(null, repositories, cache, registry)
    {
    }

    /// <inheritdoc />
    public DownloadStatsInput CurrentInput { get; private set; } = DownloadStatsInput.Empty;

    /// <inheritdoc />
    public async Task<OperationResult<DownloadStatsReport>> ComputeAsync(
        DownloadStatsRequest request,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ct.ThrowIfCancellationRequested();

        try
        {
            // 如果显式注入了 IInstanceService 但当前无实例，按规范返回空快照而不抛出异常。
            if (_instances is not null && _instances.Current is null)
            {
                CurrentInput = DownloadStatsInput.Empty;
                return OperationResult<DownloadStatsReport>.Ok(DownloadStatsReport.Empty);
            }

            var (modules, repoMap) = CollectModules();
            var entries = await ListCacheEntriesAsync(ct).ConfigureAwait(false);
            var input = new DownloadStatsInput(modules, entries, repoMap);
            CurrentInput = input;
            return OperationResult<DownloadStatsReport>.Ok(DownloadStatsAggregator.Compute(input, request));
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            return OperationResult<DownloadStatsReport>.Fail(
                "无法聚合下载统计。",
                ex.Message,
                retryable: true);
        }
    }

    private (IReadOnlyList<ModRowData> Modules, IReadOnlyDictionary<string, string>? Repositories) CollectModules()
    {
        var snapshot = _repositories.Current;
        if (snapshot.Available.Count == 0)
        {
            return (Array.Empty<ModRowData>(), null);
        }

        var installed = _registry.Current;
        var installedMap = installed?.Installed;

        var rows = new List<ModRowData>(snapshot.Available.Count);
        var repoMap = new Dictionary<string, string>(snapshot.Available.Count, StringComparer.OrdinalIgnoreCase);

        // 如果快照声明了单一仓库名称，则作为默认仓库
        string? defaultRepo = snapshot.RepositoryIds.Count == 1 ? snapshot.RepositoryIds[0] : null;

        foreach (var row in snapshot.Available)
        {
            InstalledModuleInfo? info = null;
            bool isInstalled = installedMap != null && installedMap.TryGetValue(row.Identifier, out info);
            rows.Add(row with { IsInstalled = isInstalled });

            // 优先从已装模组信息获取来源仓库，否则若只有一个仓库则归于该仓库
            string? repoName = (isInstalled && info is not null && !string.IsNullOrWhiteSpace(info.SourceRepository))
                ? info.SourceRepository
                : defaultRepo;

            if (!string.IsNullOrWhiteSpace(repoName))
            {
                repoMap[row.Identifier] = repoName;
            }
        }

        return (rows, repoMap);
    }

    private async Task<IReadOnlyList<CacheEntryInfo>> ListCacheEntriesAsync(CancellationToken ct)
    {
        var result = await _cache.ListEntriesAsync(ct).ConfigureAwait(false);
        return result.IsSuccess && result.Value is not null
            ? result.Value
            : Array.Empty<CacheEntryInfo>();
    }
}
