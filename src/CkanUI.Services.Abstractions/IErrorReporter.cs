namespace CkanUI.Services.Abstractions;

/// <summary>一条错误记录（错误条 + 日志 + 诊断复制共用）。</summary>
public sealed record ErrorEntry(
    DateTime Timestamp,
    string Context,
    string UserMessage,
    string? Detail,
    Exception? Exception);

/// <summary>统一异常上报（04 篇 §3 / ARC-04）：错误条展示 + 写日志，绝不吞异常。</summary>
public interface IErrorReporter
{
    event Action<ErrorEntry>? Reported;

    /// <summary>上报一个异常；不抛出。</summary>
    void Report(Exception exception, string context, string? userMessage = null);

    /// <summary>上报一条非异常错误。</summary>
    void Report(string context, string userMessage, string? detail = null);

    IReadOnlyList<ErrorEntry> Recent { get; }
}
