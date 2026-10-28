namespace CkanUI.Services.Abstractions.Logs;

/// <summary>
/// 规则表（14 篇 §3.3）：外置 JSON 可热更新，内置默认集保证「找不到文件也能跑」。
/// 规则表是快照测试的对象——50 条真实片段必须 100% 命中，所以规则集本身要是纯数据、可序列化、可比对。
/// </summary>
public sealed record LogRuleSet
{
    /// <summary>空规则集（全部落到 Other）。</summary>
    public static LogRuleSet Empty { get; } = new(Array.Empty<LogRule>(), null);

    /// <summary>规则列表。</summary>
    public IReadOnlyList<LogRule> Rules { get; }

    /// <summary>加载来源（文件路径或 &quot;builtin&quot;），用于 UI/诊断包显示规则来自哪里。</summary>
    public string? Origin { get; init; }

    /// <summary>规则条数。</summary>
    public int Count => Rules.Count;

    /// <summary>构造规则集。</summary>
    public LogRuleSet(IReadOnlyList<LogRule> rules, string? origin = null)
    {
        ArgumentNullException.ThrowIfNull(rules);
        Rules = rules;
        Origin = origin;
    }
}
