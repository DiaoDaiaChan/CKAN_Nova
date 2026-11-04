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
        Assert.That(ViewLocator.ResolveViewType(typeof(CkanUI.Plugin.Saves.SavesPageViewModel))?.FullName, Is.EqualTo("CkanUI.Plugin.Saves.SavesPageView"));
        Assert.That(ViewLocator.ResolveViewType(typeof(UnmanagedPageViewModel))?.FullName, Is.EqualTo("CkanUI.App.Views.UnmanagedPageView"));
        Assert.That(ViewLocator.ResolveViewType(typeof(CachePageViewModel))?.FullName, Is.EqualTo("CkanUI.App.Views.CachePageView"));
        Assert.That(ViewLocator.ResolveViewType(typeof(PlayTimePageViewModel))?.FullName, Is.EqualTo("CkanUI.App.Views.PlayTimePageView"));
        Assert.That(ViewLocator.ResolveViewType(typeof(DedupPageViewModel))?.FullName, Is.EqualTo("CkanUI.App.Views.DedupPageView"));
        Assert.That(ViewLocator.ResolveViewType(typeof(InstallHistoryPageViewModel))?.FullName, Is.EqualTo("CkanUI.App.Views.InstallHistoryPageView"));
        Assert.That(ViewLocator.ResolveViewType(typeof(PluginsPageViewModel))?.FullName, Is.EqualTo("CkanUI.App.Views.PluginsPageView"));
        Assert.That(ViewLocator.ResolveViewType(typeof(HomePageViewModel))?.FullName, Is.EqualTo("CkanUI.App.Views.HomePageView"));

        // Mod 配置页已搬进插件：ViewModel→View 按「同程序集同命名空间」解析（插件页回退分支）。
        Assert.That(
            ViewLocator.ResolveViewType(typeof(CkanUI.Plugin.ModConfig.ModConfigPageViewModel))?.FullName,
            Is.EqualTo("CkanUI.Plugin.ModConfig.ModConfigPageView"));
        // 音乐插件的页面在 UI 子命名空间下：解析只换末尾的 ViewModel→View，前缀原样保留。
        Assert.That(
            ViewLocator.ResolveViewType(typeof(CkanUI.Plugin.Soundtrack.UI.SoundtrackPageViewModel))?.FullName,
            Is.EqualTo("CkanUI.Plugin.Soundtrack.UI.SoundtrackPageView"));
        // kOS 脚本插件用同一形态：页面在插件内的 UI 子命名空间，末尾 ViewModel→View。
        Assert.That(
            ViewLocator.ResolveViewType(typeof(CkanUI.Plugin.Kos.UI.KosScriptPageViewModel))?.FullName,
            Is.EqualTo("CkanUI.Plugin.Kos.UI.KosScriptPageView"));
        // RP-1 生涯日志插件同样把页面放在 UI 子命名空间。
        Assert.That(
            ViewLocator.ResolveViewType(typeof(CkanUI.Plugin.Rp1.UI.Rp1CareerPageViewModel))?.FullName,
            Is.EqualTo("CkanUI.Plugin.Rp1.UI.Rp1CareerPageView"));
        // 存储管理与整合包分享也一样：视图就在插件自己的程序集里，宿主不认类型。
        Assert.That(
            ViewLocator.ResolveViewType(typeof(CkanUI.Plugin.Storage.UI.StoragePageViewModel))?.FullName,
            Is.EqualTo("CkanUI.Plugin.Storage.UI.StoragePageView"));
        Assert.That(
            ViewLocator.ResolveViewType(typeof(CkanUI.Plugin.ModPackShare.UI.ModPackSharePageViewModel))?.FullName,
            Is.EqualTo("CkanUI.Plugin.ModPackShare.UI.ModPackSharePageView"));
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
