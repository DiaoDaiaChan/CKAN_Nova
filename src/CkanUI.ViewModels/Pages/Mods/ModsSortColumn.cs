namespace CkanUI.ViewModels.Pages.Mods;

/// <summary>可排序列（03 篇 §3.2）。顺序即工具栏排序下拉的选项顺序。</summary>
public enum ModsSortColumn
{
    /// <summary>名称。</summary>
    Name = 0,

    /// <summary>标识符。</summary>
    Identifier = 1,

    /// <summary>已装版本。</summary>
    InstalledVersion = 2,

    /// <summary>最新版本。</summary>
    LatestVersion = 3,

    /// <summary>作者。</summary>
    Author = 4,

    /// <summary>下载量。</summary>
    DownloadCount = 5,

    /// <summary>安装体积。</summary>
    SizeBytes = 6,

    /// <summary>已装状态。</summary>
    IsInstalled = 7,
}
