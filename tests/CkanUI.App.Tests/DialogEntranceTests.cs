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

/// <summary>对话框正文编排：块级与文字只做位移、关闭档不动。</summary>
[TestFixture]
[NonParallelizable]
public sealed class DialogEntranceTests
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
    public void BodyRisesWithoutTouchingOpacity()
    {
        var body = new StackPanel
        {
            Children =
            {
                new TextBlock { Text = "第一行说明" },
                new TextBlock { Text = "第二行说明" },
            },
        };
        var window = HeadlessAppHost.CreateTestWindow(body);

        try
        {
            DialogEntrance.Play(window);
            Tick(1);

            var texts = window.GetVisualDescendants().OfType<TextBlock>().ToList();
            Assert.That(texts, Is.Not.Empty);
            Assert.That(texts[0].RenderTransform, Is.InstanceOf<TransformGroup>(), "正文要先落到位移起点");
            Assert.That(texts[0].Opacity, Is.EqualTo(1).Within(1e-9), "窗口自己已经淡入，正文不再压透明度");
            Assert.That(TranslateY(texts[0]), Is.GreaterThan(2), "起点要有可见的上浮距离");

            Tick(60);
            Assert.That(TranslateY(texts[0]), Is.EqualTo(0).Within(0.2));
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
        SetTokens(0, 0);

        var body = new StackPanel { Children = { new TextBlock { Text = "只有一行" } } };
        var window = HeadlessAppHost.CreateTestWindow(body);

        try
        {
            DialogEntrance.Play(window);

            var texts = window.GetVisualDescendants().OfType<TextBlock>().ToList();
            Assert.That(texts, Is.Not.Empty);
            Assert.That(texts.All(t => t.RenderTransform is null), "关闭档不许动正文");
        }
        finally
        {
            window.Close();
            HeadlessAppHost.PumpJobs();
        }
    }

    [Test]
    public void ViewLayersInsideTheDialogAreHandedToTheirSpring()
    {
        var layer = new Border { Classes = { "view-layer" }, Width = 240, Height = 120 };
        var body = new StackPanel
        {
            Children =
            {
                layer,
                new TextBlock { Text = "说明" },
            },
        };
        var window = HeadlessAppHost.CreateTestWindow(body);

        try
        {
            DialogEntrance.Play(window);

            Assert.That(layer.RenderTransform, Is.InstanceOf<TransformGroup>(), "对话框里的视图层也要接上弹簧");
            Assert.That(TranslateY(layer), Is.EqualTo(ViewLayerMotion.ClosedOffsetPixels).Within(0.01));
            Assert.That(ScaleX(layer), Is.EqualTo(ViewLayerMotion.ClosedScale).Within(0.001));
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

    private static double TranslateY(Control control) =>
        control.RenderTransform is TransformGroup group
        && group.Children.OfType<TranslateTransform>().FirstOrDefault() is { } translate
            ? translate.Y
            : 0d;
}
