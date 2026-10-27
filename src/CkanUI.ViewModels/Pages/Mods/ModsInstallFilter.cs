namespace CkanUI.ViewModels.Pages.Mods;

/// <summary>安装状态过滤器（03 篇 §3.5）。顺序即工具栏下拉的选项顺序，不要随意调整。</summary>
public enum ModsInstallFilter
{
    /// <summary>全部。</summary>
    All = 0,

    /// <summary>已装。</summary>
    Installed = 1,

    /// <summary>未装。</summary>
    NotInstalled = 2,

    /// <summary>有更新。</summary>
    Upgradeable = 3,
}
