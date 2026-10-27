namespace CkanUI.Services.Abstractions.Dtos;

/// <summary>仓库索引快照（04 篇 §6）。</summary>
public sealed record RepositorySnapshot(
    IReadOnlyList<ModRowData> Available,
    IReadOnlyList<string> RepositoryIds,
    DateTime? UpdatedAt,
    bool IsFromCache)
{
    public static RepositorySnapshot Empty { get; } = new(
        Array.Empty<ModRowData>(), Array.Empty<string>(), null, true);
}
