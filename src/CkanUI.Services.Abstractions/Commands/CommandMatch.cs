namespace CkanUI.Services.Abstractions.Commands;

/// <summary>
/// 一条命中结果：命令 + 分数 + 标题高亮区间（UI-40）。
/// </summary>
/// <remarks>
/// 分数是纯整数运算的结果（不使用浮点，避免排序在不同平台上抖动）：
/// 越大越靠前，同分时按目录注册顺序稳定排序。空查询命中的条目分数为 0 且没有高亮，
/// 顺序为「分类名 → 注册顺序」，用于打开面板时的默认浏览态。
/// </remarks>
/// <param name="Command">命中的命令。</param>
/// <param name="Score">匹配得分（空查询为 0）。</param>
/// <param name="TitleHighlights">标题里需要高亮的区间（已排序、已合并，可能为空）。</param>
public sealed record CommandMatch(CommandDescriptor Command, int Score, IReadOnlyList<TextMatchRange> TitleHighlights)
{
    /// <summary>空高亮（构造结果时复用，避免每次分配）。</summary>
    public static IReadOnlyList<TextMatchRange> NoHighlights { get; } = Array.Empty<TextMatchRange>();
}
