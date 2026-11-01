using System.Collections.Concurrent;
using System.Globalization;

namespace CkanUI.Localization;

/// <summary>
/// 字符串目录 + 运行时切语言 + 缺译回退（16 篇 §4.2，UI-13"切语言无需改代码"）。
/// 实现要点：
/// ①三层解析：界面语言包（含回退链，zh-CN→zh→en-US）→ <see cref="IStringSource"/> 补充层
///   （元数据包 / Core 运行时资源）→ key 本身；回退到 key 时记 Warn、按 key 计数、按层计数；
/// ②切语言时把"已应用回退链"的扁平字典整体建好再原子替换，<see cref="Get"/> 无锁读取，
///   因此切换与并发读取安全（UI 线程不会读到半成品）；
/// ③JSON 损坏、缺文件、未随包语言、补充层抛异常一律降级不抛，最坏结果显示 key。
/// 补充层结果按 (语言, 键) 缓存，命中与未命中都缓存，避免每次取文案都重探一层。
/// </summary>
public sealed class Localizer : ILocalizer, ILocalizationDiagnostics
{
    private readonly Dictionary<string, LanguagePack> _packs;
    private readonly ILocalizationLog _log;
    private readonly string _fallbackLocale;
    private readonly bool _applyThreadCulture;
    private readonly IReadOnlyList<IStringSource> _sources;
    private readonly bool _pseudo;
    private readonly object _switchGate = new();
    private readonly ConcurrentDictionary<string, int> _missing = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, byte> _warned = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, int> _layerHits = new(StringComparer.Ordinal);

    // 补充层结果缓存：值型 key 是 (语言, 键)，null 表示"这一层也没命中"，防止反复探测。
    private readonly ConcurrentDictionary<(string Locale, string Key), string?> _sourceCache = new();
    private readonly IReadOnlyList<LocaleInfo> _localeInfos;

    // 解析快照：整体替换，读侧无锁。volatile 保证 SetCulture 之后所有线程立刻看到新字典。
    private volatile ResolvedSnapshot _snapshot;

    /// <summary>空目录构造（保持旧用法：字典 + 语言标识，缺 key 直接回退 key）。</summary>
    /// <param name="strings">键值表；为空表示没有任何译文。</param>
    /// <param name="locale">语言标识；为空时取当前 UI Culture。</param>
    public Localizer(Dictionary<string, string>? strings = null, string? locale = null)
        : this(
            [
                new LanguagePack(
                    string.IsNullOrWhiteSpace(locale) ? CurrentUiLocale() : locale,
                    locale ?? CurrentUiLocale(),
                    locale ?? CurrentUiLocale(),
                    new Dictionary<string, string>(strings ?? new Dictionary<string, string>(), StringComparer.Ordinal)),
            ],
            null)
    {
    }

    /// <summary>多语言包构造（运行时通常由 <see cref="LocalizerFactory"/> 装配）。</summary>
    /// <param name="packs">语言包集合。</param>
    /// <param name="options">装配选项；为空表示用默认（回退 en-US、不额外设线程 culture 之外的行为）。</param>
    public Localizer(IEnumerable<LanguagePack> packs, LocalizationOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(packs);

        _log = options?.ResolveLog() ?? NullLocalizationLog.Instance;
        _applyThreadCulture = options?.ApplyThreadCulture ?? true;
        _fallbackLocale = NormalizeLocale(options?.FallbackLocale ?? "en-US");
        _pseudo = options?.PseudoLocalize ?? false;
        _sources = options?.Sources is { Count: > 0 } configured
            ? configured.Where(s => s is not null).ToArray()
            : Array.Empty<IStringSource>();

        _packs = new Dictionary<string, LanguagePack>(StringComparer.OrdinalIgnoreCase);
        foreach (var pack in packs)
        {
            _packs[pack.Locale] = pack;
        }

        var initial = ResolveInitialLocale(options?.DefaultLocale);
        _snapshot = BuildSnapshot(initial);
        _localeInfos = BuildLocaleInfos();

        if (_applyThreadCulture)
        {
            ApplyThreadCulture(_snapshot.Culture);
        }
    }

    /// <inheritdoc />
    public string CurrentLocale => _snapshot.Locale;

    /// <inheritdoc />
    public CultureInfo CurrentCulture => _snapshot.Culture;

    /// <inheritdoc />
    public event EventHandler<CultureInfo>? LanguageChanged;

    /// <inheritdoc />
    public IReadOnlyList<LocaleInfo> AvailableLocales => _localeInfos;

