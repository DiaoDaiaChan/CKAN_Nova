using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Security.Cryptography;

using CKAN;

using CkanUI.Services.Abstractions;

namespace CkanUI.Services.Downloads;

/// <summary>一次下载失败的归类结果：用户可读文案 + 技术细节 + 是否值得重试 + 是否属于取消。</summary>
/// <param name="Message">面向用户的文案（中文，可直接进错误条）。</param>
/// <param name="Detail">技术细节（异常类型、内层异常、URL），进日志与「复制错误」。</param>
/// <param name="Retryable">网络类为 true，磁盘/校验/证书以外的永久性错误为 false。</param>
/// <param name="Cancelled">用户取消（不是错误，文案是「已取消」）。</param>
public sealed record DownloadFailure(string Message, string? Detail, bool Retryable, bool Cancelled);

/// <summary>
/// 把 Core 与 BCL 抛出的异常翻译成 <see cref="OperationResult"/> 的三要素（01 篇 §8 / 05 篇 §5）。
/// </summary>
/// <remarks>
/// 分类依据：
/// <list type="bullet">
/// <item>取消：<see cref="CancelledActionKraken"/> 与 <see cref="OperationCanceledException"/>；</item>
/// <item>可重试（网络类）：WebException / HttpRequestException / SocketException / TimeoutException /
/// 证书链失败 / 服务器限流 / 传输过程中的 IOException（见下方说明）；</item>
/// <item>不可重试：磁盘（权限、路径、空间）与校验（大小、SHA256）类失败 —— 重试必然再次失败，
/// 这类判定由 <see cref="DownloadService"/> 直接给出 retryable=false，不经过本类。</item>
/// </list>
/// 传输层的 IOException 一律视为网络断流：写临时文件用的流由调用方创建并在传输前就绪，
/// 传输过程中出现的 IOException 只可能来自网络流（WebClient 把断流包成 IOException）。
/// </remarks>
public static class DownloadFailureClassifier
{
    /// <summary>取消时的统一文案（05 篇 §5：「已取消，未做改动」）。</summary>
    public const string CancelledMessage = "已取消下载，未做改动。";

    /// <summary>网络类失败的统一文案。</summary>
    public const string NetworkMessage = "下载失败，请检查网络连接后重试。";

    /// <summary>无法归类的失败的统一文案。</summary>
    public const string GenericMessage = "下载失败。";

    /// <summary>归类一个异常。</summary>
    /// <param name="exception">被捕获的异常。</param>
    /// <param name="title">下载项名字（进文案与细节，便于用户认出是哪个文件）。</param>
    /// <param name="sourceUrl">主地址（进细节，便于排障）。</param>
    public static DownloadFailure Classify(Exception exception, string? title = null, Uri? sourceUrl = null)
    {
        ArgumentNullException.ThrowIfNull(exception);
        var detail = Describe(exception, title, sourceUrl);

        switch (exception)
        {
            case OperationCanceledException:
            case CancelledActionKraken:
                return new DownloadFailure(CancelledMessage, detail, Retryable: false, Cancelled: true);

            // 多地址下载时 Core 把所有单文件错误打包成一个 kraken（Core/Types/Kraken.cs:397）。
            case DownloadErrorsKraken errors:
            {
                var inner = errors.Exceptions.ConvertAll(kvp => kvp.Value);
                var retryable = inner.Exists(IsRetryableNetwork);
                return new DownloadFailure(retryable ? NetworkMessage : GenericMessage, detail, retryable, false);
            }

            case MissingCertificateKraken:
                return new DownloadFailure("服务器证书校验失败，无法建立安全连接。", detail, Retryable: true, false);

            case RequestThrottledKraken:
                return new DownloadFailure("下载服务器正在限流，请稍后重试。", detail, Retryable: true, false);
        }

        return IsRetryableNetwork(exception)
            ? new DownloadFailure(NetworkMessage, detail, Retryable: true, false)
            : new DownloadFailure(GenericMessage, detail, Retryable: false, false);
    }

    /// <summary>这个异常是否属于「重试可能成功」的网络类失败。</summary>
    public static bool IsRetryableNetwork(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);

        return exception switch
        {
            WebException => true,
            HttpRequestException => true,
            SocketException => true,
            TimeoutException => true,
            IOException => true,
            AggregateException aggregate
                => aggregate.InnerExceptions.Count > 0
                   && aggregate.InnerExceptions.All(IsRetryableNetwork),
            _ => false,
        };
    }

    /// <summary>技术细节：异常链 + 目标，用于日志与「复制错误到剪贴板」（12 篇 F-38）。</summary>
    private static string Describe(Exception exception, string? title, Uri? sourceUrl)
    {
        var lines = new List<string>(4);
        if (!string.IsNullOrWhiteSpace(title))
        {
            lines.Add($"文件: {title}");
        }

        if (sourceUrl is not null)
        {
            lines.Add($"地址: {sourceUrl}");
        }

        var depth = 0;
        for (Exception? current = exception; current is not null && depth < 4; current = current.InnerException, depth++)
        {
            lines.Add($"{current.GetType().Name}: {current.Message}");
        }

        if (exception is DownloadErrorsKraken errors)
        {
            foreach (var (target, inner) in errors.Exceptions)
            {
                lines.Add($"镜像: {target} → {inner.GetType().Name}: {inner.Message}");
            }
        }

        var detail = string.Join(Environment.NewLine, lines);
        return detail.Length > 2000 ? detail[..2000] + "…" : detail;
    }

    /// <summary>把一个失败转成统一的泛型失败结果（避免各处重复写 Fail 参数）。</summary>
    public static OperationResult<T> ToResult<T>(DownloadFailure failure)
    {
        ArgumentNullException.ThrowIfNull(failure);
        return OperationResult<T>.Fail(failure.Message, failure.Detail, failure.Retryable);
    }
}
