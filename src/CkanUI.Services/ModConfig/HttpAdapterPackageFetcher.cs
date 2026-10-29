using System.Net;
using System.Net.Http;
using CkanUI.Services.Abstractions;
using CkanUI.Services.Abstractions.ModConfig;

namespace CkanUI.Services.ModConfig;

/// <summary>
/// 基于 <see cref="HttpClient"/> 的 adapter 包获取端口（ADP-04）。
/// </summary>
/// <remarks>
/// 测试注入路径有两条：给假 <see cref="HttpMessageHandler"/>（走真实 HttpClient 管线）或自己实现
/// <see cref="IAdapterPackageFetcher"/>。构造时不发任何请求，超时/取消/状态码失败全部变成
/// <see cref="OperationResult{T}"/>，不向调用方抛异常。
/// </remarks>
public sealed class HttpAdapterPackageFetcher : IAdapterPackageFetcher, IDisposable
{
    /// <summary>User-Agent：部分 CDN 会拒绝空 UA 的请求。</summary>
    public const string DefaultUserAgent = "CKAN-UI-AdapterFetcher";

    /// <summary>响应体默认上限（与 <see cref="AdapterInstallerLimits.MaxPackageBytes"/> 默认值一致）。</summary>
    public const long DefaultMaxPayloadBytes = 16L * 1024 * 1024;

    /// <summary>默认超时。</summary>
    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(30);

    private readonly HttpClient _client;
    private readonly bool _ownsClient;
    private readonly long _maxPayloadBytes;

    /// <summary>自己建 HttpClient（带 SocketsHttpHandler）。</summary>
    /// <param name="maxPayloadBytes">响应体上限。</param>
    /// <param name="timeout">超时；null 用默认 30 秒。</param>
    public HttpAdapterPackageFetcher(long maxPayloadBytes = DefaultMaxPayloadBytes, TimeSpan? timeout = null)
        : this(CreateClient(DefaultUserAgent, timeout), maxPayloadBytes, ownsClient: true)
    {
    }

    /// <summary>用注入的 handler 建 HttpClient（集成测试/假响应走这条路）。</summary>
    /// <param name="handler">消息处理器，由新建的 HttpClient 负责释放。</param>
    /// <param name="maxPayloadBytes">响应体上限。</param>
    /// <param name="timeout">超时；null 用默认 30 秒。</param>
    public HttpAdapterPackageFetcher(HttpMessageHandler handler, long maxPayloadBytes = DefaultMaxPayloadBytes, TimeSpan? timeout = null)
        : this(CreateClient(handler, timeout), maxPayloadBytes, ownsClient: true)
    {
    }

    /// <summary>用外部 HttpClient（生命周期归调用方，本类不释放它）。</summary>
    /// <param name="client">已配置好的客户端。</param>
    /// <param name="maxPayloadBytes">响应体上限。</param>
    public HttpAdapterPackageFetcher(HttpClient client, long maxPayloadBytes = DefaultMaxPayloadBytes)
        : this(client, maxPayloadBytes, ownsClient: false)
    {
    }

    private HttpAdapterPackageFetcher(HttpClient client, long maxPayloadBytes, bool ownsClient)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxPayloadBytes);
        _client = client;
        _maxPayloadBytes = maxPayloadBytes;
        _ownsClient = ownsClient;
    }

    /// <inheritdoc />
    public async Task<OperationResult<AdapterHttpResponse>> GetAsync(Uri uri, string? etag = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(uri);
        if (!uri.IsAbsoluteUri
            || (!string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)
                && !string.Equals(uri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase)))
        {
            return AdapterResults.Fail<AdapterHttpResponse>("adapter-url-invalid", $"adapter 包地址必须是 http(s) 绝对地址：{uri}", uri.ToString());
        }

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, uri);
            if (!string.IsNullOrWhiteSpace(etag))
            {
                request.Headers.TryAddWithoutValidation("If-None-Match", etag.Trim());
            }

            using var response = await _client
                .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                .ConfigureAwait(false);

            var responseEtag = response.Headers.ETag?.ToString() ?? etag;
            if (response.StatusCode == HttpStatusCode.NotModified)
            {
                return OperationResult<AdapterHttpResponse>.Ok(AdapterHttpResponse.Unchanged(responseEtag));
            }

            if (!response.IsSuccessStatusCode)
            {
                var status = (int)response.StatusCode;
                var retryable = status >= 500 || response.StatusCode is HttpStatusCode.RequestTimeout or HttpStatusCode.TooManyRequests;
                return AdapterResults.Fail<AdapterHttpResponse>(
                    "adapter-http-status",
                    $"获取 adapter 包失败：HTTP {status}（{response.ReasonPhrase}）。",
                    uri.ToString(),
                    retryable);
            }

            var declared = response.Content.Headers.ContentLength;
            if (declared.HasValue && declared.Value > _maxPayloadBytes)
            {
                return AdapterResults.Fail<AdapterHttpResponse>(
                    "adapter-package-too-large",
                    $"远端 adapter 包 {declared.Value} 字节，超过上限 {_maxPayloadBytes} 字节。",
                    uri.ToString());
            }

            var payload = await response.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
            if (payload.Length > _maxPayloadBytes)
            {
                return AdapterResults.Fail<AdapterHttpResponse>(
                    "adapter-package-too-large",
                    $"远端 adapter 包 {payload.Length} 字节，超过上限 {_maxPayloadBytes} 字节。",
                    uri.ToString());
            }

            return OperationResult<AdapterHttpResponse>.Ok(new AdapterHttpResponse(payload, responseEtag, false, payload.Length));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return AdapterResults.Fail<AdapterHttpResponse>("adapter-cancelled", "获取 adapter 包已取消。", uri.ToString());
        }
        catch (TaskCanceledException ex)
        {
            return AdapterResults.Fail<AdapterHttpResponse>("adapter-http-timeout", $"获取 adapter 包超时：{ex.Message}", uri.ToString(), retryable: true);
        }
        catch (HttpRequestException ex)
        {
            return AdapterResults.Fail<AdapterHttpResponse>("adapter-http-error", $"获取 adapter 包失败：{ex.Message}", uri.ToString(), retryable: true);
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_ownsClient)
        {
            _client.Dispose();
        }

        GC.SuppressFinalize(this);
    }

    private static HttpClient CreateClient(string userAgent, TimeSpan? timeout)
    {
        var client = new HttpClient(new SocketsHttpHandler(), disposeHandler: true)
        {
            Timeout = timeout ?? DefaultTimeout,
        };
        client.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", userAgent);
        return client;
    }

    private static HttpClient CreateClient(HttpMessageHandler handler, TimeSpan? timeout)
    {
        ArgumentNullException.ThrowIfNull(handler);
        var client = new HttpClient(handler, disposeHandler: true)
        {
            Timeout = timeout ?? DefaultTimeout,
        };
        client.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", DefaultUserAgent);
        return client;
    }
}
