using System.Globalization;
using System.Text;

using CkanUI.Services.Abstractions.Logs;

namespace CkanUI.Services.Abstractions.Ai;

/// <summary>
/// 把日志窗口拼成一份可诊断的提示词。三条纪律：
/// 1. 每行带 <c>#Seq</c>：模型指认证据时只能引用序号，界面上点序号即可跳回原行，杜绝「它说的那行根本不存在」。
/// 2. 先脱敏再发送：路径里的用户名、token 一律先过 <see cref="ILogRedactor"/>。
/// 3. 只保留最近的整行直到字符预算：宁可少给早期噪声，也不给半行（半行会让模型把截断当成语法错误）。
/// </summary>
public static class DiagnosticPromptBuilder
{
    /// <summary>默认字符预算（约 3k token），够放几百行日志又不至于让一次诊断烧掉整月额度。</summary>
    public const int DefaultBudget = 12000;

    private const string Instructions = """
        你是 KSP（Kerbal Space Program）模组管理器 CKAN UI 的排障助手。用户会给你一段日志窗口。
        只依据给定日志作答，不要臆造不存在的行。每条假设必须引用日志里的 #序号 作为证据；找不到证据就放进 needMoreInfo。
        严格返回一个 JSON 对象，不要 Markdown 代码块、不要额外解释，字段如下：
        {"summary":"一句话结论","severity":"low|medium|high","hypotheses":[{"cause":"原因","confidence":0.0,"evidenceSeq":[123],"explain":"为什么这样判断","fix":"玩家下一步该做什么"}],"needMoreInfo":["还需要什么"]}
        要求：假设按 confidence 从高到低排列；fix 必须是玩家能执行的具体动作（改哪个文件、装/删哪个模组、看哪个设置），不要写"检查日志"这类空话。
        """;

    /// <summary>拼提示词。</summary>
    /// <param name="events">日志窗口（通常就是日志页当前可见的行）。</param>
    /// <param name="instanceName">当前实例名。</param>
    /// <param name="gameVersion">KSP 版本（未知传 null）。</param>
    /// <param name="channel">「界面日志」或「游戏日志」，让模型知道这段日志是哪一侧的。</param>
    /// <param name="redactor">脱敏器；为 null 时只做长度裁剪。</param>
    /// <param name="budget">字符预算。</param>
    public static string Build(
        IEnumerable<LogEvent> events,
        string? instanceName,
        string? gameVersion,
        string channel,
        ILogRedactor? redactor = null,
        int budget = DefaultBudget)
    {
        ArgumentNullException.ThrowIfNull(events);
        var list = events as IReadOnlyList<LogEvent> ?? events.ToList();

        var header = new StringBuilder();
        header.AppendLine(Instructions).AppendLine();
        header.Append("实例：").AppendLine(string.IsNullOrWhiteSpace(instanceName) ? "（未选中）" : instanceName);
        header.Append("KSP 版本：").AppendLine(string.IsNullOrWhiteSpace(gameVersion) ? "未知" : gameVersion);
        header.Append("日志通道：").AppendLine(string.IsNullOrWhiteSpace(channel) ? "界面日志" : channel);
        header.AppendLine("日志（#序号 时间 级别 来源 文本）：");

        var lines = new List<string>(list.Count);
        foreach (var e in list)
        {
            var message = redactor?.Redact(e.Message) ?? e.Message;
            var detail = string.IsNullOrWhiteSpace(e.Detail) ? null : redactor?.Redact(e.Detail) ?? e.Detail;
            var text = detail is null ? message : message + " | " + detail.ReplaceLineEndings(" ⏎ ");
            lines.Add(string.Create(
                CultureInfo.InvariantCulture,
                $"#{e.Seq} {e.TimeLocal:HH:mm:ss.fff} {Level(e.Level)} {Source(e)} {text}"));
        }

        // 从最旧的开始丢弃整行，直到塞进预算。
        var budgetLeft = Math.Max(0, budget - header.Length);
        var start = lines.Count;
        var used = 0;
        while (start > 0)
        {
            var cost = lines[start - 1].Length + 1;
            if (used + cost > budgetLeft)
            {
                break;
            }

            used += cost;
            start--;
        }

        var body = new StringBuilder(header.Length + used + 64);
        body.Append(header);
        if (start > 0)
        {
            body.Append("#").Append(start).AppendLine(" 行之前的日志已按预算省略");
        }

        for (var i = start; i < lines.Count; i++)
        {
            body.AppendLine(lines[i]);
        }

        return body.ToString();
    }

    private static string Level(LogLevel level) => level switch
    {
        LogLevel.Trace => "TRC",
        LogLevel.Debug => "DBG",
        LogLevel.Info => "INF",
        LogLevel.Warn => "WRN",
        LogLevel.Error => "ERR",
        LogLevel.Fatal => "FTL",
        LogLevel.User => "USR",
        _ => "INF",
    };

    private static string Source(LogEvent e) =>
        string.IsNullOrWhiteSpace(e.Source) ? e.Kind.ToString() : e.Source!;
}
