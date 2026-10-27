namespace CkanUI.Services.Abstractions.Dtos;

/// <summary>
/// 跨实例去重扫描请求（规格 F-41 / UI-21 / INT-09）。纯 BCL：只有实例名与几个阈值，不携带任何 CKAN 类型。
/// </summary>
/// <remarks>
/// 默认值与旧 GUI 口径一致：<c>InstalledFilesDeduplicator.MinDedupFileSize = 128 * 1024</c>
/// （小于 128 KiB 的文件去重收益低，且更容易被用户事后编辑）。
/// </remarks>
public sealed record DedupScanRequest(IReadOnlyList<string> InstanceNames)
{
    /// <summary>最小候选文件尺寸（字节），默认 128 KiB。</summary>
    public long MinimumFileSizeBytes { get; init; } = 128 * 1024;

    /// <summary>
    /// 是否把注册表受管文件也纳入候选，默认 <c>false</c>：
    /// 受管文件被改成硬链接后，卸载时删掉一个路径会影响另一个实例的同一文件，破坏卸载语义。
    /// </summary>
    public bool IncludeManagedFiles { get; init; }

    /// <summary>扫描全部实例（实例名集合为空即「全部」，与 <see cref="InstanceNames"/> 的语义一致）。</summary>
    public static DedupScanRequest AllInstances() => new(Array.Empty<string>());
}

/// <summary>一个内容相同的候选文件（属于某个实例的 GameData）。</summary>
/// <param name="InstanceName">所属实例名。</param>
/// <param name="RelativePath">相对 <c>&lt;GameDir&gt;/GameData</c> 的正斜杠路径。</param>
/// <param name="AbsolutePath">绝对路径（仅服务层与视图打开文件夹用）。</param>
/// <param name="SizeBytes">文件字节数（同组内必然相等）。</param>
/// <param name="VolumeId">卷标识（只有同卷的两个文件才能做硬链接）。</param>
/// <param name="IsManaged">是否由注册表登记为受管文件。</param>
public sealed record DedupCandidateFile(
    string InstanceName,
    string RelativePath,
    string AbsolutePath,
    long SizeBytes,
    string VolumeId,
    bool IsManaged)
{
    /// <summary>「实例名: 相对路径」，供表格与提示文案直接展示。</summary>
    public string DisplayName => $"{InstanceName}: {RelativePath}";
}

/// <summary>
/// 一组内容相同（大小 + SHA256 一致）且同卷的文件。
/// </summary>
/// <remarks>
/// 分组键里带 <see cref="VolumeId"/>：跨卷的重复文件无法硬链接，因此按卷拆成两组分别报告，
/// 而不是给出一个永远执行不了的「可释放空间」。
/// </remarks>
public sealed record DedupGroup(string ContentHash, long SizeBytes, string VolumeId, IReadOnlyList<DedupCandidateFile> Files)
{
    /// <summary>组内文件数。</summary>
    public int FileCount => Files.Count;

    /// <summary>
    /// 组内**不同文件实体**的个数（同一 inode/文件标识只算一个）；平台读不到文件标识时为 <c>null</c>，按文件数计。
    /// </summary>
    public int? DistinctFileCount { get; init; }

    /// <summary>参与计算的不同实体数：读不到标识时退化为文件数。</summary>
    public int EffectiveDistinctCount => Math.Max(1, DistinctFileCount ?? FileCount);

    /// <summary>保留的那一份（第一份，稳定排序后取首位）；其余文件会被改成指向它的硬链接。</summary>
    public DedupCandidateFile? KeepFile => Files.Count == 0 ? null : Files[0];

    /// <summary>本组可释放字节数 = 尺寸 × (不同实体数 − 1)。</summary>
    public long ReclaimableBytes => SizeBytes * (EffectiveDistinctCount - 1);

    /// <summary>本组涉及的总字节数（含保留的那一份），用于「重复占用」统计。</summary>
    public long TotalBytes => SizeBytes * FileCount;

    /// <summary>组内实例名（去重后按名称排序），供表格展示「跨哪些实例」。</summary>
    public IReadOnlyList<string> InstanceNames => Files
        .Select(file => file.InstanceName)
        .Distinct(StringComparer.Ordinal)
        .OrderBy(name => name, StringComparer.Ordinal)
        .ToArray();
}

