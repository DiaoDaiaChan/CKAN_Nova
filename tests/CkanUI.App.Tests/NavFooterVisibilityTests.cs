using System;
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
/// 精简轨底部的「日志 / 设置」必须开机就完整可见。
/// FluentAvalonia 用排期期量到的页脚高度去切 FooterItemsScrollViewer 的 MaxHeight，
/// 那次测量赶在 repeater 只实例化了一条的时候，上限只有一行 ——
/// 设置项被挤出可视区，用户得先展开一次侧栏（触发重算）才看得到它。
/// </summary>
[TestFixture]
[NonParallelizable]
public sealed class NavFooterVisibilityTests
{
    [SetUp]
    public void SetUp() => HeadlessAppHost.EnsureInitialized();

    [Test]
    public void BothFooterItems_AreFullyVisibleOnFirstFrame()
    {
        var (window, _) = CreateWindow();
        try
        {
            var nav = window.FindControl<FANavigationView>("NavView")!;
            var footer = FindNamed<ScrollViewer>(nav, "FooterItemsScrollViewer");
            var host = FindNamed<Control>(nav, "FooterMenuItemsHost");

            Assert.That(nav.FooterMenuItems.Count, Is.EqualTo(2), "页脚固定是日志 + 设置两项");
            Assert.That(host.DesiredSize.Height, Is.GreaterThan(0d), "页脚项必须已实例化");
            Assert.That(
                footer.MaxHeight,
                Is.GreaterThanOrEqualTo(host.DesiredSize.Height - 0.5),
                $"页脚上限只给一行时第二项会消失（MaxHeight={footer.MaxHeight:F0}, 内容={host.DesiredSize.Height:F0}）");

            Assert.That(FooterItemsInsideWindow(window, host, out var detail), Is.True, detail);
        }
        finally
        {
            window.Close();
            HeadlessAppHost.PumpJobs();
        }
    }

    [Test]
    public void StaleFooterCap_IsCorrectedByTheNextNavigationRebuild()
    {
        var (window, _) = CreateWindow();
        try
        {
            var nav = window.FindControl<FANavigationView>("NavView")!;
            var footer = FindNamed<ScrollViewer>(nav, "FooterItemsScrollViewer");
            var host = FindNamed<Control>(nav, "FooterMenuItemsHost");
            var contentHeight = host.DesiredSize.Height;

            // 复刻首帧的坏状态：上限被算成一行，设置项被裁掉。
            footer.MaxHeight = contentHeight / 2;
            footer.InvalidateMeasure();
            Pump(10);
            Assert.That(footer.Bounds.Height, Is.LessThan(contentHeight - 0.5), "先确认坏状态确实成立");

            // 换一次壳（生产里由入口表变化触发）→ 重建导航 → 上限必须回到内容高度。
            window.DataContext = new ShellViewModel();
            Pump(30);

            var refreshedFooter = FindNamed<ScrollViewer>(nav, "FooterItemsScrollViewer");
            var refreshedHost = FindNamed<Control>(nav, "FooterMenuItemsHost");
            Assert.That(
                refreshedFooter.MaxHeight,
                Is.GreaterThanOrEqualTo(refreshedHost.DesiredSize.Height - 0.5),
                "重建导航后页脚上限必须覆盖全部页脚项");
            Assert.That(FooterItemsInsideWindow(window, refreshedHost, out var detail), Is.True, detail);
        }
        finally
        {
            window.Close();
            HeadlessAppHost.PumpJobs();
        }
    }

    private static bool FooterItemsInsideWindow(Window window, Control host, out string detail)
    {
        var items = host.GetVisualDescendants().OfType<FANavigationViewItem>().ToList();
        detail = $"页脚项数={items.Count}";
        if (items.Count != 2)
        {
            detail += "（应有两项）";
            return false;
        }

        foreach (var item in items)
        {
            var bottom = item.TranslatePoint(new Point(0, item.Bounds.Height), window)?.Y ?? double.MaxValue;
            detail += $"，「{item.Content}」bottom={bottom:F0}，窗口高={window.Height:F0}";
            if (bottom > window.Height + 0.5 || !item.IsEffectivelyVisible)
            {
                return false;
            }
        }

        return true;
    }

    private static T FindNamed<T>(Visual root, string name)
        where T : Control =>
        root.GetVisualDescendants()
            .OfType<T>()
            .FirstOrDefault(control => string.Equals(control.Name, name, StringComparison.Ordinal))
        ?? throw new InvalidOperationException($"找不到模板部件 {name}");

    private static (Window Window, ShellViewModel Shell) CreateWindow()
    {
        var shell = new ShellViewModel();
        var window = new MainWindow { DataContext = shell };
        window.Width = 1282;
        window.Height = 802;
        window.Show();
        Pump(30);
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
