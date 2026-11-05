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
        var conflictEntries = new List<ConflictEntry>();
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

            conflictEntries.AddRange(DescribeConflicts(resolver, registry));
            conflicts.AddRange(conflictEntries.Select(entry => entry.Description));

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
            new ChangeResolution(resolved, extras, conflicts, warnings)
            {
                ConflictEntries = conflictEntries,
            });
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

    /// <summary>
    /// 把 resolver 的冲突转成结构化条目（B0/B2）。
    /// <para>
    /// Core 在 <c>proceed_with_inconsistencies</c> 下会把同一条冲突按 A→B 与 B→A 各 Add 一次
    /// （external/CKAN/Core/Relationships/RelationshipResolver.cs:385-386）；<c>ConflictDescriptions</c>
    /// 只在两侧依赖深度不等时砍一边（同文件 :597-604），两个手选模组相等时两条都留，所以字符串
    /// <c>Distinct</c> 去不掉反向重复。这里用描述串里出现的模组 <c>ToString()</c>（"identifier version"）
    /// 识别成对标识，按「identifier 排序后的二元组」无序去重；<see cref="ConflictEntry.Description"/>
    /// 原样复用 Core 已本地化的串，不自造第二套中文。
    /// </para>
    /// </summary>
    private static List<ConflictEntry> DescribeConflicts(RelationshipResolver resolver, CkanRegistry registry)
    {
        var described = resolver.ConflictDescriptions
                                .Where(description => !string.IsNullOrWhiteSpace(description))
                                .ToList();
        if (described.Count > 0)
        {
            return BuildConflictEntries(described, ConflictCandidates(resolver, registry)).ToList();
        }

        // ConflictDescriptions 为空时退回 ConflictList：key 就是用户手选的模组，标识可直接填。
        return resolver.ConflictList
                       .OrderBy(pair => pair.Key.identifier, StringComparer.Ordinal)
                       .Select(pair => new ConflictEntry(
                           string.Format("模组「{0}」：{1}", pair.Key.identifier, pair.Value),
                           pair.Key.identifier,
                           ModuleName(pair.Key)))
                       .ToList();
    }

    /// <summary>
    /// 按描述串里出现的模组 <c>ToString()</c> 识别成对标识，用「identifier 排序后的二元组」做无序去重。
    /// 识别不到两个标识时按原字符串去重兜底，<see cref="ConflictEntry.ModuleIdentifier"/> 留 null。
    /// </summary>
    /// <param name="descriptions">Core 已本地化的冲突描述。</param>
    /// <param name="candidates">可能出现在描述里的模组（冲突双方、依赖闭包、已装集合）。</param>
    public static IReadOnlyList<ConflictEntry> BuildConflictEntries(
        IEnumerable<string> descriptions,
        IEnumerable<CkanModule> candidates)
    {
        ArgumentNullException.ThrowIfNull(descriptions);
        ArgumentNullException.ThrowIfNull(candidates);

        var pool = candidates as IReadOnlyList<CkanModule> ?? candidates.ToList();
        var seenPairs = new HashSet<string>(StringComparer.Ordinal);
        var seenTexts = new HashSet<string>(StringComparer.Ordinal);
        var entries = new List<ConflictEntry>();

        foreach (var description in descriptions)
        {
            if (string.IsNullOrWhiteSpace(description))
            {
                continue;
            }

            var matched = MatchModules(description, pool);
            if (matched.Count >= 2)
            {
                if (seenPairs.Add(PairKey(matched[0].identifier, matched[1].identifier)))
                {
                    entries.Add(new ConflictEntry(description, matched[0].identifier, ModuleName(matched[0])));
                }

                continue;
            }

            if (seenTexts.Add(description))
            {
                entries.Add(new ConflictEntry(description));
            }
        }

        return entries;
    }

    /// <summary>候选模组：ConflictList 的 key（用户手选）+ 依赖闭包 + 已装集合，按 ToString() 去重。</summary>
    private static List<CkanModule> ConflictCandidates(RelationshipResolver resolver, CkanRegistry registry)
    {
        var candidates = new List<CkanModule>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        void Add(IEnumerable<CkanModule> modules)
        {
            foreach (var module in modules)
            {
                if (module is not null && seen.Add(module.ToString()))
                {
                    candidates.Add(module);
                }
            }
        }

        Add(resolver.ConflictList.Keys);
        Add(resolver.ModList());
        Add(registry.InstalledModules.Select(installed => installed.Module));
        return candidates;
    }

    /// <summary>在描述串里按出现位置匹配模组 ToString()，同一 identifier 只取首次出现。</summary>
    private static List<CkanModule> MatchModules(string description, IReadOnlyList<CkanModule> candidates)
    {
        var hits = new List<(int Index, CkanModule Module)>();
        foreach (var module in candidates)
        {
            var text = module.ToString();
            var index = text.Length == 0 ? -1 : description.IndexOf(text, StringComparison.Ordinal);
            // 词边界：避免 "alpha 1.0" 误命中 "alpha 1.0.1"
            if (index < 0 || !EndsAtWordBoundary(description, index + text.Length))
            {
                continue;
            }

            hits.Add((index, module));
        }

        var seenIdentifiers = new HashSet<string>(StringComparer.Ordinal);
        var matched = new List<CkanModule>();
        foreach (var hit in hits.OrderBy(h => h.Index))
        {
            if (seenIdentifiers.Add(hit.Module.identifier))
            {
                matched.Add(hit.Module);
            }
        }

        return matched;
    }

    private static bool EndsAtWordBoundary(string text, int end)
        => end >= text.Length || (!char.IsLetterOrDigit(text[end]) && text[end] != '.');

    /// <summary>无序去重键：两个 identifier 排序后拼接。</summary>
    private static string PairKey(string first, string second)
        => string.CompareOrdinal(first, second) <= 0
               ? first + "\n" + second
               : second + "\n" + first;

    private static string? ModuleName(CkanModule module)
        => string.IsNullOrWhiteSpace(module.name) ? null : module.name;

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