/// <summary>扫描阶段被跳过的原因分类（口径见 docs/design/dedup.md）。</summary>
public enum DedupSkipKind
{
    /// <summary>文件小于最小候选尺寸。</summary>
    TooSmall,

    /// <summary>受管文件（注册表登记），默认不参与去重。</summary>
    Managed,

    /// <summary>符号链接或其它重解析点。</summary>
    Link,

    /// <summary>不在 GameData 之内。</summary>
    OutsideGameData,

    /// <summary>读不到属性/内容（权限、被占用）。</summary>
    Unreadable,

    /// <summary>实例不存在或 GameData 缺失。</summary>
    MissingInstance,

    /// <summary>跨卷（不能硬链接）。</summary>
    CrossVolume,

    /// <summary>平台不支持硬链接。</summary>
    Unsupported,
}

/// <summary>一条「跳过」记录（可读中文原因）。</summary>
public sealed record DedupSkipInfo(string InstanceName, string RelativePath, DedupSkipKind Kind, string Reason)
{
    /// <summary>「实例名: 相对路径 — 原因」，供界面直接展示。</summary>
    public string DisplayText => string.IsNullOrEmpty(RelativePath)
        ? $"{InstanceName}：{Reason}"
        : $"{InstanceName}: {RelativePath} — {Reason}";
}

/// <summary>当前平台/文件系统的硬链接能力。</summary>
public enum DedupPlatformSupport
{
    /// <summary>支持创建硬链接。</summary>
    Supported,

    /// <summary>平台没有硬链接能力（例如浏览器/WASI）。</summary>
    UnsupportedPlatform,
}

/// <summary>一次去重扫描的报告。</summary>
/// <param name="InstanceNames">实际参与扫描的实例名（按名称排序）。</param>
/// <param name="Groups">内容重复且同卷的分组（≥2 个文件）。</param>
/// <param name="Skipped">逐文件的跳过记录（最多 200 条，完整计数见各 Count 字段）。</param>
/// <param name="ScannedFileCount">GameData 内枚举到的文件总数。</param>
/// <param name="CandidateFileCount">进入内容哈希的候选文件数（≥最小尺寸且非受管/非链接）。</param>
/// <param name="SmallFileCount">因尺寸过小被跳过的文件数。</param>
/// <param name="ManagedFileCount">因受管被跳过的文件数。</param>
/// <param name="LinkFileCount">因是符号链接/重解析点被跳过的文件数。</param>
/// <param name="UnreadableFileCount">因读取失败被跳过的文件数。</param>
/// <param name="SkippedCount">跳过记录总数（可能大于 <paramref name="Skipped"/> 的条数）。</param>
/// <param name="Support">平台硬链接能力。</param>
public sealed record DedupScanReport(
    IReadOnlyList<string> InstanceNames,
    IReadOnlyList<DedupGroup> Groups,
    IReadOnlyList<DedupSkipInfo> Skipped,
    int ScannedFileCount,
    int CandidateFileCount,
    int SmallFileCount,
    int ManagedFileCount,
    int LinkFileCount,
    int UnreadableFileCount,
    int SkippedCount,
    DedupPlatformSupport Support)
{
    /// <summary>重复组数。</summary>
    public int GroupCount => Groups.Count;

    /// <summary>重复文件总数（各组文件数之和）。</summary>
    public int DuplicateFileCount => Groups.Sum(group => group.FileCount);

    /// <summary>可释放字节合计。</summary>
    public long ReclaimableBytes => Groups.Sum(group => group.ReclaimableBytes);

    /// <summary>重复内容占用的总字节（含保留副本）。</summary>
    public long DuplicateBytes => Groups.Sum(group => group.TotalBytes);

    /// <summary>是否有可去重的分组。</summary>
    public bool HasDuplicates => Groups.Count > 0;

    /// <summary>空报告（实例列表为空或全部实例都不可读时使用）。</summary>
    public static DedupScanReport Empty(DedupPlatformSupport support) => new(
        Array.Empty<string>(),
        Array.Empty<DedupGroup>(),
        Array.Empty<DedupSkipInfo>(),
        0,
        0,
        0,
        0,
        0,
        0,
        0,
        support);
}
