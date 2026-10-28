namespace CkanUI.Services.Abstractions.Logs;

/// <summary>
/// 日志级别（14 篇 §3.1）。比 log4net 的 Level 多一个 <see cref="User"/>：
/// 它承载 CKAN Core 通过 <c>IUser.RaiseMessage/RaiseError</c> 的流式消息，
/// 语义上不是「代码写死的日志」而是「用户可见的过程播报」，所以必须能与 log4net 级别并列过滤、独立着色。
/// </summary>
public enum LogLevel
{
    /// <summary>全部细节（默认不采集，避免 Core 的 DEBUG 淹没缓冲区）。</summary>
    Trace = 0,

    /// <summary>调试细节。</summary>
    Debug = 1,

    /// <summary>常规信息。</summary>
    Info = 2,

    /// <summary>警告：不影响整体流程，但用户该知道。</summary>
    Warn = 3,

    /// <summary>错误：某个操作失败。</summary>
    Error = 4,

    /// <summary>致命：进程已不可继续。</summary>
    Fatal = 5,

    /// <summary>Core 的 IUser 消息（级别语义由调用方的 RaiseMessage/RaiseError 决定，这里只区分「来自用户通道」）。</summary>
    User = 6,
}
