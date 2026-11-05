using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace CkanUI.Plugin.Ai.Markdown;

/// <summary>
/// 零外部依赖的高性能 Markdown + Math + Mindmap 解析器。
/// </summary>
public static class MarkdownParser
{
    public static MarkdownDocument Parse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return new MarkdownDocument(Array.Empty<MarkdownBlock>());
        }

        var lines = text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        var blocks = new List<MarkdownBlock>();
        var i = 0;

        while (i < lines.Length)
        {
            var line = lines[i];
            var trimmed = line.Trim();

            if (string.IsNullOrWhiteSpace(trimmed))
            {
                i++;
                continue;
            }

            // 1. 独立块级数学公式 $$ ... $$
            if (trimmed.StartsWith("$$"))
            {
                if (trimmed.Length > 2 && trimmed.EndsWith("$$") && trimmed.Length >= 4)
                {
                    var formula = trimmed.Substring(2, trimmed.Length - 4).Trim();
                    blocks.Add(new MathBlock(formula));
                    i++;
                    continue;
                }

                var mathLines = new List<string>();
                i++;
                while (i < lines.Length)
                {
                    var cur = lines[i];
                    if (cur.Trim().EndsWith("$$"))
                    {
                        var endTrim = cur.Trim();
                        var lastLine = endTrim.Substring(0, endTrim.Length - 2).Trim();
                        if (!string.IsNullOrEmpty(lastLine))
                        {
                            mathLines.Add(lastLine);
                        }
                        i++;
                        break;
                    }
                    mathLines.Add(cur);
                    i++;
                }
                blocks.Add(new MathBlock(string.Join("\n", mathLines).Trim()));
                continue;
            }

            // 2. 围栏代码块 / 思维导图 / Mermaid
            if (trimmed.StartsWith("```") || trimmed.StartsWith("~~~"))
            {
                var fence = trimmed.Substring(0, 3);
                var lang = trimmed.Substring(3).Trim().ToLowerInvariant();
                var codeLines = new List<string>();
                i++;
                while (i < lines.Length)
                {
                    var cur = lines[i];
                    if (cur.Trim().StartsWith(fence))
                    {
                        i++;
                        break;
                    }
                    codeLines.Add(cur);
                    i++;
                }
                var content = string.Join("\n", codeLines);

                if (lang is "mermaid" or "mindmap" or "diagram")
                {
                    var syntaxType = lang;
                    if (lang == "mermaid")
                    {
                        var trimmedContent = content.TrimStart();
                        if (trimmedContent.StartsWith("mindmap", StringComparison.OrdinalIgnoreCase))
                        {
                            syntaxType = "mindmap";
                        }
                    }
                    blocks.Add(new MindmapBlock(syntaxType, content));
                }
                else
                {
                    blocks.Add(new CodeBlock(lang, content));
                }
                continue;
            }

            // 3. 标题 (#, ##, ###, ...)
            if (trimmed.StartsWith('#'))
            {
                var hashCount = 0;
                while (hashCount < trimmed.Length && trimmed[hashCount] == '#')
                {
                    hashCount++;
                }
                if (hashCount is >= 1 and <= 6 && hashCount < trimmed.Length && trimmed[hashCount] == ' ')
                {
                    var headingText = trimmed.Substring(hashCount + 1).Trim();
                    blocks.Add(new HeadingBlock(hashCount, ParseInlines(headingText)));
                    i++;
                    continue;
                }
            }

            // 4. 水平分割线 (---, ***, ___)
            if (IsHorizontalRule(trimmed))
            {
                blocks.Add(new HorizontalRuleBlock());
                i++;
                continue;
            }

            // 5. 引用块 (>)
            if (trimmed.StartsWith('>'))
            {
                var quoteLines = new List<string>();
                while (i < lines.Length && (lines[i].Trim().StartsWith('>') || (quoteLines.Count > 0 && !string.IsNullOrWhiteSpace(lines[i]) && !IsBlockStarter(lines[i]))))
                {
                    var qLine = lines[i].Trim();
                    if (qLine.StartsWith('>'))
                    {
                        qLine = qLine.Substring(1);
                        if (qLine.StartsWith(' ')) qLine = qLine.Substring(1);
                    }
                    quoteLines.Add(qLine);
                    i++;
                }
                var childDoc = Parse(string.Join("\n", quoteLines));
                blocks.Add(new BlockquoteBlock(childDoc.Blocks));
                continue;
            }

            // 6. 表格 (行带 | 且下一行是表头分割线 |---|)
            if (IsTableStart(lines, i))
            {
                var headerRow = ParseTableRow(lines[i]);
                i += 2; // 跳过表头和分割线
                var dataRows = new List<IReadOnlyList<string>>();
                while (i < lines.Length && lines[i].Trim().Contains('|'))
                {
                    dataRows.Add(ParseTableRow(lines[i]));
                    i++;
                }
                blocks.Add(new TableBlock(headerRow, dataRows));
                continue;
            }

            // 7. 列表项 (- / * / 1.)
            if (IsListStart(trimmed, out var isOrdered, out var startNum))
            {
                var items = new List<ListItemBlock>();
                while (i < lines.Length && IsListItem(lines[i].Trim(), isOrdered, out var itemText))
                {
                    items.Add(new ListItemBlock(ParseInlines(itemText)));
                    i++;
                }
                blocks.Add(new ListBlock(isOrdered, startNum, items));
                continue;
            }

            // 8. 普通段落（累积多行直到遇到空行或块级元素）
            var paragraphLines = new List<string>();
            while (i < lines.Length && !string.IsNullOrWhiteSpace(lines[i]) && !IsBlockStarter(lines[i]))
            {
                paragraphLines.Add(lines[i].Trim());
                i++;
            }

            if (paragraphLines.Count > 0)
            {
                var fullText = string.Join(" ", paragraphLines);
                blocks.Add(new ParagraphBlock(ParseInlines(fullText)));
            }
        }

        return new MarkdownDocument(blocks);
    }

    private static bool IsHorizontalRule(string line)
    {
        if (line.Length < 3) return false;
        var cleaned = line.Replace(" ", "");
        return cleaned is "---" or "***" or "___" or "------" or "******" or "______";
    }

    private static bool IsTableStart(string[] lines, int index)
    {
        if (index + 1 >= lines.Length) return false;
        var cur = lines[index].Trim();
        var next = lines[index + 1].Trim();
        if (!cur.Contains('|') || !next.Contains('|')) return false;
        // 检查下一行是否是表头分隔线，如 |---|:---|---:|
        var stripped = next.Replace("|", "").Replace("-", "").Replace(":", "").Replace(" ", "");
        return stripped.Length == 0 && next.Contains('-');
    }

    private static IReadOnlyList<string> ParseTableRow(string line)
    {
        var trimmed = line.Trim();
        if (trimmed.StartsWith('|')) trimmed = trimmed.Substring(1);
        if (trimmed.EndsWith('|')) trimmed = trimmed.Substring(0, trimmed.Length - 1);
        var parts = trimmed.Split('|');
        var result = new List<string>();
        foreach (var part in parts)
        {
            result.Add(part.Trim());
        }
        return result;
    }

    private static bool IsListStart(string line, out bool isOrdered, out int startNum)
    {
        isOrdered = false;
        startNum = 1;
        if (line.StartsWith("- ") || line.StartsWith("* ") || line.StartsWith("+ "))
        {
            return true;
        }
        var match = Regex.Match(line, @"^(\d+)[\.\)]\s+(.*)$");
        if (match.Success && int.TryParse(match.Groups[1].Value, out startNum))
        {
            isOrdered = true;
            return true;
        }
        return false;
    }

    private static bool IsListItem(string line, bool expectOrdered, out string itemText)
    {
        itemText = string.Empty;
        if (!expectOrdered)
        {
            if (line.StartsWith("- ") || line.StartsWith("* ") || line.StartsWith("+ "))
            {
                itemText = line.Substring(2).Trim();
                return true;
            }
            return false;
        }
        var match = Regex.Match(line, @"^\d+[\.\)]\s+(.*)$");
        if (match.Success)
        {
            itemText = match.Groups[1].Value.Trim();
            return true;
        }
        return false;
    }

    private static bool IsBlockStarter(string rawLine)
    {
        var line = rawLine.Trim();
        if (line.StartsWith('#') || line.StartsWith("```") || line.StartsWith("~~~") || line.StartsWith("> ") || line.StartsWith("- ") || line.StartsWith("* ") || line.StartsWith("$$"))
        {
            return true;
        }
        if (IsHorizontalRule(line))
        {
            return true;
        }
        if (Regex.IsMatch(line, @"^\d+[\.\)]\s+"))
        {
            return true;
        }
        return false;
    }

    // ── 行内解析 ──────────────────────────────────────────────────────────

    public static IReadOnlyList<MarkdownInline> ParseInlines(string text)
    {
        var inlines = new List<MarkdownInline>();
        if (string.IsNullOrEmpty(text)) return inlines;

        var i = 0;
        var buf = new StringBuilder();

        void FlushText()
        {
            if (buf.Length > 0)
            {
                inlines.Add(new TextInline(buf.ToString()));
                buf.Clear();
            }
        }

        while (i < text.Length)
        {
            // 转义字符 \
            if (text[i] == '\\' && i + 1 < text.Length)
            {
                buf.Append(text[i + 1]);
                i += 2;
                continue;
            }

            // 1. 行内数学公式 $formula$ (非 $$)
            if (text[i] == '$' && (i + 1 >= text.Length || text[i + 1] != '$'))
            {
                var end = text.IndexOf('$', i + 1);
                if (end > i + 1 && !text.Substring(i + 1, end - i - 1).Contains('\n'))
                {
                    FlushText();
                    var formula = text.Substring(i + 1, end - i - 1);
                    inlines.Add(new MathInline(formula.Trim()));
                    i = end + 1;
                    continue;
                }
            }

            // 2. 行内代码 `code`
            if (text[i] == '`')
            {
                var end = text.IndexOf('`', i + 1);
                if (end > i)
                {
                    FlushText();
                    var code = text.Substring(i + 1, end - i - 1);
                    inlines.Add(new CodeInline(code));
                    i = end + 1;
                    continue;
                }
            }

            // 3. 超链接 [text](url)
            if (text[i] == '[')
            {
                var closeBracket = text.IndexOf(']', i + 1);
                if (closeBracket > i && closeBracket + 1 < text.Length && text[closeBracket + 1] == '(')
                {
                    var closeParen = text.IndexOf(')', closeBracket + 2);
                    if (closeParen > closeBracket + 1)
                    {
                        FlushText();
                        var linkText = text.Substring(i + 1, closeBracket - i - 1);
                        var url = text.Substring(closeBracket + 2, closeParen - closeBracket - 2).Trim();
                        inlines.Add(new LinkInline(linkText, url));
                        i = closeParen + 1;
                        continue;
                    }
                }
            }

            // 4. 删除线 ~~text~~
            if (i + 1 < text.Length && text[i] == '~' && text[i + 1] == '~')
            {
                var end = text.IndexOf("~~", i + 2, StringComparison.Ordinal);
                if (end > i + 1)
                {
                    FlushText();
                    var content = text.Substring(i + 2, end - i - 2);
                    inlines.Add(new StrikethroughInline(ParseInlines(content)));
                    i = end + 2;
                    continue;
                }
            }

            // 5. 加粗 **text** 或 __text__
            if (i + 1 < text.Length && ((text[i] == '*' && text[i + 1] == '*') || (text[i] == '_' && text[i + 1] == '_')))
            {
                var delimiter = text.Substring(i, 2);
                var end = text.IndexOf(delimiter, i + 2, StringComparison.Ordinal);
                if (end > i + 1)
                {
                    FlushText();
                    var content = text.Substring(i + 2, end - i - 2);
                    inlines.Add(new BoldInline(ParseInlines(content)));
                    i = end + 2;
                    continue;
                }
            }

            // 6. 斜体 *text* 或 _text_
            if (text[i] is '*' or '_')
            {
                var delimiter = text[i];
                var end = text.IndexOf(delimiter, i + 1);
                if (end > i && end < text.Length)
                {
                    FlushText();
                    var content = text.Substring(i + 1, end - i - 1);
                    inlines.Add(new ItalicInline(ParseInlines(content)));
                    i = end + 1;
                    continue;
                }
            }

            buf.Append(text[i]);
            i++;
        }

        FlushText();
        return inlines;
    }
}