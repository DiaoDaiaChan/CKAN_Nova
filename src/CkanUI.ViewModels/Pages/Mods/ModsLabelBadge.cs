namespace CkanUI.ViewModels.Pages.Mods;

/// <summary>
/// 详情面板里的一枚标签组标记（12 篇 F-24）：只读展示，不带命令。
/// </summary>
public sealed record ModsLabelBadge(string Name, string ColorHex, bool HasColor);
