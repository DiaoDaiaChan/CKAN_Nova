using CkanUI.Services.Abstractions.Logs;

namespace CkanUI.Services.Logs;

/// <summary>
/// 日志查询与聚合（14 篇 §4.2 ③④⑤ / LOG-04）。
/// 为什么查询走「快照 + 线性扫描」而不是给环形缓冲建索引：
/// 20k 行 × 数十纳秒的行匹配在 60fps 预算内（16.7ms）绰绰有余，
/// 而索引的维护成本（每次 Append 更新倒排表）会直接压在采集热路径上，得不偿失。
/// </summary>
public sealed class LogQueryService : ILogQueryService
{
    private readonly ILogStore _store;
    private LogSnapshot? _cached;
    private long _cachedVersion = -1;

    /// <summary>构造查询服务。</summary>
    public LogQueryService(ILogStore store)
    {
        ArgumentNullException.ThrowIfNull(store);
        _store = store;
    }

    /// <inheritdoc />
    public LogSnapshot Snapshot() => GetSnapshot();

    /// <summary>
    /// 取快照并缓存：一次查询要扫全表，复制 10 万行的代价（两次大数组分配）会直接吃掉 16.7ms 预算。
    /// 用 store 的版本号做失效判断——没有新日志到达时（用户在翻页/筛选）复用同一份快照；
    /// 有写入则重新复制。版本读两次的写法允许良性竞态：读到旧版本最多多复制一次，不会错数据。
    /// </summary>
    private LogSnapshot GetSnapshot()
    {
        var version = _store.Version;
        var cached = Volatile.Read(ref _cached);
        if (cached is not null && _cachedVersion == version)
        {
            return cached;
        }

        var snapshot = _store.Snapshot();
        Volatile.Write(ref _cached, snapshot);
        _cachedVersion = snapshot.Version;
        return snapshot;
    }

    /// <inheritdoc />
    public LogQueryResult Query(LogQuery query)
    {
        ArgumentNullException.ThrowIfNull(query);
        var snapshot = GetSnapshot();
        if (snapshot.Count == 0)
        {
            return LogQueryResult.Empty;
        }

        // 筛选条件一次性摊平成数组再进循环：满载 10 万行时，每行访问一次
        // IReadOnlyCollection 的属性并走一次枚举器，是 Debug 构建下最主要的额外开销。
        var plan = QueryPlan.Create(query);
        var take = query.SafeTake;
        var skip = query.SafeSkip;
        var events = new List<LogEvent>(Math.Min(take, snapshot.Count));
        var total = 0;

        if (query.NewestFirst)
        {
            for (var i = snapshot.Count - 1; i >= 0; i--)
            {
                if (!plan.Matches(snapshot, i))
                {
                    continue;
                }

                if (total >= skip && events.Count < take)
                {
                    events.Add(snapshot.Events[i]);
                }

                total++;
            }
        }
        else
        {
            for (var i = 0; i < snapshot.Count; i++)
            {
                if (!plan.Matches(snapshot, i))
                {
                    continue;
                }

                if (total >= skip && events.Count < take)
                {
                    events.Add(snapshot.Events[i]);
                }

                total++;
            }
        }

        return new LogQueryResult(events, total);
    }

