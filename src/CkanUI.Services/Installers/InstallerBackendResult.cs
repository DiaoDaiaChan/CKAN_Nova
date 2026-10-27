using System;
using System.Collections.Generic;

namespace CkanUI.Services.Installers;

/// <summary>
/// 后端一次操作的汇报（06 篇 INT-08 的「部分失败汇报」）。
/// <para>
/// 为什么用结果对象而不是异常：Core 的 Kraken 异常种类多且消息面向人类，
/// 编排层只关心「成功/失败/取消 + 到底改了哪些标识符」，异常文本放进
/// <see cref="Message"/>/<see cref="Detail"/> 供错误条展示。
/// </para>
/// </summary>
public sealed record InstallerBackendResult(
    InstallerBackendStatus Status,
    IReadOnlyList<string> Completed,
    IReadOnlyList<string> Failed,
    IReadOnlyList<string> Warnings,
    string? Message = null,
    string? Detail = null)
{
    /// <summary>是否成功（警告不影响成功）。</summary>
    public bool IsSuccess => Status == InstallerBackendStatus.Succeeded;

    /// <summary>成功结果。</summary>
    public static InstallerBackendResult Ok(IReadOnlyList<string>? completed = null, IReadOnlyList<string>? warnings = null)
        => new(InstallerBackendStatus.Succeeded,
               completed ?? Array.Empty<string>(),
               Array.Empty<string>(),
               warnings ?? Array.Empty<string>());

    /// <summary>失败结果（带可供错误条展示的原因与细节）。</summary>
    public static InstallerBackendResult Fail(string message,
                                              string? detail = null,
                                              IReadOnlyList<string>? failed = null,
                                              IReadOnlyList<string>? warnings = null)
        => new(InstallerBackendStatus.Failed,
               Array.Empty<string>(),
               failed ?? Array.Empty<string>(),
               warnings ?? Array.Empty<string>(),
               message,
               detail);

    /// <summary>取消结果。</summary>
    public static InstallerBackendResult Cancelled(string? message = null, string? detail = null)
        => new(InstallerBackendStatus.Cancelled,
               Array.Empty<string>(),
               Array.Empty<string>(),
               Array.Empty<string>(),
               message ?? "已取消。",
               detail);
}
