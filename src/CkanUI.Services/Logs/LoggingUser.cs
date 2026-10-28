using System.Globalization;

using CKAN;

using CkanUI.Services.Abstractions.Logs;

namespace CkanUI.Services.Logs;

/// <summary>
/// Core <see cref="IUser"/> 的日志装饰器（14 篇 §3.2 ②）。
/// 为什么用装饰器而不是改 <c>AvaloniaUser</c>：AvaloniaUser 是 UI 交互通道，日志是旁路关注点；
/// 装饰器可以只在需要时包一层，遇到问题能摘掉而不动交互逻辑（也避免两人同时改同一文件）。
/// </summary>
/// <remarks>
/// 进度（RaiseProgress）节流原因：下载进度每秒几十条，直接入流会把 20k 缓冲冲干净，
/// 让用户真正要找的错误被挤出——所以只在整十百分点或完成时记一条。
/// </remarks>
public sealed class LoggingUser : IUser
{
    private readonly IUser _inner;
    private readonly ILogStore _store;

    private int _lastProgressPercent = -1;

    /// <summary>构造装饰器。</summary>
    public LoggingUser(IUser inner, ILogStore store)
    {
        ArgumentNullException.ThrowIfNull(inner);
        ArgumentNullException.ThrowIfNull(store);
        _inner = inner;
        _store = store;
    }

    /// <inheritdoc />
    public bool Headless => _inner.Headless;

    /// <summary>被包装的原始通道（UI 需要读取 RecentMessage 等具体实现成员时用）。</summary>
    public IUser Inner => _inner;

    /// <inheritdoc />
    public bool RaiseYesNoDialog(string question) => _inner.RaiseYesNoDialog(question);

    /// <inheritdoc />
    public int RaiseSelectionDialog(string message, params object[] args) => _inner.RaiseSelectionDialog(message, args);

    /// <inheritdoc />
    public void RaiseError(string message, params object[] args)
    {
        var text = Format(message, args);
        Write(LogLevel.Error, text);
        _inner.RaiseError(message, args);
    }

    /// <inheritdoc />
    public void RaiseMessage(string message, params object[] args)
    {
        var text = Format(message, args);
        Write(LogLevel.Info, text);
        _inner.RaiseMessage(message, args);
    }

    /// <inheritdoc />
    public void RaiseProgress(string message, int percent)
    {
        // 只在跨过整十百分点或完成时记一条：既留下过程轨迹，又不淹没错误。
        var bucket = percent / 10;
        if (percent >= 100 || bucket != _lastProgressPercent)
        {
            _lastProgressPercent = bucket;
            Write(LogLevel.User, string.Create(CultureInfo.InvariantCulture, $"{percent}% {message}"));
        }

        _inner.RaiseProgress(message, percent);
    }

    /// <inheritdoc />
    public void RaiseProgress(ByteRateCounter rateCounter)
    {
        // 传输层已经通过 RaiseProgress(message, percent) 报了进度，这里不重复入流。
        _inner.RaiseProgress(rateCounter);
    }

    private void Write(LogLevel level, string text)
    {
        try
        {
            _store.Append(LogEvent.Create(
                LogSourceKind.User,
                level,
                LogSources.Prefix(LogSourceKind.User),
                text,
                null,
                Environment.CurrentManagedThreadId.ToString(CultureInfo.InvariantCulture)));
        }
        catch (Exception)
        {
            // 日志旁路失败不能影响 Core 的消息通道。
            System.Diagnostics.Debug.WriteLine("LoggingUser 写入失败");
        }
    }

    private static string Format(string message, object[]? args)
    {
        if (args is null || args.Length == 0)
        {
            return message ?? string.Empty;
        }

        try
        {
            return string.Format(CultureInfo.CurrentCulture, message, args);
        }
        catch (FormatException)
        {
            // Core 的格式串与参数不匹配时不能抛回 Core 线程，原样保留（与 AvaloniaUser 的既有策略一致）。
            return message ?? string.Empty;
        }
    }
}
