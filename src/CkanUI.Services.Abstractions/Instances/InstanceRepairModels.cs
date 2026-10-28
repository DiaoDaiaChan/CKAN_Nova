using CkanUI.Services.Abstractions.Dtos;

namespace CkanUI.Services.Abstractions.Instances;

/// <summary>实例自检命中问题的类别（12 篇 F-08 实例可靠性处置）。</summary>
public enum InstanceIssueKind
{
    /// <summary>注册表锁存在：可能是别的 CKAN 进程正在使用该实例，也可能是上次异常退出留下的残留锁。</summary>
    RegistryLocked = 0,

    /// <summary>registry.json 无法解析（被截断、手工改坏或磁盘故障）。</summary>
    RegistryCorrupt = 1,

    /// <summary>registry.json 由更新版本的 CKAN 写入，本程序不认识这个注册表版本。</summary>
    RegistryFromNewerCkan = 2,

    /// <summary>注册表声明的兼容 KSP 版本高于实例当前版本。</summary>
    GameVersionTooOld = 3,

    /// <summary>实例的游戏目录不存在（配置里留着一条死链）。</summary>
    GameDirectoryMissing = 4,
}

/// <summary>锁路径的形态：同一条路径在磁盘上可能是文件，也可能是目录。</summary>
public enum InstanceLockKind
{
    /// <summary>文件形态（上游 CKAN 的 registry.locked 就是文件）。</summary>
    RegistryLockFile = 0,

    /// <summary>目录形态（本程序或第三方工具用目录占位）。</summary>
    RegistryLockDirectory = 1,
}

/// <summary>被探测到的一处锁痕迹。</summary>
/// <param name="Path">锁路径（绝对路径）。</param>
/// <param name="Kind">文件形态还是目录形态。</param>
/// <param name="LastWriteTimeUtc">最后写入时间（UTC，取不到时为 <c>null</c>）。</param>
/// <param name="OccupantHint">占用者提示：能定位到进程就写进程名与 PID，否则说明只拿到路径与时间。</param>
public sealed record InstanceLockArtifact(
    string Path,
    InstanceLockKind Kind,
    DateTimeOffset? LastWriteTimeUtc,
    string OccupantHint)
{
    /// <summary>是否为目录形态的锁。</summary>
    public bool IsDirectory => Kind == InstanceLockKind.RegistryLockDirectory;

    /// <summary>锁形态的中文名。</summary>
    public string KindText => IsDirectory ? "锁目录" : "锁文件";

    /// <summary>一行摘要，界面列表可以直接显示（路径 + 形态 + 时间 + 占用者提示）。</summary>
    public string Summary => LastWriteTimeUtc is { } time
        ? $"{Path}（{KindText}，{time.ToLocalTime():yyyy-MM-dd HH:mm:ss}）{OccupantHint}"
        : $"{Path}（{KindText}）{OccupantHint}";
}

/// <summary>自检命中的一条问题，消息已经是可以直接展示的中文。</summary>
/// <param name="Kind">问题类别。</param>
/// <param name="Title">一句话标题（界面列表首行）。</param>
/// <param name="Message">现象描述与影响。</param>
/// <param name="Detail">技术细节（异常消息、路径、版本号等），可为 <c>null</c>。</param>
/// <param name="FixHint">给用户的处置建议，可为 <c>null</c>。</param>
/// <param name="CanAutoFix">本程序的修复动作能否直接处理（表现为界面上按钮是否可用）。</param>
public sealed record InstanceIssue(
    InstanceIssueKind Kind,
    string Title,
    string Message,
    string? Detail,
    string? FixHint,
    bool CanAutoFix)
{
    /// <summary>标题 + 说明 + 建议拼成的一句话，便于日志与纯文本展示。</summary>
    public string Summary => string.IsNullOrEmpty(FixHint)
        ? $"{Title}：{Message}"
        : $"{Title}：{Message} 建议：{FixHint}";
}

