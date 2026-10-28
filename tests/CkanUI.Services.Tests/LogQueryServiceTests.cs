using System.Diagnostics;

using CkanUI.Services.Abstractions.Logs;
using CkanUI.Services.Logs;
using NUnit.Framework;

namespace CkanUI.Services.Tests;

/// <summary>LOG-04 查询与聚合（14 篇 §4.2 ③④⑤ / §9.1 T-LOG-02 性能门禁）。</summary>
[TestFixture]
sealed class LogQueryServiceTests
{
    private static LogStore NewStore(int capacity = 500) =>
        new(null, new LogStoreOptions { Capacity = capacity });

    private static LogEvent Add(
        LogStore store,
        string message,
        LogLevel level = LogLevel.Info,
        LogFamily family = LogFamily.Other,
        LogSourceKind kind = LogSourceKind.Log4Net,
        string? detail = null,
        DateTime? time = null,
        IReadOnlyDictionary<string, string>? tags = null) =>
        store.Append(new LogEvent
        {
            Message = message,
            Detail = detail,
            Level = level,
            Family = family,
            Kind = kind,
            Source = LogSources.Encode(kind, "CKAN.Module"),
            TimeLocal = time ?? default,
            Tags = tags,
        });

    [Test]
    public void Query_EmptyStoreReturnsEmpty()
    {
        var service = new LogQueryService(NewStore());

        var result = service.Query(LogQuery.All);

        Assert.That(result.Events, Is.Empty);
        Assert.That(result.TotalMatches, Is.Zero);
        Assert.That(result.HasMore, Is.False);
    }

    [Test]
    public void Query_ReturnsNewestFirstByDefault()
    {
        var store = NewStore();
        Add(store, "first");
        Add(store, "second");
        Add(store, "third");

        var result = new LogQueryService(store).Query(LogQuery.All);

        Assert.That(result.Events.Select(e => e.Message), Is.EqualTo(new[] { "third", "second", "first" }));
    }

    [Test]
    public void Query_ReturnsOldestFirstWhenRequested()
    {
        var store = NewStore();
        Add(store, "first");
        Add(store, "second");

        var result = new LogQueryService(store).Query(new LogQuery { NewestFirst = false });

        Assert.That(result.Events.Select(e => e.Message), Is.EqualTo(new[] { "first", "second" }));
    }

    [Test]
    public void Query_TextFilterIsCaseInsensitive()
    {
        var store = NewStore();
        Add(store, "Download FAILED for RealChute");
        Add(store, "install complete");

        var result = new LogQueryService(store).Query(LogSearchSyntax.Parse("download failed"));

        Assert.That(result.Events, Has.Count.EqualTo(1));
        Assert.That(result.Events[0].Message, Does.Contain("RealChute"));
    }

    [Test]
    public void Query_TextFilterMatchesDetail()
    {
        var store = NewStore();
        Add(store, "Install failed", detail: "System.Exception: SHA256 mismatch");
        Add(store, "nothing here");

        var result = new LogQueryService(store).Query(LogSearchSyntax.Parse("sha256"));

        Assert.That(result.Events, Has.Count.EqualTo(1));
    }

    [Test]
    public void Query_TextFilterMatchesLevelLabel()
    {
        var store = NewStore();
        Add(store, "boom", level: LogLevel.Error);
        Add(store, "fine");

        var result = new LogQueryService(store).Query(LogSearchSyntax.Parse("error"));

        Assert.That(result.Events.Select(e => e.Message), Is.EqualTo(new[] { "boom" }));
    }

    [Test]
    public void Query_TextFilterMatchesFamilyName()
    {
        var store = NewStore();
        Add(store, "boom", family: LogFamily.Download);
        Add(store, "fine", family: LogFamily.Install);

        var result = new LogQueryService(store).Query(LogSearchSyntax.Parse("download"));

        Assert.That(result.Events.Select(e => e.Message), Is.EqualTo(new[] { "boom" }));
    }

