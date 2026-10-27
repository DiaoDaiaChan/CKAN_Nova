using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using CkanUI.Services.Abstractions;
using CkanUI.Services.Abstractions.Dtos;

namespace CkanUI.Services;

/// <summary>
/// 已安装模组列表格式导出转换器 (F-45 / UI-21)。
/// 纯函数转换，支持 PlainText, Markdown, BbCode, CSV, TSV。
/// </summary>
public static class InstalledListExporter
{
    /// <summary>
    /// 将已安装模组列表导出为指定格式的字符串。
    /// </summary>
    public static string ExportToString(IEnumerable<InstalledModExportItem>? mods, InstalledListExportFormat format)
    {
        var items = (mods ?? Enumerable.Empty<InstalledModExportItem>())
            .OrderBy(m => string.IsNullOrWhiteSpace(m.Name) ? m.Identifier : m.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var sb = new StringBuilder();

        switch (format)
        {
            case InstalledListExportFormat.PlainText:
                ExportPlainText(items, sb);
                break;
            case InstalledListExportFormat.Markdown:
                ExportMarkdown(items, sb);
                break;
            case InstalledListExportFormat.BbCode:
                ExportBbCode(items, sb);
                break;
            case InstalledListExportFormat.Csv:
                ExportDelimited(items, sb, ",");
                break;
            case InstalledListExportFormat.Tsv:
                ExportDelimited(items, sb, "\t");
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(format), format, null);
        }

        return sb.ToString();
    }

    /// <summary>
    /// 将已安装模组列表导出为指定格式并写入输出流（UTF-8 编码）。
    /// </summary>
    public static void ExportToStream(IEnumerable<InstalledModExportItem>? mods, InstalledListExportFormat format, Stream outputStream)
    {
        ArgumentNullException.ThrowIfNull(outputStream);
        var content = ExportToString(mods, format);
        using var writer = new StreamWriter(outputStream, new UTF8Encoding(false), leaveOpen: true);
        writer.Write(content);
        writer.Flush();
    }

    private static void ExportPlainText(List<InstalledModExportItem> items, StringBuilder sb)
    {
        foreach (var mod in items)
        {
            var displayName = !string.IsNullOrWhiteSpace(mod.Name) ? mod.Name : mod.Identifier;
            sb.Append(displayName);
            if (!string.IsNullOrWhiteSpace(mod.InstalledVersion))
            {
                sb.Append(' ').Append(mod.InstalledVersion);
            }
            sb.AppendLine();
        }
    }

    private static void ExportMarkdown(List<InstalledModExportItem> items, StringBuilder sb)
    {
        sb.AppendLine("| Mod 名称 | 标识符 | 已装版本 | 作者 | 链接 | 描述 |");
        sb.AppendLine("| :--- | :--- | :--- | :--- | :--- | :--- |");

        foreach (var mod in items)
        {
            var name = EscapeMarkdown(string.IsNullOrWhiteSpace(mod.Name) ? mod.Identifier : mod.Name);
            var id = EscapeMarkdown(mod.Identifier);
            var version = EscapeMarkdown(mod.InstalledVersion);
            var author = EscapeMarkdown(mod.Author ?? "-");

            var link = "-";
            if (!string.IsNullOrWhiteSpace(mod.Homepage))
            {
                link = $"[主页]({mod.Homepage})";
            }
            else if (!string.IsNullOrWhiteSpace(mod.DownloadUrl))
            {
                link = $"[下载]({mod.DownloadUrl})";
            }

            var desc = EscapeMarkdown(mod.Description ?? "-");

            sb.Append("| ")
              .Append(name).Append(" | ")
              .Append(id).Append(" | ")
              .Append(version).Append(" | ")
              .Append(author).Append(" | ")
              .Append(link).Append(" | ")
              .Append(desc).Append(" |")
              .AppendLine();
        }
    }

    private static void ExportBbCode(List<InstalledModExportItem> items, StringBuilder sb)
    {
        sb.AppendLine("[table]");
        sb.AppendLine("[tr][th]Mod 名称[/th][th]标识符[/th][th]已装版本[/th][th]作者[/th][th]链接[/th][th]描述[/th][/tr]");

        foreach (var mod in items)
        {
            var name = EscapeBbCode(string.IsNullOrWhiteSpace(mod.Name) ? mod.Identifier : mod.Name);
            var id = EscapeBbCode(mod.Identifier);
            var version = EscapeBbCode(mod.InstalledVersion);
            var author = EscapeBbCode(mod.Author ?? "-");

            var link = "-";
            if (!string.IsNullOrWhiteSpace(mod.Homepage))
            {
                link = $"[url={mod.Homepage}]主页[/url]";
            }
            else if (!string.IsNullOrWhiteSpace(mod.DownloadUrl))
            {
                link = $"[url={mod.DownloadUrl}]下载[/url]";
            }

            var desc = EscapeBbCode(mod.Description ?? "-");

            sb.Append("[tr]")
              .Append("[td][b]").Append(name).Append("[/b][/td]")
              .Append("[td]").Append(id).Append("[/td]")
              .Append("[td]").Append(version).Append("[/td]")
              .Append("[td]").Append(author).Append("[/td]")
              .Append("[td]").Append(link).Append("[/td]")
              .Append("[td]").Append(desc).Append("[/td]")
              .Append("[/tr]")
              .AppendLine();
        }

        sb.AppendLine("[/table]");
    }

    private static void ExportDelimited(List<InstalledModExportItem> items, StringBuilder sb, string delimiter)
    {
        // Header: Identifier,Name,InstalledVersion,Author,License,DownloadSize
        var headers = new[] { "Identifier", "Name", "InstalledVersion", "Author", "License", "DownloadSize" };
        sb.AppendLine(string.Join(delimiter, headers));

        foreach (var mod in items)
        {
            var sizeStr = mod.DownloadSizeBytes.HasValue ? mod.DownloadSizeBytes.Value.ToString() : "";
            var fields = new[]
            {
                mod.Identifier ?? "",
                mod.Name ?? "",
                mod.InstalledVersion ?? "",
                mod.Author ?? "",
                mod.License ?? "",
                sizeStr
            };

            var formattedFields = fields.Select(f => FormatDelimitedField(f, delimiter));
            sb.AppendLine(string.Join(delimiter, formattedFields));
        }
    }

    private static string FormatDelimitedField(string field, string delimiter)
    {
        bool mustQuote = field.Contains(delimiter) || field.Contains('"') || field.Contains('\r') || field.Contains('\n');
        if (!mustQuote)
        {
            return field;
        }

        return $"\"{field.Replace("\"", "\"\"")}\"";
    }

    private static string EscapeMarkdown(string text)
    {
        if (string.IsNullOrEmpty(text)) return "";
        return text
            .Replace("|", "\\|")
            .Replace("\r", " ")
            .Replace("\n", " ");
    }

    private static string EscapeBbCode(string text)
    {
        if (string.IsNullOrEmpty(text)) return "";
        return text
            .Replace("[", "&#91;")
            .Replace("]", "&#93;")
            .Replace("\r", " ")
            .Replace("\n", " ");
    }
}
