using System.Linq;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Media;
using Avalonia.VisualTree;

using CkanUI.App.Tests.Automation;

using NUnit.Framework;

namespace CkanUI.App.Tests;

/// <summary>
/// 磨砂玻璃基底（MAT）：按钮/胶囊/行级控件默认是半透明填充 + 高光描边。
/// 这里不拍图，只钉住「样式是否真的落到了控件的画刷上」和「token 是否存在」两件事。
/// </summary>
[TestFixture]
public sealed class GlassSurfaceTests
{
    [SetUp]
    public void SetUp() => HeadlessAppHost.EnsureInitialized();

    [Test]
    public void SecondaryButton_BorderIsATopLightGradient()
    {
        var button = new Button { Content = "次要操作", Classes = { "CkanSecondary" } };
        var window = HeadlessAppHost.CreateTestWindow(button, 320, 160);
        try
        {
            // 亚克力的「厚度」来自顶亮底暗的 1px 描边，不是纯色边框。
            // Button 自身只声明画刷，真正画边框的是模板里的 ContentPresenter，必须看后者。
            var presenter = button.GetVisualDescendants().OfType<ContentPresenter>().FirstOrDefault();
            Assert.That(presenter, Is.Not.Null, "按钮模板必须生成 ContentPresenter");
            var gradient = presenter!.BorderBrush as LinearGradientBrush;
            Assert.That(gradient, Is.Not.Null, "玻璃描边必须是渐变");
            Assert.That(gradient!.GradientStops.Count, Is.GreaterThanOrEqualTo(2));
            Assert.That(gradient.GradientStops[0].Color.A, Is.GreaterThan(gradient.GradientStops[^1].Color.A));
        }
        finally
        {
            window.Close();
        }
    }

    [Test]
    public void SecondaryButton_GetsATranslucentGlassFill()
    {
        var button = new Button { Content = "次要操作", Classes = { "CkanSecondary" } };
        var window = HeadlessAppHost.CreateTestWindow(button, 320, 160);
        try
        {
            var brush = button.Background as ISolidColorBrush;
            Assert.That(brush, Is.Not.Null);
            Assert.That(brush!.Color.A, Is.LessThan((byte)255));
            Assert.That(brush.Color.A, Is.GreaterThan((byte)0));
        }
        finally
        {
            window.Close();
        }
    }

    [Test]
    public void GhostButton_StaysFullyTransparent()
    {
        var button = new Button { Content = "幽灵", Classes = { "CkanGhost" } };
        var window = HeadlessAppHost.CreateTestWindow(button, 320, 160);
        try
        {
            var brush = button.Background as ISolidColorBrush;
            if (brush is not null)
            {
                Assert.That(brush.Color.A, Is.EqualTo((byte)0));
            }
        }
        finally
        {
            window.Close();
        }
    }

    [Test]
    public void Chip_GetsTheSameGlassFamilyAsButtons()
    {
        var chip = new Button { Content = "筛选", Classes = { "CkanChip" } };
        var window = HeadlessAppHost.CreateTestWindow(chip, 320, 160);
        try
        {
            var brush = chip.Background as ISolidColorBrush;
            Assert.That(brush, Is.Not.Null);
            Assert.That(brush!.Color.A, Is.LessThan((byte)255));
            Assert.That(chip.BorderBrush, Is.InstanceOf<IBrush>());
        }
        finally
        {
            window.Close();
        }
    }
}