    [Test]
    public void Query_LevelFilter()
    {
        var store = NewStore();
        Add(store, "a", level: LogLevel.Error);
        Add(store, "b", level: LogLevel.Warn);
        Add(store, "c", level: LogLevel.Info);

        var result = new LogQueryService(store).Query(new LogQuery { Levels = [LogLevel.Error, LogLevel.Warn] });

        Assert.That(result.Events.Select(e => e.Message), Is.EqualTo(new[] { "b", "a" }));
    }

    [Test]
    public void Query_FamilyFilter()
    {
        var store = NewStore();
        Add(store, "a", family: LogFamily.Download);
        Add(store, "b", family: LogFamily.Net);

        var result = new LogQueryService(store).Query(new LogQuery { Families = [LogFamily.Download] });

        Assert.That(result.Events.Select(e => e.Message), Is.EqualTo(new[] { "a" }));
    }

    [Test]
    public void Query_KindFilter()
    {
        var store = NewStore();
        Add(store, "core line", kind: LogSourceKind.Log4Net);
        Add(store, "user line", kind: LogSourceKind.User);

        var result = new LogQueryService(store).Query(LogSearchSyntax.Parse("源:user"));

        Assert.That(result.Events.Select(e => e.Message), Is.EqualTo(new[] { "user line" }));
    }

    [Test]
    public void Query_TagFilter()
    {
        var store = NewStore();
        Add(store, "with tag", tags: new Dictionary<string, string> { ["mod"] = "RealChute" });
        Add(store, "without tag");

        var result = new LogQueryService(store).Query(LogSearchSyntax.Parse("mod:RealChute"));

        Assert.That(result.Events.Select(e => e.Message), Is.EqualTo(new[] { "with tag" }));
    }

    [Test]
    public void Query_CombinedFilters()
    {
        var store = NewStore();
        Add(store, "connection reset", level: LogLevel.Error, family: LogFamily.Net);
        Add(store, "connection reset", level: LogLevel.Info, family: LogFamily.Net);
        Add(store, "connection reset", level: LogLevel.Error, family: LogFamily.Fs);

        var result = new LogQueryService(store).Query(LogSearchSyntax.Parse("级别:error 族:net connection"));

        Assert.That(result.Events, Has.Count.EqualTo(1));
        Assert.That(result.Events[0].Level, Is.EqualTo(LogLevel.Error));
        Assert.That(result.Events[0].Family, Is.EqualTo(LogFamily.Net));
    }

    [Test]
    public void Query_SkipAndTake()
    {
        var store = NewStore();
        for (var i = 0; i < 10; i++)
        {
            Add(store, "line" + i);
        }

        var result = new LogQueryService(store).Query(new LogQuery { Skip = 2, Take = 3 });

        Assert.That(result.Events.Select(e => e.Message), Is.EqualTo(new[] { "line7", "line6", "line5" }));
        Assert.That(result.TotalMatches, Is.EqualTo(10));
        Assert.That(result.HasMore, Is.True);
    }

    [Test]
    public void Query_TotalMatchesCountsBeyondTake()
    {
        var store = NewStore();
        for (var i = 0; i < 20; i++)
        {
            Add(store, "line" + i);
        }

        var result = new LogQueryService(store).Query(new LogQuery { Take = 5 });

        Assert.That(result.Events, Has.Count.EqualTo(5));
        Assert.That(result.TotalMatches, Is.EqualTo(20));
    }

    [Test]
    public void Aggregate_CountsByLevelFamilyAndSource()
    {
        var store = NewStore();
        Add(store, "a", level: LogLevel.Error, family: LogFamily.Net, kind: LogSourceKind.Log4Net);
        Add(store, "b", level: LogLevel.Warn, family: LogFamily.Net, kind: LogSourceKind.User);
        Add(store, "c", level: LogLevel.Info, family: LogFamily.Install, kind: LogSourceKind.User);

        var aggregate = new LogQueryService(store).Aggregate(LogQuery.All);

        Assert.That(aggregate.Total, Is.EqualTo(3));
        Assert.That(aggregate.Count(LogLevel.Error), Is.EqualTo(1));
        Assert.That(aggregate.Count(LogFamily.Net), Is.EqualTo(2));
        Assert.That(aggregate.Count(LogSourceKind.User), Is.EqualTo(2));
        Assert.That(aggregate.ErrorCount, Is.EqualTo(1));
        Assert.That(aggregate.WarningCount, Is.EqualTo(1));
    }

