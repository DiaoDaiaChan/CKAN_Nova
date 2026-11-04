using Avalonia;
using CkanUI.App.Tests.Automation;
using CkanUI.Plugin.ModPackShare.UI;
using NUnit.Framework;

namespace CkanUI.App.Tests;

/// <summary>
/// 整合包配置页视图的加载冒烟：XAML 能解析、DataContext 能落到视图模型、布局能算出一个正尺寸。
/// </summary>
/// <remarks>
/// 插件测试工程没有 Avalonia.Headless，视图只经过 XAML 编译；真正的 Avalonia 运行时解析
/// （样式类 CkanCard、DynamicResource、绑定路径）只有在这里才会跑到。
/// 用例刻意不 Show、不泵帧（docs/design/plugins.md §11.7 记过一次挂死）。
/// </remarks>
[TestFixture]
[NonParallelizable]
public sealed class ModPackSharePageViewTests
{
    [SetUp]
    public void SetUp() => HeadlessAppHost.EnsureInitialized();

    [Test]
    public void 页面视图能加载XAML并绑定视图模型()
    {
        using var viewModel = ModPackSharePageViewModel.CreateForTests(profiles: null);
        var view = new ModPackSharePageView { DataContext = viewModel };

        view.Measure(new Size(1280, 800));
        view.Arrange(new Rect(0, 0, 1280, 800));

        Assert.Multiple(() =>
        {
            Assert.That(view.DataContext, Is.SameAs(viewModel));
            Assert.That(view.Bounds.Width, Is.GreaterThan(0));
            Assert.That(view.Bounds.Height, Is.GreaterThan(0));
            Assert.That(viewModel.EmptyTitle, Is.Not.Empty, "缺服务时也要有可读空态，而不是空白页");
        });
    }
}
