namespace CkanUI.ViewModels.CommandPalette;

/// <summary>
/// 快捷键目录里的一行：手势 + 动作说明 + 所属分类。
/// </summary>
/// <param name="Gesture">手势文案，例如 <c>Ctrl+K</c>、<c>Enter</c>。</param>
/// <param name="Description">动作说明（已翻译）。</param>
/// <param name="Category">分类文案（已翻译），用于分组。</param>
public sealed record ShortcutEntryDto(string Gesture, string Description, string Category);
