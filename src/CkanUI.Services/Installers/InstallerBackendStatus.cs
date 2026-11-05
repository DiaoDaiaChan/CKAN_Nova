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

    /// <summary>
    /// 依赖有多个提供者，必须由用户先选一个再继续（Core 的 <c>TooManyModsProvideKraken</c>）。
    /// <para>
    /// 这不是失败：什么都没改动，用户选完即可原样重试。调用方应通过
    /// <see cref="InstallerBackendResult.PendingChoice"/> 把候选集交给 UI 弹选择框，
    /// 而不是把它当错误汇报（否则一屏会出现四遍同义的「未执行任何操作」）。
    /// </para>
    /// </summary>
    NeedsChoice,
}
