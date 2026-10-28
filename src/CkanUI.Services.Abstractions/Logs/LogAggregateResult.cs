namespace CkanUI.Services.Abstractions.Logs;

/// <summary>
/// 聚合统计（族/级别/来源计数 + 时间轴 + TopN 指纹）。
/// 供日志页右侧统计栏、诊断包 summary.json 的「按族错误计数」复用同一份计算结果——两处口径必须一致。
/// </summary>
public sealed record LogAggregateResult
{
    /// <summary>空结果。</summary>
    public static LogAggregateResult Empty { get; } = new(
        0,
        new Dictionary<LogLevel, int>(),
        new Dictionary<LogFamily, int>(),
        new Dictionary<LogSourceKind, int>(),
        Array.Empty<LogTimelineBucket>(),
        Array.Empty<LogFingerprintGroup>());

    /// <summary>参与统计的总条数。</summary>
    public int Total { get; }

    /// <summary>按级别计数（未出现的级别不在字典里，取用时用 <see cref="Count(LogLevel)"/>）。</summary>
    public IReadOnlyDictionary<LogLevel, int> ByLevel { get; }

    /// <summary>按族计数。</summary>
    public IReadOnlyDictionary<LogFamily, int> ByFamily { get; }

    /// <summary>按来源计数。</summary>
    public IReadOnlyDictionary<LogSourceKind, int> BySource { get; }

    /// <summary>时间轴分桶（旧→新）。</summary>
    public IReadOnlyList<LogTimelineBucket> Timeline { get; }

    /// <summary>TopN 指纹组（条数降序）。</summary>
    public IReadOnlyList<LogFingerprintGroup> TopFingerprints { get; }

    /// <summary>构造结果。</summary>
    public LogAggregateResult(
        int total,
        IReadOnlyDictionary<LogLevel, int> byLevel,
        IReadOnlyDictionary<LogFamily, int> byFamily,
        IReadOnlyDictionary<LogSourceKind, int> bySource,
        IReadOnlyList<LogTimelineBucket> timeline,
        IReadOnlyList<LogFingerprintGroup> topFingerprints)
    {
        ArgumentNullException.ThrowIfNull(byLevel);
        ArgumentNullException.ThrowIfNull(byFamily);
        ArgumentNullException.ThrowIfNull(bySource);
        ArgumentNullException.ThrowIfNull(timeline);
        ArgumentNullException.ThrowIfNull(topFingerprints);
        Total = total;
        ByLevel = byLevel;
        ByFamily = byFamily;
        BySource = bySource;
        Timeline = timeline;
        TopFingerprints = topFingerprints;
    }

    /// <summary>取某级别条数（0 安全）。</summary>
    public int Count(LogLevel level) => ByLevel.TryGetValue(level, out var value) ? value : 0;

    /// <summary>取某族条数（0 安全）。</summary>
    public int Count(LogFamily family) => ByFamily.TryGetValue(family, out var value) ? value : 0;

    /// <summary>取某来源条数（0 安全）。</summary>
    public int Count(LogSourceKind kind) => BySource.TryGetValue(kind, out var value) ? value : 0;

    /// <summary>错误 + 致命总数（徽标与诊断包共用口径）。</summary>
    public int ErrorCount => Count(LogLevel.Error) + Count(LogLevel.Fatal);

    /// <summary>警告总数。</summary>
    public int WarningCount => Count(LogLevel.Warn);
}
