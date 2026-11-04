using System;
using CkanUI.Plugin.Performance.Telemetry;
using NUnit.Framework;

namespace CkanUI.Plugin.Performance.Tests;

/// <summary>
/// 帧时间曲线的构建：只画契约真给了的点，缺席就断线，全空就不画。
/// </summary>
[TestFixture]
public sealed class FrameSeriesBuilderTests
{
    [Test]
    public void NoTimeline_SaysWhyThereIsNoCurve()
    {
        var result = FrameSeriesBuilder.Build(Snapshot(timeline: null));

        Assert.Multiple(() =>
        {
            Assert.That(result.HasData, Is.False);
            Assert.That(result.CostSeries, Is.Empty);
            Assert.That(result.FpsSeries, Is.Empty);
            Assert.That(result.Summary, Does.Contain("timeline.frames"));
        });
    }

    [Test]
    public void EmptyFrames_BehaveLikeMissingTimeline()
    {
        var result = FrameSeriesBuilder.Build(Snapshot(new KppTimeline(10d, Array.Empty<KppTimelineFrame>())));

        Assert.Multiple(() =>
        {
            Assert.That(result.HasData, Is.False);
            Assert.That(result.CostSeries, Is.Empty);
            Assert.That(result.FpsSeries, Is.Empty);
        });
    }

    [Test]
    public void BuildsOneSeriesPerMetricInFrameOrder()
    {
        var result = FrameSeriesBuilder.Build(Snapshot(
            new KppTimeline(5d, [
                Frame(16.7, 60d),
                Frame(33.3, 30d),
                Frame(null, null),
            ])));

        Assert.Multiple(() =>
        {
            Assert.That(result.HasData, Is.True);
            Assert.That(result.CostSeries, Has.Count.EqualTo(1));
            Assert.That(result.FpsSeries, Has.Count.EqualTo(1));
            Assert.That(result.CostSeries[0].Name, Is.EqualTo("帧耗时"));
            Assert.That(result.FpsSeries[0].Name, Is.EqualTo("帧率"));
            Assert.That(result.CostSeries[0].Values, Is.EqualTo(new double?[] { 16.7, 33.3, null }), "顺序就是帧顺序，最新在右");
            Assert.That(result.FpsSeries[0].Values, Is.EqualTo(new double?[] { 60d, 30d, null }));
            Assert.That(result.Summary, Does.Contain("3 帧"));
            Assert.That(result.Summary, Does.Contain("5.0s"));
            Assert.That(result.Summary, Does.Contain("帧序号"));
        });
    }

    [Test]
    public void MissingWindow_ShowsTheSamePlaceholderAsTheRestOfThePage()
    {
        var result = FrameSeriesBuilder.Build(Snapshot(new KppTimeline(null, [Frame(16d, 60d)])));

        Assert.That(result.Summary, Does.Contain("N/A"));
    }

    [Test]
    public void AllEmptyMetrics_DoNotPretendToHaveACurve()
    {
        var result = FrameSeriesBuilder.Build(Snapshot(new KppTimeline(5d, [Frame(null, null), Frame(null, null)])));

        Assert.Multiple(() =>
        {
            Assert.That(result.HasData, Is.False);
            Assert.That(result.CostSeries, Is.Empty);
            Assert.That(result.FpsSeries, Is.Empty);
            Assert.That(result.Summary, Does.Contain("全为空"));
        });
    }

    [Test]
    public void OnlyFpsAvailable_DrawsOnlyTheFpsChart()
    {
        var result = FrameSeriesBuilder.Build(Snapshot(new KppTimeline(5d, [Frame(null, 60d), Frame(null, 55d)])));

        Assert.Multiple(() =>
        {
            Assert.That(result.HasData, Is.True);
            Assert.That(result.CostSeries, Is.Empty, "没有帧耗时就不给一张空图");
            Assert.That(result.FpsSeries, Has.Count.EqualTo(1));
        });
    }

    [Test]
    public void NonFiniteValuesAreNotPlotted()
    {
        var result = FrameSeriesBuilder.Build(Snapshot(new KppTimeline(5d, [
            Frame(double.NaN, double.PositiveInfinity),
            Frame(20d, 50d),
        ])));

        var cost = result.CostSeries[0].Values;
        Assert.Multiple(() =>
        {
            Assert.That(cost[0], Is.NaN, "缺席的帧保持原样（NaN），绘制层会自动断线");
            Assert.That(cost[1], Is.EqualTo(20d));
        });
    }

    [Test]
    public void NullSnapshot_Throws()
    {
        Assert.That(() => FrameSeriesBuilder.Build(null!), Throws.ArgumentNullException);
    }

    private static KppTimelineFrame Frame(double? frameMs, double? fps)
        => new(T: null, FrameMs: frameMs, Module: null, Plugin: null, Physx: null, Render: null, Gpu: null, Gc: null, Fps: fps);

    private static KppTelemetrySnapshot Snapshot(KppTimeline? timeline)
        => new(
            Schema: "kpp.telemetry/1",
            SchemaVersion: 1,
            Seq: 7,
            PublishedUnixMs: 0d,
            PublishIntervalMs: 1000d,
            Writer: null,
            Session: null,
            Context: null,
            Frame: null,
            Breakdown: null,
            Gpu: null,
            GpuBrackets: Array.Empty<KppGpuBracket>(),
            Memory: null,
            Hotspots: null,
            Timeline: timeline,
            Diagnostics: null,
            Spikes: Array.Empty<KppSpike>(),
            Counters: null);
}
