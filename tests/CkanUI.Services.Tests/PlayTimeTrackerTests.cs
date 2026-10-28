using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using CkanUI.Services.Abstractions;
using CkanUI.Services.PlayTime;
using CkanUI.TestSupport;
using NUnit.Framework;

namespace CkanUI.Services.Tests;

/// <summary>
/// 游戏时长自动累计用例（12 篇 F-48）：会话起止结算、跨自然日切分、崩溃遗留会话处置，
/// 以及「自动累计」与「手工补录」合并时不得双计。
/// 时钟全部由替身支配，断言里不出现 System 的当前时间。
/// </summary>
[TestFixture]
public sealed class PlayTimeTrackerTests
{
    private TempWorkspace _workspace = null!;
    private FakeClock _clock = null!;

    private string TimesFile => _workspace.Combine("play-time.json");

    [SetUp]
    public void SetUp()
    {
        _workspace = new TempWorkspace("playtime-tracker");
        _clock = new FakeClock(new DateTimeOffset(2024, 5, 1, 10, 0, 0, TimeSpan.Zero));
    }

    [TearDown]
    public void TearDown() => _workspace.Dispose();

    private PlayTimeService CreateService() => new(TimesFile, _clock.Read);

    [Test]
    public void BeginThenEnd_AccumulatesElapsedHours()
    {
        var tracker = CreateService();

        Assert.That(tracker.BeginSession("测试实例"), Is.True);
        Assert.That(tracker.ActiveInstanceName, Is.EqualTo("测试实例"));

        _clock.Advance(TimeSpan.FromMinutes(90));
        var hours = tracker.EndSession();

        Assert.Multiple(() =>
        {
            Assert.That(hours, Is.EqualTo(1.5).Within(0.001));
            Assert.That(tracker.ActiveInstanceName, Is.Null);
            Assert.That(tracker.Load().Single().Hours, Is.EqualTo(1.5));
        });
    }

    [Test]
    public void SessionAcrossMidnight_SplitsIntoNaturalDays()
    {
        _clock.Set(new DateTimeOffset(2024, 5, 1, 23, 0, 0, TimeSpan.Zero));
        var tracker = CreateService();
        tracker.BeginSession("测试实例");

        _clock.Advance(TimeSpan.FromHours(2));
        tracker.EndSession();

        var ledger = new PlayTimeSessionLedger(PlayTimeSessionLedger.PathFor(TimesFile));
        var slices = ledger.SlicesByDate("测试实例");

        Assert.Multiple(() =>
        {
            // 跨零点的会话必须按本地自然日切成两段，否则「每天玩了多久」无从回溯。
            Assert.That(slices.Keys, Is.EqualTo(new[] { new DateOnly(2024, 5, 1), new DateOnly(2024, 5, 2) }));
            Assert.That(slices[new DateOnly(2024, 5, 1)], Is.EqualTo(1.0).Within(0.001));
            Assert.That(slices[new DateOnly(2024, 5, 2)], Is.EqualTo(1.0).Within(0.001));
            Assert.That(tracker.Load().Single().Hours, Is.EqualTo(2.0));
        });
    }

    [Test]
    public void EndSessionWithoutBegin_ReturnsZeroAndWritesNothing()
    {
        var tracker = CreateService();

        Assert.Multiple(() =>
        {
            Assert.That(tracker.EndSession(), Is.Zero);
            Assert.That(tracker.Load(), Is.Empty);
            Assert.That(File.Exists(PlayTimeSessionLedger.PathFor(TimesFile)), Is.False);
        });
    }

    [Test]
    public void DiscardOpenSession_IsIdempotent()
    {
        var tracker = CreateService();
        tracker.BeginSession("测试实例");

        Assert.Multiple(() =>
        {
            Assert.That(tracker.DiscardOpenSession(), Is.True);
            Assert.That(tracker.DiscardOpenSession(), Is.False);
            Assert.That(tracker.ActiveInstanceName, Is.Null);
            // 丢弃未闭合会话不记任何时长。
            Assert.That(tracker.Load(), Is.Empty);
        });
    }

    [Test]
    public void StaleOpenSession_IsDiscardedWhenNextSessionBegins()
    {
        var tracker = CreateService();
        tracker.BeginSession("测试实例");

        // 模拟进程被杀：没有 EndSession，只留下 open 记录。拿不到真实退出时刻，宁可少记也不虚增。
        _clock.Advance(TimeSpan.FromHours(5));
        Assert.That(tracker.BeginSession("测试实例"), Is.True);

        _clock.Advance(TimeSpan.FromHours(1));
        tracker.EndSession();

        Assert.That(tracker.Load().Single().Hours, Is.EqualTo(1.0));
    }

    [Test]
    public void OpenSession_SurvivesServiceRestart()
    {
        CreateService().BeginSession("测试实例");
        _clock.Advance(TimeSpan.FromMinutes(120));

        // 换一个实例读同一份文件：账本落在磁盘上，宿主重启后仍能结算。
        var restarted = CreateService();

        Assert.Multiple(() =>
        {
            Assert.That(restarted.ActiveInstanceName, Is.EqualTo("测试实例"));
            Assert.That(restarted.EndSession(), Is.EqualTo(2.0).Within(0.001));
            Assert.That(restarted.Load().Single().Hours, Is.EqualTo(2.0));
        });
    }

