using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CKAN;
using CKAN.Versioning;
using CkanUI.Services.Abstractions;
using CkanUI.Services.Abstractions.Dtos;

namespace CkanUI.Services.Compatibility;

/// <summary>
/// 基于 CKAN Core 的兼容游戏版本管理领域服务实现（F-14 / UI-11）。
/// </summary>
public sealed class CkanCompatibleVersionsService : ICompatibleVersionsService
{
    private readonly Lazy<GameInstanceManager> _managerLazy;
    private readonly IRegistryService? _registryService;
    private readonly IRepositoryService? _repositoryService;

    public CkanCompatibleVersionsService(
        Lazy<GameInstanceManager> managerLazy,
        IRegistryService? registryService = null,
        IRepositoryService? repositoryService = null)
    {
        _managerLazy = managerLazy ?? throw new ArgumentNullException(nameof(managerLazy));
        _registryService = registryService;
        _repositoryService = repositoryService;
    }

    /// <inheritdoc />
    public Task<CompatibleVersionsSnapshot> LoadAsync(string? instanceName = null, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var manager = _managerLazy.Value;
        var instance = ResolveInstance(manager, instanceName);
        if (instance is null)
        {
            return Task.FromResult(new CompatibleVersionsSnapshot(
                InstanceName: instanceName ?? "None",
                RealVersion: "Unknown",
                Items: Array.Empty<CompatibleVersionItemDto>()));
        }

        var realVersion = instance.Version()?.ToString() ?? "Unknown";

        // 获取已知版本列表（从该实例对应的游戏定义中获取）
        var knownVersions = instance.Game.KnownVersions;

        // 获取当前实例已配置的兼容版本
        var currentCompats = new HashSet<string>(
            instance.CompatibleVersions.Select(v => v.ToString()).Where(s => !string.IsNullOrEmpty(s))!,
            StringComparer.OrdinalIgnoreCase);

        // 构建版本项目列表
        // CKAN 逻辑中，Game.KnownVersions 包含所有的明确已知版本。
        // 同时根据已知版本，计算其主/次版本通配符（如 "1.12" 对应 1.12.*）。
        var majorVersions = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var kv in knownVersions)
        {
            if (kv.IsMajorDefined && kv.IsMinorDefined)
            {
                majorVersions.Add($"{kv.Major}.{kv.Minor}");
            }
        }

        var items = new List<CompatibleVersionItemDto>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // 真实版本作为原生版本
        if (realVersion != "Unknown")
        {
            seen.Add(realVersion);
            items.Add(new CompatibleVersionItemDto(
                VersionString: realVersion,
                IsSelected: true,
                IsRealVersion: true,
                IsWildcard: false));
        }

        // 遍历所有当前已配置的兼容版本，确保它们都在列表内（包括用户曾经自定义追加的）
        foreach (var cv in currentCompats)
        {
            if (seen.Add(cv))
            {
                bool isWildcard = !cv.Contains('.') || cv.Split('.').Length <= 2 || cv.EndsWith(".*", StringComparison.Ordinal);
                items.Add(new CompatibleVersionItemDto(
                    VersionString: cv,
                    IsSelected: true,
                    IsRealVersion: false,
                    IsWildcard: isWildcard));
            }
        }

        // 添加已知主要/通配版本
        foreach (var maj in majorVersions.OrderByDescending(v => v))
        {
            if (seen.Add(maj))
            {
                items.Add(new CompatibleVersionItemDto(
                    VersionString: maj,
                    IsSelected: currentCompats.Contains(maj),
                    IsRealVersion: false,
                    IsWildcard: true));
            }
        }

        // 添加已知明确版本
        foreach (var kv in knownVersions.OrderByDescending(v => v))
        {
            var str = kv.ToString();
            if (!string.IsNullOrEmpty(str) && seen.Add(str))
            {
                items.Add(new CompatibleVersionItemDto(
                    VersionString: str,
                    IsSelected: currentCompats.Contains(str),
                    IsRealVersion: false,
                    IsWildcard: false));
            }
        }

        // 排序规则：真实版本在首位，随后通配版本与具体版本降序
        var sortedItems = items
            .OrderByDescending(i => i.IsRealVersion)
            .ThenByDescending(i => i.VersionString, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var snapshot = new CompatibleVersionsSnapshot(
            InstanceName: instance.Name,
            RealVersion: realVersion,
            Items: sortedItems);

        return Task.FromResult(snapshot);
    }

    /// <inheritdoc />
    public async Task SaveAsync(
        IReadOnlyCollection<string> selectedVersions,
        string? instanceName = null,
        bool triggerRefresh = true,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var manager = _managerLazy.Value;
        var instance = ResolveInstance(manager, instanceName);
        if (instance is null)
        {
            throw new InvalidOperationException($"无法找到游戏实例：{instanceName ?? "当前默认实例"}");
        }

        // 解析并转换版本列表为 GameVersion 对象
        var gameVersions = new List<GameVersion>();
        foreach (var vStr in selectedVersions)
        {
            if (string.IsNullOrWhiteSpace(vStr)) continue;
            if (GameVersion.TryParse(vStr.Trim(), out var parsedVersion))
            {
                gameVersions.Add(parsedVersion);
            }
        }

        // 调用 Core 接口保存配置
        instance.SetCompatibleVersions(gameVersions);

        // 如果要求触发刷新，通知仓库与注册表重新评估兼容状态
        if (triggerRefresh)
        {
            if (_registryService is not null)
            {
                await _registryService.RefreshAsync(cancellationToken).ConfigureAwait(false);
            }

            if (_repositoryService is not null)
            {
                await _repositoryService.LoadCachedAsync(cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private static GameInstance? ResolveInstance(GameInstanceManager manager, string? instanceName)
    {
        if (!string.IsNullOrWhiteSpace(instanceName))
        {
            if (manager.Instances.TryGetValue(instanceName, out var found))
            {
                return found;
            }

            return manager.Instances.Values.FirstOrDefault(i =>
                string.Equals(i.Name, instanceName, StringComparison.OrdinalIgnoreCase));
        }

        return manager.CurrentInstance;
    }
}
