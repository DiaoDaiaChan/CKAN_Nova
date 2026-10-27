namespace CkanUI.ViewModels.Operations;

/// <summary>生产用单调时钟：直接取 <see cref="System.Environment.TickCount64"/>，不受系统时间回拨影响。</summary>
public sealed class MonotonicOperationClock : IOperationClock
{
    /// <summary>进程级共享实例（无状态，线程安全）。</summary>
    public static MonotonicOperationClock Instance { get; } = new();

    /// <inheritdoc />
    public long TimestampMilliseconds => System.Environment.TickCount64;
}
