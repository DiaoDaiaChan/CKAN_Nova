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

    /// <summary>
    /// 与 <see cref="Conflicts"/> 一一对应的结构化冲突行（B0/B2）。
    /// <para>
    /// 以 <c>init</c> 属性追加而不是新增构造参数：全仓 6 个生产构造点与 20+ 测试构造点一个都不用改，
    /// 默认空集合等价于"没有结构化明细"，此时调用方退回只读 <see cref="Conflicts"/> 文本。
    /// </para>
    /// </summary>
    public IReadOnlyList<ConflictEntry> ConflictEntries { get; init; } = Array.Empty<ConflictEntry>();

    public bool HasConflicts => Conflicts.Count > 0 || ConflictEntries.Count > 0;
}
