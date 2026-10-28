using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using Autofac;
using CKAN;
using CKAN.Configuration;
using CKAN.Versioning;
using CkanUI.Services.ChangeSets;

namespace CkanUI.Services.Models;

public static class GUIModResources
{
    public static string GUIModUnknown => "Unknown";
    public static string GUIModNSlashA => "N/A";
}

public sealed class GUIMod : INotifyPropertyChanged
{
    private CkanModule Mod { get; set; }
    public CkanModule? LatestCompatibleMod { get; private set; }
    public CkanModule? LatestAvailableMod { get; private set; }
    private readonly InstalledModule? directInstalledMod;
    private readonly IRegistryQuerier? registry;
    public InstalledModule? InstalledMod => directInstalledMod ?? registry?.InstalledModule(Identifier);

    /// <summary>
    /// The module of the checkbox that is checked in the MainAllModVersions list if any,
    /// null otherwise.
    /// Used for generating this mod's part of the change set.
    /// </summary>
    public CkanModule? SelectedMod
    {
        get => selectedMod;
        set
        {
            if (!(selectedMod?.Equals(value) ?? value?.Equals(selectedMod) ?? true))
            {
                selectedMod = value;
                OnPropertyChanged();
            }
        }
    }
    private CkanModule? selectedMod = null;

    /// <summary>
    /// Notify listeners when certain properties change.
    /// </summary>
    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    public string Name { get; private set; }
    public bool IsInstalled { get; private set; }
    public bool IsAutoInstalled => InstalledMod?.AutoInstalled ?? false;
    public bool HasUpdate { get; set; }
    public bool HasReplacement { get; private set; }
    public bool IsIncompatible { get; private set; }
    public bool IsAutodetected { get; private set; }
    public List<string> Authors => Mod.author ?? new List<string>();
    public string? InstalledVersion { get; private set; }
    public DateTime? InstallDate { get; private set; }
    public string LatestVersion { get; private set; }
    public string DownloadSize { get; private set; }
    public string InstallSize { get; private set; }
    public int? DownloadCount { get; private set; }

    private bool isCached;
    public bool IsCached
    {
        get => isCached;
        private set
        {
            if (value != isCached)
            {
                isCached = value;
                OnPropertyChanged();
            }
        }
    }

    // These indicate the maximum game version that the maximum available
    // version of this mod can handle. The "Long" version also indicates
    // to the user if a mod upgrade would be required. (#1270)
    public GameVersion? GameCompatibilityVersion { get; private set; }
    public string? GameCompatibility
        => GameCompatibilityVersion == null ? GUIModResources.GUIModUnknown
           : GameCompatibilityVersion.IsAny ? GameVersion.AnyString
           : GameCompatibilityVersion.ToString();

    public string Abstract { get; private set; }
    public string Description { get; private set; }
    public string Identifier { get; private set; }
    public bool IsNew { get; set; }
    public string Abbrevation { get; private set; }

    public string SearchableName { get; private set; }
    public string SearchableIdentifier { get; private set; }
    public string SearchableAbstract { get; private set; }
    public string SearchableDescription { get; private set; }
    public string? SearchableTranslatedAbstract { get; private set; }
    public string? SearchableTranslatedDescription { get; private set; }
    public List<string> SearchableAuthors { get; private set; }

    /// <summary>
    /// 注入翻译后摘要/描述，供搜索并集匹配使用（17 篇 §6.2）。
    /// </summary>
    public void SetTranslations(string? translatedAbstract, string? translatedDescription)
    {
        SearchableTranslatedAbstract = string.IsNullOrEmpty(translatedAbstract) ? null : CkanModule.nonAlphaNumsAnyLanguage.Replace(translatedAbstract, "");
        SearchableTranslatedDescription = string.IsNullOrEmpty(translatedDescription) ? null : CkanModule.nonAlphaNumsAnyLanguage.Replace(translatedDescription, "");
    }

    /// <summary>
    /// Return whether this mod is installable.
    /// Used for determining whether to show a checkbox in the leftmost column.
    /// </summary>
    public bool IsInstallable()
        // Compatible mods are installable, but so are mods that are already installed
        => !IsIncompatible || IsInstalled;

    public string Version
        => InstalledVersion ?? LatestVersion;

    /// <summary>
    /// Initialize a GUIMod based on an InstalledModule
    /// </summary>

