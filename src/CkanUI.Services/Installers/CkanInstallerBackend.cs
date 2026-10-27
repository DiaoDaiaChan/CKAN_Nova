using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

using CkanUI.Services.Abstractions.Dtos;
using CKAN;
using CKAN.Configuration;
using CKAN.IO;

namespace CkanUI.Services.Installers;

/// <summary>
/// 真实 CKAN 后端：ModuleInstaller / RelationshipResolver / NetAsyncModulesDownloader 的唯一触达点。
/// 流程对齐上游 GUI/Main/MainInstall.cs 的 InstallMods（02 篇 §7/§8）：
/// 先卸载 → 再安装/升级，每一阶段自己带 ConfirmPrompt:false。
/// </summary>
/// <remarks>
/// 刻意不在外层再套 <c>CkanTransaction.CreateTransactionScope()</c>：InstallList / UninstallList
/// 内部已经各自开事务，嵌套事务（TxF）行为未定义（02 篇 §7）。
/// 跨阶段的回滚因此由 <see cref="InstallerService"/> 编排 + 本类的 <see cref="Rollback"/> 完成。
/// </remarks>
internal sealed class CkanInstallerBackend : IInstallerBackend
{
    private readonly GameInstance _instance;
    private readonly GameInstanceManager _instances;
    private readonly RepositoryDataManager _repositoryData;
    private readonly IConfiguration _configuration;
    private readonly IUser _user;

    public CkanInstallerBackend(GameInstance instance,
                                GameInstanceManager instances,
                                RepositoryDataManager repositoryData,
                                IConfiguration configuration,
                                IUser user)
    {
        _instance = instance ?? throw new ArgumentNullException(nameof(instance));
        _instances = instances ?? throw new ArgumentNullException(nameof(instances));
        _repositoryData = repositoryData ?? throw new ArgumentNullException(nameof(repositoryData));
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        _user = user ?? throw new ArgumentNullException(nameof(user));
    }

    /// <inheritdoc />
    public IReadOnlyList<string> InstalledIdentifiers()
        => GetRegistry().InstalledModules.Select(module => module.identifier).ToArray();

    /// <inheritdoc />
    public InstallerBackendResult Resolve(ChangeSetPreview changeSet, CancellationToken ct, out InstallerPlan? plan)
    {
        ArgumentNullException.ThrowIfNull(changeSet);
        plan = null;
        ct.ThrowIfCancellationRequested();

        if (changeSet.HasConflicts)
        {
            return InstallerBackendResult.Fail("变更集存在冲突，未执行任何操作。",
                                               string.Join("；", changeSet.Conflicts),
                                               changeSet.Conflicts);
        }

        var registry = GetRegistry();
        var criteria = _instance.VersionCriteria();
        var stabilityTolerance = _instance.StabilityToleranceConfig;
        var toInstall = new List<CkanModule>();
        var toRemove = new List<CkanModule>();
        var missing = new List<string>();
        var warnings = new List<string>(changeSet.Warnings);

        foreach (var item in changeSet.Items)
        {
            if (item.Action == ChangeAction.Remove)
            {
                // 未安装的 Remove 直接忽略：注册表里没有它，卸载是无操作（不当作失败）
                if (registry.InstalledModule(item.Identifier) is { } installed)
                {
                    toRemove.Add(installed.Module);
                }

                continue;
            }

            var module = registry.LatestAvailable(item.Identifier, stabilityTolerance, criteria);
            if (module is null)
            {
                missing.Add(item.Identifier);
                continue;
            }

            if (item.ToVersion is { Length: > 0 } wanted
                && !string.Equals(module.version.ToString(), wanted, StringComparison.Ordinal))
            {
                warnings.Add($"“{item.Identifier}”请求的版本 {wanted} 不可用，将使用 {module.version}。");
            }

            toInstall.Add(module);
        }

        if (missing.Count > 0)
        {
            return InstallerBackendResult.Fail("以下模组在仓库中找不到可用版本，未执行任何操作。",
                                               string.Join("；", missing),
                                               missing,
                                               warnings);
        }

        ct.ThrowIfCancellationRequested();

        List<CkanModule> resolved;
        try
        {
            var resolver = new RelationshipResolver(toInstall, toRemove, CreateOptions(),
                                                    registry, _instance.Game, criteria);
            resolved = resolver.ModList().ToList();
        }
        catch (DependenciesNotSatisfiedKraken ex)
        {
            return InstallerBackendResult.Fail("依赖不满足，未执行任何操作。", ex.Message,
                                               toInstall.Select(module => module.identifier).ToArray(),
                                               warnings);
        }
        catch (InconsistentKraken ex)
        {
            return InstallerBackendResult.Fail("变更集与其他模组冲突，未执行任何操作。", ex.Message,
                                               toInstall.Select(module => module.identifier).ToArray(),
                                               warnings);
        }
        catch (TooManyModsProvideKraken ex)
        {
            return InstallerBackendResult.Fail("有多个模组提供同一依赖，无法自动选择，未执行任何操作。",
                                               ex.Message,
                                               toInstall.Select(module => module.identifier).ToArray(),
                                               warnings);
        }
        catch (ModuleNotFoundKraken ex)
        {
            return InstallerBackendResult.Fail("仓库缺少依赖模组，未执行任何操作。", ex.Message,
                                               toInstall.Select(module => module.identifier).ToArray(),
                                               warnings);
        }

        plan = new InstallerPlan(changeSet.Items,
                                 toRemove.Select(module => module.identifier).ToArray(),
                                 resolved.Select(module => module.identifier).ToArray(),
                                 warnings,
                                 changeSet.EstimatedDownloadBytes);
        return InstallerBackendResult.Ok(plan.ToInstall, warnings);
    }

