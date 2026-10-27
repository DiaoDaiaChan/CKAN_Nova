using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using CKAN;
using CKAN.Configuration;
using CKAN.IO;
using CKAN.Versioning;
using CkanUI.Services.Abstractions;
using CkanUI.Services.Abstractions.Dtos;
using CkanRegistry = CKAN.Registry;

namespace CkanUI.Services.ChangeSets;

/// <summary>
/// 用 Core 的 <see cref="RelationshipResolver"/> 计算变更集。
/// 对齐 02-core-integration.md 与上游 GUI/Model/ModList.cs:479-585 的 ComputeFullChangeSetFromUserChangeSet：
/// 选项用 <see cref="RelationshipResolverOptions.ConflictsOpts"/>，依赖闭包取 ModList()，冲突取 ConflictDescriptions。
/// </summary>
public sealed class CoreChangeSetPlanner : IChangeSetPlanner
{
    /// <summary>无活动实例时的统一中文提示。</summary>
    public const string NoInstanceMessage = "当前没有选中的游戏实例";

    private readonly GameInstanceManager _instances;
    private readonly RepositoryDataManager _repositoryData;

    /// <summary>创建规划器。</summary>
    /// <param name="instances">Core 的实例管理器，用于取当前实例。</param>
    /// <param name="repositoryData">Core 的仓库数据管理，用于读取可用模组索引。</param>
    public CoreChangeSetPlanner(GameInstanceManager instances, RepositoryDataManager repositoryData)
    {
        _instances = instances ?? throw new ArgumentNullException(nameof(instances));
        _repositoryData = repositoryData ?? throw new ArgumentNullException(nameof(repositoryData));
    }

    /// <inheritdoc />
    public bool HasActiveInstance => _instances.CurrentInstance is not null;

