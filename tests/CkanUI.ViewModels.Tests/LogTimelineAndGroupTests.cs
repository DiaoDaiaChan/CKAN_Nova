using System;
using CkanUI.Services.Abstractions.Logs;
using CkanUI.ViewModels.Pages.Logs;
using NUnit.Framework;

namespace CkanUI.ViewModels.Tests;

/// <summary>右缘时间轴与聚合分组的小视图模型（14 篇 §4.2④⑤）。</summary>
[TestFixture]
public sealed class LogTimelineAndGroupTests
{
    private static LogTimelineBucket Bucket(int errors, int warnings, int others) =>
        new(new DateTime(2026, 3, 7, 14, 5, 0, DateTimeKind.Local), errors, warnings, others);

    [Test]
    public void TimelineBar_ScalesToBucketShare()
    {
        var bar = new LogTimelineBarViewModel(Bucket(2, 1, 3), maxTotal: 6);

        Assert.That(bar.Total, Is.EqualTo(6));
        Assert.That(bar.HeightRatio, Is.EqualTo(1.0).Within(0.001));
        Assert.That(bar.BarHeight, Is.EqualTo(LogTimelineBarViewModel.MaxBarHeight).Within(0.001));
        Assert.That(bar.HasError, Is.True);
        Assert.That(bar.HasWarning, Is.True);
    }

    [Test]
    public void TimelineBar_HasMinimumVisibleHeight()
    {
        var bar = new LogTimelineBarViewModel(Bucket(0, 0, 1), maxTotal: 1000);

        Assert.That(bar.BarHeight, Is.EqualTo(2.0), "再少的日志也要画出可见的柱子");
    }

    [Test]
    public void TimelineBar_TipTextMentionsCounts()
    {
        var bar = new LogTimelineBarViewModel(Bucket(2, 1, 3), maxTotal: 6);

        Assert.That(bar.TipText, Does.Contain("错误 2"));
        Assert.That(bar.TipText, Does.Contain("警告 1"));
        Assert.That(bar.TipText, Does.Contain("14:05:00"));
    }

    [Test]
    public void TimelineBar_WithNoProblemsHasNoFlags()
    {
        var bar = new LogTimelineBarViewModel(Bucket(0, 0, 4), maxTotal: 4);

        Assert.That(bar.HasError, Is.False);
        Assert.That(bar.HasWarning, Is.False);
    }

    [Test]
    public void FingerprintGroup_FormatsCountAndSeverity()
    {
        var group = new LogFingerprintGroup("key", LogLevel.Error, LogFamily.Net, "connection reset", 3)
        {
            FirstSeq = 10,
            LastSeq = 42,
        };

        var vm = new LogFingerprintGroupViewModel(group);

        Assert.That(vm.Count, Is.EqualTo(3));
        Assert.That(vm.CountText, Is.EqualTo("×3"));
        Assert.That(vm.Glyph, Is.EqualTo("×"));
        Assert.That(vm.LevelClass, Is.EqualTo("error"));
        Assert.That(vm.FamilyLabel, Is.EqualTo("网络"));
        Assert.That(vm.Sample, Is.EqualTo("connection reset"));
        Assert.That(vm.FirstSeq, Is.EqualTo(10));
        Assert.That(vm.LastSeq, Is.EqualTo(42));
        Assert.That(vm.IsError, Is.True);
    }

    [Test]
    public void FingerprintGroup_WarningIsNotError()
    {
        var vm = new LogFingerprintGroupViewModel(
            new LogFingerprintGroup("key", LogLevel.Warn, LogFamily.Download, "partial download", 2));

        Assert.That(vm.IsError, Is.False);
        Assert.That(vm.LevelLabel, Is.EqualTo("警告"));
    }
}
