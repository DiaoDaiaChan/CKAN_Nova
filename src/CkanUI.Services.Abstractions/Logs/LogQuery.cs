namespace CkanUI.Services.Abstractions.Logs;

/// <summary>
/// 过滤条件（UI 三段过滤条 + 搜索框的产物，也是查询服务的输入）。
/// 全部条件为「与」关系：级别集合、族集合、源集合、标签等值、自由文本。
/// 集合为 null 表示不限制（而不是空集合——空集合会让用户「取消全选」时看到 0 行，语义上应该等价于不过滤）。
/// </summary>
public sealed record LogQuery
{
    /// <summary>不过滤任何条件（默认分段用）。</summary>
    public static LogQuery All { get; } = new();

    /// <summary>自由文本（子串匹配，大小写不敏感）。</summary>
    public string? Text { get; init; }

    /// <summary>允许的级别；null = 不限。</summary>
    public IReadOnlyCollection<LogLevel>? Levels { get; init; }

    /// <summary>允许的族；null = 不限。</summary>
    public IReadOnlyCollection<LogFamily>? Families { get; init; }

    /// <summary>允许的来源类别；null = 不限。</summary>
    public IReadOnlyCollection<LogSourceKind>? Kinds { get; init; }

    /// <summary>结构化标签等值过滤（<c>mod:xxx</c> 落到 mod 键）。</summary>
    public IReadOnlyDictionary<string, string>? Tags { get; init; }

    /// <summary>跳过多少条（分页/时间轴定位用）。</summary>
    public int Skip { get; init; }

    /// <summary>最多返回多少条；0 或负数按 500 处理，防止误传把整缓冲塞进 UI。</summary>
    public int Take { get; init; } = 500;

    /// <summary>排序方向：true = 新到旧（审查看最新），false = 旧到新（跟随末尾）。</summary>
    public bool NewestFirst { get; init; } = true;

    /// <summary>归一后的自由文本（小写、去空白）。</summary>
    public string NormalizedText => LogSearchKeys.NormalizeKeyword(Text);

    /// <summary>过滤是否为「空」——空查询可以走整份快照的快速路径。</summary>
    public bool IsUnfiltered =>
        string.IsNullOrWhiteSpace(Text)
        && Levels is null
        && Families is null
        && Kinds is null
        && (Tags is null || Tags.Count == 0);

    /// <summary>安全取值：Take 非法时退回 500。</summary>
    public int SafeTake => Take <= 0 ? 500 : Take;

    /// <summary>安全取值：Skip 非法时退回 0。</summary>
    public int SafeSkip => Skip < 0 ? 0 : Skip;
}
