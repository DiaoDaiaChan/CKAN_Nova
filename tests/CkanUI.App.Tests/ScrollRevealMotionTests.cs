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

/// <summary>滚动入场：跨进底边的行自己浮上来、首屏不重演、每行只演一次。</summary>
[TestFixture]
[NonParallelizable]
public sealed class ScrollRevealMotionTests
{
    private const double RowHeight = 40d;

    [SetUp]
    public void SetUp()
    {
        HeadlessAppHost.EnsureInitialized();
        SetTokens(200, 90);
    }

    [TearDown]
    public void TearDown() => SetTokens(200, 90);

    [Test]
    public void RowsCrossingTheBottomEdgeFloatInByThemselves()
    {
        var (window, scroll, rows) = CreateFeed(rowCount: 40);
        try
        {
            Assert.That(ScrollRevealMotion.Attach(window), Is.EqualTo(1));

            scroll.Offset = new Vector(0, 200);
            Tick(4);

            var entering = rows[5];
            Assert.That(TranslateY(entering), Is.GreaterThan(0.5), "刚跨进底边的行必须还在下移位置");
            Assert.That(entering.Opacity, Is.LessThan(1), "同时要淡进来");

            Tick(45);
            Assert.That(TranslateY(entering), Is.EqualTo(0).Within(0.05));
            Assert.That(entering.Opacity, Is.EqualTo(1).Within(0.01));
        }
        finally
        {
            window.Close();
            HeadlessAppHost.PumpJobs();
        }
    }

    [Test]
    public void FirstScreenItemsAreLeftToThePageEntrance()
    {
        var (window, _, rows) = CreateFeed(rowCount: 40);
        try
        {
            ScrollRevealMotion.Attach(window);

            Assert.That(HasSpring(rows[0]), Is.False, "首屏元素由页面入场负责，滚动层不许重复演");
            Assert.That(HasSpring(rows[3]), Is.False);
        }
        finally
        {
            window.Close();
            HeadlessAppHost.PumpJobs();
        }
    }

    [Test]
    public void EachRowOnlyRevealsOnce()
    {
        var (window, scroll, rows) = CreateFeed(rowCount: 40);
        try
        {
            ScrollRevealMotion.Attach(window);

            scroll.Offset = new Vector(0, 200);
            Tick(45);
            Assert.That(TranslateY(rows[5]), Is.EqualTo(0).Within(0.05));

            scroll.Offset = new Vector(0, 0);
            Tick(6);
            scroll.Offset = new Vector(0, 200);
            Tick(4);

            Assert.That(TranslateY(rows[5]), Is.EqualTo(0).Within(0.05), "回滚再下来不许重演");
        }
        finally
        {
            window.Close();
            HeadlessAppHost.PumpJobs();
        }
    }

    [Test]
    public void RowsStillBelowTheViewportStayUntouched()
    {
        var (window, scroll, rows) = CreateFeed(rowCount: 40);
        try
        {
            ScrollRevealMotion.Attach(window);

            scroll.Offset = new Vector(0, 200);
            Tick(4);

            Assert.That(HasSpring(rows[30]), Is.False, "还在下面的行不参与");
        }
        finally
        {
            window.Close();
            HeadlessAppHost.PumpJobs();
        }
    }

    [Test]
    public void CollapsedMotionAttachesNothing()
    {
        SetTokens(0, 0);

        var (window, scroll, rows) = CreateFeed(rowCount: 40);
        try
        {
            Assert.That(ScrollRevealMotion.Attach(window), Is.EqualTo(0));

            scroll.Offset = new Vector(0, 200);
            Tick(4);

            Assert.That(HasSpring(rows[5]), Is.False);
        }
        finally
        {
            window.Close();
            HeadlessAppHost.PumpJobs();
        }
    }

    private static (Window Window, ScrollViewer Scroll, Border[] Rows) CreateFeed(int rowCount)
    {
        var stack = new StackPanel { Width = 260 };
        var rows = new Border[rowCount];
        for (var i = 0; i < rowCount; i++)
        {
            rows[i] = new Border { Height = RowHeight, Background = Brushes.Transparent };
            stack.Children.Add(rows[i]);
        }

        var scroll = new ScrollViewer { Height = 200, Width = 280, Content = stack };
        var window = HeadlessAppHost.CreateTestWindow(scroll, 320, 240);
        return (window, scroll, rows);
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

    private static bool HasSpring(Control control) =>
        control.RenderTransform is TransformGroup { Children.Count: 2 } group
        && group.Children[0] is ScaleTransform
        && group.Children[1] is TranslateTransform;

    private static double TranslateY(Control control) =>
        control.RenderTransform is TransformGroup group
        && group.Children.OfType<TranslateTransform>().FirstOrDefault() is { } translate
            ? translate.Y
            : 0d;
}