    /// <summary>
    /// 仅供测试与无头模型使用的轻量构造函数（纯内存创建，不依赖外部实例与磁盘缓存）。
    /// </summary>
    public GUIMod(CkanModule mod,
                  InstalledModule? installed = null,
                  CkanModule? latestCompatibleMod = null,
                  CkanModule? latestAvailableMod = null,
                  bool incompatible = false,
                  bool isNew = false,
                  bool isCached = false,
                  bool hasUpdate = false,
                  bool hasReplacement = false)
    {
        Mod = mod;
        Identifier = mod.identifier;
        Name = mod.name.Trim();
        Abstract = mod.@abstract.Trim();
        Description = mod.description?.Trim() ?? "";
        directInstalledMod = installed;
        registry = null;
        IsInstalled = installed != null;
        InstalledVersion = installed?.Module.version.ToString();
        InstallDate = installed?.InstallTime;
        LatestCompatibleMod = latestCompatibleMod ?? mod;
        LatestAvailableMod = latestAvailableMod ?? mod;
        LatestVersion = (latestCompatibleMod ?? mod).version.ToString();
        IsIncompatible = incompatible;
        IsNew = isNew;
        IsCached = isCached;
        HasUpdate = hasUpdate;
        HasReplacement = hasReplacement;

        SearchableIdentifier = CkanModule.nonAlphaNums.Replace(Identifier, "");
        SearchableName = mod.SearchableName;
        SearchableAbstract = mod.SearchableAbstract;
        SearchableDescription = mod.SearchableDescription;
        SearchableAuthors = mod.SearchableAuthors;
        Abbrevation = new string(Name.Split(' ')
                                       .Select(s => s.Length > 0 && s[0] is char first ? (char?)first : null)
                                       .OfType<char>()
                                       .ToArray());
        DownloadSize = mod.download_size == 0 ? "" : CkanModule.FmtSize(mod.download_size);
        InstallSize = mod.install_size == 0 ? "" : CkanModule.FmtSize(mod.install_size);
    }


    public GUIMod(InstalledModule instMod,
                  RepositoryDataManager repoDataMgr,
                  IRegistryQuerier registry,
                  StabilityToleranceConfig stabilityTolerance,
                  GameInstance instance,
                  NetModuleCache cache,
                  bool? incompatible,
                  bool hideEpochs,
                  bool hideV)
        : this(instMod.Module, repoDataMgr, registry,
               stabilityTolerance, instance, cache,
               incompatible, hideEpochs, hideV)
    {
        IsInstalled = true;
        this.registry = registry;
        selectedMod = registry.GetModuleByVersion(instMod.identifier, instMod.Module.version)
                      ?? instMod.Module;
        InstallDate = instMod.InstallTime;

        InstalledVersion = instMod.Module.version.ToString(hideEpochs, hideV);
        if (LatestVersion == null || LatestVersion.Equals("-"))
        {
            LatestVersion = InstalledVersion;
        }
        // For mods not known to the registry LatestCompatibleMod is null, however the installed module might be compatible
        IsIncompatible = incompatible ?? (LatestCompatibleMod == null
                                            && !instMod.Module.IsCompatible(instance.VersionCriteria()));
    }

    /// <summary>
    /// Initialize a GUIMod based on a CkanModule
    /// </summary>
    public GUIMod(CkanModule mod,
                  RepositoryDataManager repoDataMgr,
                  IRegistryQuerier registry,
                  StabilityToleranceConfig stabilityTolerance,
                  GameInstance instance,
                  NetModuleCache cache,
                  bool? incompatible,
                  bool hideEpochs,
                  bool hideV)
    {
        this.registry = registry;
        Identifier = mod.identifier;
        IsAutodetected = registry.IsAutodetected(Identifier);
        DownloadCount = repoDataMgr.GetDownloadCount(registry.Repositories.Values, Identifier);
        if (IsAutodetected)
        {
            IsInstalled = true;
        }

        ModuleVersion? latest_version = null;
        if (incompatible != true)
        {
            try
            {
                LatestCompatibleMod = registry.LatestAvailable(Identifier,
                                                               stabilityTolerance,
                                                               instance.VersionCriteria(),
                                                               null,
                                                               registry.InstalledModules
                                                                       .Select(im => im.Module)
                                                                       .ToArray());
                latest_version = LatestCompatibleMod?.version;
            }
            catch (ModuleNotFoundKraken)
            {
                if (!incompatible.HasValue)
                {
                    incompatible = true;
                }
            }
        }

        IsIncompatible = incompatible ?? (LatestCompatibleMod == null);

        // Let's try to find the compatibility for this mod. If it's not in the registry at
        // all (because it's a DarkKAN mod) then this might fail.
        try
        {
            LatestAvailableMod = registry.LatestAvailable(Identifier, stabilityTolerance, null, null,
                                                          registry.InstalledModules
                                                                  .Select(im => im.Module)
                                                                  .ToArray());
        }
        catch
        { }

        // If there's known information for this mod in any form, calculate the highest compatible
        // game version.
        if (LatestAvailableMod != null)
        {
            GameCompatibilityVersion = registry.LatestCompatibleGameVersion(
                instance.Game.KnownVersions,
                Identifier);
        }

        if (latest_version != null)
        {
            LatestVersion = latest_version.ToString(hideEpochs, hideV);
        }
        else if (LatestAvailableMod != null)
        {
            LatestVersion = LatestAvailableMod.version.ToString(hideEpochs, hideV);
        }
        else
        {
            LatestVersion = "-";
        }

        SearchableIdentifier = CkanModule.nonAlphaNums.Replace(Identifier, "");

        Mod = mod;

        Name = mod.name.Trim();
        Abstract = mod.@abstract.Trim();
        Description = mod.description?.Trim() ?? "";
        Abbrevation = new string(Name.Split(' ')
                                       .Select(s => s.Length > 0 && s[0] is char first ? (char?)first : null)
                                       .OfType<char>()
                                       .ToArray());

        HasReplacement = registry.GetReplacement(mod, stabilityTolerance, instance.VersionCriteria()) != null;
        if (mod.IsMetapackage)
        {
            // Nothing to download or install
            DownloadSize = GUIModResources.GUIModNSlashA;
            InstallSize = GUIModResources.GUIModNSlashA;
        }
        else
        {
            DownloadSize = mod.download_size == 0 ? "" : CkanModule.FmtSize(mod.download_size);
            InstallSize = mod.install_size == 0 ? "" : CkanModule.FmtSize(mod.install_size);
        }

        // Get the Searchables.
        SearchableName = mod.SearchableName;
        SearchableAbstract = mod.SearchableAbstract;
        SearchableDescription = mod.SearchableDescription;
        SearchableAuthors = mod.SearchableAuthors;

        // If not set in GUIMod(identifier, ...) (because the mod is not known to the registry),
        // set based on the data we have from the CkanModule.
        if (GameCompatibilityVersion == null)
        {
            GameCompatibilityVersion = mod.LatestCompatibleGameVersion();
            if (GameCompatibilityVersion.IsAny)
            {
                GameCompatibilityVersion = mod.LatestCompatibleRealGameVersion(
                    instance.Game.KnownVersions);
            }
        }
        UpdateIsCached(cache);
    }

