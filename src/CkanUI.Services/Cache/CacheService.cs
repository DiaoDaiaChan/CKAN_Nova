using CkanUI.Services.Abstractions;
using CkanUI.Services.Abstractions.Dtos;

namespace CkanUI.Services.Cache;

/// <summary>
/// 下载缓存管理（12 篇 F-50 / F-40、06 篇 UI-19）：统计、按上限清理、清空、删除单条、ZIP 灌缓存。
/// </summary>
/// <remarks>
/// <para>清理口径（与 CKAN <c>NetFileCache.EnforceSizeLimit</c> 的意图一致，但更保守）：
/// 按最后修改时间从旧到新删，直到总字节不超过上限；**正被已安装模组引用的条目一律跳过**，
/// <see cref="PurgeAllAsync"/> 也跳过它们并在 <see cref="CachePurgeResult.SkippedFileNames"/> 里报告。</para>
/// <para>上限为 <c>null</c> 或 <c>&lt;= 0</c> 都视为「不限制」（CKAN <c>JsonConfiguration</c> 把负值归一为 <c>null</c>），
/// 此时 <see cref="PurgeToLimitAsync"/> 什么都不删。</para>
/// <para>上游异常（IO、注册表读取）一律转成 <c>retryable: true</c> 的失败结果，不向界面抛。</para>
/// </remarks>
public sealed class CacheService : ICacheService
{
    private const string TaskId = "cache";

    private readonly ICacheStore _store;

    /// <summary>用存储抽象构造（测试注入假实现）。</summary>
    public CacheService(ICacheStore store)
    {
        ArgumentNullException.ThrowIfNull(store);
        _store = store;
    }

    /// <inheritdoc />
    public Task<OperationResult<CacheSnapshot>> GetSnapshotAsync(CancellationToken ct = default)
    {
        try
        {
            ct.ThrowIfCancellationRequested();
            var entries = _store.ListEntries();
            var total = entries.Sum(entry => entry.SizeBytes);
            return Task.FromResult(OperationResult<CacheSnapshot>.Ok(
                new CacheSnapshot(entries.Count, total, _store.SizeLimitBytes, _store.CacheDirectory)));
        }
        catch (Exception ex)
        {
            return Task.FromResult(OperationResult<CacheSnapshot>.Fail(
                "读取下载缓存统计失败。", ex.ToString(), retryable: true));
        }
    }

    /// <inheritdoc />
    public Task<OperationResult<IReadOnlyList<CacheEntryInfo>>> ListEntriesAsync(CancellationToken ct = default)
    {
        try
        {
            ct.ThrowIfCancellationRequested();
            var entries = _store.ListEntries()
                                .OrderByDescending(fact => fact.ModifiedTimeUtc)
                                .Select(ToEntryInfo)
                                .ToList();
            return Task.FromResult(OperationResult<IReadOnlyList<CacheEntryInfo>>.Ok(entries));
        }
        catch (Exception ex)
        {
            return Task.FromResult(OperationResult<IReadOnlyList<CacheEntryInfo>>.Fail(
                "读取下载缓存列表失败。", ex.ToString(), retryable: true));
        }
    }

    /// <inheritdoc />
    public Task<OperationResult<CachePurgeResult>> PurgeToLimitAsync(
        IProgress<OperationProgress>? progress = null,
        CancellationToken ct = default)
        => Task.FromResult(PurgeToLimit(progress, ct));

    /// <inheritdoc />
    public Task<OperationResult<CachePurgeResult>> PurgeAllAsync(
        IProgress<OperationProgress>? progress = null,
        CancellationToken ct = default)
        => Task.FromResult(PurgeAll(progress, ct));

