using System.Threading;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;

using CkanUI.App.Animations;
using CkanUI.App.Tests.Automation;
using CkanUI.App.Theming;

using NUnit.Framework;

namespace CkanUI.App.Tests;

/// <summary>缩略图解码完成后淡入：不再一帧硬切，同一张源不重播。</summary>
[TestFixture]
[NonParallelizable]
public sealed class ImageLoadMotionTests
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
    public void SourceArrivingAfterAttachFadesIn()
    {
        var image = new Image { Width = 64d, Height = 64d };
        var window = HeadlessAppHost.CreateTestWindow(image);
        try
        {
            Assert.That(ImageLoadMotion.Attach(window), Is.EqualTo(1));

            image.Source = Bitmap();
            Tick(2);
            Assert.That(image.Opacity, Is.LessThan(1d), "源到位后要淡入，不能一帧落下");
            Assert.That(TransformScale(image), Is.LessThan(1d), "轻微顶一下");

            Tick(60);
            Assert.That(image.Opacity, Is.EqualTo(1d).Within(0.01d));
            Assert.That(TransformScale(image), Is.EqualTo(1d).Within(0.01d));
        }
        finally
        {
            Close(window);
        }
    }

    [Test]
    public void AlreadyLoadedImagePlaysOnAttach()
    {
        var image = new Image { Width = 64d, Height = 64d, Source = Bitmap() };
        var window = HeadlessAppHost.CreateTestWindow(image);
        try
        {
            ImageLoadMotion.Attach(window);
            Tick(2);

            Assert.That(image.Opacity, Is.LessThan(1d), "挂载时已经有图，也要入场一次");
        }
        finally
        {
            Close(window);
        }
    }

    [Test]
    public void SameSourceTwiceDoesNotReplay()
    {
        var image = new Image { Width = 64d, Height = 64d };
        var window = HeadlessAppHost.CreateTestWindow(image);
        try
        {
            ImageLoadMotion.Attach(window);
            var bitmap = Bitmap();
            image.Source = bitmap;
            Tick(60);
            Assert.That(image.Opacity, Is.EqualTo(1d).Within(0.01d));

            image.Source = bitmap;
            Tick(2);

            Assert.That(image.Opacity, Is.EqualTo(1d).Within(0.01d), "同一张源重复赋值不重播");
        }
        finally
        {
            Close(window);
        }
    }

    [Test]
    public void CollapsedMotionLeavesImagesAlone()
    {
        SetTokens(page: 0d, fast: 0d);

        var image = new Image { Width = 64d, Height = 64d };
        var window = HeadlessAppHost.CreateTestWindow(image);
        var before = image.RenderTransform;
        try
        {
            ImageLoadMotion.Attach(window);
            image.Source = Bitmap();
            Tick(2);

            Assert.That(image.Opacity, Is.EqualTo(1d).Within(1e-9));
            Assert.That(image.RenderTransform, Is.SameAs(before), "关闭档不接管变换");
        }
        finally
        {
            Close(window);
        }
    }

    private static double TransformScale(Control control) =>
        control.RenderTransform is Avalonia.Media.TransformGroup group &&
        group.Children.OfType<ScaleTransform>().FirstOrDefault() is { } scale
            ? scale.ScaleX
            : 1d;

    private static WriteableBitmap Bitmap() => new(
        new PixelSize(2, 2),
        new Vector(96d, 96d),
        PixelFormat.Bgra8888,
        AlphaFormat.Premul);

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
