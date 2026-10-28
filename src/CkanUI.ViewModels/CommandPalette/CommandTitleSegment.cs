using CkanUI.Services.Abstractions.Commands;

namespace CkanUI.ViewModels.CommandPalette;

/// <summary>
/// 命令标题切出来的一个文本段：<see cref="Text"/> 是原文片段，<see cref="IsHighlighted"/> 表示这段命中了查询词。
/// </summary>
/// <remarks>
/// 高亮不用 Avalonia 的 <c>Inline</c>/<c>Run</c>，也不引入值转换器：视图模型只产出「纯数据分段」，
/// 视图用 <c>ItemsControl</c> + 样式类着色即可。这样匹配与高亮逻辑全部能在 NUnit 里断言，
/// 视图模型层也不会被拖进 Avalonia 依赖（架构测试禁止 ViewModels 引用 Avalonia）。
/// </remarks>
/// <param name="Text">片段文本。</param>
/// <param name="IsHighlighted">是否命中查询词。</param>
public readonly record struct CommandTitleSegment(string Text, bool IsHighlighted)
{
    /// <summary>把标题按命中区间切段。</summary>
    /// <param name="title">命令标题。</param>
    /// <param name="ranges">命中区间；允许乱序、重叠、越界（越界部分会被裁掉）。</param>
    /// <returns>按原文顺序排列的文本段；标题为空时返回空数组。</returns>
    public static IReadOnlyList<CommandTitleSegment> FromRanges(string? title, IReadOnlyList<TextMatchRange>? ranges)
    {
        var text = title ?? string.Empty;
        if (text.Length == 0)
        {
            return Array.Empty<CommandTitleSegment>();
        }

        if (ranges is null || ranges.Count == 0)
        {
            return new[] { new CommandTitleSegment(text, false) };
        }

        // 匹配器是「每个查询词各找一次」，因此区间可能乱序、重叠；先排序再按游标推进。
        var sorted = ranges
            .Where(range => range.Length > 0 && range.Start >= 0 && range.Start < text.Length)
            .OrderBy(range => range.Start)
            .ToArray();

        if (sorted.Length == 0)
        {
            return new[] { new CommandTitleSegment(text, false) };
        }

        var segments = new List<CommandTitleSegment>(sorted.Length * 2 + 1);
        var cursor = 0;
        foreach (var range in sorted)
        {
            var end = Math.Min(text.Length, range.End);
            if (end <= cursor)
            {
                // 完全被前一段覆盖。
                continue;
            }

            if (range.Start > cursor)
            {
                segments.Add(new CommandTitleSegment(text[cursor..range.Start], false));
            }

            var start = Math.Max(cursor, range.Start);
            segments.Add(new CommandTitleSegment(text[start..end], true));
            cursor = end;
        }

        if (cursor < text.Length)
        {
            segments.Add(new CommandTitleSegment(text[cursor..], false));
        }

        return segments;
    }
}
