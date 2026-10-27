using CkanUI.Services.Abstractions.Dtos;

namespace CkanUI.Services.Abstractions;

/// <summary>
/// 单文件下载服务（M2：真实下载）。
/// </summary>
/// <remarks>
/// 契约约定（05 篇 §5/§6、02 篇 §6.2）：
/// <list type="bullet">
/// <item>进度一律经 <see cref="IDispatcher.Post"/> 封送到 UI 线程，并按 100ms 节流；</item>
/// <item>取消经 <paramref name="ct"/> 传导到底层传输，取消后不留半截文件，也不改动既有目标文件；</item>
/// <item>不抛异常，一律返回 <see cref="OperationResult{T}"/>：网络类失败 retryable=true，磁盘/校验类 false。</item>
/// </list>
/// 安装流程里 Core 自己的 <c>NetAsyncModulesDownloader</c> 仍然负责「装模组时的批量下载」，
/// 本服务面向「下载到缓存 / 内容预览 / 更新包」这类单文件场景。
/// </remarks>
public interface IDownloadService
{
    /// <summary>下载一个文件到 <see cref="DownloadRequest.DestinationPath"/>。</summary>
    /// <param name="request">源地址、目标路径、可选的大小/哈希期望值。</param>
    /// <param name="progress">进度接收者；内部保证已封送到 UI 线程且已节流。</param>
    /// <param name="ct">取消令牌；取消时返回失败结果并清理临时文件。</param>
    Task<OperationResult<DownloadTaskInfo>> DownloadAsync(
        DownloadRequest request,
        IProgress<DownloadProgressInfo>? progress = null,
        CancellationToken ct = default);
}
