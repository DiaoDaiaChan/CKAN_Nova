namespace CkanUI.ViewModels.CommandPalette;

/// <summary>
/// 快捷键目录的一个分组（分类名 + 该分类下的条目）。
/// </summary>
/// <remarks>
/// 分组顺序按条目首次出现的顺序，不做字母排序：内置条目的书写顺序就是用户最该记住的顺序
/// （全局快捷键在前，命令面板自身手势在后）。
/// </remarks>
/// <param name="Category">分类文案（已翻译）。</param>
/// <param name="Entries">该分类下的条目。</param>
public sealed record ShortcutGroupViewModel(string Category, IReadOnlyList<ShortcutEntryDto> Entries)
{
    /// <summary>该分组的条目数。</summary>
    public int Count => Entries.Count;
}
