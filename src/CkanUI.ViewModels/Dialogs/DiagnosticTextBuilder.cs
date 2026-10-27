using System.Globalization;
using System.Text;
using CkanUI.Services.Abstractions;

namespace CkanUI.ViewModels.Dialogs;

/// <summary>
/// 诊断文本拼装（03 篇 §8 D-03 "复制诊断信息"）：把 <see cref="ErrorEntry"/> 的
/// 时间戳/上下文/用户消息/详情/异常堆栈拼成一段可直接贴进 issue 的纯文本。
/// 纯函数、无 IO、无本地化依赖，因此 headless 可断言（07 篇）。
/// </summary>
internal static class DiagnosticTextBuilder
{
    /// <summary>由错误条目拼装诊断文本。</summary>
    internal static string FromErrorEntry(ErrorEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        return Build(entry.Timestamp, entry.Context, entry.UserMessage, entry.Detail, entry.Exception?.ToString());
    }

    /// <summary>由未处理异常拼装崩溃诊断文本（含日志路径，D-08）。</summary>
    internal static string FromCrash(DateTime timestamp, Exception exception, string? logPath)
    {
        ArgumentNullException.ThrowIfNull(exception);
        var detail = string.IsNullOrWhiteSpace(logPath) ? null : "日志文件: " + logPath;
        return Build(timestamp, "crash", "程序遇到未处理的异常，已停止当前操作。", detail, exception.ToString());
    }

    /// <summary>由键值列表拼装诊断文本。</summary>
    internal static string FromItems(IReadOnlyList<DiagnosticItem> items)
    {
        ArgumentNullException.ThrowIfNull(items);

        var builder = new StringBuilder();
        builder.AppendLine("诊断信息");
        if (items.Count == 0)
        {
            builder.AppendLine("（无）");
            return builder.ToString().TrimEnd();
        }

        foreach (var item in items)
        {
            builder.Append(item.DisplayText).AppendLine();
        }

        return builder.ToString().TrimEnd();
    }

    /// <summary>按固定字段顺序拼装；空字段整行省略，避免出现"详情: "这种半截行。</summary>
    internal static string Build(DateTime timestamp, string? context, string? userMessage, string? detail, string? exceptionText)
    {
        var builder = new StringBuilder();
        builder.Append("时间: ").AppendLine(timestamp.ToString("yyyy-MM-dd HH:mm:ss zzz", CultureInfo.CurrentCulture));

        if (!string.IsNullOrWhiteSpace(context))
        {
            builder.Append("上下文: ").AppendLine(context);
        }

        if (!string.IsNullOrWhiteSpace(userMessage))
        {
            builder.Append("用户消息: ").AppendLine(userMessage);
        }

        if (!string.IsNullOrWhiteSpace(detail))
        {
            builder.Append("详情: ").AppendLine(detail);
        }

        if (!string.IsNullOrWhiteSpace(exceptionText))
        {
            builder.AppendLine("异常:");
            builder.AppendLine(exceptionText);
        }

        return builder.ToString().TrimEnd();
    }
}
