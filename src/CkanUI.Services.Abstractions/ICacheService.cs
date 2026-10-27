using CkanUI.Services.Abstractions.Dtos;

namespace CkanUI.Services.Abstractions;

/// <summary>下载缓存的一次快照（12 篇 F-50 / 06 篇 UI-19）。</summary>
/// <param name="EntryCount">缓存条目数（不含 <c>.sha1</c>/<c>.sha256</c> 伴生文件与下载中的临时文件）。</param>
/// <param name="TotalBytes">全部条目字节合计。</param>
/// <param name="SizeLimitBytes">容量上限字节；<c>null</c> 表示「不限」。</param>
/// <remarks>上限语义与 CKAN 一致（<c>Core/Configuration/IConfiguration.cs:23</c>）：<c>null</c> 或 <c>&lt;= 0</c> 都视为不限制。</remarks>
/// <param name="CacheDirectory">缓存目录绝对路径；没有可用目录时为 <c>null</c>。</param>
public sealed record CacheSnapshot(int EntryCount, long TotalBytes, long? SizeLimitBytes, string? CacheDirectory = null)
{
    /// <summary>是否设置了有效的容量上限。</summary>
    public bool HasLimit => SizeLimitBytes is { } limit && limit > 0;

    /// <summary>距离上限还剩多少字节；未设上限时为 <c>null</c>。</summary>
    public long? RemainingBytes => SizeLimitBytes is { } limit && limit > 0
        ? Math.Max(0L, limit - TotalBytes)
        : null;

    /// <summary>使用率百分比（0–100，保留一位小数）；未设上限时为 0。</summary>
    public double UsagePercent
    {
        get
        {
            if (SizeLimitBytes is not { } limit || limit <= 0)
            {
                return 0d;
            }

            return Math.Min(100d, Math.Round(TotalBytes * 100d / limit, 1));
        }
    }
}

/// <summary>缓存里的一个条目（一个已下载的模块压缩包）。</summary>
/// <param name="FileName">缓存文件名（形如 <c>1A2B3C4D-Identifier-1.2.3.zip</c>）。</param>
/// <param name="SizeBytes">文件字节数。</param>
/// <param name="ModifiedTimeUtc">最后修改时间（UTC）；清理时按它从旧到新删。</param>
/// <param name="Identifier">能解析出的模块标识符；解析不出时为 <c>null</c>。</param>
/// <param name="Version">能解析出的模块版本；解析不出时为 <c>null</c>。</param>
/// <param name="IsReferenced">是否被当前实例里已安装的模组引用（引用中的条目不参与清理）。</param>
public sealed record CacheEntryInfo(
    string FileName,
    long SizeBytes,
    DateTime ModifiedTimeUtc,
    string? Identifier,
    string? Version,
    bool IsReferenced)
{
    /// <summary>人读的模块标签：<c>标识符 版本</c>，解析不出时退回「未知模块」。</summary>
    public string ModuleLabel => Identifier switch
    {
        null or "" => "未知模块",
        _ => string.IsNullOrEmpty(Version) ? Identifier : Identifier + " " + Version,
    };
}

/// <summary>一次清理的结果。</summary>
/// <param name="DeletedFileNames">实际删掉的文件名。</param>
/// <param name="FreedBytes">释放的字节数。</param>
/// <param name="SkippedFileNames">因为正被已安装模组引用而跳过的文件名（PurgeAll 也会跳过并在此报告）。</param>
public sealed record CachePurgeResult(
    IReadOnlyList<string> DeletedFileNames,
    long FreedBytes,
    IReadOnlyList<string> SkippedFileNames)
{
    /// <summary>实际删除条目数。</summary>
    public int DeletedCount => DeletedFileNames.Count;

    /// <summary>被跳过的条目数。</summary>
    public int SkippedCount => SkippedFileNames.Count;
}

/// <summary>下载缓存管理（12 篇 F-50 / F-40、06 篇 UI-19）。</summary>
/// <remarks>
/// <para>缓存目录、容量上限与「哪些条目被已安装模组引用」都由 CKAN 上游定义
/// （<c>NetFileCache</c> / <c>IConfiguration.CacheSizeLimit</c> / <c>Registry</c>），
/// 本契约只把它们投影成界面能消费的只读视图与三个动作。</para>
/// <para>全部成员都有 DIM 兜底实现（契约决策 §12 #9），默认返回不可重试失败，
/// 好让别的宿主（测试夹具、设计时）不必实现全部动作。</para>
/// </remarks>
public interface ICacheService
{
    /// <summary>读取缓存快照（条目数、总字节、上限与使用率）。</summary>
    Task<OperationResult<CacheSnapshot>> GetSnapshotAsync(CancellationToken ct = default)
        => Task.FromResult(OperationResult<CacheSnapshot>.Fail(
            "当前实现未提供下载缓存统计能力。",
            "ICacheService.GetSnapshotAsync 的默认实现（契约决策 §12 #9）。",
            retryable: false));

