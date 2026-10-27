using System.Net;
using System.Net.Http;
using System.Net.Sockets;

using CKAN;

using CkanUI.Services.Downloads;

using NUnit.Framework;

namespace CkanUI.Services.Tests;

[TestFixture]
public sealed class DownloadFailureClassifierTests
{
    private static readonly Uri Source = new("https://example.invalid/mod.zip");

    private static NetAsyncDownloader.DownloadTargetFile Target()
        => new(new List<Uri> { Source }, "/tmp/x.zip");

    [Test]
    public void WebException_IsRetryableNetworkFailure()
    {
        var failure = DownloadFailureClassifier.Classify(new WebException("连接被重置"), "mod.zip", Source);

        Assert.That(failure.Message, Is.EqualTo(DownloadFailureClassifier.NetworkMessage));
        Assert.That(failure.Retryable, Is.True);
        Assert.That(failure.Cancelled, Is.False);
        Assert.That(failure.Detail, Does.Contain("mod.zip"));
        Assert.That(failure.Detail, Does.Contain("example.invalid"));
        Assert.That(failure.Detail, Does.Contain("WebException"));
    }

    [Test]
    public void HttpAndSocketAndTimeoutAndIo_AreRetryable()
    {
        Assert.That(DownloadFailureClassifier.IsRetryableNetwork(new HttpRequestException("boom")), Is.True);
        Assert.That(DownloadFailureClassifier.IsRetryableNetwork(new SocketException(10054)), Is.True);
        Assert.That(DownloadFailureClassifier.IsRetryableNetwork(new TimeoutException()), Is.True);
        Assert.That(DownloadFailureClassifier.IsRetryableNetwork(new IOException("断流")), Is.True);
    }

    [Test]
    public void AggregateOfNetworkExceptions_IsRetryable()
    {
        var aggregate = new AggregateException(new HttpRequestException("a"), new IOException("b"));

        Assert.That(DownloadFailureClassifier.IsRetryableNetwork(aggregate), Is.True);
        Assert.That(DownloadFailureClassifier.Classify(aggregate).Retryable, Is.True);
    }

    [Test]
    public void MixedAggregate_IsNotRetryable()
    {
        var aggregate = new AggregateException(new HttpRequestException("a"), new InvalidOperationException("b"));

        Assert.That(DownloadFailureClassifier.IsRetryableNetwork(aggregate), Is.False);
        Assert.That(DownloadFailureClassifier.Classify(aggregate).Retryable, Is.False);
    }

    [Test]
    public void UnauthorizedAccess_IsNotRetryable()
    {
        var failure = DownloadFailureClassifier.Classify(new UnauthorizedAccessException("拒绝访问"));

        Assert.That(failure.Retryable, Is.False);
        Assert.That(failure.Message, Is.EqualTo(DownloadFailureClassifier.GenericMessage));
    }

    [Test]
    public void OperationCanceled_IsReportedAsCancelled()
    {
        var failure = DownloadFailureClassifier.Classify(new OperationCanceledException());

        Assert.That(failure.Cancelled, Is.True);
        Assert.That(failure.Retryable, Is.False);
        Assert.That(failure.Message, Is.EqualTo(DownloadFailureClassifier.CancelledMessage));
    }

    [Test]
    public void CancelledActionKraken_IsReportedAsCancelled()
    {
        var failure = DownloadFailureClassifier.Classify(new CancelledActionKraken("用户取消"));

        Assert.That(failure.Cancelled, Is.True);
        Assert.That(failure.Message, Is.EqualTo(DownloadFailureClassifier.CancelledMessage));
    }

    [Test]
    public void MissingCertificateKraken_IsRetryable()
    {
        var failure = DownloadFailureClassifier.Classify(new MissingCertificateKraken(Source));

        Assert.That(failure.Retryable, Is.True);
        Assert.That(failure.Message, Does.Contain("证书"));
    }

    [Test]
    public void RequestThrottledKraken_IsRetryable()
    {
        var failure = DownloadFailureClassifier.Classify(
            new RequestThrottledKraken(Source, new Uri("https://example.invalid/info"), new WebException("429")));

        Assert.That(failure.Retryable, Is.True);
        Assert.That(failure.Message, Does.Contain("限流"));
    }

    [Test]
    public void DownloadErrorsKraken_WithNetworkInner_IsRetryable_AndListsMirrors()
    {
        var errors = new List<KeyValuePair<NetAsyncDownloader.DownloadTarget, Exception>>
        {
            new(Target(), new WebException("镜像 A 超时")),
        };

        var failure = DownloadFailureClassifier.Classify(new DownloadErrorsKraken(errors), "mod.zip", Source);

        Assert.That(failure.Retryable, Is.True);
        Assert.That(failure.Cancelled, Is.False);
        Assert.That(failure.Detail, Does.Contain("镜像 A 超时"));
    }

    [Test]
    public void DownloadErrorsKraken_WithPermanentInner_IsNotRetryable()
    {
        var errors = new List<KeyValuePair<NetAsyncDownloader.DownloadTarget, Exception>>
        {
            new(Target(), new InvalidOperationException("校验失败")),
        };

        var failure = DownloadFailureClassifier.Classify(new DownloadErrorsKraken(errors));

        Assert.That(failure.Retryable, Is.False);
    }

    [Test]
    public void ToResult_CopiesMessageDetailAndRetryable()
    {
        var failure = new DownloadFailure("文案", "细节", Retryable: true, Cancelled: false);

        var result = DownloadFailureClassifier.ToResult<int>(failure);

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Message, Is.EqualTo("文案"));
        Assert.That(result.Detail, Is.EqualTo("细节"));
        Assert.That(result.Retryable, Is.True);
    }

    [Test]
    public void LongDetail_IsTruncated()
    {
        var failure = DownloadFailureClassifier.Classify(new InvalidOperationException(new string('x', 5000)));

        Assert.That(failure.Detail!.Length, Is.LessThanOrEqualTo(2001));
    }
}
