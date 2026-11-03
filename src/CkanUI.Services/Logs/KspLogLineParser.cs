using System.Globalization;
using System.Text.RegularExpressions;

using CkanUI.Services.Abstractions.Logs;

namespace CkanUI.Services.Logs;

/// <summary>KSP.log 里的一行（14 篇 §3.2 游戏日志那一路）。</summary>
/// <param name="Level">由行首标签推出的级别：LOG→Info、WRN→Warn、ERR/EXC→Error。</param>
/// <param name="Module">行内首个 <c>[模块]</c> 标签（KSP 的 logger 名，如 <c>Tac.Foo</c>），没有则为 null。</param>
/// <param name="Message">去掉时间戳与模块标签后的正文。</param>
/// <param name="TimeStamp">日志自带时间（合并到解析当天的日期）；续行与无时间戳行为 null。</param>
/// <param name="IsContinuation">是否为多行堆栈/Unity 附注这类「接着上一行」的续行。</param>
public sealed record KspLogLine(
    LogLevel Level,
    string? Module,
    string Message,
    DateTime? TimeStamp,
    bool IsContinuation);

/// <summary>
/// KSP.log 单行解析：把 Unity 的 <c>[LOG 12:00:00.123] [Module] text</c> 形态拆成级别/时间/模块/正文。
/// 纯函数、不碰文件：轮询与单元测试共用同一套规则，避免「测试里能过、真日志里全是续行」。
/// </summary>
public static class KspLogLineParser
{
    // KSP 的标签面：LOG / WRN / ERR / EXC，外加少数插件的 DBG / MSG。时间可省毫秒。
    private static readonly Regex HeadPattern = new(
        @"^\s*\[(?<tag>LOG|WRN|ERR|EXC|DBG|MSG)\s+(?<time>\d{1,2}:\d{2}:\d{2}(?:\.\d{1,7})?)\]\s?(?<body>.*)$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex ModulePattern = new(
        @"^\[(?<module>[^\]]{1,80})\]\s?(?<rest>.*)$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>解析一行；空行返回 false（调用方直接丢弃，不制造噪声事件）。</summary>
    public static bool TryParse(string? raw, DateTime dateHint, out KspLogLine line)
    {
        line = null!;
        if (string.IsNullOrWhiteSpace(raw))
        {
            return false;
        }

        var text = raw.TrimEnd('\r', '\n');
        var head = HeadPattern.Match(text);
        if (!head.Success)
        {
            // 堆栈行、Unity 的 "(Filename: ... Line: 12)"、插件直接 Console.WriteLine 的内容都走这里：
            // 保留原文但标记续行，UI 才能把它缩进/压暗，而不是当成一条独立信息。
            line = new KspLogLine(LogLevel.Info, null, text.Trim(), null, true);
            return true;
        }

        var level = head.Groups["tag"].Value switch
        {
            "WRN" => LogLevel.Warn,
            "ERR" => LogLevel.Error,
            "EXC" => LogLevel.Error,
            "DBG" => LogLevel.Debug,
            _ => LogLevel.Info,
        };

        var body = head.Groups["body"].Value;
        string? module = null;
        var moduleMatch = ModulePattern.Match(body);
        if (moduleMatch.Success)
        {
            module = moduleMatch.Groups["module"].Value.Trim();
            body = moduleMatch.Groups["rest"].Value;
        }

        line = new KspLogLine(level, string.IsNullOrWhiteSpace(module) ? null : module, body.Trim(), ParseTime(head.Groups["time"].Value, dateHint), false);
        return true;
    }

    /// <summary>把 <c>HH:mm:ss.fff</c> 拼到 <paramref name="dateHint"/> 的日期上；解析失败返回 null（由存储层补当前时间）。</summary>
    public static DateTime? ParseTime(string? raw, DateTime dateHint)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        if (!TimeSpan.TryParse(raw, CultureInfo.InvariantCulture, out var span) || span < TimeSpan.Zero || span >= TimeSpan.FromDays(1))
        {
            return null;
        }

        return dateHint.Date + span;
    }
}
