namespace CkanUI.Services.Abstractions.Dtos;

/// <summary>
/// 去重执行请求（F-41 / UI-21）。
/// </summary>
/// <remarks>
/// <see cref="DryRun"/> 默认 <c>true</c>：与「未管理文件删除」同一口径（工程约定 §12 #16）——
/// 服务层默认只预演，必须由界面在两次确认之后显式传 <c>false</c> 才写盘。
/// </remarks>
public sealed record DedupExecuteRequest(IReadOnlyList<DedupGroup> Groups)
{
    /// <summary>是否只预演（默认 true，不写盘）。</summary>
    public bool DryRun { get; init; } = true;

    /// <summary>是否允许改动受管文件（默认 false，与扫描口径一致）。</summary>
    public bool IncludeManagedFiles { get; init; }

    /// <summary>从扫描报告构造执行请求。</summary>
    public static DedupExecuteRequest From(DedupScanReport report, bool dryRun = true)
    {
        ArgumentNullException.ThrowIfNull(report);
        return new DedupExecuteRequest(report.Groups) { DryRun = dryRun };
    }
}

/// <summary>单个文件的执行结果分类。</summary>
public enum DedupLinkOutcome
{
    /// <summary>预演通过，真正执行时会改成硬链接（dry-run 专用）。</summary>
    Planned,

    /// <summary>已成功改成硬链接。</summary>
    Linked,

    /// <summary>已跳过（跨卷/受管/链接/不存在等，原因在 Message）。</summary>
    Skipped,

    /// <summary>失败（原因在 Message）。</summary>
    Failed,

    /// <summary>本组后续失败，已回滚成独立副本。</summary>
    RolledBack,
}

/// <summary>一个文件的执行结果。</summary>
/// <param name="InstanceName">所属实例名。</param>
/// <param name="RelativePath">相对 GameData 的路径。</param>
/// <param name="AbsolutePath">绝对路径。</param>
/// <param name="KeepPath">保留副本的绝对路径（硬链接指向它）。</param>
/// <param name="Outcome">结果分类。</param>
/// <param name="Message">可读中文说明（跳过/失败原因）。</param>
/// <param name="FreedBytes">本条释放的字节数（预演时为预计值，失败/跳过为 0）。</param>
public sealed record DedupLinkResult(
    string InstanceName,
    string RelativePath,
    string AbsolutePath,
    string KeepPath,
    DedupLinkOutcome Outcome,
    string Message,
    long FreedBytes)
{
    /// <summary>「实例名: 相对路径」，供界面直接展示。</summary>
    public string DisplayName => $"{InstanceName}: {RelativePath}";
}

/// <summary>一次去重执行的报告。</summary>
/// <param name="DryRun">是否为预演。</param>
/// <param name="Support">平台硬链接能力（不支持时不会写盘）。</param>
/// <param name="Results">逐文件结果。</param>
/// <param name="FreedBytes">实际（或预演预计）释放的字节合计。</param>
public sealed record DedupExecuteReport(
    bool DryRun,
    DedupPlatformSupport Support,
    IReadOnlyList<DedupLinkResult> Results,
    long FreedBytes)
{
    /// <summary>已改成硬链接的文件数。</summary>
    public int LinkedCount => Results.Count(result => result.Outcome == DedupLinkOutcome.Linked);

    /// <summary>预演通过的文件数。</summary>
    public int PlannedCount => Results.Count(result => result.Outcome == DedupLinkOutcome.Planned);

    /// <summary>跳过数。</summary>
    public int SkippedCount => Results.Count(result => result.Outcome == DedupLinkOutcome.Skipped);

    /// <summary>失败数。</summary>
    public int FailedCount => Results.Count(result => result.Outcome == DedupLinkOutcome.Failed);

    /// <summary>回滚数。</summary>
    public int RolledBackCount => Results.Count(result => result.Outcome == DedupLinkOutcome.RolledBack);

    /// <summary>是否有失败。</summary>
    public bool HasFailures => FailedCount > 0;

    /// <summary>是否因为平台不支持而没有做任何事。</summary>
    public bool IsUnsupported => Support != DedupPlatformSupport.Supported;

    /// <summary>空报告。</summary>
    public static DedupExecuteReport Empty(bool dryRun, DedupPlatformSupport support) =>
        new(dryRun, support, Array.Empty<DedupLinkResult>(), 0);
}
