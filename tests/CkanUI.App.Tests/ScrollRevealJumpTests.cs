using System;
using System.Collections.Generic;
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
/// 拖滚动条不是"从底边露头"：视口整片换行时逐行从 Opacity=0 淡入，会让整屏在拖动期间一直半透明，
/// 用户看到的就是「内容丢失」。这里锁住两条口径：整片跳转不许把行刷透明，小步滚动仍要逐行浮上来。
/// </summary>
[TestFixture]
[NonParallelizable]
public sealed class ScrollRevealJumpTests
{
    private const double RowHeight = 40d;

    [SetUp]
    public void SetUp()
    {
        HeadlessAppHost.EnsureInitialized();
        var resources = Application.Current!.Resources;
        resources[MotionHost.PageKey] = TimeSpan.FromMilliseconds(200);
        resources[MotionHost.FastKey] = TimeSpan.FromMilliseconds(90);
    }

    [Test]
    public void DraggingTheScrollbar_NeverBlanksTheRows()
    {
        var list = new ListBox
        {
            Width = 420,
            Height = 600,
            ItemsSource = Enumerable.Range(0, 4000).Select(index => "行 " + index).ToList(),
        };

        var window = HeadlessAppHost.CreateTestWindow(list, 520, 700);
        try
        {
            Assert.That(ScrollRevealMotion.Attach(window), Is.GreaterThan(0), "长列表必须被滚动入场接管");

            var scroll = list.GetVisualDescendants().OfType<ScrollViewer>().First();
            Tick(2);

            // 真实拖动：每一帧都是大跨度位移，虚拟化面板整屏换一批容器。
            for (var step = 1; step <= 12; step++)
            {
                scroll.Offset = new Vector(0, step * 2400d);
                Tick(1);

                var rows = list.GetRealizedContainers().OfType<Control>().ToList();
                Assert.That(rows, Is.Not.Empty, $"第 {step} 步跳转后一行都没实现，拖不动了");

                var faded = rows
                    .Where(row => row.Opacity < 0.99d)
                    .Select(row => row.Opacity)
                    .ToList();
                Assert.That(
                    faded,
                    Is.Empty,
                    $"第 {step} 步跳转把 {faded.Count} 行刷成了半透明（最小 {faded.DefaultIfEmpty(1d).Min():0.###}）—— 拖动期间就是一片空白");
            }
        }
        finally
        {
            window.Close();
            HeadlessAppHost.PumpJobs();
        }
    }

    [Test]
    public void AFarJumpInAStaticFeedRevealsWithoutFadingIn()
    {
        var stack = new StackPanel { Width = 260 };
        var rows = new Border[200];
        for (var index = 0; index < rows.Length; index++)
        {
            rows[index] = new Border { Height = RowHeight, Background = Brushes.Transparent };
            stack.Children.Add(rows[index]);
        }

        var scroll = new ScrollViewer { Height = 200, Width = 280, Content = stack };
        var window = HeadlessAppHost.CreateTestWindow(scroll, 320, 240);

        try
        {
            ScrollRevealMotion.Attach(window);

            // 一次跳过 3000px（远超一个视口）：新出现的行是「换了一批」，不该淡入。
            scroll.Offset = new Vector(0, 3000d);
            Tick(1);

            var firstVisible = rows[76];
            Assert.That(firstVisible.Opacity, Is.EqualTo(1d).Within(0.001d), "整片跳转不许把行刷透明");
            Assert.That(TranslateY(firstVisible), Is.EqualTo(0d).Within(0.001d), "也不许从下方滑进来");
        }
        finally
        {
            window.Close();
            HeadlessAppHost.PumpJobs();
        }
    }

    [Test]
    public void AWheelSizedScrollStillFloatsTheRowIn()
    {
        var stack = new StackPanel { Width = 260 };
        var rows = new Border[200];
        for (var index = 0; index < rows.Length; index++)
        {
            rows[index] = new Border { Height = RowHeight, Background = Brushes.Transparent };
            stack.Children.Add(rows[index]);
        }

        var scroll = new ScrollViewer { Height = 200, Width = 280, Content = stack };
        var window = HeadlessAppHost.CreateTestWindow(scroll, 320, 240);

        try
        {
            ScrollRevealMotion.Attach(window);

            // 滚轮一小步（不到一屏）：这是真的「从底边露头」，要保留浮入。
            scroll.Offset = new Vector(0, 40d);
            Tick(4);

            var entering = rows[5];
            Assert.That(TranslateY(entering), Is.GreaterThan(0.5), "小步滚动仍然要浮上来");
            Assert.That(entering.Opacity, Is.LessThan(1d), "同时淡入");
        }
        finally
        {
            window.Close();
            HeadlessAppHost.PumpJobs();
        }
    }

    private static void Tick(int frames)
    {
        for (var index = 0; index < frames; index++)
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
