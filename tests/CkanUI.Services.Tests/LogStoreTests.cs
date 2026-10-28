using CkanUI.Services.Abstractions.Logs;
using CkanUI.Services.Logs;
using NUnit.Framework;

namespace CkanUI.Services.Tests;

/// <summary>LOG-02 环形缓冲的行为契约（编号/淘汰/快照顺序/事件）。</summary>
[TestFixture]
sealed class LogStoreTests
{
    private static LogEvent Draft(LogLevel level = LogLevel.Info, string message = "hello", LogSourceKind kind = LogSourceKind.Ui) =>
        LogEvent.Create(kind, level, LogSources.Prefix(kind), message);

    private static LogStore NewStore(ILogClassifier? classifier = null, int capacity = LogStoreOptions.MinCapacity) =>
        new(classifier, new LogStoreOptions { Capacity = capacity });

    [Test]
    public void Append_AssignsMonotonicSequenceFromOne()
    {
        var store = NewStore();

        var first = store.Append(Draft(message: "a"));
        var second = store.Append(Draft(message: "b"));
        var third = store.Append(Draft(message: "c"));

        Assert.That(new[] { first.Seq, second.Seq, third.Seq }, Is.EqualTo(new long[] { 1, 2, 3 }));
        Assert.That(store.LastSeq, Is.EqualTo(3));
        Assert.That(store.Count, Is.EqualTo(3));
    }

    [Test]
    public void Append_OverridesCallerSequence()
    {
        // 采集侧可能复制别人的 Seq；存储必须自己发号，否则 AI 证据行会串。
        var store = NewStore();

        var stored = store.Append(Draft() with { Seq = 999 });

        Assert.That(stored.Seq, Is.EqualTo(1));
    }

    [Test]
    public void Append_FillsMissingTimeWithNow()
    {
        var store = NewStore();
        var before = DateTime.Now;

        var stored = store.Append(Draft());

        Assert.That(stored.TimeLocal, Is.GreaterThanOrEqualTo(before.AddSeconds(-1)));
    }

    [Test]
    public void Append_KeepsExplicitTime()
    {
        var store = NewStore();
        var explicitTime = new DateTime(2024, 5, 17, 8, 30, 0, DateTimeKind.Local);

        var stored = store.Append(Draft() with { TimeLocal = explicitTime });

        Assert.That(stored.TimeLocal, Is.EqualTo(explicitTime));
    }

    [Test]
    public void Append_EvictsOldestWhenFull()
    {
        var store = NewStore(capacity: 100);

        for (var i = 0; i < 105; i++)
        {
            store.Append(Draft(message: "m" + i));
        }

        var snapshot = store.Snapshot();
        Assert.That(store.Count, Is.EqualTo(100));
        Assert.That(snapshot.Events[0].Message, Is.EqualTo("m5"));
        Assert.That(snapshot.Events[^1].Message, Is.EqualTo("m104"));
    }

    [Test]
    public void Options_ClampsCapacityBelowMinimum()
    {
        var store = new LogStore(null, new LogStoreOptions { Capacity = 10 });

        Assert.That(store.Options.Capacity, Is.EqualTo(LogStoreOptions.MinCapacity));
    }

    [Test]
    public void Options_ClampsCapacityAboveMaximum()
    {
        var clamped = new LogStoreOptions { Capacity = LogStoreOptions.MaxCapacity + 5 }.Clamp();

        Assert.That(clamped.Capacity, Is.EqualTo(LogStoreOptions.MaxCapacity));
    }

    [Test]
    public void Snapshot_EmptyStoreHasNoEventsAndZeroVersion()
    {
        var snapshot = NewStore().Snapshot();

        Assert.That(snapshot.Count, Is.Zero);
        Assert.That(snapshot.Version, Is.Zero);
        Assert.That(snapshot.Events, Is.Empty);
    }

    [Test]
    public void Snapshot_ReturnsEventsOldestFirst()
    {
        var store = NewStore();
        store.Append(Draft(message: "a"));
        store.Append(Draft(message: "b"));

        var snapshot = store.Snapshot();

        Assert.That(snapshot.Events.Select(e => e.Message), Is.EqualTo(new[] { "a", "b" }));
    }

