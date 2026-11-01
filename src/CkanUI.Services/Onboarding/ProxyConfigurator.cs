using System.Diagnostics;
using System.Net;

using CkanUI.Services.Abstractions;
using CkanUI.Services.Logging;

using log4net;

namespace CkanUI.Services.Onboarding;

/// <summary>
/// 把 <see cref="ProxySettings"/> 装到 .NET 的全局代理上（首次启动指引第 3 步）。
/// </summary>
/// <remarks>
/// <para>
/// <b>为什么必须是全局</b>：Core 的下载链路是 <c>NetAsyncDownloader</c> → <c>ResumingWebClient</c>（<c>WebClient</c>），
/// 没有任何「按请求传代理」的入口；索引刷新走的 <c>WebClient</c> 同理。所以代理只能在进程级生效
/// （<c>HttpClient.DefaultProxy</c> 管 <c>SocketsHttpHandler</c>，<c>WebRequest.DefaultWebProxy</c> 管 <c>WebClient</c>）。
/// </para>
/// <para>
/// <b>为什么构造时抓默认值</b>：用户从「手动」切回「跟随系统」时必须能真的回到系统代理。
/// 不抓默认值就只能把 DefaultProxy 设成某个 WebProxy，等于永久劫持了进程的代理配置。
/// </para>
/// </remarks>
public sealed class ProxyConfigurator : IProxyConfigurator
{
    /// <summary>默认探测地址：CKAN 官方索引所在的域名，能连通它就等于能拉元数据。</summary>
    public const string DefaultProbeUrl = "https://raw.githubusercontent.com/";

    private static readonly ILog Log = LogManager.GetLogger(typeof(ProxyConfigurator));

    private readonly IWebProxy _systemHttpProxy;
    private readonly IWebProxy? _systemWebProxy;
    private ProxySettings _current = ProxySettings.System;

    /// <summary>构造并抓住当前进程的系统代理默认值。</summary>
    public ProxyConfigurator()
    {
        _systemHttpProxy = HttpClient.DefaultProxy;
        _systemWebProxy = WebRequest.DefaultWebProxy;
    }

    /// <inheritdoc />
    public ProxySettings Current => _current;

    /// <inheritdoc />
    public void Apply(ProxySettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        switch (settings.Mode)
        {
            case ProxyMode.Direct:
                // 空 WebProxy = 不走任何代理（不是「代理地址为空」，语义上等价于直连）。
                HttpClient.DefaultProxy = new WebProxy();
                WebRequest.DefaultWebProxy = new WebProxy();
                break;

            case ProxyMode.System:
                HttpClient.DefaultProxy = _systemHttpProxy;
                WebRequest.DefaultWebProxy = _systemWebProxy;
                break;

            case ProxyMode.Manual:
                if (!TryBuildProxy(settings, out var proxy, out var error))
                {
                    Log.Warn($"代理地址无效，保持原配置：{error}");
                    return;
                }

                HttpClient.DefaultProxy = proxy;
                WebRequest.DefaultWebProxy = proxy;
                break;
        }

        _current = settings;
        Log.Info($"下载代理已应用：{settings.Mode}" +
                 (settings.Mode == ProxyMode.Manual ? $"（{settings.Server}）" : string.Empty));
    }

    /// <inheritdoc />
    public async Task<OperationResult<string>> TestAsync(
        ProxySettings settings,
        string? probeUrl = null,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(settings);

        if (!settings.IsValid)
        {
            return OperationResult<string>.Fail("请先填写代理地址，例如 http://127.0.0.1:7890。");
        }

        var url = string.IsNullOrWhiteSpace(probeUrl) ? DefaultProbeUrl : probeUrl!;
        try
        {
            using var handler = new HttpClientHandler();
            if (settings.Mode == ProxyMode.Direct)
            {
                handler.UseProxy = false;
            }
            else if (settings.Mode == ProxyMode.Manual)
            {
                if (!TryBuildProxy(settings, out var proxy, out var error))
                {
                    return OperationResult<string>.Fail(error);
                }

                handler.Proxy = proxy;
                handler.UseProxy = true;
            }

            using var client = new HttpClient(handler, disposeHandler: false)
            {
                Timeout = TimeSpan.FromSeconds(15),
            };

            var watch = Stopwatch.StartNew();
            using var response = await client
                .GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct)
                .ConfigureAwait(false);
            watch.Stop();

            return OperationResult<string>.Ok(
                $"连接正常：{url} 返回 HTTP {(int)response.StatusCode}，耗时 {watch.ElapsedMilliseconds} ms。");
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            return OperationResult<string>.Fail("代理连通性测试已取消。", retryable: true);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or InvalidOperationException)
        {
            Log.Warn("代理连通性测试失败。", ex);
            return OperationResult<string>.Fail($"连接失败：{ex.Message}", ex.ToString(), retryable: true);
        }
    }

    /// <summary>把配置转成 <see cref="WebProxy"/>；地址不合法时返回 <c>false</c> 并给出可直接显示的原因。</summary>
    private static bool TryBuildProxy(ProxySettings settings, out WebProxy proxy, out string error)
    {
        proxy = new WebProxy();
        error = string.Empty;

        var raw = settings.Server?.Trim() ?? string.Empty;
        if (raw.Length == 0)
        {
            error = "代理地址不能为空。";
            return false;
        }

        // 允许只写 host:port（最常见的粘贴形式），补上 http 前缀再解析。
        if (!raw.Contains("://", StringComparison.Ordinal))
        {
            raw = "http://" + raw;
        }

        if (!Uri.TryCreate(raw, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            error = "代理地址格式应为 http://主机:端口。";
            return false;
        }

        proxy = new WebProxy(uri);
        var bypass = settings.BypassList;
        if (!string.IsNullOrWhiteSpace(bypass))
        {
            proxy.BypassList = bypass.Split(new[] { ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        }

        return true;
    }
}
