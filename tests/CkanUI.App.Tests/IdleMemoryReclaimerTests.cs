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
        TimeSpan? sinceLastReclaim = null,
        TimeSpan? sinceLastInteraction = null)
    {
        return IdleMemoryReclaimerPolicy.ShouldReclaim(
            isIdle,
            sinceLastInteraction ?? TimeSpan.FromMinutes(1),
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
    public void ShouldReclaim_RightAfterUserInteraction_ReturnsFalse()
    {
        // 用户刚放下鼠标就压堆，压缩式 GC 会正好落在下一次交互上：实测单次 0.7–1.5 秒冻结。
        Assert.That(
            ShouldReclaim(hasReclaimed: false, sinceLastInteraction: TimeSpan.FromSeconds(IdleMemoryReclaimerPolicy.MinQuietSeconds) - TimeSpan.FromSeconds(1)),
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
    public void ReclaimNow_InvokesBeforeReclaimHookBeforeCollecting()
    {
        var order = new List<string>();
        IdleMemoryReclaimer.BeforeReclaim = () => order.Add("trim");
        try
        {
            var reclaimer = new IdleMemoryReclaimer(
                () => 0L, () => order.Add("gc"), () => true, () => TimeSpan.FromMinutes(1));

            reclaimer.ReclaimNow();

            // 顺序是语义的一部分：弦上的投影要先松手，压缩堆才带得走。
            Assert.That(order, Is.EqualTo(new[] { "trim", "gc" }));
        }
        finally
        {
            IdleMemoryReclaimer.BeforeReclaim = null;
        }
    }

    [Test]
    public void ReclaimNow_WhenHookThrows_StillCollects()
    {
        IdleMemoryReclaimer.BeforeReclaim = () => throw new InvalidOperationException("投影释放失败");
        try
        {
            var collected = false;
            var reclaimer = new IdleMemoryReclaimer(
                () => 0L, () => collected = true, () => true, () => TimeSpan.FromMinutes(1));

            Assert.DoesNotThrow(reclaimer.ReclaimNow);
            Assert.That(collected, Is.True, "钩子失败不该挡住回收。");
        }
        finally
        {
            IdleMemoryReclaimer.BeforeReclaim = null;
        }
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