using System.Text.Json;
using System.Text.Json.Serialization;

namespace CkanUI.Sfs;

/// <summary>
/// 值的"可编辑类型"分类。只用来给编辑器挑控件与做输入校验，**不改变值的字面量**（L4：不投影）。
/// 未知键一律 <see cref="Unknown"/>，编辑器退回纯文本控件，不做任何猜测性转换。
/// </summary>
public enum SfsFieldKind
{
    Unknown = 0,
    Text = 1,
    Integer = 2,
    Decimal = 3,
    Boolean = 4,
    Vector = 5,
    Identifier = 6,
}

/// <summary>
/// 键的展示规则（13 篇 §8 编辑器规格）：标签、控件类型、排序权重、单位、分组。
/// 这是**可替换词表**而不是代码分支：内置词表 + JSON 词表都走同一套匹配逻辑，
/// 因此任何整合包塞进来的自定义键都不会被"硬编码漏掉"——它们只是没有标签，照样能编辑。
/// </summary>
public sealed record SfsKeyRule(
    string Key,
    string Label,
    SfsFieldKind Kind = SfsFieldKind.Text,
    int Priority = 100,
    string? Unit = null,
    string? Group = null);

/// <summary>节点（块）的展示规则。场景名用于 SCENARIO 的二次分类（KSP 的 31 个 SCENARIO 都叫 SCENARIO）。</summary>
public sealed record SfsSectionRule(
    string NodeName,
    string Title,
    int Rank = 500,
    string? ScenarioName = null);

/// <summary>
/// 词表：键规则 + 块规则。所有匹配都忽略大小写（KSP 的 ConfigNode 本身不区分大小写），
/// 未知键/未知块返回 null，由调用方走"通用展示"路径。
/// </summary>
public sealed class SfsVocabulary
{
    private readonly Dictionary<string, SfsKeyRule> _keys;
    private readonly Dictionary<string, SfsSectionRule> _sections;

    public SfsVocabulary(IEnumerable<SfsKeyRule>? keys = null, IEnumerable<SfsSectionRule>? sections = null)
    {
        Keys = (keys ?? []).ToArray();
        Sections = (sections ?? []).ToArray();

        _keys = new Dictionary<string, SfsKeyRule>(StringComparer.OrdinalIgnoreCase);
        foreach (var rule in Keys)
        {
            _keys.TryAdd(rule.Key, rule);
        }

        _sections = new Dictionary<string, SfsSectionRule>(StringComparer.OrdinalIgnoreCase);
        foreach (var rule in Sections)
        {
            _sections.TryAdd(SectionKey(rule.NodeName, rule.ScenarioName), rule);
        }
    }

    /// <summary>内置词表：覆盖原版 KSP 里"改存档必看"的键，其余键走通用展示。</summary>
    /// <remarks>
    /// 必须惰性创建：静态字段按文本顺序初始化，若在这里直接 new，<c>DefaultKeyRules</c>/<c>DefaultSectionRules</c>
    /// 还没赋值，词表会静默变空（所有键退化成未知、所有标签丢失）。
    /// </remarks>
    public static SfsVocabulary Default => DefaultLazy.Value;

    private static readonly Lazy<SfsVocabulary> DefaultLazy = new(static () => new(DefaultKeyRules, DefaultSectionRules));

    public IReadOnlyList<SfsKeyRule> Keys { get; }

    public IReadOnlyList<SfsSectionRule> Sections { get; }

    /// <summary>按键名取规则（忽略大小写）；没有规则返回 null。</summary>
    public SfsKeyRule? MatchKey(string? key)
    {
        if (string.IsNullOrEmpty(key))
        {
            return null;
        }

        return _keys.TryGetValue(key, out var rule) ? rule : null;
    }

    /// <summary>按块名（+SCENARIO 的 name）取规则；没有返回 null。</summary>
    public SfsSectionRule? MatchSection(string? nodeName, string? scenarioName = null)
    {
        if (string.IsNullOrEmpty(nodeName))
        {
            return null;
        }

        if (!string.IsNullOrEmpty(scenarioName) && _sections.TryGetValue(SectionKey(nodeName, scenarioName), out var scenarioRule))
        {
            return scenarioRule;
        }

        return _sections.TryGetValue(SectionKey(nodeName, null), out var rule) ? rule : null;
    }

