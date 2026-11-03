using System.Linq;
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
    public void NavItems_HasTwentyThreeConfiguredItems()
    {
        var vm = new ShellViewModel();
        Assert.That(vm.NavItems.Count, Is.EqualTo(23));
        Assert.That(vm.NavItems[0].Key, Is.EqualTo("mods"));
        Assert.That(vm.NavItems[1].Key, Is.EqualTo("content"));
        Assert.That(vm.NavItems[2].Key, Is.EqualTo("changes"));
        Assert.That(vm.NavItems[3].Key, Is.EqualTo("repos"));
        Assert.That(vm.NavItems[4].Key, Is.EqualTo("modpacks"));
        Assert.That(vm.NavItems[5].Key, Is.EqualTo("modpack-editor"));
        Assert.That(vm.NavItems[6].Key, Is.EqualTo("instances"));
        Assert.That(vm.NavItems[7].Key, Is.EqualTo("playtime"));
        Assert.That(vm.NavItems[8].Key, Is.EqualTo("saves"));
        Assert.That(vm.NavItems[9].Key, Is.EqualTo("unmanaged"));
        Assert.That(vm.NavItems[10].Key, Is.EqualTo("cache"));
        Assert.That(vm.NavItems[11].Key, Is.EqualTo("downloadstats"));
        Assert.That(vm.NavItems[12].Key, Is.EqualTo("dedup"));
        Assert.That(vm.NavItems[13].Key, Is.EqualTo("installhistory"));
        Assert.That(vm.NavItems[14].Key, Is.EqualTo("plugins"));
        // 模组翻译已解耦为内置插件：插件页签在引导完成后插在「插件」之后，不再占内置下标。
        Assert.That(vm.NavItems[15].Key, Is.EqualTo("logs"));
        Assert.That(vm.NavItems[16].Key, Is.EqualTo("settings"));
        // Mod 配置页追加在末尾：插在中间会让上面全部下标位移，测试按位置断言导航顺序是刻意的。
        Assert.That(vm.NavItems[17].Key, Is.EqualTo("modconfig"));
        // 资产工作台（飞船/旗帜/加载图）同样追加在末尾，与存档同属「实例」分区。
        Assert.That(vm.NavItems[18].Key, Is.EqualTo("ships"));
        Assert.That(vm.NavItems[19].Key, Is.EqualTo("flags"));
        Assert.That(vm.NavItems[20].Key, Is.EqualTo("loadingimages"));
        // 主菜单同样追加在末尾；它在侧栏分区里排第一，所以 RailItems[0] 恒为主页。
        Assert.That(vm.NavItems[21].Key, Is.EqualTo("home"));
        // KSP 游戏原生设置页同样追加在末尾，挂在「实例」分区。
        Assert.That(vm.NavItems[22].Key, Is.EqualTo("kspconfig"));
    }

    [Test]
    public void RailItems_PutHomeFirst_SoStartupLandsOnMainMenu()
    {
        var vm = new ShellViewModel();

        Assert.That(vm.RailItems, Is.Not.Empty);
        Assert.That(vm.RailItems[0].Key, Is.EqualTo("home"), "启动页取 RailItems[0]，主菜单必须排第一。");
        // 主菜单独占一个分区：挂进「模组」就成了第 6 个页签，启动落地也就不再是主菜单。
        Assert.That(vm.Sections[0].Key, Is.EqualTo("home"));
        Assert.That(vm.Sections[0].Tabs.Select(tab => tab.Key), Is.EqualTo(new[] { "home" }));
        // 日志与设置由外壳放进侧栏底部区，不占主入口列表。
        Assert.That(vm.RailItems.Any(item => item.Key is "logs" or "settings"), Is.False);
    }

    [Test]
    public void NavItems_TitlesComeFromTranslationDelegate()
    {
        var vm = new ShellViewModel(new NavigationService(_ => null), key => "T:" + key);
        Assert.That(vm.NavItems[1].Title, Is.EqualTo("T:Ui_NavRail_Content"));
        Assert.That(vm.NavItems[2].Title, Is.EqualTo("T:Ui_NavRail_Changes"));
        Assert.That(vm.NavItems[4].Title, Is.EqualTo("T:Ui_NavRail_ModPacks"));
        Assert.That(vm.NavItems[5].Title, Is.EqualTo("T:Ui_NavRail_ModPackEditor"));
        Assert.That(vm.NavItems[7].Title, Is.EqualTo("T:Ui_NavRail_PlayTime"));
        Assert.That(vm.NavItems[10].Title, Is.EqualTo("T:Ui_NavRail_Cache"));
        Assert.That(vm.NavItems[11].Title, Is.EqualTo("T:Ui_NavRail_DownloadStats"));
        Assert.That(vm.NavItems[12].Title, Is.EqualTo("T:Ui_NavRail_Dedup"));
        Assert.That(vm.NavItems[13].Title, Is.EqualTo("T:Ui_NavRail_InstallHistory"));
        Assert.That(vm.NavItems[14].Title, Is.EqualTo("T:Ui_NavRail_Plugins"));
        Assert.That(vm.NavItems[15].Title, Is.EqualTo("T:Ui_NavRail_Logs"));
        Assert.That(vm.NavItems[16].Title, Is.EqualTo("T:Ui_NavRail_Settings"));
        Assert.That(vm.NavItems[17].Title, Is.EqualTo("T:Ui_NavRail_ModConfig"));
        Assert.That(vm.NavItems[18].Title, Is.EqualTo("T:Ui_NavRail_Ships"));
        Assert.That(vm.NavItems[19].Title, Is.EqualTo("T:Ui_NavRail_Flags"));
        Assert.That(vm.NavItems[20].Title, Is.EqualTo("T:Ui_NavRail_LoadingScreens"));
        Assert.That(vm.NavItems[21].Title, Is.EqualTo("T:Ui_NavRail_Home"));
        Assert.That(vm.NavItems[22].Title, Is.EqualTo("T:Ui_NavRail_KspConfig"));
        Assert.That(vm.NavItems.Count, Is.EqualTo(23));
    }

    [Test]
    public async Task ModText_IsNoLongerAHostPage_TheBuiltInPluginOwnsIt()
    {
        var navService = new NavigationService(key => key switch
        {
            "mods" => new ModsPageViewModel(),
            "plugins" => new PluginsPageViewModel(),
            _ => null
        });

        var vm = new ShellViewModel(navService);

        // 「模组」入口不再挂模组翻译页签：那排页签只留「同一对象的多个面」。
        await vm.NavigateCommand.ExecuteAsync("mods");
        Assert.That(vm.ActiveSection?.Key, Is.EqualTo("mods"));
        Assert.That(vm.SectionTabs.Any(tab => tab.Key == "modtext"), Is.False);

        // 模组翻译解耦为内置插件（com.ckanui.modtext）后，宿主不该再有它的导航项、侧栏入口和分区条目：
        // 否则插件被停用后，界面上还会留一个点进去空白的入口。
        Assert.That(vm.NavItems.Any(item => item.Key == "modtext"), Is.False, "宿主不该再声明模组翻译页签");
        Assert.That(vm.RailItems.Any(item => item.Key == "modtext"), Is.False, "没有插件时侧栏不该有模组翻译入口");
        Assert.That(vm.Sections.Any(section => section.Key == "modtext"), Is.False, "分区表也不能残留：插件页落在「扩展」分组");

        // 工具入口同样只剩插件一页。
        await vm.NavigateCommand.ExecuteAsync("plugins");
        Assert.That(vm.ActiveSection?.Key, Is.EqualTo("plugins"));
        Assert.That(vm.SectionTabs.Count, Is.EqualTo(1));
        Assert.That(vm.SectionTabs[0].Key, Is.EqualTo("plugins"));
        Assert.That(vm.HasSectionTabs, Is.False);
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

    [Test]
    public async Task SelectedNavItem_SynchronizesWithNavigation()
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

        // 默认未导航时为 null
        Assert.That(vm.SelectedNavItem, Is.Null);

        // 导航到 mods 后更新 SelectedNavItem
        await vm.NavigateCommand.ExecuteAsync("mods");
        Assert.That(vm.SelectedNavItem?.Key, Is.EqualTo("mods"));

        // 导航更新 SelectedNavItem
        await vm.NavigateCommand.ExecuteAsync("instances");
        Assert.That(vm.SelectedNavItem?.Key, Is.EqualTo("instances"));

        // 通过更改 SelectedNavItem 触发导航
        var modsItem = vm.NavItems.First(i => i.Key == "mods");
        vm.SelectedNavItem = modsItem;
        Assert.That(vm.CurrentPage, Is.SameAs(modsPage));
        Assert.That(modsPage.IsActive, Is.True);

        // 后退导航同步 SelectedNavItem
        await navService.NavigateAsync("instances");
        Assert.That(vm.SelectedNavItem?.Key, Is.EqualTo("instances"));
        await vm.BackCommand.ExecuteAsync(null);
        Assert.That(vm.SelectedNavItem?.Key, Is.EqualTo("mods"));
    }

    [Test]
    public void InstanceService_UpdatesShellTitleOnCurrentChanged()
    {
        var instances = new FakeInstanceService();
        instances.Current = new CkanUI.Services.Abstractions.Dtos.InstanceInfo("KSP 1.12", "C:\\KSP", "1.12.5", true, false, 42);

        var vm = new ShellViewModel(new NavigationService(_ => null), instances: instances);
        Assert.That(vm.Title, Is.EqualTo("CKAN UI - KSP 1.12 (1.12.5)"));

        instances.RaiseCurrentChanged(new CkanUI.Services.Abstractions.Dtos.InstanceInfo("KSP RSS", "C:\\KSP_RSS", "1.12.3", false, false, 150));
        Assert.That(vm.Title, Is.EqualTo("CKAN UI - KSP RSS (1.12.3)"));

        instances.RaiseCurrentChanged(null);
        Assert.That(vm.Title, Is.EqualTo("CKAN UI"));
    }
}
