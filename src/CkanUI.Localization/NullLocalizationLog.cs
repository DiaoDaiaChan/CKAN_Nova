namespace CkanUI.Localization;

/// <summary>丢弃日志的默认实现（headless 测试与未接线场景用，绝不抛异常）。</summary>
public sealed class NullLocalizationLog : ILocalizationLog
{
    /// <summary>全局单例：无状态、可安全共享。</summary>
    public static NullLocalizationLog Instance { get; } = new();

    /// <inheritdoc />
    public void Warn(string message)
    {
        // 有意丢弃：未接线的调用方不该因为"没有日志"而失败。
    }
}
