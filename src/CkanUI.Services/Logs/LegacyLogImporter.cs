using System.Globalization;
using System.Text.RegularExpressions;

using CkanUI.Services.Abstractions;
using CkanUI.Services.Abstractions.Logs;

namespace CkanUI.Services.Logs;

/// <summary>
/// 旧 <c>CKAN.log</c> 导入（14 篇 §8 LOG-02）。
/// 旧格式来自 GUI/log4net.xml 的 conversionPattern：
/// <c>%date{yyyy-MM-dd HH:mm:ss} [%thread] %level %logger - %message</c>，
/// 续行（异常栈）没有时间戳，必须挂回上一条事件的 Detail，否则栈会变成一堆孤立行。
/// </summary>
public sealed partial class LegacyLogImporter : ILegacyLogImporter
{
    private const string LegacySourceName = "CKAN.log";

    private readonly ILogStore _store;

    /// <summary>构造导入器。</summary>
    public LegacyLogImporter(ILogStore store)
    {
        ArgumentNullException.ThrowIfNull(store);
        _store = store;
    }

    /// <inheritdoc />
    public OperationResult<int> Import(string logPath)
    {
        if (string.IsNullOrWhiteSpace(logPath))
        {
            return OperationResult<int>.Fail("日志文件路径为空");
        }

        try
        {
            if (!File.Exists(logPath))
            {
                return OperationResult<int>.Fail("日志文件不存在", logPath);
            }

            var drafts = Parse(File.ReadLines(logPath));
            var written = _store.AppendRange(drafts);
            return OperationResult<int>.Ok(written);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return OperationResult<int>.Fail("读取旧日志失败", exception.Message, retryable: true);
        }
    }

    /// <summary>解析旧格式行（纯函数，便于用快照直接断言）。</summary>
    public static List<LogEvent> Parse(IEnumerable<string> lines)
    {
        ArgumentNullException.ThrowIfNull(lines);
        var events = new List<LogEvent>();
        LogEvent? pending = null;
        var detail = new System.Text.StringBuilder();

        foreach (var line in lines)
        {
            if (TryParseLine(line, out var time, out var level, out var logger, out var message))
            {
                if (pending is not null)
                {
                    events.Add(detail.Length == 0 ? pending : pending with { Detail = detail.ToString().TrimEnd() });
                    detail.Clear();
                }

                pending = LogEvent.Create(
                    LogSourceKind.Legacy,
                    level,
                    LogSources.Encode(LogSourceKind.Legacy, logger),
                    message) with { TimeLocal = time };
                continue;
            }

            // 续行：挂到上一条的 Detail（异常栈、多行 HTTP 响应体）。
            if (pending is not null && !string.IsNullOrWhiteSpace(line))
            {
                detail.Append(line).Append('\n');
            }
        }

        if (pending is not null)
        {
            events.Add(detail.Length == 0 ? pending : pending with { Detail = detail.ToString().TrimEnd() });
        }

        return events;
    }

    /// <summary>尝试解析一行头部（时间 + 级别 + logger + 消息）。</summary>
    public static bool TryParseLine(
        string? line,
        out DateTime time,
        out LogLevel level,
        out string logger,
        out string message)
    {
        time = default;
        level = LogLevel.Info;
        logger = LegacySourceName;
        message = string.Empty;
        if (string.IsNullOrWhiteSpace(line))
        {
            return false;
        }

        var match = HeaderPattern().Match(line);
        if (!match.Success)
        {
            return false;
        }

        if (!DateTime.TryParseExact(
                match.Groups["time"].Value,
                ["yyyy-MM-dd HH:mm:ss", "yyyy-MM-ddTHH:mm:ss", "yyyy-MM-dd HH:mm:ss,fff"],
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out time))
        {
            return false;
        }

        level = LogLevels.Parse(match.Groups["level"].Value);
        logger = match.Groups["logger"].Value.Trim();
        message = match.Groups["message"].Value;
        return true;
    }

    // 兼容 "[thread] LEVEL logger - msg"（新版 GUI 配置）与 "LEVEL logger - msg"（无线程列）。
    [GeneratedRegex(
        @"^(?<time>\d{4}-\d{2}-\d{2}[ T]\d{2}:\d{2}:\d{2}(?:,\d+)?)\s+(?:\[[^\]]*\]\s+)?\[?(?<level>[A-Za-z]+)\]?\s+(?<logger>[^\s-]+)\s+-\s(?<message>.*)$",
        RegexOptions.CultureInvariant)]
    private static partial Regex HeaderPattern();
}