    /// <inheritdoc />
    public LogAggregateResult Aggregate(LogQuery query, int timelineBuckets = 60, int topFingerprints = 10)
    {
        ArgumentNullException.ThrowIfNull(query);
        var snapshot = GetSnapshot();
        if (snapshot.Count == 0)
        {
            return LogAggregateResult.Empty;
        }

        var plan = QueryPlan.Create(query);
        var byLevel = new Dictionary<LogLevel, int>();
        var byFamily = new Dictionary<LogFamily, int>();
        var bySource = new Dictionary<LogSourceKind, int>();
        var fingerprints = new Dictionary<string, FingerprintAccumulator>(StringComparer.Ordinal);
        long firstTick = long.MaxValue;
        long lastTick = long.MinValue;
        var total = 0;

        for (var i = 0; i < snapshot.Count; i++)
        {
            if (!plan.Matches(snapshot, i))
            {
                continue;
            }

            var item = snapshot.Events[i];
            total++;
            Increment(byLevel, item.Level);
            Increment(byFamily, item.Family);
            Increment(bySource, item.Kind);

            var ticks = item.TimeLocal.Ticks;
            firstTick = Math.Min(firstTick, ticks);
            lastTick = Math.Max(lastTick, ticks);

            var key = BuildFingerprintKey(item);
            if (fingerprints.TryGetValue(key, out var accumulator))
            {
                accumulator.Add(item);
            }
            else
            {
                fingerprints[key] = new FingerprintAccumulator(item);
            }
        }

        if (total == 0)
        {
            return LogAggregateResult.Empty;
        }

        var buckets = Math.Clamp(timelineBuckets, 1, 600);
        var timeline = BuildTimeline(snapshot, plan, firstTick, lastTick, buckets);
        var top = TopGroups(fingerprints, Math.Max(topFingerprints, 0));

        return new LogAggregateResult(total, byLevel, byFamily, bySource, timeline, top);
    }

    /// <inheritdoc />
    public IReadOnlyList<LogFingerprintGroup> Fingerprints(LogQuery query, int take = 10) =>
        Aggregate(query, timelineBuckets: 1, topFingerprints: take).TopFingerprints;

    /// <summary>聚合指纹键：级别 + 族 + 消息骨架（数字/路径替换为占位）。</summary>
    private static string BuildFingerprintKey(LogEvent item) =>
        string.Concat(LogLevels.Label(item.Level), "|", item.Family.ToString(), "|", LogSearchKeys.Skeletonize(item.Message));

    private static List<LogTimelineBucket> BuildTimeline(
        LogSnapshot snapshot,
        QueryPlan plan,
        long firstTick,
        long lastTick,
        int buckets)
    {
        var span = Math.Max(lastTick - firstTick, 1);
        var width = Math.Max(span / buckets, 1);
        var counters = new (int Errors, int Warnings, int Others)[buckets];
        var starts = new DateTime[buckets];
        var origin = new DateTime(firstTick, DateTimeKind.Local);

        for (var i = 0; i < snapshot.Count; i++)
        {
            if (!plan.Matches(snapshot, i))
            {
                continue;
            }

            var item = snapshot.Events[i];
            var index = (int)Math.Min((item.TimeLocal.Ticks - firstTick) / width, buckets - 1);
            var slot = counters[index];
            if (LogLevels.IsError(item.Level))
            {
                counters[index] = (slot.Errors + 1, slot.Warnings, slot.Others);
            }
            else if (LogLevels.IsWarning(item.Level))
            {
                counters[index] = (slot.Errors, slot.Warnings + 1, slot.Others);
            }
            else
            {
                counters[index] = (slot.Errors, slot.Warnings, slot.Others + 1);
            }
        }

        var result = new List<LogTimelineBucket>(buckets);
        for (var i = 0; i < buckets; i++)
        {
            starts[i] = origin.AddTicks(width * i);
            result.Add(new LogTimelineBucket(starts[i], counters[i].Errors, counters[i].Warnings, counters[i].Others));
        }

        return result;
    }

    private static List<LogFingerprintGroup> TopGroups(Dictionary<string, FingerprintAccumulator> fingerprints, int take)
    {
        if (take <= 0)
        {
            return [];
        }

        var ordered = new List<KeyValuePair<string, FingerprintAccumulator>>(fingerprints.Count);
        foreach (var pair in fingerprints)
        {
            ordered.Add(pair);
        }

        ordered.Sort(static (left, right) =>
        {
            var byCount = right.Value.Count.CompareTo(left.Value.Count);
            return byCount != 0 ? byCount : left.Value.FirstSeq.CompareTo(right.Value.FirstSeq);
        });

        var result = new List<LogFingerprintGroup>(Math.Min(take, ordered.Count));
        foreach (var pair in ordered)
        {
            if (result.Count >= take)
            {
                break;
            }

            result.Add(new LogFingerprintGroup(
                pair.Key,
                pair.Value.Level,
                pair.Value.Family,
                pair.Value.Sample,
                pair.Value.Count) { FirstSeq = pair.Value.FirstSeq, LastSeq = pair.Value.LastSeq });
        }

        return result;
    }

