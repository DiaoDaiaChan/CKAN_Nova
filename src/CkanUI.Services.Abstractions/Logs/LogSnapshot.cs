namespace CkanUI.Services.Abstractions.Logs;

/// <summary>
/// 缓冲区的不可变快照：事件数组 + 与之一一对应的预计算搜索串。
/// 为什么要成对返回：过滤要在 10 万行上跑到 p99 ≤16.7ms，每行都做 ToLower/拼接会直接吃光预算；
/// 搜索串在入队时算一次（<see cref="LogSearchKeys"/>），查询时只做一次 IndexOf。
/// </summary>
public sealed record LogSnapshot
{
    /// <summary>空快照（未初始化或已清空）。</summary>
    public static LogSnapshot Empty { get; } = new(Array.Empty<LogEvent>(), Array.Empty<string>(), 0);

    /// <summary>按序（旧→新）排列的事件。</summary>
    public IReadOnlyList<LogEvent> Events { get; }

    /// <summary>与 <see cref="Events"/> 同下标的预计算搜索串（已小写）。</summary>
    public IReadOnlyList<string> SearchKeys { get; }

    /// <summary>快照对应的存储版本号，用来判断 UI 是否需要重算。</summary>
    public long Version { get; }

    /// <summary>事件条数。</summary>
    public int Count => Events.Count;

    /// <summary>构造快照；两个数组长度必须一致（不一致说明存储层有 bug，直接抛给测试暴露）。</summary>
    public LogSnapshot(IReadOnlyList<LogEvent> events, IReadOnlyList<string> searchKeys, long version)
    {
        ArgumentNullException.ThrowIfNull(events);
        ArgumentNullException.ThrowIfNull(searchKeys);
        if (events.Count != searchKeys.Count)
        {
            throw new ArgumentException(
                $"快照的 events({events.Count}) 与 searchKeys({searchKeys.Count}) 长度必须一致。",
                nameof(searchKeys));
        }

        Events = events;
        SearchKeys = searchKeys;
        Version = version;
    }
}
