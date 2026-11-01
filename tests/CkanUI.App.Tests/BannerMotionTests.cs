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
/// 状态提示条的入场：显隐是 IsVisible 硬切换，样式里的透明度过渡没有中间帧，改由弹簧补上。
/// </summary>
[TestFixture]
[NonParallelizable]
public sealed class BannerMotionTests
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
    public void BannerAppearingSlidesIn()
    {
        var panel = new StackPanel();
        var banner = CreateBanner(visible: false);
        panel.Children.Add(banner);
        var window = HeadlessAppHost.CreateTestWindow(panel);

        try
        {
            Assert.That(BannerMotion.Attach(panel), Is.EqualTo(1));

            banner.IsVisible = true;
            Tick(1);

            Assert.That(banner.Opacity, Is.GreaterThan(0.001), "出现瞬间要开始淡入");
            Assert.That(banner.Opacity, Is.LessThan(0.9), "不能一帧就落位");
            Assert.That(TranslateY(banner), Is.GreaterThan(0), "还要从上浮起点往下走");

            Tick(40);
            Assert.That(banner.Opacity, Is.EqualTo(1).Within(0.01));
            Assert.That(TranslateY(banner), Is.EqualTo(0).Within(0.05));
        }
        finally
        {
            window.Close();
            HeadlessAppHost.PumpJobs();
        }
    }

    [Test]
    public void VisibleBannerAnimateOnAttach()
    {
        var panel = new StackPanel();
        var banner = CreateBanner(visible: true);
        panel.Children.Add(banner);
        var window = HeadlessAppHost.CreateTestWindow(panel);

        try
        {
            BannerMotion.Attach(panel);

            Assert.That(banner.Opacity, Is.EqualTo(0).Within(1e-6), "挂载时已经可见的提示条也要走入场");
            Assert.That(TranslateY(banner), Is.EqualTo(BannerMotion.OffsetPixels).Within(1e-6));

            Tick(40);
            Assert.That(banner.Opacity, Is.EqualTo(1).Within(0.01));
        }
        finally
        {
            window.Close();
            HeadlessAppHost.PumpJobs();
        }
    }

    [Test]
    public void ReducedMotionUsesASmallerLift()
    {
        SetTokens(page: 0, fast: 90);
        var panel = new StackPanel();
        var banner = CreateBanner(visible: true);
        panel.Children.Add(banner);
        var window = HeadlessAppHost.CreateTestWindow(panel);

        try
        {
            BannerMotion.Attach(panel);

            Assert.That(TranslateY(banner), Is.EqualTo(BannerMotion.ReducedOffsetPixels).Within(1e-6));
        }
        finally
        {
            window.Close();
            HeadlessAppHost.PumpJobs();
        }
    }

    [Test]
    public void CollapsedMotionLeavesTheBannerAlone()
    {
        SetTokens(page: 0, fast: 0);
        var panel = new StackPanel();
        var banner = CreateBanner(visible: true);
        panel.Children.Add(banner);
        var window = HeadlessAppHost.CreateTestWindow(panel);

        try
        {
            var before = banner.RenderTransform;
            Assert.That(BannerMotion.Attach(panel), Is.EqualTo(1));
            Tick(2);

            Assert.That(banner.Opacity, Is.EqualTo(1).Within(1e-9));
            Assert.That(banner.RenderTransform, Is.SameAs(before));
        }
        finally
        {
            window.Close();
            HeadlessAppHost.PumpJobs();
        }
    }

    [Test]
    public void AttachingTwiceDoesNotDoubleHook()
    {
        var panel = new StackPanel();
        var banner = CreateBanner(visible: false);
        panel.Children.Add(banner);
        var window = HeadlessAppHost.CreateTestWindow(panel);

        try
        {
            Assert.That(BannerMotion.Attach(panel), Is.EqualTo(1));
            Assert.That(BannerMotion.Attach(panel), Is.EqualTo(1));

            banner.IsVisible = true;
            Tick(1);
            Assert.That(banner.Opacity, Is.GreaterThan(0.001));

            Tick(40);
            Assert.That(banner.Opacity, Is.EqualTo(1).Within(0.01), "重复挂载不能把动画叠加成两点出发");
        }
        finally
        {
            window.Close();
            HeadlessAppHost.PumpJobs();
        }
    }

    private static Border CreateBanner(bool visible)
    {
        var banner = new Border
        {
            IsVisible = visible,
            Height = 36,
            Background = Brushes.Transparent,
        };
        banner.Classes.Add("CkanInfoBanner");
        return banner;
    }

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
}
