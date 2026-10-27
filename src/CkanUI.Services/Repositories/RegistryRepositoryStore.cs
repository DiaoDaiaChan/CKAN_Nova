using CKAN;
using log4net;

namespace CkanUI.Services.Repositories;

/// <summary>
/// 仓库列表的注册表实现（03 篇 §6 V-04）：读写 <c>Registry.Repositories</c> 并保存 registry.json。
/// </summary>
/// <remarks>
/// <para>仓库列表存在注册表里（<c>Registry.Repositories</c>），不在 <c>GameInstance</c> 上；
/// 改动后必须显式 <c>RegistryManager.Save()</c> 才会落盘。</para>
/// <para>本类是唯一碰 Core 注册表事务与网络的实现，逻辑校验全部在 <see cref="RepositoryCatalog"/> 里，
/// 因此本类不需要单测覆盖（离线无法构造真实注册表）。</para>
/// </remarks>
public sealed class RegistryRepositoryStore : IRepositoryStore
{
    private static readonly ILog Log = LogManager.GetLogger(typeof(RegistryRepositoryStore));

    private readonly GameInstanceManager _instances;
    private readonly RepositoryDataManager _repositoryData;
    private readonly IUser _user;

    public RegistryRepositoryStore(GameInstanceManager instances,
                                   RepositoryDataManager repositoryData,
                                   IUser user)
    {
        _instances = instances ?? throw new ArgumentNullException(nameof(instances));
        _repositoryData = repositoryData ?? throw new ArgumentNullException(nameof(repositoryData));
        _user = user ?? throw new ArgumentNullException(nameof(user));
    }

    /// <inheritdoc />
    public bool IsAvailable => _instances.CurrentInstance is not null;

    /// <inheritdoc />
    public string? DefaultRepositoryName
        => _instances.CurrentInstance is { } instance ? Repository.DefaultGameRepo(instance.Game).name : null;

    /// <inheritdoc />
    public IReadOnlyList<RepositoryEntry> Read()
    {
        var repositories = CurrentRepositories();
        if (repositories is null)
        {
            return Array.Empty<RepositoryEntry>();
        }

        return repositories.Values
            .Select(repository => new RepositoryEntry(repository.name, repository.uri.ToString(), repository.priority))
            .OrderBy(entry => entry.Priority)
            .ThenBy(entry => entry.Name, StringComparer.Ordinal)
            .ToArray();
    }

    /// <inheritdoc />
    public void Write(IReadOnlyList<RepositoryEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);
        var instance = _instances.CurrentInstance
                       ?? throw new InvalidOperationException("还没有选择游戏实例，无法保存仓库列表。");
        var manager = RegistryManager.Instance(instance, _repositoryData);
        var registry = manager.registry;

        var existing = new Dictionary<string, Repository>(StringComparer.Ordinal);
        foreach (var repository in registry.Repositories.Values)
        {
            existing[repository.name] = repository;
        }

        var replaced = new SortedDictionary<string, Repository>(StringComparer.Ordinal);
        for (var index = 0; index < entries.Count; index++)
        {
            var entry = entries[index];
            if (existing.TryGetValue(entry.Name, out var repository))
            {
                // 复用同一个 Repository 对象，保住 x_mirror / x_comment 等只在 repositories.json 里出现的字段。
                repository.uri = new Uri(entry.Uri, UriKind.Absolute);
            }
            else
            {
                repository = new Repository(entry.Name, entry.Uri);
            }

            repository.priority = index;
            replaced[entry.Name] = repository;
        }

        registry.RepositoriesSet(replaced);
        manager.Save();
        Log.Info($"仓库列表已保存：{replaced.Count} 个仓库。");
    }

    /// <inheritdoc />
    public IReadOnlyList<RepositoryEntry>? ReadOfficialDefaults()
    {
        var instance = _instances.CurrentInstance;
        if (instance is null)
        {
            return null;
        }

        var list = RepositoryList.DefaultRepositories(instance.Game, userAgent: null);
        if (list?.repositories is null)
        {
            return null;
        }

        var entries = new List<RepositoryEntry>(list.repositories.Length);
        for (var index = 0; index < list.repositories.Length; index++)
        {
            var repository = list.repositories[index];
            entries.Add(new RepositoryEntry(repository.name, repository.uri.ToString(), index));
        }

        return entries;
    }

    /// <inheritdoc />
    public RepositoryUpdateOutcome Refresh(string name)
    {
        var instance = _instances.CurrentInstance;
        if (instance is null)
        {
            return RepositoryUpdateOutcome.Failed;
        }

        var repository = Find(name);
        if (repository is null)
        {
            return RepositoryUpdateOutcome.NotFound;
        }

        try
        {
            var downloader = new NetAsyncDownloader(_user, () => null);
            var result = _repositoryData.Update(new[] { repository }, instance.Game, skipETags: false,
                downloader, _user);
            Log.Info($"仓库「{name}」刷新结果：{result}");
            return result switch
            {
                RepositoryDataManager.UpdateResult.Updated => RepositoryUpdateOutcome.Updated,
                RepositoryDataManager.UpdateResult.NoChanges => RepositoryUpdateOutcome.NoChanges,
                _ => RepositoryUpdateOutcome.Failed,
            };
        }
        catch (Exception ex)
        {
            Log.Warn($"刷新仓库「{name}」失败。", ex);
            return RepositoryUpdateOutcome.Failed;
        }
    }

    /// <inheritdoc />
    public TimeSpan? AgeOf(string name)
    {
        var repository = Find(name);
        if (repository is null)
        {
            return null;
        }

        // Core 只提供「最旧的那个过期仓库有多旧」，未过期时返回 Zero（无法区分「刚刷新」与「未知」）。
        var age = _repositoryData.LastUpdate(new[] { repository });
        return age > TimeSpan.Zero ? age : null;
    }

    /// <inheritdoc />
    public Repository? Find(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return null;
        }

        var repositories = CurrentRepositories();
        return repositories is not null && repositories.TryGetValue(name, out var repository)
            ? repository
            : null;
    }

    private IReadOnlyDictionary<string, Repository>? CurrentRepositories()
        => _instances.CurrentInstance is { } instance
            ? RegistryManager.Instance(instance, _repositoryData).registry.Repositories
            : null;
}
