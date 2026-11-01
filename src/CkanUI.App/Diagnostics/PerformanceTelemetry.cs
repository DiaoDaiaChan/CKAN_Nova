using System;
using System.Collections.Generic;
using System.Linq;

namespace CkanUI.App.Diagnostics;

/// <summary>单帧测量样本。</summary>
public readonly record struct FrameMetricSample(
    double DurationMs,
    double DispatcherLagMs,
    long TimestampMs);

/// <summary>性能度量只读快照（包含 FPS、分位数、响应度与内存）。</summary>
public sealed record PerformanceSnapshot(
    double CurrentFps,
    double AvgFps,
    double MinFps,
    double MaxFps,
    double OnePercentLowFps,
    double CurrentFrameMs,
    double P50FrameMs,
    double P90FrameMs,
    double P95FrameMs,
    double P99FrameMs,
    double MaxFrameMs,
    double DispatcherLagMs,
    double MaxDispatcherLagMs,
    int TotalFrames,
    int Hitches16Count,
    int Hitches33Count,
    long MemoryBytes,
    int Gen0Count,
    int Gen1Count,
    int Gen2Count);

/// <summary>
/// 性能遥测计算核心与环形缓冲区：
/// 负责高精度帧时间累积、分位数 (p50/p90/p95/p99) 与 1% Low 计算，以及主线程调度滞后 (Dispatcher Lag) 统计。
/// </summary>
public sealed class PerformanceTelemetryBuffer
{
    private readonly FrameMetricSample[] _buffer;
    private readonly object _sync = new();
    private int _head;
    private int _count;
    private int _totalFrames;
    private int _hitches16;
    private int _hitches33;
    private double _maxFrameMs;
    private double _maxLagMs;

    public PerformanceTelemetryBuffer(int capacity = 120)
    {
        if (capacity < 2) capacity = 2;
        _buffer = new FrameMetricSample[capacity];
    }

    public int Capacity => _buffer.Length;
    public int Count { get { lock (_sync) return _count; } }
    public int TotalFrames { get { lock (_sync) return _totalFrames; } }

    /// <summary>重置所有统计与历史样本。</summary>
    public void Reset()
    {
        lock (_sync)
        {
            _head = 0;
            _count = 0;
            _totalFrames = 0;
            _hitches16 = 0;
            _hitches33 = 0;
            _maxFrameMs = 0;
            _maxLagMs = 0;
            Array.Clear(_buffer);
        }
    }

    /// <summary>推入一帧采样结果。</summary>
    /// <param name="durationMs">本帧耗时（毫秒）。</param>
    /// <param name="dispatcherLagMs">主线程调度滞后（毫秒）。</param>
    /// <param name="timestampMs">采样时间戳（毫秒）。</param>
    /// <param name="countHitches">
    /// 是否把这一帧计入卡顿统计。空闲降频期帧间隔是我们主动拉长的，计进卡顿只会制造假阳性。
    /// </param>
    public void RecordFrame(double durationMs, double dispatcherLagMs, long timestampMs = 0, bool countHitches = true)
    {
        if (durationMs < 0.001) durationMs = 0.001;
        if (dispatcherLagMs < 0) dispatcherLagMs = 0;

        lock (_sync)
        {
            _totalFrames++;
            if (countHitches && durationMs > 16.7) _hitches16++;
            if (countHitches && durationMs > 33.3) _hitches33++;
            if (durationMs > _maxFrameMs) _maxFrameMs = durationMs;
            if (dispatcherLagMs > _maxLagMs) _maxLagMs = dispatcherLagMs;

            _buffer[_head] = new FrameMetricSample(durationMs, dispatcherLagMs, timestampMs);
            _head = (_head + 1) % _buffer.Length;
            if (_count < _buffer.Length)
            {
                _count++;
            }
        }
    }

    /// <summary>获取最近的帧耗时数组（用于火花线/历史图渲染，按时间从旧到新排列）。</summary>
    public double[] GetRecentFrameDurations()
    {
        lock (_sync)
        {
            if (_count == 0) return Array.Empty<double>();

            var result = new double[_count];
            var start = (_count == _buffer.Length) ? _head : 0;
            for (var i = 0; i < _count; i++)
            {
                var index = (start + i) % _buffer.Length;
                result[i] = _buffer[index].DurationMs;
            }
            return result;
        }
    }