    /// <summary>判定一个值的控件类型：先看键规则，再看字面量形状；都不像就是 Text。</summary>
    public SfsFieldKind Classify(string? key, string? value)
    {
        var rule = MatchKey(key);
        if (rule is not null)
        {
            return rule.Kind;
        }

        return InferKind(value);
    }

    /// <summary>只按字面量推断类型（不解释、不转换）。</summary>
    public static SfsFieldKind InferKind(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return SfsFieldKind.Text;
        }

        var text = value.AsSpan().Trim();
        if (text.Length == 0)
        {
            return SfsFieldKind.Text;
        }

        if (text.Length is 4 or 5
            && (text.Equals("true", StringComparison.OrdinalIgnoreCase) || text.Equals("false", StringComparison.OrdinalIgnoreCase)))
        {
            return SfsFieldKind.Boolean;
        }

        var commas = 0;
        var allNumeric = true;
        var hasDot = false;
        for (var index = 0; index < text.Length; index++)
        {
            var ch = text[index];
            if (ch == ',')
            {
                commas++;
                continue;
            }

            if (ch == '.')
            {
                hasDot = true;
                continue;
            }

            if (!char.IsAsciiDigit(ch) && ch != '-' && ch != '+' && ch != 'e' && ch != 'E')
            {
                allNumeric = false;
                break;
            }
        }

        if (!allNumeric)
        {
            return SfsFieldKind.Text;
        }

        if (commas > 0)
        {
            return SfsFieldKind.Vector;
        }

