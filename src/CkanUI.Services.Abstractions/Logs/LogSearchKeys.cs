using System.Globalization;
using System.Text;

namespace CkanUI.Services.Abstractions.Logs;

/// <summary>
/// 搜索串的构建与匹配（性能关键路径）。
/// 构建时把「可能被搜到的字段」全部小写拼成一串：原文、Detail、来源、级别标签、族名、标签键值。
/// 这样一次 <see cref="string.IndexOf(string, StringComparison)"/> 就能覆盖「搜异常栈里的 url」这种真实诉求。
/// </summary>
public static class LogSearchKeys
{
    /// <summary>构建一条事件的搜索串（小写，字段用 \u001f 分隔避免跨字段误命中）。</summary>
    public static string Build(LogEvent @event)
    {
        ArgumentNullException.ThrowIfNull(@event);
        var builder = new StringBuilder(96 + (@event.Message?.Length ?? 0));
        builder.Append(@event.Message).Append('\u001f')
            .Append(@event.Detail).Append('\u001f')
            .Append(@event.Source).Append('\u001f')
            .Append(LogLevels.Label(@event.Level)).Append('\u001f')
            .Append(@event.Family.ToString());

        if (@event.Tags is { Count: > 0 } tags)
        {
            builder.Append('\u001f');
            foreach (var pair in tags)
            {
                builder.Append(pair.Key).Append('=').Append(pair.Value).Append(' ');
            }
        }

        // 只在这里做一次 ToLowerInvariant：查询侧不再逐行转换。
        return builder.ToString().ToLowerInvariant();
    }

    /// <summary>
    /// 判断搜索串是否包含关键词。关键词在传入前须已小写（由 <see cref="LogQuery.NormalizedText"/> 保证）。
    /// </summary>
    public static bool Contains(string searchKey, string lowerKeyword) =>
        !string.IsNullOrEmpty(lowerKeyword) && searchKey.Contains(lowerKeyword, StringComparison.Ordinal);

    /// <summary>把用户输入的关键词归一（去空白 + 小写），空输入返回空串。</summary>
    public static string NormalizeKeyword(string? keyword) =>
        string.IsNullOrWhiteSpace(keyword) ? string.Empty : keyword.Trim().ToLowerInvariant();

    /// <summary>小写化并保持中性区域性（日志内容可能是中文/土耳其语 i 等，必须 Invariant）。</summary>
    public static string Lower(string? value) =>
        value?.ToLowerInvariant() ?? string.Empty;

    /// <summary>用于指纹/骨架化的数字与路径替换（聚合视图「×N」折叠用）。</summary>
    public static string Skeletonize(string? message)
    {
        if (string.IsNullOrEmpty(message))
        {
            return string.Empty;
        }

        var builder = new StringBuilder(message.Length);
        var inDigits = false;
        foreach (var ch in message)
        {
            if (char.IsDigit(ch) && !inDigits)
            {
                inDigits = true;
                builder.Append('#');
            }
            else if (char.IsDigit(ch))
            {
                // 连续数字只留一个占位符，长度不参与指纹。
            }
            else
            {
                inDigits = false;
                builder.Append(ch);
            }
        }

        return builder.ToString().Trim();
    }

    /// <summary>把事件拼成一行稳定文本（日志文件/诊断包/Tail 用），时间用不变区域性避免格式随系统语言变化。</summary>
    public static string FormatLine(LogEvent @event)
    {
        ArgumentNullException.ThrowIfNull(@event);
        var time = @event.TimeLocal == default
            ? "--:--:--"
            : @event.TimeLocal.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
        var level = LogLevels.Label(@event.Level);
        return string.Create(
            CultureInfo.InvariantCulture,
            $"{@event.Seq} {time} {level} [{@event.Source}] {@event.Message}");
    }
}
