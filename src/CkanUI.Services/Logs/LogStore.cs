using System.Globalization;

using CkanUI.Services.Abstractions.Logs;

namespace CkanUI.Services.Logs;

/// <summary>
/// 环形缓冲实现（14 篇 §8 LOG-02）。
/// 设计要点：
/// 1. 环形数组而不是 <c>LinkedList</c>：容量固定、无 GC 抖动，且淘汰最旧一条是 O(1)（用户只关心最近发生了什么）。
/// 2. 搜索串在写入时算一次并缓存，查询侧只做 IndexOf——10 万行的 p99 预算只有 16.7ms。
/// 3. 序号（Seq）单调递增且清空不回退：AI 结论要按 Seq 回指「证据行」，回退会让旧结论指向新日志。
/// </summary>
public sealed class LogStore : ILogStore
{
    private readonly Lock _gate = new();
    private readonly ILogClassifier? _classifier;
    private readonly LogEvent?[] _ring;
    private readonly string[] _keys;

    private int _head;
    private int _count;
    private long _lastSeq;
    private long _version;

    /// <summary>构造存储。<paramref name="classifier"/> 为 null 时族取调用方给的值（测试/极简场景）。</summary>
    public LogStore(ILogClassifier? classifier, LogStoreOptions? options = null)
    {
        Options = (options ?? LogStoreOptions.Default).Clamp();
        _classifier = classifier;
        _ring = new LogEvent?[Options.Capacity];
        _keys = new string[Options.Capacity];
    }

    /// <inheritdoc />
    public LogStoreOptions Options { get; }

    /// <inheritdoc />
    public int Count
    {
        get
        {
            lock (_gate)
            {
                return _count;
            }
        }
    }

    /// <inheritdoc />
    public long LastSeq
    {
        get
        {
            lock (_gate)
            {
                return _lastSeq;
            }
        }
    }

    /// <inheritdoc />
    public long Version
    {
        get
        {
            lock (_gate)
            {
                return _version;
            }
        }
    }

    /// <inheritdoc />
    public event EventHandler<LogStoreChangedEventArgs>? Changed;

    /// <inheritdoc />
    public LogEvent Append(LogEvent draft)
    {
        ArgumentNullException.ThrowIfNull(draft);
        LogEvent stored;
        LogStoreChangedEventArgs args;
        lock (_gate)
        {
            stored = Normalize(draft, out var evicted);
            Write(stored);
            args = new LogStoreChangedEventArgs(1, _lastSeq, false, evicted ? 1 : 0);
        }

        Changed?.Invoke(this, args);
        return stored;
    }

    /// <inheritdoc />
    public int AppendRange(IEnumerable<LogEvent> drafts)
    {
        ArgumentNullException.ThrowIfNull(drafts);
        var added = 0;
        var evicted = 0;
        long lastSeq;
        lock (_gate)
        {
            foreach (var draft in drafts)
            {
                if (draft is null)
                {
                    continue;
                }

                var stored = Normalize(draft, out var evictedOne);
                Write(stored);
                added++;
                if (evictedOne)
                {
                    evicted++;
                }
            }

            lastSeq = _lastSeq;
        }

        if (added > 0)
        {
            Changed?.Invoke(this, new LogStoreChangedEventArgs(added, lastSeq, false, evicted));
        }

        return added;
    }

    /// <inheritdoc />
    public LogSnapshot Snapshot()
    {
        lock (_gate)
        {
            if (_count == 0)
            {
                return new LogSnapshot(Array.Empty<LogEvent>(), Array.Empty<string>(), _version);
            }

            var events = new LogEvent[_count];
            var keys = new string[_count];
            var start = _head - _count;
            if (start < 0)
            {
                start += Options.Capacity;
            }

            for (var i = 0; i < _count; i++)
            {
                var index = start + i;
                if (index >= Options.Capacity)
                {
                    index -= Options.Capacity;
                }

                // ring 在该下标上必定有值（count 只统计已写入槽位），用 ! 断言避免每行都判空。
                events[i] = _ring[index]!;
                keys[i] = _keys[index];
            }

            return new LogSnapshot(events, keys, _version);
        }
    }

    /// <inheritdoc />
    public void Clear()
    {
        int cleared;
        long lastSeq;
        lock (_gate)
        {
            cleared = _count;
            Array.Clear(_ring, 0, _ring.Length);
            Array.Clear(_keys, 0, _keys.Length);
            _head = 0;
            _count = 0;
            _version++;
            lastSeq = _lastSeq;
        }

        Changed?.Invoke(this, new LogStoreChangedEventArgs(0, lastSeq, true, 0));
    }

    /// <summary>补全序号/时间/族，返回可入队的事件。</summary>
    private LogEvent Normalize(LogEvent draft, out bool evicted)
    {
        evicted = _count == Options.Capacity;
        _lastSeq++;
        var sequence = _lastSeq;
        var time = draft.TimeLocal == default ? DateTime.Now : draft.TimeLocal;

        // 采集侧可以显式给族（例如旧日志导入、UI 事件），否则交给规则表。
        // 规则表返回 Other 时不覆盖显式族：Other 是「没认出来」，不是「确定无关」。
        var family = draft.Family;
        if (_classifier is not null)
        {
            var classified = _classifier.Classify(draft.Kind, draft.Level, draft.Source, draft.Message, draft.Detail);
            if (classified != LogFamily.Other || family == LogFamily.Other)
            {
                family = classified;
            }
        }

        return (draft.Seq == sequence && draft.TimeLocal == time && draft.Family == family)
            ? draft
            : draft with { Seq = sequence, TimeLocal = time, Family = family };
    }

    private void Write(LogEvent stored)
    {
        _ring[_head] = stored;
        _keys[_head] = LogSearchKeys.Build(stored);
        _head++;
        if (_head == Options.Capacity)
        {
            _head = 0;
        }

        if (_count < Options.Capacity)
        {
            _count++;
        }

        _version++;
    }

    /// <summary>诊断用：当前占用率（UI 可在缓冲将满时提示「早期日志会被挤出」）。</summary>
    public string DescribeOccupancy() => string.Create(
        CultureInfo.InvariantCulture,
        $"{_count}/{Options.Capacity}");
}
