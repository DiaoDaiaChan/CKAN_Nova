using System;
using System.Linq;
using System.Threading;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;

using CkanUI.App.Animations;
using CkanUI.App.Tests.Automation;
using CkanUI.App.Theming;

using NUnit.Framework;

namespace CkanUI.App.Tests;

/// <summary>
/// 入场编排与按压反馈：三档强度是否按 token 推导、错峰是否真的错开、按压是否按"边"收缩。
/// </summary>
/// <remarks>
/// 帧推进走 <see cref="Tick"/>：让合成器真实出一帧再排空调度队列，等价于 60Hz 真机。
/// 只断言可观察的观测量（位置、不透明度、类名），不碰内部字段。
/// </remarks>
[TestFixture]
[NonParallelizable]
public sealed class MotionChoreographyTests
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
    public void ModeIsDerivedFromTheDurationTokens()
    {
        Assert.That(MotionBudget.Mode, Is.EqualTo(MotionMode.Full));

        SetTokens(page: 0, fast: 90);
        Assert.That(MotionBudget.Mode, Is.EqualTo(MotionMode.Reduced));

        SetTokens(page: 0, fast: 0);
        Assert.That(MotionBudget.Mode, Is.EqualTo(MotionMode.None));
        Assert.That(MotionBudget.IsCollapsed, Is.True);
    }

    [Test]
    public void FullMotion_QueuesDirectChildren()
    {
        var panel = new StackPanel();
        var items = CreateBorders(panel, 3);
        var window = HeadlessAppHost.CreateTestWindow(panel);

        try
        {
            var count = EntranceChoreographer.Play(panel);

            Assert.That(count, Is.EqualTo(3));
            Assert.That(TranslateY(items[2]), Is.EqualTo(EntranceChoreographer.OffsetPixels).Within(1e-6));
            Assert.That(items[2].Opacity, Is.EqualTo(0).Within(1e-6));

            Tick(2);
            Assert.That(items[0].Opacity, Is.GreaterThan(0.05), "第一个元素要已经出发");
            Assert.That(items[2].Opacity, Is.EqualTo(0).Within(1e-6), "第三个元素还在排队");

            Tick(45);
            Assert.That(items.All(item => Math.Abs(item.Opacity - 1) < 0.01), Is.True);
            Assert.That(items.All(item => Math.Abs(TranslateY(item)) < 0.05), Is.True);
        }
        finally
        {
            window.Close();
            HeadlessAppHost.PumpJobs();
        }
    }

    [Test]
    public void ReducedMotion_MovesTogetherAndKeepsASmallOffset()
    {
        SetTokens(page: 0, fast: 90);
        var panel = new StackPanel();
        var items = CreateBorders(panel, 3);
        var window = HeadlessAppHost.CreateTestWindow(panel);

        try
        {
            EntranceChoreographer.Play(panel);

            Assert.That(TranslateY(items[2]), Is.EqualTo(EntranceChoreographer.ReducedOffsetPixels).Within(1e-6));
            Tick(2);
            Assert.That(items[2].Opacity, Is.GreaterThan(0.05), "精简档不排队，三个一起动");
        }
        finally
        {
            window.Close();
            HeadlessAppHost.PumpJobs();
        }
    }

    [Test]
    public void CollapsedMotion_LeavesEverythingAlone()
    {
        SetTokens(page: 0, fast: 0);
        var panel = new StackPanel();
        var items = CreateBorders(panel, 3);
        var window = HeadlessAppHost.CreateTestWindow(panel);

        try
        {
            var before = items.Select(item => item.RenderTransform).ToArray();
            var count = EntranceChoreographer.Play(panel);

            Assert.That(count, Is.EqualTo(0));
            Assert.That(items.All(item => Math.Abs(item.Opacity - 1) < 1e-9), Is.True);
            Assert.That(items.Select(item => item.RenderTransform).SequenceEqual(before), Is.True);
        }
        finally
        {
            window.Close();
            HeadlessAppHost.PumpJobs();
        }
    }

    [Test]
    public void PressScale_ShrinksLargeSurfacesByPixelsNotByRatio()
    {
        Assert.That(InteractionMotion.ScaleFor(100, 40), Is.EqualTo(InteractionMotion.PressedScale).Within(1e-9));
        Assert.That(InteractionMotion.ScaleFor(1000, 240), Is.EqualTo(0.988).Within(1e-9));
    }

    [Test]
    public void PressScale_FollowsTheBudget()
    {
        SetTokens(page: 0, fast: 90);
        var reduced = InteractionMotion.ScaleFor(100, 40);

        SetTokens(page: 0, fast: 0);
        var none = InteractionMotion.ScaleFor(100, 40);

        Assert.That(reduced, Is.GreaterThan(InteractionMotion.PressedScale), "精简档只收一部分");
        Assert.That(reduced, Is.LessThan(1));
        Assert.That(none, Is.EqualTo(1).Within(1e-9), "关掉动效时不能有缩放");
    }

    [Test]
    public void Shake_MarksTheErrorStateAndMovesTheControl()
    {
        var border = new Border { Width = 120, Height = 32 };
        var window = HeadlessAppHost.CreateTestWindow(border);

        try
        {
            _ = MotionFeedback.ShakeAsync(border);
            Assert.That(border.Classes.Contains(MotionFeedback.InvalidClass), Is.True, "抖动同时要留错误态");

            Tick(2);
            Assert.That(Math.Abs(TranslateX(border)), Is.GreaterThan(0.01), "弹簧冲量要真的把控件推出去");

            MotionFeedback.ClearInvalid(border);
            Assert.That(border.Classes.Contains(MotionFeedback.InvalidClass), Is.False);
        }
        finally
        {
            window.Close();
            HeadlessAppHost.PumpJobs();
        }
    }

    [Test]
    public void NavPulse_KicksTheSelectedItemSideways()
    {
        var border = new Border { Width = 200, Height = 40 };
        var window = HeadlessAppHost.CreateTestWindow(border);

        try
        {
            MotionFeedback.PulseNav(border);
            Tick(2);

            Assert.That(Math.Abs(TranslateX(border)), Is.GreaterThan(0.01), "追赶位移要真的发生");
            Assert.That(Math.Abs(TranslateX(border)), Is.LessThan(MotionFeedback.NavPulseAmplitude + 1));
        }
        finally
        {
            window.Close();
            HeadlessAppHost.PumpJobs();
        }
    }

    [Test]
    public void TwoChildRootContinuesIntoTheRicherBranch()
    {
        var header = new Border { Height = 28, Background = Brushes.Transparent };
        var content = new StackPanel();
        var rows = CreateBorders(content, 3);
        var shell = new StackPanel();
        shell.Children.Add(header);
        shell.Children.Add(content);
        var root = new Border { Child = shell };
        var window = HeadlessAppHost.CreateTestWindow(root);

        try
        {
            var count = EntranceChoreographer.Play(root);

            Assert.That(count, Is.EqualTo(5), "两子级根要继续下钻，不能只排头部和内容壳");
            Assert.That(TranslateY(rows[2]), Is.EqualTo(EntranceChoreographer.OffsetPixels).Within(1e-6));
            Assert.That(header.Opacity, Is.EqualTo(0).Within(1e-6));

            Tick(60);
            Assert.That(rows.All(row => Math.Abs(TranslateY(row)) < 0.05), Is.True);
            Assert.That(rows.All(row => Math.Abs(row.Opacity - 1) < 0.01), Is.True);
        }
        finally
        {
            window.Close();
            HeadlessAppHost.PumpJobs();
        }
    }

    private static Border[] CreateBorders(Panel panel, int count)
    {
        var items = new Border[count];
        for (var i = 0; i < count; i++)
        {
            items[i] = new Border { Height = 24, Background = Brushes.Transparent };
            panel.Children.Add(items[i]);
        }

        return items;
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

    private static double TranslateX(Control control) =>
        control.RenderTransform is TransformGroup group
        && group.Children.OfType<TranslateTransform>().FirstOrDefault() is { } translate
            ? translate.X
            : 0d;

    private static double TranslateY(Control control) =>
        control.RenderTransform is TransformGroup group
        && group.Children.OfType<TranslateTransform>().FirstOrDefault() is { } translate
            ? translate.Y
            : 0d;
}