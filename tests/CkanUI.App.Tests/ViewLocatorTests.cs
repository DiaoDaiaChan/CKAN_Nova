using CkanUI.App;
using CkanUI.Services.Abstractions;
using CkanUI.ViewModels;
using CkanUI.ViewModels.Pages;
using NUnit.Framework;

namespace CkanUI.App.Tests;

/// <summary>
/// ViewLocator 映射测试（ARC-03）：页面 VM 必须解析到真实视图类型，
/// 否则导航会静默落到占位视图——这类回归在 GUI 冒烟里很难发现，所以在这里钉死。
/// </summary>
[TestFixture]
public sealed class ViewLocatorTests
{
    [Test]
    public void PageViewModels_ResolveToConcreteViews()
    {
        Assert.That(ViewLocator.ResolveViewType(typeof(ModsPageViewModel))?.FullName, Is.EqualTo("CkanUI.App.Views.ModsPageView"));
        Assert.That(ViewLocator.ResolveViewType(typeof(ChangesPageViewModel))?.FullName, Is.EqualTo("CkanUI.App.Views.ChangesPageView"));
        Assert.That(ViewLocator.ResolveViewType(typeof(ContentPageViewModel))?.FullName, Is.EqualTo("CkanUI.App.Views.ContentPageView"));
        Assert.That(ViewLocator.ResolveViewType(typeof(ReposPageViewModel))?.FullName, Is.EqualTo("CkanUI.App.Views.ReposPageView"));
        Assert.That(ViewLocator.ResolveViewType(typeof(ModPacksPageViewModel))?.FullName, Is.EqualTo("CkanUI.App.Views.ModPacksPageView"));
        Assert.That(ViewLocator.ResolveViewType(typeof(ModPackEditorPageViewModel))?.FullName, Is.EqualTo("CkanUI.App.Views.ModPackEditorPageView"));
        Assert.That(ViewLocator.ResolveViewType(typeof(InstancesPageViewModel))?.FullName, Is.EqualTo("CkanUI.App.Views.InstancesPageView"));
        Assert.That(ViewLocator.ResolveViewType(typeof(SettingsPageViewModel))?.FullName, Is.EqualTo("CkanUI.App.Views.SettingsPageView"));
        Assert.That(ViewLocator.ResolveViewType(typeof(SavesPageViewModel))?.FullName, Is.EqualTo("CkanUI.App.Views.SavesPageView"));
        Assert.That(ViewLocator.ResolveViewType(typeof(UnmanagedPageViewModel))?.FullName, Is.EqualTo("CkanUI.App.Views.UnmanagedPageView"));
        Assert.That(ViewLocator.ResolveViewType(typeof(CachePageViewModel))?.FullName, Is.EqualTo("CkanUI.App.Views.CachePageView"));
        Assert.That(ViewLocator.ResolveViewType(typeof(PlayTimePageViewModel))?.FullName, Is.EqualTo("CkanUI.App.Views.PlayTimePageView"));
        Assert.That(ViewLocator.ResolveViewType(typeof(DedupPageViewModel))?.FullName, Is.EqualTo("CkanUI.App.Views.DedupPageView"));
        Assert.That(ViewLocator.ResolveViewType(typeof(InstallHistoryPageViewModel))?.FullName, Is.EqualTo("CkanUI.App.Views.InstallHistoryPageView"));
        Assert.That(ViewLocator.ResolveViewType(typeof(PluginsPageViewModel))?.FullName, Is.EqualTo("CkanUI.App.Views.PluginsPageView"));
        Assert.That(ViewLocator.ResolveViewType(typeof(HomePageViewModel))?.FullName, Is.EqualTo("CkanUI.App.Views.HomePageView"));

        Assert.That(
            ViewLocator.ResolveViewType(typeof(ModConfigPageViewModel))?.FullName,
            Is.EqualTo("CkanUI.App.Views.ModConfigPageView"));
        // 日志页在 Views/Logs 与 ViewModels/Pages/Logs 两个子命名空间下，解析规则只换 ".ViewModels.Pages." 段，
        // 子命名空间原样保留——这条断言就是钉住这一点（写错会静默回退到占位视图）。
        Assert.That(
            ViewLocator.ResolveViewType(typeof(CkanUI.ViewModels.Pages.Logs.LogPageViewModel))?.FullName,
            Is.EqualTo("CkanUI.App.Views.Logs.LogPageView"));
    }

    [Test]
    public void UnimplementedPage_ResolvesToNullSoTheLocatorFallsBack()
    {
        // 没有对应视图的页面必须返回 null，Build 才会回退 PagePlaceholderView。
        Assert.That(ViewLocator.ResolveViewType(typeof(PlaceholderPageViewModel)), Is.Null);
    }

    [Test]
    public void Match_AcceptsBuiltInAndPluginPages()
    {
        var locator = new ViewLocator();

        Assert.That(locator.Match(new PlaceholderPageViewModel()), Is.True);
        Assert.That(locator.Match(new DemoPluginPageViewModel()), Is.True);
        Assert.That(locator.Match(new object()), Is.False);
        Assert.That(locator.Match(null), Is.False);
    }

    [Test]
    public void PluginPage_ResolvesViewFromItsOwnAssembly()
    {
        // 插件页视图与视图模型同程序集同命名空间：默认上下文按名字查不到，只能在本程序集里找。
        Assert.That(
            ViewLocator.ResolveViewType(typeof(DemoPluginPageViewModel))?.FullName,
            Is.EqualTo(typeof(DemoPluginPageView).FullName));
    }

    [Test]
    public void PluginPage_WithoutView_ResolvesToNullSoTheLocatorFallsBack()
    {
        Assert.That(ViewLocator.ResolveViewType(typeof(OrphanPluginPageViewModel)), Is.Null);
    }

    private sealed class PlaceholderPageViewModel : PageViewModel
    {
        public override string Title => "占位";
    }

    /// <summary>插件页视图模型替身：只实现契约层的标记接口。</summary>
    private sealed class DemoPluginPageViewModel : IPluginPage
    {
    }

    /// <summary>与视图模型配对的自述视图（真实插件里应是 Avalonia 控件）。</summary>
    private sealed class DemoPluginPageView
    {
    }

    /// <summary>没有配对视图的插件页。</summary>
    private sealed class OrphanPluginPageViewModel : IPluginPage
    {
    }
}
