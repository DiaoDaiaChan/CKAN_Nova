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
/// 浮层接管：抽屉/浮条是否真的走弹簧、反向是否连续、关闭档是否只落位不动。
/// </summary>
[TestFixture]
[NonParallelizable]
public sealed class SurfaceMotionTests
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
    public void DrawerStartsBelowAndOpensThroughSpring()
    {
        var drawer = CreateSurface(SurfaceMotion.DrawerClass);
        var window = HeadlessAppHost.CreateTestWindow(drawer);

        try
        {
            Assert.That(SurfaceMotion.Attach(window), Is.EqualTo(1));
            Assert.That(TranslateY(drawer), Is.EqualTo(SurfaceMotion.DrawerClosedOffset).Within(1e-6));
            Assert.That(drawer.Opacity, Is.EqualTo(0).Within(1e-6));

            drawer.Classes.Add(SurfaceMotion.OpenClass);
            Tick(2);
            Assert.That(TranslateY(drawer), Is.LessThan(SurfaceMotion.DrawerClosedOffset - 1), "弹簧要真的把抽屉抬起来");
            Assert.That(drawer.Opacity, Is.GreaterThan(0.05), "同时要淡入");

            Tick(60);
            Assert.That(TranslateY(drawer), Is.EqualTo(0).Within(0.05));
            Assert.That(drawer.Opacity, Is.EqualTo(1).Within(0.01));
        }
        finally
        {
            window.Close();
            HeadlessAppHost.PumpJobs();
        }
    }

    [Test]
    public void ReversalKeepsTheCurrentPositionAndSpeed()
    {
        var drawer = CreateSurface(SurfaceMotion.DrawerClass);
        var window = HeadlessAppHost.CreateTestWindow(drawer);

        try
        {
            SurfaceMotion.Attach(window);
            drawer.Classes.Add(SurfaceMotion.OpenClass);
            Tick(4);

            var before = TranslateY(drawer);
            Assert.That(before, Is.LessThan(SurfaceMotion.DrawerClosedOffset - 1));

            drawer.Classes.Remove(SurfaceMotion.OpenClass);
            Tick(1);

            Assert.That(TranslateY(drawer), Is.LessThan(before + 40d), "收起不能从 260 重新出发（瞬移指纹）");
            Tick(60);
            Assert.That(TranslateY(drawer), Is.EqualTo(SurfaceMotion.DrawerClosedOffset).Within(0.05));
            Assert.That(drawer.Opacity, Is.EqualTo(0).Within(0.01));
        }
        finally
        {
            window.Close();
            HeadlessAppHost.PumpJobs();
        }
    }

    [Test]
    public void PillScalesUpWhileFloatingIn()
    {
        var pill = CreateSurface(SurfaceMotion.PillClass);
        var window = HeadlessAppHost.CreateTestWindow(pill);

        try
        {
            SurfaceMotion.Attach(window);
            Assert.That(ScaleX(pill), Is.EqualTo(SurfaceMotion.PillClosedScale).Within(1e-6));

            pill.Classes.Add(SurfaceMotion.OpenClass);
            Tick(2);
            Assert.That(ScaleX(pill), Is.GreaterThan(SurfaceMotion.PillClosedScale + 0.001), "浮条要跟着长大");

            Tick(60);
            Assert.That(ScaleX(pill), Is.EqualTo(1).Within(0.005));
            Assert.That(TranslateY(pill), Is.EqualTo(0).Within(0.05));
        }
        finally
        {
            window.Close();
            HeadlessAppHost.PumpJobs();
        }
    }

    [Test]
    public void CollapsedMotionSnapsWithoutFrames()
    {
        SetTokens(page: 0, fast: 0);

        var drawer = CreateSurface(SurfaceMotion.DrawerClass);
        var window = HeadlessAppHost.CreateTestWindow(drawer);

        try
        {
            SurfaceMotion.Attach(window);
            drawer.Classes.Add(SurfaceMotion.OpenClass);

            Assert.That(TranslateY(drawer), Is.EqualTo(0).Within(1e-6), "关闭档直接落位，不排帧");
            Assert.That(drawer.Opacity, Is.EqualTo(1).Within(1e-6));
        }
        finally
        {
            window.Close();
            HeadlessAppHost.PumpJobs();
        }
    }

    [Test]
    public void AttachIsIdempotent()
    {
        var drawer = CreateSurface(SurfaceMotion.DrawerClass);
        var pill = CreateSurface(SurfaceMotion.PillClass);
        var panel = new StackPanel();
        panel.Children.Add(drawer);
        panel.Children.Add(pill);
        var window = HeadlessAppHost.CreateTestWindow(panel);

        try
        {
            Assert.That(SurfaceMotion.Attach(window), Is.EqualTo(2));
            Assert.That(SurfaceMotion.Attach(window), Is.EqualTo(0), "重复挂载不能再加一份订阅");
        }
        finally
        {
            window.Close();
            HeadlessAppHost.PumpJobs();
        }
    }

    private static Border CreateSurface(string className)
    {
        var border = new Border
        {
            Classes = { className },
            Height = 80,
            Background = Brushes.Transparent,
        };
        return border;
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

    private static double ScaleX(Control control) =>
        control.RenderTransform is TransformGroup group
        && group.Children.OfType<ScaleTransform>().FirstOrDefault() is { } scale
            ? scale.ScaleX
            : 1d;
}
