using System.Windows.Input;

namespace CkanUI.ViewModels.Pages.Home;

/// <summary>
/// 主菜单「快捷操作」页签里的一张卡片：图标字形 + 标题 + 一句说明 + 直达命令。
/// </summary>
/// <param name="Title">卡片标题（已本地化）。</param>
/// <param name="Description">一句说明（已本地化）。</param>
/// <param name="Glyph">字形标识符，对应 <c>NavGlyphToGeometryConverter</c> 里的键。</param>
/// <param name="Command">点击后执行的导航命令。</param>
public sealed record HomeQuickActionViewModel(string Title, string Description, string Glyph, ICommand Command);
