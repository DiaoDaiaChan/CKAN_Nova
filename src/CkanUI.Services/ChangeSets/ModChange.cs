using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using CKAN;
using CKAN.Configuration;
using CKAN.Extensions;

namespace CkanUI.Services.ChangeSets;

/// <summary>
/// 变更类型资源（提供给 DisplayAttribute 描述）。
/// 在新 UI 里映射到标准资源或英语回退，避免引用 WinForms 的 Properties.Resources。
/// </summary>
public static class ModChangeResources
{
    public static string ChangeTypeNone => "None";
    public static string ChangeTypeInstall => "Install";
    public static string ChangeTypeRemove => "Remove";
    public static string ChangeTypeUpdate => "Update";
    public static string ChangeTypeReplace => "Replace";

    public static string MainChangesetUpdateSelected => "Update to version {0}.";
    public static string MainChangesetReinstallUser => "Re-install (user requested)";
    public static string MainChangesetReinstallMetadataChanged => "Re-install (metadata changed)";
    public static string MainChangesetReinstallMissing => "Re-install (missing folders or files)";
}

public enum GUIModChangeType
{
    [Display(Name = nameof(ModChangeResources.ChangeTypeNone),
             Description = nameof(ModChangeResources.ChangeTypeNone),
             ResourceType = typeof(ModChangeResources))]
    None = 0,

    [Display(Name = nameof(ModChangeResources.ChangeTypeInstall),
             Description = nameof(ModChangeResources.ChangeTypeInstall),
             ResourceType = typeof(ModChangeResources))]
    Install = 1,

    [Display(Name = nameof(ModChangeResources.ChangeTypeRemove),
             Description = nameof(ModChangeResources.ChangeTypeRemove),
             ResourceType = typeof(ModChangeResources))]
    Remove = 2,

    [Display(Name = nameof(ModChangeResources.ChangeTypeUpdate),
             Description = nameof(ModChangeResources.ChangeTypeUpdate),
             ResourceType = typeof(ModChangeResources))]
    Update = 3,

    [Display(Name = nameof(ModChangeResources.ChangeTypeReplace),
             Description = nameof(ModChangeResources.ChangeTypeReplace),
             ResourceType = typeof(ModChangeResources))]
    Replace = 4,
}

/// <summary>
/// Everything the GUI needs to know about a change, including
/// the mod itself, the change we're making, and the reason why.
/// </summary>
public class ModChange
{
    public CkanModule Mod { get; private set; }
    public GUIModChangeType ChangeType { get; private set; }
    public SelectionReason[] Reasons { get; private set; }

    /// <summary>
    /// true if the reason for this change is that an installed dependency is no longer needed,
    /// false otherwise
    /// </summary>
    public readonly bool IsAutoRemoval;

    /// <summary>
    /// true if this change is user requested and no other changes depend on it, false otherwise.
    /// </summary>
    public readonly bool IsUserRequested;

    /// <summary>
    /// true if this change can be removed from a changeset, false otherwise
    /// </summary>
    public bool IsRemovable => IsAutoRemoval || IsUserRequested;

    // If we don't have a Reason, the user probably wanted to install it
    public ModChange(CkanModule mod,
                     GUIModChangeType changeType,
                     IConfiguration config)
        : this(mod, changeType, new SelectionReason.UserRequested(), config)
    {
    }

    public ModChange(CkanModule mod,
                     GUIModChangeType changeType,
                     SelectionReason reason,
                     IConfiguration config)
        : this(mod, changeType, Enumerable.Repeat(reason, 1), config)
    {
    }

    public ModChange(CkanModule mod,
                     GUIModChangeType changeType,
                     IEnumerable<SelectionReason> reasons,
                     IConfiguration config)
    {
        Mod = mod;
        ChangeType = changeType;
        Reasons = reasons.ToArray();
        this.config = config;
        IsAutoRemoval = Reasons.All(r => r is SelectionReason.NoLongerUsed);
        IsUserRequested = Reasons.All(r => r is SelectionReason.UserRequested);
    }

    public override bool Equals(object? obj)
        => obj is not null
           && (ReferenceEquals(this, obj)
               || (obj is ModChange ch
                   && ch.Mod.Equals(Mod)
                   && ch.ChangeType.Equals(ChangeType)));

    private static readonly int maxEnumVal = Enum.GetValues(typeof(GUIModChangeType)).Cast<int>().Max();

    public override int GetHashCode()
        // Distinguish between installing and removing
        => Mod == null
            ? 0
            : (((maxEnumVal + 1) * Mod.GetHashCode()) + (int)ChangeType);

    public override string ToString()
        => $"{ChangeType.LocalizeDescription()} {Mod} ({Description})";

    public virtual string NameAndStatus(NetModuleCache cache)
        => cache.DescribeAvailability(config, Mod);

    private static string DescribeGroup(IEnumerable<SelectionReason> reasons)
        => reasons.First().DescribeWith(reasons.Skip(1));

    public virtual string Description
        => string.Join("; ",
            Reasons.GroupBy(r => r.GetType(), (t, reasons) =>
                DescribeGroup(
                    t.IsSubclassOf(typeof(SelectionReason.RelationshipReason))
                        ? reasons.OfType<SelectionReason.RelationshipReason>()
                                 .OrderBy(r => r.Parent.name)
                        : reasons)));

    protected IConfiguration config;
}

public class ModUpgrade : ModChange
{
    public ModUpgrade(CkanModule mod,
                      CkanModule targetMod,
                      IConfiguration config)
        : base(mod, GUIModChangeType.Update, config)
    {
        this.targetMod = targetMod;
    }

    public override string NameAndStatus(NetModuleCache cache)
        => cache.DescribeAvailability(config, targetMod);

    public override string Description
        => string.Format(ModChangeResources.MainChangesetUpdateSelected,
                         targetMod.version);

    /// <summary>
    /// The target version for upgrading
    /// </summary>
    public CkanModule targetMod;
}

public class ModReinstall : ModChange
{
    public ModReinstall(CkanModule mod,
                        bool userReinstall,
                        bool metadataChanged,
                        bool reinstallFiles,
                        IConfiguration config)
        : base(mod, GUIModChangeType.Update, config)
    {
        this.userReinstall = userReinstall;
        this.metadataChanged = metadataChanged;
        this.reinstallFiles = reinstallFiles;
    }

    private readonly bool userReinstall;
    private readonly bool metadataChanged;
    private readonly bool reinstallFiles;

    public override string Description
        => userReinstall ? ModChangeResources.MainChangesetReinstallUser
         : metadataChanged ? ModChangeResources.MainChangesetReinstallMetadataChanged
         : ModChangeResources.MainChangesetReinstallMissing;

    public bool SkipReinstallingFiles
        => !userReinstall && metadataChanged && !reinstallFiles;
}
