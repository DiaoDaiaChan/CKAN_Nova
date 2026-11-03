using CkanUI.Services.Abstractions.Dtos;
using CkanUI.ViewModels;
using CkanUI.ViewModels.Navigation;
using NUnit.Framework;

namespace CkanUI.ViewModels.Tests;

/// <summary>
/// 插件页签（12 篇 F-17）：契约筛选规则与外壳导航栏接线。
/// </summary>
/// <remarks>
/// 契约层只给字符串，宿主必须在「哪些页签能用」这一步做完全部过滤，
/// 否则用户看到的是点了没反应的页签，或者内置页签被插件顶掉。
/// </remarks>
[TestFixture]
public sealed class PluginPageCatalogTests
{
    private static readonly string[] ReservedKeys = { "mods", "plugins", "settings" };

    private static PluginInfo Plugin(string name, params PluginPageDescriptor[] pages) =>
        new(
            "com.example." + name.ToLowerInvariant(),
            name,
            "1.0",
            "tester",
            string.Empty,
            string.Empty,
            @"C:\CKAN\plugins\" + name + ".dll",
            PluginState.Active)
        {
            Pages = pages,
        };

    private static PluginPageDescriptor Page(string key, string title = "示例页", string typeName = "CkanUI.ViewModels.SomePageViewModel") =>
        new(key, title, typeName);

    [Test]
    public void Collect_EmptyInput_ReturnsEmpty()
    {
        Assert.That(PluginPageCatalog.Collect(null, ReservedKeys), Is.Empty);
        Assert.That(PluginPageCatalog.Collect(Array.Empty<PluginInfo>(), null), Is.Empty);
    }

    [Test]
    public void Collect_KeepsActivePluginPagesInDeclarationOrder()
    {
        var plugin = Plugin("Hello", Page("hello-one", "第一页"), Page("hello-two", "第二页"));

        var entries = PluginPageCatalog.Collect(new[] { plugin }, ReservedKeys);

        Assert.That(entries.Select(entry => entry.Key), Is.EqualTo(new[] { "hello-one", "hello-two" }));
        Assert.That(entries[0].Title, Is.EqualTo("第一页"));
        Assert.That(entries[0].PluginName, Is.EqualTo("Hello - 1.0"));
        Assert.That(entries[0].ViewModelTypeName, Is.EqualTo("CkanUI.ViewModels.SomePageViewModel"));
    }

    [Test]
    public void Collect_SkipsDormantPlugins()
    {
        var dormant = Plugin("Dormant", Page("dormant-page")) with { State = PluginState.Dormant };

        Assert.That(PluginPageCatalog.Collect(new[] { dormant }, ReservedKeys), Is.Empty);
    }

    [Test]
    public void Collect_SkipsReservedKeysCaseInsensitively()
    {
        var plugin = Plugin("Hello", Page("Settings"), Page("hello-ok"));

        var entries = PluginPageCatalog.Collect(new[] { plugin }, ReservedKeys);

        Assert.That(entries.Select(entry => entry.Key), Is.EqualTo(new[] { "hello-ok" }));
    }

    [Test]
    public void Collect_SkipsDuplicateKeysAcrossPlugins_PreferringFirstName()
    {
        var alpha = Plugin("Alpha", Page("shared-page", "Alpha 的页"));
        var beta = Plugin("Beta", Page("shared-page", "Beta 的页"));

        var entries = PluginPageCatalog.Collect(new[] { beta, alpha }, ReservedKeys);

        Assert.That(entries, Has.Count.EqualTo(1));
        Assert.That(entries[0].Title, Is.EqualTo("Alpha 的页"));
    }

    [Test]
    public void Collect_SkipsIncompleteDescriptors()
    {
        var plugin = Plugin(
            "Hello",
            Page(" "),
            Page("no-title", string.Empty),
            Page("no-type", "有标题", string.Empty),
            Page(" hello-ok "));

        var entries = PluginPageCatalog.Collect(new[] { plugin }, ReservedKeys);

        Assert.That(entries.Select(entry => entry.Key), Is.EqualTo(new[] { "hello-ok" }));
    }

