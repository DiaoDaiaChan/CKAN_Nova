using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;
using CkanUI.Services.Abstractions;

namespace CkanUI.Services.Spacedock;

/// <summary>
/// SpaceDock 模组截图抓取服务实现。
/// </summary>
public sealed class SpacedockImageService : ISpacedockImageService, IDisposable
{
    private static readonly Regex ModIdRegex = new(@"spacedock\.info/mod/(\d+)", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private const long MaxImageSizeBytes = 10L * 1024 * 1024; // 10 MB 单图上限
    private const long MaxCacheBytes = 24L * 1024 * 1024; // 图片字节缓存上限：浏览几十页后不该把整站封面留在内存里
    private const int MaxCacheEntries = 160;

    private readonly HttpClient _client;
    private readonly bool _ownsClient;
    private readonly object _cacheLock = new();
    private readonly Dictionary<int, byte[]> _cache = new();
    private readonly Queue<int> _cacheOrder = new();
    private long _cacheBytes;

    public SpacedockImageService()
        : this(new HttpClient { Timeout = TimeSpan.FromSeconds(15) }, ownsClient: true)
    {
    }

    public SpacedockImageService(HttpMessageHandler handler)
        : this(new HttpClient(handler, disposeHandler: true) { Timeout = TimeSpan.FromSeconds(15) }, ownsClient: true)
    {
    }

    public SpacedockImageService(HttpClient client)
        : this(client, ownsClient: false)
    {
    }

    private SpacedockImageService(HttpClient client, bool ownsClient)
    {
        ArgumentNullException.ThrowIfNull(client);
        _client = client;
        _ownsClient = ownsClient;
    }

    /// <inheritdoc />
    public async Task<OperationResult<byte[]>> FetchScreenshotAsync(string spacedockUrl, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(spacedockUrl))
        {
            return OperationResult<byte[]>.Fail("SpaceDock 链接为空");
        }

        var match = ModIdRegex.Match(spacedockUrl);
        if (!match.Success || !int.TryParse(match.Groups[1].Value, out var modId))
        {
            return OperationResult<byte[]>.Fail("无法从 URL 解析 SpaceDock 模组 ID", spacedockUrl);
        }

        lock (_cacheLock)
        {
            if (_cache.TryGetValue(modId, out var cachedBytes))
            {
                return OperationResult<byte[]>.Ok(cachedBytes);
            }
        }

        try
        {
            var apiUrl = $"https://spacedock.info/api/mod/{modId}";
            using var apiResponse = await _client.GetAsync(apiUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
            if (!apiResponse.IsSuccessStatusCode)
            {
                return OperationResult<byte[]>.Fail($"SpaceDock API 请求失败 (HTTP {(int)apiResponse.StatusCode})", apiUrl);
            }

            var jsonStream = await apiResponse.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            using var doc = await JsonDocument.ParseAsync(jsonStream, cancellationToken: cancellationToken).ConfigureAwait(false);
            var root = doc.RootElement;

            string? imageUrl = null;
            if (root.TryGetProperty("background", out var bgProp) && bgProp.ValueKind == JsonValueKind.String)
            {
                var val = bgProp.GetString();
                if (!string.IsNullOrWhiteSpace(val))
                {
                    imageUrl = val;
                }
            }

            if (string.IsNullOrWhiteSpace(imageUrl) && root.TryGetProperty("thumbnail", out var thumbProp) && thumbProp.ValueKind == JsonValueKind.String)
            {
                var val = thumbProp.GetString();
                if (!string.IsNullOrWhiteSpace(val))
                {
                    imageUrl = val;
                }
            }

            if (string.IsNullOrWhiteSpace(imageUrl))
            {
                return OperationResult<byte[]>.Fail("该模组在 SpaceDock 上未提供背景图或缩略图");
            }

            if (imageUrl.StartsWith('/'))
            {
                imageUrl = "https://spacedock.info" + imageUrl;
            }

            using var imgResponse = await _client.GetAsync(imageUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
            if (!imgResponse.IsSuccessStatusCode)
            {
                return OperationResult<byte[]>.Fail($"下载模组截图失败 (HTTP {(int)imgResponse.StatusCode})", imageUrl);
            }

            if (imgResponse.Content.Headers.ContentLength > MaxImageSizeBytes)
            {
                return OperationResult<byte[]>.Fail("截图大小超过 10MB 上限，已取消下载以保护内存");
            }

            var imageBytes = await imgResponse.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
            if (imageBytes.Length > MaxImageSizeBytes)
            {
                return OperationResult<byte[]>.Fail("截图大小超过 10MB 上限，已取消下载以保护内存");
            }

            StoreInCache(modId, imageBytes);
            return OperationResult<byte[]>.Ok(imageBytes);
        }
        catch (OperationCanceledException)
        {
            return OperationResult<byte[]>.Fail("下载截图已取消");
        }
        catch (Exception ex)
        {
            return OperationResult<byte[]>.Fail("获取 SpaceDock 截图失败", ex.Message);
        }
    }

    /// <summary>当前缓存的图片份数；只给测试与探针读。</summary>
    public int CachedEntryCount
    {
        get
        {
            lock (_cacheLock)
            {
                return _cache.Count;
            }
        }
    }

    /// <summary>当前缓存占用的源字节数；只给测试与探针读。</summary>
    public long CachedBytes
    {
        get
        {
            lock (_cacheLock)
            {
                return _cacheBytes;
            }
        }
    }

    /// <summary>把图片字节按先来先出放进缓存，超预算就从最旧的开始丢。</summary>
    private void StoreInCache(int modId, byte[] imageBytes)
    {
        lock (_cacheLock)
        {
            if (_cache.TryGetValue(modId, out var previous))
            {
                _cacheBytes -= previous.Length;
            }
            else
            {
                _cacheOrder.Enqueue(modId);
            }

            _cache[modId] = imageBytes;
            _cacheBytes += imageBytes.Length;

            while ((_cache.Count > MaxCacheEntries || _cacheBytes > MaxCacheBytes) && _cacheOrder.Count > 0)
            {
                var oldest = _cacheOrder.Dequeue();
                if (_cache.Remove(oldest, out var evicted))
                {
                    _cacheBytes -= evicted.Length;
                }
            }
        }
    }

    public void Dispose()
    {
        lock (_cacheLock)
        {
            _cache.Clear();
            _cacheOrder.Clear();
            _cacheBytes = 0;
        }

        if (_ownsClient)
        {
            _client.Dispose();
        }
    }
}
