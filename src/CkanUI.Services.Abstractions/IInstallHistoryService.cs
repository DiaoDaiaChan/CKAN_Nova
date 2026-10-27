using CkanUI.Services.Abstractions.Dtos;

namespace CkanUI.Services.Abstractions;

/// <summary>
/// 安装历史（12 篇 F-44 / 13 篇 UI-23）：把每次安装/卸载/升级落成可浏览的历史，
/// 并支持"从某个时间点的已装集合重装"。
/// </summary>
/// <remarks>
/// <para>
/// 所有成员都带默认实现（DIM）：<c>ModPacksPageViewModel</c> 那种"没有服务也能跑"的
/// 无参 ctor 场景下，页面拿到的是兜底实现，调用会得到一条中文可读的失败结果而不是
/// <see cref="NullReferenceException"/>。
/// </para>
/// <para>
/// 重装必须走 <see cref="IChangeSetService"/> + <see cref="IInstallerService"/> 的既有编排，
/// 实现不得自己拼安装逻辑（见 <c>CkanUI.Services/ModPacks/ModPackService.cs</c>）。
/// </para>
/// </remarks>
public interface IInstallHistoryService
{
    /// <summary>当前是否有可用实例（没有实例就没有历史文件，页面据此禁用按钮）。</summary>
    bool HasActiveInstance => false;

    /// <summary>当前实例名；没有实例时为 null。</summary>
    string? InstanceName => null;

    /// <summary>
    /// 读取历史条目（新的在前）。
    /// </summary>
    /// <param name="limit">只取最近多少条；null = 全部（受落盘容量上限约束）。</param>
    Task<OperationResult<IReadOnlyList<InstallHistoryEntry>>> LoadAsync(
        int? limit = null,
        CancellationToken ct = default)
        => Task.FromResult(OperationResult<IReadOnlyList<InstallHistoryEntry>>.Fail(
            "当前安装历史服务不可用。"));

    /// <summary>计算"从历史重装"会带来哪些变更（纯计算，不落盘、不装）。</summary>
    Task<OperationResult<InstallHistoryRestorePlan>> PreviewRestoreAsync(
        InstallHistoryRestoreRequest request,
        CancellationToken ct = default)
        => Task.FromResult(OperationResult<InstallHistoryRestorePlan>.Fail(
            "当前安装历史服务不可用。"));

    /// <summary>执行"从历史重装"：求解变更集 → 安装；有冲突时不执行安装。</summary>
    Task<OperationResult<InstallHistoryRestoreResult>> RestoreAsync(
        InstallHistoryRestoreRequest request,
        IProgress<OperationProgress>? progress = null,
        CancellationToken ct = default)
        => Task.FromResult(OperationResult<InstallHistoryRestoreResult>.Fail(
            "当前安装历史服务不可用。"));

    /// <summary>追加一条历史（由安装流水线在操作结束后调用）。</summary>
    Task<OperationResult<InstallHistoryEntry>> RecordAsync(
        InstallHistoryRecordRequest request,
        CancellationToken ct = default)
        => Task.FromResult(OperationResult<InstallHistoryEntry>.Fail(
            "当前安装历史服务不可用。"));
}