/// <summary>
/// 实例自检报告：既给事实（锁、注册表版本、KSP 版本），也给已经翻译成中文的问题清单。
/// <para>报告只读且自包含，界面不必再回头读磁盘；失败一律由 <see cref="OperationResult{T}"/> 承载。</para>
/// </summary>
public sealed record InstanceRepairDiagnosis(
    string InstanceName,
    string GameDir,
    string CkanDirectory,
    string RegistryPath,
    bool GameDirectoryExists,
    bool RegistryExists,
    bool RegistryReadable,
    string? RegistryError,
    int? RegistryVersion,
    int SupportedRegistryVersion,
    string? KspVersion,
    string? RequiredKspVersion,
    bool IsGameRunning,
    IReadOnlyList<InstanceLockArtifact> Locks,
    IReadOnlyList<InstanceIssue> Issues)
{
    /// <summary>没有任何问题。</summary>
    public bool IsHealthy => Issues.Count == 0;

    /// <summary>磁盘上存在锁痕迹。</summary>
    public bool IsLocked => Locks.Count > 0;

    /// <summary>注册表损坏（需要隔离后才能继续）。</summary>
    public bool HasCorruptedRegistry => Issues.Any(static issue => issue.Kind == InstanceIssueKind.RegistryCorrupt);

    /// <summary>当前是否允许强制解锁：有锁，且游戏进程没在跑。</summary>
    public bool CanForceUnlock => IsLocked && !IsGameRunning;

    /// <summary>一句话结论，界面抬头直接用。</summary>
    public string Headline => IsHealthy
        ? $"实例「{InstanceName}」自检通过。"
        : $"实例「{InstanceName}」自检发现 {Issues.Count} 个问题。";
}

/// <summary>强制解锁的结果（含操作记录落盘情况）。</summary>
/// <param name="RemovedPaths">实际删除的锁路径。</param>
/// <param name="SkippedPaths">探测到但没敢删的锁路径（例如非空锁目录）。</param>
/// <param name="JournalPath">操作记录文件路径。</param>
/// <param name="JournalWritten">操作记录是否写成功（写失败不影响解锁结果，只是少了留痕）。</param>
/// <param name="Timestamp">动作发生时间。</param>
/// <param name="Message">给用户看的中文结论（成功了什么、还需要人工做什么）。</param>
public sealed record InstanceUnlockReport(
    IReadOnlyList<string> RemovedPaths,
    IReadOnlyList<string> SkippedPaths,
    string JournalPath,
    bool JournalWritten,
    DateTimeOffset Timestamp,
    string Message);

/// <summary>注册表隔离（改名）的结果：内容原样保留，绝不删除用户文件。</summary>
/// <param name="OriginalPath">原来的 registry.json 路径。</param>
/// <param name="QuarantinePath">隔离后的文件名（registry.json.corrupt-时间戳）。</param>
/// <param name="Bytes">被隔离文件的字节数。</param>
/// <param name="Timestamp">动作发生时间。</param>
/// <param name="JournalPath">操作记录文件路径。</param>
/// <param name="JournalWritten">操作记录是否写成功。</param>
/// <param name="Message">给用户看的中文结论。</param>
public sealed record InstanceRegistryQuarantine(
    string OriginalPath,
    string QuarantinePath,
    long Bytes,
    DateTimeOffset Timestamp,
    string JournalPath,
    bool JournalWritten,
    string Message);

/// <summary>版本兼容判定结果（F-08 第 4 条：KSP 版本过旧 / CKAN 自身过旧）。</summary>
/// <param name="InstanceName">实例名。</param>
/// <param name="KspVersion">实例当前的 KSP 版本（取不到为 <c>null</c>）。</param>
/// <param name="RequiredKspVersion">注册表声明的兼容 KSP 版本（未声明为 <c>null</c>）。</param>
/// <param name="RegistryVersion">registry.json 里的 registry_version。</param>
/// <param name="SupportedRegistryVersion">本程序支持的最高 registry_version。</param>
/// <param name="CkanTooOld">注册表由更新版本的 CKAN 写入，需要升级 CKAN。</param>
/// <param name="GameVersionTooOld">实例的 KSP 版本低于注册表声明的版本。</param>
/// <param name="Advice">中文建议（兼容时为空串）。</param>
public sealed record InstanceCompatibilityInfo(
    string InstanceName,
    string? KspVersion,
    string? RequiredKspVersion,
    int? RegistryVersion,
    int SupportedRegistryVersion,
    bool CkanTooOld,
    bool GameVersionTooOld,
    string Advice)
{
    /// <summary>没有版本层面的阻塞。</summary>
    public bool IsCompatible => !CkanTooOld && !GameVersionTooOld;
}

/// <summary>
/// 自检 / 修复的输入：只需要名字、游戏目录和 KSP 版本三个事实。
/// <para>刻意不复用完整实例投影，是为了让界面行对象和 headless 测试都能直接构造。</para>
/// </summary>
/// <param name="InstanceName">实例名（用于消息文案）。</param>
/// <param name="GameDir">游戏根目录（CKAN 目录是它下面的 <c>CKAN</c> 子目录）。</param>
/// <param name="GameVersion">KSP 版本字符串，取不到时为 <c>null</c>。</param>
public sealed record InstanceRepairTarget(string InstanceName, string GameDir, string? GameVersion)
{
    /// <summary>从实例投影构造。</summary>
    public static InstanceRepairTarget From(InstanceInfo info) => new(info.Name, info.GameDir, info.GameVersion);
}