    /// <inheritdoc />
    public OperationResult<ChangeResolution> Plan(
        IReadOnlyCollection<ChangeRequest> requests,
        bool includeRecommendations,
        CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        if (_instances.CurrentInstance is not { } instance)
        {
            return OperationResult<ChangeResolution>.Fail(NoInstanceMessage, null, retryable: true);
        }

        var registry = RegistryManager.Instance(instance, _repositoryData).registry;
        var stability = instance.StabilityToleranceConfig;
        var criteria = instance.VersionCriteria();
        var available = LoadAvailable(registry);

        var warnings = new List<string>();
        var resolved = new List<ResolvedChangeRequest>();
        var toInstall = new List<CkanModule>();
        var toRemove = new List<CkanModule>();

        foreach (var request in Deduplicate(requests))
        {
            ct.ThrowIfCancellationRequested();
            var installed = registry.InstalledModule(request.Identifier)?.Module;

            switch (request.Action)
            {
                case ChangeAction.Remove:
                {
                    if (installed is null)
                    {
                        warnings.Add($"「{request.Identifier}」尚未安装，已忽略这次卸载请求。");
                        break;
                    }

                    toRemove.Add(installed);
                    resolved.Add(new ResolvedChangeRequest(request.Identifier, ChangeAction.Remove,
                                                           ChangeOrigin.UserRequested, installed, null, 0));
                    break;
                }

                case ChangeAction.Replace:
                {
                    var replacement = installed is null
                        ? null
                        : registry.GetReplacement(installed, stability, criteria);
                    if (replacement is null)
                    {
                        warnings.Add($"「{request.Identifier}」当前没有可用的替代模组，已忽略这次替换请求。");
                        break;
                    }

                    toRemove.Add(replacement.ToReplace);
                    toInstall.Add(replacement.ReplaceWith);
                    resolved.Add(new ResolvedChangeRequest(request.Identifier, ChangeAction.Replace,
                                                           ChangeOrigin.UserRequested, replacement.ToReplace,
                                                           replacement.ReplaceWith,
                                                           EstimateDownload(instance, replacement.ReplaceWith)));
                    break;
                }

                default:
                {
                    var target = ResolveTarget(available, request, stability, criteria);
                    if (target is null)
                    {
                        warnings.Add(request.TargetVersion is { Length: > 0 } version
                                         ? $"仓库索引里没有「{request.Identifier}」{version} 的可安装版本，已忽略这次请求。"
                                         : $"仓库索引里没有「{request.Identifier}」的可安装版本，已忽略这次请求。");
                        break;
                    }

                    toInstall.Add(target);
                    resolved.Add(new ResolvedChangeRequest(request.Identifier, request.Action,
                                                           ChangeOrigin.UserRequested, installed, target,
                                                           EstimateDownload(instance, target)));
                    break;
                }
            }
        }

        var conflicts = new List<string>();
        var extras = new List<ResolvedModule>();

        if (toInstall.Count > 0 || toRemove.Count > 0)
        {
            ct.ThrowIfCancellationRequested();

            var resolver = new RelationshipResolver(
                toInstall, toRemove, RelationshipResolverOptions.ConflictsOpts(stability),
                registry, instance.Game, criteria);

            var requested = new HashSet<string>(toInstall.Select(m => m.identifier), StringComparer.Ordinal);
            foreach (var module in resolver.ModList())
            {
                if (requested.Contains(module.identifier))
                {
                    continue;
                }

                var installed = registry.InstalledModule(module.identifier)?.Module;
                if (installed is not null && installed.version == module.version)
                {
                    // 已经是目标版本，不需要出现在变更集里
                    continue;
                }

                var origin = resolver.IsAutoInstalled(module)
                                 ? ChangeOrigin.AutoInstalled
                                 : ChangeOrigin.Dependency;
                extras.Add(new ResolvedModule(module, installed, origin, EstimateDownload(instance, module)));
            }

            conflicts.AddRange(DescribeConflicts(resolver));

            if (includeRecommendations)
            {
                AddRecommendations(instance, registry, resolver, toInstall, toRemove, extras, warnings);
            }
        }

        if (!DownloadCacheAvailable(instance))
        {
            warnings.Add("未能读取下载缓存目录，预计下载体积可能重复计算已缓存的文件。");
        }

        return OperationResult<ChangeResolution>.Ok(
            new ChangeResolution(resolved, extras, conflicts, warnings));
    }

    /// <summary>读取全部仓库的可用模组索引，按标识合并成一个字典（同名仓库合并为 AvailableModule）。</summary>
    private Dictionary<string, AvailableModule> LoadAvailable(CkanRegistry registry)
    {
        var result = new Dictionary<string, AvailableModule>(StringComparer.Ordinal);
        foreach (var dict in _repositoryData.GetAllAvailDicts(registry.Repositories.Values))
        {
            foreach (var pair in dict)
            {
                result[pair.Key] = result.TryGetValue(pair.Key, out var existing)
                    ? AvailableModule.Merge(new[] { existing, pair.Value })
                    : pair.Value;
            }
        }

        return result;
    }

