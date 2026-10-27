namespace CkanUI.Localization.Tests;

/// <summary>测试用日志出口：收下 Warn 文本，用于断言"缺 key 记 Warn"等规格要求。</summary>
public sealed class RecordingLocalizationLog : ILocalizationLog
{
    private readonly List<string> _messages = [];
    private readonly object _gate = new();

    /// <summary>已记录的告警文本（快照，线程安全）。</summary>
    public IReadOnlyList<string> Messages
    {
        get
        {
            lock (_gate)
            {
                return [.. _messages];
            }
        }
    }

    /// <summary>告警条数。</summary>
    public int Count
    {
        get
        {
            lock (_gate)
            {
                return _messages.Count;
            }
        }
    }

    /// <inheritdoc />
    public void Warn(string message)
    {
        lock (_gate)
        {
            _messages.Add(message);
        }
    }

    /// <summary>是否有一条告警包含指定片段。</summary>
    /// <param name="fragment">要找的片段。</param>
    public bool Contains(string fragment) =>
        Messages.Any(m => m.Contains(fragment, StringComparison.Ordinal));
}
