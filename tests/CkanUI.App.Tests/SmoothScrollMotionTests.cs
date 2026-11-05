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

    [Test]
    public void ScrollByDeltaStartsFastAndBrakesSmoothlyToRest()
    {
        var (window, scroll) = BuildHost();
        try
        {
            Assert.That(scroll.Offset.Y, Is.EqualTo(0).Within(1e-6), "初始在顶部");

            // 触发 200px 的向下滚动
            var started = SmoothScrollMotion.ScrollByDelta(scroll, 200d);
            Assert.That(started, Is.True, "长列表滚动应当启动平滑动画");

            // 经过 2 帧：由于注入了启动冲量（快速启动），速度应该迅速提上来，位移显著大于线性平均
            Tick(2);
            var initialOffset = scroll.Offset.Y;
            Assert.That(initialOffset, Is.GreaterThan(15d), "快速响应：启动必须迅猛");
            Assert.That(initialOffset, Is.LessThan(200d), "仍在平滑滑动途中");

            // 再跑 10 帧：进入减速刹车区间
            Tick(10);
            var midOffset = scroll.Offset.Y;
            Assert.That(midOffset, Is.GreaterThan(initialOffset), "持续前进");
            Assert.That(midOffset, Is.LessThan(200d), "尚未过冲/未完全刹停");

            // 充分迭代后，弹簧临界阻尼平稳刹车停靠在目标点
            Tick(120);
            Assert.That(scroll.Offset.Y, Is.EqualTo(200d).Within(1d), "平滑刹车落位在精确目标 200px");
        }
        finally
        {
            Close(window);
        }
    }

    [Test]
    public void ScrollByDeltaInCollapsedMotionSnapsImmediately()
    {
        SetTokens(page: 0d, fast: 0d);
        var (window, scroll) = BuildHost();
        try
        {
            var started = SmoothScrollMotion.ScrollByDelta(scroll, 150d);
            Assert.That(started, Is.False, "关闭档不启动帧泵动画");
            Assert.That(scroll.Offset.Y, Is.EqualTo(150d).Within(0.5), "立即跳到目标位置");
        }
        finally
        {
            Close(window);
        }
    }

    [Test]
    public void FindScrollViewerResolvesForAncestorSelfAndDescendants()
    {
        var (window, scroll) = BuildHost();
        try
        {
            // 1. 从列表项（表项）向上查找
            var childRow = Item(scroll, 5);
            var foundFromRow = SmoothScrollMotion.FindScrollViewer(childRow);
            Assert.That(foundFromRow, Is.SameAs(scroll), "表项元素必须能解析到其承载的 ScrollViewer");

            // 2. 从 ScrollViewer 自身查找
            var foundFromSelf = SmoothScrollMotion.FindScrollViewer(scroll);
            Assert.That(foundFromSelf, Is.SameAs(scroll), "自身必须直接识别");

            // 3. 从包含 ScrollViewer 的外层控件向下查找（如 DataGrid、ListBox 或表头外层容器）
            var foundFromWindow = SmoothScrollMotion.FindScrollViewer(window);
            Assert.That(foundFromWindow, Is.SameAs(scroll), "从上层容器向下必须能穿透解析到子 ScrollViewer");
        }
        finally
        {
            Close(window);
        }
    }

    [Test]
    public void AttachWindowProvidesTunnelWheelInterceptionAcrossAllItems()
    {
        var (window, scroll) = BuildHost();
        try
        {
            var attached = SmoothScrollMotion.Attach(window);
            Assert.That(attached, Is.GreaterThanOrEqualTo(0));

            // 验证光标在任何表项上触发滚轮时，能通过 Tunnel 阶段捕获并启动快速弹簧平滑滚动
            var row = Item(scroll, 2);
            var targetSv = SmoothScrollMotion.FindScrollViewer(row, 100d);
            Assert.That(targetSv, Is.Not.Null);

            var started = SmoothScrollMotion.ScrollByDelta(targetSv!, 100d);
            Assert.That(started, Is.True, "表项滚动必须启动平滑弹簧阻尼");

            Tick(2);
            Assert.That(scroll.Offset.Y, Is.GreaterThan(10d), "两帧内迅速启动");

            Tick(100);
            Assert.That(scroll.Offset.Y, Is.EqualTo(100d).Within(1d), "平稳弹簧刹车落位在 100px");
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
