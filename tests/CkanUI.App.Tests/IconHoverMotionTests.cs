using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.VisualTree;

using CkanUI.App.Animations;
using CkanUI.App.Tests.Automation;
using CkanUI.ViewModels;

using FluentAvalonia.UI.Controls;
using NUnit.Framework;

namespace CkanUI.App.Tests;

/// <summary>
/// 图标悬浮动效：全局样式是否覆盖所有图标、划过是否抬角放大、长悬浮是否持续循环、
/// 离开是否回位，以及宿主（按钮留白、导航项）是否替内部图标代收指针。
/// </summary>
/// <remarks>
/// 动效是无限循环，没法只断言样式表写了什么；这里用 Avalonia.Headless 的真实指针事件
/// 加真实渲染帧推进，看角度与缩放的实际数值。帧推进走 <see cref="Tick"/>：
/// 每次让合成器出一帧再跑一遍调度队列，等价于真机的 60Hz。
/// </remarks>
[TestFixture]
[NonParallelizable]
public sealed class IconHoverMotionTests
{
    [SetUp]
    public void SetUp() => HeadlessAppHost.EnsureInitialized();

    [Test]
    public void GlobalStyle_EnablesHoverMotionForEveryPathIcon()
    {
        var icon = CreateIcon();
        var window = ShowInCanvas(icon, 20, 20);

        try
        {
            HeadlessAppHost.PumpJobs();

            Assert.That(IconHoverMotion.GetEnabled(icon), Is.True, "全局样式必须给每个图标打开悬浮动效");
            Assert.That(IconHoverMotion.ShapeOf(icon), Is.EqualTo(IconHoverShape.Waggle), "没有特殊要求时用摇摆形态");
        }
        finally
        {
            window.Close();
            HeadlessAppHost.PumpJobs();
        }
    }

    [Test]
    public void Hover_TiltsAndScalesTheIcon()
    {
        var icon = CreateIcon();
        var window = ShowInCanvas(icon, 20, 20);

        try
        {
            HeadlessAppHost.PumpJobs();
            var track = IconHoverTrack.For(icon);

            window.MouseMove(CenterOf(icon, window));
            HeadlessAppHost.PumpJobs();

            Assert.That(track.IsHovered, Is.True);

            Tick(12);

            Assert.That(track.Angle, Is.GreaterThan(1), "划过时图标要抬起一点角度");
            Assert.That(track.Scale, Is.GreaterThan(1.005), "划过时图标要轻轻放大");
        }
        finally
        {
            window.Close();
            HeadlessAppHost.PumpJobs();
        }
    }

    [Test]
    public void SustainedHover_KeepsSpinning()
    {
        var icon = CreateIcon();
        IconHoverMotion.SetShape(icon, IconHoverShape.Spin);
        var window = ShowInCanvas(icon, 20, 20);

        try
        {
            HeadlessAppHost.PumpJobs();
            var track = IconHoverTrack.For(icon);

            window.MouseMove(CenterOf(icon, window));
            HeadlessAppHost.PumpJobs();

            Tick(50);
            var first = track.Angle;
            Assert.That(first, Is.GreaterThan(12), "齿轮类图标悬停一会儿就该转起来");

            Tick(50);
            Assert.That(track.Angle, Is.GreaterThan(first + 10), "长悬浮必须一直转，不能转一下就停");
        }
        finally
        {
            window.Close();
            HeadlessAppHost.PumpJobs();
        }
    }

    [Test]
    public void Waggle_KeepsOscillatingWithoutRunningAway()
    {
        var icon = CreateIcon();
        var window = ShowInCanvas(icon, 20, 20);

        try
        {
            HeadlessAppHost.PumpJobs();
            var track = IconHoverTrack.For(icon);

            window.MouseMove(CenterOf(icon, window));
            HeadlessAppHost.PumpJobs();

            var samples = new List<double>();
            for (var frame = 0; frame < 60; frame++)
            {
                Tick(1);
                samples.Add(track.Angle);
            }

            var min = samples.Min();
            var max = samples.Max();
            var upper = IconHoverTrack.EnterTiltDegrees + IconHoverTrack.WaggleAmplitudeDegrees + 2;
            var lower = IconHoverTrack.EnterTiltDegrees - IconHoverTrack.WaggleAmplitudeDegrees - 2;

            Assert.That(max - min, Is.GreaterThan(0.5), "默认形态要有可见的呼吸感");
            Assert.That(max, Is.LessThan(upper), "摇摆不能越摆越大");
            Assert.That(min, Is.GreaterThan(lower), "摇摆不能跑到别处去");
        }
        finally
        {
            window.Close();
            HeadlessAppHost.PumpJobs();
        }
    }