    /// <inheritdoc />
    public Task<OperationResult<CacheEntryInfo>> ImportZipAsync(
        string sourceZipPath,
        CancellationToken ct = default)
    {
        try
        {
            ct.ThrowIfCancellationRequested();
            if (string.IsNullOrWhiteSpace(sourceZipPath))
            {
                return Task.FromResult(OperationResult<CacheEntryInfo>.Fail(
                    "没有指定要导入的 ZIP 文件。", "sourceZipPath 为空。", retryable: false));
            }

            var result = _store.Import(sourceZipPath, ct);
            return Task.FromResult(result.IsSuccess
                ? OperationResult<CacheEntryInfo>.Ok(ToEntryInfo(result.Value!))
                : OperationResult<CacheEntryInfo>.Fail(
                    result.Message ?? "从 ZIP 灌入下载缓存失败。",
                    result.Detail,
                    result.Retryable));
        }
        catch (Exception ex)
        {
            return Task.FromResult(OperationResult<CacheEntryInfo>.Fail(
                "从 ZIP 灌入下载缓存失败。", ex.ToString(), retryable: true));
        }
    }

    /// <inheritdoc />
    public Task<OperationResult> DeleteEntryAsync(string fileName, CancellationToken ct = default)
    {
        try
        {
            ct.ThrowIfCancellationRequested();
            if (string.IsNullOrWhiteSpace(fileName))
            {
                return Task.FromResult(OperationResult.Fail(
                    "没有指定要删除的缓存条目。", "fileName 为空。", retryable: false));
            }

            var entry = _store.ListEntries()
                              .FirstOrDefault(fact => string.Equals(
                                  fact.FileName, fileName, StringComparison.OrdinalIgnoreCase));
            if (entry is null)
            {
                return Task.FromResult(OperationResult.Fail(
                    "下载缓存里已经没有这个条目。",
                    "文件可能已被外部程序删除：" + fileName,
                    retryable: true));
            }

            if (_store.IsReferenced(entry.FileName))
            {
                return Task.FromResult(OperationResult.Fail(
                    "这个条目正被已安装的模组使用，不能删除。",
                    "先卸载引用它的模组，或改用「清理到上限」："
                    + entry.FileName,
                    retryable: false));
            }

            return Task.FromResult(_store.Delete(entry.FileName)
                ? OperationResult.Ok()
                : OperationResult.Fail(
                    "删除缓存条目失败。",
                    "文件在删除过程中消失或被占用：" + entry.FileName,
                    retryable: true));
        }
        catch (Exception ex)
        {
            return Task.FromResult(OperationResult.Fail(
                "删除缓存条目失败。", ex.ToString(), retryable: true));
        }
    }

    /// <inheritdoc />
    public Task<OperationResult<string?>> GetCacheDirectoryAsync(CancellationToken ct = default)
    {
        try
        {
            ct.ThrowIfCancellationRequested();
            var dir = _store.CacheDirectory;
            return Task.FromResult(OperationResult<string?>.Ok(dir));
        }
        catch (Exception ex)
        {
            return Task.FromResult(OperationResult<string?>.Fail(
                "获取下载缓存目录失败。", ex.ToString(), retryable: true));
        }
    }

    /// <inheritdoc />
    public Task<OperationResult> SetSizeLimitBytesAsync(long? limitBytes, CancellationToken ct = default)
    {
        try
        {
            ct.ThrowIfCancellationRequested();
            var normalized = limitBytes is { } limit && limit > 0 ? limit : (long?)null;
            var success = _store.SetSizeLimitBytes(normalized);
            return Task.FromResult(success
                ? OperationResult.Ok()
                : OperationResult.Fail("设置下载缓存上限失败。", "存储层未能保存容量上限配置。", retryable: true));
        }
        catch (Exception ex)
        {
            return Task.FromResult(OperationResult.Fail(
                "设置下载缓存上限失败。", ex.ToString(), retryable: true));
        }
    }

