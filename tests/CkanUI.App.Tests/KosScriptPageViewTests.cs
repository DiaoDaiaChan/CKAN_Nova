using Avalonia;
using CkanUI.App.Tests.Automation;
using CkanUI.Plugin.Kos.UI;
using NUnit.Framework;

namespace CkanUI.App.Tests;

/// <summary>
/// kOS 页视图的加载冒烟：XAML 能解析、DataContext 能落到视图模型、布局能算出一个正尺寸。
/// </summary>
/// <remarks>
/// 插件测试工程没有 Avalonia.Headless，视图只经过 XAML 编译；真正的 Avalonia 运行时解析
/// （样式类、DynamicResource、绑定路径）只有在这里才会跑到。用例刻意不 Show、不泵帧（§11.7 的坑）。
/// </remarks>
[TestFixture]
[NonParallelizable]
public sealed class KosScriptPageViewTests
{
    [SetUp]
    public void SetUp() => HeadlessAppHost.EnsureInitialized();

    [Test]
    public void 页面视图能加载XAML并绑定视图模型()
    {
        using var viewModel = KosScriptPageViewModel.CreateForTests(scripts: null, instances: null);
        var view = new KosScriptPageView { DataContext = viewModel };

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