    [Test]
    public void Shell_AppendsActivePluginPages_AfterPluginsNavItem()
    {
        var plugins = new FakePluginService();
        plugins.ActiveItems.Add(Plugin("Hello", Page("hello-page", "Hello 页")));
        var shell = new ShellViewModel(new NavigationService(_ => null), key => "T:" + key, plugins: plugins);

        Assert.That(shell.NavItems, Has.Count.EqualTo(25));
        Assert.That(shell.NavItems[14].Key, Is.EqualTo("plugins"));
        Assert.That(shell.NavItems[15].Key, Is.EqualTo("hello-page"));
        Assert.That(shell.NavItems[15].Title, Is.EqualTo("Hello 页"));
        Assert.That(shell.NavItems[16].Key, Is.EqualTo("modtext"));
        Assert.That(shell.NavItems[17].Key, Is.EqualTo("logs"));
        Assert.That(shell.NavItems[18].Key, Is.EqualTo("settings"));
        Assert.That(shell.NavItems[19].Key, Is.EqualTo("modconfig"));
        Assert.That(shell.NavItems[20].Key, Is.EqualTo("ships"));
        Assert.That(shell.NavItems[21].Key, Is.EqualTo("flags"));
        Assert.That(shell.NavItems[22].Key, Is.EqualTo("loadingimages"));
        Assert.That(shell.NavItems[23].Key, Is.EqualTo("home"));
        Assert.That(shell.NavItems[24].Key, Is.EqualTo("kspconfig"));
    }

    [Test]
    public void Shell_DeactivatePlugin_RemovesNavItems_AndKeepsBaseInstances()
    {
        var plugins = new FakePluginService();
        plugins.ActiveItems.Add(Plugin("Hello", Page("hello-page", "Hello 页")));
        var shell = new ShellViewModel(new NavigationService(_ => null), key => "T:" + key, plugins: plugins);
        var settingsItem = shell.NavItems.Single(item => item.Key == "settings");

        plugins.ActiveItems.Clear();
        plugins.RaiseChanged();

        Assert.That(shell.NavItems, Has.Count.EqualTo(24));
        Assert.That(shell.NavItems.Any(item => item.Key == "hello-page"), Is.False);
        Assert.That(shell.NavItems.Single(item => item.Key == "settings"), Is.SameAs(settingsItem));
    }

    [Test]
    public void Shell_AddingPluginPage_KeepsCurrentSelectionInstance()
    {
        var plugins = new FakePluginService();
        var shell = new ShellViewModel(new NavigationService(_ => null), key => "T:" + key, plugins: plugins);
        var pluginsItem = shell.NavItems.Single(item => item.Key == "plugins");
        shell.SelectedNavItem = pluginsItem;

        plugins.ActiveItems.Add(Plugin("Hello", Page("hello-page", "Hello 页")));
        plugins.RaiseChanged();

        Assert.That(shell.SelectedNavItem, Is.SameAs(pluginsItem));
        Assert.That(shell.NavItems.Single(item => item.Key == "plugins"), Is.SameAs(pluginsItem));
    }

    [Test]
    public async Task Shell_NavigatesToPluginPage_ThroughFactory()
    {
        var plugins = new FakePluginService();
        plugins.ActiveItems.Add(Plugin("Hello", Page("hello-page", "Hello 页")));
        var page = new object();
        var navigation = new NavigationService(key => key == "hello-page" ? page : null);
        var shell = new ShellViewModel(navigation, key => "T:" + key, plugins: plugins);

        await shell.NavigateCommand.ExecuteAsync("hello-page");

        Assert.That(shell.CurrentPage, Is.SameAs(page));
        Assert.That(shell.SelectedNavItem?.Key, Is.EqualTo("hello-page"));
    }

    [Test]
    public void Shell_WithoutPluginService_HasTwentyThreeItems()
    {
        var shell = new ShellViewModel(new NavigationService(_ => null), key => "T:" + key);

        Assert.That(shell.NavItems, Has.Count.EqualTo(24));
    }
}
