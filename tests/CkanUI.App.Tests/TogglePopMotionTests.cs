using System;
using System.Linq;
using System.Threading;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.Threading;

using CkanUI.App.Animations;
using CkanUI.App.Tests.Automation;
using CkanUI.App.Theming;

using NUnit.Framework;

namespace CkanUI.App.Tests;

/// <summary>开关选中反馈：勾上要真的弹一下，且能被强度档位收住。</summary>
[TestFixture]
[NonParallelizable]
public sealed class TogglePopMotionTests
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
    public void CheckingAPillPopsThenSettles()
    {
        var toggle = new ToggleButton { Width = 90, Height = 30 };
        var window = HeadlessAppHost.CreateTestWindow(toggle);

        try
        {
            Assert.That(TogglePopMotion.Attach(toggle), Is.EqualTo(1));

            toggle.IsChecked = true;
            Tick(5);
            Assert.That(ScaleX(toggle), Is.GreaterThan(1.005), "勾上要看到放大");

            Tick(60);
            Assert.That(ScaleX(toggle), Is.EqualTo(1).Within(0.002));
        }
        finally
        {
            window.Close();
            HeadlessAppHost.PumpJobs();
        }
    }

    [Test]
    public void AlreadyCheckedAtAttachDoesNotPop()
    {
        var toggle = new ToggleButton { Width = 90, Height = 30, IsChecked = true };
        var window = HeadlessAppHost.CreateTestWindow(toggle);

        try
        {
            var before = toggle.RenderTransform;
            Assert.That(TogglePopMotion.Attach(toggle), Is.EqualTo(1));

            Tick(4);
            Assert.That(toggle.RenderTransform, Is.SameAs(before), "本来就已经选中的不该凭空动");
        }
        finally
        {
            window.Close();
            HeadlessAppHost.PumpJobs();
        }
    }

    [Test]
    public void ReducedMotionPopsLess()
    {
        SetTokens(page: 0, fast: 90);
        var toggle = new ToggleButton { Width = 90, Height = 30 };
        var window = HeadlessAppHost.CreateTestWindow(toggle);

        try
        {
            TogglePopMotion.Attach(toggle);
            toggle.IsChecked = true;
            Tick(5);

            var reduced = ScaleX(toggle);
            Assert.That(reduced, Is.GreaterThan(1));
            Assert.That(reduced, Is.LessThan(1.02), "精简档只留一点弹性");
        }
        finally
        {
            window.Close();
            HeadlessAppHost.PumpJobs();
        }
    }

    [Test]
    public void CollapsedMotionTouchesNothing()
    {
        SetTokens(page: 0, fast: 0);
        var toggle = new ToggleButton { Width = 90, Height = 30 };
        var window = HeadlessAppHost.CreateTestWindow(toggle);

        try
        {
            var before = toggle.RenderTransform;
            Assert.That(TogglePopMotion.Attach(toggle), Is.EqualTo(0));

            toggle.IsChecked = true;
            Tick(4);
            Assert.That(toggle.RenderTransform, Is.SameAs(before));
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
        var panel = new StackPanel();
        panel.Children.Add(new ToggleButton());
        panel.Children.Add(new ToggleButton());
        var window = HeadlessAppHost.CreateTestWindow(panel);

        try
        {
            Assert.That(TogglePopMotion.Attach(panel), Is.EqualTo(2));
            Assert.That(TogglePopMotion.Attach(panel), Is.EqualTo(0));
        }
        finally
        {
            window.Close();
            HeadlessAppHost.PumpJobs();
        }
    }

    private static double ScaleX(Control control) =>
        control.RenderTransform is TransformGroup group
        && group.Children.OfType<ScaleTransform>().FirstOrDefault() is { } scale
            ? scale.ScaleX
            : 1d;

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
}
