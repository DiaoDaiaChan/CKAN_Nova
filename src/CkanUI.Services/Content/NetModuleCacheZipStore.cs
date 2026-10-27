using CkanUI.Services.Abstractions;
using CkanUI.Services.Abstractions.Dtos;
using CKAN;

namespace CkanUI.Services.Content;

/// <summary>用 CKAN NetModuleCache 解析缓存路径，缺失时经 IDownloadService 拉取模块 ZIP。</summary>
public sealed class NetModuleCacheZipStore : IContentZipStore
{
    private readonly GameInstanceManager _instances;
    private readonly RepositoryDataManager _repositories;
    private readonly IDownloadService _downloads;
    public NetModuleCacheZipStore(GameInstanceManager instances, RepositoryDataManager repositories, IDownloadService downloads)
    { _instances = instances ?? throw new ArgumentNullException(nameof(instances)); _repositories = repositories ?? throw new ArgumentNullException(nameof(repositories)); _downloads = downloads ?? throw new ArgumentNullException(nameof(downloads)); }
    private CkanModule? Find(string identifier)
    {
        var instance = _instances.CurrentInstance; if (instance is null) return null;
        var registry = RegistryManager.Instance(instance, _repositories).registry;
        return registry.InstalledModule(identifier)?.Module ?? registry.LatestAvailable(identifier, instance.StabilityToleranceConfig, instance.VersionCriteria());
    }
    public bool IsCached(string identifier) { var module = Find(identifier); return module is not null && _instances.Cache?.IsMaybeCachedZip(module) == true; }
    public string? GetCachedPath(string identifier) { var module = Find(identifier); return module is not null && _instances.Cache?.IsMaybeCachedZip(module) == true ? _instances.Cache.GetCachedFilename(module) : null; }
    public async Task<OperationResult<string>> EnsureCachedAsync(string identifier, IProgress<OperationProgress>? progress, CancellationToken ct)
    {
        try
        {
            var module = Find(identifier); var cache = _instances.Cache;
            if (module is null || cache is null) return OperationResult<string>.Fail("找不到模组或下载缓存不可用。");
            if (cache.IsMaybeCachedZip(module) && cache.GetCachedFilename(module) is { } existing) return OperationResult<string>.Ok(existing);
            if (module.download is not { Count: > 0 } urls) return OperationResult<string>.Fail("该模组没有可用的下载地址。");
            // NetModuleCache 只提供"查已存在的缓存名"（GetCachedFilename(CkanModule)），拿不到新文件的
            // 目标名，所以先下到缓存目录里的临时文件，再交给 Store 校验并搬进缓存。
            var cacheDir = _instances.Configuration?.DownloadCacheDir;
            if (string.IsNullOrWhiteSpace(cacheDir))
            {
                return OperationResult<string>.Fail("尚未配置下载缓存目录，无法把内容下载到本地。");
            }

            Directory.CreateDirectory(cacheDir);
            var destination = Path.Combine(cacheDir, identifier + "-" + Guid.NewGuid().ToString("N") + ".zip.part");
            var result = await _downloads.DownloadAsync(
                new DownloadRequest(urls.ToArray(), destination, module.download_size, DisplayName: identifier),
                null, ct).ConfigureAwait(false);
            if (!result.IsSuccess || result.Value is null)
            {
                TryDelete(destination);
                return OperationResult<string>.Fail(result.Message ?? "下载到缓存失败。", result.Detail, result.Retryable);
            }

            string stored;
            try
            {
                stored = cache.Store(module, result.Value.DestinationPath, null, identifier, move: true, validate: true);
            }
            catch (Exception ex)
            {
                TryDelete(destination);
                return OperationResult<string>.Fail("下载的文件未通过校验，已丢弃。", ex.Message, true);
            }

            progress?.Report(new OperationProgress("content-download", "内容下载", identifier, "下载完成", 100, true, OperationState.Succeeded));
            return OperationResult<string>.Ok(stored);
        }
        catch (OperationCanceledException ex) { return OperationResult<string>.Fail("操作已取消。", ex.Message, true); }
        catch (Exception ex) { return OperationResult<string>.Fail("无法下载该模组的 ZIP 到缓存。", ex.Message, false); }
    }

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); }
        catch (Exception) { /* 临时文件删不掉不影响结论。 */ }
    }
}
