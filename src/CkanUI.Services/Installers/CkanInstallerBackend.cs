using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

using CkanUI.Services.Abstractions.Dtos;
using CkanUI.Services.Unmanaged;
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

    /// <summary>跨阶段进度闸门：保护高水位与下面几个事件里的累积字典（事件来自后台线程）。</summary>
    private readonly object _progressGate = new();

    private double _progressHighWater;

    // 双段进度的分段区间（05 篇 §4）。下载塞在 InstallList 内部，以前这段零上报，百分比会卡在 8%。
    private const double DownloadFromPercent = 8d;
    private const double DownloadToPercent = 35d;
    private const double InstallFromPercent = 35d;
    private const double InstallToPercent = 99d;
    private const double RemoveFromPercent = 8d;
    private const double RemoveToPercent = 30d;

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
            // 这不是失败，是「还差一次用户裁决」：把候选集原样交给 UI 弹选择框，
            // 用户选完把那个提供者加进待装集合重试即可（对齐上游 GUI/Main/MainInstall.cs）。
            return InstallerBackendResult.NeedsChoice(BuildPendingChoice(ex, registry), warnings);
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
        // 卸载段 8→30。UninstallList 不触发 OneComplete（Core/IO/ModuleInstaller.cs:343 只在 Install 内 invoke），
        // 所以「卸完一个」只能靠 RemoveProgress 的分数到达 1 来判定。
        var removeTotal = plan.ToRemove.Count;
        var removeFractions = new Dictionary<string, double>(StringComparer.Ordinal);
        var removeOrder = new List<string>(removeTotal);
        var removeCompleted = new HashSet<string>(StringComparer.Ordinal);
        installer.RemoveProgress += (module, remaining, total) =>
        {
            var identifier = module.identifier;
            int index;
            double percent;
            bool done;
            lock (_progressGate)
            {
                index = IndexOfOrAdd(removeOrder, identifier);
                Accumulate(removeFractions, identifier, Fraction(remaining, total));
                percent = ScaleUnit(removeFractions.Values.Sum(), removeTotal, RemoveFromPercent, RemoveToPercent);
                done = removeFractions[identifier] >= 1d && removeCompleted.Add(identifier);
            }

            ReportProgress(progress, new InstallerBackendProgress(
                $"{(done ? "已卸载" : "正在卸载")} {index}/{removeTotal}：{identifier}", percent, BypassThrottle: done)
            {
                Phase = InstallerPhase.Remove,
                ItemIndex = index,
                ItemTotal = removeTotal,
            });
        };

        var possibleConfigOnlyDirs = new HashSet<string>(StringComparer.Ordinal);
        try
        {
            installer.UninstallList(plan.ToRemove, ref possibleConfigOnlyDirs, registryManager, ConfirmPrompt: false);
            // 12 篇 F-43：Core 收集的"可能只剩配置"的目录不能丢，交给残留清理对话框让用户决定。
            return InstallerBackendResult.Ok(plan.ToRemove,
                                             residualDirectories: ResidualDirectoriesOf(possibleConfigOnlyDirs));
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
        // 安装段 35→99：按「条目分数」累积，一个模组装完就报 m/M。
        var installTotal = modules.Count;
        var installFractions = new Dictionary<string, double>(StringComparer.Ordinal);
        var installOrder = new List<string>(installTotal);
        var installCompleted = new HashSet<string>(StringComparer.Ordinal);

        void ReportInstall(InstallerPhase phase, string identifier, string label, string verb, double fraction, bool completed)
        {
            int index;
            double percent;
            int done;
            lock (_progressGate)
            {
                index = IndexOfOrAdd(installOrder, identifier);
                if (completed)
                {
                    installFractions[identifier] = 1d;
                    installCompleted.Add(identifier);
                }
                else
                {
                    Accumulate(installFractions, identifier, fraction);
                }

                percent = ScaleUnit(installFractions.Values.Sum(), installTotal, InstallFromPercent, InstallToPercent);
                done = installCompleted.Count;
            }

            var counted = completed ? done : index;
            ReportProgress(progress, new InstallerBackendProgress(
                $"{verb} {counted}/{installTotal}：{label}", percent, BypassThrottle: completed)
            {
                Phase = phase,
                ItemIndex = counted,
                ItemTotal = installTotal,
            });
        }

        installer.InstallProgress += (module, remaining, total)
            => ReportInstall(InstallerPhase.Install, module.identifier, DisplayName(module), "正在安装",
                             Fraction(remaining, total), completed: false);
        installer.RemoveProgress += (module, remaining, total)
            => ReportInstall(InstallerPhase.Remove, module.identifier, DisplayName(module.Module), "正在替换",
                             Fraction(remaining, total), completed: false);
        installer.OneComplete += module
            => ReportInstall(InstallerPhase.Install, module.identifier, DisplayName(module), "已安装", 1d, completed: true);

        var possibleConfigOnlyDirs = new HashSet<string>(StringComparer.Ordinal);
        try
        {
            var downloader = new NetAsyncModulesDownloader(_user, cache, userAgent: null, cancelToken: ct);
            SubscribeDownloadProgress(downloader, cache, modules, progress);
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
            ReportProgress(progress, new InstallerBackendProgress(
                $"已安装 {installTotal}/{installTotal}", InstallToPercent, BypassThrottle: true)
            {
                Phase = InstallerPhase.Install,
                ItemIndex = installTotal,
                ItemTotal = installTotal,
            });
            // 升级/替换同样会留下旧版本的配置目录，一并上报（12 篇 F-43）。
            return InstallerBackendResult.Ok(plan.ToInstall,
                                             residualDirectories: ResidualDirectoriesOf(possibleConfigOnlyDirs));
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
            // 解析阶段漏掉的情况（仓库在两次调用之间变了、或 InstallList 内部重新解析才撞上）。
            // 交给上层弹选择框，选完重试；已经下到缓存的文件不会白下。
            return InstallerBackendResult.NeedsChoice(BuildPendingChoice(ex, GetRegistry()));
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

    /// <summary>
    /// 把 Core 报上来的绝对残留目录折算成 GameData 相对路径（12 篇 F-43）。
    /// 落在 GameData 之外的一律丢弃：清理服务只接受 GameData 内的相对路径，越界路径不该进契约。
    /// </summary>
    private IReadOnlyList<string> ResidualDirectoriesOf(HashSet<string>? absoluteDirectories)
    {
        // UninstallList 的 ref 形参允许把集合置空（Core 在无残留时确实会），这里按"没有残留"处理。
        if (absoluteDirectories is null || absoluteDirectories.Count == 0)
        {
            return Array.Empty<string>();
        }

        var gameData = UnmanagedScanner.ResolveGameData(_instance.GameDir);
        var result = new List<string>(absoluteDirectories.Count);
        foreach (var directory in absoluteDirectories)
        {
            var relative = UnmanagedScanner.ToRelative(gameData, directory);
            if (relative.Length == 0
                || relative.StartsWith("..", StringComparison.Ordinal)
                || Path.IsPathRooted(relative))
            {
                continue;
            }

            if (!result.Contains(relative, StringComparer.OrdinalIgnoreCase))
            {
                result.Add(relative);
            }
        }

        result.Sort(StringComparer.OrdinalIgnoreCase);
        return result;
    }

    /// <summary>
    /// 把 Core 的「多个模组都能提供某依赖」翻译成交互式选择请求（F-34/F-35）。
    /// <para>
    /// 排序对齐上游 GUI/Main/MainInstall.cs:348-385：已缓存优先 → 就是指名的那个 → 名字，
    /// 让用户第一眼看到最省事的候选。
    /// </para>
    /// </summary>
    private VirtualModuleRequest BuildPendingChoice(TooManyModsProvideKraken ex, CKAN.Registry registry)
    {
        var cache = GetCache();

        var providers = ex.modules
            .OrderByDescending(module => IsCached(cache, module))
            .ThenByDescending(module => string.Equals(module.identifier, ex.requested, StringComparison.OrdinalIgnoreCase))
            .ThenBy(module => string.IsNullOrWhiteSpace(module.name) ? module.identifier : module.name,
                    StringComparer.OrdinalIgnoreCase)
            .Select(module => new VirtualModuleCandidate(
                module.identifier,
                string.IsNullOrWhiteSpace(module.name) ? module.identifier : module.name,
                string.IsNullOrWhiteSpace(module.@abstract) ? null : module.@abstract,
                IsCached(cache, module),
                registry.InstalledModule(module.identifier) is not null))
            .ToArray();

        return new VirtualModuleRequest(
            $"「{ex.requested}」有 {providers.Length} 个模组可以提供，请选择一个继续。",
            providers,
            ex.requested);
    }

    private static bool IsCached(NetModuleCache? cache, CkanModule module)
    {
        if (cache is null)
        {
            return false;
        }

        try
        {
            return cache.IsCached(module);
        }
        catch (Exception)
        {
            // 缓存目录读不到时（正是「未能读取下载缓存目录」那条提示的来源）别让选择框跟着炸
            return false;
        }
    }

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

    /// <summary>
    /// 下载段进度（05 篇 §4）。Core 把下载塞在 <c>InstallList</c> 内部，此前这里零上报，
    /// 百分比会一直挂在 8% 直到开始安装。订阅 downloader 后按下载字节加权推进 8→35，每条都带 n/N。
    /// </summary>
    private void SubscribeDownloadProgress(NetAsyncModulesDownloader downloader,
                                           NetModuleCache cache,
                                           IReadOnlyList<CkanModule> modules,
                                           IProgress<InstallerBackendProgress>? progress)
    {
        if (progress is null)
        {
            return;
        }

        var downloadTotal = modules.Count;
        var order = new List<string>(downloadTotal);
        var fractions = new Dictionary<string, double>(StringComparer.Ordinal);
        var weights = new Dictionary<string, double>(StringComparer.Ordinal);
        var completed = new HashSet<string>(StringComparer.Ordinal);
        foreach (var module in modules)
        {
            weights[module.identifier] = Math.Max(module.download_size, 0);
        }

        var weightTotal = weights.Values.Sum();
        // 元数据里全没有下载大小时退化成「每个模组 1 份」的计数进度。
        if (weightTotal <= 0d)
        {
            foreach (var identifier in weights.Keys.ToArray())
            {
                weights[identifier] = 1d;
            }

            weightTotal = downloadTotal;
        }

        double Percent()
        {
            var done = 0d;
            foreach (var pair in fractions)
            {
                done += pair.Value * weights[pair.Key];
            }

            return ScaleUnit(done, weightTotal, DownloadFromPercent, DownloadToPercent);
        }

        // 缓存命中不会触发 downloader.OneComplete，先记成已完成，否则 n/N 永远追不平。
        foreach (var module in modules.Where(cache.IsCached))
        {
            fractions[module.identifier] = 1d;
            completed.Add(module.identifier);
            order.Add(module.identifier);
        }

        double startPercent;
        int cachedCount;
        lock (_progressGate)
        {
            startPercent = Percent();
            cachedCount = completed.Count;
        }

        if (cachedCount < downloadTotal)
        {
            ReportProgress(progress, new InstallerBackendProgress(
                $"准备下载 {cachedCount}/{downloadTotal} 个模组…", startPercent, BypassThrottle: true)
            {
                Phase = InstallerPhase.Download,
                ItemIndex = cachedCount,
                ItemTotal = downloadTotal,
            });
        }

        downloader.DownloadProgress += (module, remaining, total) =>
        {
            int index;
            double percent;
            lock (_progressGate)
            {
                // download_size 缺失的模组用实测文件大小补位（只会让分母变大，高水位闸门兜住倒退）。
                if (total > 0 && weights.TryGetValue(module.identifier, out var weight) && weight <= 0d)
                {
                    weights[module.identifier] = total;
                    weightTotal += total;
                }

                index = IndexOfOrAdd(order, module.identifier);
                Accumulate(fractions, module.identifier, Fraction(remaining, total));
                percent = Percent();
            }

            ReportProgress(progress, new InstallerBackendProgress(
                $"正在下载 {index}/{downloadTotal}：{DisplayName(module)}", percent)
            {
                Phase = InstallerPhase.Download,
                ItemIndex = index,
                ItemTotal = downloadTotal,
            });
        };

        downloader.OneComplete += module =>
        {
            int count;
            double percent;
            lock (_progressGate)
            {
                fractions[module.identifier] = 1d;
                completed.Add(module.identifier);
                IndexOfOrAdd(order, module.identifier);
                count = completed.Count;
                percent = Percent();
            }

            ReportProgress(progress, new InstallerBackendProgress(
                $"已下载 {count}/{downloadTotal}：{DisplayName(module)}", percent, BypassThrottle: true)
            {
                Phase = InstallerPhase.Download,
                ItemIndex = count,
                ItemTotal = downloadTotal,
            });
        };

        downloader.AllComplete += () =>
        {
            double percent;
            lock (_progressGate)
            {
                percent = Math.Max(Percent(), DownloadToPercent);
            }

            ReportProgress(progress, new InstallerBackendProgress(
                $"下载完成 {downloadTotal}/{downloadTotal}", percent, BypassThrottle: true)
            {
                Phase = InstallerPhase.Download,
                ItemIndex = downloadTotal,
                ItemTotal = downloadTotal,
            });
        };
    }

    /// <summary>
    /// 进度统一出口：同一实例内跨阶段只增不减。回滚会在同一后端实例上复用 Remove/Install（05 篇 §9），
    /// 不夹高水位闸门就会出现 96% → 8% 这种倒退。
    /// </summary>
    private void ReportProgress(IProgress<InstallerBackendProgress>? progress, InstallerBackendProgress value)
    {
        if (progress is null)
        {
            return;
        }

        InstallerBackendProgress monotonic;
        lock (_progressGate)
        {
            monotonic = value.Percent < _progressHighWater
                ? value with { Percent = _progressHighWater }
                : value;
            _progressHighWater = Math.Max(_progressHighWater, value.Percent);
        }

        progress.Report(monotonic);
    }

    /// <summary>上游事件的首参是「剩余字节」，完成量要算 total - remaining（Core/IO/ModuleInstaller.cs:1276）。</summary>
    private static double Fraction(long remaining, long total)
        => total <= 0
            ? 0d
            : Math.Clamp((double)(total - Math.Max(remaining, 0)) / total, 0d, 1d);

    private static double ScaleUnit(double done, double total, double from, double to)
        => total <= 0
            ? to
            : from + ((to - from) * Math.Clamp(done / total, 0d, 1d));

    /// <summary>条目首次出现时追加，返回 1 起的序号。调用方必须持有 <c>_progressGate</c>。</summary>
    private static int IndexOfOrAdd(List<string> order, string identifier)
    {
        var index = order.IndexOf(identifier);
        if (index < 0)
        {
            order.Add(identifier);
            index = order.Count - 1;
        }

        return index + 1;
    }

    /// <summary>同一模组的分数只保留最大值：事件可能乱序或重复上报。调用方必须持有 <c>_progressGate</c>。</summary>
    private static void Accumulate(IDictionary<string, double> fractions, string identifier, double fraction)
    {
        var previous = fractions.TryGetValue(identifier, out var value) ? value : 0d;
        fractions[identifier] = Math.Max(previous, fraction);
    }

    private static string DisplayName(CkanModule module)
        => string.IsNullOrEmpty(module.name) ? module.identifier : module.name;
}
