using System;
using System.Collections.Generic;
using System.ComponentModel;
using CkanUI.App.Diagnostics;
using NUnit.Framework;

namespace CkanUI.App.Tests;

[TestFixture]
public sealed class PerformanceTelemetryTests
{
    [Test]
    public void Buffer_WhenEmpty_ReturnsDefaultSnapshot()
    {
        var buffer = new PerformanceTelemetryBuffer(60);

        Assert.That(buffer.Count, Is.EqualTo(0));
        Assert.That(buffer.TotalFrames, Is.EqualTo(0));
        Assert.That(buffer.GetRecentFrameDurations(), Is.Empty);

        var snapshot = buffer.TakeSnapshot();
        Assert.That(snapshot.CurrentFps, Is.EqualTo(0));
        Assert.That(snapshot.AvgFps, Is.EqualTo(0));
        Assert.That(snapshot.P50FrameMs, Is.EqualTo(0));
        Assert.That(snapshot.P99FrameMs, Is.EqualTo(0));
        Assert.That(snapshot.TotalFrames, Is.EqualTo(0));
    }

    [Test]
    public void Buffer_UnderCapacity_RecordsInChronologicalOrder()
    {
        var buffer = new PerformanceTelemetryBuffer(10);
        buffer.RecordFrame(16.0, 1.0, 100);
        buffer.RecordFrame(8.0, 2.0, 116);
        buffer.RecordFrame(12.0, 0.5, 124);

        Assert.That(buffer.Count, Is.EqualTo(3));
        Assert.That(buffer.TotalFrames, Is.EqualTo(3));

        var durations = buffer.GetRecentFrameDurations();
        Assert.That(durations, Is.EqualTo(new[] { 16.0, 8.0, 12.0 }));

        var snapshot = buffer.TakeSnapshot();
        Assert.That(snapshot.TotalFrames, Is.EqualTo(3));
        Assert.That(snapshot.CurrentFrameMs, Is.EqualTo(12.0));
        Assert.That(snapshot.DispatcherLagMs, Is.EqualTo(0.5));
    }

    [Test]
    public void Buffer_WhenOverflows_WrapsRingBufferPreservingOrder()
    {
        var buffer = new PerformanceTelemetryBuffer(4);
        for (var i = 1; i <= 6; i++)
        {
            buffer.RecordFrame(i * 5.0, 0.1 * i, i * 10);
        }

        Assert.That(buffer.Count, Is.EqualTo(4));
        Assert.That(buffer.TotalFrames, Is.EqualTo(6));

        // Capacity is 4, so last 4 frames are 3*5=15, 4*5=20, 5*5=25, 6*5=30
        var durations = buffer.GetRecentFrameDurations();
        Assert.That(durations, Is.EqualTo(new[] { 15.0, 20.0, 25.0, 30.0 }));

        var snapshot = buffer.TakeSnapshot();
        Assert.That(snapshot.CurrentFrameMs, Is.EqualTo(30.0));
        Assert.That(snapshot.TotalFrames, Is.EqualTo(6));
    }

    [Test]
    public void Buffer_TracksHitchesAccurately()
    {
        var buffer = new PerformanceTelemetryBuffer(100);
        buffer.RecordFrame(8.0, 0.1);   // Normal (<16.7)
        buffer.RecordFrame(18.0, 0.5);  // Hitch > 16.7ms
        buffer.RecordFrame(40.0, 2.0);  // Hitch > 33.3ms (also > 16.7ms)
        buffer.RecordFrame(10.0, 0.2);  // Normal

        var snapshot = buffer.TakeSnapshot();
        Assert.That(snapshot.Hitches16Count, Is.EqualTo(2));
        Assert.That(snapshot.Hitches33Count, Is.EqualTo(1));
        Assert.That(snapshot.MaxFrameMs, Is.EqualTo(40.0));
        Assert.That(snapshot.MaxDispatcherLagMs, Is.EqualTo(2.0));
    }

    [Test]
    public void Buffer_Reset_ClearsAllHistoryAndMetrics()
    {
        var buffer = new PerformanceTelemetryBuffer(10);
        buffer.RecordFrame(20.0, 5.0);
        buffer.RecordFrame(35.0, 8.0);

        buffer.Reset();

        Assert.That(buffer.Count, Is.EqualTo(0));
        Assert.That(buffer.TotalFrames, Is.EqualTo(0));
        Assert.That(buffer.GetRecentFrameDurations(), Is.Empty);

        var snapshot = buffer.TakeSnapshot();
        Assert.That(snapshot.TotalFrames, Is.EqualTo(0));
        Assert.That(snapshot.Hitches16Count, Is.EqualTo(0));
        Assert.That(snapshot.Hitches33Count, Is.EqualTo(0));
        Assert.That(snapshot.MaxFrameMs, Is.EqualTo(0));
        Assert.That(snapshot.MaxDispatcherLagMs, Is.EqualTo(0));
    }

