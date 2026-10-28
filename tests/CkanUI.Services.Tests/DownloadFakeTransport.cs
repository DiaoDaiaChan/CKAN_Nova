using System.Security.Cryptography;

using CkanUI.Services.Abstractions;
using CkanUI.Services.Abstractions.Dtos;
using CkanUI.Services.Downloads;

namespace CkanUI.Services.Tests;

/// <summary>
/// 测试用传输层：把「字节怎么写进临时文件」这件事从网络解耦出来（07 篇：单测不得触网）。
/// </summary>
/// <remarks>
/// 所有工厂方法都模拟真实传输的关键行为：先往临时文件写一部分，再按 3 段上报进度，
/// 这样 <see cref="DownloadService"/> 的校验 / 原子改名 / 清理分支都能被真实触发。
/// </remarks>
public sealed class FakeDownloadTransport : IDownloadTransport
{
    private readonly Func<DownloadRequest, string, IProgress<TransferProgress>?, CancellationToken, Task<OperationResult<TransferOutcome>>> _handler;

    public FakeDownloadTransport(
        Func<DownloadRequest, string, IProgress<TransferProgress>?, CancellationToken, Task<OperationResult<TransferOutcome>>> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        _handler = handler;
    }

    /// <summary>被调用次数（断言「没有地址时不触网」「重试了几次」）。</summary>
    public int Calls { get; private set; }

    /// <summary>每次调用开始时临时文件里已有多少字节（断言「重试是从断点续传」）。</summary>
    public List<long> ObservedExistingBytes { get; } = new();

    /// <summary>最后一次调用收到的临时文件路径（断言清理用）。</summary>
    public string? LastTempPath { get; private set; }

    /// <summary>最后一次调用收到的进度接收者（断言节流用）。</summary>
    public IProgress<TransferProgress>? LastProgress { get; private set; }

    public Task<OperationResult<TransferOutcome>> TransferAsync(DownloadRequest request,
                                                                string tempFilePath,
                                                                IProgress<TransferProgress>? progress,
                                                                CancellationToken ct)
    {
        Calls++;
        LastTempPath = tempFilePath;
        LastProgress = progress;
        ObservedExistingBytes.Add(File.Exists(tempFilePath) ? new FileInfo(tempFilePath).Length : 0);
        return _handler(request, tempFilePath, progress, ct);
    }

    /// <summary>成功写满 payload。</summary>
    public static FakeDownloadTransport Ok(byte[] payload, string? sha256 = null, string? etag = "abc")
        => new((_, temp, progress, _) =>
        {
            progress?.Report(new TransferProgress(0, payload.Length));
            File.WriteAllBytes(temp, payload);
            progress?.Report(new TransferProgress(payload.Length / 2, payload.Length));
            progress?.Report(new TransferProgress(payload.Length, payload.Length));
            var hash = sha256 ?? Convert.ToHexString(SHA256.HashData(payload));
            return Task.FromResult(OperationResult<TransferOutcome>.Ok(
                new TransferOutcome(temp, hash, etag, payload.Length)));
        });

    /// <summary>带真实停顿的成功下载：让 100ms 节流窗口真的跨过去，以便断言「中途有字节级进度」。</summary>
    public static FakeDownloadTransport Slow(byte[] payload, int stepDelayMs)
        => new(async (_, temp, progress, _) =>
        {
            var half = payload.Length / 2;
            progress?.Report(new TransferProgress(0, payload.Length));
            await Task.Delay(stepDelayMs).ConfigureAwait(false);
            progress?.Report(new TransferProgress(half, payload.Length));
            await File.WriteAllBytesAsync(temp, payload).ConfigureAwait(false);
            await Task.Delay(stepDelayMs).ConfigureAwait(false);
            progress?.Report(new TransferProgress(payload.Length, payload.Length));
            return OperationResult<TransferOutcome>.Ok(new TransferOutcome(
                temp, Convert.ToHexString(SHA256.HashData(payload)), null, payload.Length));
        });

    /// <summary>写到一半就取消：临时文件里已有半截数据，必须被清理。</summary>
    public static FakeDownloadTransport CancelsAfterPartialWrite(byte[] payload)
        => new(async (_, temp, progress, ct) =>
        {
            await File.WriteAllBytesAsync(temp, payload, CancellationToken.None).ConfigureAwait(false);
            progress?.Report(new TransferProgress(payload.Length, payload.Length * 2));
            await Task.Delay(Timeout.Infinite, ct).ConfigureAwait(false);
            throw new InvalidOperationException("unreachable");
        });

    /// <summary>抛网络异常（模拟 Core 把 WebException 透出）。</summary>
    public static FakeDownloadTransport Throws(Exception exception)
        => new((_, _, _, _) => Task.FromException<OperationResult<TransferOutcome>>(exception));

    /// <summary>
    /// 第一次失败（只写了前 <paramref name="partialBytes"/> 字节），重试时从断点追加剩余字节。
    /// 模拟真实的 Range 续传语义：临时文件里已有的字节就是进度，不该重下。
    /// </summary>
    public static FakeDownloadTransport FailsOnceThenResumes(byte[] payload, int partialBytes)
    {
        ArgumentNullException.ThrowIfNull(payload);
        var calls = 0;
        return new((_, temp, progress, _) =>
        {
            calls++;
            var existing = File.Exists(temp) ? (int)new FileInfo(temp).Length : 0;
            if (calls == 1)
            {
                using (var stream = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None))
                {
                    stream.Write(payload, 0, Math.Min(partialBytes, payload.Length));
                }

                progress?.Report(new TransferProgress(existing + partialBytes, payload.Length));
                return Task.FromResult(OperationResult<TransferOutcome>.Fail(
                    "下载失败，请检查网络连接后重试。", "SocketException: 连接被重置", retryable: true));
            }

            using (var stream = new FileStream(temp, FileMode.Append, FileAccess.Write, FileShare.None))
            {
                var remaining = Math.Max(0, payload.Length - existing);
                stream.Write(payload, existing, remaining);
            }

            progress?.Report(new TransferProgress(payload.Length, payload.Length));
            var hash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(temp)));
            return Task.FromResult(OperationResult<TransferOutcome>.Ok(
                new TransferOutcome(temp, hash, null, payload.Length)));
        });
    }

    /// <summary>直接返回失败结果（模拟传输层已归类好的失败）。</summary>
    public static FakeDownloadTransport Fails(string message, string? detail = null, bool retryable = true)
        => new((_, _, _, _) => Task.FromResult(OperationResult<TransferOutcome>.Fail(message, detail, retryable)));

    /// <summary>成功返回但临时文件长度少于声明值（模拟截断）。</summary>
    public static FakeDownloadTransport Truncated(byte[] payload, long claimedSize)
        => new((_, temp, progress, _) =>
        {
            File.WriteAllBytes(temp, payload);
            progress?.Report(new TransferProgress(payload.Length, claimedSize));
            return Task.FromResult(OperationResult<TransferOutcome>.Ok(
                new TransferOutcome(temp, null, null, payload.Length)));
        });

    /// <summary>大量高频进度上报（断言 100ms 节流）。</summary>
    public static FakeDownloadTransport Flood(byte[] payload, int reports)
        => new((_, temp, progress, _) =>
        {
            for (var i = 0; i < reports; i++)
            {
                progress?.Report(new TransferProgress(i, reports));
            }

            File.WriteAllBytes(temp, payload);
            return Task.FromResult(OperationResult<TransferOutcome>.Ok(
                new TransferOutcome(temp, null, null, payload.Length)));
        });
}
