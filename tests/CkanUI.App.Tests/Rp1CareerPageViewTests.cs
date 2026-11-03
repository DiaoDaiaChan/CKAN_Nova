using Avalonia;
using CkanUI.App.Tests.Automation;
using CkanUI.Plugin.Rp1.UI;
using NUnit.Framework;

namespace CkanUI.App.Tests;

/// <summary>
/// RP-1 生涯日志页视图的加载冒烟：XAML 能解析、DataContext 能落到视图模型、布局能算出一个正尺寸。
/// </summary>
[TestFixture]
[NonParallelizable]
public sealed class Rp1CareerPageViewTests
{
    [SetUp]
    public void SetUp() => HeadlessAppHost.EnsureInitialized();

    [Test]
    public void 页面视图能加载XAML并绑定视图模型()
    {
        using var viewModel = Rp1CareerPageViewModel.CreateForTests(career: null, instances: null);
        var view = new Rp1CareerPageView { DataContext = viewModel };

        view.Measure(new Size(1280, 800));
        view.Arrange(new Rect(0, 0, 1280, 800));

        Assert.Multiple(() =>
        {
            Assert.That(view.DataContext, Is.SameAs(viewModel));
            Assert.That(view.Bounds.Width, Is.GreaterThan(0));
            Assert.That(view.Bounds.Height, Is.GreaterThan(0));
        });
    }
}