    [Test]
    public void Snapshot_SearchKeysAlignWithEvents()
    {
        var store = NewStore();
        store.Append(Draft(message: "Download FAILED"));

        var snapshot = store.Snapshot();

        Assert.That(snapshot.SearchKeys, Has.Count.EqualTo(snapshot.Events.Count));
        Assert.That(snapshot.SearchKeys[0], Does.Contain("download failed"));
    }

    [Test]
    public void Snapshot_VersionIncreasesPerAppend()
    {
        var store = NewStore();
        var firstVersion = store.Version;

        store.Append(Draft());
        store.Append(Draft());

        Assert.That(store.Version, Is.GreaterThanOrEqualTo(firstVersion + 2));
    }

    [Test]
    public void Clear_ResetsCountButKeepsSequenceMonotonic()
    {
        var store = NewStore();
        store.Append(Draft());
        store.Append(Draft());
        var versionBeforeClear = store.Version;

        store.Clear();
        var afterClear = store.Append(Draft());

        Assert.That(store.Count, Is.EqualTo(1));
        Assert.That(afterClear.Seq, Is.EqualTo(3));
        Assert.That(store.Version, Is.GreaterThan(versionBeforeClear));
    }

    [Test]
    public void Changed_ReportsAddedLastSeqAndClear()
    {
        var store = NewStore();
        var events = new List<LogStoreChangedEventArgs>();
        store.Changed += (_, args) => events.Add(args);

        store.Append(Draft());
        store.Clear();

        Assert.That(events, Has.Count.EqualTo(2));
        Assert.That(events[0].Added, Is.EqualTo(1));
        Assert.That(events[0].LastSeq, Is.EqualTo(1));
        Assert.That(events[0].Cleared, Is.False);
        Assert.That(events[1].Cleared, Is.True);
    }

    [Test]
    public void Changed_ReportsEvictionWhenRingIsFull()
    {
        var store = NewStore(capacity: 100);
        for (var i = 0; i < 100; i++)
        {
            store.Append(Draft());
        }

        LogStoreChangedEventArgs? captured = null;
        store.Changed += (_, args) => captured = args;
        store.Append(Draft());

        Assert.That(captured, Is.Not.Null);
        Assert.That(captured!.Evicted, Is.EqualTo(1));
    }

    [Test]
    public void AppendRange_AddsAllAndFiresOnce()
    {
        var store = NewStore();
        var fired = 0;
        store.Changed += (_, _) => fired++;

        var added = store.AppendRange([Draft(message: "a"), Draft(message: "b"), Draft(message: "c")]);

        Assert.That(added, Is.EqualTo(3));
        Assert.That(store.Count, Is.EqualTo(3));
        Assert.That(fired, Is.EqualTo(1));
    }

    [Test]
    public void AppendRange_EmptyInputDoesNotFireEvent()
    {
        var store = NewStore();
        var fired = 0;
        store.Changed += (_, _) => fired++;

        var added = store.AppendRange([]);

        Assert.That(added, Is.Zero);
        Assert.That(fired, Is.Zero);
    }

    [Test]
    public void Append_ClassifierAssignsFamily()
    {
        var store = NewStore(new StubClassifier(LogFamily.Net));

        var stored = store.Append(Draft(message: "socket hang up"));

        Assert.That(stored.Family, Is.EqualTo(LogFamily.Net));
    }

    [Test]
    public void Append_ClassifierOtherKeepsExplicitFamily()
    {
        // 规则表返回 Other 只代表「没认出来」，不该抹掉采集侧已知的族（例如旧日志导入）。
        var store = NewStore(new StubClassifier(LogFamily.Other));

        var stored = store.Append(Draft() with { Family = LogFamily.Download });

        Assert.That(stored.Family, Is.EqualTo(LogFamily.Download));
    }

    [Test]
    public void Append_WithoutClassifierKeepsDraftFamily()
    {
        var store = NewStore();

        var stored = store.Append(Draft() with { Family = LogFamily.Fs });

        Assert.That(stored.Family, Is.EqualTo(LogFamily.Fs));
    }

    [Test]
    public void Append_NullDraftThrows()
    {
        var store = NewStore();

        Assert.That(() => store.Append(null!), Throws.ArgumentNullException);
    }

    [Test]
    public void DescribeOccupancy_ShowsCountOverCapacity()
    {
        var store = NewStore(capacity: 100);
        store.Append(Draft());

        Assert.That(store.DescribeOccupancy(), Is.EqualTo("1/100"));
    }
}
