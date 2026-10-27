namespace CkanUI.ViewModels.Operations;

/// <summary>一次进度采样的推算结果：速率与剩余时间（两者都可能未知）。</summary>
/// <param name="BytesPerSecond">平滑后的每秒字节数；没有字节数据时为 null。</param>
/// <param name="Remaining">剩余时间粗估；无法推算时为 null。</param>
public sealed record OperationRate(double? BytesPerSecond, TimeSpan? Remaining)
{
    /// <summary>未知速率与未知剩余时间。</summary>
    public static OperationRate Unknown { get; } = new(null, null);
}

/// <summary>
/// 逐任务的速率/剩余时间推算（03 篇 §1「操作状态区」、05 篇 §4 的 100ms 采样节奏）。
/// </summary>
/// <remarks>
/// 队列每 100ms 推一次快照，但快照里只有百分比（以及可能藏在消息里的字节数）。
/// 速率只能靠"前后两次采样的差分"得到，剩余时间再按剩余量除以速率算。
/// 三个刻意的取舍：
/// ① 最小采样间隔 200ms——100ms 的抖动会让速率在 0 和峰值之间反复跳；
/// ② 指数平滑（新值权重 0.4）——瞬时速率抖得太厉害，用户看到的是"闪烁的数字"；
/// ③ 无字节时用"已用时间 × 剩余百分比"粗估——精度差但方向对，且明确标注是粗估。
/// </remarks>
public sealed class OperationRateTracker
{
    /// <summary>两次采样之间的最小间隔（毫秒）：短于它就沿用上一次的速率，不重新计算。</summary>
    public const int MinimumSampleIntervalMilliseconds = 200;

    /// <summary>新样本在指数平滑中的权重。</summary>
    private const double SmoothingFactor = 0.4;

    /// <summary>粗估上限：超过 7 天的剩余时间没有意义（多半是百分比长期不动）。</summary>
    private static readonly TimeSpan MaximumRemaining = TimeSpan.FromDays(7);

    private readonly Dictionary<string, Sample> _samples = new(StringComparer.Ordinal);

    /// <summary>清空全部采样（队列清空或重新订阅时调用）。</summary>
    public void Reset() => _samples.Clear();

    /// <summary>丢弃单个任务的采样（任务被移除后调用，避免字典随会话无限增长）。</summary>
    /// <param name="taskId">任务 id。</param>
    public void Forget(string taskId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(taskId);
        _samples.Remove(taskId);
    }

    /// <summary>记录一次采样并推算速率与剩余时间。</summary>
    /// <param name="taskId">任务 id。</param>
    /// <param name="percent">当前百分比（0..100）。</param>
    /// <param name="bytes">已传字节；未知传 null。</param>
    /// <param name="totalBytes">总字节；未知传 null。</param>
    /// <param name="nowMilliseconds">当前单调时间戳（毫秒）。</param>
    /// <returns>本次推算结果。</returns>
    public OperationRate Track(string taskId, double percent, long? bytes, long? totalBytes, long nowMilliseconds)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(taskId);

        if (!_samples.TryGetValue(taskId, out var sample))
        {
            sample = new Sample { FirstTimestamp = nowMilliseconds, Timestamp = nowMilliseconds, Bytes = bytes };
            _samples[taskId] = sample;
            return OperationRate.Unknown;
        }

        var elapsed = nowMilliseconds - sample.Timestamp;
        if (elapsed >= MinimumSampleIntervalMilliseconds)
        {
            if (bytes is { } current && sample.Bytes is { } previous && current >= previous && elapsed > 0)
            {
                var instant = (current - previous) * 1000.0 / elapsed;
                sample.BytesPerSecond = sample.BytesPerSecond is { } smoothed
                    ? (SmoothingFactor * instant) + ((1 - SmoothingFactor) * smoothed)
                    : instant;
            }

            sample.Timestamp = nowMilliseconds;
            sample.Bytes = bytes;
        }

        return new OperationRate(sample.BytesPerSecond, EstimateRemaining(sample, percent, bytes, totalBytes, nowMilliseconds));
    }

    private static TimeSpan? EstimateRemaining(Sample sample, double percent, long? bytes, long? totalBytes, long nowMilliseconds)
    {
        TimeSpan? remaining = null;

        if (bytes is { } done && totalBytes is { } total && total > done && sample.BytesPerSecond is { } speed && speed > 1)
        {
            remaining = TimeSpan.FromSeconds((total - done) / speed);
        }
        else if (percent >= 1 && percent < 100)
        {
            // 无字节数据时的粗估：已用时间 × (剩余百分比 / 已完成百分比)
            var elapsed = nowMilliseconds - sample.FirstTimestamp;
            if (elapsed > 0)
            {
                remaining = TimeSpan.FromMilliseconds(elapsed * (100 - percent) / percent);
            }
        }

        if (remaining is not { } value || value < TimeSpan.Zero || value > MaximumRemaining)
        {
            return null;
        }

        return value;
    }

    private sealed class Sample
    {
        public long FirstTimestamp;

        public long Timestamp;

        public long? Bytes;

        public double? BytesPerSecond;
    }
}