    /// <inheritdoc />
    public IReadOnlyDictionary<string, int> MissingKeys => _missing;

    /// <inheritdoc />
    public IReadOnlyDictionary<string, int> LayerHits => _layerHits;

    /// <inheritdoc />
    public bool IsPseudoLocalized => _pseudo;

    /// <summary>当前语言下由回退语言（en-US）顶上的键数量，供缺译清单估算真实翻译进度。</summary>
    public int FallbackKeyCount => _snapshot.FallbackKeyCount;

    /// <summary>
    /// 清空缺键计数、告警去重表、层命中计数与来源结果缓存，返回被释放的"未命中/告警"条目数。
    /// </summary>
    /// <returns>缺键条目数 + 告警去重条目数（历史语义：层命中是正常事件，不计入"被释放"）。</returns>
    /// <remarks>
    /// 返回值刻意只统计缺键与告警：调用方（设置页"重新统计"）关心的是"有多少问题被清掉"，
    /// 把层命中算进去会让"一切正常"时也返回非零，失去诊断意义。层命中表仍一并清空，
    /// 需要读数的一方在调用前自行取 <see cref="LayerHits"/> 快照。
    /// </remarks>
    public int ClearDiagnostics()
    {
        var released = _missing.Count + _warned.Count;
        _missing.Clear();
        _warned.Clear();
        _layerHits.Clear();
        _sourceCache.Clear();
        return released;
    }

    /// <inheritdoc />
    public string Get(string key)
    {
        if (key is null)
        {
            throw new ArgumentNullException(nameof(key));
        }

        if (key.Length == 0)
        {
            return string.Empty;
        }

        if (_snapshot.Strings.TryGetValue(key, out var value))
        {
            BumpHits("pack");
            return value;
        }

        // 第二层：元数据包 / Core 资源。放在语言包全链之后、回退键之前（任务口径的三层顺序）。
        if (TryResolveFromSources(key, out var extra))
        {
            return extra;
        }

        ReportMissing(key);
        return key;
    }

    /// <inheritdoc />
    public string Format(string key, params object?[] args)
    {
        var template = Get(key);
        if (args is null || args.Length == 0)
        {
            return template;
        }

        try
        {
            // 显示侧按系统区域格式化（16 篇 §8.1：存盘一律 Invariant，显示跟随 CurrentCulture）。
            return string.Format(CultureInfo.CurrentCulture, template, args);
        }
        catch (FormatException)
        {
            if (_warned.TryAdd("format:" + key, 0))
            {
                _log.Warn($"[i18n] 占位符与参数不匹配：key={key}，模板={template}，参数 {args.Length} 个；已返回原文。");
            }

            return template;
        }
    }

    /// <inheritdoc />
    public bool TryGet(string key, out string value)
    {
        if (string.IsNullOrEmpty(key))
        {
            value = string.Empty;
            return false;
        }

        if (_snapshot.Strings.TryGetValue(key, out var found))
        {
            BumpHits("pack");
            value = found;
            return true;
        }

        if (TryResolveFromSources(key, out var extra))
        {
            value = extra;
            return true;
        }

        value = string.Empty;
        return false;
    }

    /// <inheritdoc />
    public void SetCulture(string locale)
    {
        if (string.IsNullOrWhiteSpace(locale))
        {
            throw new ArgumentException("语言标识不能为空。", nameof(locale));
        }

        var normalized = NormalizeLocale(locale);
        ResolvedSnapshot next;
        bool changed;
        lock (_switchGate)
        {
            changed = !string.Equals(_snapshot.Locale, normalized, StringComparison.OrdinalIgnoreCase);
            if (!changed)
            {
                return;
            }

            next = BuildSnapshot(normalized);
            _snapshot = next;
            if (_applyThreadCulture)
            {
                ApplyThreadCulture(next.Culture);
            }
        }

        if (FindPackLocale(normalized) is null)
        {
            // 未随包的语言（例如 Core 的 config.json 里存了 es-ES）：不抛，只提示并走回退链。
            WarnOnce("locale:" + normalized, $"[i18n] 未随包的语言 {normalized}，仅使用回退链 {_fallbackLocale}。");
        }

        LanguageChanged?.Invoke(this, next.Culture);
    }

    /// <inheritdoc />
    public IReadOnlyDictionary<string, string> Snapshot() => _snapshot.Strings;

    // ── 内部：解析链 ────────────────────────────────────────────────────────────

