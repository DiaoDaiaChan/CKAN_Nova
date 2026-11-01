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
/// 文字逐行入场：节拍是否快、三档强度是否各按口径收放、换页重入是否从起点重来。
/// </summary>
[TestFixture]
[NonParallelizable]
public sealed class TextRevealChoreographerTests
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
    public void FullMotion_QueuesLinesQuickly()
    {
        var panel = new StackPanel();
        var lines = CreateLines(panel, 3);
        var window = HeadlessAppHost.CreateTestWindow(panel);

        try
        {
            var count = TextRevealChoreographer.Play(panel);

            Assert.That(count, Is.EqualTo(3));
            Assert.That(TranslateY(lines[2]), Is.EqualTo(TextRevealChoreographer.OffsetPixels).Within(1e-6));
            Assert.That(lines[2].Opacity, Is.EqualTo(0).Within(1e-6));

            Tick(1);
            // 单帧（16 ms）弹簧进度约 0.046，阈值留到 0.02：只要"确实离开了起点"即可，不把曲线钉成契约。
            Assert.That(lines[0].Opacity, Is.GreaterThan(0.02), "第一行要立刻出发");
            Assert.That(lines[2].Opacity, Is.EqualTo(0).Within(1e-6), "末行还在排队（一帧还不到 2×12 ms 的节拍）");

            Tick(45);
            Assert.That(lines.All(line => Math.Abs(line.Opacity - 1) < 0.01), Is.True);
            Assert.That(lines.All(line => Math.Abs(TranslateY(line)) < 0.05), Is.True);
        }
        finally
        {
            window.Close();
            HeadlessAppHost.PumpJobs();
        }
    }

    [Test]
    public void StepIsFasterThanBlockChoreography()
    {
        Assert.That(
            TextRevealChoreographer.StepMilliseconds,
            Is.LessThan(EntranceChoreographer.StepMilliseconds),
            "文字是密集对象，节拍必须比块级编排快，否则整页看起来像卡顿");
    }

    [Test]
    public void ReducedMotion_RevealsTogetherWithASmallOffset()
    {
        SetTokens(page: 0, fast: 90);
        var panel = new StackPanel();
        var lines = CreateLines(panel, 3);
        var window = HeadlessAppHost.CreateTestWindow(panel);

        try
        {
            TextRevealChoreographer.Play(panel);

            Assert.That(TranslateY(lines[2]), Is.EqualTo(TextRevealChoreographer.ReducedOffsetPixels).Within(1e-6));
            Tick(2);
            Assert.That(lines[2].Opacity, Is.GreaterThan(0.05), "精简档不排队，三行一起淡");
        }
        finally
        {
            window.Close();
            HeadlessAppHost.PumpJobs();
        }
    }

    [Test]
    public void CollapsedMotion_LeavesTextAlone()
    {
        SetTokens(page: 0, fast: 0);
        var panel = new StackPanel();
        var lines = CreateLines(panel, 3);
        var window = HeadlessAppHost.CreateTestWindow(panel);

        try
        {
            var before = lines.Select(line => line.RenderTransform).ToArray();
            var count = TextRevealChoreographer.Play(panel);

            Assert.That(count, Is.EqualTo(0));
            Assert.That(lines.All(line => Math.Abs(line.Opacity - 1) < 1e-9), Is.True);
            Assert.That(lines.Select(line => line.RenderTransform).SequenceEqual(before), Is.True);
        }
        finally
        {
            window.Close();
            HeadlessAppHost.PumpJobs();
        }
    }

    [Test]
    public void SkippedTextDoesNotDisturbTheQueue()
    {
        var panel = new StackPanel();
        panel.Children.Add(new TextBlock { Text = "   " });
        panel.Children.Add(new TextBlock { Text = "隐藏的行", IsVisible = false });
        var visible = new TextBlock { Text = "真正要上屏的行" };
        panel.Children.Add(visible);
        var border = new Border { Height = 20, Background = Brushes.Transparent };
        panel.Children.Add(border);
        var window = HeadlessAppHost.CreateTestWindow(panel);

        try
        {
            var borderTransform = border.RenderTransform;
            var count = TextRevealChoreographer.Play(panel);

            Assert.That(count, Is.EqualTo(1));
            Assert.That(visible.Opacity, Is.EqualTo(0).Within(1e-6));
            Assert.That(border.RenderTransform, Is.SameAs(borderTransform), "非文字控件不参与逐行编排");
        }
        finally
        {
            window.Close();
            HeadlessAppHost.PumpJobs();
        }
    }

    [Test]
    public void ReplayingRestartsFromTheTop()
    {
        var panel = new StackPanel();
        var lines = CreateLines(panel, 3);
        var window = HeadlessAppHost.CreateTestWindow(panel);

        try
        {
            TextRevealChoreographer.Play(panel);
            Tick(20);
            Assert.That(lines[0].Opacity, Is.GreaterThan(0.5), "先让第一行亮起来");

            TextRevealChoreographer.Play(panel);

            Assert.That(lines[0].Opacity, Is.EqualTo(0).Within(1e-6), "换回本页要重新从 0 淡入");
            Assert.That(TranslateY(lines[2]), Is.EqualTo(TextRevealChoreographer.OffsetPixels).Within(1e-6));
        }
        finally
        {
            window.Close();
            HeadlessAppHost.PumpJobs();
        }
    }

    [Test]
    public void SwitchingTabsReplaysTheTextOfTheNewContent()
    {
        var first = new TextBlock { Text = "第一个页签的文字" };
        var second = new TextBlock { Text = "第二个页签的文字" };
        var block = new Border { Height = 40, Background = Brushes.Transparent };
        var secondPanel = new StackPanel { Children = { block, second } };
        var tabs = new TabControl
        {
            Items =
            {
                new TabItem { Header = "一", Content = first },
                new TabItem { Header = "二", Content = secondPanel },
            },
        };
        var window = HeadlessAppHost.CreateTestWindow(tabs, 420, 240);

        try
        {
            TextRevealChoreographer.Play(window);
            Tick(2);

            tabs.SelectedIndex = 1;
            Tick(4);

            Assert.That(TranslateY(second), Is.GreaterThan(0.5), "页内页签切换后，新内容文字也要逐行上屏");
            Assert.That(TranslateY(block), Is.GreaterThan(0.5), "同一页签里的块级元素也要重新入场");
        }
        finally
        {
            window.Close();
            HeadlessAppHost.PumpJobs();
        }
    }

    private static TextBlock[] CreateLines(Panel panel, int count)
    {
        var lines = new TextBlock[count];
        for (var i = 0; i < count; i++)
        {
            lines[i] = new TextBlock { Text = "第 " + i + " 行文字" };
            panel.Children.Add(lines[i]);
        }

        return lines;
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