using CkanUI.Services.Abstractions;
using CkanUI.Services.Abstractions.Dtos;
using CKAN;
using CkanUI.Services.Models;
using log4net;

namespace CkanUI.Services.Repositories;

/// <summary>
/// 仓库索引服务（04 篇 §6）。把 Core 的 <see cref="RepositoryDataManager"/> 投影成不可变
/// <see cref="RepositorySnapshot"/>，联网刷新只在 <see cref="RefreshAsync"/> 里发生。
/// 约定：任何异常都不穿过接口，失败时保留旧快照（04 篇 §3）。
/// </summary>
public sealed class RepositoryService : IRepositoryService, IDisposable
{
    /// <summary>进度事件里标识本服务任务的固定 id。</summary>
    public const string RefreshTaskId = "repository-refresh";

    private static readonly ILog Log = LogManager.GetLogger(typeof(RepositoryService));

    private readonly GameInstanceManager _instances;
    private readonly RepositoryDataManager _repositoryData;
    private readonly IDispatcher _dispatcher;
    private readonly IErrorReporter _errors;
    private readonly IUser _user;
    private readonly IRepositoryStore _store;
    private readonly RepositoryCatalog _catalog;

    private RepositorySnapshot _current = RepositorySnapshot.Empty;
    private IReadOnlyList<RepositoryEntryInfo> _repositories = RepositoryEntryInfo.Empty;
    private int _refreshing;

    public RepositoryService(GameInstanceManager instances,
                             RepositoryDataManager repositoryData,
                             IDispatcher dispatcher,
                             IErrorReporter errors,
                             IUser user,
                             IRepositoryStore? store = null)
    {
        _instances = instances ?? throw new ArgumentNullException(nameof(instances));
        _repositoryData = repositoryData ?? throw new ArgumentNullException(nameof(repositoryData));
        _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
        _errors = errors ?? throw new ArgumentNullException(nameof(errors));
        _user = user ?? throw new ArgumentNullException(nameof(user));

        // 仓库列表编辑走 <see cref="IRepositoryStore"/>：默认实现落在 Core 的注册表上，
        // 测试/降级场景可注入替身；参数带默认值，因此 DI 注册不需要额外改动。
        _store = store ?? new RegistryRepositoryStore(_instances, _repositoryData, _user);
        _catalog = new RepositoryCatalog(_store);
        _repositories = _catalog.Snapshot;
    }

    public RepositorySnapshot Current => _current;

    public event Action<RepositorySnapshot>? Updated;
    public event Action<OperationProgress>? RefreshProgress;

    public Task<OperationResult<RepositorySnapshot>> LoadCachedAsync(CancellationToken ct = default) =>
        GuardAsync(() =>
        {
            ct.ThrowIfCancellationRequested();
            var instance = _instances.CurrentInstance;
            if (instance is null) return OperationResult<RepositorySnapshot>.Ok(Publish(RepositorySnapshot.Empty));
            return OperationResult<RepositorySnapshot>.Ok(Publish(BuildSnapshot(instance, fromCache: true)));
        });

    public Task<OperationResult<RepositorySnapshot>> RefreshAsync(CancellationToken ct = default) =>
        GuardAsync(async () =>
        {
            ct.ThrowIfCancellationRequested();
            if (Interlocked.CompareExchange(ref _refreshing, 1, 0) != 0)
            {
                return OperationResult<RepositorySnapshot>.Fail("仓库索引正在刷新，请稍后再试。", retryable: true);
            }

            var instance = _instances.CurrentInstance;
            if (instance is null) return OperationResult<RepositorySnapshot>.Fail("还没有选择游戏实例，无法刷新仓库索引。");

            try
            {
                ReportProgress("正在检查仓库索引…", 0, OperationState.Running);
                var repositories = RepositoriesOf(instance);
                var downloader = new NetAsyncDownloader(_user, () => null);
                var result = _repositoryData.Update(repositories, instance.Game, skipETags: false,
                    downloader, _user);
                Log.Info($"仓库索引刷新结果：{result}");
                var snapshot = Publish(BuildSnapshot(instance, fromCache: false));
                ReportProgress(result == RepositoryDataManager.UpdateResult.NoChanges
                    ? "仓库索引已是最新。"
                    : "仓库索引已更新。", 100, OperationState.Succeeded);
                return OperationResult<RepositorySnapshot>.Ok(snapshot);
            }
            catch (OperationCanceledException)
            {
                ReportProgress("仓库索引刷新已取消。", 0, OperationState.Cancelled);
                throw;
            }
            catch (Exception ex)
            {
                // 失败保留旧快照，只上报，不把异常抛给 UI。
                ReportProgress("仓库索引刷新失败。", 0, OperationState.Failed);
                _errors.Report(ex, "刷新仓库索引", "无法从网络获取仓库索引，将继续使用本地缓存。");
                return OperationResult<RepositorySnapshot>.Fail("仓库索引刷新失败。", ex.Message, retryable: true);
            }
            finally
            {
                Interlocked.Exchange(ref _refreshing, 0);
            }
        });

