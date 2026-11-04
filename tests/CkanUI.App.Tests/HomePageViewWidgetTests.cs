using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.VisualTree;
using CkanUI.App.Tests.Automation;
using CkanUI.App.Views;
using CkanUI.Services.Abstractions.Home;
using CkanUI.ViewModels.Pages;
using CkanUI.ViewModels.Pages.Home;
using NUnit.Framework;

namespace CkanUI.App.Tests;

/// <summary>主页部件板的 XAML 冒烟：四套模板能落到可视树，空态与移除同步。</summary>
/// <remarks>刻意不 Show、不泵帧（§11.7）：这里只验模板解析与绑定，不驱动渲染队列。</remarks>
[TestFixture]
[NonParallelizable]
public sealed class HomePageViewWidgetTests
{
    [SetUp]
    public void SetUp() => HeadlessAppHost.EnsureInitialized();

    [Test]
    public void DefaultLayout_MaterializesThreeBuiltInWidgets()
    {
        var viewModel = new HomePageViewModel();
        viewModel.Activate();
        var view = Layout(viewModel);

        var host = view.FindControl<ItemsControl>("HomeWidgetsHost");
        var widgets = view.GetVisualDescendants().OfType<Border>()
            .Where(border => border.Classes.Contains("HomeWidget"))
            .ToArray();

        Assert.Multiple(() =>
        {
            Assert.That(host, Is.Not.Null);
            Assert.That(host!.ItemsSource!.Cast<object>().Count(), Is.EqualTo(3));
            Assert.That(widgets.Length, Is.GreaterThanOrEqualTo(3), "三块默认部件都要有实体卡片，不能只留数据。");
            Assert.That(view.FindControl<Border>("HomeWidgetEmptyState")!.IsVisible, Is.False);
        });
    }

    [Test]
    public void RemovingEveryWidget_ShowsTheEmptyState()
    {
        var viewModel = new HomePageViewModel();
        viewModel.Activate();
        var view = Layout(viewModel);

        viewModel.RemoveWidgetCommand.Execute(HomePageViewModel.InstanceWidgetId);
        viewModel.RemoveWidgetCommand.Execute(HomePageViewModel.QuickActionsWidgetId);
        viewModel.RemoveWidgetCommand.Execute(HomePageViewModel.ScreenshotsWidgetId);

        Assert.That(view.FindControl<Border>("HomeWidgetEmptyState")!.IsVisible, Is.True);
        Assert.That(viewModel.Widgets, Is.Empty);
    }

    [Test]
    public void PluginWidget_UsesItsOwnTemplate_AndShowsMetricsAndLines()
    {
        var viewModel = new HomePageViewModel();
        viewModel.Activate();
        var widget = new PluginHomeWidgetViewModel(
            viewModel,
            "com.example.rp1",
            "RP-1 示例",
            new PluginHomeWidgetDescriptor("career", "生涯日志", "desc", "History", "rp1-career"));
        widget.Apply(new PluginHomeWidgetSnapshot(
            "生涯日志",
            "DSC · RP-1 4.7.0.0",
            new[] { new PluginHomeWidgetMetric("当前日期", "1988-08-01") },
            new[] { new PluginHomeWidgetLine("第一次轨道飞行", "1961-04-12", "里程碑") },
            "存档更新于 2026-11-04 07:10"));
        viewModel.Widgets.Insert(0, widget);
        var view = Layout(viewModel);

        var texts = view.GetVisualDescendants().OfType<TextBlock>().Select(block => block.Text).ToArray();

        Assert.Multiple(() =>
        {
            Assert.That(texts, Does.Contain("1988-08-01"), "插件模板要画出统计值。");
            Assert.That(texts, Does.Contain("第一次轨道飞行"), "插件模板要画出行内容。");
            Assert.That(texts, Does.Contain("里程碑"), "徽标也要在。");
        });
    }

    [Test]
    public void FeedWidget_UsesItsOwnTemplate_AndShowsMetricsAndFooters()
    {
        var viewModel = new HomePageViewModel();
        viewModel.Activate();
        var widget = new FeedHomeWidgetViewModel(
            viewModel,
            HomePageViewModel.CacheWidgetId,
            "下载缓存",
            "Storage",
            "cache",
            _ => Task.FromResult(new HomeWidgetFeed(
                "已用 37.5%",
                new[] { new PluginHomeWidgetMetric("缓存条目", "12") },
                new[] { new PluginHomeWidgetLine("entry.zip", "1.5 KB", "可清理") },
                @"目录：D:\cache")));
        widget.Apply(new HomeWidgetFeed(
            "已用 37.5%",
            new[] { new PluginHomeWidgetMetric("缓存条目", "12") },
            new[] { new PluginHomeWidgetLine("entry.zip", "1.5 KB", "可清理") },
            @"目录：D:\cache"));
        viewModel.Widgets.Insert(0, widget);
        var view = Layout(viewModel);

        var texts = view.GetVisualDescendants().OfType<TextBlock>().Select(block => block.Text).ToArray();

        Assert.Multiple(() =>
        {
            Assert.That(texts, Does.Contain("缓存条目"));
            Assert.That(texts, Does.Contain("entry.zip"));
            Assert.That(texts, Does.Contain(@"目录：D:\cache"));
            Assert.That(widget.CanOpen, Is.True);
        });
    }

    private static HomePageView Layout(HomePageViewModel viewModel)
    {
        var view = new HomePageView { DataContext = viewModel };
        HeadlessAppHost.CreateTestWindow(view, 1280, 1200);
        return view;
    }
}