    /// <summary>
    /// Refresh the IsCached property based on current contents of the cache
    /// </summary>
    public void UpdateIsCached(NetModuleCache cache)
    {
        IsCached = Mod.download is { Count: > 0 }
                   && cache.IsMaybeCachedZip(Mod);
    }

    /// <summary>
    /// Get the CkanModule associated with this GUIMod.
    /// </summary>
    /// <returns>The CkanModule associated with this GUIMod or null if there is none</returns>
    public CkanModule Module => Mod;

    public IEnumerable<ModChange> GetModChanges(bool upgradeChecked,
                                                bool replaceChecked,
                                                bool metadataChanged,
                                                bool installMetadataChanged,
                                                GameInstance instance,
                                                HashSet<string> filters)
    {
        var config = ServiceLocator.Container.Resolve<IConfiguration>();
        return GetModChanges(upgradeChecked, replaceChecked, metadataChanged, installMetadataChanged, instance, filters, config);
    }

    public IEnumerable<ModChange> GetModChanges(bool upgradeChecked,
                                                bool replaceChecked,
                                                bool metadataChanged,
                                                bool installMetadataChanged,
                                                GameInstance instance,
                                                HashSet<string> filters,
                                                IConfiguration config)
    {
        var installed = InstalledMod?.Module;
        if (replaceChecked)
        {
            yield return new ModChange(Mod, GUIModChangeType.Replace, config);
        }
        else if (!(SelectedMod?.Equals(installed)
                   ?? installed?.Equals(SelectedMod)
                   // Both null
                   ?? true))
        {
            if (installed != null
                && SelectedMod != null
                && SelectedMod == LatestAvailableMod
                && SelectedMod.version > installed.version)
            {
                yield return new ModUpgrade(Mod, SelectedMod, config);
            }
            else
            {
                if (installed != null)
                {
                    yield return new ModChange(installed, GUIModChangeType.Remove, config);
                }
                if (SelectedMod != null)
                {
                    yield return new ModChange(SelectedMod, GUIModChangeType.Install, config);
                }
            }
        }
        else if (upgradeChecked && SelectedMod != null)
        {
            // Reinstall
            yield return new ModReinstall(Mod, false, metadataChanged,
                                          installMetadataChanged || (!InstalledMod?.AllFilesExist(instance, filters) ?? false),
                                          config);
        }
    }

    /// <summary>
    /// Set auto-install state without WinForms DataGridView dependency.
    /// </summary>
    public void SetAutoInstall(bool value)
    {
        if (InstalledMod != null)
        {
            InstalledMod.AutoInstalled = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsAutoInstalled)));
        }
    }

    private bool Equals(GUIMod? other) => Equals(Identifier, other?.Identifier);

    public override bool Equals(object? obj)
        => obj is not null
           && (ReferenceEquals(this, obj)
               || (obj is GUIMod gmod && Equals(gmod)));

    public override int GetHashCode() => Identifier?.GetHashCode() ?? 0;

    public override string ToString() => Mod.ToString();
}
