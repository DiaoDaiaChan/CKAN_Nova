using CkanUI.ModText;
using CommunityToolkit.Mvvm.ComponentModel;

namespace CkanUI.Plugin.ModText;

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
        Key = item.Key ?? string.Empty;

        // 稳定键 k 优先：运行时包里的 n 是归一化原文，只有 k 还带着 ckan:<identifier>:<field>；
        // 没有 k 的老包再退回 n，最后退回 ctx。
        ParseStableKey(item.Key);
        if (string.IsNullOrEmpty(Identifier))
        {
            ParseStableKey(Normalized);
        }

        if (string.IsNullOrEmpty(Identifier) && !string.IsNullOrEmpty(Context))
        {
            Identifier = Context;
        }
    }

    private void ParseStableKey(string? key)
    {
        if (string.IsNullOrEmpty(key) || !key.StartsWith("ckan:", System.StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var parts = key.Split(':');
        if (parts.Length >= 3)
        {
            Identifier = parts[1];
            Field = parts[2];
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

    /// <summary>行级稳定键原值（<c>ckan:&lt;identifier&gt;:&lt;field&gt;</c>）；没有就是空串。</summary>
    public string Key { get; }

    public string Identifier { get; private set; } = string.Empty;
    public string Field { get; private set; } = string.Empty;

    /// <summary>
    /// 表格首列展示用键：元数据条目给 identifier，游戏内语言包没有 identifier，退回模组名，
    /// 再退上下文——否则 UI18n 全量包（modui / 部件描述）在列表里首列全空，看着像没数据。
    /// </summary>
    public string DisplayKey =>
        !string.IsNullOrEmpty(Identifier) ? Identifier
        : !string.IsNullOrEmpty(Mod) ? Mod
        : Context;

    /// <summary>首列的悬停提示：标识 + 字段（单行表格放不下字段，靠提示补）。</summary>
    public string KeyTooltip => string.IsNullOrEmpty(Field) ? DisplayKey : DisplayKey + " · " + Field;

    public bool IsHumanOverride => string.Equals(Src, "human", System.StringComparison.OrdinalIgnoreCase);

    public string SourceBadge => IsKeep ? "保留/不可译" : (IsHumanOverride ? "人工覆盖" : (!string.IsNullOrEmpty(Src) ? Src : "词典"));

    /// <summary>
    /// 只有"非默认来源"才挂徽标：默认词典条目每行都挂一个"词典"是纯噪声，
    /// 还把窄窗口下的原文/译文列再压掉 60px。
    /// </summary>
    public bool ShowSourceBadge => IsKeep || IsHumanOverride || !string.IsNullOrEmpty(Src);
}