    /// <summary>生成当前遥测快照。</summary>
    public PerformanceSnapshot TakeSnapshot()
    {
        double[] durations;
        double latestLag;
        int hitches16;
        int hitches33;
        int total;
        double maxLag;
        double maxFrame;

        lock (_sync)
        {
            if (_count == 0)
            {
                return new PerformanceSnapshot(
                    CurrentFps: 0,
                    AvgFps: 0,
                    MinFps: 0,
                    MaxFps: 0,
                    OnePercentLowFps: 0,
                    CurrentFrameMs: 0,
                    P50FrameMs: 0,
                    P90FrameMs: 0,
                    P95FrameMs: 0,
                    P99FrameMs: 0,
                    MaxFrameMs: 0,
                    DispatcherLagMs: 0,
                    MaxDispatcherLagMs: 0,
                    TotalFrames: _totalFrames,
                    Hitches16Count: _hitches16,
                    Hitches33Count: _hitches33,
                    MemoryBytes: GC.GetTotalMemory(false),
                    Gen0Count: GC.CollectionCount(0),
                    Gen1Count: GC.CollectionCount(1),
                    Gen2Count: GC.CollectionCount(2));
            }

            durations = new double[_count];
            var start = (_count == _buffer.Length) ? _head : 0;
            for (var i = 0; i < _count; i++)
            {
                durations[i] = _buffer[(start + i) % _buffer.Length].DurationMs;
            }

            var latestIndex = (_head - 1 + _buffer.Length) % _buffer.Length;
            latestLag = _buffer[latestIndex].DispatcherLagMs;
            hitches16 = _hitches16;
            hitches33 = _hitches33;
            total = _totalFrames;
            maxLag = _maxLagMs;
            maxFrame = _maxFrameMs;
        }

        var currentFrameMs = durations[^1];
        var currentFps = currentFrameMs > 0 ? Math.Min(999.0, 1000.0 / currentFrameMs) : 0;

        // 计算分位数需要排序副本
        var sorted = (double[])durations.Clone();
        Array.Sort(sorted);

        var p50 = CalculatePercentile(sorted, 50.0);
        var p90 = CalculatePercentile(sorted, 90.0);
        var p95 = CalculatePercentile(sorted, 95.0);
        var p99 = CalculatePercentile(sorted, 99.0);

        var avgFrameMs = durations.Average();
        var avgFps = avgFrameMs > 0 ? Math.Min(999.0, 1000.0 / avgFrameMs) : 0;
        var minFps = sorted[^1] > 0 ? Math.Min(999.0, 1000.0 / sorted[^1]) : 0;
        var maxFps = sorted[0] > 0 ? Math.Min(999.0, 1000.0 / sorted[0]) : 0;

        var onePercentLow = CalculateOnePercentLow(sorted);

        return new PerformanceSnapshot(
            CurrentFps: Math.Round(currentFps, 1),
            AvgFps: Math.Round(avgFps, 1),
            MinFps: Math.Round(minFps, 1),
            MaxFps: Math.Round(maxFps, 1),
            OnePercentLowFps: Math.Round(onePercentLow, 1),
            CurrentFrameMs: Math.Round(currentFrameMs, 2),
            P50FrameMs: Math.Round(p50, 2),
            P90FrameMs: Math.Round(p90, 2),
            P95FrameMs: Math.Round(p95, 2),
            P99FrameMs: Math.Round(p99, 2),
            MaxFrameMs: Math.Round(maxFrame > sorted[^1] ? maxFrame : sorted[^1], 2),
            DispatcherLagMs: Math.Round(latestLag, 2),
            MaxDispatcherLagMs: Math.Round(maxLag, 2),
            TotalFrames: total,
            Hitches16Count: hitches16,
            Hitches33Count: hitches33,
            MemoryBytes: GC.GetTotalMemory(false),
            Gen0Count: GC.CollectionCount(0),
            Gen1Count: GC.CollectionCount(1),
            Gen2Count: GC.CollectionCount(2));
    }

    /// <summary>
    /// 标准线性插值分位数计算（NIST 规范）。
    /// 输入必须是已按升序排好序的数组。
    /// </summary>
    public static double CalculatePercentile(IReadOnlyList<double> sortedSequence, double percentile)
    {
        if (sortedSequence.Count == 0) return 0;
        if (sortedSequence.Count == 1) return sortedSequence[0];
        if (percentile <= 0) return sortedSequence[0];
        if (percentile >= 100) return sortedSequence[^1];

        var n = (sortedSequence.Count - 1) * (percentile / 100.0);
        var k = (int)n;
        var d = n - k;
        if (k + 1 < sortedSequence.Count)
        {
            return sortedSequence[k] + d * (sortedSequence[k + 1] - sortedSequence[k]);
        }
        return sortedSequence[k];
    }

    /// <summary>
    /// 1% Low FPS 计算：最慢 1% 帧的平均帧时间倒数。
    /// 若样本数较少，至少取最慢的 1 帧。
    /// </summary>
    public static double CalculateOnePercentLow(IReadOnlyList<double> sortedAscending)
    {
        if (sortedAscending.Count == 0) return 0;
        var count = Math.Max(1, (int)Math.Ceiling(sortedAscending.Count * 0.01));
        var startIndex = sortedAscending.Count - count;

        double sum = 0;
        for (var i = startIndex; i < sortedAscending.Count; i++)
        {
            sum += sortedAscending[i];
        }
        var avgSlowestMs = sum / count;
        return avgSlowestMs > 0 ? 1000.0 / avgSlowestMs : 0;
    }
}
