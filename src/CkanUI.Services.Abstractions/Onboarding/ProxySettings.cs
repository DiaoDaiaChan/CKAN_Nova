namespace CkanUI.Services.Abstractions;

/// <summary>下载代理的工作模式（首次启动指引第 3 步）。</summary>
public enum ProxyMode
{
    /// <summary>直连：显式关掉系统代理，连代理配置都不读。</summary>
    Direct,

    /// <summary>跟随系统（默认）：保持 .NET 的默认代理解析。</summary>
    System,

    /// <summary>手动指定 HTTP 代理。</summary>
    Manual,
}

/// <summary>
/// 下载代理配置。
/// </summary>
/// <remarks>
/// 只影响 .NET 的全局代理（<c>HttpClient.DefaultProxy</c> / <c>WebRequest.DefaultWebProxy</c>），
/// 因为 Core 的 <c>NetAsyncDownloader</c> 走 <c>WebClient</c>，没有按请求传代理的入口。
/// 见 02 篇 §6.2：代理只能在进程级装，装配点必须是启动序列之前与向导保存时。
/// </remarks>
/// <param name="Mode">工作模式。</param>
/// <param name="Server">手动模式的代理地址（<c>http://host:port</c>）。</param>
/// <param name="BypassList">不分流的地址列表（逗号或分号分隔），仅手动模式使用。</param>
public sealed record ProxySettings(ProxyMode Mode = ProxyMode.System, string? Server = null, string? BypassList = null)
{
    /// <summary>直连配置。</summary>
    public static ProxySettings Direct { get; } = new(ProxyMode.Direct);

    /// <summary>跟随系统配置。</summary>
    public static ProxySettings System { get; } = new(ProxyMode.System);

    /// <summary>手动模式是否给了可用地址。</summary>
    public bool HasServer => !string.IsNullOrWhiteSpace(Server);

    /// <summary>手动模式但地址无效时返回 <c>false</c>；其他模式恒为 <c>true</c>。</summary>
    public bool IsValid => Mode != ProxyMode.Manual || HasServer;
}

/// <summary>
/// 把代理配置装到进程上，并提供「先测后存」的连通性探测。
/// </summary>
/// <remarks>
/// 实现必须幂等且可回退：用户从「手动」切回「跟随系统」时要恢复构造时抓到的系统默认值，
/// 否则一旦设过手动代理，整个进程到退出前都还在走那个代理。
/// </remarks>
public interface IProxyConfigurator
{
    /// <summary>当前生效的配置。</summary>
    ProxySettings Current { get; }

    /// <summary>立即生效（进程级），并记住为 <see cref="Current"/>。</summary>
    void Apply(ProxySettings settings);

    /// <summary>
    /// 用给定配置实际发一次请求验证连通性（不影响进程当前代理）。
    /// </summary>
    /// <param name="settings">待验证的配置。</param>
    /// <param name="probeUrl">探测地址；为空时用 CKAN 的官方元数据源。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>成功时带回一句可直接显示的结论（含耗时与状态码）。</returns>
    Task<OperationResult<string>> TestAsync(ProxySettings settings, string? probeUrl = null, CancellationToken ct = default);
}
