using System.Linq;

using Avalonia;
using Avalonia.Animation;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.VisualTree;

using CkanUI.App.Tests.Automation;

using NUnit.Framework;

namespace CkanUI.App.Tests;

/// <summary>
/// 选中态切换的观感：从 A 换到 B 时，A 必须当帧熄灭，不能淡出一段"残留高亮"。
/// </summary>
/// <remarks>
/// 用户报的场景是「从 A 选到 B，A 会高亮一下，像被再次选中」。根因是行的底色挂了 BrushTransition：
/// 选中色退场要跑完过渡，旧行在那 100–200ms 里一直带着选中色。这里用真实指针 + 键盘走一遍，
/// 断言旧行在同一帧里就没有任何底色了。
/// </remarks>
[TestFixture]
[NonParallelizable]
public sealed class RowSelectionFlashTests
{
    [Test]
    public void MovingTheSelectionAwayFromARow_LeavesNoLingeringHighlight()
    {
        var list = new ListBox
        {
            Width = 240,
            Height = 200,
            ItemsSource = new[] { "第一项", "第二项", "第三项", "第四项" },
        };
        var canvas = new Canvas { Width = 400, Height = 300 };
        Canvas.SetLeft(list, 10);
        Canvas.SetTop(list, 10);
        canvas.Children.Add(list);
        var window = HeadlessAppHost.CreateTestWindow(canvas, 400, 300);

        try
        {
            HeadlessAppHost.PumpJobs();
            var first = (ListBoxItem)list.ContainerFromIndex(0)!;
            var second = (ListBoxItem)list.ContainerFromIndex(1)!;

            var firstPoint = first.TranslatePoint(new Point(40, 10), window)!.Value;
            window.MouseDown(firstPoint, MouseButton.Left);
            window.MouseUp(firstPoint, MouseButton.Left);
            HeadlessAppHost.PumpJobs();

            Assert.That(first.IsSelected, Is.True, "鼠标点下去必须选中第一行");
            Assert.That(ColorOf(first.Background).A, Is.GreaterThan(0), "选中行必须有底色");

            // 指针移出列表再走键盘：否则旧行会命中 :pointerover，测到的底色是悬停色而不是选中残留。
            window.MouseMove(new Point(380, 280));
            list.Focus();
            HeadlessAppHost.PumpJobs();
            Assert.That(first.IsPointerOver, Is.False, "指针应该已经离开列表");

            window.KeyPress(Key.Down, RawInputModifiers.None, PhysicalKey.ArrowDown, null);
            HeadlessAppHost.PumpJobs();

            Assert.That(first.IsSelected, Is.False, "方向键必须把选中移到第二行");
            Assert.That(second.IsSelected, Is.True);
            Assert.That(ColorOf(first.Background).A, Is.EqualTo(0),
                "旧行必须当帧熄灭：残留任何底色都会看起来像「又被选中了一次」");
        }
        finally
        {
            window.Close();
        }
    }

    [Test]
    public void RowContainers_DoNotAnimateTheirBackground()
    {
        var list = new ListBox
        {
            Width = 240,
            Height = 200,
            ItemsSource = new[] { "第一项", "第二项" },
        };
        var canvas = new Canvas { Width = 400, Height = 300 };
        canvas.Children.Add(list);
        var window = HeadlessAppHost.CreateTestWindow(canvas, 400, 300);

        try
        {
            HeadlessAppHost.PumpJobs();
            var item = (ListBoxItem)list.ContainerFromIndex(0)!;
            var backgroundTransitions = item.Transitions?.OfType<BrushTransition>().Count() ?? 0;

            Assert.That(backgroundTransitions, Is.Zero,
                "行容器不许给底色挂过渡：选中切换会变成一段可见的淡出");
            Assert.That(item.GetVisualDescendants().OfType<ContentPresenter>().FirstOrDefault(), Is.Not.Null,
                "位移过渡仍要留在模板内的 ContentPresenter 上");
        }
        finally
        {
            window.Close();
        }
    }

    private static Color ColorOf(IBrush? brush) => brush is ISolidColorBrush solid ? solid.Color : default;
}
