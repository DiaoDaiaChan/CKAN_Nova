using CkanUI.Services.Abstractions;
using NUnit.Framework;

namespace CkanUI.Plugin.ModConfig.Tests;

/// <summary>
/// 插件描述符用例：页签键、分节、排序、字形与本地化键一旦写错，页签会掉出「模组」分节
/// 或换成通用插件图标——这类回归在 GUI 冒烟里只表现为「页签位置变了」，很难追，所以在这里钉死。
/// </summary>
[TestFixture]
public sealed class ModConfigPluginTests
{
    [Test]
    public void Plugin_DeclaresModConfigTabInsideModsSection()
    {
        ICkanUiPlugin plugin = new ModConfigPlugin();

        Assert.Multiple(() =>
        {
            Assert.That(plugin.Id, Is.EqualTo("com.ckanui.modconfig"));
            Assert.That(plugin.Name, Is.EqualTo("Mod 配置"));
            Assert.That(plugin.Version, Is.EqualTo("1.0.0"));
            Assert.That(plugin.Pages, Has.Count.EqualTo(1));
        });

        var page = plugin.Pages[0];
        Assert.Multiple(() =>
        {
            // 导航键与宿主原内置键一致：搬迁对用户与既有深链透明。
            Assert.That(page.Key, Is.EqualTo("modconfig"));
            Assert.That(page.Title, Is.EqualTo("Mod 配置"));
            Assert.That(page.ViewModelTypeName, Is.EqualTo(typeof(ModConfigPageViewModel).FullName));
            Assert.That(page.TitleKey, Is.EqualTo("Ui_NavRail_ModConfig"));
            // 字形必须是宿主字形表里已有的名字（NavGlyphToGeometryConverter），插件不新增字形。
            Assert.That(page.Glyph, Is.EqualTo("Sliders"));
            // 分节槽位：宿主 SectionSpecs 的 mods 槽位是 ["mods","content","changes","repos","modconfig"]，
            // 页签落在 repos 之后，位置与搬迁前一致。
            Assert.That(page.Section, Is.EqualTo("mods"));
            Assert.That(page.Order, Is.EqualTo(50));
        });
    }

    [Test]
    public void PageViewModel_ImplementsPluginPageMarker()
    {
        var vm = new ModConfigPageViewModel();

        Assert.That(vm, Is.InstanceOf<IPluginPage>());
        Assert.That(vm, Is.InstanceOf<CkanUI.ViewModels.PageViewModel>());
    }
}
