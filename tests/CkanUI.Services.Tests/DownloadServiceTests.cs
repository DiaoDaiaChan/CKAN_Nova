using System.Net;
using System.Security.Cryptography;

using CkanUI.Services.Abstractions.Dtos;
using CkanUI.Services.Downloads;
using CkanUI.TestSupport;

using NUnit.Framework;

namespace CkanUI.Services.Tests;

[TestFixture]
public sealed class DownloadServiceTests
{
    private static DownloadRequest RequestFor(TempWorkspace workspace, string name = "mod.zip", long expectedSize = 0, string? sha256 = null)
        => new(new[] { new Uri("https://example.invalid/" + name) }, workspace.Combine(name), expectedSize, sha256);

    [Test]
    public async Task DownloadAsync_WritesDestination_AndLeavesNoTempFile()
    {
        using var workspace = new TempWorkspace("dl-ok");
        var payload = new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 };
        var expectedHash = Convert.ToHexString(SHA256.HashData(payload));
        var service = new DownloadService(FakeDownloadTransport.Ok(payload), new FakeDispatcher());

        var result = await service.DownloadAsync(RequestFor(workspace), null, CancellationToken.None);

        Assert.That(result.IsSuccess, Is.True, result.Message);
        Assert.That(result.Value!.BytesReceived, Is.EqualTo(payload.Length));
        Assert.That(result.Value.Sha256, Is.EqualTo(expectedHash));
        Assert.That(result.Value.ETag, Is.EqualTo("abc"));
        Assert.That(File.ReadAllBytes(workspace.Combine("mod.zip")), Is.EqualTo(payload));
        Assert.That(Directory.GetFiles(workspace.Path), Has.Length.EqualTo(1), "临时文件必须已被改名或删除");
    }

    [Test]
    public async Task DownloadAsync_CreatesMissingDirectory()
    {
        using var workspace = new TempWorkspace("dl-mkdir");
        var nested = Path.Combine(workspace.Combine("a", "b"), "mod.zip");
        var service = new DownloadService(FakeDownloadTransport.Ok(new byte[] { 9 }), new FakeDispatcher());

        var result = await service.DownloadAsync(
            new DownloadRequest(new[] { new Uri("https://example.invalid/mod.zip") }, nested), null, CancellationToken.None);

        Assert.That(result.IsSuccess, Is.True, result.Message);
        Assert.That(File.Exists(nested), Is.True);
    }

    [Test]
    public async Task DownloadAsync_EmptyUrls_FailsWithoutTouchingTransport()
    {
        using var workspace = new TempWorkspace("dl-empty");
        var transport = FakeDownloadTransport.Ok(new byte[] { 1 });
        var service = new DownloadService(transport, new FakeDispatcher());

        var result = await service.DownloadAsync(
            new DownloadRequest(Array.Empty<Uri>(), workspace.Combine("mod.zip")), null, CancellationToken.None);

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Retryable, Is.False);
        Assert.That(transport.Calls, Is.Zero);
        Assert.That(Directory.GetFiles(workspace.Path), Is.Empty);
    }

    [Test]
    public async Task DownloadAsync_Cancelled_CleansTempAndKeepsExistingDestination()
    {
        using var workspace = new TempWorkspace("dl-cancel");
        var destination = workspace.Combine("mod.zip");
        await File.WriteAllTextAsync(destination, "old-content");
        var service = new DownloadService(
            FakeDownloadTransport.CancelsAfterPartialWrite(new byte[] { 1, 2, 3, 4 }), new FakeDispatcher());

        using var cts = new CancellationTokenSource();
        cts.CancelAfter(TimeSpan.FromMilliseconds(50));
        var result = await service.DownloadAsync(RequestFor(workspace), null, cts.Token);

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Message, Is.EqualTo(DownloadFailureClassifier.CancelledMessage));
        Assert.That(await File.ReadAllTextAsync(destination), Is.EqualTo("old-content"), "取消不得改动既有目标文件");
        Assert.That(Directory.GetFiles(workspace.Path), Has.Length.EqualTo(1), "临时文件必须被清理");
    }

    [Test]
    public async Task DownloadAsync_AlreadyCancelledToken_FailsFastAndLeavesNothing()
    {
        using var workspace = new TempWorkspace("dl-cancel-pre");
        var transport = FakeDownloadTransport.Ok(new byte[] { 1 });
        var service = new DownloadService(transport, new FakeDispatcher());

        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var result = await service.DownloadAsync(RequestFor(workspace), null, cts.Token);

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Message, Is.EqualTo(DownloadFailureClassifier.CancelledMessage));
        Assert.That(Directory.GetFiles(workspace.Path), Is.Empty);
    }

    [Test]
    public async Task DownloadAsync_NetworkFailure_IsRetriedThenReportedRetryable()
    {
        using var workspace = new TempWorkspace("dl-net");
        var transport = FakeDownloadTransport.Throws(new WebException("连接被重置"));
        var service = new DownloadService(transport, new FakeDispatcher(), null, DownloadRetryPolicy.Immediate);

        var result = await service.DownloadAsync(RequestFor(workspace), null, CancellationToken.None);

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Retryable, Is.True);
        Assert.That(result.Message, Is.EqualTo(DownloadFailureClassifier.NetworkMessage));
        Assert.That(result.Detail, Does.Contain("WebException"));
        Assert.That(transport.Calls, Is.EqualTo(DownloadRetryPolicy.DefaultMaxAttempts), "网络类失败应自动重试到上限");
        Assert.That(Directory.GetFiles(workspace.Path), Is.Empty);
    }

    [Test]
    public async Task DownloadAsync_TransportFailureResult_PassesThroughClassification()
    {
        using var workspace = new TempWorkspace("dl-transport-fail");
        var transport = FakeDownloadTransport.Fails("下载服务器正在限流，请稍后重试。", "429", retryable: true);
        var service = new DownloadService(transport, new FakeDispatcher(), null, DownloadRetryPolicy.Immediate);

        var result = await service.DownloadAsync(RequestFor(workspace), null, CancellationToken.None);

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Retryable, Is.True);
        Assert.That(result.Message, Does.Contain("限流"));
        Assert.That(result.Detail, Is.EqualTo("429"));
        Assert.That(transport.Calls, Is.EqualTo(3), "可重试的传输失败应用满三次尝试");
    }

    [Test]
    public async Task DownloadAsync_SizeMismatch_IsNotRetryableAndKeepsExistingFile()
    {
        using var workspace = new TempWorkspace("dl-size");
        var destination = workspace.Combine("mod.zip");
        await File.WriteAllTextAsync(destination, "old-content");
        var service = new DownloadService(
            FakeDownloadTransport.Truncated(new byte[] { 1, 2, 3 }, claimedSize: 10), new FakeDispatcher());

        var result = await service.DownloadAsync(RequestFor(workspace, expectedSize: 10), null, CancellationToken.None);

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Retryable, Is.False, "校验类失败不应提示重试");
        Assert.That(result.Message, Does.Contain("大小不符"));
        Assert.That(await File.ReadAllTextAsync(destination), Is.EqualTo("old-content"));
        Assert.That(Directory.GetFiles(workspace.Path), Has.Length.EqualTo(1));
    }

    [Test]
    public async Task DownloadAsync_Sha256Mismatch_IsNotRetryableAndKeepsExistingFile()
    {
        using var workspace = new TempWorkspace("dl-sha");
        var destination = workspace.Combine("mod.zip");
        await File.WriteAllTextAsync(destination, "old-content");
        var payload = new byte[] { 1, 2, 3, 4 };
        var wrongHash = new string('A', 64);
        var service = new DownloadService(
            FakeDownloadTransport.Ok(payload, sha256: wrongHash), new FakeDispatcher());

        var result = await service.DownloadAsync(RequestFor(workspace, sha256: new string('B', 64)), null, CancellationToken.None);

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Retryable, Is.False);
        Assert.That(result.Message, Does.Contain("SHA256"));
        Assert.That(await File.ReadAllTextAsync(destination), Is.EqualTo("old-content"));
    }

    [Test]
    public async Task DownloadAsync_Sha256Match_AcceptsHyphenatedAndLowercase()
    {
        using var workspace = new TempWorkspace("dl-sha-ok");
        var payload = new byte[] { 42, 43, 44 };
        var upper = Convert.ToHexString(SHA256.HashData(payload));
        var expected = upper.ToLowerInvariant();
        var service = new DownloadService(FakeDownloadTransport.Ok(payload, sha256: upper), new FakeDispatcher());

        var result = await service.DownloadAsync(RequestFor(workspace, sha256: expected), null, CancellationToken.None);

        Assert.That(result.IsSuccess, Is.True, result.Message);
    }

    [Test]
    public async Task DownloadAsync_OverwritesExistingDestinationOnSuccess()
    {
        using var workspace = new TempWorkspace("dl-overwrite");
        var destination = workspace.Combine("mod.zip");
        await File.WriteAllTextAsync(destination, "old-content");
        var service = new DownloadService(FakeDownloadTransport.Ok(new byte[] { 7, 7, 7 }), new FakeDispatcher());

        var result = await service.DownloadAsync(RequestFor(workspace), null, CancellationToken.None);

        Assert.That(result.IsSuccess, Is.True, result.Message);
        Assert.That(File.ReadAllBytes(destination), Is.EqualTo(new byte[] { 7, 7, 7 }));
    }

    [Test]
    public async Task DownloadAsync_ReportsProgressThroughDispatcher_AndEndsAt100Percent()
    {
        using var workspace = new TempWorkspace("dl-progress");
        var dispatcher = new FakeDispatcher(queuePosts: true);
        var reports = new List<DownloadProgressInfo>();
        var sink = new SyncProgress(reports.Add);
        var payload = new byte[4096];
        var service = new DownloadService(FakeDownloadTransport.Slow(payload, stepDelayMs: 120), dispatcher);

        var task = service.DownloadAsync(RequestFor(workspace), sink, CancellationToken.None);
        var result = await task;
        Assert.That(dispatcher.Posted, Is.Not.Empty, "进度必须经 IDispatcher.Post 封送");
        Assert.That(reports, Is.Empty, "Post 未 Pump 之前不应交付给订阅者");

        dispatcher.PumpPosts();

        Assert.That(result.IsSuccess, Is.True, result.Message);
        Assert.That(reports, Is.Not.Empty);
        Assert.That(reports[^1].Percent, Is.EqualTo(100));
        Assert.That(reports[^1].BytesReceived, Is.EqualTo(payload.Length));
        Assert.That(reports.Any(r => r.BytesReceived > 0 && r.BytesReceived < payload.Length), Is.True, "中途应有字节级进度");
        Assert.That(reports.All(r => r.BytesReceived >= 0 && r.Percent is >= 0 and <= 100), Is.True);
    }

    [Test]
    public async Task DownloadAsync_ThrottlesHighFrequencyProgress()
    {
        using var workspace = new TempWorkspace("dl-throttle");
        var reports = new List<DownloadProgressInfo>();
        var sink = new SyncProgress(reports.Add);
        var service = new DownloadService(FakeDownloadTransport.Flood(new byte[] { 1 }, reports: 5000), new FakeDispatcher());

        var result = await service.DownloadAsync(RequestFor(workspace), sink, CancellationToken.None);

        Assert.That(result.IsSuccess, Is.True, result.Message);
        Assert.That(reports.Count, Is.LessThan(200), "100ms 节流后 5000 次上报应被压到很少几条");
        Assert.That(reports[^1].Percent, Is.EqualTo(100));
    }

    [Test]
    public async Task DownloadAsync_RetryableFailure_ResumesFromThePartialFile()
    {
        using var workspace = new TempWorkspace("dl-resume");
        var payload = Enumerable.Range(0, 32).Select(i => (byte)i).ToArray();
        var transport = FakeDownloadTransport.FailsOnceThenResumes(payload, partialBytes: 20);
        var service = new DownloadService(transport, new FakeDispatcher(), null, DownloadRetryPolicy.Immediate);

        var result = await service.DownloadAsync(RequestFor(workspace), null, CancellationToken.None);

        Assert.That(result.IsSuccess, Is.True, result.Message);
        Assert.That(transport.Calls, Is.EqualTo(2), "第一次失败后应自动再试一次");
        Assert.That(transport.ObservedExistingBytes, Is.EqualTo(new long[] { 0, 20 }),
                    "第二次调用必须能看到第一次留下的 20 字节，才谈得上续传");
        Assert.That(File.ReadAllBytes(workspace.Combine("mod.zip")), Is.EqualTo(payload), "续传拼起来的文件必须完整");
        Assert.That(result.Value!.BytesReceived, Is.EqualTo(payload.Length));
        Assert.That(Directory.GetFiles(workspace.Path), Has.Length.EqualTo(1), "成功后不得留下临时文件");
    }

    [Test]
    public async Task DownloadAsync_NonRetryableFailure_IsNotRetried()
    {
        using var workspace = new TempWorkspace("dl-noretry");
        var transport = FakeDownloadTransport.Fails("服务器返回 404，文件不存在。", null, retryable: false);
        var service = new DownloadService(transport, new FakeDispatcher(), null, DownloadRetryPolicy.Immediate);

        var result = await service.DownloadAsync(RequestFor(workspace), null, CancellationToken.None);

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Retryable, Is.False);
        Assert.That(transport.Calls, Is.EqualTo(1), "不可重试的失败不该再打一次服务器");
        Assert.That(Directory.GetFiles(workspace.Path), Is.Empty);
    }

    [Test]
    public async Task DownloadAsync_Retry_ReportsARetryMessageToTheProgressSink()
    {
        using var workspace = new TempWorkspace("dl-retry-msg");
        var reports = new List<DownloadProgressInfo>();
        var sink = new SyncProgress(reports.Add);
        var payload = new byte[] { 1, 2, 3, 4, 5, 6 };
        var transport = FakeDownloadTransport.FailsOnceThenResumes(payload, partialBytes: 3);
        var service = new DownloadService(transport, new FakeDispatcher(), null, DownloadRetryPolicy.Immediate);

        var result = await service.DownloadAsync(RequestFor(workspace), sink, CancellationToken.None);

        Assert.That(result.IsSuccess, Is.True, result.Message);
        Assert.That(reports.Any(r => r.Message.Contains("正在重试", StringComparison.Ordinal)
                                     && r.Message.Contains("2/3", StringComparison.Ordinal)),
                    Is.True,
                    "重试必须在进度文案里写明第几次，用户才知道不是卡死");
        Assert.That(reports[^1].Percent, Is.EqualTo(100));
    }

    [Test]
    public async Task DownloadAsync_CancelDuringBackoff_StopsRetrying()
    {
        using var workspace = new TempWorkspace("dl-retry-cancel");
        var transport = FakeDownloadTransport.Fails("下载失败，请检查网络连接后重试。", null, retryable: true);
        // 退避 5 秒：取消一定发生在等待里，而不是传输里。
        var service = new DownloadService(transport, new FakeDispatcher(), null,
                                          new DownloadRetryPolicy(3, new[] { 5000 }));

        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(80));
        var result = await service.DownloadAsync(RequestFor(workspace), null, cts.Token);

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Message, Is.EqualTo(DownloadFailureClassifier.CancelledMessage));
        Assert.That(result.Retryable, Is.False);
        Assert.That(transport.Calls, Is.EqualTo(1), "取消之后不该再有下一次尝试");
        Assert.That(Directory.GetFiles(workspace.Path), Is.Empty);
    }

    [Test]
    public void DownloadAsync_NullRequest_Throws()
    {
        var service = new DownloadService(FakeDownloadTransport.Ok(Array.Empty<byte>()), new FakeDispatcher());
        Assert.ThrowsAsync<ArgumentNullException>(() => service.DownloadAsync(null!));
    }

    /// <summary>同步 IProgress：断言时不必再关心封送线程。</summary>
    private sealed class SyncProgress : IProgress<DownloadProgressInfo>
    {
        private readonly Action<DownloadProgressInfo> _sink;

        public SyncProgress(Action<DownloadProgressInfo> sink) => _sink = sink;

        public void Report(DownloadProgressInfo value) => _sink(value);
    }
}
