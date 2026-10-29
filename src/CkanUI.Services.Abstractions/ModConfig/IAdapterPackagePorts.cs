namespace CkanUI.Services.Abstractions.ModConfig;

/// <summary>
/// 一次 HTTP 获取的原始结果：内容、ETag 与"服务器说没变"标志。
/// </summary>
/// <param name="Payload">响应体；304 时为 null。</param>
/// <param name="ETag">响应带回来的 ETag；服务器没给就沿用请求里那份。</param>
/// <param name="NotModified">服务器回了 304。</param>
/// <param name="ContentLength">响应体字节数（304 时为 0）。</param>
public sealed record AdapterHttpResponse(byte[]? Payload, string? ETag, bool NotModified, long ContentLength = 0)
{
    /// <summary>304 结果（没有内容）。</summary>
    /// <param name="etag">沿用的 ETag。</param>
    public static AdapterHttpResponse Unchanged(string? etag) => new(null, etag, true);

    /// <summary>是否有内容。</summary>
    public bool HasPayload => Payload is { Length: > 0 };
}

/// <summary>
/// HTTP 获取端口（ADP-04）：只要一段字节 + ETag 就够了，刻意不依赖 HttpClient 类型本身，
/// 这样离线测试可以塞假实现，也可以在真实实现里注入自定义 HttpMessageHandler。
/// </summary>
/// <remarks>实现必须把网络/超时/状态码失败都变成 <see cref="OperationResult{T}"/>，不得向调用方抛异常。</remarks>
public interface IAdapterPackageFetcher
{
    /// <summary>带 If-None-Match 的条件 GET。</summary>
    /// <param name="uri">http(s) 绝对地址。</param>
    /// <param name="etag">上次响应的 ETag；为空表示普通 GET。</param>
    /// <param name="cancellationToken">取消。</param>
    /// <returns>成功时 <c>NotModified=true</c>（304，无 payload）或带 payload 的 200 内容。</returns>
    Task<OperationResult<AdapterHttpResponse>> GetAsync(Uri uri, string? etag = null, CancellationToken cancellationToken = default);
}

/// <summary>
/// adapter 安装端口（ADP-04）：导入第三方 adapter、维护 installed.json 清单、检查更新。
/// </summary>
/// <remarks>
/// 约定：所有失败都通过 <see cref="OperationResult{T}"/> 返回，<c>Detail</c> 以稳定的诊断码开头
/// （例如 <c>adapter-zip-slip</c>），便于日志、界面提示与用例断言。
/// </remarks>
public interface IAdapterInstaller
{
    /// <summary>安装根目录（默认 %LocalAppData%\CKAN\ui\adapters）。每个 adapter 落在其下的 &lt;id&gt; 子目录。</summary>
    string AdapterRootDirectory { get; }

    /// <summary>安装一个 adapter 包（.json 或 .zip；本地或远端）。</summary>
    /// <param name="source">包来源。</param>
    /// <param name="cancellationToken">取消。</param>
    /// <returns>成功时给出清单与落盘目录；校验不过时不落盘。</returns>
    Task<OperationResult<AdapterInstallResult>> InstallAsync(AdapterPackageSource source, CancellationToken cancellationToken = default);

    /// <summary>读一个已装 adapter 的清单。</summary>
    /// <param name="adapterId">adapter id。</param>
    /// <param name="cancellationToken">取消。</param>
    /// <returns>未安装（或清单损坏降级）时返回失败，<c>Detail</c> 以 <c>adapter-not-installed</c> 或 <c>adapter-manifest-corrupt</c> 开头。</returns>
    Task<OperationResult<AdapterInstallManifest>> GetInstalledAsync(string adapterId, CancellationToken cancellationToken = default);

    /// <summary>枚举安装根目录下所有带清单的 adapter（清单损坏的只记 warning，不阻断枚举）。</summary>
    /// <param name="cancellationToken">取消。</param>
    Task<OperationResult<AdapterInstalledIndex>> ListInstalledAsync(CancellationToken cancellationToken = default);

    /// <summary>卸载一个已装 adapter（整目录删除）。</summary>
    /// <param name="adapterId">adapter id。</param>
    /// <param name="cancellationToken">取消。</param>
    Task<OperationResult> UninstallAsync(string adapterId, CancellationToken cancellationToken = default);

    /// <summary>
    /// 用 ETag 条件请求检查某个已装 adapter 是否有更新。
    /// </summary>
    /// <param name="adapterId">要检查的已装 adapter id（同时提供清单里的已装哈希）。</param>
    /// <param name="remoteUri">远端包地址。</param>
    /// <param name="etag">显式 ETag；为 null 时用清单里记录的上次 ETag。</param>
    /// <param name="cancellationToken">取消。</param>
    /// <returns>304 → 无更新；200 且 sha256 相同 → 无更新；sha256 不同 → 有更新。</returns>
    Task<OperationResult<AdapterUpdateCheckResult>> CheckForUpdateAsync(
        string adapterId,
        Uri remoteUri,
        string? etag = null,
        CancellationToken cancellationToken = default);
}