    // ---- 仓库列表（UI-06 / 03 篇 §6 V-04）-------------------------------

    /// <inheritdoc />
    public IReadOnlyList<RepositoryEntryInfo> Repositories => _repositories;

    /// <inheritdoc />
    public event Action<IReadOnlyList<RepositoryEntryInfo>>? RepositoriesChanged;

    /// <inheritdoc />
    public Task<OperationResult<IReadOnlyList<RepositoryEntryInfo>>> LoadRepositoriesAsync(
        CancellationToken ct = default) =>
        GuardAsync(() =>
        {
            ct.ThrowIfCancellationRequested();
            return Apply(_catalog.Load());
        });

    /// <inheritdoc />
    public Task<OperationResult<IReadOnlyList<RepositoryEntryInfo>>> AddRepositoryAsync(
        RepositoryEditRequest request, CancellationToken ct = default) =>
        GuardAsync(() =>
        {
            ct.ThrowIfCancellationRequested();
            return Apply(_catalog.Add(request?.Name, request?.Uri));
        });

    /// <inheritdoc />
    public Task<OperationResult<IReadOnlyList<RepositoryEntryInfo>>> RemoveRepositoryAsync(
        string name, CancellationToken ct = default) =>
        GuardAsync(() =>
        {
            ct.ThrowIfCancellationRequested();
            return Apply(_catalog.Remove(name));
        });

    /// <inheritdoc />
    public Task<OperationResult<IReadOnlyList<RepositoryEntryInfo>>> MoveRepositoryAsync(
        string name, int delta, CancellationToken ct = default) =>
        GuardAsync(() =>
        {
            ct.ThrowIfCancellationRequested();
            return Apply(_catalog.Move(name, delta));
        });

    /// <inheritdoc />
    public Task<OperationResult<IReadOnlyList<RepositoryEntryInfo>>> RestoreDefaultRepositoriesAsync(
        CancellationToken ct = default) =>
        GuardAsync(() =>
        {
            ct.ThrowIfCancellationRequested();
            return Apply(_catalog.RestoreDefaults());
        });

