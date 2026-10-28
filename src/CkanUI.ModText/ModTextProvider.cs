using System;
using System.Collections.Generic;
using UI18n.Core.Dict;
using UI18n.Core.Hashing;
using UI18n.Core.Text;

namespace CkanUI.ModText;

/// <summary>
/// 纯内存 IModTextProvider 实现：人工覆盖层 → 语言包层 → 策略/黑名单过滤。
/// 基于 UI18n.Core 的 DictPack 与 TextNormalizer，0 IO、不依赖外部配置与网络。
/// </summary>
/// <remarks>
/// 为什么拆两层 DictPack 而不是把 overrides 混进同一个包：overrides 是人类维护的少量条目，
/// 语言包是几千条机翻，混在一起后"这条到底是人工的还是机翻的"只能靠 src 猜测；
/// 分层后优先级由结构保证（先查 overrides），且重载语言包时可以只重建第二层。
/// </remarks>
public sealed class ModTextProvider : IModTextProvider
{
    private readonly DictPack _overrides;
    private readonly DictPack _packs;

    private ModTextPolicy _policy;
    private ModTextBlocklist _blocklist;

    /// <summary>单层包构造（兼容旧调用）：该包即"语言包层"，无覆盖层。</summary>
    /// <param name="pack">语言包。</param>
    public ModTextProvider(DictPack pack)
        : this(new DictPack(), pack, ModTextPolicy.ExistingOnly, ModTextBlocklist.Empty)
    {
    }

    /// <summary>分层包构造。</summary>
    /// <param name="overrides">人工覆盖层（先查）。</param>
    /// <param name="packs">语言包层（后查）。</param>
    /// <param name="policy">使用策略。</param>
    /// <param name="blocklist">黑名单；null 表示空名单。</param>
    public ModTextProvider(
        DictPack overrides,
        DictPack packs,
        ModTextPolicy policy = ModTextPolicy.ExistingOnly,
        ModTextBlocklist? blocklist = null)
    {
        _overrides = overrides ?? new DictPack();
        _packs = packs ?? new DictPack();
        _policy = policy;
        _blocklist = blocklist ?? ModTextBlocklist.Empty;
    }

    /// <summary>使用策略（运行期可改；查找路径每次读取，改完立即生效）。</summary>
    public ModTextPolicy Policy
    {
        get => _policy;
        set => _policy = value;
    }

    /// <summary>黑名单（运行期可换）。</summary>
    public ModTextBlocklist Blocklist
    {
        get => _blocklist;
        set => _blocklist = value ?? ModTextBlocklist.Empty;
    }

    /// <summary>人工覆盖层条目数。</summary>
    public int OverrideEntries => _overrides.Entries;

    /// <summary>语言包层条目数。</summary>
    public int PackEntries => _packs.Entries;

    /// <summary>由 JSONL 行构造单层 provider（测试与工具链用）。</summary>
    /// <param name="lines">NDJSON 行。</param>
    public static ModTextProvider FromJsonLines(IEnumerable<string> lines) =>
        new(new DictPack(), DictPack.LoadJsonLines(lines));

    /// <summary>由覆盖层与语言包层的 JSONL 行构造分层 provider。</summary>
    /// <param name="overrideLines">人工覆盖层行。</param>
    /// <param name="packLines">语言包层行。</param>
    /// <param name="policy">使用策略。</param>
    /// <param name="blocklist">黑名单。</param>
    public static ModTextProvider FromJsonLines(
        IEnumerable<string> overrideLines,
        IEnumerable<string> packLines,
        ModTextPolicy policy = ModTextPolicy.ExistingOnly,
        ModTextBlocklist? blocklist = null) =>
        new(DictPack.LoadJsonLines(overrideLines), DictPack.LoadJsonLines(packLines), policy, blocklist);

    /// <inheritdoc />
    public ModText? Find(string identifier, string field, string sourceText)
    {
        if (string.IsNullOrEmpty(sourceText) || _policy == ModTextPolicy.Off)
        {
            return null;
        }

        // 黑名单按 identifier 命中即整条禁用：连人工覆盖也不该绕开它（那是作者意愿，不是数据问题）。
        if (_blocklist.IsBlocked(identifier))
        {
            return null;
        }

        string normalized = TextNormalizer.Normalize(sourceText);
        ulong hash = Fnv1a.Hash(normalized);

        // context = identifier（如 "MechJeb2"），见 17 篇 §2 的 n 字段：ckan:<identifier>:<field>。
        // field 只体现在 n 字段与人工覆盖的命名上，不参与哈希——哈希只由文本决定，
        // 这样同一段文本换字段仍能命中，避免同一句话在 abstract/description 里各存一份。
        string? context = string.IsNullOrEmpty(identifier) ? null : identifier;

        if (TryFindInLayer(_overrides, hash, sourceText, normalized, context, out var entry))
        {
            return ToModText(entry!, hash);
        }

        if (TryFindInLayer(_packs, hash, sourceText, normalized, context, out entry))
        {
            return ToModText(entry!, hash);
        }

        return null;
    }

    private bool TryFindInLayer(
        DictPack pack,
        ulong hash,
        string sourceText,
        string normalized,
        string? context,
        out DictEntry? entry)
    {
        // 先按原始文本查、再按归一化文本查：包里的 s 字段可能留着原始空白，
        // 只按归一化查会漏掉那些条目（T-MTI-02 的归一化行为）。
        if (!pack.TryGetEntry(hash, sourceText, context, out entry) &&
            !pack.TryGetEntry(hash, normalized, context, out entry))
        {
            return false;
        }

        if (entry is null || entry.IsKeep || string.IsNullOrEmpty(entry.Translation))
        {
            return false;
        }

        // 策略过滤：显式标注为机翻的条目在"仅已有译文"档下跳过，继续往下一层找，
        // 不能直接返回 null，否则人工层里的机翻条目会挡住语言包里的人工条目。
        if (_policy == ModTextPolicy.ExistingOnly && IsMachine(entry))
        {
            return false;
        }

        return true;
    }

    private ModText ToModText(DictEntry entry, ulong currentHash)
    {
        ModTextSource source = ModTextSource.None;
        if (IsHuman(entry))
        {
            source = ModTextSource.Human;
        }
        else if (IsMachine(entry))
        {
            source = ModTextSource.Machine;
        }

        // 过期判定：条目登记的 h 与当前文本算出的 h 不一致 = 原文改过但译文没跟上。
        bool stale = entry.Hash != 0 && entry.Hash != currentHash;

        return new ModText(entry.Translation, source, stale);
    }

    private static bool IsHuman(DictEntry entry) =>
        string.Equals(entry.Src, "human", StringComparison.OrdinalIgnoreCase);

    private static bool IsMachine(DictEntry entry) =>
        string.Equals(entry.Src, "machine", StringComparison.OrdinalIgnoreCase);
}