    /// <summary>按"回退语言优先、当前语言覆盖"的顺序拼出一张扁平字典。</summary>
    private ResolvedSnapshot BuildSnapshot(string locale)
    {
        var chain = BuildChain(locale);
        var resolved = new Dictionary<string, string>(StringComparer.Ordinal);

        // 反向写入：先放最低优先级的兜底，再被高优先级覆盖，省掉逐 key 查找。
        for (var i = chain.Count - 1; i >= 0; i--)
        {
            if (!_packs.TryGetValue(chain[i], out var pack))
            {
                continue;
            }

            foreach (var pair in pack.Strings)
            {
                resolved[pair.Key] = pair.Value;
            }
        }

        // 回退键计数：当前语言链(zh-CN→zh)覆盖到的键数从总数里扣掉，剩下的就是 en-US 顶上的部分。
        // 用集合而不是逐键记 origin，只为省一份 per-key 字典（快照会被整表塞进资源字典）。
        var currentChain = new List<string>();
        AppendCultureChain(currentChain, locale);
        var currentKeys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var name in currentChain)
        {
            if (_packs.TryGetValue(name, out var pack))
            {
                currentKeys.UnionWith(pack.Strings.Keys);
            }
        }

        if (_pseudo)
        {
            // 伪本地化只改显示：快照是显示层，所以在这里一次性变形，Get 侧不必重复判断。
            foreach (var key in resolved.Keys.ToArray())
            {
                resolved[key] = PseudoLocalization.Transform(resolved[key]);
            }
        }

