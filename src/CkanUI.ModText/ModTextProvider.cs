using System;
using System.Collections.Generic;
using UI18n.Core.Dict;
using UI18n.Core.Hashing;
using UI18n.Core.Text;

namespace CkanUI.ModText;

/// <summary>
/// 纯内存 IModTextProvider 实现。基于 UI18n.Core 的 DictPack 与 TextNormalizer。
/// 0 IO，不依赖外部配置与网络。
/// </summary>
public sealed class ModTextProvider : IModTextProvider
{
    private readonly DictPack _pack;

    public ModTextProvider(DictPack pack)
    {
        _pack = pack ?? new DictPack();
    }

    public static ModTextProvider FromJsonLines(IEnumerable<string> lines)
    {
        var pack = DictPack.LoadJsonLines(lines);
        return new ModTextProvider(pack);
    }

    public ModText? Find(string identifier, string field, string sourceText)
    {
        if (string.IsNullOrEmpty(sourceText))
        {
            return null;
        }

        string normalized = TextNormalizer.Normalize(sourceText);
        ulong hash = Fnv1a.Hash(normalized);

        // context = identifier (e.g. "MechJeb2")
        string? context = string.IsNullOrEmpty(identifier) ? null : identifier;

        DictEntry? entry;
        if (!_pack.TryGetEntry(hash, sourceText, context, out entry) &&
            !_pack.TryGetEntry(hash, normalized, context, out entry))
        {
            return null;
        }

        if (entry == null || entry.IsKeep || string.IsNullOrEmpty(entry.Translation))
        {
            return null;
        }

        ModTextSource src = ModTextSource.None;
        if (string.Equals(entry.Src, "human", StringComparison.OrdinalIgnoreCase))
        {
            src = ModTextSource.Human;
        }
        else if (string.Equals(entry.Src, "machine", StringComparison.OrdinalIgnoreCase))
        {
            src = ModTextSource.Machine;
        }

        // Stale detection: if normalized hash doesn't match entry hash or source mismatch
        bool stale = entry.Hash != 0 && entry.Hash != hash;

        return new ModText(entry.Translation, src, stale);
    }
}