    /// <inheritdoc />
    public Task<OperationResult<IReadOnlyList<RepositoryEntryInfo>>> RefreshRepositoryAsync(
        string name, CancellationToken ct = default) =>
        GuardAsync(async () =>
        {
            ct.ThrowIfCancellationRequested();
            if (string.IsNullOrWhiteSpace(name))
            {
                return OperationResult<IReadOnlyList<RepositoryEntryInfo>>.Fail("请先选择一个仓库。");
            }

            var instance = _instances.CurrentInstance;
            if (instance is null)
            {
                return OperationResult<IReadOnlyList<RepositoryEntryInfo>>.Fail(
                    "还没有选择游戏实例，无法刷新仓库索引。");
            }

            if (Interlocked.CompareExchange(ref _refreshing, 1, 0) != 0)
            {
                return OperationResult<IReadOnlyList<RepositoryEntryInfo>>.Fail(
                    "仓库索引正在刷新，请稍后再试。", retryable: true);
            }

            try
            {
                ReportProgress($"正在刷新仓库「{name}」…", 0, OperationState.Running);
                var outcome = _store.Refresh(name);
                switch (outcome)
                {
                    case RepositoryUpdateOutcome.NotFound:
                        ReportProgress($"没有找到仓库「{name}」。", 0, OperationState.Failed);
                        return OperationResult<IReadOnlyList<RepositoryEntryInfo>>.Fail(
                            $"没有找到名为「{name}」的仓库。");

                    case RepositoryUpdateOutcome.Failed:
                        // 单个仓库失败不影响其它仓库：保留旧快照，只标记这一行。
                        _catalog.MarkFailure(name, null);
                        Apply(_catalog.Load());
                        ReportProgress($"仓库「{name}」刷新失败。", 0, OperationState.Failed);
                        _errors.Report("刷新仓库索引", $"仓库「{name}」刷新失败，其它仓库不受影响。");
                        return OperationResult<IReadOnlyList<RepositoryEntryInfo>>.Fail(
                            $"仓库「{name}」刷新失败。", null, retryable: true);

                    default:
                        _catalog.ClearFailure(name);
                        Publish(BuildSnapshot(instance, fromCache: false));
                        ReportProgress(outcome == RepositoryUpdateOutcome.NoChanges
                            ? "仓库索引已是最新。"
                            : "仓库索引已更新。", 100, OperationState.Succeeded);
                        return Apply(_catalog.Load());
                }
            }
            catch (OperationCanceledException)
            {
                ReportProgress("仓库索引刷新已取消。", 0, OperationState.Cancelled);
                throw;
            }
            finally
            {
                Interlocked.Exchange(ref _refreshing, 0);
            }
        });

    private OperationResult<IReadOnlyList<RepositoryEntryInfo>> Apply(
        OperationResult<IReadOnlyList<RepositoryEntryInfo>> result)
    {
        if (result.IsSuccess && result.Value is not null)
        {
            PublishRepositories(result.Value);
        }

        return result;
    }

    private void PublishRepositories(IReadOnlyList<RepositoryEntryInfo> rows)
    {
        _repositories = rows;
        _dispatcher.Post(() =>
        {
            try
            {
                RepositoriesChanged?.Invoke(rows);
            }
            catch (Exception ex)
            {
                Log.Warn("仓库列表订阅方抛出异常。", ex);
            }
        });
    }

    private IReadOnlyCollection<Repository> RepositoriesOf(GameInstance instance) =>
        RegistryManager.Instance(instance, _repositoryData).registry.Repositories.Values.ToArray();

