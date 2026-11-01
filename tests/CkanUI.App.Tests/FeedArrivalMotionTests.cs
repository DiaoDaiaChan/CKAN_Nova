using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.Media.Transformation;
using Avalonia.Threading;

using CkanUI.App.Animations;
using CkanUI.App.Tests.Automation;
using CkanUI.App.Theming;

using NUnit.Framework;

namespace CkanUI.App.Tests;

/// <summary>数据到达入场：列表在换页之后才拿到数据时，行按批错峰浮上来。</summary>
[TestFixture]
[NonParallelizable]
public sealed class FeedArrivalMotionTests
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
    public void RowsArrivingAfterAttachFloatInWithAStagger()
    {
        var (window, list, items) = CreateFeed();
        try
        {
            Assert.That(FeedArrivalMotion.Attach(window), Is.EqualTo(1));

            items.Add("a");
            items.Add("b");
            items.Add("c");
            Layout(window);
            Tick(4);

            var first = Container(list, 0);
            var last = Container(list, 2);
            Assert.That(TranslateY(first), Is.GreaterThan(0.5), "第一批行必须还在下移位置");
            Assert.That(TranslateY(last), Is.GreaterThan(TranslateY(first) + 1d), "最后一行还在自己的排期里，落在第一行后面");

            Tick(60);
            Assert.That(TranslateY(first), Is.EqualTo(0).Within(0.05));
            Assert.That(TranslateY(last), Is.EqualTo(0).Within(0.05));
            Assert.That(first.Opacity, Is.EqualTo(1).Within(0.01));
        }
        finally
        {
            window.Close();
            HeadlessAppHost.PumpJobs();
        }
    }

    [Test]
    public void RowsAddedLaterDoNotReplayTheOnesAlreadyShown()
    {
        var (window, list, items) = CreateFeed();
        try
        {
            FeedArrivalMotion.Attach(window);
            items.Add("a");
            Layout(window);
            Tick(60);

            items.Add("b");
            Layout(window);
            Tick(4);

            Assert.That(TranslateY(Container(list, 0)), Is.EqualTo(0).Within(0.05), "老行不许重演");
            Assert.That(TranslateY(Container(list, 1)), Is.GreaterThan(0.5), "新行要入场");
        }
        finally
        {
            window.Close();
            HeadlessAppHost.PumpJobs();
        }
    }

    [Test]
    public void CollapsedMotionAttachesNothing()
    {
        SetTokens(0, 0);

        var (window, list, items) = CreateFeed();
        try
        {
            Assert.That(FeedArrivalMotion.Attach(window), Is.EqualTo(0));

            items.Add("a");
            Layout(window);
            Tick(4);

            Assert.That(HasSpring(Container(list, 0)), Is.False);
        }
        finally
        {
            window.Close();
            HeadlessAppHost.PumpJobs();
        }
    }

    private static (Window Window, ListBox List, ObservableCollection<string> Items) CreateFeed()
    {
        var items = new ObservableCollection<string>();
        var list = new ListBox { Width = 260, Height = 300, ItemsSource = items };
        var window = HeadlessAppHost.CreateTestWindow(list, 320, 360);
        return (window, list, items);
    }

    private static Control Container(ItemsControl list, int index) =>
        list.ContainerFromIndex(index) as Control ?? throw new InvalidOperationException($"container {index} not realized");

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

    private static bool HasSpring(Control control) =>
        control.RenderTransform is TransformGroup group
        && group.Children.OfType<TranslateTransform>().Any();

    private static double TranslateY(Control control) =>
        control.RenderTransform is TransformGroup group
        && group.Children.OfType<TranslateTransform>().FirstOrDefault() is { } translate
            ? translate.Y
            : 0d;
}