    private static void Increment<TKey>(Dictionary<TKey, int> counters, TKey key)
        where TKey : notnull =>
        counters[key] = counters.TryGetValue(key, out var current) ? current + 1 : 1;

    /// <summary>
    /// 一次查询的预编译筛选条件。为什么单列一个类：满载 10 万行时一次筛选要跑 10 万次，
    /// 把「集合是否为空」「关键词已小写化」这类固定判断提到循环外、把集合摊平成数组，
    /// 是唯一不需要额外倒排索引就能守住 16.7ms 帧预算的做法（索引的维护成本会压在采集热路径上）。
    /// </summary>
    private sealed class QueryPlan
    {
        private readonly LogLevel[]? _levels;
        private readonly LogFamily[]? _families;
        private readonly LogSourceKind[]? _kinds;
        private readonly KeyValuePair<string, string>[]? _tags;
        private readonly string _keyword;

        private QueryPlan(
            LogLevel[]? levels,
            LogFamily[]? families,
            LogSourceKind[]? kinds,
            KeyValuePair<string, string>[]? tags,
            string keyword)
        {
            _levels = levels;
            _families = families;
            _kinds = kinds;
            _tags = tags;
            _keyword = keyword;
        }

        public static QueryPlan Create(LogQuery query)
        {
            var levels = query.Levels;
            var families = query.Families;
            var kinds = query.Kinds;
            var tags = query.Tags;

            return new QueryPlan(
                levels is { Count: > 0 } ? [.. levels] : null,
                families is { Count: > 0 } ? [.. families] : null,
                kinds is { Count: > 0 } ? [.. kinds] : null,
                tags is { Count: > 0 } ? [.. tags] : null,
                query.NormalizedText);
        }

        /// <summary>单行匹配（顺序固定为级别→族→源→标签→关键词，先便宜后贵）。</summary>
        public bool Matches(LogSnapshot snapshot, int index)
        {
            var item = snapshot.Events[index];

            var levels = _levels;
            if (levels is not null && Array.IndexOf(levels, item.Level) < 0)
            {
                return false;
            }

            var families = _families;
            if (families is not null && Array.IndexOf(families, item.Family) < 0)
            {
                return false;
            }

            var kinds = _kinds;
            if (kinds is not null && Array.IndexOf(kinds, item.Kind) < 0)
            {
                return false;
            }

            var tags = _tags;
            if (tags is not null)
            {
                for (var i = 0; i < tags.Length; i++)
                {
                    var pair = tags[i];
                    if (!string.Equals(item.Tag(pair.Key), pair.Value, StringComparison.OrdinalIgnoreCase))
                    {
                        return false;
                    }
                }
            }

            var keyword = _keyword;
            return keyword.Length == 0
                || snapshot.SearchKeys[index].Contains(keyword, StringComparison.Ordinal);
        }
    }

    private sealed class FingerprintAccumulator
    {
        public FingerprintAccumulator(LogEvent first)
        {
            Level = first.Level;
            Family = first.Family;
            Sample = first.Message;
            FirstSeq = first.Seq;
            LastSeq = first.Seq;
            Count = 1;
        }

        public LogLevel Level { get; }

        public LogFamily Family { get; }

        public string Sample { get; }

        public long FirstSeq { get; }

        public long LastSeq { get; private set; }

        public int Count { get; private set; }

        public void Add(LogEvent item)
        {
            Count++;
            LastSeq = item.Seq;
        }
    }
}
