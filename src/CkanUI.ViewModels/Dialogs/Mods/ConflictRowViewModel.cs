using CkanUI.Services.Abstractions.Dtos;

namespace CkanUI.ViewModels.Dialogs.Mods;

/// <summary>冲突详情对话框里的一行（F-36）：文案直接来自 Core 已本地化的资源串。</summary>
public sealed class ConflictRowViewModel
{
    /// <summary>用 DTO 构造一行。</summary>
    public ConflictRowViewModel(ConflictEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        Entry = entry;
    }

    /// <summary>原始条目。</summary>
    public ConflictEntry Entry { get; }

    /// <summary>冲突理由（可直接展示）。</summary>
    public string Description => Entry.Description;

    /// <summary>模组标识。</summary>
    public string? ModuleIdentifier => Entry.ModuleIdentifier;

    /// <summary>模组显示名。</summary>
    public string? ModuleName => Entry.ModuleName;

    /// <summary>是否带模组信息。</summary>
    public bool HasModule =>
        !string.IsNullOrWhiteSpace(ModuleIdentifier) || !string.IsNullOrWhiteSpace(ModuleName);

    /// <summary>模组信息展示文案。</summary>
    public string ModuleText
    {
        get
        {
            var hasId = !string.IsNullOrWhiteSpace(ModuleIdentifier);
            var hasName = !string.IsNullOrWhiteSpace(ModuleName);

            if (hasId && hasName)
            {
                return ModuleName + "（" + ModuleIdentifier + "）";
            }

            if (hasName)
            {
                return ModuleName!;
            }

            return hasId ? ModuleIdentifier! : string.Empty;
        }
    }
}
