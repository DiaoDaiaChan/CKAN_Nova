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
    /// <summary>条目 n 字段的键前缀：n = ckan:&lt;identifier&gt;:&lt;field&gt;。</summary>
    private const string KeyPrefix = "ckan:";

    private readonly DictPack _overrides;
    private readonly DictPack _packs;

    // 过期判定用的二级索引：(identifier, field) 键 → 条目，见 BuildKeyIndex 的注释说明为什么必须有它。
    private readonly Dictionary<string, DictEntry> _overrideKeyIndex;
    private readonly Dictionary<string, DictEntry> _packKeyIndex;

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

        // 包在 provider 生命周期内不可变，索引一次建好；重载语言包时服务会重建 provider，
        // 因此这里不需要失效逻辑。建索引在重载线程（Task.Run）里发生，不占 UI 线程。
        _overrideKeyIndex = BuildKeyIndex(_overrides);
        _packKeyIndex = BuildKeyIndex(_packs);
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

        // 第一轮：同 hash 精确命中，译文与当前原文一一对应，Stale 必为 false。
        if (TryFindInLayer(_overrides, hash, sourceText, normalized, context, out var entry))
        {
            return ToModText(entry!, hash);
        }

        if (TryFindInLayer(_packs, hash, sourceText, normalized, context, out entry))
        {
            return ToModText(entry!, hash);
        }

        // 第二轮：同 hash 找不到，再按 (identifier, field) 键找"这条曾经有过译文、但原文改过"的条目。
        // 只有这一轮才可能报 Stale=true：DictPack 只在 entry.Hash == hash 的桶里找，
        // 而 hash 由当前文本算出，所以纯靠 DictPack 永远看不到 h 不一致的条目（17 篇已知缺口 1）。
        string key = BuildKey(identifier, field);
        if (TryFindStaleInLayer(_overrideKeyIndex, key, hash, out entry))
        {
            return ToModText(entry!, hash, forceStale: true);
        }

        if (TryFindStaleInLayer(_packKeyIndex, key, hash, out entry))
        {
            return ToModText(entry!, hash, forceStale: true);
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

    /// <summary>
    /// 按 (identifier, field) 键找过期条目。命中说明"这个键曾经有译文，但 h 与当前文本不一致"。
    /// </summary>
    /// <param name="keyIndex">二级键索引。</param>
    /// <param name="key">(identifier, field) 键；空键表示不做过期判定。</param>
    /// <param name="currentHash">当前文本的哈希。</param>
    /// <param name="entry">命中的过期条目。</param>
    /// <returns>是否命中。</returns>
    private bool TryFindStaleInLayer(
        Dictionary<string, DictEntry> keyIndex,
        string key,
        ulong currentHash,
        out DictEntry? entry)
    {
        entry = null;
        if (key.Length == 0 || !keyIndex.TryGetValue(key, out var candidate))
        {
            return false;
        }

        // h 相同的条目在精确查找那一轮就该命中；走到这里说明只是撞了键、文本已经不同。
        if (candidate.Hash == currentHash)
        {
            return false;
        }

        // 策略过滤要与精确查找保持一致：ExistingOnly 下宁可回退英文，也不拿机翻旧译文顶替，
        // 且必须继续往下一层找（覆盖层的机翻不能挡住语言包里的人工条目）。
        if (_policy == ModTextPolicy.ExistingOnly && IsMachine(candidate))
        {
            return false;
        }

        entry = candidate;
        return true;
    }

    /// <summary>
    /// 建 (identifier, field) → 条目 的二级索引，键取条目的 n 字段（ckan:&lt;identifier&gt;:&lt;field&gt;）。
    /// 为什么只认这个形状：上游管线生成的包 n 是归一化原文，n 相同的条目可能属于不同 field，
    /// 拿它做键会把 A 字段的译文贴到 B 字段上——宁可漏报过期，也不能错报（错报比不报更伤信任）。
    /// </summary>
    /// <param name="pack">层包。</param>
    /// <returns>键索引（不区分层，层优先级由调用顺序保证）。</returns>
    private static Dictionary<string, DictEntry> BuildKeyIndex(DictPack pack)
    {
        var index = new Dictionary<string, DictEntry>(StringComparer.Ordinal);

        foreach (var entry in pack.AllEntries)
        {
            string? key = entry.Normalized;
            if (string.IsNullOrEmpty(key) || !key.StartsWith(KeyPrefix, StringComparison.Ordinal))
            {
                continue;
            }

            // 与精确查找一致：保留原文（IsKeep）与空译文条目本来就不算译文，不进索引。
            if (entry.IsKeep || string.IsNullOrEmpty(entry.Translation))
            {
                continue;
            }

            // 同一个键有多条历史译文时：人工译文压过机翻，同类则后载入的胜
            //（文件按追加顺序载入，后写的是作者最近的意图）。
            if (index.TryGetValue(key, out var existing) && IsHuman(existing) && !IsHuman(entry))
            {
                continue;
            }

            index[key] = entry;
        }

        return index;
    }

    /// <summary>按 (identifier, field) 拼 n 字段键；identifier 或 field 为空则返回空键（不做过期判定）。</summary>
    private static string BuildKey(string identifier, string field) =>
        string.IsNullOrEmpty(identifier) || string.IsNullOrEmpty(field)
            ? string.Empty
            : KeyPrefix + identifier + ":" + field;

    private ModText ToModText(DictEntry entry, ulong currentHash, bool forceStale = false)
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
        // forceStale 来自键命中分支：那条路径上文本一定已经不同，即使条目没记 h 也必须报过期，
        // 否则会把旧译文当成当前译文交给界面。
        bool stale = forceStale || (entry.Hash != 0 && entry.Hash != currentHash);

        return new ModText(entry.Translation, source, stale);
    }

    private static bool IsHuman(DictEntry entry) =>
        string.Equals(entry.Src, "human", StringComparison.OrdinalIgnoreCase);

    private static bool IsMachine(DictEntry entry) =>
        string.Equals(entry.Src, "machine", StringComparison.OrdinalIgnoreCase);
}
