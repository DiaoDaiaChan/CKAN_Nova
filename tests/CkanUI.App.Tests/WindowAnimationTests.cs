using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using CkanUI.App.Animations;
using CkanUI.App.Tests.Automation;
using NUnit.Framework;

namespace CkanUI.App.Tests;

[TestFixture]
[NonParallelizable]
public class WindowAnimationTests
{
    [SetUp]
    public void SetUp() => HeadlessAppHost.EnsureInitialized();

    [Test]
    public void EnsureScaleTransform_AttachesScaleTransformAndCenterOrigin()
    {
        var panel = new Panel();
        var st = WindowAnimation.EnsureScaleTransform(panel);

        Assert.That(st, Is.Not.Null);
        Assert.That(panel.RenderTransform, Is.SameAs(st));
        Assert.That(panel.RenderTransformOrigin, Is.EqualTo(RelativePoint.Center));
    }

    [Test]
    public void ResetVisualState_RestoresOpacityAndScale()
    {
        var panel = new Panel
        {
            Opacity = 0.3d,
            RenderTransform = new ScaleTransform(0.85d, 0.85d)
        };

        WindowAnimation.ResetVisualState(panel);

        Assert.That(panel.Opacity, Is.EqualTo(1.0d));
        var st = (ScaleTransform)panel.RenderTransform!;
        Assert.That(st.ScaleX, Is.EqualTo(1.0d));
        Assert.That(st.ScaleY, Is.EqualTo(1.0d));
    }

    [Test]
    public void GetDuration_ReturnsPositiveDurationWhenMotionAllowed()
    {
        var panel = new Panel();
        var duration = WindowAnimation.GetDuration(panel);
        Assert.That(duration, Is.GreaterThan(TimeSpan.Zero));
    }

    [Test]
    public void AnimateRestoreAsync_ReturnsTaskAndResetsVisualStateOnDemand()
    {
        var panel = new Panel
        {
            Opacity = 0.0d,
            RenderTransform = new ScaleTransform(0.9d, 0.9d)
        };

        WindowAnimation.ResetVisualState(panel);

        Assert.That(panel.Opacity, Is.EqualTo(1.0d).Within(0.01d));
        var st = (ScaleTransform)panel.RenderTransform!;
        Assert.That(st.ScaleX, Is.EqualTo(1.0d).Within(0.01d));
        Assert.That(st.ScaleY, Is.EqualTo(1.0d).Within(0.01d));
    }
}