        return hasDot ? SfsFieldKind.Decimal : SfsFieldKind.Integer;
    }

    /// <summary>从 JSON 词表构建（字段名不区分大小写，未知字段忽略）。空串/空白返回内置词表。</summary>
    public static SfsVocabulary FromJson(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return Default;
        }

        var payload = JsonSerializer.Deserialize<VocabularyPayload>(json, JsonOptions);
        if (payload is null)
        {
            return Default;
        }

        var keys = payload.Keys.Select(static item => new SfsKeyRule(
            item.Key ?? string.Empty,
            string.IsNullOrWhiteSpace(item.Label) ? item.Key ?? string.Empty : item.Label!,
            Enum.TryParse<SfsFieldKind>(item.Kind, ignoreCase: true, out var kind) ? kind : SfsFieldKind.Text,
            item.Priority,
            item.Unit,
            item.Group)).Where(static rule => rule.Key.Length > 0).ToArray();

        var sections = payload.Sections.Select(static item => new SfsSectionRule(
            item.Node ?? string.Empty,
            string.IsNullOrWhiteSpace(item.Title) ? item.Node ?? string.Empty : item.Title!,
            item.Rank,
            item.Scenario)).Where(static rule => rule.NodeName.Length > 0).ToArray();

        return new SfsVocabulary(keys, sections);
    }

    /// <summary>内置键词表（顺序即默认优先级，配合 Priority 使用）。</summary>
    public static IReadOnlyList<SfsKeyRule> DefaultKeyRules { get; } =
    [
        new("funds", "资金", SfsFieldKind.Decimal, 0, "√", "生涯"),
        new("sci", "科技点", SfsFieldKind.Decimal, 1, null, "生涯"),
        new("rep", "声望", SfsFieldKind.Decimal, 2, null, "生涯"),
        new("UT", "游戏时间", SfsFieldKind.Decimal, 3, "s", "生涯"),
        new("Title", "存档标题", SfsFieldKind.Text, 4, null, "存档信息"),
        new("version", "存档版本", SfsFieldKind.Text, 5, null, "存档信息"),
        new("mode", "游戏模式", SfsFieldKind.Text, 6, null, "存档信息"),
        new("name", "名称", SfsFieldKind.Text, 8, null, "标识"),
        new("launchID", "发射编号", SfsFieldKind.Integer, 9, null, "标识"),
        new("persistentId", "持久 ID", SfsFieldKind.Identifier, 10, null, "标识"),
        new("persistentID", "持久 ID", SfsFieldKind.Identifier, 11, null, "标识"),
        new("seed", "随机种子", SfsFieldKind.Integer, 12, null, "存档信息"),
        new("description", "描述", SfsFieldKind.Text, 14, null, "存档信息"),
        new("flag", "旗帜", SfsFieldKind.Text, 16, null, "存档信息"),
        new("vesselName", "飞船名", SfsFieldKind.Text, 20, null, "飞船"),
        new("vesselType", "飞船类型", SfsFieldKind.Text, 21, null, "飞船"),
        new("sit", "坐标", SfsFieldKind.Vector, 22, null, "飞船"),
        new("lat", "纬度", SfsFieldKind.Decimal, 23, null, "飞船"),
        new("lon", "经度", SfsFieldKind.Decimal, 24, null, "飞船"),
        new("alt", "高度", SfsFieldKind.Decimal, 25, null, "飞船"),
        new("ref", "参考体", SfsFieldKind.Text, 26, null, "飞船"),
        new("part", "部件名", SfsFieldKind.Text, 30, null, "部件"),
        new("mid", "模块 ID", SfsFieldKind.Identifier, 31, null, "部件"),
        new("amount", "数量", SfsFieldKind.Decimal, 33, null, "资源"),
        new("maxAmount", "容量", SfsFieldKind.Decimal, 34, null, "资源"),
        new("flowState", "流向", SfsFieldKind.Boolean, 35, null, "资源"),
        new("crew", "乘员", SfsFieldKind.Text, 40, null, "乘员"),
        new("trait", "特性", SfsFieldKind.Text, 41, null, "乘员"),
        new("experience", "经验", SfsFieldKind.Decimal, 42, null, "乘员"),
        new("level", "等级", SfsFieldKind.Integer, 43, null, "乘员"),
    ];

    /// <summary>内置块词表。rank 越小越靠前；未列出的块排在最后（保持原文件顺序）。</summary>
    public static IReadOnlyList<SfsSectionRule> DefaultSectionRules { get; } =
    [
        new("GAME", "存档根", 0),
        new("FLIGHTSTATE", "飞行器", 10),
        new("SCENARIO", "模组数据", 20),
        new("ROSTER", "乘员", 30),
        new("MESSAGESYSTEM", "消息", 40),
        new("SCENARIO", "资金", 0, "Funding"),
        new("SCENARIO", "声望", 1, "Reputation"),
        new("SCENARIO", "科技", 2, "ResearchAndDevelopment"),
        new("SCENARIO", "进度", 3, "ProgressTracking"),
        new("SCENARIO", "成就", 4, "Achievements"),
        new("SCENARIO", "合同", 5, "ContractSystem"),
        new("SCENARIO", "合同历史", 6, "ContractsApp"),
        new("SCENARIO", "科技树", 7, "TechTree"),
        new("SCENARIO", "部件升级", 8, "PartUpgradeManager"),
    ];

    private static string SectionKey(string nodeName, string? scenarioName)
        => string.IsNullOrEmpty(scenarioName) ? nodeName : nodeName + "\u001F" + scenarioName;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    private sealed class VocabularyPayload
    {
        [JsonPropertyName("keys")]
        public List<KeyPayload> Keys { get; set; } = [];

        [JsonPropertyName("sections")]
        public List<SectionPayload> Sections { get; set; } = [];
    }

    private sealed class KeyPayload
    {
        [JsonPropertyName("key")]
        public string? Key { get; set; }

        [JsonPropertyName("label")]
        public string? Label { get; set; }

        [JsonPropertyName("kind")]
        public string? Kind { get; set; }

        [JsonPropertyName("priority")]
        public int Priority { get; set; } = 100;

        [JsonPropertyName("unit")]
        public string? Unit { get; set; }

        [JsonPropertyName("group")]
        public string? Group { get; set; }
    }

    private sealed class SectionPayload
    {
        [JsonPropertyName("node")]
        public string? Node { get; set; }

        [JsonPropertyName("title")]
        public string? Title { get; set; }

        [JsonPropertyName("rank")]
        public int Rank { get; set; } = 500;

        [JsonPropertyName("scenario")]
        public string? Scenario { get; set; }
    }
}
