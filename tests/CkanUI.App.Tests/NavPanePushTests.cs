using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.VisualTree;
using CkanUI.App.Tests.Automation;
using CkanUI.ViewModels;
using FluentAvalonia.UI.Controls;
using NUnit.Framework;

namespace CkanUI.App.Tests;

/// <summary>
/// 侧栏展开必须是"推开"内容（inline），不能浮在页面上覆盖：
/// LeftCompact 的底层是 CompactOverlay，展开只会盖住内容；Left 才会把内容整体右移。
/// 断言对象是页面宿主 PageHost 在窗口坐标系里的左边界（内容层容器没有独立命名控件）。
/// </summary>
[TestFixture]
[NonParallelizable]
public sealed class NavPanePushTests
{
    [SetUp]
    public void SetUp() => HeadlessAppHost.EnsureInitialized();

    [Test]
    public void ExpandedPane_PushesContentAside()
    {
        var (window, shell, nav, page) = CreateWindow();
        try
        {
            var collapsedX = LeftOf(page, window);
            Assert.That(collapsedX, Is.LessThan(80d), $"折叠态内容应贴着 52px 精简轨，实测 {collapsedX:F0}px");

            shell.IsNavCollapsed = false;
            Pump(30);

            var expandedX = LeftOf(page, window);
            Assert.That(nav.IsPaneOpen, Is.True, "展开后侧栏面板必须真的打开");
            Assert.That(nav.Bounds.Width, Is.GreaterThan(0d));
            Assert.That(expandedX, Is.GreaterThan(200d), $"展开后内容必须被推开，实测左边界只有 {expandedX:F0}px");
            Assert.That(expandedX - collapsedX, Is.InRange(180d, 230d), "推开位移应接近 256-52=204px");

            shell.IsNavCollapsed = true;
            Pump(30);

            var restoredX = LeftOf(page, window);
            Assert.That(restoredX, Is.LessThan(80d), $"收回后内容必须回弹，实测左边界 {restoredX:F0}px");
        }
        finally
        {
            window.Close();
            HeadlessAppHost.PumpJobs();
        }
    }

    [Test]
    public void ExpandedPane_UsesInlineNotOverlay()
    {
        var (window, shell, nav, _) = CreateWindow();
        try
        {
            shell.IsNavCollapsed = false;
            Pump(30);

            Assert.That(nav.PaneDisplayMode, Is.EqualTo(FANavigationViewPaneDisplayMode.Left),
                "常驻展开必须是 Left（inline 推开），LeftCompact 的 CompactOverlay 只会盖住内容");
        }
        finally
        {
            window.Close();
            HeadlessAppHost.PumpJobs();
        }
    }

    [Test]
    public void CollapseAnimatesTogether_ContentNeverSlidesUnderPane()
    {
        var (window, shell, nav, page) = CreateWindow();
        try
        {
            shell.IsNavCollapsed = false;
            Pump(30);

            shell.IsNavCollapsed = true;

            // 逐帧检查：面板宽度收缩的每一帧，内容左边界都必须仍在面板右侧。
            // 收起时若立刻切回 LeftCompact（CompactOverlay），内容会先弹回 x=53、面板还停在 256px 处盖住内容。
            var sample = new List<(double Pane, double Page)>();
            for (var i = 0; i < 12; i++)
            {
                AvaloniaHeadlessPlatform.ForceRenderTimerTick();
                HeadlessAppHost.PumpJobs();
                System.Threading.Thread.Sleep(8);
                var pane = nav.GetVisualDescendants().OfType<Control>().FirstOrDefault(c => c.Name == "PART_PaneRoot");
                if (pane is not null)
                {
                    sample.Add((pane.Bounds.Width, LeftOf(page, window)));
                }
            }

            var overlaps = sample.Where(s => s.Page < s.Pane - 4).ToList();
            Assert.That(overlaps, Is.Empty,
                "收起过程中内容不得被面板盖住：" + string.Join(", ", sample.Select(s => $"{s.Pane:F0}/{s.Page:F0}")));
            Assert.That(sample.Select(s => s.Pane).Distinct().Count(), Is.GreaterThan(2), "面板宽度必须逐帧过渡，不能瞬间切换");

            Pump(70);
            Assert.That(LeftOf(page, window), Is.LessThan(80d), "收起完成后内容回到精简轨右侧");

            // 降回 LeftCompact 由 _navPaneSettleTimer（DispatcherTimer）收尾；headless 下没有真实 dispatcher 循环，
            // 定时器不触发，所以这里不断言模式，只锁用户可见的几何契约。真实运行时的模式回落由抽屉路径兜底。
        }
        finally
        {
            window.Close();
            HeadlessAppHost.PumpJobs();
        }
    }

    private static (Window Window, ShellViewModel Shell, FANavigationView Nav, Control Page) CreateWindow()
    {
        var shell = new ShellViewModel();
        var window = new MainWindow { DataContext = shell };
        window.Width = 1280;
        window.Height = 800;
        window.Show();
        Pump(20);
        return (window, shell, window.FindControl<FANavigationView>("NavView")!, window.FindControl<Control>("PageHost")!);
    }

    private static double LeftOf(Control content, Window window)
        => content.TranslatePoint(new Point(0, 0), window)?.X ?? -1;

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
