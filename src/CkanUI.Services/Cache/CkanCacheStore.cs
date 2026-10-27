using System.IO.Compression;
using System.Text.RegularExpressions;

using CkanUI.Services.Abstractions;

using CKAN;

namespace CkanUI.Services.Cache;

/// <summary>
/// <see cref="ICacheStore"/> 的 Core 实现：把上游 <c>NetModuleCache</c> / <c>IConfiguration</c> /
/// <c>Registry</c> 的能力收窄成「列目录、判引用、删一个、灌一个」，好让 <see cref="CacheService"/>
/// 的清理逻辑能在没有游戏目录的环境里被离线测试。
/// </summary>
/// <remarks>
/// <para>格式语义全部来自上游，本类不自定义任何缓存文件命名规则：缓存文件名由
/// <c>NetFileCache.Store</c> 生成为 <c>&lt;URL 哈希前 8 位大写十六进制&gt;-&lt;模块名&gt;.zip</c>
/// （<c>external/CKAN/Core/Net/NetFileCache.cs:401</c>），条目标识符/版本则靠
/// <c>Registry.GetDownloadUrlHashIndex()</c>（<c>external/CKAN/Core/Registry/Registry.cs:1287</c>）
/// 按文件名前缀反查，因此这里只认 <c>^[0-9A-F]{8}-</c> 的文件，与上游 <c>cacheFileRegex</c> 一致。</para>
/// <para>ZIP 灌缓存走的是 <c>ckan import</c> 同一条路径：先用
/// <c>Registry.GetDownloadHashesIndex()</c> 按 SHA256/SHA1 匹配仓库里的模块，匹配不到再读 ZIP 内部的
/// <c>*.ckan</c> 元数据并补一个 <c>https://ckan/imported-from-zip/…</c> 下载地址
/// （与 <c>external/CKAN/Core/IO/ModuleImporter.cs:186</c> 的 <c>GetInternalModules</c> 同义），
/// 最后交给 <c>NetModuleCache.Store</c> 落盘。</para>
/// <para>注意：本程序集里有 <c>CkanUI.Services.Registry</c> 命名空间，<c>CKAN.Registry</c> 必须写全名。</para>
/// </remarks>
internal sealed class CkanCacheStore : ICacheStore
{
    /// <summary>上游 <c>NetFileCache.cacheFileRegex</c>：CKAN 写进缓存的文件都以 8 位大写十六进制加连字符开头。</summary>
    private static readonly Regex EntryNamePattern = new("^[0-9A-F]{8}-", RegexOptions.Compiled);

    private const string ImportedFromZipHost = "https://ckan/imported-from-zip/";

    /// <summary>上游哈希 API 要求非空的进度回调；灌缓存时不需要进度，给一个空实现。</summary>
    private static readonly IProgress<int> NoProgress = new Progress<int>();

    private readonly GameInstanceManager _instances;
    private readonly RepositoryDataManager _repositoryData;

    /// <summary>构造 Core 存储。</summary>
    public CkanCacheStore(GameInstanceManager instances, RepositoryDataManager repositoryData)
    {
        _instances = instances ?? throw new ArgumentNullException(nameof(instances));
        _repositoryData = repositoryData ?? throw new ArgumentNullException(nameof(repositoryData));
    }

    /// <inheritdoc />
    public string? CacheDirectory
    {
        get
        {
            var configured = _instances.Configuration.DownloadCacheDir;
            var dir = string.IsNullOrWhiteSpace(configured)
                ? GameInstanceManager.DefaultDownloadCacheDir
                : configured;
            return string.IsNullOrWhiteSpace(dir) ? null : dir;
        }
    }

    /// <inheritdoc />
    public long? SizeLimitBytes => _instances.Configuration.CacheSizeLimit;

    /// <inheritdoc />
    public IReadOnlyList<CacheFileFact> ListEntries()
    {
        if (CacheDirectory is not { } dir || !Directory.Exists(dir))
        {
            return Array.Empty<CacheFileFact>();
        }

        var index = TryUrlHashIndex();
        var facts = new List<CacheFileFact>();
        foreach (var path in Directory.EnumerateFiles(dir, "*.zip"))
        {
            var name = Path.GetFileName(path);
            if (!IsCacheEntryName(name))
            {
                continue;
            }

            var info = new FileInfo(path);
            var (identifier, version) = ResolveModule(name, index);
            facts.Add(new CacheFileFact(name, info.Length, info.LastWriteTimeUtc, identifier, version));
        }

        return facts;
    }