    /// <inheritdoc />
    public InstallerBackendResult Remove(InstallerPlan plan,
                                         IProgress<InstallerBackendProgress>? progress,
                                         CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(plan);
        if (plan.ToRemove.Count == 0)
        {
            return InstallerBackendResult.Ok();
        }

        ct.ThrowIfCancellationRequested();

        if (GetCache() is not { } cache)
        {
            return InstallerBackendResult.Fail("下载缓存未初始化，无法卸载模组。", failed: plan.ToRemove);
        }

        var registryManager = GetRegistryManager();
        var installer = new ModuleInstaller(_instance, cache, _configuration, _user, ct);
        var lastPercent = 10d;
        installer.RemoveProgress += (module, done, total) =>
        {
            lastPercent = Scale(done, total, 10, 40);
            progress?.Report(new InstallerBackendProgress($"正在卸载 {module.identifier}", lastPercent));
        };
        installer.OneComplete += module
            => progress?.Report(new InstallerBackendProgress($"已完成 {module.identifier}", lastPercent, BypassThrottle: true));

        var possibleConfigOnlyDirs = new HashSet<string>(StringComparer.Ordinal);
        try
        {
            installer.UninstallList(plan.ToRemove, ref possibleConfigOnlyDirs, registryManager, ConfirmPrompt: false);
            return InstallerBackendResult.Ok(plan.ToRemove);
        }
        catch (OperationCanceledException)
        {
            return InstallerBackendResult.Cancelled("卸载已取消。");
        }
        catch (ModNotInstalledKraken ex)
        {
            return InstallerBackendResult.Fail("模组未安装，无法卸载。", ex.Message, failed: plan.ToRemove);
        }
        catch (Exception ex)
        {
            return InstallerBackendResult.Fail("卸载失败。", ex.ToString(), failed: plan.ToRemove);
        }
    }

    /// <inheritdoc />
    public InstallerBackendResult Install(InstallerPlan plan,
                                          IProgress<InstallerBackendProgress>? progress,
                                          CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(plan);
        if (plan.ToInstall.Count == 0)
        {
            return InstallerBackendResult.Ok();
        }

        ct.ThrowIfCancellationRequested();

        if (GetCache() is not { } cache)
        {
            return InstallerBackendResult.Fail("下载缓存未初始化，无法安装模组。", failed: plan.ToInstall);
        }

        var registry = GetRegistry();
        var criteria = _instance.VersionCriteria();
        var stabilityTolerance = _instance.StabilityToleranceConfig;
        var modules = new List<CkanModule>(plan.ToInstall.Count);
        foreach (var identifier in plan.ToInstall)
        {
            var module = registry.LatestAvailable(identifier, stabilityTolerance, criteria)
                         ?? registry.InstalledModule(identifier)?.Module;
            if (module is null)
            {
                return InstallerBackendResult.Fail($"依赖解析后找不到“{identifier}”的可用版本。",
                                                   failed: plan.ToInstall);
            }

            modules.Add(module);
        }

        var registryManager = GetRegistryManager();
        var installer = new ModuleInstaller(_instance, cache, _configuration, _user, ct);
        var lastPercent = 40d;
        installer.InstallProgress += (module, done, total) =>
        {
            lastPercent = Scale(done, total, 40, 99);
            progress?.Report(new InstallerBackendProgress($"正在安装 {module.identifier}", lastPercent));
        };
        installer.RemoveProgress += (module, done, total) =>
        {
            lastPercent = Scale(done, total, 40, 60);
            progress?.Report(new InstallerBackendProgress($"正在替换 {module.identifier}", lastPercent));
        };
        installer.OneComplete += module
            => progress?.Report(new InstallerBackendProgress($"已完成 {module.identifier}", lastPercent, BypassThrottle: true));

        var possibleConfigOnlyDirs = new HashSet<string>(StringComparer.Ordinal);
        try
        {
            var downloader = new NetAsyncModulesDownloader(_user, cache, userAgent: null, cancelToken: ct);
            var deduper = TryCreateDeduplicator();
            // 用户点名的才不是 autoInstalled，其余（依赖/推荐）都要标记，否则以后清理不掉
            var requested = plan.Items
                                .Where(item => item.Origin == ChangeOrigin.UserRequested)
                                .Select(item => item.Identifier)
                                .ToHashSet(StringComparer.Ordinal);
            var autoInstalled = modules.Where(module => !requested.Contains(module.identifier))
                                       .ToHashSet();

            installer.InstallList(modules, CreateOptions(), registryManager, ref possibleConfigOnlyDirs,
                                  deduper, userAgent: null, downloader: downloader,
                                  autoInstalled: autoInstalled, ConfirmPrompt: false);
            return InstallerBackendResult.Ok(plan.ToInstall);
        }
        catch (OperationCanceledException)
        {
            return InstallerBackendResult.Cancelled("安装已取消。");
        }
        catch (ModuleDownloadErrorsKraken ex)
        {
            return InstallerBackendResult.Fail("部分模组下载失败。", ex.ToString(), failed: plan.ToInstall);
        }
        catch (TooManyModsProvideKraken ex)
        {
            return InstallerBackendResult.Fail("有多个模组提供同一依赖，无法自动选择。", ex.Message,
                                               failed: plan.ToInstall);
        }
        catch (Exception ex)
        {
            return InstallerBackendResult.Fail("安装失败。", ex.ToString(), failed: plan.ToInstall);
        }
    }

