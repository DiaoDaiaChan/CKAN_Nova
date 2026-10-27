using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;

using CkanUI.Services.Abstractions;
using CkanUI.Services.Abstractions.Dtos;
using CkanUI.Services.Logging;

namespace CkanUI.Services.Downloads;

/// <summary>
/// 单文件下载服务：临时文件 → 校验 → 原子改名（03 篇 §「下载」/ 05 篇 §5）。
/// </summary>
/// <remarks>
/// <para>职责划分：本类只管「落盘语义」，真正的字节搬运交给 <see cref="IDownloadTransport"/>。
/// 因此校验、原子改名、失败清理、进度节流与封送对所有传输实现一致，测试可以用假传输覆盖全部分支。</para>
/// <para><b>不留半截文件</b>：始终先写同目录的 `{文件名}.tmp-{guid}`，只有大小与 SHA256 都通过才
/// `File.Move(temp, dest, overwrite: true)`（同卷改名是原子的）。任何失败/取消都在 finally 删临时文件，
/// 既有目标文件保持原样（与 `src/CkanUI.Sfs.Cli/SfsCli.cs:299-327` 的 CLI 写法一致）。</para>
/// <para><b>不进 IOperationQueue</b>：下载由调用方持有 `IProgress` + `CancellationToken`，
/// 生命周期与页面绑定；队列是给「后台长任务 + 全局进度条」用的，混进来会让同一份进度出现两条来源。</para>
/// </remarks>
public sealed class DownloadService : IDownloadService
{
    /// <summary>进度节流窗口（05 篇 §4：100ms，肉眼够顺滑又不淹 UI 线程）。</summary>
    private const int ThrottleMilliseconds = 100;

    /// <summary>改名重试次数：Windows 上杀软/索引器短暂占用目标文件时重试即可成功。</summary>
    private const int MoveAttempts = 3;

    private readonly IDownloadTransport _transport;
    private readonly IDispatcher _dispatcher;
    private readonly IErrorReporter? _errorReporter;
    private readonly log4net.ILog _log = LoggingSetup.GetLogger(LoggingSetup.UiLoggerName);

    public DownloadService(IDownloadTransport transport, IDispatcher dispatcher, IErrorReporter? errorReporter = null)
    {
        ArgumentNullException.ThrowIfNull(transport);
        ArgumentNullException.ThrowIfNull(dispatcher);
        _transport = transport;
        _dispatcher = dispatcher;
        _errorReporter = errorReporter;
    }

    /// <inheritdoc />
    public async Task<OperationResult<DownloadTaskInfo>> DownloadAsync(DownloadRequest request,
                                                                      IProgress<DownloadProgressInfo>? progress = null,
                                                                      CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (request.Urls is null || request.Urls.Count == 0)
        {
            return OperationResult<DownloadTaskInfo>.Fail("没有可用的下载地址，未下载任何内容。");
        }

        if (string.IsNullOrWhiteSpace(request.DestinationPath))
        {
            return OperationResult<DownloadTaskInfo>.Fail("下载目标路径为空。", retryable: false);
        }

        var destination = Path.GetFullPath(request.DestinationPath);
        var directory = Path.GetDirectoryName(destination);
        if (string.IsNullOrEmpty(directory))
        {
            return OperationResult<DownloadTaskInfo>.Fail($"下载目标路径无效：{request.DestinationPath}", retryable: false);
        }

        try
        {
            Directory.CreateDirectory(directory);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            return DiskFailure<DownloadTaskInfo>(ex, request, $"无法创建下载目录：{directory}");
        }

        // 同目录临时文件：只有同卷改名才是原子的（跨卷会退化成复制）。
        var temp = Path.Combine(directory,
                                Path.GetFileName(destination) + ".tmp-" + Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture));

        var reporter = new ProgressReporter(this, request, progress);
        var stopwatch = Stopwatch.StartNew();
        reporter.PublishStart();

