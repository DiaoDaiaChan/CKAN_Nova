namespace CkanUI.ViewModels.Pages.Mods;

/// <summary>兼容性过滤器（03 篇 §3.5，默认"仅兼容"）。顺序即下拉选项顺序。</summary>
public enum ModsCompatibilityFilter
{
    /// <summary>全部。</summary>
    All = 0,

    /// <summary>仅兼容当前游戏版本（默认）。</summary>
    CompatibleOnly = 1,

    /// <summary>仅不兼容。</summary>
    IncompatibleOnly = 2,
}
