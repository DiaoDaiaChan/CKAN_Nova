using CkanUI.Services.Abstractions.Dtos;

using CKAN;
using CKAN.Versioning;

namespace CkanUI.Services.ModPacks;

/// <summary>
/// <see cref="IModPackStore"/> 的 Core 实现：把上游 <c>RegistryManager</c> / <c>CkanModule</c> /
/// <c>NetModuleCache</c> 的能力收窄成六个同步方法，好让 <see cref="ModPackCatalog"/> 能在没有游戏目录的
/// 环境里被离线测试。
/// </summary>
/// <remarks>
/// 格式语义全部来自上游，本类不自定义任何字段：
/// <list type="bullet">
/// <item>导出 JSON = <c>RegistryManager.GenerateModpack(recommends, with_versions).ToJson()</c>
/// （<c>external/CKAN/Core/Registry/RegistryManager.cs:477</c>，元数据包 kind=metapackage，
/// 依赖项写成 <c>depends</c> 或 <c>recommends</c>），因此产物一定能被 <c>ckan import</c> 消费。</item>
/// <item>导入解析 = <c>CkanModule.FromFile</c>（<c>external/CKAN/Core/Types/CkanModule.cs</c>），
/// 与 <c>ckan import</c> 走同一条解析路径。</item>
/// <item>缓存包 = <c>NetModuleCache.GetCachedFilename</c>（<c>external/CKAN/Core/Net/NetModuleCache.cs:67</c>）。</item>
/// </list>
/// 注意：本程序集里有 <c>CkanUI.Services.Registry</c> 命名空间，<c>CKAN.Registry</c> 必须写全名。
/// </remarks>
internal sealed class CkanModPackStore : IModPackStore
{
    private readonly GameInstanceManager _instances;
    private readonly RepositoryDataManager _repositoryData;

    /// <summary>构造 Core 存储。</summary>
    public CkanModPackStore(GameInstanceManager instances, RepositoryDataManager repositoryData)
    {
        _instances = instances ?? throw new ArgumentNullException(nameof(instances));
        _repositoryData = repositoryData ?? throw new ArgumentNullException(nameof(repositoryData));
    }

    /// <inheritdoc />
    public bool HasActiveInstance => _instances.CurrentInstance is not null;

    /// <inheritdoc />
    public string? InstanceName => _instances.CurrentInstance?.Name;

    /// <inheritdoc />
    public IReadOnlyList<InstalledModuleInfo> InstalledModules()
    {
        var instance = _instances.CurrentInstance
                       ?? throw new InvalidOperationException("当前没有选中的游戏实例。");
        return Project(instance);
    }

    /// <inheritdoc />
    public IReadOnlyList<InstalledModuleInfo> InstalledModulesIn(string instanceName)
    {
        if (string.IsNullOrWhiteSpace(instanceName) || !_instances.Instances.TryGetValue(instanceName.Trim(), out var instance))
        {
            throw new InvalidOperationException($"找不到游戏实例“{instanceName}”。");
        }

        return Project(instance);
    }

    /// <inheritdoc />
    public string BuildModPackJson(bool useRecommends, bool withVersions)
    {
        var instance = _instances.CurrentInstance
                       ?? throw new InvalidOperationException("当前没有选中的游戏实例。");
        var manager = RegistryManager.Instance(instance, _repositoryData);
        return manager.GenerateModpack(useRecommends, withVersions).ToJson();
    }

    /// <inheritdoc />
    public ModPackFile LoadModPackFile(string path)
    {
        var module = CkanModule.FromFile(path);
        var entries = new List<ModPackReference>();
        AddReferences(entries, module.depends, isOptional: false);
        AddReferences(entries, module.recommends, isOptional: true);
        return new ModPackFile(
            path,
            module.name ?? string.Empty,
            module.version?.ToString() ?? string.Empty,
            entries);
    }

    /// <inheritdoc />
    public ModPackAvailability Lookup(string identifier, string? version)
    {
        var instance = _instances.CurrentInstance;
        if (instance is null)
        {
            return ModPackAvailability.NotFound;
        }

        var registry = GetRegistry(instance);
        var latest = registry.LatestAvailable(identifier, instance.StabilityToleranceConfig, instance.VersionCriteria());
        if (latest is null)
        {
            return ModPackAvailability.NotFound;
        }

        var requestedExists = true;
        if (!string.IsNullOrWhiteSpace(version))
        {
            requestedExists = TryParseVersion(version) is { } wanted
                              && registry.GetModuleByVersion(identifier, wanted) is not null;
        }

        return new ModPackAvailability(true, latest.version?.ToString(), requestedExists);
    }

    /// <inheritdoc />
    public string? CachedArchive(string identifier, string version)
    {
        var instance = _instances.CurrentInstance;
        var cache = _instances.Cache;
        if (instance is null || cache is null)
        {
            return null;
        }

        var registry = GetRegistry(instance);
        var module = registry.InstalledModule(identifier)?.Module;
        if (module is null && TryParseVersion(version) is { } wanted)
        {
            module = registry.GetModuleByVersion(identifier, wanted);
        }

        return module is null ? null : cache.GetCachedFilename(module);
    }

    private static void AddReferences(List<ModPackReference> entries, List<RelationshipDescriptor>? descriptors, bool isOptional)
    {
        if (descriptors is null)
        {
            return;
        }

        foreach (var descriptor in descriptors)
        {
            if (descriptor is not ModuleRelationshipDescriptor { } moduleDescriptor
                || string.IsNullOrWhiteSpace(moduleDescriptor.name))
            {
                continue;
            }

            // 导出时上游总是写 version（with_versions=true）；手写的 .ckan 可能只给 min_version，
            // 这里把它当作「要求的最低版本」，语义仍然只用于预览提示，不会放宽安装条件。
            var version = moduleDescriptor.version ?? moduleDescriptor.min_version;
            entries.Add(new ModPackReference(moduleDescriptor.name, version?.ToString(), isOptional));
        }
    }

    private static ModuleVersion? TryParseVersion(string? version)
    {
        if (string.IsNullOrWhiteSpace(version))
        {
            return null;
        }

        try
        {
            return new ModuleVersion(version.Trim());
        }
        catch (Exception)
        {
            return null;
        }
    }

    private IReadOnlyList<InstalledModuleInfo> Project(GameInstance instance)
    {
        var registry = GetRegistry(instance);
        return registry.InstalledModules
            .Select(module => new InstalledModuleInfo(
                module.identifier,
                module.Module.version?.ToString() ?? string.Empty,
                module.AutoInstalled,
                null,
                module.InstallTime))
            .ToList();
    }

    private CKAN.Registry GetRegistry(GameInstance instance)
        => RegistryManager.Instance(instance, _repositoryData).registry;
}
