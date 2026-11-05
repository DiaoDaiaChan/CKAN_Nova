namespace CkanUI.Services.Abstractions.Dtos;

/// <summary>历史条目记录的是哪一类操作（12 篇 F-44 / UI-23）。</summary>
public enum InstallHistoryOperation
{
    /// <summary>新装。</summary>
    Install,

    /// <summary>升到更高版本。</summary>
    Upgrade,

    /// <summary>降到更低版本。</summary>
    Downgrade,

    /// <summary>卸载。</summary>
    Remove,

    /// <summary>替换（同一标识换版本并换来源）。</summary>
    Replace,

    /// <summary>从历史重装（本仓新增的操作类型，老 GUI 没有）。</summary>
    Restore,
}

/// <summary>一次操作的最终结果。部分失败与整体失败必须分开：前者装了一半，后者一个都没动。</summary>
public enum InstallHistoryOutcome
{
    /// <summary>全部落盘成功。仅有警告仍是成功——警告单独记在 <c>Warnings</c>，不影响结果。</summary>
    Succeeded,

    /// <summary>有失败项（已装列表与历史不一致，需要用户知道）。</summary>
    PartiallyFailed,

    /// <summary>整体失败（变更集求解或安装流水线直接报错）。</summary>
    Failed,

    /// <summary>用户取消。</summary>
    Cancelled,
}

/// <summary>历史条目里的单个模组变更：标识 + 动作 + 版本区间 + 这一项是否成功。</summary>
public sealed record InstallHistoryChange(
    string Identifier,
    ChangeAction Action,
    string? FromVersion,
    string? ToVersion,
    bool Succeeded = true);

/// <summary>快照里的一个模组：当时装的版本，以及**现在**是否还装着、装的是哪版。</summary>
/// <remarks>
/// 「现在」两个字段由服务层读取当前注册表快照填进来（老 GUI 用 <c>registry.IsInstalled</c> 在视图里现算），
/// 这样页面只做分组、不接触任何 Core 概念，headless 也能测。
/// </remarks>
public sealed record InstallHistoryModule(
    string Identifier,
    string Version,
    bool IsInstalled = false,
    string? InstalledVersion = null)
{
    /// <summary>与当前已装状态不一致（未装，或装的版本不同）——重装会碰到它。</summary>
    public bool IsChanged => !IsInstalled
        || !string.Equals(Version, InstalledVersion, comparisonType: StringComparison.Ordinal);
}

/// <summary>只带标识与版本的一对（落盘快照的输入形态）。</summary>
public sealed record InstalledModuleVersion(string Identifier, string Version);

/// <summary>某个时间点的已装集合快照（F-44 的"回滚目标"）。</summary>
public sealed record InstallHistorySnapshot(
    DateTimeOffset TakenAt,
    string InstanceName,
    IReadOnlyList<InstallHistoryModule> Modules);

/// <summary>一条历史条目：时间戳 / 操作 / 实例 / 变更明细 / 结果，并附带当时的已装集合快照。</summary>
public sealed record InstallHistoryEntry(
    DateTimeOffset Timestamp,
    string InstanceName,
    InstallHistoryOperation Operation,
    InstallHistoryOutcome Outcome,
    IReadOnlyList<InstallHistoryChange> Changes,
    string? Message = null)
{
    /// <summary>这条历史记录了多少个模组变更。</summary>
    public int ChangeCount => Changes.Count;

    /// <summary>
    /// 这条历史对应的已装集合快照；只有条目、没有快照时为 null（旧文件或记录时读不到注册表）。
    /// </summary>
    public InstallHistorySnapshot? Snapshot { get; init; }
}

/// <summary>追加一条历史的请求（由安装流水线的调用方在操作结束后提交）。</summary>
public sealed record InstallHistoryRecordRequest(
    InstallHistoryOperation Operation,
    IReadOnlyList<InstallHistoryChange> Changes,
    InstallHistoryOutcome Outcome,
    string? Message = null)
{
    /// <summary>操作结束后实例的已装集合；为空则只写条目、不写快照（重装就无从谈起）。</summary>
    public IReadOnlyList<InstalledModuleVersion>? InstalledModules { get; init; }
}

/// <summary>「从历史重装」的请求：把实例的已装集合恢复到某个时间点。</summary>
public sealed record InstallHistoryRestoreRequest(DateTimeOffset Timestamp)
{
    /// <summary>只重装这些标识；null = 快照里的全部模组。</summary>
    public IReadOnlyList<string>? Identifiers { get; init; }

    /// <summary>同时卸载快照里没有、但现在装着的模组。默认 false：只补装，不删东西。</summary>
    public bool RemoveExtras { get; init; }
}

/// <summary>重装前的预览：将被变更的模组数量与明细（纯计算，不落盘）。</summary>
public sealed record InstallHistoryRestorePlan(
    DateTimeOffset Timestamp,
    IReadOnlyList<ChangeItemDto> Items,
    IReadOnlyList<string> Conflicts,
    long EstimatedDownloadBytes)
{
    /// <summary>将被变更的模组总数（含依赖带入的）。</summary>
    public int ChangeCount => Items.Count;

    /// <summary>其中要新装的。</summary>
    public int InstallCount => Items.Count(item => item.Action == ChangeAction.Install);

    /// <summary>其中要换版本的（升级 + 降级 + 替换）。</summary>
    public int UpgradeCount => Items.Count(
        item => item.Action is ChangeAction.Upgrade or ChangeAction.Downgrade or ChangeAction.Replace);

    /// <summary>其中要卸载的。</summary>
    public int RemoveCount => Items.Count(item => item.Action == ChangeAction.Remove);

    /// <summary>没有任何变更（历史与现状一致）。</summary>
    public bool IsEmpty => Items.Count == 0;

    /// <summary>依赖求解发现冲突。</summary>
    public bool HasConflicts => Conflicts.Count > 0;
}

/// <summary>重装结果汇总（与 <see cref="InstallReport"/> 同构，另带本次预览计划，便于页面回显"变了哪些"）。</summary>
public sealed record InstallHistoryRestoreResult(
    InstallHistoryRestorePlan Plan,
    IReadOnlyList<string> Installed,
    IReadOnlyList<string> Removed,
    IReadOnlyList<string> Failed,
    IReadOnlyList<string> Warnings)
{
    /// <summary>全部落盘成功（B1）：警告不是失败，只有失败项会让结果不完整。</summary>
    public bool IsFullSuccess => Failed.Count == 0;

    /// <summary>这次重装是否带出了警告（警告不等于失败）。</summary>
    public bool HasWarnings => Warnings.Count > 0;
}