        try
        {
            ct.ThrowIfCancellationRequested();

            OperationResult<TransferOutcome> transferred;
            try
            {
                transferred = await _transport.TransferAsync(request, temp, reporter.TransferSink, ct)
                                             .ConfigureAwait(false);
            }
            catch (OperationCanceledException ex)
            {
                return Cancel<DownloadTaskInfo>(ex, request);
            }
            catch (Exception ex)
            {
                return FailFromException<DownloadTaskInfo>(ex, request);
            }

            if (!transferred.IsSuccess)
            {
                // 传输层已经归好类（可重试 / 不可重试 / 取消），原样上报。
                return OperationResult<DownloadTaskInfo>.Fail(
                    transferred.Message ?? DownloadFailureClassifier.GenericMessage,
                    transferred.Detail,
                    transferred.Retryable);
            }

            var outcome = transferred.Value!;

            if (!File.Exists(temp))
            {
                return OperationResult<DownloadTaskInfo>.Fail(
                    "下载已结束但临时文件不存在。",
                    $"临时文件: {temp}",
                    retryable: true);
            }

            var length = new FileInfo(temp).Length;

            // 大小校验（元数据给了才校验）。属于校验类失败：重试同一份数据必然再失败。
            if (request.ExpectedSize > 0 && length != request.ExpectedSize)
            {
                var message = FormattableString.Invariant(
                    $"下载的文件大小不符（期望 {request.ExpectedSize} 字节，实际 {length} 字节）。");
                _log.Warn($"{request.Title} {message}");
                return OperationResult<DownloadTaskInfo>.Fail(message, $"临时文件: {temp}", retryable: false);
            }

            // 取消可能发生在传输刚结束的瞬间：改名之前再确认一次，避免「取消了却还是落了盘」。
            ct.ThrowIfCancellationRequested();

            var sha256 = outcome.Sha256;
            if (!string.IsNullOrWhiteSpace(request.ExpectedSha256))
            {
                sha256 ??= ComputeSha256(temp);
                if (!HashEquals(sha256, request.ExpectedSha256))
                {
                    var message = "下载的文件校验失败（SHA256 不符），已丢弃。";
                    _log.Warn($"{request.Title} {message} 期望 {request.ExpectedSha256} 实际 {sha256}");
                    return OperationResult<DownloadTaskInfo>.Fail(
                        message,
                        FormattableString.Invariant($"期望: {request.ExpectedSha256}{Environment.NewLine}实际: {sha256}"),
                        retryable: false);
                }
            }

            sha256 ??= ComputeSha256(temp);

            try
            {
                MoveIntoPlace(temp, destination);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return DiskFailure<DownloadTaskInfo>(ex, request, $"无法写入目标文件：{destination}");
            }

            stopwatch.Stop();
            reporter.PublishCompleted(length, request.ExpectedSize > 0 ? request.ExpectedSize : length);

            _log.Info(FormattableString.Invariant(
                $"下载落盘 {request.Title} → {destination}（{length} 字节，{stopwatch.ElapsedMilliseconds} ms）"));

            return OperationResult<DownloadTaskInfo>.Ok(new DownloadTaskInfo(
                destination,
                request.PrimaryUrl,
                length,
                request.ExpectedSize > 0 ? request.ExpectedSize : length,
                sha256,
                outcome.ETag,
                stopwatch.Elapsed));
        }
        catch (OperationCanceledException ex)
        {
            return Cancel<DownloadTaskInfo>(ex, request);
        }
        catch (Exception ex)
        {
            return FailFromException<DownloadTaskInfo>(ex, request);
        }
        finally
        {
            TryDeleteTemp(temp);
        }
    }

    /// <summary>目标文件已存在时覆盖；占用则短暂重试。</summary>
    private static void MoveIntoPlace(string temp, string destination)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                File.Move(temp, destination, overwrite: true);
                return;
            }
            catch (IOException) when (attempt < MoveAttempts)
            {
                Thread.Sleep(50 * attempt);
            }
        }
    }

    private static string ComputeSha256(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, FileOptions.SequentialScan);
        return Convert.ToHexString(SHA256.HashData(stream));
    }

    private static bool HashEquals(string? actual, string? expected)
    {
        if (string.IsNullOrWhiteSpace(actual) || string.IsNullOrWhiteSpace(expected))
        {
            return false;
        }

        return string.Equals(Normalize(actual), Normalize(expected), StringComparison.OrdinalIgnoreCase);

        static string Normalize(string value)
            => value.Trim().Replace("-", string.Empty, StringComparison.Ordinal)
                          .Replace(" ", string.Empty, StringComparison.Ordinal);
    }

    private void TryDeleteTemp(string temp)
    {
        try
        {
            if (File.Exists(temp))
            {
                File.Delete(temp);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // 删不掉也不能盖掉真正的失败原因，只记日志。
            _log.Warn($"临时文件清理失败：{temp}", ex);
        }
    }

    private OperationResult<T> Cancel<T>(Exception ex, DownloadRequest request)
    {
        _log.Info($"下载已取消：{request.Title}");
        var failure = DownloadFailureClassifier.Classify(ex, request.Title, request.PrimaryUrl);
        return OperationResult<T>.Fail(DownloadFailureClassifier.CancelledMessage, failure.Detail, retryable: false);
    }

    private OperationResult<T> FailFromException<T>(Exception ex, DownloadRequest request)
    {
        var failure = DownloadFailureClassifier.Classify(ex, request.Title, request.PrimaryUrl);
        if (failure.Cancelled)
        {
            return Cancel<T>(ex, request);
        }

        _log.Warn($"下载失败：{request.Title}", ex);
        _errorReporter?.Report(ex, "下载", failure.Message);
        return DownloadFailureClassifier.ToResult<T>(failure);
    }

    private OperationResult<T> DiskFailure<T>(Exception ex, DownloadRequest request, string message)
    {
        _log.Warn($"{message}（{request.Title}）", ex);
        _errorReporter?.Report(ex, "下载", message);
        var detail = $"{ex.GetType().Name}: {ex.Message}{Environment.NewLine}目标: {request.DestinationPath}";
        return OperationResult<T>.Fail(message, detail, retryable: false);
    }

    /// <summary>
    /// 进度汇总：100ms 节流 + 速率平滑 + 封送到 UI 线程。
    /// </summary>
    /// <remarks>
    /// 速率取「全程平均」与「本窗口瞬时」的均值：纯瞬时抖动大（一个 64KB 块就能翻倍），
    /// 纯平均又对「网络突然变慢」反应太慢（05 篇 §4 的体验要求）。
    /// </remarks>
    private sealed class ProgressReporter
    {
        private readonly DownloadService _owner;
        private readonly DownloadRequest _request;
        private readonly IProgress<DownloadProgressInfo>? _sink;
        private readonly Stopwatch _clock = Stopwatch.StartNew();
        private readonly object _gate = new();

        private long _lastPublishTicks;
        private long _lastBytes;
        private long _lastSampleTicks;
        private long _bytes;
        private long _total;
        private double _rate;

        public ProgressReporter(DownloadService owner, DownloadRequest request, IProgress<DownloadProgressInfo>? sink)
        {
            _owner = owner;
            _request = request;
            _sink = sink;
        }

        /// <summary>交给传输层的原始进度接收者（可能在后台线程被调用）。</summary>
        public IProgress<TransferProgress>? TransferSink
            => _sink is null ? null : new InlineProgress(this);

        public void PublishStart()
            => Publish(DownloadProgressInfo.Start(FormattableString.Invariant($"正在下载 {_request.Title}…")), force: true);

        public void PublishCompleted(long bytes, long total)
            => Publish(new DownloadProgressInfo(bytes, total, _rate, 100, FormattableString.Invariant($"下载完成 {_request.Title}")), force: true);

        private void OnTransferProgress(TransferProgress progress)
        {
            var now = Environment.TickCount64;
            double rate;
            lock (_gate)
            {
                _bytes = Math.Max(_bytes, progress.BytesReceived);
                if (progress.TotalBytes > 0)
                {
                    _total = progress.TotalBytes;
                }

                var elapsed = _clock.Elapsed.TotalSeconds;
                var overall = elapsed > 0.0005 ? _bytes / elapsed : 0;

                var window = (now - _lastSampleTicks) / 1000.0;
                var instant = window > 0.0005 ? (_bytes - _lastBytes) / window : overall;
                if (window > 0.0005)
                {
                    _lastBytes = _bytes;
                    _lastSampleTicks = now;
                }

                _rate = overall <= 0 ? Math.Max(0, instant) : 0.5 * (overall + Math.Max(0, instant));
                rate = _rate;
            }

            var percent = _total > 0 ? (int)Math.Clamp(_bytes * 100 / _total, 0, 100) : 0;
            var message = _total > 0
                ? FormattableString.Invariant($"正在下载 {_request.Title}：{FormatBytes(_bytes)} / {FormatBytes(_total)}（{FormatRate(rate)}）")
                : FormattableString.Invariant($"正在下载 {_request.Title}：{FormatBytes(_bytes)}（{FormatRate(rate)}）");

            Publish(new DownloadProgressInfo(_bytes, _total, rate, percent, message), force: false);
        }

        private void Publish(DownloadProgressInfo info, bool force)
        {
            if (_sink is null)
            {
                return;
            }

            var now = Environment.TickCount64;
            lock (_gate)
            {
                if (!force && now - _lastPublishTicks < ThrottleMilliseconds)
                {
                    return;
                }

                _lastPublishTicks = now;
            }

            // 05 篇 §6：跨线程事件一律经 IDispatcher.Post 封送，不在后台线程碰 UI。
            _owner._dispatcher.Post(() =>
            {
                try
                {
                    _sink.Report(info);
                }
                catch (Exception ex)
                {
                    // 进度订阅者的异常不能反过来弄死下载。
                    _owner._log.Warn("下载进度回调抛出异常", ex);
                }
            });
        }

        private static string FormatBytes(long bytes)
            => bytes >= 1024L * 1024 * 1024
                ? FormattableString.Invariant($"{bytes / (1024.0 * 1024 * 1024):0.00} GB")
                : bytes >= 1024L * 1024
                    ? FormattableString.Invariant($"{bytes / (1024.0 * 1024):0.0} MB")
                    : bytes >= 1024
                        ? FormattableString.Invariant($"{bytes / 1024.0:0.0} KB")
                        : FormattableString.Invariant($"{bytes} B");

        private static string FormatRate(double bytesPerSecond)
            => bytesPerSecond <= 0 ? "—" : FormatBytes((long)bytesPerSecond) + "/s";

        /// <summary>把传输层回调转成进度汇报（避免每帧多一次 lambda 分配）。</summary>
        private sealed class InlineProgress : IProgress<TransferProgress>
        {
            private readonly ProgressReporter _parent;

            public InlineProgress(ProgressReporter parent) => _parent = parent;

            public void Report(TransferProgress value) => _parent.OnTransferProgress(value);
        }
    }
}
