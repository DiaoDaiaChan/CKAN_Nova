using System;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;

using CkanUI.App.Animations;
using CkanUI.App.Tests.Automation;
using CkanUI.App.Theming;

using NUnit.Framework;

namespace CkanUI.App.Tests;

/// <summary>键盘/焦点把目标带进视野时的平滑滚动。</summary>
[TestFixture]
[NonParallelizable]
public sealed class SmoothScrollMotionTests
{
    private const double RowHeight = 40d;

    [SetUp]
    public void SetUp()
    {
        HeadlessAppHost.EnsureInitialized();
        SetTokens(page: 200d, fast: 90d);
    }

    [TearDown]
    public void TearDown() => SetTokens(page: 200d, fast: 90d);

    [Test]
    public void FocusingAnItemBelowTheFoldSlidesInsteadOfJumping()
    {
        var (window, scroll) = BuildHost();
        try
        {
            var target = Item(scroll, 10); // 顶边 400，视口 200

            Assert.That(scroll.Offset.Y, Is.EqualTo(0).Within(1e-6), "起始在顶部");
            Assert.That(SmoothScrollMotion.EnsureVisible(target), Is.True, "遥控目标应当触发一段滚动");

            Tick(8);
            var midway = scroll.Offset.Y;
            Assert.That(midway, Is.GreaterThan(0.5), "已经开始滑");
            Assert.That(midway, Is.LessThan(ExpectedOffset(scroll, target) - 1), "还没有瞬跳到终点");

            Tick(150);
            Assert.That(scroll.Offset.Y, Is.EqualTo(ExpectedOffset(scroll, target)).Within(1),
                "最终要停在目标位置");
        }
        finally
        {
            Close(window);
        }
    }

    [Test]
    public void AlreadyVisibleItemDoesNotScroll()
    {
        var (window, scroll) = BuildHost();
        try
        {
            Assert.That(SmoothScrollMotion.EnsureVisible(Item(scroll, 0)), Is.False, "首行本来就在视野里");
            Tick(4);
            Assert.That(scroll.Offset.Y, Is.EqualTo(0).Within(1e-6));
        }
        finally
        {
            Close(window);
        }
    }

    [Test]
    public void RetargetKeepsTheCurrentOffsetAndSpeed()
    {
        var (window, scroll) = BuildHost();
        try
        {
            SmoothScrollMotion.EnsureVisible(Item(scroll, 14));
            Tick(10);
            var before = scroll.Offset.Y;
            Assert.That(before, Is.GreaterThan(5), "先滑出一段");

            var closer = Item(scroll, 3);
            var targetAfter = ExpectedOffset(scroll, closer);
            Assert.That(SmoothScrollMotion.EnsureVisible(closer), Is.True);
            Tick(1);
            Assert.That(Math.Abs(scroll.Offset.Y - before), Is.LessThan(Math.Abs(before - targetAfter) * 0.5),
                "换目标不许瞬移：一帧只走完新距离的一半以内");

            Tick(20);
            Assert.That(scroll.Offset.Y, Is.LessThan(before - 1), "随后朝新目标回滑");
        }
        finally
        {
            Close(window);
        }
    }

    [Test]
    public void CollapsedMotionJumpsStraightThere()
    {
        SetTokens(page: 0d, fast: 0d);

        var (window, scroll) = BuildHost();
        try
        {
            var target = Item(scroll, 10);
            var expected = ExpectedOffset(scroll, target);

            Assert.That(SmoothScrollMotion.EnsureVisible(target), Is.False, "关闭档不排帧");
            Assert.That(scroll.Offset.Y, Is.EqualTo(expected).Within(0.5));
        }
        finally
        {
            Close(window);
        }
    }

    private static (Window Window, ScrollViewer Scroll) BuildHost()
    {
        var panel = new StackPanel();
        for (int i = 0; i < 20; i++)
        {
            panel.Children.Add(new Border { Height = RowHeight, Classes = { "row" }, Tag = i });
        }

        var scroll = new ScrollViewer
        {
            Height = 200d,
            Width = 280d,
            Content = panel,
        };

        var window = HeadlessAppHost.CreateTestWindow(scroll, 320d, 260d);
        window.Show();
        HeadlessAppHost.PumpJobs();
        return (window, scroll);
    }

    private static Control Item(ScrollViewer scroll, int index) =>
        ((StackPanel)scroll.Content!).Children[index];

    private static double ExpectedOffset(ScrollViewer scroll, Control target)
    {
        var top = target.TranslatePoint(new Point(0d, 0d), scroll)?.Y ?? 0d;
        var bottom = top + target.Bounds.Height;
        var viewport = scroll.Viewport.Height;
        var margin = Math.Min(SmoothScrollMotion.EdgeMarginPixels, viewport / 4d);
        var desired = scroll.Offset.Y;

        if (top < margin)
        {
            desired += top - margin;
        }
        else if (bottom > viewport - margin)
        {
            desired += bottom - (viewport - margin);
        }

        var max = Math.Max(0d, scroll.Extent.Height - viewport);
        return Math.Clamp(desired, 0d, max);
    }

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
