using CkanUI.App;
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
        Assert.That(ViewLocator.ResolveViewType(typeof(ReposPageViewModel))?.FullName, Is.EqualTo("CkanUI.App.Views.ReposPageView"));
        Assert.That(ViewLocator.ResolveViewType(typeof(ModPacksPageViewModel))?.FullName, Is.EqualTo("CkanUI.App.Views.ModPacksPageView"));
        Assert.That(ViewLocator.ResolveViewType(typeof(InstancesPageViewModel))?.FullName, Is.EqualTo("CkanUI.App.Views.InstancesPageView"));
        Assert.That(ViewLocator.ResolveViewType(typeof(SettingsPageViewModel))?.FullName, Is.EqualTo("CkanUI.App.Views.SettingsPageView"));
        Assert.That(ViewLocator.ResolveViewType(typeof(SavesPageViewModel))?.FullName, Is.EqualTo("CkanUI.App.Views.SavesPageView"));
        Assert.That(ViewLocator.ResolveViewType(typeof(UnmanagedPageViewModel))?.FullName, Is.EqualTo("CkanUI.App.Views.UnmanagedPageView"));
    }

    [Test]
    public void UnimplementedPage_ResolvesToNullSoTheLocatorFallsBack()
    {
        // 没有对应视图的页面必须返回 null，Build 才会回退 PagePlaceholderView。
        Assert.That(ViewLocator.ResolveViewType(typeof(PlaceholderPageViewModel)), Is.Null);
    }

    private sealed class PlaceholderPageViewModel : PageViewModel
    {
        public override string Title => "占位";
    }
}
