using System;
using System.Collections.Generic;
using System.Linq;
using System.Collections.ObjectModel;
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

/// <summary>滚动入场：挂载晚于内容成形也要接上（页面模板/数据/滚动容器都可能后到）。</summary>
[TestFixture]
[NonParallelizable]
public sealed class ScrollRevealAttachTests
{
    [SetUp]
    public void SetUp()
    {
        HeadlessAppHost.EnsureInitialized();
        SetTokens(200, 90);
    }

    [TearDown]
    public void TearDown() => SetTokens(200, 90);

    [Test]
    public void ScrollViewerAddedAfterAttachStillGetsTheReveal()
    {
        var host = new StackPanel();
        var window = HeadlessAppHost.CreateTestWindow(host, 320, 240);
        try
        {
            // 换页那一刻页面还是空的：Attach 扫不到任何滚动容器。
            Assert.That(ScrollRevealMotion.Attach(window), Is.EqualTo(0));

            var rows = AddFeed(host, rowCount: 40);
            Layout(window);
            Tick(2);

            var scroll = (ScrollViewer)host.Children[0];
            scroll.Offset = new Vector(0, 200);
            Tick(4);

            Assert.That(TranslateY(rows[5]), Is.GreaterThan(0.5), "后到的滚动容器也必须接上滚动入场");
        }
        finally
        {
            window.Close();
            HeadlessAppHost.PumpJobs();
        }
    }

    [Test]
    public void AttachingBeforeLayoutDoesNotMarkTheFirstScreenAsSeen()
    {
        // 先建树、不跑布局就挂载：视口高度还是 0，此时不许把首屏行登记成"看过"。
        var rows = new Border[40];
        var stack = new StackPanel { Width = 260 };
        for (var i = 0; i < rows.Length; i++)
        {
            rows[i] = new Border { Height = 40d, Background = Brushes.Transparent };
            stack.Children.Add(rows[i]);
        }

        var scroll = new ScrollViewer { Height = 200, Width = 280, Content = stack };
        var window = new Window { Width = 320, Height = 240, Content = scroll };
        window.Show();
        try
        {
            ScrollRevealMotion.Attach(window);
            window.Measure(new Size(320, 240));
            window.Arrange(new Rect(0, 0, 320, 240));
            HeadlessAppHost.PumpJobs();

            scroll.Offset = new Vector(0, 200);
            Tick(4);

            Assert.That(TranslateY(rows[5]), Is.GreaterThan(0.5), "首屏登记必须等布局完成，不能把后面的行也标掉");
        }
        finally
        {
            window.Close();
            HeadlessAppHost.PumpJobs();
        }
    }

    private static Border[] AddFeed(Panel host, int rowCount)
    {
        var stack = new StackPanel { Width = 260 };
        var rows = new Border[rowCount];
        for (var i = 0; i < rowCount; i++)
        {
            rows[i] = new Border { Height = 40d, Background = Brushes.Transparent };
            stack.Children.Add(rows[i]);
        }

        host.Children.Add(new ScrollViewer { Height = 200, Width = 280, Content = stack });
        return rows;
    }

    private static void Layout(Window window)
    {
        window.Measure(new Size(window.Width, window.Height));
        window.Arrange(new Rect(0, 0, window.Width, window.Height));
        HeadlessAppHost.PumpJobs();
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