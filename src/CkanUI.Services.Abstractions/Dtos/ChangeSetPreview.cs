namespace CkanUI.Services.Abstractions.Dtos;

/// <summary>变更动作（对齐 GUI/Model/ModChange.cs 的语义）。</summary>
public enum ChangeAction
{
    Install,
    Upgrade,
    Downgrade,
    Remove,
    Replace,
}

/// <summary>变更来源：用户显式勾选，还是依赖/推荐隐式带入。</summary>
public enum ChangeOrigin
{
    UserRequested,
    Dependency,
    Recommendation,
    AutoInstalled,
}

public sealed record ChangeItemDto(
    string Identifier,
    string Name,
    ChangeAction Action,
    string? FromVersion,
    string? ToVersion,
    ChangeOrigin Origin,
    long? DownloadBytes);

/// <summary>变更集预览（04 篇 §6）：提交前的纯数据结果，可直接喂给 <see cref="IInstallerService"/>。</summary>
public sealed record ChangeSetPreview(
    IReadOnlyList<ChangeItemDto> Items,
    IReadOnlyList<string> Conflicts,
    long EstimatedDownloadBytes,
    IReadOnlyList<string> Warnings)
{
    public bool IsEmpty => Items.Count == 0;

    public bool HasConflicts => Conflicts.Count > 0;
}
