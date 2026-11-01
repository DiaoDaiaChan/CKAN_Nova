using System;

using Avalonia.Controls;

using CkanUI.App.Animations;
using CkanUI.App.Tests.Automation;

using NUnit.Framework;

namespace CkanUI.App.Tests;

/// <summary>
/// 静止判定：换页幕布要等两页真的停住才收尾，不能按固定时长硬抹最后几像素。
/// </summary>
[TestFixture]
[NonParallelizable]
public sealed class SpringSettleTests
{
    [SetUp]
    public void SetUp() => HeadlessAppHost.EnsureInitialized();

    [Test]
    public void ASnappedSpringCountsAsSettled()
    {
        var border = new Border();
        var spring = new TransformSpring(border, SpringSpec.Glide, SpringSpec.Glide);

        Assert.That(spring.IsSettled, Is.True, "刚建好还没动，算静止");

        spring.MoveTo(x: 120, snap: true);
        Assert.That(spring.IsSettled, Is.True, "snap 是立即落位，不是运动");
    }

    [Test]
    public void AFreshMoveIsNotSettledUntilItArrives()
    {
        var border = new Border();
        var window = HeadlessAppHost.CreateTestWindow(border, width: 400, height: 300);
        var spring = new TransformSpring(border, SpringSpec.Glide, SpringSpec.Glide);

        try
        {
            spring.MoveTo(x: 120);
            Assert.That(spring.IsSettled, Is.False, "刚给新目标、已挂树，还没到");

            var frames = 0;
            while (!spring.IsSettled && frames < 240)
            {
                spring.AdvanceFrame(1d / 60d);
                frames++;
            }

            Assert.That(frames, Is.LessThan(60), "1 s 内应收敛");
            Assert.That(spring.IsSettled, Is.True);
        }
        finally
        {
            window.Close();
            HeadlessAppHost.PumpJobs();
        }
    }

    [Test]
    public void AHeldChannelCountsAsMoving()
    {
        var border = new Border();
        var window = HeadlessAppHost.CreateTestWindow(border, width: 400, height: 300);
        var spring = new TransformSpring(border, SpringSpec.Glide, SpringSpec.Glide);

        try
        {
            spring.MoveTo(y: 24, hold: 0.2d);

            Assert.That(spring.IsSettled, Is.False, "还在排队等 hold，不算静止");

            for (var i = 0; i < 20; i++)
            {
                spring.AdvanceFrame(1d / 60d);
            }

            Assert.That(spring.IsSettled, Is.False, "hold 期间目标未达，不能算静止");
        }
        finally
        {
            window.Close();
            HeadlessAppHost.PumpJobs();
        }
    }
}