    /// <summary>列出缓存条目（按最后修改时间从新到旧）。</summary>
    Task<OperationResult<IReadOnlyList<CacheEntryInfo>>> ListEntriesAsync(CancellationToken ct = default)
        => Task.FromResult(OperationResult<IReadOnlyList<CacheEntryInfo>>.Fail(
            "当前实现未提供下载缓存列表能力。",
            "ICacheService.ListEntriesAsync 的默认实现（契约决策 §12 #9）。",
            retryable: false));

    /// <summary>
    /// 清理到上限：按最后修改时间从旧到新删除条目，直到总字节不超过 <c>CacheSizeLimit</c>。
    /// 上限为空或 <c>&lt;= 0</c> 时视为不限制，什么都不删。正被已安装模组引用的条目一律跳过。
    /// </summary>
    Task<OperationResult<CachePurgeResult>> PurgeToLimitAsync(
        IProgress<OperationProgress>? progress = null,
        CancellationToken ct = default)
        => Task.FromResult(OperationResult<CachePurgeResult>.Fail(
            "当前实现未提供按上限清理下载缓存的能力。",
            "ICacheService.PurgeToLimitAsync 的默认实现（契约决策 §12 #9）。",
            retryable: false));

    /// <summary>
    /// 清空缓存：删除全部条目，但跳过正被已安装模组引用的条目，并在结果里报告跳过数。
    /// </summary>
    Task<OperationResult<CachePurgeResult>> PurgeAllAsync(
        IProgress<OperationProgress>? progress = null,
        CancellationToken ct = default)
        => Task.FromResult(OperationResult<CachePurgeResult>.Fail(
            "当前实现未提供清空下载缓存的能力。",
            "ICacheService.PurgeAllAsync 的默认实现（契约决策 §12 #9）。",
            retryable: false));

    /// <summary>
    /// 把本地 ZIP 灌进缓存（12 篇 F-40）：先按 CKAN 的 zip 校验与元数据识别模块，
    /// 再复制进缓存目录，返回新条目。ZIP 不合法或识别不出模块时返回不可重试失败。
    /// </summary>
    Task<OperationResult<CacheEntryInfo>> ImportZipAsync(
        string sourceZipPath,
        CancellationToken ct = default)
        => Task.FromResult(OperationResult<CacheEntryInfo>.Fail(
            "当前实现未提供从 ZIP 导入下载缓存的能力。",
            "ICacheService.ImportZipAsync 的默认实现（契约决策 §12 #9）。",
            retryable: false));

    /// <summary>删除单个缓存条目；被已安装模组引用时拒绝（不可重试）。</summary>
    Task<OperationResult> DeleteEntryAsync(string fileName, CancellationToken ct = default)
        => Task.FromResult(OperationResult.Fail(
            "当前实现未提供删除下载缓存条目的能力。",
            "ICacheService.DeleteEntryAsync 的默认实现（契约决策 §12 #9）。",
            retryable: false));

    /// <summary>获取缓存目录的绝对路径；没有可用目录时返回 null。</summary>
    Task<OperationResult<string?>> GetCacheDirectoryAsync(CancellationToken ct = default)
        => Task.FromResult(OperationResult<string?>.Fail(
            "当前实现未提供获取下载缓存目录的能力。",
            "ICacheService.GetCacheDirectoryAsync 的默认实现（契约决策 §12 #9）。",
            retryable: false));

    /// <summary>设置或重置缓存容量上限（字节）；<c>null</c> 或 <c>&lt;= 0</c> 表示不限制。</summary>
    Task<OperationResult> SetSizeLimitBytesAsync(long? limitBytes, CancellationToken ct = default)
        => Task.FromResult(OperationResult.Fail(
            "当前实现未提供设置下载缓存上限的能力。",
            "ICacheService.SetSizeLimitBytesAsync 的默认实现（契约决策 §12 #9）。",
            retryable: false));
}
