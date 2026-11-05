using CkanUI.Services.Abstractions.Dtos;

namespace CkanUI.Services.Abstractions;

/// <summary>
/// 队列工作项自报的终态：让「浮层说全部完成、页面说失败」这种自相矛盾不再可能出现。
/// </summary>
/// <remarks>
/// <para>
/// 为什么需要它：<c>IOperationQueue</c> 原来的工作签名是 <c>Func&lt;CancellationToken, Task&gt;</c>，
/// 队列只能在 <c>work</c> 正常返回后**无条件**写成功（100% / 已完成）。而安装链路
/// （<c>InstallerService</c> / <c>AiCkanBridge</c>）按契约「绝不抛异常」，失败是作为
/// <c>OperationResult</c> 正常返回的——于是页面进入失败态、后台浮层却显示「全部完成」。
/// </para>
/// <para>
/// 现在工作项自己说结论，队列照抄：<see cref="Succeeded"/> 补 100%，
/// <see cref="Failed"/> 只改状态与文案（不伪造成 100%），<see cref="Cancelled"/> 保留取消语义。
/// 异常与取消仍然由队列兜住，工作项不需要为了表达失败去抛异常。
/// </para>
/// </remarks>
/// <param name="State">工作项结束时希望队列记录的状态。</param>
/// <param name="Message">队列浮层上的一行说明；为 null 时沿用队列已有文案。</param>
public readonly record struct OperationOutcome(OperationState State, string? Message = null)
{
    /// <summary>工作项成功完成。</summary>
    /// <param name="message">可选说明，例如「等待你选定依赖提供者后继续。」。</param>
    /// <returns>成功结论。</returns>
    public static OperationOutcome Succeeded(string? message = null)
        => new(OperationState.Succeeded, message);

    /// <summary>工作项做了判断但没做成（未执行、未改盘），例如依赖解析失败。</summary>
    /// <param name="message">给人看的原因，会显示在浮层里。</param>
    /// <returns>失败结论。</returns>
    public static OperationOutcome Failed(string? message)
        => new(OperationState.Failed, message);

    /// <summary>工作项被取消。</summary>
    /// <param name="message">可选说明。</param>
    /// <returns>取消结论。</returns>
    public static OperationOutcome Cancelled(string? message = null)
        => new(OperationState.Cancelled, message);
}
