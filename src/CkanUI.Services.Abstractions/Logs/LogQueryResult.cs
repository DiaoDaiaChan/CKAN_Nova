namespace CkanUI.Services.Abstractions.Logs;

/// <summary>
/// 查询结果：命中的窗口 + 总命中数。
/// 为什么要 TotalMatches：UI 要显示「1,204 条其中显示 500 条」，而且分页需要一个稳定的总数。
/// </summary>
public sealed record LogQueryResult
{
    /// <summary>空结果。</summary>
    public static LogQueryResult Empty { get; } = new(Array.Empty<LogEvent>(), 0);

    /// <summary>当前窗口的事件（已按 <see cref="LogQuery.NewestFirst"/> 排序）。</summary>
    public IReadOnlyList<LogEvent> Events { get; }

    /// <summary>符合条件的总条数（不受 Skip/Take 限制）。</summary>
    public int TotalMatches { get; }

    /// <summary>是否还有未显示的匹配（UI 用来提示「还有 N 条」）。</summary>
    public bool HasMore => TotalMatches > Events.Count;

    /// <summary>构造结果。</summary>
    public LogQueryResult(IReadOnlyList<LogEvent> events, int totalMatches)
    {
        ArgumentNullException.ThrowIfNull(events);
        Events = events;
        TotalMatches = totalMatches;
    }
}
