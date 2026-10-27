using System.Security.Cryptography;

using CKAN;

using CkanUI.Services.Abstractions;
using CkanUI.Services.Abstractions.Dtos;
using CkanUI.Services.Logging;

namespace CkanUI.Services.Downloads;

/// <summary>
/// 基于 Core <see cref="NetAsyncDownloader"/> 的真实传输实现（02 篇 §6.2 契约表）。
/// </summary>
/// <remarks>
/// <para>
/// <b>为什么整体 <c>Task.Run</c> 包</b>：<see cref="NetAsyncDownloader.DownloadAndWait"/> 是同步阻塞的
/// ——内部 `complete_or_canceled.WaitOne()`（`Core/Net/NetAsyncDownloader.cs:112`），返回前不会让出线程。
/// 它真正的 I/O 在自己的 WebClient 异步回调里完成，但「等待」这一步是阻塞的；
/// 在 UI 线程上直接调用就是 05 篇 §9 门禁里明令禁止的「未响应」。
/// 因此把整段同步调用挪到线程池：`Task.Run(... CancellationToken.None)`——
/// 取消由构造器传入的 <see cref="CancellationToken"/> 传导（不靠中止线程），见下。
/// </para>
/// <para>
/// <b>取消怎么到底层</b>：`NetAsyncDownloader` 的构造器第 4 个参数就是 `CancellationToken`，
/// 它被转交给 `DownloadPart.Download(cancelToken)` → `ResumingWebClient.DownloadFileAsyncWithResume`
/// → 内部的 `CancellationTokenSource`（`Core/Net/ResumingWebClient.cs:179-191`）。所以取消是真取消，
/// 不是「停订阅 + 放弃结果」。Core 观察到取消后在进度回调里置位并以
/// <see cref="CancelledActionKraken"/> 结束 `DownloadAndWait`。
/// </para>
/// </remarks>
public sealed class NetAsyncDownloaderTransport : IDownloadTransport
{
    private readonly IUser _user;
    private readonly string _userAgent;
    private readonly log4net.ILog _log = LoggingSetup.GetLogger(LoggingSetup.UiLoggerName);

    /// <param name="user">Core 的反向回调通道（UI 侧是 <c>AvaloniaUser</c>，它自己负责封送与节流）。</param>
    /// <param name="userAgent">User-Agent；空则用 Core 的 `Net.UserAgentString`。</param>
    public NetAsyncDownloaderTransport(IUser user, string? userAgent = null)
    {
        ArgumentNullException.ThrowIfNull(user);
        _user = user;
        _userAgent = string.IsNullOrWhiteSpace(userAgent) ? Net.UserAgentString : userAgent!;
    }

    /// <inheritdoc />
    public Task<OperationResult<TransferOutcome>> TransferAsync(DownloadRequest request,
                                                               string          tempFilePath,
                                                               IProgress<TransferProgress>? progress,
                                                               CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(tempFilePath);

        if (request.Urls is null || request.Urls.Count == 0)
        {
            return Task.FromResult(OperationResult<TransferOutcome>.Fail("没有可用的下载地址，未下载任何内容。"));
        }

        // 见类注释：DownloadAndWait 是同步阻塞的，必须挪到线程池。
        return Task.Run(() => TransferBlocking(request, tempFilePath, progress, ct), CancellationToken.None);
    }

    private OperationResult<TransferOutcome> TransferBlocking(DownloadRequest request,
                                                              string tempFilePath,
                                                              IProgress<TransferProgress>? progress,
                                                              CancellationToken ct)
    {
        // size 必须传 0：Core 会把 DownloadTargetFile.size 当作「已有字节数」去做断点续传
        // （DownloadPart 构造器 size = bytesLeft = target.size），我们的临时文件每次都是新的，
        // 传 0 才能从第 0 字节开始下。
        var target = new NetAsyncDownloader.DownloadTargetFile(request.Urls.ToList(),
                                                               tempFilePath,
                                                               0,
                                                               request.MimeType ?? string.Empty);

        var downloader = new NetAsyncDownloader(_user, () => SHA256.Create(), _userAgent, ct);

        string? etag = null;
        string? sha256 = null;
        var received = 0L;

        // 踩坑点：TargetProgress 的签名写着 (target, bytesLeft, size)，第二个参数是「剩余字节」而不是
        // 「已接收字节」（Core/Net/NetAsyncDownloader.cs:289 传的是 download.bytesLeft）。
        // 已接收 = size - bytesLeft。
        downloader.TargetProgress += (_, bytesLeft, size) =>
        {
            received = Math.Max(0, size - bytesLeft);
            progress?.Report(new TransferProgress(received, size));
        };

        // onOneCompleted 的 hash 是 Core 边下边算的 SHA256（大写、去分隔符），ETag 已去引号。
        downloader.onOneCompleted += (_, _, tag, hash) =>
        {
            etag = tag;
            sha256 = string.IsNullOrEmpty(hash) ? null : hash;
        };

        _log.Info($"开始下载 {request.Title}：( {string.Join(" 或 ", request.Urls)} ) → {tempFilePath}");

        try
        {
            // 传单元素数组即可：这个方法只是把每个 target 交给 DownloadPart 并等全部结束。
            downloader.DownloadAndWait(target is null ? Array.Empty<NetAsyncDownloader.DownloadTarget>() : new[] { target });
        }
        catch (Exception ex)
        {
            var failure = DownloadFailureClassifier.Classify(ex, request.Title, request.PrimaryUrl);
            if (failure.Cancelled)
            {
                _log.Info($"下载已取消：{request.Title}");
            }
            else
            {
                _log.Warn($"下载失败：{request.Title}", ex);
            }

            return DownloadFailureClassifier.ToResult<TransferOutcome>(failure);
        }

        // 走到这里说明最后一个镜像成功（Core 在全部失败时会抛 DownloadErrorsKraken）。
        var length = FileLengthOrZero(tempFilePath);
        _log.Info($"下载完成 {request.Title}：{length} 字节");
        return OperationResult<TransferOutcome>.Ok(
            new TransferOutcome(tempFilePath, sha256, etag, received > 0 ? received : length));
    }

    private static long FileLengthOrZero(string path)
    {
        try
        {
            var info = new FileInfo(path);
            return info.Exists ? info.Length : 0;
        }
        catch (IOException)
        {
            return 0;
        }
    }
}