    [Test]
    public void Aggregate_FatalCountsAsError()
    {
        var store = NewStore();
        Add(store, "a", level: LogLevel.Fatal);
        Add(store, "b", level: LogLevel.Error);

        var aggregate = new LogQueryService(store).Aggregate(LogQuery.All);

        Assert.That(aggregate.ErrorCount, Is.EqualTo(2));
    }

    [Test]
    public void Aggregate_TimelineCoversEveryMatch()
    {
        var store = NewStore();
        var start = new DateTime(2024, 5, 17, 9, 0, 0, DateTimeKind.Local);
        Add(store, "a", level: LogLevel.Error, time: start);
        Add(store, "b", level: LogLevel.Warn, time: start.AddMinutes(1));
        Add(store, "c", level: LogLevel.Info, time: start.AddMinutes(2));

        var aggregate = new LogQueryService(store).Aggregate(LogQuery.All, timelineBuckets: 10);

        Assert.That(aggregate.Timeline, Has.Count.EqualTo(10));
        Assert.That(aggregate.Timeline.Sum(b => b.Total), Is.EqualTo(3));
        Assert.That(aggregate.Timeline.Sum(b => b.ErrorCount), Is.EqualTo(1));
        Assert.That(aggregate.Timeline.Sum(b => b.WarningCount), Is.EqualTo(1));
        Assert.That(aggregate.Timeline[0].StartLocal, Is.EqualTo(start));
    }

    [Test]
    public void Aggregate_SingleEventStillProducesTimeline()
    {
        var store = NewStore();
        Add(store, "only");

        var aggregate = new LogQueryService(store).Aggregate(LogQuery.All, timelineBuckets: 5);

        Assert.That(aggregate.Timeline, Has.Count.EqualTo(5));
        Assert.That(aggregate.Timeline.Sum(b => b.Total), Is.EqualTo(1));
    }

    [Test]
    public void Aggregate_GroupsRepeatedMessagesByFingerprint()
    {
        var store = NewStore();
        // 数字不同但骨架相同：聚合视图要把它们折叠成 ×N。
        Add(store, "Failed to install mod 12", level: LogLevel.Error, family: LogFamily.Install);
        Add(store, "Failed to install mod 34", level: LogLevel.Error, family: LogFamily.Install);
        Add(store, "Something else", level: LogLevel.Info, family: LogFamily.Ui);

        var aggregate = new LogQueryService(store).Aggregate(LogQuery.All);

        Assert.That(aggregate.TopFingerprints, Has.Count.EqualTo(2));
        var top = aggregate.TopFingerprints[0];
        Assert.That(top.Count, Is.EqualTo(2));
        Assert.That(top.Level, Is.EqualTo(LogLevel.Error));
        Assert.That(top.Family, Is.EqualTo(LogFamily.Install));
        Assert.That(top.Sample, Is.EqualTo("Failed to install mod 12"));
        Assert.That(top.FirstSeq, Is.EqualTo(1));
        Assert.That(top.LastSeq, Is.EqualTo(2));
    }

    [Test]
    public void Aggregate_TopFingerprintsHonorsTake()
    {
        var store = NewStore();
        foreach (var word in new[] { "alpha", "bravo", "charlie", "delta", "echo" })
        {
            Add(store, "distinct " + word);
        }

        var aggregate = new LogQueryService(store).Aggregate(LogQuery.All, topFingerprints: 2);

        Assert.That(aggregate.TopFingerprints, Has.Count.EqualTo(2));
    }

    [Test]
    public void Fingerprints_ReturnsGroupsOnly()
    {
        var store = NewStore();
        Add(store, "boom 1", level: LogLevel.Error);
        Add(store, "boom 2", level: LogLevel.Error);

        var groups = new LogQueryService(store).Fingerprints(LogQuery.All);

        Assert.That(groups, Has.Count.EqualTo(1));
        Assert.That(groups[0].Count, Is.EqualTo(2));
    }