    [Test]
    public void CalculatePercentile_KnownSequence_InterpolatesCorrectly()
    {
        // 5 elements: indices 0, 1, 2, 3, 4 -> values 10, 20, 30, 40, 50
        var seq = new double[] { 10, 20, 30, 40, 50 };

        // p0 = 10, p100 = 50
        Assert.That(PerformanceTelemetryBuffer.CalculatePercentile(seq, 0), Is.EqualTo(10.0));
        Assert.That(PerformanceTelemetryBuffer.CalculatePercentile(seq, 100), Is.EqualTo(50.0));

        // p50: n = (5-1) * 0.5 = 2 -> seq[2] = 30
        Assert.That(PerformanceTelemetryBuffer.CalculatePercentile(seq, 50), Is.EqualTo(30.0));

        // p25: n = 4 * 0.25 = 1 -> seq[1] = 20
        Assert.That(PerformanceTelemetryBuffer.CalculatePercentile(seq, 25), Is.EqualTo(20.0));

        // p75: n = 4 * 0.75 = 3 -> seq[3] = 40
        Assert.That(PerformanceTelemetryBuffer.CalculatePercentile(seq, 75), Is.EqualTo(40.0));

        // p10: n = 4 * 0.1 = 0.4 -> 10 + 0.4 * (20 - 10) = 14
        Assert.That(PerformanceTelemetryBuffer.CalculatePercentile(seq, 10), Is.EqualTo(14.0).Within(0.001));

        // p90: n = 4 * 0.9 = 3.6 -> 40 + 0.6 * (50 - 40) = 46
        Assert.That(PerformanceTelemetryBuffer.CalculatePercentile(seq, 90), Is.EqualTo(46.0).Within(0.001));
    }

    [Test]
    public void CalculatePercentile_EdgeCases_HandleGracefully()
    {
        Assert.That(PerformanceTelemetryBuffer.CalculatePercentile(Array.Empty<double>(), 50), Is.EqualTo(0));
        Assert.That(PerformanceTelemetryBuffer.CalculatePercentile(new[] { 42.0 }, 50), Is.EqualTo(42.0));
        Assert.That(PerformanceTelemetryBuffer.CalculatePercentile(new[] { 1.0, 10.0 }, -10), Is.EqualTo(1.0));
        Assert.That(PerformanceTelemetryBuffer.CalculatePercentile(new[] { 1.0, 10.0 }, 150), Is.EqualTo(10.0));
    }

    [Test]
    public void CalculateOnePercentLow_ComputesSlowestFramesInverse()
    {
        // 100 frames: 1 to 100 ms
        var sorted = new double[100];
        for (var i = 0; i < 100; i++)
        {
            sorted[i] = i + 1; // 1..100
        }

        // 1% of 100 is 1 frame: slowest frame is 100 ms.
        // 1000 / 100 = 10 FPS
        var low100 = PerformanceTelemetryBuffer.CalculateOnePercentLow(sorted);
        Assert.That(low100, Is.EqualTo(10.0).Within(0.01));

        // 200 frames: 1 to 200 ms
        // 1% of 200 is 2 frames: 199 and 200 ms. Average = 199.5 ms.
        // 1000 / 199.5 ≈ 5.0125 FPS
        var sorted200 = new double[200];
        for (var i = 0; i < 200; i++)
        {
            sorted200[i] = i + 1;
        }
        var low200 = PerformanceTelemetryBuffer.CalculateOnePercentLow(sorted200);
        Assert.That(low200, Is.EqualTo(1000.0 / 199.5).Within(0.01));
    }

    [Test]
    public void PerformanceMonitor_InitialProperties_AreConsistent()
    {
        var monitor = new PerformanceMonitor();
        Assert.That(monitor.Telemetry, Is.Not.Null);
        Assert.That(monitor.IsOpen, Is.False);
        Assert.That(monitor.CurrentFps, Is.EqualTo(0));
        Assert.That(monitor.P50FrameMs, Is.EqualTo(0));
        Assert.That(monitor.P99FrameMs, Is.EqualTo(0));
        Assert.That(monitor.FpsSummaryText, Is.EqualTo("FPS --"));
        Assert.That(monitor.FrameTimeSummaryText, Does.Contain("p50: 0.0ms"));
    }

    [Test]
    public void PerformanceMonitor_RefreshSnapshot_NotifiesPropertyChanges()
    {
        var monitor = new PerformanceMonitor();
        var changedProps = new List<string>();
        monitor.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName != null) changedProps.Add(e.PropertyName);
        };

        // Record a sequence of frames
        monitor.Telemetry.RecordFrame(8.0, 0.5);
        monitor.Telemetry.RecordFrame(16.0, 1.0);

        monitor.RefreshSnapshot();

        Assert.That(changedProps, Does.Contain(nameof(monitor.CurrentFps)));
        Assert.That(changedProps, Does.Contain(nameof(monitor.CurrentFrameMs)));
        Assert.That(changedProps, Does.Contain(nameof(monitor.P50FrameMs)));
        Assert.That(changedProps, Does.Contain(nameof(monitor.P99FrameMs)));
        Assert.That(changedProps, Does.Contain(nameof(monitor.DispatcherLagMs)));
        Assert.That(changedProps, Does.Contain(nameof(monitor.FpsSummaryText)));
        Assert.That(changedProps, Does.Contain(nameof(monitor.FrameTimeSummaryText)));
        Assert.That(changedProps, Does.Contain(nameof(monitor.ResponsivenessText)));
        Assert.That(changedProps, Does.Contain(nameof(monitor.MemorySummaryText)));
    }
}