    [Test]
    public void ClockRollback_RecordsZeroInsteadOfNegative()
    {
        var tracker = CreateService();
        tracker.BeginSession("测试实例");

        // 夏令时或手工改表把时钟拨回去：总时长绝不倒退。
        _clock.Set(new DateTimeOffset(2024, 5, 1, 9, 0, 0, TimeSpan.Zero));

        Assert.Multiple(() =>
        {
            Assert.That(tracker.EndSession(), Is.Zero);
            Assert.That(tracker.Load(), Is.Empty);
        });
    }

    [Test]
    public void ManualSave_DoesNotDoubleCountAutoHours()
    {
        var tracker = CreateService();
        tracker.BeginSession("测试实例");
        _clock.Advance(TimeSpan.FromHours(2));
        tracker.EndSession();

        // 页面上填的是「累计总量」：5 小时里已经包含自动累计的 2 小时，落盘只存差值 3。
        Assert.That(tracker.Save("测试实例", 5), Is.True);
        Assert.That(tracker.Load().Single().Hours, Is.EqualTo(5.0));

        tracker.BeginSession("测试实例");
        _clock.Advance(TimeSpan.FromHours(1));
        tracker.EndSession();

        Assert.That(tracker.Load().Single().Hours, Is.EqualTo(6.0));
    }

    [Test]
    public void ManualTotalBelowAutoAccumulation_ShowsAutoAccumulation()
    {
        var tracker = CreateService();
        tracker.BeginSession("测试实例");
        _clock.Advance(TimeSpan.FromHours(2));
        tracker.EndSession();

        // 手工填得比自动累计还小：基线夹到 0，页面显示自动累计（宁可不少记）。
        Assert.That(tracker.Save("测试实例", 1), Is.True);
        Assert.That(tracker.Load().Single().Hours, Is.EqualTo(2.0));
    }

    [Test]
    public void TwoInstances_AccumulateIndependentlyAndLoadIsOrdered()
    {
        var tracker = CreateService();

        tracker.BeginSession("乙实例");
        _clock.Advance(TimeSpan.FromHours(1));
        tracker.EndSession();

        tracker.BeginSession("甲实例");
        _clock.Advance(TimeSpan.FromHours(3));
        tracker.EndSession();

        var entries = tracker.Load();

        Assert.Multiple(() =>
        {
            // 顺序按实例名序数排序，页面刷新时行序不会抖动。
            Assert.That(entries.Select(entry => entry.InstanceName), Is.EqualTo(new[] { "乙实例", "甲实例" }));
            Assert.That(entries[0].Hours, Is.EqualTo(1.0));
            Assert.That(entries[1].Hours, Is.EqualTo(3.0));
        });
    }

    [Test]
    public void ManualSave_KeepsOtherInstanceBaselines()
    {
        var tracker = CreateService();
        Assert.That(tracker.Save("甲实例", 4), Is.True);
        Assert.That(tracker.Save("乙实例", 7), Is.True);

        var entries = tracker.Load();

        Assert.Multiple(() =>
        {
            Assert.That(entries, Has.Count.EqualTo(2));
            // 「乙」的码位小于「甲」，序数序与中文阅读顺序不同；这里只要求写入一个实例不会覆盖另一个。
            Assert.That(entries.Select(entry => entry.InstanceName), Is.EqualTo(new[] { "乙实例", "甲实例" }));
            Assert.That(entries[0].Hours, Is.EqualTo(7.0));
            Assert.That(entries[1].Hours, Is.EqualTo(4.0));
        });
    }

    [Test]
    public void LedgerPath_IsSiblingOfPlayTimeFile()
    {
        Assert.That(
            PlayTimeSessionLedger.PathFor(Path.Combine("x", "play-time.json")),
            Is.EqualTo(Path.Combine("x", "play-time.sessions.json")));
    }

    [Test]
    public void CorruptLedger_IsTreatedAsEmptyAndOverwrittenOnNextSession()
    {
        Directory.CreateDirectory(_workspace.Path);
        File.WriteAllText(PlayTimeSessionLedger.PathFor(TimesFile), "{ 这不是 JSON");
        var tracker = CreateService();

        Assert.Multiple(() =>
        {
            // 账本坏了当没有：自动累计是附赠信息，不该阻断时长页。
            Assert.That(tracker.Load(), Is.Empty);
            Assert.That(tracker.BeginSession("测试实例"), Is.True);
        });

        _clock.Advance(TimeSpan.FromHours(1));
        tracker.EndSession();

        Assert.That(tracker.Load().Single().Hours, Is.EqualTo(1.0));
    }

    [Test]
    public void BeginSession_RejectsBlankInstanceName()
    {
        var tracker = CreateService();

        Assert.Multiple(() =>
        {
            Assert.That(tracker.BeginSession("  "), Is.False);
            Assert.That(tracker.ActiveInstanceName, Is.Null);
        });
    }

    /// <summary>可支配「现在」的时钟：自动累计的断言必须与真实时间无关。</summary>
    private sealed class FakeClock
    {
        private DateTimeOffset _now;

        public FakeClock(DateTimeOffset start) => _now = start;

        public DateTimeOffset Read() => _now;

        public void Advance(TimeSpan delta) => _now += delta;

        public void Set(DateTimeOffset now) => _now = now;
    }
}
