using System;
using System.Collections.Generic;
using System.Linq;
using CKAN;
using CKAN.Configuration;
using CKAN.Games;
using CKAN.Versioning;
using CkanUI.Services.Abstractions.Dtos;
using CkanUI.Services.ChangeSets;

namespace CkanUI.Services.Models;

/// <summary>
/// 模组列表的核心纯函数算法（MOD-04）：
/// 彻底解耦 DataGridView，把过滤、排序、标签判定、更新检测和变更集推导抽成可测试的不可变纯函数。
/// </summary>
public static class ModIndexOperations
{
    #region Visibility & Filtering

    /// <summary>
    /// 判定一个 GUIMod 是否在当前搜索集与隐藏标签规则下可见。
    /// </summary>
    public static bool IsVisible(
        GUIMod mod,
        IReadOnlyCollection<ModSearch>? activeSearches,
        GameInstance instance,
        IRegistryQuerier registry,
        ModuleLabelList? allLabels,
        ModuleTagList? allTags)
    {
        var matchesSearch = activeSearches == null
                            || activeSearches.Count == 0
                            || activeSearches.Any(s => s?.Matches(mod) ?? true);

        return matchesSearch && !IsHiddenByTagsOrLabels(mod, activeSearches, instance, registry, allLabels, allTags);
    }

    /// <summary>
    /// 判断是否被标签或自定义 Label 规则隐藏。
    /// </summary>
    public static bool IsHiddenByTagsOrLabels(
        GUIMod mod,
        IReadOnlyCollection<ModSearch>? activeSearches,
        GameInstance instance,
        IRegistryQuerier registry,
        ModuleLabelList? allLabels,
        ModuleTagList? allTags)
    {
        var labelHidden = allLabels?.LabelsFor(instance.Name)
            .Where(l => !IsLabelInSearches(l, activeSearches) && l.Hide)
            .Any(l => l.ContainsModule(instance.Game, mod.Identifier))
            ?? false;

        if (labelHidden)
        {
            return true;
        }

        var reg = registry as CKAN.Registry;
        var tagHidden = (allTags != null && reg?.Tags != null)
            && reg.Tags.Values
                .Where(t => !IsTagInSearches(t, activeSearches) && allTags.HiddenTags.Contains(t.Name))
                .Any(t => t.ModuleIdentifiers.Contains(mod.Identifier));

        return tagHidden;
    }

    private static bool IsTagInSearches(ModuleTag tag, IReadOnlyCollection<ModSearch>? activeSearches)
        => activeSearches != null && activeSearches.Any(s => s.TagNames.Contains(tag.Name));

    private static bool IsLabelInSearches(ModuleLabel label, IReadOnlyCollection<ModSearch>? activeSearches)
        => activeSearches != null && activeSearches.Any(s => s.LabelNames.Contains(label.Name));

    /// <summary>
    /// 按一组搜索条件统计匹配的模组数量。
    /// </summary>
    public static int CountModsBySearches(IEnumerable<GUIMod> modules, IReadOnlyCollection<ModSearch>? searches)
    {
        if (searches == null || searches.Count == 0)
        {
            return modules.Count();
        }

        return modules.Count(mod => searches.Any(s => s?.Matches(mod) ?? true));
    }

    /// <summary>
    /// 按单项过滤器统计匹配的模组数量。
    /// </summary>
    public static int CountModsByFilter(
        IEnumerable<GUIMod> modules,
        GameInstance instance,
        ModuleLabelList allLabels,
        GUIModFilter filter,
        ModuleTag? tag = null,
        ModuleLabel? label = null)
    {
        var search = new ModSearch(allLabels, instance, filter, tag, label);
        return CountModsBySearches(modules, new[] { search });
    }

    /// <summary>
    /// 将 GUIModFilter 转换为 SavedSearch。
    /// </summary>
    public static SavedSearch FilterToSavedSearch(
        GameInstance instance,
        GUIModFilter filter,
        ModuleLabelList allLabels,
        ModuleTag? tag = null,
        ModuleLabel? label = null)
        => new()
        {
            Name = FilterName(filter, tag, label),
            Values = new List<string>
            {
                new ModSearch(allLabels, instance, filter, tag, label).Combined ?? string.Empty,
            },
        };