    /// <inheritdoc />
    public InstallerBackendResult Rollback(IReadOnlyList<string> restore,
                                           IReadOnlyList<string> cleanup,
                                           IProgress<InstallerBackendProgress>? progress)
    {
        ArgumentNullException.ThrowIfNull(restore);
        ArgumentNullException.ThrowIfNull(cleanup);

        var warnings = new List<string>();
        var failed = new List<string>();

        if (cleanup.Count > 0)
        {
            progress?.Report(new InstallerBackendProgress("正在清理半成品…", 96, BypassThrottle: true));
            var cleanupPlan = new InstallerPlan(Array.Empty<ChangeItemDto>(), cleanup.ToArray(),
                                                Array.Empty<string>(), Array.Empty<string>(), 0);
            // 回滚不响应取消：取消之后必须把实例恢复原状（05 篇 §9）
            var result = Remove(cleanupPlan, progress, CancellationToken.None);
            warnings.AddRange(result.Warnings);
            if (result.IsSuccess)
            {
                warnings.AddRange(result.Completed.Select(identifier => $"已清理半成品：{identifier}"));
            }
            else
            {
                failed.AddRange(cleanup);
                warnings.Add("清理半成品未完全成功，请检查 GameData 与下载缓存。");
            }
        }

        if (restore.Count > 0)
        {
            progress?.Report(new InstallerBackendProgress("正在恢复被卸载的模组…", 98, BypassThrottle: true));
            var restorePlan = new InstallerPlan(Array.Empty<ChangeItemDto>(), Array.Empty<string>(),
                                                restore.ToArray(), Array.Empty<string>(), 0);
            var result = Install(restorePlan, progress, CancellationToken.None);
            warnings.AddRange(result.Warnings);
            if (result.IsSuccess)
            {
                warnings.AddRange(result.Completed.Select(identifier => $"已恢复：{identifier}（版本可能为仓库最新）"));
            }
            else
            {
                failed.AddRange(restore);
                warnings.Add("被卸载的模组未能全部恢复，请手动重新安装。");
            }
        }

        return failed.Count == 0
            ? InstallerBackendResult.Ok(warnings: warnings)
            : InstallerBackendResult.Fail("回滚未完全成功。", string.Join("；", warnings), failed, warnings);
    }

    /// <summary>
    /// 02 篇 §7：依赖求解必须传 StabilityToleranceConfig（上游 ctor 必填），
    /// 并显式给出 without_toomanyprovides_kraken / proceed_with_inconsistencies /
    /// without_enforce_consistency / with_recommends 四个字段。
    /// </summary>
    private RelationshipResolverOptions CreateOptions()
        => new(_instance.StabilityToleranceConfig)
        {
            // 推荐项随主模组一起装（与上游 ModList 的默认行为一致）
            with_recommends = true,
            with_suggests = false,
            // 有多个提供者时抛 TooManyModsProvideKraken 让用户选，而不是随便挑一个
            without_toomanyprovides_kraken = false,
            // 一致性/冲突检查必须开：宁可失败也不要装出坏注册表
            without_enforce_consistency = false,
            proceed_with_inconsistencies = false,
            allow_incompatible = false,
        };

    private NetModuleCache? GetCache() => _instances.Cache;

    private RegistryManager GetRegistryManager() => RegistryManager.Instance(_instance, _repositoryData);

    // CKAN.Registry 必须写全名：本程序集里有同名的 CkanUI.Services.Registry 命名空间会抢解析。
    private CKAN.Registry GetRegistry() => GetRegistryManager().registry;

    private InstalledFilesDeduplicator? TryCreateDeduplicator()
    {
        try
        {
            return new InstalledFilesDeduplicator(_instance, _instances.Instances.Values.ToArray(), _repositoryData);
        }
        catch (Exception)
        {
            // 硬链接去重只是优化：实例目录缺失/跨盘时不要因此让安装失败
            return null;
        }
    }

    private static double Scale(long done, long total, double from, double to)
        => total <= 0
            ? from
            : from + ((to - from) * Math.Clamp((double)done / total, 0d, 1d));
}
