using System.Collections.Generic;
using System.Globalization;

using CkanUI.Services.Abstractions.Logs;

using log4net.Appender;
using log4net.Core;

namespace CkanUI.Services.Logs;

/// <summary>
/// 把 log4net 事件灌进 <see cref="ILogStore"/> 的 appender（14 篇 §3.2 ①）。
/// 为什么用 appender 而不是包装 ILog：Core 内部 245 处 logger 全部走 log4net，
/// 在仓库级挂一个 appender 就能零改动抓到它们，包装 ILog 只能覆盖我们自己 new 出来的 logger。
/// </summary>
public sealed class LogStoreAppender : AppenderSkeleton
{
    /// <summary>log4net 线程上下文里会被识别为结构化标签的属性名。</summary>
    private static readonly string[] TagPropertyNames = ["mod", "module", "instance", "url", "task", "correlationId"];

    private readonly ILogStore _store;

    /// <summary>构造 appender（只对 INFO 及以上生效，避免 Core 的调试噪音占满 20k 缓冲）。</summary>
    public LogStoreAppender(ILogStore store)
    {
        ArgumentNullException.ThrowIfNull(store);
        _store = store;
        Name = "CkanUI.LogStore";
        Threshold = Level.Info;
    }

    /// <inheritdoc />
    protected override void Append(LoggingEvent loggingEvent)
    {
        try
        {
            _store.Append(ToEvent(loggingEvent));
        }
        catch (Exception)
        {
            // 日志通道自身出错绝不能把业务线程带崩（例如缓冲被替换/释放的瞬间）。
            System.Diagnostics.Debug.WriteLine("LogStoreAppender 写入失败");
        }
    }

    /// <summary>把 log4net 事件转成内部事件（公开以便快照测试直接断言字段映射）。</summary>
    public static LogEvent ToEvent(LoggingEvent loggingEvent)
    {
        ArgumentNullException.ThrowIfNull(loggingEvent);
        var loggerName = loggingEvent.LoggerName ?? string.Empty;
        return LogEvent.Create(
            LogSourceKind.Log4Net,
            MapLevel(loggingEvent.Level),
            LogSources.Encode(LogSourceKind.Log4Net, loggerName),
            loggingEvent.RenderedMessage ?? string.Empty,
            loggingEvent.GetExceptionString(),
            loggingEvent.ThreadName,
            loggingEvent.LookupProperty("correlationId")?.ToString(),
            ReadTags(loggingEvent));
    }

    /// <summary>log4net 级别 → 内部级别（按数值比较，兼容 Trace/Finer/Notice 等扩展级别）。</summary>
    public static LogLevel MapLevel(Level? level)
    {
        if (level is null)
        {
            return LogLevel.Info;
        }

        // log4net 内置级别数值：Debug=30000 Info=40000 Warn=60000 Error=70000 Fatal=110000 Emergency=120000。
        // 写成字面常量而不是 Level.Xxx.Value：switch 的关系模式要求编译期常量。
        return level.Value switch
        {
            >= 110_000 => LogLevel.Fatal,
            >= 70_000 => LogLevel.Error,
            >= 60_000 => LogLevel.Warn,
            >= 40_000 => LogLevel.Info,
            >= 30_000 => LogLevel.Debug,
            _ => LogLevel.Trace,
        };
    }

    private static IReadOnlyDictionary<string, string>? ReadTags(LoggingEvent loggingEvent)
    {
        Dictionary<string, string>? tags = null;
        foreach (var name in TagPropertyNames)
        {
            if (loggingEvent.LookupProperty(name) is not { } value)
            {
                continue;
            }

            var text = Convert.ToString(value, CultureInfo.InvariantCulture);
            if (string.IsNullOrWhiteSpace(text))
            {
                continue;
            }

            tags ??= new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            tags[name] = text;
        }

        return tags;
    }
}
