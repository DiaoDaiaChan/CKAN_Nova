using CkanUI.Services.Downloads;

using NUnit.Framework;

namespace CkanUI.Services.Tests;

[TestFixture]
public sealed class DownloadRetryPolicyTests
{
    [Test]
    public void Default_IsThreeAttemptsWithGrowingBackoff()
    {
        var policy = DownloadRetryPolicy.Default;

        Assert.That(policy.MaxAttempts, Is.EqualTo(DownloadRetryPolicy.DefaultMaxAttempts));
        Assert.That(policy.MaxAttempts, Is.EqualTo(3));
        Assert.That(policy.DelayBefore(1), Is.EqualTo(TimeSpan.FromMilliseconds(400)));
        Assert.That(policy.DelayBefore(2), Is.EqualTo(TimeSpan.FromMilliseconds(1200)));
        Assert.That(policy.DelayBefore(3), Is.EqualTo(TimeSpan.FromMilliseconds(1200)), "末尾的等待一直复用最后一个值");
    }

    [Test]
    public void Immediate_NeverWaits()
    {
        Assert.That(DownloadRetryPolicy.Immediate.DelayBefore(1), Is.EqualTo(TimeSpan.Zero));
        Assert.That(DownloadRetryPolicy.Immediate.MaxAttempts, Is.EqualTo(DownloadRetryPolicy.DefaultMaxAttempts));
    }

    [Test]
    public void ShouldRetry_RequiresRetryableAndRemainingAttempts()
    {
        var policy = new DownloadRetryPolicy(maxAttempts: 2);

        Assert.That(policy.ShouldRetry(retryable: true, completedAttempt: 1), Is.True);
        Assert.That(policy.ShouldRetry(retryable: true, completedAttempt: 2), Is.False, "已到上限");
        Assert.That(policy.ShouldRetry(retryable: false, completedAttempt: 1), Is.False, "不可重试的失败一次都不该重试");
    }

    [Test]
    public void Constructor_ClampsTheAttemptCount()
    {
        Assert.That(new DownloadRetryPolicy(maxAttempts: 0).MaxAttempts, Is.EqualTo(1));
        Assert.That(new DownloadRetryPolicy(maxAttempts: -5).MaxAttempts, Is.EqualTo(1));
    }

    [Test]
    public void Constructor_IgnoresNegativeDelays()
    {
        var policy = new DownloadRetryPolicy(maxAttempts: 3, delaysMs: new[] { -1, 250 });

        Assert.That(policy.DelayBefore(1), Is.EqualTo(TimeSpan.FromMilliseconds(250)));
        Assert.That(policy.DelayBefore(2), Is.EqualTo(TimeSpan.FromMilliseconds(250)));
    }

    [Test]
    public void ToString_MentionsTheAttemptsAndBackoff()
    {
        Assert.That(DownloadRetryPolicy.Default.ToString(), Does.Contain("3"));
        Assert.That(DownloadRetryPolicy.Default.ToString(), Does.Contain("400"));
    }
}
