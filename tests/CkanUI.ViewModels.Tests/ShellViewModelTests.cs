using System.Threading.Tasks;
using CkanUI.ViewModels;
using CkanUI.ViewModels.Navigation;
using CkanUI.ViewModels.Pages;
using NUnit.Framework;

namespace CkanUI.ViewModels.Tests;

[TestFixture]
public sealed class ShellViewModelTests
{
    [Test]
    public void Defaults_AreCorrect()
    {
        var vm = new ShellViewModel();
        Assert.That(vm.StatusText, Is.EqualTo("就绪"));
        Assert.That(vm.Title, Is.EqualTo("CKAN UI"));
        Assert.That(vm.CurrentPage, Is.Null);
    }

    [Test]
    public void NavItems_HasThirteenConfiguredItems()
    {
        var vm = new ShellViewModel();
        Assert.That(vm.NavItems.Count, Is.EqualTo(13));
        Assert.That(vm.NavItems[0].Key, Is.EqualTo("mods"));
        Assert.That(vm.NavItems[1].Key, Is.EqualTo("changes"));
        Assert.That(vm.NavItems[2].Key, Is.EqualTo("repos"));
        Assert.That(vm.NavItems[3].Key, Is.EqualTo("modpacks"));
        Assert.That(vm.NavItems[4].Key, Is.EqualTo("instances"));
        Assert.That(vm.NavItems[5].Key, Is.EqualTo("playtime"));
        Assert.That(vm.NavItems[6].Key, Is.EqualTo("saves"));
        Assert.That(vm.NavItems[7].Key, Is.EqualTo("unmanaged"));
        Assert.That(vm.NavItems[8].Key, Is.EqualTo("cache"));
        Assert.That(vm.NavItems[9].Key, Is.EqualTo("dedup"));
        Assert.That(vm.NavItems[10].Key, Is.EqualTo("installhistory"));
        Assert.That(vm.NavItems[11].Key, Is.EqualTo("plugins"));
        Assert.That(vm.NavItems[12].Key, Is.EqualTo("settings"));
    }

    [Test]
    public void NavItems_TitlesComeFromTranslationDelegate()
    {
        var vm = new ShellViewModel(new NavigationService(_ => null), key => "T:" + key);
        Assert.That(vm.NavItems[1].Title, Is.EqualTo("T:Ui_NavRail_Changes"));
        Assert.That(vm.NavItems[3].Title, Is.EqualTo("T:Ui_NavRail_ModPacks"));
        Assert.That(vm.NavItems[8].Title, Is.EqualTo("T:Ui_NavRail_Cache"));
        Assert.That(vm.NavItems[5].Title, Is.EqualTo("T:Ui_NavRail_PlayTime"));
        Assert.That(vm.NavItems[9].Title, Is.EqualTo("T:Ui_NavRail_Dedup"));
        Assert.That(vm.NavItems[10].Title, Is.EqualTo("T:Ui_NavRail_InstallHistory"));
        Assert.That(vm.NavItems[11].Title, Is.EqualTo("T:Ui_NavRail_Plugins"));
        Assert.That(vm.NavItems.Count, Is.EqualTo(13));
    }

    [Test]
    public async Task NavigateCommand_UpdatesCurrentPage()
    {
        var modsPage = new ModsPageViewModel();
        var instancesPage = new InstancesPageViewModel();

        var navService = new NavigationService(key => key switch
        {
            "mods" => modsPage,
            "instances" => instancesPage,
            _ => null
        });

        var vm = new ShellViewModel(navService);
        Assert.That(vm.CurrentPage, Is.Null);

        // 导航到 mods
        await vm.NavigateCommand.ExecuteAsync("mods");
        Assert.That(vm.CurrentPage, Is.SameAs(modsPage));
        Assert.That(modsPage.IsActive, Is.True);

        // 导航到 instances
        await vm.NavigateCommand.ExecuteAsync("instances");
        Assert.That(vm.CurrentPage, Is.SameAs(instancesPage));
        Assert.That(instancesPage.IsActive, Is.True);
        Assert.That(modsPage.IsActive, Is.False);
    }
}
