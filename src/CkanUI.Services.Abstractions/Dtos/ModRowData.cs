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
    bool IsImplicit)
{
    /// <summary>
    /// 高级搜索事实（12 篇 F-20/F-21）：描述、许可证、语言、五类关系、标签组、缓存/新近/可替代状态。
    /// 默认值表示"没有额外事实"，依赖这些字段的搜索条件一律不命中。
    /// </summary>
    public ModSearchFacts Facts { get; init; }
}
