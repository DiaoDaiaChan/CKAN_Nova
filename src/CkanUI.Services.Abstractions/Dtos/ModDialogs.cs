namespace CkanUI.Services.Abstractions.Dtos;

/// <summary>推荐候选的来源分组（对齐 GUI/Controls/ChooseRecommendedMods.cs 的三组）。</summary>
public enum RecommendationKind
{
    /// <summary>被勾选模组 recommends 的。</summary>
    Recommended,

    /// <summary>被勾选模组 suggests 的（可选增强）。</summary>
    Suggested,

    /// <summary>声明支持被勾选模组的（并非被推荐）。</summary>
    SupportedBy,
}

/// <summary>
/// 一条推荐/建议/被支持候选（F-34）。只带标识与展示文案，不含任何 CKAN 类型（11 篇 §6 DTO 铁律）；
/// 调用方按 <see cref="Identifier"/> 回查注册表快照。
/// </summary>
public sealed record RecommendationCandidate(
    string Identifier,
    string Name,
    RecommendationKind Kind,
    string? Description = null,
    IReadOnlyList<string>? RelatedIdentifiers = null,
    bool IsSelectedByDefault = true,
    bool IsDlc = false,
    bool IsInstalled = false,
    bool IsConflicting = false,
    string? ConflictReason = null);

/// <summary>F-34 推荐模组对话框的入参。</summary>
public sealed record RecommendationDialogRequest(
    string Message,
    IReadOnlyList<RecommendationCandidate> Candidates,
    bool SuppressRecommendations = false);

/// <summary>推荐对话框的确认结果；取消时服务返回 null（与上游 ChooseRecommendedMods.Wait() 一致）。</summary>
public sealed record RecommendationSelection(
    IReadOnlyList<string> Identifiers,
    bool SuppressRecommendations);

/// <summary>虚拟模组的一个提供者（F-35），按 IsCached/DownloadCount 排序由调用方完成。</summary>
public sealed record VirtualModuleCandidate(
    string Identifier,
    string Name,
    string? Description = null,
    bool IsCached = false,
    bool IsInstalled = false,
    long? DownloadCount = null);

/// <summary>F-35 提供者选择对话框的入参。</summary>
public sealed record VirtualModuleRequest(
    string Message,
    IReadOnlyList<VirtualModuleCandidate> Providers,
    string? Dependency = null);

/// <summary>提供者选择结果；取消时服务返回 null，调用方应放弃安装。</summary>
public sealed record VirtualModuleChoice(string Identifier, string Name);

/// <summary>一条冲突/不一致理由（F-36）；<see cref="Description"/> 可直接用 Core 已本地化的串。</summary>
public sealed record ConflictEntry(
    string Description,
    string? ModuleIdentifier = null,
    string? ModuleName = null);

/// <summary>F-36 冲突详情对话框的入参。</summary>
public sealed record ConflictDetailsRequest(
    string Message,
    IReadOnlyList<ConflictEntry> Conflicts,
    IReadOnlyList<string>? Warnings = null,
    bool IsBlocking = true);
