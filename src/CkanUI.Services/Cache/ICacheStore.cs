using CkanUI.Services.Abstractions;

namespace CkanUI.Services.Cache;

/// <summary>缓存目录里的一个文件事实（不含「是否被已安装模组引用」这类需要注册表状态的判断）。</summary>
/// <param name="FileName">缓存文件名。</param>
/// <param name="SizeBytes">字节数。</param>
/// <param name="ModifiedTimeUtc">最后修改时间（UTC）。</param>
/// <param name="Identifier">能解析出的模块标识符。</param>
/// <param name="Version">能解析出的模块版本。</param>
public sealed record CacheFileFact(
    string FileName,
    long SizeBytes,
    DateTime ModifiedTimeUtc,
    string? Identifier,
    string? Version);

/// <summary>
/// 把 CKAN 上游的触达面收在一处（<c>NetModuleCache</c> / <c>IConfiguration.CacheSizeLimit</c> /
/// <c>DownloadCacheDir</c> / <c>Registry</c>），好让 <see cref="CacheService"/> 的清理与校验逻辑
/// 能在没有游戏目录、没有网络的环境里被离线测试。
/// </summary>
/// <remarks>
/// 实现方只在「上游本身失败」时抛异常（IO、注册表读取），业务判定一律通过返回值表达；
/// <see cref="CacheService"/> 负责把异常转成可重试的失败结果。
/// </remarks>
public interface ICacheStore
{
    /// <summary>缓存目录的绝对路径；没有可用缓存目录时为 <c>null</c>。</summary>
    string? CacheDirectory { get; }

    /// <summary>容量上限字节（<c>IConfiguration.CacheSizeLimit</c>）；<c>null</c> 或 <c>&lt;= 0</c> 表示不限制。</summary>
    long? SizeLimitBytes { get; }

    /// <summary>列出缓存目录里的条目（不含伴生哈希文件与下载中的临时文件）。</summary>
    IReadOnlyList<CacheFileFact> ListEntries();

    /// <summary>该条目是否正被当前实例里已安装的模组引用。</summary>
    bool IsReferenced(string fileName);

    /// <summary>删除单个条目及其伴生哈希文件；文件本来就不在时返回 <c>false</c>。</summary>
    bool Delete(string fileName);

    /// <summary>
    /// 把本地 ZIP 灌进缓存：先用 CKAN 的 zip 校验，再识别模块元数据，最后复制进缓存目录。
    /// 校验不过或识别不出模块时返回失败（含中文原因）。
    /// </summary>
    OperationResult<CacheFileFact> Import(string sourceZipPath, CancellationToken ct);

    /// <summary>设置缓存容量上限（字节）；<c>null</c> 或 <c>&lt;= 0</c> 表示不限制。</summary>
    bool SetSizeLimitBytes(long? limitBytes);
}