    public static string FilterName(
        GUIModFilter filter,
        ModuleTag? tag = null,
        ModuleLabel? label = null)
        => filter switch
        {
            GUIModFilter.Compatible => "Compatible",
            GUIModFilter.Incompatible => "Incompatible",
            GUIModFilter.Installed => "Installed",
            GUIModFilter.NotInstalled => "Not Installed",
            GUIModFilter.InstalledUpdateAvailable => "Upgradeable",
            GUIModFilter.Replaceable => "Replaceable",
            GUIModFilter.Cached => "Cached",
            GUIModFilter.Uncached => "Uncached",
            GUIModFilter.NewInRepository => "New in Repository",
            GUIModFilter.All => "All",
            GUIModFilter.CustomLabel => $"Label: {label?.Name ?? "CUSTOM"}",
            GUIModFilter.Tag => tag == null ? "Untagged" : $"Tag: {tag.Name}",
            _ => string.Empty,
        };

    #endregion

    #region Upgrades & Updates Detection

    /// <summary>
    /// 纯函数：重新计算已安装模组的更新可用状态。
    /// 返回一个字典：模组标识符 -> 是否有可用更新，以及整体是否有任何可更新项。
    /// </summary>
    public static (bool HasAnyUpdate, IReadOnlyDictionary<string, bool> UpdatesByIdentifier) DetectUpdates(
        GameInstance instance,
        IRegistryQuerier registry,
        ModuleLabelList allLabels)
    {
        var result = new Dictionary<string, bool>(StringComparer.Ordinal);
        var dlls = registry.InstalledDlls.ToHashSet(StringComparer.Ordinal);
        var hasUpgradeable = false;

        var held = allLabels.HeldIdentifiers(instance).ToHashSet();
        var ignoreMissing = allLabels.IgnoreMissingIdentifiers(instance).ToHashSet();

        foreach (var (upgradeable, module) in registry.InstalledModulesByUpgradeability(instance, held, ignoreMissing))
        {
            hasUpgradeable = hasUpgradeable || upgradeable;
            dlls.Remove(module.identifier);
            result[module.identifier] = upgradeable;
        }

        // AD (Auto-detected) mods don't have CkanModules in InstalledModulesByUpgradeability
        foreach (var ident in dlls)
        {
            result[ident] = false;
        }

        return (hasUpgradeable, result);
    }

    #endregion

    #region ChangeSet Computation

    /// <summary>
    /// 根据用户勾选意图（升级、替换、选择的模块）纯函数生成初步变更集。
    /// </summary>
    public static HashSet<ModChange> ComputeUserChangeSet(
        IEnumerable<GUIMod> modules,
        IRegistryQuerier registry,
        GameInstance instance,
        IConfiguration config,
        Func<GUIMod, (bool UpgradeChecked, bool ReplaceChecked)> userSelections,
        ModuleLabelList allLabels)
    {
        var instFilters = config.GetGlobalInstallFilters(instance.Game)
            .Concat(instance.InstallFilters)
            .ToHashSet();

        var modChanges = new HashSet<ModChange>();

        foreach (var gmod in modules)
        {
            var (upgradeChecked, replaceChecked) = userSelections(gmod);
            if (!upgradeChecked && !replaceChecked && gmod.SelectedMod == null)
            {
                continue;
            }

            var metadataChanged = registry.MetadataChanged(gmod.Identifier, out bool installedFilesChanged);
            var changes = gmod.GetModChanges(
                upgradeChecked,
                replaceChecked,
                metadataChanged,
                installedFilesChanged,
                instance,
                instFilters,
                config);

            foreach (var ch in changes)
            {
                modChanges.Add(ch);
            }
        }

        // 处理互斥依赖可能对版本升级产生的影响
        var upgrades = modChanges.OfType<ModUpgrade>()
            .Where(upg => upg.Mod != upg.targetMod)
            .ToArray();

        if (upgrades.Length > 0)
        {
            var held = registry.Installed(false)
                .Select(kvp => kvp.Key)
                .Except(upgrades.Select(ch => ch.Mod.identifier))
                .ToHashSet();

            var ignoreMissing = allLabels.IgnoreMissingIdentifiers(instance).ToHashSet();

            var upgradeable = registry.UpgradeableModules(instance, held, ignoreMissing)
                .ToDictionary(m => m.identifier, m => m, StringComparer.Ordinal);

            foreach (var change in upgrades)
            {
                change.targetMod = upgradeable.TryGetValue(change.Mod.identifier, out CkanModule? allowedMod)
                    ? allowedMod
                    : change.Mod;

                if (change.Mod == change.targetMod)
                {
                    // 升级由于依赖冲突被作废
                    modChanges.Remove(change);
                }
            }
        }

        return modChanges;
    }

