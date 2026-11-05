using System;
using System.Collections.Generic;
using CkanUI.Services.Abstractions.Dtos;

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

    /// <summary>
    /// 卸载/替换之后可能只剩配置的残留目录（12 篇 F-43）：相对 <c>&lt;GameDir&gt;/GameData</c>、正斜杠。
    /// <para>
    /// 默认空集合意味着"这次操作没有产生残留"，因此旧调用点不必改就能编译。
    /// </para>
    /// </summary>
    public IReadOnlyList<string> ResidualDirectories { get; init; } = Array.Empty<string>();

    /// <summary>
    /// 状态为 <see cref="InstallerBackendStatus.NeedsChoice"/> 时，需要用户裁决的依赖提供者候选。
    /// <para>
    /// 用 init 属性而不是位置参数：20 多个既有构造点（含测试）不必改动就能编译。
    /// </para>
    /// </summary>
    public VirtualModuleRequest? PendingChoice { get; init; }

    /// <summary>成功结果。</summary>
    public static InstallerBackendResult Ok(IReadOnlyList<string>? completed = null,
                                           IReadOnlyList<string>? warnings = null,
                                           IReadOnlyList<string>? residualDirectories = null)
        => new(InstallerBackendStatus.Succeeded,
               completed ?? Array.Empty<string>(),
               Array.Empty<string>(),
               warnings ?? Array.Empty<string>())
        {
            ResidualDirectories = residualDirectories ?? Array.Empty<string>(),
        };

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

    /// <summary>
    /// 需要用户先选依赖提供者的结果（Core 的 <c>TooManyModsProvideKraken</c>）。
    /// 什么都没改动，选完直接重试即可，不要回滚也不要报错。
    /// </summary>
    public static InstallerBackendResult NeedsChoice(VirtualModuleRequest choice,
                                                    IReadOnlyList<string>? warnings = null)
        => new(InstallerBackendStatus.NeedsChoice,
               Array.Empty<string>(),
               Array.Empty<string>(),
               warnings ?? Array.Empty<string>())
        {
            PendingChoice = choice,
        };

    /// <summary>取消结果。</summary>
    public static InstallerBackendResult Cancelled(string? message = null, string? detail = null)
        => new(InstallerBackendStatus.Cancelled,
               Array.Empty<string>(),
               Array.Empty<string>(),
               Array.Empty<string>(),
               message ?? "已取消。",
               detail);
}