        return new ResolvedSnapshot(locale, ToCulture(locale), resolved, Math.Max(0, resolved.Count - currentKeys.Count));
    }

    /// <summary>当前语言 → 父语言链 → 回退语言 → 回退语言的父语言链（不跨语言，不做机器翻译）。</summary>
    private List<string> BuildChain(string locale)
    {
        var chain = new List<string>();
        AppendCultureChain(chain, locale);
        AppendCultureChain(chain, _fallbackLocale);
        return chain;
    }

    private static void AppendCultureChain(List<string> chain, string locale)
    {
        if (string.IsNullOrWhiteSpace(locale))
        {
            return;
        }

        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var name = locale;
        while (!string.IsNullOrEmpty(name) && visited.Add(name))
        {
            if (!chain.Contains(name, StringComparer.OrdinalIgnoreCase))
            {
                chain.Add(name);
            }

            var culture = TryGetCulture(name);
            name = culture?.Parent.Name ?? string.Empty;
        }
    }

    private string ResolveInitialLocale(string? requested)
    {
        if (_packs.Count == 0)
        {
            return NormalizeLocale(string.IsNullOrWhiteSpace(requested) ? CurrentUiLocale() : requested);
        }

        string?[] candidates = [requested, CurrentUiLocale(), "zh-CN", _fallbackLocale];
        foreach (var candidate in candidates)
        {
            var found = FindPackLocale(candidate);
            if (found is not null)
            {
                return found;
            }
        }

        return _packs.Keys.OrderBy(k => k, StringComparer.Ordinal).First();
    }

    /// <summary>在已装载的语言包里找候选语言（含父语言逐级回退）。</summary>
    private string? FindPackLocale(string? candidate)
    {
        if (string.IsNullOrWhiteSpace(candidate))
        {
            return null;
        }

        var chain = new List<string>();
        AppendCultureChain(chain, NormalizeLocale(candidate));
        return chain.FirstOrDefault(_packs.ContainsKey);
    }

    private IReadOnlyList<LocaleInfo> BuildLocaleInfos()
    {
        var fallbackCount = _packs.TryGetValue(_fallbackLocale, out var fallback)
            ? fallback.Strings.Count
            : 0;
        var fallbackKeys = fallback is null
            ? new HashSet<string>(StringComparer.Ordinal)
            : new HashSet<string>(fallback.Strings.Keys, StringComparer.Ordinal);

        var infos = new List<LocaleInfo>(_packs.Count);
        foreach (var pack in _packs.Values.OrderBy(p => p.Locale, StringComparer.Ordinal))
        {
            var matched = pack.Strings.Keys.Count(fallbackKeys.Contains);
            var coverage = fallbackCount == 0
                ? (pack.Strings.Count > 0 ? 1d : 0d)
                : (double)matched / fallbackCount;
            infos.Add(new LocaleInfo(pack.Locale, pack.NativeName, pack.EnglishName, coverage, coverage >= 1d));
        }

        return infos;
    }

    /// <summary>
    /// 探测补充层（L2 元数据包 / L3 Core 资源）：命中即返回，未命中缓存 null 以免反复探测。
    /// 任何一层抛异常都只记一次 Warn 并跳过该层——模组译文包损坏不能让界面打不开。
    /// </summary>
    private bool TryResolveFromSources(string key, out string value)
    {
        value = string.Empty;
        if (_sources.Count == 0)
        {
            return false;
        }

        var cacheKey = (_snapshot.Locale, key);
        if (_sourceCache.TryGetValue(cacheKey, out var cached))
        {
            if (cached is null)
            {
                return false;
            }

            value = cached;
            return true;
        }

        foreach (var source in _sources)
        {
            string found;
            try
            {
                if (!source.TryGet(cacheKey.Locale, key, out found) || string.IsNullOrEmpty(found))
                {
                    continue;
                }
            }
            catch (Exception ex)
            {
                WarnOnce("source:" + source.Name + ":" + ex.GetType().Name,
                    $"[i18n] 字符串来源「{source.Name}」探测失败：{ex.GetType().Name}: {ex.Message}；已跳过该层。");
                continue;
            }

            _sourceCache[cacheKey] = found;
            BumpHits("source:" + source.Name);
            value = found;
            return true;
        }

        _sourceCache[cacheKey] = null;
        return false;
    }

    private void BumpHits(string layer) => _layerHits.AddOrUpdate(layer, 1, static (_, count) => count + 1);

    private void ReportMissing(string key)
    {
        BumpHits("missing");
        _missing.AddOrUpdate(key, 1, static (_, count) => count + 1);
        if (_warned.TryAdd("key:" + key, 0))
        {
            _log.Warn($"[i18n] 缺少字符串 key「{key}」（当前语言 {_snapshot.Locale}），已回退为 key 本身。");
        }
    }

    private void WarnOnce(string token, string message)
    {
        if (_warned.TryAdd(token, 0))
        {
            _log.Warn(message);
        }
    }

    /// <summary>
    /// 把当前实例的界面语言重新应用到**调用线程**。
    /// </summary>
    /// <remarks>
    /// 构造期的 <see cref="ApplyThreadCulture"/> 只作用于构造线程。实例被后台线程预热、真正使用发生在 UI 线程时，
    /// UI 线程的 CurrentUICulture 还没设过，由调用方在 UI 线程补一次，保证线程 culture 与「构造在 UI 线程」时一致（16 篇 §2.3）。
    /// </remarks>
    public void ApplyCultureToCurrentThread()
    {
        if (_applyThreadCulture)
        {
            ApplyThreadCulture(_snapshot.Culture);
        }
    }

    private static void ApplyThreadCulture(CultureInfo culture)
    {
        // 只设 UI Culture：数字与日期继续跟随系统区域（16 篇 §2.3 / Q3 决策）。
        CultureInfo.DefaultThreadCurrentUICulture = culture;
        CultureInfo.CurrentUICulture = culture;
    }

    private static CultureInfo ToCulture(string locale) => TryGetCulture(locale) ?? CultureInfo.InvariantCulture;

    private static CultureInfo? TryGetCulture(string locale)
    {
        if (string.IsNullOrWhiteSpace(locale))
        {
            return null;
        }

        try
        {
            return CultureInfo.GetCultureInfo(locale);
        }
        catch (CultureNotFoundException)
        {
            // 非法语言标识不是错误：本地化层自行降级，绝不因为配置里的脏值崩界面。
            return null;
        }
    }

    // 只做空白规整，不采用 CultureInfo.Name：ICU 下 zh-CN 可能被规范成 zh-Hans-CN，
    // 拿它当字典键就查不到随包的 zh-CN.json。语言包文件名与 Core 配置都按原样保存标识。
    private static string NormalizeLocale(string locale) => locale.Trim();

    private static string CurrentUiLocale() => CultureInfo.CurrentUICulture.Name;

    /// <summary>解析结果快照（不可变；整体替换以实现无锁读）。</summary>
    /// <param name="Locale">快照对应的语言。</param>
    /// <param name="Culture">对应的 CultureInfo。</param>
    /// <param name="Strings">已应用回退链的扁平字典（只含语言包层，补充层按需探测）。</param>
    /// <param name="FallbackKeyCount">由回退语言顶上的键数量（诊断用）。</param>
    private sealed record ResolvedSnapshot(string Locale, CultureInfo Culture, IReadOnlyDictionary<string, string> Strings, int FallbackKeyCount);
}
