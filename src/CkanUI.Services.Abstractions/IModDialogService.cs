using CkanUI.Services.Abstractions.Dtos;

namespace CkanUI.Services.Abstractions;

/// <summary>
/// 模组对话框族（F-34 推荐 / F-35 提供者选择 / F-36 冲突详情）。
/// <see cref="IDialogService"/> 的通用 Confirm/Select/ShowMessage 无法表达"分组勾选"与"单选提供者"，
/// 因此单开一个窄接口；实现负责在 UI 线程上建窗、注入 owner，取消一律返回 null（03 篇 §7、11 篇 §11）。
/// </summary>
public interface IModDialogService
{
    /// <summary>展示 recommends/suggests/supports 带来的额外模组并让用户勾选；取消返回 null。</summary>
    Task<RecommendationSelection?> SelectRecommendationsAsync(
        RecommendationDialogRequest request,
        CancellationToken ct = default);

    /// <summary>某个虚拟模组有多个提供者时让用户选一个；取消返回 null，调用方应放弃安装。</summary>
    Task<VirtualModuleChoice?> SelectVirtualModuleAsync(
        VirtualModuleRequest request,
        CancellationToken ct = default);

    /// <summary>只读展示 RelationshipResolver 给出的冲突理由。</summary>
    Task ShowConflictDetailsAsync(
        ConflictDetailsRequest request,
        CancellationToken ct = default);
}