    private OperationResult<CachePurgeResult> PurgeToLimit(
        IProgress<OperationProgress>? progress,
        CancellationToken ct)
    {
        try
        {
            ct.ThrowIfCancellationRequested();
            if (_store.SizeLimitBytes is not { } limit || limit <= 0)
            {
                return OperationResult<CachePurgeResult>.Ok(Empty);
            }

            var entries = _store.ListEntries()
                                .OrderBy(fact => fact.ModifiedTimeUtc)
                                .ToList();
            var total = entries.Sum(fact => fact.SizeBytes);
            var deleted = new List<string>();
            var skipped = new List<string>();
            long freed = 0;
            var processed = 0;

            foreach (var entry in entries)
            {
                ct.ThrowIfCancellationRequested();
                if (total <= limit)
                {
                    break;
                }

                if (_store.IsReferenced(entry.FileName))
                {
                    skipped.Add(entry.FileName);
                    processed++;
                    Report(progress, "purge", "清理到上限", entry.FileName + "（被已安装模组引用，跳过）", Percent(processed, entries.Count));
                    continue;
                }

                if (!_store.Delete(entry.FileName))
                {
                    processed++;
                    continue;
                }

                deleted.Add(entry.FileName);
                freed += entry.SizeBytes;
                total -= entry.SizeBytes;
                processed++;
                Report(progress, "purge", "清理到上限", entry.FileName, Percent(processed, entries.Count));
            }

            ReportDone(progress, "purge", "清理到上限", deleted.Count);
            return OperationResult<CachePurgeResult>.Ok(
                new CachePurgeResult(deleted, freed, skipped));
        }
        catch (Exception ex)
        {
            return OperationResult<CachePurgeResult>.Fail(
                "按上限清理下载缓存失败。", ex.ToString(), retryable: true);
        }
    }

    private OperationResult<CachePurgeResult> PurgeAll(
        IProgress<OperationProgress>? progress,
        CancellationToken ct)
    {
        try
        {
            ct.ThrowIfCancellationRequested();
            var entries = _store.ListEntries();
            var deleted = new List<string>();
            var skipped = new List<string>();
            long freed = 0;
            var processed = 0;

            foreach (var entry in entries)
            {
                ct.ThrowIfCancellationRequested();
                processed++;
                if (_store.IsReferenced(entry.FileName))
                {
                    skipped.Add(entry.FileName);
                    Report(progress, "purge", "清空下载缓存", entry.FileName + "（被已安装模组引用，跳过）", Percent(processed, entries.Count));
                    continue;
                }

                if (!_store.Delete(entry.FileName))
                {
                    continue;
                }

                deleted.Add(entry.FileName);
                freed += entry.SizeBytes;
                Report(progress, "purge", "清空下载缓存", entry.FileName, Percent(processed, entries.Count));
            }

            ReportDone(progress, "purge", "清空下载缓存", deleted.Count);
            return OperationResult<CachePurgeResult>.Ok(
                new CachePurgeResult(deleted, freed, skipped));
        }
        catch (Exception ex)
        {
            return OperationResult<CachePurgeResult>.Fail(
                "清空下载缓存失败。", ex.ToString(), retryable: true);
        }
    }

    private CacheEntryInfo ToEntryInfo(CacheFileFact fact)
        => new(
            fact.FileName,
            fact.SizeBytes,
            fact.ModifiedTimeUtc,
            fact.Identifier,
            fact.Version,
            _store.IsReferenced(fact.FileName));

    private static CachePurgeResult Empty { get; } =
        new(Array.Empty<string>(), 0, Array.Empty<string>());

    private static double Percent(int processed, int total)
        => total <= 0 ? 100d : Math.Min(100d, processed * 100d / total);

    private static void Report(
        IProgress<OperationProgress>? progress,
        string kind,
        string title,
        string? message,
        double percent)
        => progress?.Report(new OperationProgress(
            TaskId, kind, title, message, percent, CanCancel: false, OperationState.Running));

    private static void ReportDone(
        IProgress<OperationProgress>? progress,
        string kind,
        string title,
        int deletedCount)
        => progress?.Report(new OperationProgress(
            TaskId,
            kind,
            title,
            deletedCount == 0 ? "没有删除任何条目。" : "已删除 " + deletedCount + " 个条目。",
            100d,
            CanCancel: false,
            OperationState.Succeeded));
}