    /// <inheritdoc />
    public bool IsReferenced(string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName) || _instances.Cache is not { } cache)
        {
            return false;
        }

        if (TryRegistry() is not { } registry)
        {
            return false;
        }

        foreach (var installed in registry.InstalledModules)
        {
            string? cached;
            try
            {
                cached = cache.GetCachedFilename(installed.Module);
            }
            catch (Exception)
            {
                // 元数据缺 download 字段的已装模块不参与引用判定，不让一条脏数据挡住整次清理。
                continue;
            }

            if (cached is not null
                && string.Equals(Path.GetFileName(cached), fileName, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    /// <inheritdoc />
    public bool Delete(string fileName)
    {
        if (CacheDirectory is not { } dir || string.IsNullOrWhiteSpace(fileName))
        {
            return false;
        }

        var path = Path.Combine(dir, fileName);
        if (!File.Exists(path))
        {
            return false;
        }

        File.Delete(path);
        // 伴生哈希文件是主文件的附属品，不单独算条目（上游 EnforceSizeLimit 同样顺手删掉）。
        DeleteCompanion(path + ".sha1");
        DeleteCompanion(path + ".sha256");
        return true;
    }

    /// <inheritdoc />
    public OperationResult<CacheFileFact> Import(string sourceZipPath, CancellationToken ct)
    {
        if (!File.Exists(sourceZipPath))
        {
            return OperationResult<CacheFileFact>.Fail(
                "找不到要导入的 ZIP 文件。",
                sourceZipPath,
                retryable: false);
        }

        ct.ThrowIfCancellationRequested();
        if (!NetModuleCache.ZipValid(sourceZipPath, out var invalidReason, null, ct))
        {
            return OperationResult<CacheFileFact>.Fail(
                "这个文件不是有效的 ZIP 压缩包，无法灌入下载缓存。",
                "NetModuleCache.ZipValid 报告：" + invalidReason,
                retryable: false);
        }

        if (_instances.Cache is not { } cache)
        {
            return OperationResult<CacheFileFact>.Fail(
                "当前没有可用的下载缓存目录。",
                "GameInstanceManager.Cache 为空；请先在设置里配置下载缓存目录。",
                retryable: true);
        }

        var module = IdentifyModule(sourceZipPath, cache);
        if (module is null)
        {
            return OperationResult<CacheFileFact>.Fail(
                "这个 ZIP 里没有可识别的 CKAN 模块信息，无法灌入下载缓存。",
                DescribeZip(sourceZipPath),
                retryable: false);
        }

        ct.ThrowIfCancellationRequested();
        string stored;
        try
        {
            stored = cache.Store(
                module,
                sourceZipPath,
                progress: null,
                description: null,
                move: false,
                cancelToken: ct,
                validate: false);
        }
        catch (Exception ex)
        {
            return OperationResult<CacheFileFact>.Fail(
                "写入下载缓存失败。",
                ex.ToString(),
                retryable: true);
        }

        if (string.IsNullOrEmpty(stored))
        {
            return OperationResult<CacheFileFact>.Fail(
                "写入下载缓存失败：这个模块没有下载地址，CKAN 无法为它生成缓存文件名。",
                "module.download 为空（" + module.identifier + "）。",
                retryable: false);
        }

        var file = new FileInfo(stored);
        return OperationResult<CacheFileFact>.Ok(new CacheFileFact(
            Path.GetFileName(stored),
            file.Length,
            file.LastWriteTimeUtc,
            module.identifier,
            module.version?.ToString()));
    }

    private static bool IsCacheEntryName(string fileName)
        => EntryNamePattern.IsMatch(fileName)
           && !fileName.EndsWith(".sha1", StringComparison.OrdinalIgnoreCase)
           && !fileName.EndsWith(".sha256", StringComparison.OrdinalIgnoreCase);

    private static void DeleteCompanion(string path)
    {
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }

    private static (string? Identifier, string? Version) ResolveModule(
        string fileName,
        IReadOnlyDictionary<string, List<CkanModule>>? index)
    {
        if (index is null || fileName.Length < 8)
        {
            return (null, null);
        }

        return index.TryGetValue(fileName[..8], out var modules) && modules.Count > 0
            ? (modules[0].identifier, modules[0].version?.ToString())
            : (null, null);
    }

    /// <summary>读当前实例的注册表；没有选中实例或读取失败时返回 <c>null</c>。</summary>
    private CKAN.Registry? TryRegistry()
    {
        if (_instances.CurrentInstance is not { } instance)
        {
            return null;
        }

        try
        {
            return RegistryManager.Instance(instance, _repositoryData).registry;
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>读「下载 URL 哈希 → 模块」索引；没有注册表时返回 <c>null</c>。</summary>
    private IReadOnlyDictionary<string, List<CkanModule>>? TryUrlHashIndex()
    {
        try
        {
            return TryRegistry()?.GetDownloadUrlHashIndex();
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>读「下载内容哈希 → 模块」索引；没有注册表时返回 <c>null</c>。</summary>
    private IReadOnlyDictionary<string, List<CkanModule>>? TryDownloadHashesIndex()
    {
        try
        {
            return TryRegistry()?.GetDownloadHashesIndex();
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>按仓库哈希索引、再按 ZIP 内部 <c>*.ckan</c> 元数据识别模块（与 <c>ckan import</c> 同序）。</summary>
    private CkanModule? IdentifyModule(string path, NetModuleCache cache)
    {
        if (TryDownloadHashesIndex() is { } index)
        {
            try
            {
                var sha256 = cache.GetFileHashSha256(path, NoProgress);
                if (index.TryGetValue(sha256, out var bySha256) && bySha256.Count > 0)
                {
                    return bySha256[0];
                }

                var sha1 = cache.GetFileHashSha1(path, NoProgress);
                if (index.TryGetValue(sha1, out var bySha1) && bySha1.Count > 0)
                {
                    return bySha1[0];
                }
            }
            catch (Exception)
            {
                // 哈希计算失败（文件被占用等）不致命，继续尝试 ZIP 内部元数据。
            }
        }

        return ModuleFromZipMetadata(path);
    }

    /// <summary>
    /// 读 ZIP 内部根目录的 <c>*.ckan</c> 元数据；缺下载地址时补一个
    /// <c>https://ckan/imported-from-zip/&lt;标识符&gt;/&lt;文件名&gt;</c>（同上游 <c>GetInternalModules</c>）。
    /// </summary>
    private static CkanModule? ModuleFromZipMetadata(string path)
    {
        try
        {
            using var zip = ZipFile.OpenRead(path);
            var entry = zip.Entries.FirstOrDefault(
                e => e.FullName.EndsWith(".ckan", StringComparison.OrdinalIgnoreCase)
                     && !e.FullName.Contains('/', StringComparison.Ordinal));
            if (entry is null)
            {
                return null;
            }

            using var reader = new StreamReader(entry.Open());
            var module = CkanModule.FromJson(reader.ReadToEnd());
            if (module.download is null || module.download.Count == 0)
            {
                module.download = new List<Uri>
                {
                    new(ImportedFromZipHost + module.identifier + "/" + Path.GetFileName(path)),
                };
            }

            return module;
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>给「识别不出模块」的失败补一份中文检查清单。</summary>
    private string DescribeZip(string path)
    {
        var lines = new List<string>
        {
            "已检查：",
            "1. 仓库下载哈希索引（SHA256 / SHA1）里没有匹配的模块。",
        };

        try
        {
            using var zip = ZipFile.OpenRead(path);
            var ckanFiles = zip.Entries
                               .Where(e => e.FullName.EndsWith(".ckan", StringComparison.OrdinalIgnoreCase))
                               .Select(e => e.FullName)
                               .ToList();
            var hasGameData = zip.Entries.Any(
                e => e.FullName.StartsWith("GameData/", StringComparison.OrdinalIgnoreCase)
                     || e.FullName.Contains("/GameData/", StringComparison.OrdinalIgnoreCase));

            lines.Add(ckanFiles.Count == 0
                ? "2. ZIP 里没有 .ckan 模块元数据文件。"
                : "2. ZIP 里的 .ckan 元数据无法解析：" + string.Join("、", ckanFiles));
            lines.Add(hasGameData
                ? "3. 有 GameData/ 目录，但缺少可用的模块元数据。"
                : "3. ZIP 里没有 GameData/ 目录，不像 CKAN 的模块包。");
        }
        catch (Exception ex)
        {
            lines.Add("读取 ZIP 内容失败：" + ex.Message);
        }

        lines.Add("可用的做法：把模块 zip 交给 CKAN 的「导入已下载的模块」，或先把它加入仓库索引。");
        return string.Join(Environment.NewLine, lines);
    }
}
