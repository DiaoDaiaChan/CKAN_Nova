namespace CkanUI.Services.Abstractions.Dtos;

/// <summary>
/// 模组列表行投影（04 篇 §2.1 / §6）。不可变，3000 行不产生 3000 个 ObservableObject。
/// 仅承载 UI 需要的字段；详情按 <see cref="Identifier"/> 回查快照。
/// </summary>
public readonly record struct ModRowData(
    string Identifier,
    string Name,
    string? Author,
    string? InstalledVersion,
    string? LatestVersion,
    long DownloadCount,
    IReadOnlyList<string> Tags,
    long? SizeBytes,
    bool IsInstalled,
    bool HasUpdate,
    bool IsCompatible,
    bool IsAutoInstalled,
    bool IsSelected,
    bool IsImplicit);
