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

    private RepositorySnapshot _current = RepositorySnapshot.Empty;
    private int _refreshing;

    public RepositoryService(GameInstanceManager instances,
                             RepositoryDataManager repositoryData,
                             IDispatcher dispatcher,
                             IErrorReporter errors,
                             IUser user)
    {
        _instances = instances ?? throw new ArgumentNullException(nameof(instances));
        _repositoryData = repositoryData ?? throw new ArgumentNullException(nameof(repositoryData));
        _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
        _errors = errors ?? throw new ArgumentNullException(nameof(errors));
        _user = user ?? throw new ArgumentNullException(nameof(user));
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
                var repositories = Repositories(instance);
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

    private IReadOnlyCollection<Repository> Repositories(GameInstance instance) =>
        RegistryManager.Instance(instance, _repositoryData).registry.Repositories.Values.ToArray();

    private RepositorySnapshot BuildSnapshot(GameInstance instance, bool fromCache)
    {
        var registry = RegistryManager.Instance(instance, _repositoryData).registry;
        var repositories = registry.Repositories.Values.ToArray();
        var criteria = instance.VersionCriteria();
        var tolerance = instance.StabilityToleranceConfig;

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
                IsImplicit: false));
        }

        var age = repositories.Length == 0 ? TimeSpan.MaxValue : _repositoryData.LastUpdate(repositories);
        DateTime? updatedAt = age == TimeSpan.MaxValue || age > RepositoryDataManager.TimeTillVeryStale
            ? null
            : DateTime.UtcNow - age;

        return new RepositorySnapshot(rows,
            repositories.Select(repository => repository.name).ToArray(),
            updatedAt, fromCache);
    }

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
