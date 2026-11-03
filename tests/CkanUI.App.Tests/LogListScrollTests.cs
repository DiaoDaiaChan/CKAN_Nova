using System;
using System.Linq;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.VisualTree;

using CkanUI.App.Tests.Automation;
using CkanUI.App.Views.Logs;
using CkanUI.Services.Abstractions.Logs;
using CkanUI.Services.Logs;
using CkanUI.ViewModels.Pages.Logs;

using NUnit.Framework;

namespace CkanUI.App.Tests;

/// <summary>
/// 日志列表的阅读顺序与滚动行为：时间正序（最新的在最下面）、跟随末尾自动贴底、
/// 刷新时守住用户看的位置、行高压缩到 20px。这几件事互相牵连，放在一起测免得改一个坏一个。
/// </summary>
[TestFixture]
[NonParallelizable]
public sealed class LogListScrollTests
{
    [SetUp]
    public void SetUp() => HeadlessAppHost.EnsureInitialized();

    [Test]
    public void Refresh_ShowsOldestFirst_WithNewestAtTheBottom()
    {
        var page = BuildPage(40, out _);

        page.RefreshCommand.Execute(null);
        HeadlessAppHost.PumpJobs();

        Assert.That(page.Rows, Is.Not.Empty);
        Assert.That(page.Rows[0].Seq, Is.EqualTo(1), "第一行应该是最旧的一条");
        Assert.That(page.Rows[^1].Seq, Is.EqualTo(40), "最后一行应该是最新的一条");
        Assert.That(page.SelectedRow?.Seq, Is.EqualTo(40), "跟随末尾时默认选中最新的那条");
    }

    [Test]
    public void Rows_AreTwentyPixelsHigh_SoAScreenFitsMoreOfThem()
    {
        var page = BuildPage(60, out _);
        var view = new LogPageView { DataContext = page };
        var window = new Window { Width = 1000, Height = 700, Content = view };
        window.Show();
        HeadlessAppHost.PumpJobs();

        page.RefreshCommand.Execute(null);
        HeadlessAppHost.PumpJobs();

        var items = view.FindControl<ListBox>("LogList")!
            .GetVisualDescendants()
            .OfType<ListBoxItem>()
            .Where(item => item.IsVisible)
            .ToList();
        Assert.That(items, Is.Not.Empty, "列表应该已经实现出行容器");
        Assert.That(items[0].Bounds.Height, Is.EqualTo(20d).Within(0.5d), "行高必须压到 20px，一屏才能多显示几行");
        window.Close();
    }

    [Test]
    public void FollowTail_KeepsTheViewPinnedToTheNewestRow()
    {
        var page = BuildPage(400, out var store);
        var view = new LogPageView { DataContext = page };
        var window = new Window { Width = 1000, Height = 700, Content = view };
        window.Show();
        HeadlessAppHost.PumpJobs();

        page.RefreshCommand.Execute(null);
        HeadlessAppHost.PumpJobs();

        var scroll = FindScroll(view);
        Assert.That(scroll.Offset.Y, Is.EqualTo(MaxOffset(scroll)).Within(1d), "跟随末尾时必须贴在最新一行");

        for (var seq = 401; seq <= 410; seq++)
        {
            store.Append(LogEvent.Create(LogSourceKind.Log4Net, LogLevel.Info, "log4net", "行 " + seq) with { Seq = seq });
            page.RefreshCommand.Execute(null);
            HeadlessAppHost.PumpJobs();
        }

        Assert.That(scroll.Offset.Y, Is.EqualTo(MaxOffset(scroll)).Within(1d), "再来新行也应该继续贴在末尾");
        Assert.That(page.Rows[^1].Seq, Is.EqualTo(410));
        window.Close();
    }

