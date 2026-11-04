using CkanUI.Services.Abstractions;

namespace CkanUI.Services.Abstractions.Profiles;

/// <summary>一个配置档占用的磁盘空间。</summary>
public sealed record ProfileStorageRow(string ProfileName, bool IsActive, long Bytes, int FileCount);

/// <summary>一个实例的磁盘占用。</summary>
/// <param name="InstanceName">实例名。</param>
/// <param name="GameDir">游戏根目录。</param>
/// <param name="LiveGameDataBytes">当前生效的 GameData 字节数。</param>
/// <param name="CkanBytes">当前生效的 CKAN 目录字节数（registry、缓存元数据等）。</param>
/// <param name="ProfileBytes">CKANProfiles 下各配置档合计字节数。</param>
/// <param name="TotalBytes">以上三者之和。</param>
/// <param name="Profiles">各配置档明细（含当前生效那一档的估算值）。</param>
/// <param name="FreeBytes">所在卷剩余空间；取不到为 null。</param>
/// <param name="VolumeBytes">所在卷总容量；取不到为 null。</param>
public sealed record InstanceStorageRow(
    string InstanceName,
    string GameDir,
    long LiveGameDataBytes,
    long CkanBytes,
    long ProfileBytes,
    long TotalBytes,
    IReadOnlyList<ProfileStorageRow> Profiles,
    long? FreeBytes,
    long? VolumeBytes)
{
    /// <summary>配置档数量（不含当前生效那一档的虚拟条目）。</summary>
    public int ProfileCount => Profiles.Count;
}

/// <summary>多实例存储总览。</summary>
/// <param name="Instances">按实例名排序。</param>
/// <param name="TotalBytes">所有实例合计。</param>
/// <param name="TotalProfileBytes">所有配置档合计。</param>
/// <param name="TakenAtUtc">扫描时刻。</param>
/// <param name="Diagnostics">取不到大小的目录（权限、占用等）。</param>
public sealed record InstanceStorageReport(
    IReadOnlyList<InstanceStorageRow> Instances,
    long TotalBytes,
    long TotalProfileBytes,
    DateTime TakenAtUtc,
    IReadOnlyList<string> Diagnostics)
{
    /// <summary>没有任何实例。</summary>
    public bool IsEmpty => Instances.Count == 0;
}

/// <summary>配置档去重结果（同一实例内多个配置档的相同文件改成一个硬链接）。</summary>
/// <param name="InstanceName">实例名。</param>
/// <param name="DryRun">true 表示只估算没有写盘。</param>
/// <param name="GroupCount">重复文件组数。</param>
/// <param name="DuplicateFileCount">可合并的文件个数。</param>
/// <param name="ReclaimableBytes">可释放字节数。</param>
/// <param name="Warnings">跳过原因等。</param>
public sealed record ProfileDedupReport(
    string InstanceName,
    bool DryRun,
    int GroupCount,
    int DuplicateFileCount,
    long ReclaimableBytes,
    IReadOnlyList<string> Warnings);

/// <summary>多实例磁盘占用与配置档去重。</summary>
public interface IInstanceStorageService
{
    /// <summary>扫描全部实例的磁盘占用（GameData、CKAN、各配置档、所在卷剩余空间）。</summary>
    Task<OperationResult<InstanceStorageReport>> ScanAsync(CancellationToken ct = default);

    /// <summary>
    /// 把同一实例多个配置档里内容相同的文件合并成硬链接（默认只估算）。
    /// 只处理同卷普通文件，至少 128 KiB；跳过符号链接与正在生效的 GameData。
    /// </summary>
    Task<OperationResult<ProfileDedupReport>> DeduplicateProfilesAsync(string instanceName, bool dryRun = true, CancellationToken ct = default);
}
