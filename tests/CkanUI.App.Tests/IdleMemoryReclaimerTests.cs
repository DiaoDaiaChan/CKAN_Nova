using System;
using CkanUI.App.Diagnostics;
using NUnit.Framework;

namespace CkanUI.App.Tests;

[TestFixture]
public sealed class IdleMemoryReclaimerTests
{
    private static bool ShouldReclaim(
        bool isIdle = true,
        TimeSpan? sinceAttach = null,
        bool hasReclaimed = true,
        long workingSetBytes = 1_000_000_000L,
        long workingSetAfterLastReclaim = 400_000_000L,
        TimeSpan? sinceLastReclaim = null)
    {
        return IdleMemoryReclaimerPolicy.ShouldReclaim(
            isIdle,
            sinceAttach ?? TimeSpan.FromMinutes(1),
            hasReclaimed,
            workingSetBytes,
            workingSetAfterLastReclaim,
            sinceLastReclaim ?? TimeSpan.FromMinutes(10));
    }

    [Test]
    public void ShouldReclaim_WhenNotIdle_ReturnsFalse()
    {
        Assert.That(ShouldReclaim(isIdle: false, hasReclaimed: false), Is.False);
    }

    [Test]
    public void ShouldReclaim_DuringStartupLoad_ReturnsFalse()
    {
        // 启动加载期收回等于白干（实测 8 秒就回收的版本被撑回 840 MB），必须等 MinAttachSeconds。
        Assert.That(
            ShouldReclaim(
                sinceAttach: TimeSpan.FromSeconds(IdleMemoryReclaimerPolicy.MinAttachSeconds) - TimeSpan.FromSeconds(1),
                hasReclaimed: false),
            Is.False);
    }

    [Test]
    public void ShouldReclaim_WhenNeverReclaimed_ReclaimsOnFirstIdle()
    {
        Assert.That(ShouldReclaim(hasReclaimed: false, sinceLastReclaim: TimeSpan.Zero), Is.True);
    }

    [Test]
    public void ShouldReclaim_WithinCooldown_ReturnsFalse()
    {
        Assert.That(
            ShouldReclaim(sinceLastReclaim: IdleMemoryReclaimerPolicy.MinInterval - TimeSpan.FromSeconds(1)),
            Is.False);
    }

    [Test]
    public void ShouldReclaim_WhenGrowthBelowThreshold_ReturnsFalse()
    {
        var workingSet = 400_000_000L + IdleMemoryReclaimerPolicy.GrowthThresholdBytes - 1;

        Assert.That(ShouldReclaim(workingSetBytes: workingSet), Is.False);
    }

    [Test]
    public void ShouldReclaim_WhenGrowthReachesThreshold_ReturnsTrue()
    {
        var workingSet = 400_000_000L + IdleMemoryReclaimerPolicy.GrowthThresholdBytes;

        Assert.That(ShouldReclaim(workingSetBytes: workingSet), Is.True);
    }

    [Test]
    public void ReclaimIfNeeded_WhenNotIdle_DoesNotReclaim()
    {
        var reclaims = 0;
        using var reclaimer = new IdleMemoryReclaimer(
            () => 1_000_000_000L, () => reclaims++, () => false, () => TimeSpan.FromMinutes(1));

        Assert.That(reclaimer.ReclaimIfNeeded(), Is.False);
        Assert.That(reclaims, Is.EqualTo(0));
        Assert.That(reclaimer.ReclaimCount, Is.EqualTo(0));
    }

    [Test]
    public void ReclaimIfNeeded_WhenIdle_ReclaimsOnceThenRespectsCooldown()
    {
        var reclaims = 0;
        long workingSet = 1_000_000_000L;
        using var reclaimer = new IdleMemoryReclaimer(
            () => workingSet, () => reclaims++, () => true, () => TimeSpan.FromMinutes(1));

        Assert.That(reclaimer.ReclaimIfNeeded(), Is.True);
        Assert.That(reclaims, Is.EqualTo(1));
        Assert.That(reclaimer.ReclaimCount, Is.EqualTo(1));

        // 刚回收完工作集没涨，冷却期内不应再回收。
        Assert.That(reclaimer.ReclaimIfNeeded(), Is.False);
        Assert.That(reclaims, Is.EqualTo(1));

        workingSet = 1_500_000_000L;
        Assert.That(reclaimer.ReclaimIfNeeded(), Is.False, "冷却期未过，不应再次回收。");
        Assert.That(reclaims, Is.EqualTo(1));
    }

    [Test]
    public void ReclaimIfNeeded_DuringStartupLoad_DoesNotReclaim()
    {
        var reclaims = 0;
        using var reclaimer = new IdleMemoryReclaimer(
            () => 1_000_000_000L, () => reclaims++, () => true, () => TimeSpan.FromSeconds(3));

        Assert.That(reclaimer.ReclaimIfNeeded(), Is.False);
        Assert.That(reclaims, Is.EqualTo(0));
    }

    [Test]
    public void Detach_WithoutAttach_IsSafe()
    {
        var reclaimer = new IdleMemoryReclaimer(() => 0L, () => { }, () => false, () => TimeSpan.FromMinutes(1));

        Assert.DoesNotThrow(reclaimer.Detach);
        Assert.DoesNotThrow(reclaimer.Detach);
        Assert.DoesNotThrow(reclaimer.Dispose);
    }
}