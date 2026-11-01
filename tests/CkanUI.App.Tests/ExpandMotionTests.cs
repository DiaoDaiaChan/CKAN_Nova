using System;
using System.Linq;
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

/// <summary>
/// 展开入场：`Expander` 与 `TreeViewItem` 展开时内容一帧硬切出现，改由弹簧补一段上浮淡入。
/// </summary>
[TestFixture]
[NonParallelizable]
public sealed class ExpandMotionTests
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
    public void ExpandingFadesTheBodyIn()
    {
        var expander = new Expander
        {
            Header = "详情",
            Content = new Border { Width = 200, Height = 80, Background = Brushes.Transparent },
        };
        var panel = new StackPanel();
        panel.Children.Add(expander);
        var window = HeadlessAppHost.CreateTestWindow(panel);

        try
        {
            Assert.That(ExpandMotion.Attach(panel), Is.GreaterThanOrEqualTo(1));

            expander.IsExpanded = true;
            Tick(2);

            var host = BodyOf(expander);
            Assert.That(host, Is.Not.Null, "展开后正文宿主应已实现");
            Assert.That(host!.Opacity, Is.LessThan(0.95), "正文不能一帧就到满不透明度");
            Assert.That(TranslateY(host), Is.GreaterThan(0.2), "正文要从下方浮上来");

            Tick(45);
            Assert.That(host.Opacity, Is.EqualTo(1).Within(0.02));
            Assert.That(TranslateY(host), Is.EqualTo(0).Within(0.1));
        }
        finally
        {
            window.Close();
            HeadlessAppHost.PumpJobs();
        }
    }

    [Test]
    public void PlayingOnAHostSlidesItIn()
    {
        var host = new Border { Width = 200, Height = 80 };
        var window = HeadlessAppHost.CreateTestWindow(host);

        try
        {
            ExpandMotion.Play(host);

            Assert.That(host.Opacity, Is.EqualTo(0).Within(1e-6));
            Assert.That(TranslateY(host), Is.EqualTo(ExpandMotion.OffsetPixels).Within(1e-6));

            Tick(45);
            Assert.That(host.Opacity, Is.EqualTo(1).Within(0.02));
            Assert.That(TranslateY(host), Is.EqualTo(0).Within(0.1));
        }
        finally
        {
            window.Close();
            HeadlessAppHost.PumpJobs();
        }
    }

    [Test]
    public void ReducedMotionUsesASmallerLift()
    {
        SetTokens(page: 0, fast: 90);
        var host = new Border { Width = 200, Height = 80 };
        var window = HeadlessAppHost.CreateTestWindow(host);

        try
        {
            ExpandMotion.Play(host);

            Assert.That(TranslateY(host), Is.EqualTo(ExpandMotion.ReducedOffsetPixels).Within(1e-6));
        }
        finally
        {
            window.Close();
            HeadlessAppHost.PumpJobs();
        }
    }

    [Test]
    public void CollapsedMotionLeavesTheBodyAlone()
    {
        SetTokens(page: 0, fast: 0);
        var host = new Border { Width = 200, Height = 80 };
        var window = HeadlessAppHost.CreateTestWindow(host);

        try
        {
            var before = host.RenderTransform;
            ExpandMotion.Play(host);
            Tick(2);

            Assert.That(host.Opacity, Is.EqualTo(1).Within(1e-9));
            Assert.That(host.RenderTransform, Is.SameAs(before));
        }
        finally
        {
            window.Close();
            HeadlessAppHost.PumpJobs();
        }
    }

    [Test]
    public void AttachingTwiceDoesNotDoubleHook()
    {
        var expander = new Expander { Header = "详情", Content = new TextBlock { Text = "正文" } };
        var panel = new StackPanel();
        panel.Children.Add(expander);
        var window = HeadlessAppHost.CreateTestWindow(panel);

        try
        {
            var first = ExpandMotion.Attach(panel);
            var second = ExpandMotion.Attach(panel);

            Assert.That(first, Is.GreaterThanOrEqualTo(1));
            Assert.That(second, Is.EqualTo(0), "重复挂载不应再接管同一个展开器");
        }
        finally
        {
            window.Close();
            HeadlessAppHost.PumpJobs();
        }
    }

    private static Control? BodyOf(Expander expander) =>
        expander.GetVisualDescendants().OfType<ContentPresenter>().LastOrDefault();

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
