namespace CkanUI.Services.Abstractions.Logs;

/// <summary>
/// 时间轴条的一根柱子（14 篇 §4.2 ⑤）：按时间分桶的错误/警告/其他计数。
/// 只保留三种计数而不是全级别计数——时间轴条宽只有几像素，多于三色无法分辨。
/// </summary>
public sealed record LogTimelineBucket(DateTime StartLocal, int ErrorCount, int WarningCount, int OtherCount)
{
    /// <summary>桶内总条数。</summary>
    public int Total => ErrorCount + WarningCount + OtherCount;
}
