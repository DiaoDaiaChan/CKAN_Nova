using System;
using System.Linq;
using System.Threading;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.Threading;

using CkanUI.App.Animations;
using CkanUI.App.Tests.Automation;
using CkanUI.App.Theming;

using NUnit.Framework;

namespace CkanUI.App.Tests;

/// <summary>
/// 视图层接管：空态 ↔ 数据态切换是否走弹簧、反向是否连续、带悬停位移的控件是否被放行。
/// </summary>
[TestFixture]
[NonParallelizable]
public sealed class ViewLayerMotionTests
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
    public void HiddenLayerStartsShrunkAndFadesInOnActivation()
    {
        var layer = CreateLayer();
        var window = HeadlessAppHost.CreateTestWindow(layer);

        try
        {
            Assert.That(ViewLayerMotion.Attach(window), Is.EqualTo(1));
            Assert.That(ScaleX(layer), Is.EqualTo(ViewLayerMotion.ClosedScale).Within(1e-6));
            Assert.That(TranslateY(layer), Is.EqualTo(ViewLayerMotion.ClosedOffsetPixels).Within(1e-6));
            Assert.That(layer.Opacity, Is.EqualTo(0).Within(1e-6));

            layer.Classes.Add(ViewLayerMotion.ActiveClass);
            Tick(2);
            Assert.That(ScaleX(layer), Is.GreaterThan(ViewLayerMotion.ClosedScale + 0.001), "激活要真的长大");
            Assert.That(layer.Opacity, Is.GreaterThan(0.05));

            Tick(45);
            Assert.That(ScaleX(layer), Is.EqualTo(1).Within(0.005));
            Assert.That(TranslateY(layer), Is.EqualTo(0).Within(0.05));
            Assert.That(layer.Opacity, Is.EqualTo(1).Within(0.01));
        }
        finally
        {
            window.Close();
            HeadlessAppHost.PumpJobs();
        }
    }

    [Test]
    public void SwitchingBackMidFlightKeepsTheCurrentPosition()
    {
        var layer = CreateLayer();
        var window = HeadlessAppHost.CreateTestWindow(layer);

        try
        {
            ViewLayerMotion.Attach(window);
            layer.Classes.Add(ViewLayerMotion.ActiveClass);
            Tick(3);
            var before = ScaleX(layer);

            layer.Classes.Remove(ViewLayerMotion.ActiveClass);
            Tick(1);

            Assert.That(ScaleX(layer), Is.GreaterThan(before - 0.02), "反向不能直接跳回 0.98（瞬移指纹）");
            Tick(45);
            Assert.That(ScaleX(layer), Is.EqualTo(ViewLayerMotion.ClosedScale).Within(0.002));
            Assert.That(layer.Opacity, Is.EqualTo(0).Within(0.01));
        }
        finally
        {
            window.Close();
            HeadlessAppHost.PumpJobs();
        }
    }

    [Test]
    public void InteractiveCardsKeepTheirOwnHoverTransform()
    {
        var card = CreateLayer();
        card.Classes.Add("CkanCardInteractive");
        var window = HeadlessAppHost.CreateTestWindow(card);

        try
        {
            var before = card.RenderTransform;

            Assert.That(ViewLayerMotion.Attach(window), Is.EqualTo(0), "带悬停位移的卡片不接管");
            Assert.That(card.RenderTransform, Is.SameAs(before));
        }
        finally
        {
            window.Close();
            HeadlessAppHost.PumpJobs();
        }
    }

    [Test]
    public void CollapsedMotionSnapsToTheEndState()
    {
        SetTokens(page: 0, fast: 0);

        var layer = CreateLayer();
        layer.Classes.Add(ViewLayerMotion.ActiveClass);
        var window = HeadlessAppHost.CreateTestWindow(layer);

        try
        {
            ViewLayerMotion.Attach(window);
            Assert.That(ScaleX(layer), Is.EqualTo(1).Within(1e-6), "关闭档没有中间帧");
            Assert.That(layer.Opacity, Is.EqualTo(1).Within(1e-6));
        }
        finally
        {
            window.Close();
            HeadlessAppHost.PumpJobs();
        }
    }

    [Test]
    public void AttachIsIdempotentAcrossRepeatedPageReveals()
    {
        var layer = CreateLayer();
        var window = HeadlessAppHost.CreateTestWindow(layer);

        try
        {
            Assert.That(ViewLayerMotion.Attach(window), Is.EqualTo(1));
            Assert.That(ViewLayerMotion.Attach(window), Is.EqualTo(0));
        }
        finally
        {
            window.Close();
            HeadlessAppHost.PumpJobs();
        }
    }

    private static Border CreateLayer() => new()
    {
        Classes = { ViewLayerMotion.ViewLayerClass },
        Height = 60,
        Background = Brushes.Transparent,
    };

    private static void SetTokens(int page, int fast)
    {
        var resources = Application.Current!.Resources;
        resources[MotionHost.PageKey] = TimeSpan.FromMilliseconds(page);
        resources[MotionHost.FastKey] = TimeSpan.FromMilliseconds(fast);
    }

    private static void Tick(int frames)
    {
        for (var i = 0; i < frames; i++)
        {
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            Dispatcher.UIThread.RunJobs();
            Thread.Sleep(16);
        }
    }

    private static double TranslateY(Control control) =>
        control.RenderTransform is TransformGroup group
        && group.Children.OfType<TranslateTransform>().FirstOrDefault() is { } translate
            ? translate.Y
            : 0d;

    private static double ScaleX(Control control) =>
        control.RenderTransform is TransformGroup group
        && group.Children.OfType<ScaleTransform>().FirstOrDefault() is { } scale
            ? scale.ScaleX
            : 1d;
}
