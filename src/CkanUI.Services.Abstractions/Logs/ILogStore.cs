namespace CkanUI.Services.Abstractions.Logs;

/// <summary>
/// 日志环形缓冲（14 篇 §8 LOG-02）：四路采集源唯一的汇流点，也是日志页/诊断包唯一的数据源。
/// 线程安全由实现保证：log4net appender 可能来自任意线程，UI 事件来自 UI 线程。
/// </summary>
public interface ILogStore
{
    /// <summary>当前容量配置。</summary>
    LogStoreOptions Options { get; }

    /// <summary>当前缓冲条数。</summary>
    int Count { get; }

    /// <summary>已分配的最大序号（清空后不回退，保证「证据行引用」跨清空仍然唯一）。</summary>
    long LastSeq { get; }

    /// <summary>存储版本号：每次新增/清空自增，UI 用它判断是否需要重算。</summary>
    long Version { get; }

    /// <summary>写入一条事件（序号与时间由存储补全，族已由采集侧判定）。返回补全后的事件。</summary>
    LogEvent Append(LogEvent draft);

    /// <summary>批量写入（旧日志导入、压测、异常批量上报）。返回实际写入条数。</summary>
    int AppendRange(IEnumerable<LogEvent> drafts);

    /// <summary>取一份不可变快照（旧→新）。查询与导出都用它，避免边查边变。</summary>
    LogSnapshot Snapshot();

    /// <summary>清空缓冲（序号不回退）。</summary>
    void Clear();

    /// <summary>缓冲变更通知（UI 线程订阅时必须自己切回 UI 线程）。</summary>
    event EventHandler<LogStoreChangedEventArgs>? Changed;
}
