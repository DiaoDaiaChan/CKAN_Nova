using Avalonia;
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
/// 指针交互矩阵：按钮、列表行、勾选控件、树节点在指针移上去之后的可观测变化。
/// </summary>
/// <remarks>
/// 用 Avalonia.Headless 的真实指针事件走一遍命中测试，而不是只断言样式表里写了什么——
/// 只有「指针真的落上去、样式真的生效」才算交互存在。
/// </remarks>
[TestFixture]
public sealed class HoverInteractionTests
{
    [Test]
    public void Button_HoverLiftsAndShowsHandCursor()
    {
        var button = new Button { Content = "应用", Width = 120, Height = 40 };
        var window = ShowInCanvas(button, 20, 20);

        try
        {
            Assert.That(button.Cursor?.ToString(), Is.EqualTo("Hand"), "按钮必须给手型光标");

            window.MouseMove(new Point(80, 40));
            HeadlessAppHost.PumpJobs();

            Assert.That(button.IsPointerOver, Is.True, "指针应该落在按钮上");
            Assert.That(OffsetOf(button).Y, Is.EqualTo(-1), "悬浮必须把按钮抬 1px");
        }
        finally
        {
            window.Close();
        }
    }

    [Test]
    public void ListBoxItem_HoverMovesItsPresenterNotItself_SoTheHitBoxStaysPut()
    {
        var list = new ListBox { ItemsSource = new[] { "第一项", "第二项" }, Height = 200, Width = 200 };
        var window = ShowInCanvas(list, 10, 10);

        try
        {
            HeadlessAppHost.PumpJobs();
            var item = list.ContainerFromIndex(1) as ListBoxItem;
            Assert.That(item, Is.Not.Null, "列表必须生成行容器");
            Assert.That(item!.Cursor?.ToString(), Is.EqualTo("Hand"), "列表行必须给手型光标");

            var center = item.TranslatePoint(new Point(40, 16), window);
            Assert.That(center, Is.Not.Null);
            window.MouseMove(center!.Value);
            HeadlessAppHost.PumpJobs();

            Assert.That(item.IsPointerOver, Is.True);
            // 位移必须落在一行之外的展示层（模板里的 ContentPresenter）：行容器自己一动，指针压住左缘就会
            // 掉出命中盒 → 悬停失效 → 弹回 → 再压上，视觉上在选中/未选中之间闪烁。
            Assert.That(OffsetOf(item).X, Is.EqualTo(0), "行容器本身不许位移，否则悬停会自激闪烁");
            var presenter = item.GetVisualDescendants().OfType<ContentPresenter>().FirstOrDefault();
            Assert.That(presenter, Is.Not.Null, "列表行的模板必须有 ContentPresenter");
            Assert.That(Math.Abs(OffsetOf(presenter!).X), Is.EqualTo(2).Within(0.2), "悬浮位移落在 ContentPresenter 上");
        }
        finally
        {
            window.Close();
        }
    }

    [Test]
    public void CheckBox_HoverTurnsLabelAccentAndShowsHandCursor()
    {
        var checkBox = new CheckBox { Content = "同时打包 zip", Width = 220, Height = 32 };
        var window = ShowInCanvas(checkBox, 10, 10);

        try
        {
            Assert.That(checkBox.Cursor?.ToString(), Is.EqualTo("Hand"));

            var before = ColorOf(checkBox.Foreground);

            window.MouseMove(new Point(60, 26));
            HeadlessAppHost.PumpJobs();

            Assert.That(checkBox.IsPointerOver, Is.True);
            Assert.That(ColorOf(checkBox.Foreground), Is.Not.EqualTo(before), "悬浮时文字必须换色");
        }
        finally
        {
            window.Close();
        }
    }

    [Test]
    public void TreeViewItem_HoverHighlightsWithBackground_WithoutMovingItself()
    {
        var tree = new TreeView { Width = 240, Height = 200, ItemsSource = new[] { "节点甲" } };
        var window = ShowInCanvas(tree, 10, 10);

        try
        {
            HeadlessAppHost.PumpJobs();
            var node = tree.ContainerFromIndex(0) as TreeViewItem;
            Assert.That(node, Is.Not.Null, "树控件必须生成节点容器");
            Assert.That(node!.Cursor?.ToString(), Is.EqualTo("Hand"), "树节点必须给手型光标");

            var center = node.TranslatePoint(new Point(30, 12), window);
            Assert.That(center, Is.Not.Null);
            window.MouseMove(center!.Value);
            HeadlessAppHost.PumpJobs();

            Assert.That(node.IsPointerOver, Is.True);
            Assert.That(OffsetOf(node).X, Is.EqualTo(0), "树节点用背景高亮，不许位移");
            Assert.That(node.Background, Is.Not.Null, "悬浮必须给出背景高亮");
            Assert.That(((ISolidColorBrush)node.Background!).Color, Is.Not.EqualTo(Colors.Transparent));
        }
        finally
        {
            window.Close();
        }
    }

    private static Window ShowInCanvas(Control control, double left, double top)
    {
        var canvas = new Canvas { Width = 400, Height = 300 };
        Canvas.SetLeft(control, left);
        Canvas.SetTop(control, top);
        canvas.Children.Add(control);
        return HeadlessAppHost.CreateTestWindow(canvas, 400, 300);
    }

    private static Vector OffsetOf(Control control) =>
        control.RenderTransform is ITransform transform
            ? new Vector(transform.Value.M31, transform.Value.M32)
            : default;

    private static Color ColorOf(IBrush? brush) => brush is ISolidColorBrush solid ? solid.Color : default;
}
