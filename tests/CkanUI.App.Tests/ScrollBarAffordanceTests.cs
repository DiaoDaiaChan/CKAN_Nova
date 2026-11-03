using System;
using System.Linq;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.VisualTree;

using CkanUI.App.Tests.Automation;
using CkanUI.App.Views.Logs;
using CkanUI.Services.Abstractions.Logs;
using CkanUI.Services.Logs;
using CkanUI.ViewModels.Pages.Logs;

using NUnit.Framework;

namespace CkanUI.App.Tests;

/// <summary>
/// 滚动条的可见性与拖动稳定性：以前滚动条默认 Opacity=0（指针悬浮才出现），
/// 列表看不出哪里能拖、也没有任何提示；日志列表还因为虚拟化在快速拖动时丢行。
/// </summary>
[TestFixture]
[NonParallelizable]
public sealed class ScrollBarAffordanceTests
{
    [SetUp]
    public void SetUp() => HeadlessAppHost.EnsureInitialized();

    [Test]
    public void ScrollBars_StayVisibleAtRest_AndBrightenOnHover()
    {
        var (window, view) = BuildWindow(60);
        var bar = FindVerticalBar(view);

        Assert.That(bar.Opacity, Is.EqualTo(0.7d).Within(0.001d), "滚动条在静止时也必须看得见，不能整条透明");
        Assert.That(bar.Width, Is.EqualTo(10d).Within(0.5d), "10px 宽才够瞄准拖动");

        var thumb = bar.GetVisualDescendants().OfType<Thumb>().First();
        Assert.That(thumb.Background, Is.InstanceOf<IBrush>(), "滑块要有底色，否则看不见轨道位置");
        Assert.That(((ISolidColorBrush)thumb.Background!).Color.A, Is.GreaterThan((byte)0), "滑块底色不能是全透明");

        var origin = bar.TranslatePoint(new Point(bar.Bounds.Width / 2, bar.Bounds.Height / 2), window)!.Value;
        window.MouseMove(origin);
        HeadlessAppHost.PumpJobs();
        for (var frame = 0; frame < 12; frame++)
        {
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            HeadlessAppHost.PumpJobs();
            System.Threading.Thread.Sleep(12);
        }

        Assert.That(bar.Opacity, Is.EqualTo(1d).Within(0.02d), "指针压上去要加亮，明确「这里可以拖」");
        window.Close();
    }

    [Test]
    public void EveryLogRowIsRealized_SoAFastScrollbarDragCannotLeaveHoles()
    {
        var (window, view, page) = BuildWindowWithPage(500);
        var list = view.FindControl<ListBox>("LogList")!;

        Assert.That(list.ItemsPanel, Is.Not.Null, "列表必须显式声明面板模板（这里刻意不用虚拟化面板）");
        var items = list.GetVisualDescendants().OfType<ListBoxItem>().ToList();
        Assert.That(items, Has.Count.EqualTo(page.Rows.Count), "500 行上限内必须一次全部实现，拖动时没有「待实现」的空洞");
        Assert.That(items.All(i => i.Bounds.Height > 0d), Is.True, "实现出来的行都要有实际高度");

        var scroll = list.GetVisualDescendants().OfType<ScrollViewer>().First();
        var covered = items.Sum(i => i.Bounds.Height);
        Assert.That(covered, Is.EqualTo(page.Rows.Count * 20d).Within(1d), "实现出来的行总高必须等于整条列表的内容高度");
        Assert.That(scroll.Extent.Height, Is.EqualTo(covered).Within(1d), "滚动范围与实际行高一致，拖动才不会有空白段");
        window.Close();
    }

    private static (Window Window, LogPageView View) BuildWindow(int count)
    {
        var (window, view, _) = BuildWindowWithPage(count);
        return (window, view);
    }

    private static (Window Window, LogPageView View, LogPageViewModel Page) BuildWindowWithPage(int count)
    {
        var store = new LogStore(null);
        store.AppendRange(Enumerable.Range(1, count)
            .Select(seq => LogEvent.Create(LogSourceKind.Log4Net, LogLevel.Info, "log4net", "行 " + seq) with { Seq = seq })
            .ToList());
        var page = new LogPageViewModel(store, new LogQueryService(store));
        var view = new LogPageView { DataContext = page };
        var window = new Window { Width = 1000, Height = 700, Content = view };
        window.Show();
        HeadlessAppHost.PumpJobs();
        page.RefreshCommand.Execute(null);
        HeadlessAppHost.PumpJobs();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        HeadlessAppHost.PumpJobs();
        return (window, view, page);
    }

    private static ScrollBar FindVerticalBar(LogPageView view)
        => view.FindControl<ListBox>("LogList")!
            .GetVisualDescendants()
            .OfType<ScrollBar>()
            .First(b => b.Orientation == Orientation.Vertical);
}
