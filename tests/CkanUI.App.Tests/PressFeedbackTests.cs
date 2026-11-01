using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using CkanUI.App.Animations;
using CkanUI.App.Tests.Automation;
using NUnit.Framework;

namespace CkanUI.App.Tests;

/// <summary>
/// 按压反馈不能吃掉点击：收缩会改变命中几何，边缘按压若以中心收缩，
/// 松开点会落到控件外，Button 的 Click 就不会触发。
/// </summary>
[TestFixture]
[NonParallelizable]
public sealed class PressFeedbackTests
{
    [SetUp]
    public void SetUp() => HeadlessAppHost.EnsureInitialized();

    [TestCase(1d)]
    [TestCase(2d)]
    [TestCase(3d)]
    [TestCase(60d)]
    public void PressingNearTheEdge_StillRaisesClick(double x)
    {
        var clicks = 0;
        var button = CreateButton();
        button.Click += (_, _) => clicks++;
        var window = Show(button);
        try
        {
            Press(window, new Point(x, 20));
            window.MouseUp(new Point(x, 20), MouseButton.Left);
            Pump(5);

            Assert.That(clicks, Is.EqualTo(1), $"距左边缘 {x}px 按下并松开必须仍然触发 Click");
        }
        finally
        {
            window.Close();
            HeadlessAppHost.PumpJobs();
        }
    }

    [Test]
    public void PressingShrinksTheSurface_SoFeedbackStillPlays()
    {
        var button = CreateButton();
        var window = Show(button);
        try
        {
            Press(window, new Point(6, 6));
            var scale = ScaleOf(button);
            Assert.That(scale, Is.LessThan(0.999), "按下后表面必须真的缩小，否则变成了“为了点击牺牲反馈”");

            var origin = button.RenderTransformOrigin;
            Assert.That(origin.Unit, Is.EqualTo(RelativeUnit.Relative));
            Assert.That(origin.Point.X, Is.LessThan(0.2), "收缩锚点必须落在按下点附近，否则边缘点击仍会被缩出命中范围");
            Assert.That(origin.Point.Y, Is.LessThan(0.4), "收缩锚点必须落在按下点附近");
        }
        finally
        {
            window.Close();
            HeadlessAppHost.PumpJobs();
        }
    }

    [Test]
    public void ReleasingRestoresTheSurface()
    {
        var button = CreateButton();
        var window = Show(button);
        try
        {
            Press(window, new Point(60, 20));
            window.MouseUp(new Point(60, 20), MouseButton.Left);
            Pump(80);

            Assert.That(ScaleOf(button), Is.EqualTo(1d).Within(0.01), "松开后表面必须弹回原尺寸");
        }
        finally
        {
            window.Close();
            HeadlessAppHost.PumpJobs();
        }
    }

    [Test]
    public void PressingCard_DoesNotTriggerInteractionMotionScale()
    {
        var card = new Border
        {
            Width = 300,
            Height = 150,
            Classes = { "CkanCard" },
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
        };
        var window = HeadlessAppHost.CreateTestWindow(card, 400, 300);
        window.AddHandler(InputElement.PointerPressedEvent, (_, e) => InteractionMotion.Press(e, window), RoutingStrategies.Tunnel, handledEventsToo: true);
        window.AddHandler(InputElement.PointerReleasedEvent, (_, _) => InteractionMotion.Release(), RoutingStrategies.Tunnel, handledEventsToo: true);
        Pump(5);
        try
        {
            Press(window, new Point(50, 50));
            Assert.That(ScaleOf(card), Is.EqualTo(1d), "卡片容器点击时不应被 InteractionMotion 整体收缩，避免引起边框位移");
        }
        finally
        {
            window.Close();
            HeadlessAppHost.PumpJobs();
        }
    }

    [Test]
    public void PressingAnInteractiveCardDipsIt()
    {
        var card = new Border
        {
            Width = 300,
            Height = 150,
            Classes = { InteractionMotion.CardClass },
            Background = Brushes.Transparent,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
        };
        var window = HeadlessAppHost.CreateTestWindow(card, 400, 300);
        window.AddHandler(InputElement.PointerPressedEvent, (_, e) => InteractionMotion.Press(e, window), RoutingStrategies.Tunnel, handledEventsToo: true);
        window.AddHandler(InputElement.PointerReleasedEvent, (_, _) => InteractionMotion.Release(), RoutingStrategies.Tunnel, handledEventsToo: true);
        Pump(5);

        try
        {
            Press(window, new Point(150, 75));
            var scale = ScaleOf(card);
            Assert.That(scale, Is.LessThan(0.9999), "可点卡片按下必须真的缩");
            Assert.That(scale, Is.GreaterThan(0.95), "整张卡按 12 px 收会塌，卡片用 6 px");

            window.MouseUp(new Point(150, 75), MouseButton.Left);
            Pump(80);
            Assert.That(ScaleOf(card), Is.EqualTo(1d).Within(0.01), "松手要弹回去");
        }
        finally
        {
            window.Close();
            HeadlessAppHost.PumpJobs();
        }
    }

    private static Button CreateButton() => new()
    {
        Width = 120,
        Height = 40,
        Content = "hit",
        HorizontalAlignment = HorizontalAlignment.Left,
        VerticalAlignment = VerticalAlignment.Top,
    };

    private static Window Show(Button button)
    {
        var window = HeadlessAppHost.CreateTestWindow(button, 400, 300);
        window.AddHandler(InputElement.PointerPressedEvent, (_, e) => InteractionMotion.Press(e, window), RoutingStrategies.Tunnel, handledEventsToo: true);
        window.AddHandler(InputElement.PointerReleasedEvent, (_, _) => InteractionMotion.Release(), RoutingStrategies.Tunnel, handledEventsToo: true);
        Pump(5);
        return window;
    }

    private static void Press(Window window, Point point)
    {
        window.MouseMove(point);
        window.MouseDown(point, MouseButton.Left);
        Pump(6);
    }

    private static double ScaleOf(Control control)
    {
        var group = control.RenderTransform as TransformGroup;
        var scale = group?.Children.OfType<ScaleTransform>().FirstOrDefault();
        return scale?.ScaleX ?? 1d;
    }

    private static void Pump(int frames)
    {
        for (var i = 0; i < frames; i++)
        {
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            HeadlessAppHost.PumpJobs();
            System.Threading.Thread.Sleep(8);
        }
    }
}