using System;
using System.Collections.Generic;
using System.Linq;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Shapes;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.VisualTree;

using CkanUI.App.Animations;
using CkanUI.App.Tests.Automation;

using NUnit.Framework;

namespace CkanUI.App.Tests;

/// <summary>
/// 滚动条的抓手与活动态：
/// 拇指以前被 28 的最小宽度撑出 10px 轨道、横向裁掉一多半；
/// 指针压上去只有整条变亮，拇指本身不变色；也没有「正在滚动」的反馈。
/// </summary>
[TestFixture]
[NonParallelizable]
public sealed class ScrollBarInteractionTests
{
    private Action<TimeSpan, Action>? _scheduler;

    [SetUp]
    public void SetUp()
    {
        HeadlessAppHost.EnsureInitialized();
        _scheduler = ScrollBarActivityMotion.Scheduler;
    }

    [TearDown]
    public void TearDown()
    {
        if (_scheduler is not null)
        {
            ScrollBarActivityMotion.Scheduler = _scheduler;
        }
    }

    [Test]
    public void VerticalThumbKeepsItsWidthInsideTheTrack()
    {
        var (window, list) = BuildWindow(400);
        var bar = VerticalBar(list);
        var thumb = ThumbOf(bar);

        Assert.That(thumb.Bounds.Width, Is.LessThanOrEqualTo(bar.Bounds.Width + 0.01d),
            "竖直拇指不能比轨道宽，否则左右两侧被裁掉，圆角和宽度都不对");
        Assert.That(thumb.Bounds.Height, Is.GreaterThanOrEqualTo(27.5d), "拇指要有 28 的最小高度，否则拖不住");
        Assert.That(thumb.TranslatePoint(new Point(0, 0), bar)!.Value.X, Is.GreaterThanOrEqualTo(-0.01d),
            "拇指左边缘不能跑到轨道外面去");
        window.Close();
    }

    [Test]
    public void HoverPaintsTheThumbWithTheAccent()
    {
        var (window, list) = BuildWindow(400);
        var bar = VerticalBar(list);
        var face = FaceOf(bar);
        var accent = AccentOf(bar);

        var rest = (face.Fill as ISolidColorBrush)?.Color;
        Assert.That(rest, Is.Not.Null, "拇指静止时也要有底色，不能是全透明");

        window.MouseMove(CenterOf(bar, window));
        Pump(12);

        Assert.That(bar.Opacity, Is.EqualTo(1d).Within(0.02d), "指针压上去整条要加亮");
        Assert.That((face.Fill as ISolidColorBrush)?.Color, Is.EqualTo(accent),
            "指针压上去拇指本身要换成强调色，只把整条调亮看不出可拖");
        window.MouseMove(new Point(2, 2));
        window.Close();
    }

    [Test]
    public void DraggingTheThumbKeepsTheAccent()
    {
        var (window, list) = BuildWindow(400);
        var bar = VerticalBar(list);
        var thumb = ThumbOf(bar);
        var face = FaceOf(bar);
        var accent = AccentOf(bar);

        var start = CenterOf(thumb, window);
        window.MouseMove(start);
        window.MouseDown(start, MouseButton.Left);
        Pump(12);

        Assert.That(thumb.Classes.Contains(":pressed"), Is.True, "按住拇指要进按下态");
        Assert.That((face.Fill as ISolidColorBrush)?.Color, Is.EqualTo(accent), "按住时拇指保持强调色");

        window.MouseUp(start, MouseButton.Left);
        window.MouseMove(new Point(2, 2));
        Pump(4);
        window.Close();
    }

    [Test]
    public void ScrollingLightsTheBar_AndRestPutsItBack()
    {
        var pending = new List<Action>();
        ScrollBarActivityMotion.Scheduler = (_, callback) => pending.Add(callback);

        var (window, list) = BuildWindow(400);
        ScrollRevealMotion.Attach(window);
        var bar = VerticalBar(list);
        var scroll = list.GetVisualDescendants().OfType<ScrollViewer>().First();

        Assert.That(bar.Opacity, Is.EqualTo(0.7d).Within(0.001d), "静止时是 0.7，不能一开始就全程高亮");

        scroll.Offset = new Vector(0, 600);
        HeadlessTick();
        Assert.That(bar.Classes.Contains(ScrollBarActivityMotion.ActiveClass), Is.True,
            "滚动一开始就要挂上 is-scrolling");
        Pump(12);
        Assert.That(bar.Opacity, Is.EqualTo(1d).Within(0.02d), "滚动期间整条要亮起来");

        Assert.That(pending, Is.Not.Empty, "亮起来之后必须排一次「松手多久算停」的复查");
        System.Threading.Thread.Sleep((int)(ScrollBarActivityMotion.RestAfterSeconds * 1000) + 120);
        pending[^1]();

        Assert.That(bar.Classes.Contains(ScrollBarActivityMotion.ActiveClass), Is.False,
            "停手之后要把活动态摘掉，否则滚动条永远高亮");
        Pump(14);
        Assert.That(bar.Opacity, Is.EqualTo(0.7d).Within(0.03d), "摘掉活动态后亮度要回到静止值");
        window.Close();
    }

    private static void HeadlessTick()
    {
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        HeadlessAppHost.PumpJobs();
    }

    private static void Pump(int frames)
    {
        for (var i = 0; i < frames; i++)
        {
            HeadlessTick();
            System.Threading.Thread.Sleep(12);
        }
    }

    private static (Window Window, ListBox List) BuildWindow(int rows)
    {
        var list = new ListBox
        {
            Width = 400,
            Height = 500,
            ItemsSource = Enumerable.Range(1, rows).Select(i => "行 " + i).ToList(),
        };
        var window = new Window { Width = 460, Height = 560, Content = list };
        window.Show();
        Pump(4);
        return (window, list);
    }

    private static ScrollBar VerticalBar(ListBox list)
        => list.GetVisualDescendants().OfType<ScrollBar>().First(b => b.Orientation == Orientation.Vertical);

    private static Thumb ThumbOf(ScrollBar bar)
        => bar.GetVisualDescendants().OfType<Thumb>().First();

    private static Rectangle FaceOf(ScrollBar bar)
        => ThumbOf(bar).GetVisualDescendants().OfType<Rectangle>().First();

    private static Color AccentOf(Visual visual)
    {
        Assert.That(visual.TryFindResource("CkanAccentBrush", visual.ActualThemeVariant, out var resource), Is.True,
            "必须能解析 CkanAccentBrush");
        Assert.That(resource, Is.InstanceOf<ISolidColorBrush>());
        return ((ISolidColorBrush)resource!).Color;
    }

    private static Point CenterOf(Visual visual, Window window)
        => visual.TranslatePoint(new Point(visual.Bounds.Width / 2, visual.Bounds.Height / 2), window)!.Value;
}
