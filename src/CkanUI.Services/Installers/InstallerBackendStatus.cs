namespace CkanUI.Services.Installers;

/// <summary>
/// 安装器后端的执行结局（06 篇 INT-08）。
/// 后端只负责「做」并如实汇报；是否需要回滚、如何汇报由 <see cref="InstallerService"/> 决定。
/// </summary>
public enum InstallerBackendStatus
{
    /// <summary>执行成功，可能带警告。</summary>
    Succeeded,

    /// <summary>执行失败，调用方应立即回滚。</summary>
    Failed,

    /// <summary>被取消，调用方应回滚并汇报「已取消，未做改动」。</summary>
    Cancelled,
}