    /// <summary>
    /// 完整变更集推导（老 ModList.ComputeFullChangeSetFromUserChangeSet 纯函数化）。
    /// </summary>
    public static (ICollection<ModChange> FullChangeSet, Dictionary<CkanModule, string> ConflictMap, List<string> Conflicts)
        ComputeFullChangeSet(
            IRegistryQuerier registry,
            HashSet<ModChange> changeSet,
            IConfiguration coreConfig,
            IGame game,
            StabilityToleranceConfig stabilityTolerance,
            GameVersionCriteria version)
    {
        var workingChangeSet = new HashSet<ModChange>(changeSet);

        // 处理替换
        workingChangeSet.UnionWith(workingChangeSet.Where(ch => ch.ChangeType == GUIModChangeType.Replace)
            .Select(ch => registry.GetReplacement(ch.Mod, stabilityTolerance, version))
            .OfType<ModuleReplacement>()
            .GroupBy(repl => repl.ReplaceWith)
            .Select(grp => new ModChange(grp.Key, GUIModChangeType.Install,
                grp.Select(repl => new SelectionReason.Replacement(repl.ToReplace)),
                coreConfig)));

        var toInstall = new List<CkanModule>();
        var toRemove = new HashSet<CkanModule>();
        var extraInstalls = new HashSet<CkanModule>();

        foreach (var change in workingChangeSet)
        {
            switch (change.ChangeType)
            {
                case GUIModChangeType.None:
                    break;
                case GUIModChangeType.Update:
                    var mod = (change as ModUpgrade)?.targetMod ?? change.Mod;
                    toInstall.Add(mod);
                    extraInstalls.Add(mod);
                    break;
                case GUIModChangeType.Install:
                    toInstall.Add(change.Mod);
                    break;
                case GUIModChangeType.Remove:
                    toRemove.Add(change.Mod);
                    break;
                case GUIModChangeType.Replace:
                    if (registry.GetReplacement(change.Mod, stabilityTolerance, version) is ModuleReplacement repl)
                    {
                        toRemove.Add(repl.ToReplace);
                        extraInstalls.Add(repl.ReplaceWith);
                    }
                    break;
            }
        }

        // 检查反向依赖
        if (!registry.InstalledModules.Select(im => im.Module).All(toRemove.Contains))
        {
            var installedModules = registry.InstalledModules.ToDictionary(
                imod => imod.Module.identifier,
                imod => imod.Module,
                StringComparer.Ordinal);

            var toRemoveIdents = toRemove.Select(m => m.identifier)
                .Except(toInstall.Select(m => m.identifier))
                .ToList();

            foreach (var dependent in registry.FindReverseDependencies(toRemoveIdents, toInstall))
            {
                if (!workingChangeSet.Any(ch => ch.ChangeType == GUIModChangeType.Replace && ch.Mod.identifier == dependent)
                    && installedModules.TryGetValue(dependent, out CkanModule? depMod)
                    && (registry.GetModuleByVersion(depMod.identifier, depMod.version)
                        ?? registry.InstalledModule(dependent)?.Module) is CkanModule modByVer)
                {
                    workingChangeSet.Add(new ModChange(modByVer, GUIModChangeType.Remove,
                        new SelectionReason.DependencyRemoved(),
                        coreConfig));
                    toRemove.Add(modByVer);
                }
            }

            // 检查已无需自动保留的依赖
            if (!registry.InstalledModules.Select(im => im.Module).All(toRemove.Contains))
            {
                var installedAfter = InstalledAfterChanges(registry, workingChangeSet).ToArray();
                foreach (var im in registry.FindRemovableAutoInstalled(
                    installedAfter,
                    Array.Empty<CkanModule>(), game, stabilityTolerance, version))
                {
                    workingChangeSet.Add(new ModChange(im.Module, GUIModChangeType.Remove,
                        new SelectionReason.NoLongerUsed(),
                        coreConfig));
                    toRemove.Add(im.Module);
                }
            }
        }

        var resolver = new RelationshipResolver(
            toInstall,
            toRemove,
            ConflictOptions(stabilityTolerance),
            registry,
            game,
            version);

        var finalSet = workingChangeSet.Where(ch => !(ch.ChangeType is GUIModChangeType.Install
                                                && !ch.Reasons.Any(r => r is SelectionReason.Replacement)))
            .OrderBy(ch => ch.Mod.identifier)
            .Union(resolver.ModList()
                .Except(extraInstalls)
                .Select(m => new ModChange(m, GUIModChangeType.Install, resolver.ReasonsFor(m), coreConfig)))
            .ToArray();

        return (finalSet, resolver.ConflictList, resolver.ConflictDescriptions.ToList());
    }