    /// <summary>按请求解析目标版本：给定版本按版本号精确匹配，否则取当前实例条件下可用的最新版本。</summary>
    private static CkanModule? ResolveTarget(
        Dictionary<string, AvailableModule> available,
        ChangeRequest request,
        StabilityToleranceConfig stability,
        GameVersionCriteria criteria)
    {
        if (!available.TryGetValue(request.Identifier, out var avail))
        {
            return null;
        }

        if (string.IsNullOrWhiteSpace(request.TargetVersion))
        {
            return avail.Latest(stability, criteria);
        }

        var wanted = request.TargetVersion.Trim();
        if (TryParseVersion(wanted) is { } version && avail.ByVersion(version) is { } exact)
        {
            return exact;
        }

        // 版本号归一化差异（例如 1.0 与 1.0.0）兜底按字面量再匹配一次
        return avail.AllAvailable().FirstOrDefault(
            m => string.Equals(m.version.ToString(), wanted, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Core 的 <see cref="ModuleVersion"/> 没有 TryParse，这里用构造异常兜住非法输入。</summary>
    private static ModuleVersion? TryParseVersion(string text)
    {
        try
        {
            return new ModuleVersion(text);
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>把 resolver 的冲突转成中文可读字符串，保证每条都带模组标识。</summary>
    private static IEnumerable<string> DescribeConflicts(RelationshipResolver resolver)
    {
        // Core 的 ConflictDescriptions 已按当前线程 UI 文化本地化（"X 1.0 与 Y 2.0 冲突"），
        // 且 ToString() 自带标识，直接复用，避免维护第二套中文文案。
        var described = resolver.ConflictDescriptions
                                .Where(d => !string.IsNullOrWhiteSpace(d))
                                .Distinct(StringComparer.Ordinal)
                                .ToList();
        if (described.Count > 0)
        {
            return described;
        }

        return resolver.ConflictList
                       .Select(pair => string.Format("模组「{0}」：{1}", pair.Key.identifier, pair.Value))
                       .ToList();
    }

    /// <summary>用 Core 的推荐项算法补充推荐（可能很重，失败一律降级为提示）。</summary>
    private static void AddRecommendations(
        GameInstance instance,
        CkanRegistry registry,
        RelationshipResolver resolver,
        IReadOnlyCollection<CkanModule> toInstall,
        IReadOnlyCollection<CkanModule> toRemove,
        List<ResolvedModule> extras,
        List<string> warnings)
    {
        try
        {
            var source = resolver.ModList().ToArray();
            var found = ModuleInstaller.FindRecommendations(
                instance, source, toInstall.ToArray(), toRemove.ToArray(), Array.Empty<CkanModule>(),
                registry, out var recommendations, out _, out _);
            if (!found)
            {
                return;
            }

            foreach (var pair in recommendations.OrderBy(p => p.Key.identifier, StringComparer.Ordinal))
            {
                if (!pair.Value.Item1)
                {
                    warnings.Add($"推荐模组「{pair.Key.identifier}」与当前变更集冲突，未计入预览。");
                    continue;
                }

                extras.Add(new ResolvedModule(pair.Key,
                                              registry.InstalledModule(pair.Key.identifier)?.Module,
                                              ChangeOrigin.Recommendation,
                                              EstimateDownload(instance, pair.Key)));
            }
        }
        catch (Exception ex)
        {
            warnings.Add($"推荐项计算失败，已跳过：{ex.Message}");
        }
    }

    /// <summary>按下载缓存是否命中估算需要下载的字节数；命中缓存或卸载时计 0。</summary>
    private static long EstimateDownload(GameInstance instance, CkanModule? module)
    {
        if (module is null)
        {
            return 0;
        }

        try
        {
            var dir = instance.DownloadCacheDir;
            if (!string.IsNullOrEmpty(dir)
                && File.Exists(Path.Combine(dir, module.StandardName())))
            {
                return 0;
            }
        }
        catch (Exception)
        {
            // 缓存目录不可读时按全量下载估算，由 Warnings 说明
        }

        return module.download_size;
    }

    /// <summary>下载缓存目录是否可读；不可读时调用方要提示体积可能偏高。</summary>
    private static bool DownloadCacheAvailable(GameInstance instance)
    {
        try
        {
            var dir = instance.DownloadCacheDir;
            return !string.IsNullOrEmpty(dir) && Directory.Exists(dir);
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>同一模组标识只处理第一次出现的请求，避免重复入队。</summary>
    private static IEnumerable<ChangeRequest> Deduplicate(IReadOnlyCollection<ChangeRequest> requests)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var request in requests)
        {
            if (string.IsNullOrWhiteSpace(request.Identifier))
            {
                continue;
            }

            if (seen.Add(request.Identifier))
            {
                yield return request;
            }
        }
    }
}