    private RepositorySnapshot BuildSnapshot(GameInstance instance, bool fromCache)
    {
        var registry = RegistryManager.Instance(instance, _repositoryData).registry;
        var repositories = registry.Repositories.Values.ToArray();
        var criteria = instance.VersionCriteria();
        var tolerance = instance.StabilityToleranceConfig;

        // 高级搜索事实（12 篇 F-20/F-21）：标签组、下载缓存、新近兼容、可替代模组都在服务层求值一次。
        var labels = ModSearchFactsFactory.LabelMembership(instance);
        var cache = _instances.Cache;
        var previouslyCompatible = PreviousCompatibility();

        var rows = new List<ModRowData>();
        foreach (var available in _repositoryData.GetAllAvailableModules(repositories))
        {
            // 优先取与当前游戏版本兼容的最新版；兼容版不存在时退化成"最新但不可用"，让列表仍然可见（04 篇 §2.1）。
            var latest = available.Latest(tolerance, criteria) ?? available.Latest(tolerance);
            if (latest is null) continue;

            var compatible = available.Latest(tolerance, criteria) is not null;
            var installed = registry.InstalledModule(latest.identifier)?.Module;
            var hasUpdate = installed is not null
                            && compatible
                            && ModListProjection.CheckHasUpdate(installed.version.ToString(), latest.version.ToString());

            rows.Add(new ModRowData(
                Identifier: latest.identifier,
                Name: latest.name,
                Author: latest.author is { Count: > 0 } authors ? string.Join(", ", authors) : null,
                InstalledVersion: installed?.version.ToString(),
                LatestVersion: latest.version.ToString(),
                DownloadCount: _repositoryData.GetDownloadCount(repositories, latest.identifier) ?? 0,
                Tags: latest.Tags?.ToList() ?? (IReadOnlyList<string>)Array.Empty<string>(),
                SizeBytes: latest.download_size > 0 ? latest.download_size : null,
                IsInstalled: installed is not null,
                HasUpdate: hasUpdate,
                IsCompatible: compatible,
                IsAutoInstalled: registry.InstalledModule(latest.identifier)?.AutoInstalled ?? false,
                IsSelected: false,
                IsImplicit: false)
            {
                Facts = ModSearchFactsFactory.From(
                    latest,
                    labels.TryGetValue(latest.identifier, out var modLabels) ? modLabels : null,
                    isCached: cache is not null
                              && latest.download is { Count: > 0 }
                              && cache.IsMaybeCachedZip(latest),
                    // 镜像上游 GUIMod.IsNew：首份快照不做标记，之后"新出现"或"由不兼容转为兼容"才算新。
                    isNew: previouslyCompatible.Count > 0
                           && (!previouslyCompatible.TryGetValue(latest.identifier, out var wasCompatible)
                               || (!wasCompatible && compatible)),
                    isReplaceable: installed is not null
                                   && registry.GetReplacement(latest, tolerance, criteria) is not null),
            });
        }

        var age = repositories.Length == 0 ? TimeSpan.MaxValue : _repositoryData.LastUpdate(repositories);
        DateTime? updatedAt = age == TimeSpan.MaxValue || age > RepositoryDataManager.TimeTillVeryStale
            ? null
            : DateTime.UtcNow - age;

        return new RepositorySnapshot(rows,
            repositories.Select(repository => repository.name).ToArray(),
            updatedAt, fromCache);
    }

    /// <summary>上一份快照的 标识符→是否兼容 映射（用于 <c>is:newly-compatible</c> 的会话态差分）。</summary>
    private IReadOnlyDictionary<string, bool> PreviousCompatibility()
    {
        var previous = _current.Available;
        if (previous is not { Count: > 0 }) return EmptyCompatibility;

        var map = new Dictionary<string, bool>(previous.Count, StringComparer.Ordinal);
        foreach (var row in previous) map[row.Identifier] = row.IsCompatible;
        return map;
    }

    private static readonly IReadOnlyDictionary<string, bool> EmptyCompatibility =
        new Dictionary<string, bool>();

    private RepositorySnapshot Publish(RepositorySnapshot snapshot)
    {
        _current = snapshot;
        _dispatcher.Post(() =>
        {
            try { Updated?.Invoke(snapshot); }
            catch (Exception ex) { Log.Warn("仓库快照订阅方抛出异常。", ex); }
        });
        return snapshot;
    }

    private void ReportProgress(string message, double percent, OperationState state)
    {
        var progress = new OperationProgress(RefreshTaskId, "repository", "刷新仓库索引", message, percent, false, state);
        _dispatcher.Post(() =>
        {
            try { RefreshProgress?.Invoke(progress); }
            catch (Exception ex) { Log.Warn("仓库进度订阅方抛出异常。", ex); }
        });
    }

    private static async Task<OperationResult<T>> GuardAsync<T>(Func<Task<OperationResult<T>>> action)
    {
        try { return await Task.Run(action).ConfigureAwait(false); }
        catch (OperationCanceledException ex) { return OperationResult<T>.Fail("操作已取消。", ex.Message, true); }
        catch (Exception ex) { return OperationResult<T>.Fail("仓库索引操作失败。", ex.ToString(), true); }
    }

    private static async Task<OperationResult<T>> GuardAsync<T>(Func<OperationResult<T>> action)
    {
        try { return await Task.Run(action).ConfigureAwait(false); }
        catch (OperationCanceledException ex) { return OperationResult<T>.Fail("操作已取消。", ex.Message, true); }
        catch (Exception ex) { return OperationResult<T>.Fail("仓库索引操作失败。", ex.ToString(), true); }
    }

    public void Dispose()
    {
        // 本服务不持有非托管资源；保留 IDisposable 只为让 DI 容器按释放顺序收尾。
    }
}