    [Test]
    public void PointerLeave_ReturnsTheIconHome()
    {
        var icon = CreateIcon();
        IconHoverMotion.SetShape(icon, IconHoverShape.Spin);
        var window = ShowInCanvas(icon, 20, 20);

        try
        {
            HeadlessAppHost.PumpJobs();
            var track = IconHoverTrack.For(icon);

            window.MouseMove(CenterOf(icon, window));
            HeadlessAppHost.PumpJobs();
            Tick(50);

            window.MouseMove(new Point(390, 290));
            HeadlessAppHost.PumpJobs();
            Tick(120);

            Assert.That(track.IsHovered, Is.False);
            Assert.That(Math.Abs(track.Scale - 1), Is.LessThan(0.01), "离开后必须回到原始大小");
            Assert.That(Math.Abs(Math.IEEERemainder(track.Angle, 360)), Is.LessThan(1.5), "离开后旋转必须停在整圈上");
        }
        finally
        {
            window.Close();
            HeadlessAppHost.PumpJobs();
        }
    }

    [Test]
    public void ButtonHost_DrivesItsIconWhenPointerIsOnThePadding()
    {
        var icon = CreateIcon();
        var button = new Button
        {
            Width = 120,
            Height = 40,
            Content = icon,
            HorizontalContentAlignment = Avalonia.Layout.HorizontalAlignment.Center,
            VerticalContentAlignment = Avalonia.Layout.VerticalAlignment.Center,
        };

        var window = ShowInCanvas(button, 40, 40);

        try
        {
            HeadlessAppHost.PumpJobs();
            var track = IconHoverTrack.For(icon);

            // 指针落在按钮左侧留白上：不进图标，但整块按钮都算"划过"。
            window.MouseMove(new Point(50, 60));
            HeadlessAppHost.PumpJobs();

            Assert.That(button.IsPointerOver, Is.True);
            Assert.That(icon.IsPointerOver, Is.False, "这一点必须落在按钮留白上，才测得到宿主代收指针");
            Assert.That(track.IsHovered, Is.True, "划过按钮留白也要驱动它的图标");

            Tick(12);
            Assert.That(track.Angle, Is.GreaterThan(1));
        }
        finally
        {
            window.Close();
            HeadlessAppHost.PumpJobs();
        }
    }

    [Test]
    public void ShapeOf_InheritsSpinFromTheHost()
    {
        var icon = CreateIcon();
        var button = new Button { Width = 80, Height = 30, Content = icon };
        var window = ShowInCanvas(button, 10, 10);

        try
        {
            HeadlessAppHost.PumpJobs();
            IconHoverMotion.SetShape(button, IconHoverShape.Spin);

            Assert.That(IconHoverMotion.ShapeOf(icon), Is.EqualTo(IconHoverShape.Spin), "图标要继承宿主的形态");
        }
        finally
        {
            window.Close();
            HeadlessAppHost.PumpJobs();
        }
    }

    [Test]
    public void BriefPointerJitter_KeepsTheSustainWindow()
    {
        var icon = CreateIcon();
        IconHoverMotion.SetShape(icon, IconHoverShape.Spin);
        var window = ShowInCanvas(icon, 20, 20);

        try
        {
            HeadlessAppHost.PumpJobs();
            var track = IconHoverTrack.For(icon);

            // 悬浮期间被追着抖动（弹层、提示卡常见）：每次离开都在 0.2s 宽限内回来，不能重新数 0.55s。
            for (var round = 0; round < 30; round++)
            {
                window.MouseMove(CenterOf(icon, window));
                HeadlessAppHost.PumpJobs();
                Tick(4);

                window.MouseMove(new Point(390, 290));
                HeadlessAppHost.PumpJobs();
                Tick(3);
            }

            TickHovering(window, icon, 20);
            Assert.That(track.Angle, Is.GreaterThan(20), "短暂抖动的悬浮必须照样转起来");
        }
        finally
        {
            window.Close();
            HeadlessAppHost.PumpJobs();
        }
    }

