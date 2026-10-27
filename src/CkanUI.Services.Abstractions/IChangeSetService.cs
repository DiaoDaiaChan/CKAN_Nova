using CkanUI.Services.Abstractions.Dtos;

namespace CkanUI.Services.Abstractions;

/// <summary>用户的一次选择请求（勾选/取消勾选/指定版本）。</summary>
public sealed record ChangeRequest(string Identifier, ChangeAction Action, string? TargetVersion = null);

/// <summary>
/// 用 Core 的 RelationshipResolver 计算变更集（04 篇 §3 / INT-07）。
/// 纯计算，不落盘；结果应与 CLI 的 install --dry-run 一致。
/// </summary>
public interface IChangeSetService
{
    /// <summary>当前注册表 + 仓库快照下的完整变更集（含依赖与推荐项）。</summary>
    Task<OperationResult<ChangeSetPreview>> BuildAsync(
        IReadOnlyCollection<ChangeRequest> requests,
        CancellationToken ct = default);

    /// <summary>仅本地的快速预览（不含推荐项），用于勾选时的即时反馈。</summary>
    OperationResult<ChangeSetPreview> BuildLocal(IReadOnlyCollection<ChangeRequest> requests);
}