    [Test]
    public void Aggregate_EmptyStoreReturnsEmpty()
    {
        var aggregate = new LogQueryService(NewStore()).Aggregate(LogQuery.All);

        Assert.That(aggregate, Is.SameAs(LogAggregateResult.Empty));
        Assert.That(aggregate.Total, Is.Zero);
    }

    [Test]
    public void Aggregate_RespectsQueryFilters()
    {
        var store = NewStore();
        Add(store, "net error", level: LogLevel.Error, family: LogFamily.Net);
        Add(store, "install info", level: LogLevel.Info, family: LogFamily.Install);

        var aggregate = new LogQueryService(store).Aggregate(new LogQuery { Levels = [LogLevel.Error] });

        Assert.That(aggregate.Total, Is.EqualTo(1));
        Assert.That(aggregate.Count(LogFamily.Net), Is.EqualTo(1));
        Assert.That(aggregate.Count(LogFamily.Install), Is.Zero);
    }

    [Test]
    public void Snapshot_ExposesLatestState()
    {
        var store = NewStore();
        Add(store, "a");

        var snapshot = new LogQueryService(store).Snapshot();

        Assert.That(snapshot.Count, Is.EqualTo(1));
    }

    /// <summary>
    /// 14 篇 §9.1 T-LOG-02：10 万行下查询 p99 必须落在 60fps 帧预算（16.7ms）内。
    /// 用真实规模而不是 2 万行：环形缓冲默认 20k，但用户调大容量后排障期间会到 10 万级。
    /// </summary>
    [Test]
    [NonParallelizable] // 16 个测试 worker 并行时测帧预算没有意义：这条要独占进程跑
    public void Perf_QueryOverHundredThousandLinesStaysWithinFrameBudget()
    {
        var store = NewStore(capacity: 100_000);
        for (var i = 0; i < 100_000; i++)
        {
            // 每 10 行一个命中项：既测全表扫描，也测「命中多但只取 500 行」的路径。
            Add(
                store,
                i % 10 == 0
                    ? "needle line " + i + " RealChute install failure"
                    : "filler line " + i + " transaction step",
                level: i % 7 == 0 ? LogLevel.Error : LogLevel.Info,
                family: i % 3 == 0 ? LogFamily.Install : LogFamily.Other);
        }

        var service = new LogQueryService(store);
        var query = LogSearchSyntax.Parse("needle");

        for (var i = 0; i < 5; i++)
        {
            service.Query(query);
        }

        const int Iterations = 40;
        var samples = new double[Iterations];
        for (var i = 0; i < Iterations; i++)
        {
            var watch = Stopwatch.StartNew();
            var result = service.Query(query);
            watch.Stop();
            Assert.That(result.TotalMatches, Is.EqualTo(10_000));
            samples[i] = watch.Elapsed.TotalMilliseconds;
        }

        Array.Sort(samples);
        var p99 = samples[(int)Math.Ceiling(Iterations * 0.99) - 1];
        var median = samples[Iterations / 2];
        TestContext.Out.WriteLine($"10 万行查询：median={median:F2}ms p99={p99:F2}ms");

        Assert.That(p99, Is.LessThanOrEqualTo(16.7), $"10 万行查询 p99={p99:F2}ms 超出 16.7ms 帧预算");
    }

    [Test]
    [NonParallelizable]
    public void Perf_AggregateOverHundredThousandLinesCompletes()
    {
        var store = NewStore(capacity: 100_000);
        for (var i = 0; i < 100_000; i++)
        {
            Add(store, "aggregate line " + i, level: i % 5 == 0 ? LogLevel.Error : LogLevel.Info);
        }

        var service = new LogQueryService(store);
        var watch = Stopwatch.StartNew();
        var aggregate = service.Aggregate(LogQuery.All, timelineBuckets: 60, topFingerprints: 10);
        watch.Stop();

        TestContext.Out.WriteLine($"10 万行聚合：{watch.Elapsed.TotalMilliseconds:F2}ms");
        Assert.That(aggregate.Total, Is.EqualTo(100_000));
        Assert.That(aggregate.Timeline, Has.Count.EqualTo(60));
    }
}
