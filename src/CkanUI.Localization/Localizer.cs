using System.Collections.Concurrent;
using System.Globalization;

namespace CkanUI.Localization;

/// <summary>
/// 字符串目录 + 运行时切语言 + 缺译回退（16 篇 §4.2，UI-13"切语言无需改代码"）。
/// 实现要点：
/// ①回退链 = 当前语言 → 父语言（zh-CN→zh）→ 回退语言（en-US，含其父语言）→ key 本身，回退到 key 时记 Warn；
/// ②切语言时把"已应用回退链"的扁平字典整体建好再原子替换，<see cref="Get"/> 无锁读取，
///   因此切换与并发读取安全（UI 线程不会读到半成品）；
/// ③JSON 损坏、缺文件、未随包语言一律降级不抛，最坏结果显示 key。
/// </summary>
public sealed class Localizer : ILocalizer
{
    private readonly Dictionary<string, LanguagePack> _packs;
    private readonly ILocalizationLog _log;
    private readonly string _fallbackLocale;
    private readonly bool _applyThreadCulture;
    private readonly object _switchGate = new();
    private readonly ConcurrentDictionary<string, int> _missing = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, byte> _warned = new(StringComparer.Ordinal);
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
    public int ClearDiagnostics()
    {
        var released = _missing.Count + _warned.Count;
        _missing.Clear();
        _warned.Clear();
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
            return value;
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
            value = found;
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

        return new ResolvedSnapshot(locale, ToCulture(locale), resolved);
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

    private void ReportMissing(string key)
    {
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
    private sealed record ResolvedSnapshot(string Locale, CultureInfo Culture, IReadOnlyDictionary<string, string> Strings);
}
