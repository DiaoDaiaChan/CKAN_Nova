using System;

using Avalonia;

using CkanUI.App.Animations;
using CkanUI.App.Tests.Automation;
using CkanUI.App.Theming;

using NUnit.Framework;

namespace CkanUI.App.Tests;

/// <summary>换页入场姿态：方向符号、主轴切换、三档幅度、关闭档不位移。</summary>
[TestFixture]
[NonParallelizable]
public sealed class PageRevealSpecTests
{
    [SetUp]
    public void SetUp()
    {
        HeadlessAppHost.EnsureInitialized();
        SetTokens(page: 200, fast: 90);
    }

    [TearDown]
    public void TearDown() => SetTokens(page: 200, fast: 90);

    [Test]
    public void ForwardSwitchComesInFromTheRight()
    {
        var pose = PageRevealSpec.For(1);

        Assert.That(pose.OffsetX, Is.EqualTo(PageRevealSpec.DirectionalHorizontalPixels).Within(1e-9));
        Assert.That(pose.OffsetY, Is.EqualTo(PageRevealSpec.DirectionalVerticalPixels).Within(1e-9));
        Assert.That(pose.StartScale, Is.EqualTo(PageRevealSpec.StartScale).Within(1e-9));
        Assert.That(pose.StartOpacity, Is.EqualTo(PageRevealSpec.DirectionalStartOpacity).Within(1e-9));
    }

    [Test]
    public void BackwardSwitchMirrorsTheHorizontalOffset()
    {
        Assert.That(PageRevealSpec.For(-1).OffsetX, Is.EqualTo(-PageRevealSpec.DirectionalHorizontalPixels).Within(1e-9));
    }

    [Test]
    public void HorizontalIsTheMainAxisWhenSwitchingTabs()
    {
        var pose = PageRevealSpec.For(1);

        Assert.That(Math.Abs(pose.OffsetX), Is.GreaterThan(pose.OffsetY * 2), "横切时水平位移必须是主轴");
    }

    [Test]
    public void SameSectionFirstVisitOnlyLifts()
    {
        var pose = PageRevealSpec.For(0);

        Assert.That(pose.OffsetX, Is.EqualTo(0).Within(1e-9), "没有方向时不许横移");
        Assert.That(pose.OffsetY, Is.EqualTo(PageRevealSpec.LiftOnlyPixels).Within(1e-9));
        Assert.That(pose.OffsetY, Is.GreaterThan(PageRevealSpec.DirectionalVerticalPixels));
    }

    [Test]
    public void HorizontalSwitchFadesInFurtherThanTheLiftOnlyOne()
    {
        Assert.That(
            PageRevealSpec.For(1).StartOpacity,
            Is.LessThan(PageRevealSpec.For(0).StartOpacity),
            "横切位移大，起点要更透明才不糊");
    }

    [Test]
    public void ReducedMotionScalesEveryChannelToOneHalf()
    {
        SetTokens(page: 0, fast: 90);

        var pose = PageRevealSpec.For(1);

        Assert.That(pose.OffsetX, Is.EqualTo(PageRevealSpec.DirectionalHorizontalPixels * MotionBudget.ReducedAmplitudeScale).Within(1e-6));
        Assert.That(pose.StartScale, Is.GreaterThan(0.99));
        Assert.That(pose.StartOpacity, Is.GreaterThan(0.5));
    }

    [Test]
    public void CollapsedMotionHasNoPoseAtAll()
    {
        SetTokens(page: 0, fast: 0);

        var pose = PageRevealSpec.For(-1);

        Assert.That(pose.OffsetX, Is.EqualTo(0));
        Assert.That(pose.OffsetY, Is.EqualTo(0));
        Assert.That(pose.StartScale, Is.EqualTo(1));
        Assert.That(pose.StartOpacity, Is.EqualTo(1));
    }

    private static void SetTokens(int page, int fast)
    {
        var resources = Application.Current!.Resources;
        resources[MotionHost.PageKey] = TimeSpan.FromMilliseconds(page);
        resources[MotionHost.FastKey] = TimeSpan.FromMilliseconds(fast);
    }
}
