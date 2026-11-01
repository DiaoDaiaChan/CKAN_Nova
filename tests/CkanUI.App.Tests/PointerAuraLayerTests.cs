using System.Linq;

using Avalonia;
using Avalonia.Controls;
using Avalonia.VisualTree;

using CkanUI.App.Controls;
using CkanUI.App.Tests.Automation;

using NUnit.Framework;

namespace CkanUI.App.Tests;

/// <summary>
/// 指针光环的挂载行为：指针落在按钮上时要认出「可响应表面」，落在空白处要认不出来。
/// 这层判定决定光斑是贴着元素画、还是朝整窗乱糊。
/// </summary>
[TestFixture]
public sealed class PointerAuraLayerTests
{
    [SetUp]
    public void SetUp() => HeadlessAppHost.EnsureInitialized();

    [Test]
    public void FindSurface_RecognizesButtonsAndListRows()
    {
        var button = new Button { Content = "应用变更", Classes = { "CkanPrimary" } };
        var list = new ListBox { ItemsSource = new[] { "一行" }, Height = 120 };
        var panel = new StackPanel { Children = { button, list } };
        var window = HeadlessAppHost.CreateTestWindow(panel, 420, 240);
        try
        {
            HeadlessAppHost.PumpJobs();

            Control text = button.GetVisualDescendants().OfType<TextBlock>().FirstOrDefault() ?? (Control)button;
            Assert.That(PointerAuraLayer.FindSurface(text, window), Is.SameAs(button), "按钮内的文字要归到按钮上");

            var item = list.GetVisualDescendants().OfType<ListBoxItem>().FirstOrDefault();
            Assert.That(item, Is.Not.Null);
            Assert.That(PointerAuraLayer.FindSurface(item, window), Is.SameAs(item));
        }
        finally
        {
            window.Close();
        }
    }

    [Test]
    public void FindSurface_ReturnsNullOnBlankPanel()
    {
        var panel = new Canvas { Background = Avalonia.Media.Brushes.Transparent };
        var window = HeadlessAppHost.CreateTestWindow(panel, 320, 200);
        try
        {
            HeadlessAppHost.PumpJobs();
            Assert.That(PointerAuraLayer.FindSurface(panel, window), Is.Null);
        }
        finally
        {
            window.Close();
        }
    }

    [Test]
    public void FindSurface_OnlyRecognizesInteractiveCards_ExcludesStaticCards()
    {
        var staticCard = new Border { Classes = { "CkanCard" } };
        var emptyCard = new Border { Classes = { "CkanEmptyStateCard" } };
        var interactiveCard = new Border { Classes = { "CkanCardInteractive" } };
        var panel = new StackPanel { Children = { staticCard, emptyCard, interactiveCard } };
        var window = HeadlessAppHost.CreateTestWindow(panel, 320, 200);
        try
        {
            HeadlessAppHost.PumpJobs();
            Assert.That(PointerAuraLayer.FindSurface(staticCard, window), Is.Null, "静态容器卡片不作为响应表面，避免全卡片亮起与误按压");
            Assert.That(PointerAuraLayer.FindSurface(emptyCard, window), Is.Null, "空状态卡片不作为响应表面");
            Assert.That(PointerAuraLayer.FindSurface(interactiveCard, window), Is.SameAs(interactiveCard), "可点击卡片保留响应表面");
        }
        finally
        {
            window.Close();
        }
    }

    [Test]
    public void Track_OnButton_TargetsItsRoundedBounds()
    {
        var button = new Button { Content = "刷新", Width = 120, Height = 32 };
        var layer = new PointerAuraLayer { Width = 320, Height = 200 };
        var host = new Panel { Children = { button, layer } };
        var window = HeadlessAppHost.CreateTestWindow(host, 320, 200);
        try
        {
            HeadlessAppHost.PumpJobs();

            layer.Track(new Point(10, 10), PointerAuraLayer.FindSurface(button, window));
            HeadlessAppHost.PumpJobs();

            // 刚 Track 时 Alpha 还是 0，要等计时器推一帧才亮；这里只验证「认出了表面、拿到了真实矩形」。
            Assert.That(layer.TargetKind, Is.EqualTo("Button"));
            Assert.That(layer.TargetBounds.Height, Is.GreaterThan(0d));
            Assert.That(layer.TargetBounds.Width, Is.GreaterThan(0d));

            layer.Track(new Point(200, 180), null);
            HeadlessAppHost.PumpJobs();
            Assert.That(layer.TargetKind, Is.Null, "指针移到空白处必须放下目标");
        }
        finally
        {
            window.Close();
        }
    }
}
