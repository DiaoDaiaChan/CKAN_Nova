namespace CkanUI.ViewModels.Dialogs;

/// <summary>
/// 详情面板的富字段投影（03 篇 §3.1 / 04 篇 §2 的 <c>ModDetailsViewModel</c>）。
/// <para>
/// 说明：<c>Abstractions</c> 里冻结的 <c>ModRowData</c> 只有列表列所需的字段，没有描述/依赖/推荐/冲突/许可证/主页。
/// 这些字段需要由 Core 的 <c>CkanModule</c> 适配出来，但服务契约已冻结（本任务不得改 Abstractions），
/// 因此先在本项目内定义纯记录承载，等 Lead 决定是否提升为 <c>Abstractions.Dtos</c> 的 DTO。
/// 所有字段可空：缺失时详情面板走空状态文案，而不是显示空白。
/// </para>
/// </summary>
public sealed record ModDetailsSnapshot(
    string Identifier,
    string Name,
    string? Author = null,
    string? Abstract = null,
    string? Description = null,
    string? License = null,
    string? Homepage = null,
    string? LatestVersion = null,
    string? InstalledVersion = null,
    long? SizeBytes = null,
    IReadOnlyList<string>? Tags = null,
    IReadOnlyList<ModRelationItem>? Dependencies = null,
    IReadOnlyList<ModRelationItem>? Recommendations = null,
    IReadOnlyList<ModRelationItem>? Conflicts = null,
    bool IsCompatible = true);