    [Test]
    public void NavRail_SettingsIcon_SpinsWhileHovered()
    {
        var window = new MainWindow { DataContext = new ShellViewModel() };

        try
        {
            window.Show();
            HeadlessAppHost.PumpJobs();

            var nav = window.FindControl<FANavigationView>("NavView");
            Assert.That(nav, Is.Not.Null);

            var settings = nav!.MenuItems.Concat(nav.FooterMenuItems)
                .OfType<FANavigationViewItem>()
                .FirstOrDefault(item => item.Tag is NavItem { Glyph: "Settings" });
            Assert.That(settings, Is.Not.Null, "导航栏必须有设置项");

            var icon = settings!.GetVisualDescendants()
                .OfType<Control>()
                .FirstOrDefault(control => control is IconElement || control.GetType().Name.EndsWith("Icon", StringComparison.Ordinal));
            Assert.That(icon, Is.Not.Null, "设置项必须有图标");

            var iconControl = icon!;
            var track = IconHoverTrack.For(iconControl);
            Assert.That(IconHoverMotion.ShapeOf(iconControl), Is.EqualTo(IconHoverShape.Spin), "设置齿轮必须映射成旋转形态");

            var center = iconControl.TranslatePoint(new Point(iconControl.Bounds.Width / 2, iconControl.Bounds.Height / 2), window);
            Assert.That(center, Is.Not.Null);

            // 侧栏项的悬浮弹层会和指针抢生命周期（进入即弹卡，卡盖上来指针就离开），
            // headless 里真实指针每帧进出一次，攒不满持续悬浮窗口。这里直接驱动真实设置图标上的轨道，
            // 验证 CreateNavItem 的形态接线与循环行为本身；指针通路由其余用例覆盖。
            track.Enter(IconHoverShape.Spin);
            Tick(50);
            var first = track.Angle;
            Assert.That(first, Is.GreaterThan(20), "设置齿轮悬停 0.8s 后必须转起来");

            Tick(50);
            Assert.That(track.Angle, Is.GreaterThan(first + 10), "长悬浮时齿轮要持续旋转，不能停在一圈");

            track.Exit();
            Tick(150);
            Assert.That(Math.Abs(Math.IEEERemainder(track.Angle, 360)), Is.LessThan(1.5), "离开后必须停在整圈位置");
            Assert.That(track.Scale, Is.EqualTo(1).Within(0.01), "离开后必须回到原始大小");
        }
        finally
        {
            window.Close();
            HeadlessAppHost.PumpJobs();
        }
    }

    private static PathIcon CreateIcon() => new()
    {
        Data = Geometry.Parse("M2 2 L14 2 L14 14 L2 14 Z"),
        Width = 16,
        Height = 16,
    };

    private static Window ShowInCanvas(Control content, double left, double top)
    {
        var canvas = new Canvas { Width = 400, Height = 300 };
        Canvas.SetLeft(content, left);
        Canvas.SetTop(content, top);
        canvas.Children.Add(content);
        return HeadlessAppHost.CreateTestWindow(canvas, 400, 300);
    }

    private static Point CenterOf(Control control, Window window)
    {
        var center = control.TranslatePoint(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), window);
        return center ?? new Point(0, 0);
    }

    private static void TickHovering(Window window, Control icon, int frames)
    {
        for (var frame = 0; frame < frames; frame++)
        {
            window.MouseMove(CenterOf(icon, window));
            Tick(1);
        }
    }

    private static void Tick(int frames)
    {
        for (var frame = 0; frame < frames; frame++)
        {
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            HeadlessAppHost.PumpJobs();
            Thread.Sleep(16);
        }
    }
}
