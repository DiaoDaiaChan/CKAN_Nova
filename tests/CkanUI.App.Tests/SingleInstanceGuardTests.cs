using System.Collections.Concurrent;
using CkanUI.Platform.SingleInstance;
using NUnit.Framework;

namespace CkanUI.App.Tests;

/// <summary>单实例互斥与参数转发（ARC-05）。每个用例用独立 appId，避免并行互相干扰。</summary>
[TestFixture]
public class SingleInstanceGuardTests
{
    [Test]
    public void TryAcquire_SecondCallerIsRejected()
    {
        var appId = "Test-" + Guid.NewGuid().ToString("N");
        using var first = SingleInstanceGuard.TryAcquire(appId);
        Assert.That(first, Is.Not.Null);

        using var second = SingleInstanceGuard.TryAcquire(appId);
        Assert.That(second, Is.Null);
    }

    [Test]
    public void Dispose_ReleasesMutex_SoNextAcquireSucceeds()
    {
        var appId = "Test-" + Guid.NewGuid().ToString("N");
        var first = SingleInstanceGuard.TryAcquire(appId);
        Assert.That(first, Is.Not.Null);
        first!.Dispose();

        using var second = SingleInstanceGuard.TryAcquire(appId);
        Assert.That(second, Is.Not.Null);
    }

    [Test]
    public void ForwardToOwner_DeliversArgumentsToListener()
    {
        var appId = "Test-" + Guid.NewGuid().ToString("N");
        using var guard = SingleInstanceGuard.TryAcquire(appId);
        Assert.That(guard, Is.Not.Null);

        var received = new ConcurrentQueue<IReadOnlyList<string>>();
        guard!.ArgumentsReceived += args => received.Enqueue(args);
        guard.StartListening();

        var payload = new[] { "ckan://mod/B9PartSwitch", "--flag" };
        var forwarded = SingleInstanceGuard.TryForwardToOwner(appId, payload, TimeSpan.FromSeconds(5));
        Assert.That(forwarded, Is.True);

        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(5);
        while (received.IsEmpty && DateTime.UtcNow < deadline) Thread.Sleep(50);

        Assert.That(received.TryDequeue(out var args), Is.True);
        Assert.That(args, Is.EqualTo(payload));
    }

    [Test]
    public void ForwardToOwner_ReturnsFalse_WhenNobodyListens()
    {
        var appId = "Test-" + Guid.NewGuid().ToString("N");
        Assert.That(SingleInstanceGuard.TryForwardToOwner(appId, new[] { "x" }, TimeSpan.FromMilliseconds(300)),
            Is.False);
    }
}
