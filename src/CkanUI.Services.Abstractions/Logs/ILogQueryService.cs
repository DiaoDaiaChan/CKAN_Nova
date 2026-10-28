namespace CkanUI.Services.Abstractions.Logs;

/// <summary>
/// 日志查询与聚合（14 篇 §4.2 ①④⑤）。
/// 为什么读接口不返回 <see cref="OperationResult"/>：查询是纯内存计算、不会失败，
/// 套一层操作结果只会让调用方写一堆 <c>if (!result.IsSuccess)</c> 噪音；真正可能失败的（打包、导入、读文件）才走 OperationResult。
/// </summary>
public interface ILogQueryService
{
    /// <summary>按条件过滤，返回窗口 + 总命中数。</summary>
    LogQueryResult Query(LogQuery query);

    /// <summary>统计（按级别/族/来源、时间轴分桶、TopN 指纹）。</summary>
    LogAggregateResult Aggregate(LogQuery query, int timelineBuckets = 60, int topFingerprints = 10);

    /// <summary>只取指纹聚合（日志页「聚合视图」用，避免为了分组去算整套统计）。</summary>
    IReadOnlyList<LogFingerprintGroup> Fingerprints(LogQuery query, int take = 10);

    /// <summary>当前缓冲的原始快照（导出、诊断包、测试断言用）。</summary>
    LogSnapshot Snapshot();
}
