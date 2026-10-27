using CkanUI.Services.Abstractions;
using CkanUI.Services.Abstractions.Dtos;

namespace CkanUI.Services.Downloads;

/// <summary>传输层原始进度（节流、速率换算、封送由 <see cref="DownloadService"/> 统一负责）。</summary>
/// <param name="BytesReceived">已接收字节数。</param>
/// <param name="TotalBytes">服务器声明的总字节数；0 表示未知。</param>
public readonly record struct TransferProgress(long BytesReceived, long TotalBytes);

/// <summary>传输层成功产物：临时文件已写完（此时还没改名到最终路径）。</summary>
/// <param name="TempFilePath">写好的临时文件路径。</param>
/// <param name="Sha256">传输过程中顺带算出的 SHA256；不可用时为 null。</param>
/// <param name="ETag">服务器 ETag（已去引号）；没有时为 null。</param>
/// <param name="BytesReceived">最后报告的已接收字节数（可能为 0，此时以文件长度为准）。</param>
public sealed record TransferOutcome(string TempFilePath, string? Sha256, string? ETag, long BytesReceived);

/// <summary>
/// 「把一组镜像地址落到指定临时文件」这一件事的抽象。
/// </summary>
/// <remarks>
/// 存在的唯一理由是可测：真实实现要触网（<see cref="NetAsyncDownloaderTransport"/>），
/// 而 22 篇 §IT-05 与 07 篇都禁止测试访问外网。测试用假实现驱动成功 / 网络失败 / 取消 / 不完整全部分支。
/// 实现方**不负责**校验与改名：临时文件的创建、大小与哈希校验、原子 Move、失败清理全部在
/// <see cref="DownloadService"/>，这样无论传输层是谁，落盘语义都一致。
/// </remarks>
public interface IDownloadTransport
{
    /// <summary>下载到 <paramref name="tempFilePath"/>。</summary>
    /// <param name="request">原始请求（地址顺序即镜像优先级）。</param>
    /// <param name="tempFilePath">调用方分配的临时文件路径（同目录，保证 Move 是原子改名）。</param>
    /// <param name="progress">原始字节进度回调，可能在后台线程触发。</param>
    /// <param name="ct">取消令牌，必须传导到底层传输。</param>
    Task<OperationResult<TransferOutcome>> TransferAsync(
        DownloadRequest request,
        string tempFilePath,
        IProgress<TransferProgress>? progress,
        CancellationToken ct);
}
