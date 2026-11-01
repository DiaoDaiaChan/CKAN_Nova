using System;
using System.Linq;
using System.Threading;

using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.Threading;

using CkanUI.App.Animations;
using CkanUI.App.Tests.Automation;

using NUnit.Framework;

namespace CkanUI.App.Tests;

/// <summary>
/// 帧泵集成：真控件上的打断连续性、速度保留与落位。
/// </summary>
[TestFixture]
[NonParallelizable]
public sealed class MotionFramePumpTests
{
    [SetUp]
    public void SetUp() => HeadlessAppHost.EnsureInitialized();

    [Test]
    public void RetargetMidFlight_IsContinuousAndKeepsVelocity()
    {
        var border = new Border { Width = 100, Height = 10 };
        var window = HeadlessAppHost.CreateTestWindow(border);

        try
        {
            var spring = new TransformSpring(border, SpringSpec.Glide, SpringSpec.Snappy);
            var translate = ((TransformGroup)border.RenderTransform!).Children.OfType<TranslateTransform>().First();

            spring.MoveTo(x: 200, snap: true);
            Assert.That(translate.X, Is.EqualTo(200).Within(1e-9));

            spring.MoveTo(x: 0);
            Tick(10);
            var midway = translate.X;
            Assert.That(midway, Is.GreaterThan(0).And.LessThan(199), "十帧后必须还在半路");

            spring.MoveTo(x: 200);
            Assert.That(translate.X, Is.EqualTo(midway).Within(1e-6), "换目标不能瞬移");

            Tick(6);
            Assert.That(translate.X, Is.GreaterThan(midway), "换目标后要立刻反向，不能停半拍");

            Tick(120);
            Assert.That(translate.X, Is.EqualTo(200).Within(0.5), "最终要落位");
        }
        finally
        {
            window.Close();
            HeadlessAppHost.PumpJobs();
        }
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