    private static IEnumerable<InstalledModule> InstalledAfterChanges(
        IRegistryQuerier registry,
        IReadOnlyCollection<ModChange> changeSet)
    {
        var removingIdents = changeSet
            .Where(ch => ch.ChangeType != GUIModChangeType.Install)
            .Select(ch => ch.Mod.identifier)
            .ToHashSet();

        return registry.InstalledModules
            .Where(im => !removingIdents.Contains(im.identifier))
            .Concat(changeSet
                .Where(ch => ch.ChangeType is not GUIModChangeType.Remove and not GUIModChangeType.Replace)
                .Select(ch => new InstalledModule(
                    null,
                    (ch as ModUpgrade)?.targetMod ?? ch.Mod,
                    Enumerable.Empty<string>(),
                    false)));
    }

    private static RelationshipResolverOptions ConflictOptions(StabilityToleranceConfig stabilityTolerance)
        => new(stabilityTolerance)
        {
            without_toomanyprovides_kraken = true,
            proceed_with_inconsistencies = true,
            without_enforce_consistency = true,
            with_recommends = false,
            with_suggests = false,
            with_all_suggests = false
        };

    #endregion

    #region Projection to ModRowData

    /// <summary>
    /// 将 GUIMod 映射为纯 System.* 的不可变行投影 DTO（ModRowData）。
    /// </summary>

    /// <summary>
    /// 根据给定的搜索条件过滤 GUIMod 集合（纯函数，MOD-04 / 04 篇 §4、§5）。
    /// </summary>
    public static List<GUIMod> FilterMods(IEnumerable<GUIMod> mods, IReadOnlyCollection<ModSearch>? searches)
    {
        if (searches == null || searches.Count == 0)
        {
            return mods.ToList();
        }

        return mods.Where(m => searches.Any(s => s.Matches(m))).ToList();
    }

    /// <summary>
    /// 对 GUIMod 集合按指定列排序（纯函数，MOD-04 / 04 篇 §4、§5）。
    /// </summary>
    public static IEnumerable<GUIMod> SortMods(IEnumerable<GUIMod> mods, string columnName, bool ascending)
    {
        Func<GUIMod, object?> keySelector = columnName.ToLowerInvariant() switch
        {
            "name" => m => m.Name,
            "identifier" => m => m.Identifier,
            "author" or "authors" => m => string.Join(", ", m.Authors),
            "installed" or "isinstalled" => m => m.IsInstalled,
            "latestversion" or "version" => m => m.LatestVersion,
            "installedversion" => m => m.InstalledVersion,
            "downloadsize" => m => m.DownloadSize,
            "installsize" => m => m.InstallSize,
            "downloadcount" => m => m.DownloadCount ?? 0,
            _ => m => m.Name
        };

        return ascending
            ? mods.OrderBy(keySelector)
            : mods.OrderByDescending(keySelector);
    }

    /// <summary>
    /// 将 GUIMod 集合批量投影为 ModRowData 集合（纯函数，04 篇 §6）。
    /// </summary>
    public static List<ModRowData> ProjectToRows(IEnumerable<GUIMod> mods)
        => mods.Select(m => ToModRowData(m)).ToList();


    public static ModRowData ToModRowData(
        GUIMod mod,
        bool isSelected = false,
        bool isImplicit = false)
        => new(
            Identifier: mod.Identifier,
            Name: mod.Name,
            Author: mod.Authors.Count > 0 ? string.Join(", ", mod.Authors) : null,
            InstalledVersion: mod.InstalledVersion,
            LatestVersion: mod.LatestVersion,
            DownloadCount: mod.DownloadCount ?? 0,
            Tags: mod.Module.Tags != null ? mod.Module.Tags.ToList() : Array.Empty<string>(),
            SizeBytes: mod.Module.download_size > 0 ? mod.Module.download_size : null,
            IsInstalled: mod.IsInstalled,
            HasUpdate: mod.HasUpdate,
            IsCompatible: !mod.IsIncompatible,
            IsAutoInstalled: mod.IsAutoInstalled,
            IsSelected: isSelected,
            IsImplicit: isImplicit)
        {
            Facts = ModSearchFactsFactory.From(
                mod.Module,
                isCached: mod.IsCached,
                isNew: mod.IsNew,
                isReplaceable: mod.IsInstalled && mod.HasReplacement),
        };

    #endregion
}