    [Test]
    public void Refresh_KeepsTheViewedPosition_WhenFollowingIsOff()
    {
        var page = BuildPage(400, out _);
        var view = new LogPageView { DataContext = page };
        var window = new Window { Width = 1000, Height = 700, Content = view };
        window.Show();
        HeadlessAppHost.PumpJobs();

        page.IsFollowTail = false;
        page.RefreshCommand.Execute(null);
        HeadlessAppHost.PumpJobs();

        var scroll = FindScroll(view);
        scroll.Offset = new Vector(0d, 2000d);
        HeadlessAppHost.PumpJobs();
        var before = scroll.Offset.Y;

        page.RefreshCommand.Execute(null);
        HeadlessAppHost.PumpJobs();

        Assert.That(scroll.Offset.Y, Is.EqualTo(before).Within(40d), "刷新不能把正在看历史的用户拽回列表顶部");
        window.Close();
    }

    [Test]
    public void LogRows_DoNotAnimateATransformOnHover()
    {
        // 全局 ListBoxItem 样式会给 RenderTransform 挂弹簧过渡；日志行被虚拟化反复回收/重建时，
        // 这些合成动画会让快速拖动滚动条后的画面留下错位的半透明「鬼影」行。只读列表必须关掉它。
        var page = BuildPage(60, out _);
        var view = new LogPageView { DataContext = page };
        var window = new Window { Width = 1000, Height = 700, Content = view };
        window.Show();
        HeadlessAppHost.PumpJobs();

        page.RefreshCommand.Execute(null);
        HeadlessAppHost.PumpJobs();

        var row = view.FindControl<ListBox>("LogList")!
            .GetVisualDescendants()
            .OfType<ListBoxItem>()
            .First(item => item.IsVisible);
        Assert.That(row.Transitions, Is.Not.Null);
        Assert.That(
            row.Transitions!.OfType<Avalonia.Animation.TransformOperationsTransition>(),
            Is.Empty,
            "日志行的悬停位移动画必须关掉，否则虚拟化复用容器会留鬼影");
        Assert.That(row.Cursor?.ToString(), Is.EqualTo("Arrow"), "只读日志行不该显示手型光标");
        window.Close();
    }

    [Test]
    public void ScrollingToAnyPosition_AlwaysLeavesRowsRealized()
    {
        // 用户报的怪状：拖完滚动条后整片空白，只剩一行。行还在、位置也在范围内，
        // 却没有行容器被实体化 —— 这是虚拟化列表的回收/重建窗口，视图里有一层兜底。
        var page = BuildPage(400, out _);
        var view = new LogPageView { DataContext = page };
        var window = new Window { Width = 1000, Height = 700, Content = view };
        window.Show();
        HeadlessAppHost.PumpJobs();

        page.IsFollowTail = false;
        page.RefreshCommand.Execute(null);
        HeadlessAppHost.PumpJobs();

        var scroll = FindScroll(view);
        var max = MaxOffset(scroll);
        foreach (var fraction in new[] { 0d, 0.25d, 0.5d, 0.75d, 1d })
        {
            scroll.Offset = new Vector(0d, max * fraction);
            HeadlessAppHost.PumpJobs();
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            HeadlessAppHost.PumpJobs();

            var realized = view.FindControl<ListBox>("LogList")!
                .GetVisualDescendants()
                .OfType<ListBoxItem>()
                .Count(item => item.IsVisible);
            Assert.That(realized, Is.GreaterThan(3), $"滚到 {fraction:P0} 处也必须实现出行容器，否则用户看到空白");
        }

        window.Close();
    }

    private static LogPageViewModel BuildPage(int count, out LogStore store)
    {
        store = new LogStore(null);
        store.AppendRange(Enumerable.Range(1, count)
            .Select(seq => LogEvent.Create(LogSourceKind.Log4Net, LogLevel.Info, "log4net", "行 " + seq) with { Seq = seq })
            .ToList());

        return new LogPageViewModel(store, new LogQueryService(store));
    }

    private static ScrollViewer FindScroll(LogPageView view) =>
        view.FindControl<ListBox>("LogList")!
            .GetVisualDescendants()
            .OfType<ScrollViewer>()
            .First();

    private static double MaxOffset(ScrollViewer scroll) =>
        Math.Max(0d, scroll.Extent.Height - scroll.Viewport.Height);
}
