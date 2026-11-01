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

/// <summary>页内页签：下划线弹簧冲量 + 内容舞台方向滑入。</summary>
[TestFixture]
[NonParallelizable]
public sealed class TabStripMotionTests
{
    [SetUp]
    public void SetUp()
    {
        HeadlessAppHost.EnsureInitialized();
        SetTokens(page: 200d, fast: 90d);
    }

    [TearDown]
    public void TearDown() => SetTokens(page: 200d, fast: 90d);

    [Test]
    public void SelectingATabPopsItsUnderline()
    {
        var (window, tabs) = BuildTabs();
        try
        {
            Assert.That(TabStripMotion.Attach(window), Is.EqualTo(1), "窗口里应接管到一个 TabControl");
            tabs.SelectedIndex = 1;

            Tick(6);
            var underline = Underline(tabs, 1);
            Assert.That(underline, Is.Not.Null, "模板必须实现出下划线");
            Assert.That(underline!.Opacity, Is.GreaterThan(0d), "下划线要在淡入途中");

            Tick(60);
            Assert.That(underline.Opacity, Is.EqualTo(1d).Within(0.01d));
            Assert.That(ScaleX(underline), Is.EqualTo(1d).Within(0.02d));
        }
        finally
        {
            Close(window);
        }
    }
    [Test]
    public void PreviouslySelectedUnderlineRetracts()
    {
        var (window, tabs) = BuildTabs();
        try
        {
            TabStripMotion.Attach(window);
            tabs.SelectedIndex = 0;
            Tick(60);

            tabs.SelectedIndex = 1;
            Tick(60);

            var first = Underline(tabs, 0);
            Assert.That(first, Is.Not.Null);
            Assert.That(first!.Opacity, Is.LessThan(0.05d), "旧页签的下划线要收回");
        }
        finally
        {
            Close(window);
        }
    }

    [Test]
    public void ContentStageSlidesInFromTheDirectionOfTravel()
    {
        var (window, tabs) = BuildTabs();
        try
        {
            TabStripMotion.Attach(window);
            tabs.SelectedIndex = 0;
            Tick(60);

            var host = ContentHost(tabs);
            Assert.That(host, Is.Not.Null, "模板必须实现出内容舞台");

            tabs.SelectedIndex = 1;
            Tick(4);
            Assert.That(TranslateX(host!), Is.GreaterThan(0.5d), "向后翻页时内容从右侧滑进来");

            Tick(60);
            Assert.That(TranslateX(host!), Is.EqualTo(0d).Within(0.01d));
        }
        finally
        {
            Close(window);
        }
    }

    [Test]
    public void BackwardSelectionMirrorsTheSlide()
    {
        var (window, tabs) = BuildTabs();
        try
        {
            TabStripMotion.Attach(window);
            tabs.SelectedIndex = 1;
            Tick(60);

            var host = ContentHost(tabs);
            tabs.SelectedIndex = 0;
            Tick(4);

            Assert.That(TranslateX(host!), Is.LessThan(-0.5d), "回退时内容从左侧滑进来");
        }
        finally
        {
            Close(window);
        }
    }

    [Test]
    public void CollapsedMotionLeavesTheStripAlone()
    {
        SetTokens(page: 0d, fast: 0d);

        var (window, tabs) = BuildTabs();
        try
        {
            TabStripMotion.Attach(window);
            var host = ContentHost(tabs);
            var before = host!.RenderTransform;

            tabs.SelectedIndex = 1;
            Tick(4);

            Assert.That(host.RenderTransform, Is.SameAs(before), "关闭档不接管内容舞台");
        }
        finally
        {
            Close(window);
        }
    }

    private static (Window Window, TabControl Tabs) BuildTabs()
    {
        var tabs = new TabControl
        {
            Width = 320d,
            Height = 180d,
            Items =
            {
                new TabItem { Header = "Alpha", Content = new Border { Background = Brushes.Red } },
                new TabItem { Header = "Beta", Content = new Border { Background = Brushes.Blue } },
            },
        };

        var window = HeadlessAppHost.CreateTestWindow(tabs, 360d, 240d);
        window.Show();
        HeadlessAppHost.PumpJobs();
        return (window, tabs);
    }

    private static Border? Underline(TabControl tabs, int index) =>
        tabs.Items.OfType<TabItem>().ElementAt(index)
            .GetVisualDescendants()
            .OfType<Border>()
            .FirstOrDefault(border => border.Name == TabStripMotion.UnderlineName);

    private static ContentPresenter? ContentHost(TabControl tabs) =>
        tabs.GetVisualDescendants()
            .OfType<ContentPresenter>()
            .FirstOrDefault(presenter => presenter.Name == TabStripMotion.ContentHostName);

    private static double ScaleX(Control control) =>
        control.RenderTransform is TransformGroup group &&
        group.Children.OfType<ScaleTransform>().FirstOrDefault() is { } scale
            ? scale.ScaleX
            : 1d;

    private static double TranslateX(Control control) =>
        control.RenderTransform is TransformGroup group &&
        group.Children.OfType<TranslateTransform>().FirstOrDefault() is { } translate
            ? translate.X
            : 0d;

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

