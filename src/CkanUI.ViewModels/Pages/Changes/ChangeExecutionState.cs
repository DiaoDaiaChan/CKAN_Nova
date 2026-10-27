namespace CkanUI.ViewModels.Pages.Changes;

/// <summary>
/// 变更集执行流状态机（03 篇 §4.1 / 06 篇 UI-04）。
/// </summary>
/// <remarks>
/// 规格原文的五个阶段是 Idle → 确认中 → 下载中 → 安装中 → 完成；UI 侧把它压成七态：
/// 下载与安装都归入 <see cref="Applying"/>（进度由 <c>IOperationQueue</c> 的 <c>OperationProgress</c> 提供），
/// 并补上 <see cref="Computing"/>（等 <c>IChangeSetService</c> 算结果）与 <see cref="Cancelled"/>。
/// 终态三选一：<see cref="Done"/> / <see cref="Failed"/> / <see cref="Cancelled"/>。
/// </remarks>
public enum ChangeExecutionState
{
    /// <summary>还没有待应用的变更（空状态）。</summary>
    Idle,

    /// <summary>正在计算变更集（含依赖解析）。</summary>
    Computing,

    /// <summary>变更集已就绪，可以开始执行。</summary>
    Ready,

    /// <summary>正在下载/安装（可取消，取消=回滚）。</summary>
    Applying,

    /// <summary>全部成功。</summary>
    Done,

    /// <summary>失败（含部分失败）。</summary>
    Failed,

    /// <summary>用户取消，Core 已回滚事务。</summary>
    Cancelled,
}
