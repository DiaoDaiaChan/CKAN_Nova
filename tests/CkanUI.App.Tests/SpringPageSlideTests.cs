using System;
using System.Threading;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;

using CkanUI.App.Animations;
using CkanUI.App.Tests.Automation;
using CkanUI.App.Theming;

using NUnit.Framework;

namespace CkanUI.App.Tests;

/// <summary>整页幕布过渡：接缝扫过、不碰不透明度、可打断、关闭档不参与。</summary>
[TestFixture]
[NonParallelizable]
public sealed class SpringPageSlideTests
{
    [SetUp]
    public void SetUp()
    {
        HeadlessAppHost.EnsureInitialized();
        SetTokens(page: 200d, fast: 90d);
    }

    [TearDown]
    public void TearDown() => SetTokens(page: 200d, fast: 90d);

    [Test]
    public void CurtainMovesBothPagesTheOtherWayWithoutTouchingOpacity()
    {
        var (window, from, to) = BuildPair();
        try
        {
            _ = new SpringPageSlide().Start(from, to, forward: true, CancellationToken.None);

            Tick(1);
            Assert.That(TranslateX(to), Is.LessThan(-0.5), "新页从左侧外面进场");
            Assert.That(from.Opacity, Is.EqualTo(1).Within(1e-9), "旧页不淡出：淡出就会叠出双影");
            Assert.That(to.Opacity, Is.EqualTo(1).Within(1e-9), "新页也不淡入");

            Tick(8);
            Assert.That(TranslateX(from), Is.GreaterThan(0.5), "旧页向右让位");
            Assert.That(TranslateX(to), Is.GreaterThan(-SpringPageSlide.MaxTravelPixels), "新页在往回收");

            Tick(120);
            Assert.That(TranslateX(from), Is.EqualTo(0).Within(0.01), "收敛后旧页必须回原位，否则复用时会带着偏移");
            Assert.That(TranslateX(to), Is.EqualTo(0).Within(0.01));
        }
        finally
        {
            Close(window);
        }
    }

    [Test]
    public void BackwardMirrorsTheSweep()
    {
        var (window, from, to) = BuildPair();
        try
        {
            _ = new SpringPageSlide().Start(from, to, forward: false, CancellationToken.None);

            Tick(8);
            Assert.That(TranslateX(from), Is.LessThan(-0.5), "后退时旧页向左让位");
            Assert.That(TranslateX(to), Is.GreaterThan(0.5), "新页从右侧进场");
        }
        finally
        {
            Close(window);
        }
    }

    [Test]
    public void CancellingRestoresBothPagesToIdentity()
    {
        var (window, from, to) = BuildPair();
        try
        {
            using var cts = new CancellationTokenSource();
            _ = new SpringPageSlide().Start(from, to, forward: true, cts.Token);

            Tick(4);
            cts.Cancel();
            Tick(4);

            Assert.That(TranslateX(from), Is.EqualTo(0).Within(0.01), "被打断后旧页要归位");
            Assert.That(TranslateX(to), Is.EqualTo(0).Within(0.01), "被打断后新页要归位");
        }
        finally
        {
            Close(window);
        }
    }

    [Test]
    public void CollapsedMotionLeavesBothPagesAlone()
    {
        SetTokens(page: 0d, fast: 0d);

        var (window, from, to) = BuildPair();
        var before = to.RenderTransform;
        try
        {
            _ = new SpringPageSlide().Start(from, to, forward: true, CancellationToken.None);
            Tick(6);

            Assert.That(TranslateX(from), Is.EqualTo(0).Within(1e-9));
            Assert.That(TranslateX(to), Is.EqualTo(0).Within(1e-9));
            Assert.That(to.RenderTransform, Is.SameAs(before), "关闭档不接管变换");
        }
        finally
        {
            Close(window);
        }
    }

    [Test]
    public void HostingControlDrivesTheCurtainOnContentChange()
    {
        var host = new TransitioningContentControl
        {
            Width = 320d,
            Height = 200d,
            PageTransition = new SpringPageSlide(),
            Content = new Border { Background = Brushes.Red },
        };

        var window = HeadlessAppHost.CreateTestWindow(host, 360d, 240d);
        try
        {
            window.Show();
            HeadlessAppHost.PumpJobs();

            host.Content = new Border { Background = Brushes.Blue };
            Tick(3);

            var presenters = host.GetVisualDescendants().OfType<ContentPresenter>().ToList();
            Assert.That(presenters, Is.Not.Empty, "模板必须实现出内容呈现器");
            var traveling = presenters.Count(p => Math.Abs(TranslateX(p)) > 0.5d);
            Assert.That(traveling, Is.GreaterThanOrEqualTo(1), "换内容时两页必须真的在横移（幕布由宿主驱动）");

            Tick(120);
            foreach (var presenter in presenters)
            {
                Assert.That(Math.Abs(TranslateX(presenter)), Is.LessThan(0.01d), "收敛后呈现器必须回原位");
            }
        }
        finally
        {
            Close(window);
        }
    }

    private static (Window Window, Border From, Border To) BuildPair()
    {
        var from = new Border { Width = 320d, Height = 200d, Background = Brushes.Red };
        var to = new Border { Width = 320d, Height = 200d, Background = Brushes.Blue };
        var panel = new Panel { Children = { from, to } };

        var window = HeadlessAppHost.CreateTestWindow(panel, 360d, 240d);
        window.Show();
        HeadlessAppHost.PumpJobs();
        return (window, from, to);
    }

    private static double TranslateX(Control control) =>
        control.RenderTransform is TransformGroup group &&
        group.Children.OfType<TranslateTransform>().FirstOrDefault() is { } translate
            ? translate.X
            : 0d;

    private static void Close(Window window)
    {
        window.Close();
        HeadlessAppHost.PumpJobs();
    }

    private static void Tick(int frames)
    {
        for (int i = 0; i < frames; i++)
        {
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            Dispatcher.UIThread.RunJobs();
            Thread.Sleep(16);
        }
    }

    private static void SetTokens(double page, double fast)
    {
        var resources = Application.Current!.Resources;
        resources[MotionHost.PageKey] = TimeSpan.FromMilliseconds(page);
        resources[MotionHost.FastKey] = TimeSpan.FromMilliseconds(fast);
    }
}
