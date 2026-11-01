using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.VisualTree;
using CkanUI.App.Tests.Automation;
using CkanUI.ViewModels;
using FluentAvalonia.UI.Controls;
using NUnit.Framework;

namespace CkanUI.App.Tests;

/// <summary>
/// 折叠态悬停导航项必须弹出子菜单卡片（TabPopout）：卡片贴精简轨右侧、带该页入口，
/// 移开光标后收起；常驻展开时不再弹卡片（项本身已是完整文字）。
/// </summary>
[TestFixture]
[NonParallelizable]
public sealed class NavHoverPopoutTests
{
    [SetUp]
    public void SetUp() => HeadlessAppHost.EnsureInitialized();

    [Test]
    public void HoverCollapsedNavItem_ShowsPopoutCard()
    {
        var (window, shell) = CreateWindow();
        try
        {
            Assert.That(shell.NavHoverMode, Is.EqualTo(NavHoverMode.TabPopout), "默认悬停模式应是单 Tab 弹出");
            Assert.That(shell.IsNavCollapsed, Is.True);

            var (card, _) = HoverFirstItem(window);

            Assert.That(card.IsVisible, Is.True, "折叠态悬停导航项必须弹出子菜单卡片");
            Assert.That(card.Opacity, Is.GreaterThan(0.9), "卡片必须以可见不透明度呈现");
            Assert.That(card.Bounds.Width, Is.GreaterThan(80d), "卡片必须有实际宽度");

            var x = card.TranslatePoint(new Point(0, 0), window)?.X ?? -1;
            Assert.That(x, Is.InRange(40d, 90d), $"卡片应贴在 52px 精简轨右侧，实测 x={x:F0}");

            window.MouseMove(new Point(900, 400), RawInputModifiers.None);
            Pump(40);

            Assert.That(card.IsVisible, Is.False, "移开光标后卡片必须收起");
        }
        finally
        {
            window.Close();
            HeadlessAppHost.PumpJobs();
        }
    }

    [Test]
    public void ExpandedPane_DoesNotShowPopoutCard()
    {
        var (window, shell) = CreateWindow();
        try
        {
            shell.IsNavCollapsed = false;
            Pump(30);

            var (card, _) = HoverFirstItem(window);

            Assert.That(card.IsVisible, Is.False, "常驻展开时导航项已是完整文案，不应再弹卡片");
        }
        finally
        {
            window.Close();
            HeadlessAppHost.PumpJobs();
        }
    }

    [Test]
    public void ExpandingPane_HidesVisiblePopoutCard()
    {
        var (window, shell) = CreateWindow();
        try
        {
            var (card, _) = HoverFirstItem(window);
            Assert.That(card.IsVisible, Is.True);

            shell.IsNavCollapsed = false;
            Pump(30);

            Assert.That(card.IsVisible, Is.False, "展开动作必须立即收起悬停卡片，避免两层浮层打架");
        }
        finally
        {
            window.Close();
            HeadlessAppHost.PumpJobs();
        }
    }

    private static (Control Card, FANavigationViewItem Item) HoverFirstItem(Window window)
    {
        var nav = window.FindControl<FANavigationView>("NavView")!;
        var item = nav.GetVisualDescendants().OfType<FANavigationViewItem>().First(i => i.Bounds.Height > 0);
        // 精简轨只有 52px 宽，必须把指针放在轨道内，否则击中的是被推开的内容层。
        var point = item.TranslatePoint(new Point(26, Math.Min(20, item.Bounds.Height / 2)), window)!.Value;
        window.MouseMove(point, RawInputModifiers.None);
        Pump(25);
        return (window.FindControl<Control>("NavTabPopoutCard")!, item);
    }

    private static (Window Window, ShellViewModel Shell) CreateWindow()
    {
        var shell = new ShellViewModel();
        var window = new MainWindow { DataContext = shell };
        window.Width = 1280;
        window.Height = 800;
        window.Show();
        Pump(20);
        return (window, shell);
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
