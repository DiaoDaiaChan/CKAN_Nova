using CkanUI.Services.Abstractions.Commands;

namespace CkanUI.ViewModels.CommandPalette;

/// <summary>
/// 命令面板里的一行结果（只读）：包装一条匹配结果，把视图要用的字段摊平成属性。
/// </summary>
/// <remarks>
/// 行视图模型不持有任何可变状态：查询一变整个列表就整体替换（05 篇 §4 铁律 7），
/// 这样虚拟化列表不会因为逐行改动而抖动，也不需要集合增量通知。
/// </remarks>
public sealed class CommandPaletteItemViewModel
{
    /// <summary>包装一条匹配结果。</summary>
    /// <param name="match">匹配结果（含得分与标题命中区间）。</param>
    public CommandPaletteItemViewModel(CommandMatch match)
    {
        ArgumentNullException.ThrowIfNull(match);

        Match = match;
        TitleSegments = CommandTitleSegment.FromRanges(match.Command.Title, match.TitleHighlights);
    }

    /// <summary>原始匹配结果（含得分）。</summary>
    public CommandMatch Match { get; }

    /// <summary>命令描述符。</summary>
    public CommandDescriptor Command => Match.Command;

    /// <summary>命令 Id（执行时回传给宿主）。</summary>
    public string Id => Command.Id;

    /// <summary>命令标题。</summary>
    public string Title => Command.Title;

    /// <summary>命令分类文案。</summary>
    public string Category => Command.Category;

    /// <summary>命令副标题（可为空串）。</summary>
    public string Subtitle => Command.Subtitle;

    /// <summary>快捷键文案（可为空串）。</summary>
    public string ShortcutText => Command.ShortcutText;

    /// <summary>命令当前是否可执行（例如需要选中实例却没选时是 false）。</summary>
    public bool IsEnabled => Command.IsEnabled;

    /// <summary>标题是否需要切段高亮。</summary>
    public bool HasHighlight => TitleSegments.Any(segment => segment.IsHighlighted);

    /// <summary>是否有快捷键徽标。</summary>
    public bool HasShortcut => Command.HasShortcut;

    /// <summary>是否有副标题。</summary>
    public bool HasSubtitle => Command.HasSubtitle;

    /// <summary>匹配得分（空查询时为 0）。</summary>
    public int Score => Match.Score;

    /// <summary>标题分段（高亮着色用，见 <see cref="CommandTitleSegment"/>）。</summary>
    public IReadOnlyList<CommandTitleSegment> TitleSegments { get; }

    /// <summary>行右侧的次要说明：分类与副标题拼接，缺项时省略。</summary>
    public string DetailText
    {
        get
        {
            if (HasSubtitle)
            {
                return Category.Length == 0 ? Subtitle : Category + " · " + Subtitle;
            }

            return Category;
        }
    }

    /// <summary>是否有次要说明可显示。</summary>
    public bool HasDetail => DetailText.Length > 0;
}
