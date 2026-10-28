namespace CkanUI.Services.Abstractions.Logs;

/// <summary>
/// 存储变更通知（供 UI 做增量刷新与徽标计数）。
/// 只带「变了多少」而不是整批事件：订阅者（UI 线程）不该为了刷新徽标去搬运 20k 条引用。
/// </summary>
public sealed class LogStoreChangedEventArgs : EventArgs
{
    /// <summary>构造变更通知。</summary>
    public LogStoreChangedEventArgs(int added, long lastSeq, bool cleared, int evicted)
    {
        Added = added;
        LastSeq = lastSeq;
        Cleared = cleared;
        Evicted = evicted;
    }

    /// <summary>本次新增条数。</summary>
    public int Added { get; }

    /// <summary>新增后最新序号。</summary>
    public long LastSeq { get; }

    /// <summary>本次是否发生了清空。</summary>
    public bool Cleared { get; }

    /// <summary>本次因容量上限被挤出的旧条数（诊断包/UI 可以提示「早期日志已被挤出」）。</summary>
    public int Evicted { get; }
}
