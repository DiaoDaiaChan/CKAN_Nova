namespace CkanUI.Services.Abstractions;

/// <summary>单个未管理文件在「接管为受管安装」里的结局（12 篇 F-42 / INT-09）。</summary>
public enum UnmanagedAdoptOutcome
{
    /// <summary>已写明进注册表的 installed_dlls。</summary>
    Adopted,

    /// <summary>本来就是受管文件，本次未做任何改动（幂等）。</summary>
    Skipped,

    /// <summary>不满足接管条件（目录为空、非 DLL、标识符冲突等），本次未写入。</summary>
    Refused,
}

/// <summary>
/// 一条接管的逐项结果。<see cref="RelativePath"/> 是相对 <c>&lt;GameDir&gt;/GameData</c> 的正斜杠路径，
/// <see cref="Identifier"/> 是按 Core 规则规范化后的模块标识符（<see cref="UnmanagedAdoptOutcome.Refused"/> 时可能为 <c>null</c>），
/// <see cref="Reason"/> 是给人看的拒绝或跳过原因。
/// </summary>
public sealed record UnmanagedAdoptItem(
    string RelativePath,
    string? Identifier,
    UnmanagedAdoptOutcome Outcome,
    string? Reason = null)
{
    /// <summary>是否已被接管。</summary>
    public bool IsAdopted => Outcome == UnmanagedAdoptOutcome.Adopted;
}

/// <summary>
/// 一次接管操作的整体结果（12 篇 F-42 / INT-09）。
/// </summary>
/// <remarks>
/// <para>接管是「逐项判定、批量写入」：能接管的项一次写进注册表，被拒绝的项只出现在
/// <see cref="Items"/> 里并带上原因，不会静默丢弃（上游 <c>ScanUnmanagedFiles</c> 会丢掉冲突项，导致「接管了却没生效」）。</para>
/// <para><see cref="Backups"/> 是写入前复制的注册表快照路径；只读预演与「没有需要写入的项」时为空。</para>
/// </remarks>
public sealed record UnmanagedAdoptResult(
    IReadOnlyList<UnmanagedAdoptItem> Items,
    IReadOnlyList<string>? BackupPaths = null,
    bool Applied = false)
{
    /// <summary>空结果（没有选中项时用）。</summary>
    public static UnmanagedAdoptResult Empty { get; } = new(Array.Empty<UnmanagedAdoptItem>());

    /// <summary>写入前生成的注册表快照（可能为空）。</summary>
    public IReadOnlyList<string> Backups => BackupPaths ?? Array.Empty<string>();

    /// <summary>已接管的项数。</summary>
    public int AdoptedCount => Items.Count(item => item.Outcome == UnmanagedAdoptOutcome.Adopted);

    /// <summary>本来就受管、被跳过的项数。</summary>
    public int SkippedCount => Items.Count(item => item.Outcome == UnmanagedAdoptOutcome.Skipped);

    /// <summary>被拒绝的项数。</summary>
    public int RefusedCount => Items.Count(item => item.Outcome == UnmanagedAdoptOutcome.Refused);

    /// <summary>是否有被拒绝的项（界面需要把原因聚合出来）。</summary>
    public bool HasRefusals => RefusedCount > 0;

    /// <summary>是否为只读预演（没有真正写入注册表）。</summary>
    public bool IsPreview => !Applied;
}
