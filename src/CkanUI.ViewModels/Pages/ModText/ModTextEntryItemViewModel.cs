using CkanUI.ModText;
using CommunityToolkit.Mvvm.ComponentModel;

namespace CkanUI.ViewModels.Pages.ModText;

/// <summary>
/// 词条条目呈现视图模型。
/// </summary>
public sealed partial class ModTextEntryItemViewModel : ObservableObject
{
    public ModTextEntryItemViewModel(ModTextEntryItem item)
    {
        Hash = item.Hash;
        HashHex = item.Hash.ToString("x16");
        Normalized = item.Normalized ?? string.Empty;
        Source = item.Source;
        Translation = item.Translation;
        Context = item.Context ?? string.Empty;
        Mod = item.Mod ?? string.Empty;
        Src = item.Src ?? string.Empty;
        IsKeep = item.IsKeep;

        // 解析 key/n: ckan:<identifier>:<field>
        if (!string.IsNullOrEmpty(Normalized) && Normalized.StartsWith("ckan:", System.StringComparison.OrdinalIgnoreCase))
        {
            var parts = Normalized.Split(':');
            if (parts.Length >= 3)
            {
                Identifier = parts[1];
                Field = parts[2];
            }
        }

        if (string.IsNullOrEmpty(Identifier) && !string.IsNullOrEmpty(Context))
        {
            Identifier = Context;
        }
    }

    public ulong Hash { get; }
    public string HashHex { get; }
    public string Normalized { get; }
    public string Source { get; }
    public string Translation { get; }
    public string Context { get; }
    public string Mod { get; }
    public string Src { get; }
    public bool IsKeep { get; }

    public string Identifier { get; } = string.Empty;
    public string Field { get; } = string.Empty;

    public bool IsHumanOverride => string.Equals(Src, "human", System.StringComparison.OrdinalIgnoreCase);

    public string SourceBadge => IsKeep ? "保留/不可译" : (IsHumanOverride ? "人工覆盖" : (!string.IsNullOrEmpty(Src) ? Src : "词典"));
}
