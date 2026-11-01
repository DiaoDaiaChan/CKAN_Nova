using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using CkanUI.App.Tests.Automation;
using CkanUI.ViewModels;
using FluentAvalonia.UI.Controls;
using NUnit.Framework;

namespace CkanUI.App.Tests;

/// <summary>
/// 侧栏展开必须是"推开"内容（inline），不能浮在页面上覆盖：
/// LeftCompact 的底层是 CompactOverlay，展开只会盖住内容；Left 才会把内容整体右移。
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
        var window = CreateWindow(out var nav, out var content);
        try
        {
            nav.PaneDisplayMode = FANavigationViewPaneDisplayMode.Left;
            nav.IsPaneOpen = true;
            Pump(20);

            var x = LeftOf(content, window);
            Assert.That(x, Is.GreaterThan(200d), $"展开后内容必须被推开，实测左边界只有 {x:F0}px");
        }
        finally
        {
            window.Close();
            HeadlessAppHost.PumpJobs();
        }
    }

    [Test]
    public void CompactOverlay_WouldCoverTheContent()
    {
        var window = CreateWindow(out var nav, out var content);
        try
        {
            nav.PaneDisplayMode = FANavigationViewPaneDisplayMode.LeftCompact;
            nav.IsPaneOpen = true;
            Pump(20);

            var x = LeftOf(content, window);
            Assert.That(x, Is.LessThan(100d), "LeftCompact 是覆盖语义：内容不动，这条用例锁住两种模式的差别");
        }
        finally
        {
            window.Close();
            HeadlessAppHost.PumpJobs();
        }
    }

    private static Window CreateWindow(out FANavigationView nav, out Border content)
    {
        var window = new MainWindow { DataContext = new ShellViewModel() };
        window.Show();
        Pump(10);
        nav = window.FindControl<FANavigationView>("NavView")!;
        content = window.FindControl<Border>("ContentGridBorder")!;
        return window;
    }

    private static double LeftOf(Border content, Window window)
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
