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

        // 内置 17 条 + 插件 1 条：模组翻译改由插件提供后，这里只剩「插件页签插在 plugins 之后」这一条规则要守。
        Assert.That(shell.NavItems, Has.Count.EqualTo(18));
        Assert.That(shell.NavItems[13].Key, Is.EqualTo("plugins"));
        Assert.That(shell.NavItems[14].Key, Is.EqualTo("hello-page"));
        Assert.That(shell.NavItems[14].Title, Is.EqualTo("Hello 页"));
        Assert.That(shell.NavItems[15].Key, Is.EqualTo("logs"));
        Assert.That(shell.NavItems[16].Key, Is.EqualTo("settings"));
        Assert.That(shell.NavItems[17].Key, Is.EqualTo("home"));
    }

    [Test]
    public void Collect_PassesThroughTitleKey_SoFirstPartyPluginsKeepLocalizedTitles()
    {
        var plugin = Plugin("ModText", new PluginPageDescriptor(
            "modtext",
            "模组翻译",
            "CkanUI.Plugin.ModText.ModTextPageViewModel",
            "Ui_NavRail_ModText"));

        var entries = PluginPageCatalog.Collect(new[] { plugin }, Array.Empty<string>());

        Assert.That(entries, Has.Count.EqualTo(1));
        Assert.That(entries[0].TitleKey, Is.EqualTo("Ui_NavRail_ModText"));
    }

    [Test]
    public void Shell_PrefersTitleKey_AndFallsBackToThePluginTitleWhenTheKeyIsUnknown()
    {
        var plugins = new FakePluginService();
        plugins.ActiveItems.Add(Plugin("ModText", new PluginPageDescriptor(
            "modtext",
            "模组翻译",
            "CkanUI.Plugin.ModText.ModTextPageViewModel",
            "Ui_NavRail_ModText")));

        // 翻译委托把键原样回键名 = 语言包里没有这条键：导航标题必须退回插件自述标题，不能显示一串键名。
        var shell = new ShellViewModel(new NavigationService(_ => null), key => "T:" + key, plugins: plugins);
        var item = shell.NavItems.Single(nav => nav.Key == "modtext");
        Assert.That(item.Title, Is.EqualTo("T:Ui_NavRail_ModText"));

        var unknownKey = new FakePluginService();
        unknownKey.ActiveItems.Add(Plugin("Broken", new PluginPageDescriptor(
            "broken-page",
            "坏键页",
            "CkanUI.ViewModels.SomePageViewModel",
            "Ui_No_Such_Key")));
        var shell2 = new ShellViewModel(new NavigationService(_ => null), key => key, plugins: unknownKey);
        Assert.That(shell2.NavItems.Single(nav => nav.Key == "broken-page").Title, Is.EqualTo("坏键页"));
    }

    [Test]
    public void Collect_PassesThroughGlyph_SoEachPluginCanPickItsOwnIcon()
    {
        var plugins = new FakePluginService();
        plugins.ActiveItems.Add(Plugin("Perf", new PluginPageDescriptor(
            "performance",
            "性能",
            "CkanUI.Plugin.Performance.PerformancePageViewModel",
            Glyph: "Performance")));

        var entries = PluginPageCatalog.Collect(plugins.Active, Array.Empty<string>());
        Assert.That(entries[0].Glyph, Is.EqualTo("Performance"));

        // 契约第四位是 TitleKey，图标必须靠命名参数传——这条用例顺手钉住顺序，防止有人把它写成第五个位置参数后串位。
        var shell = new ShellViewModel(new NavigationService(_ => null), key => "T:" + key, plugins: plugins);
        Assert.That(shell.NavItems.Single(nav => nav.Key == "performance").Glyph, Is.EqualTo("Performance"));
    }

    [Test]
    public void Shell_FallsBackToTheGenericPluginGlyph_WhenThePluginDeclaresNone()
    {
        var plugins = new FakePluginService();
        plugins.ActiveItems.Add(Plugin("Plain", Page("plain-page")));

        var shell = new ShellViewModel(new NavigationService(_ => null), key => "T:" + key, plugins: plugins);

        Assert.That(shell.NavItems.Single(nav => nav.Key == "plain-page").Glyph, Is.EqualTo("Puzzle"));
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

        // 插件页卸掉后回到纯内置的 17 条。
        Assert.That(shell.NavItems, Has.Count.EqualTo(17));
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
    public void Shell_WithoutPluginService_HasSeventeenItems()
    {
        var shell = new ShellViewModel(new NavigationService(_ => null), key => "T:" + key);

        Assert.That(shell.NavItems, Has.Count.EqualTo(17));
    }

    [Test]
    public void Shell_EmbedsSectionDeclaredPagesInsideThatSection_StillTracksThemInNavItems()
    {
        var plugins = new FakePluginService();
        plugins.ActiveItems.Add(Plugin(
            "Assets",
            new PluginPageDescriptor(
                "example-flag-brush",
                "旗帜笔刷",
                "CkanUI.ViewModels.SomePageViewModel",
                Section: "instances",
                Order: 2),
            new PluginPageDescriptor(
                "example-save-browser",
                "存档浏览器",
                "CkanUI.ViewModels.SomePageViewModel",
                Section: "instances",
                Order: 1)));

        var shell = new ShellViewModel(new NavigationService(_ => null), key => "T:" + key, plugins: plugins);

        var keys = shell.Sections.Single(section => section.Key == "instances").Tabs.Select(tab => tab.Key).ToList();
        Assert.That(keys[^2], Is.EqualTo("example-save-browser"), "同一分节里 Order 小的排前面");
        Assert.That(keys[^1], Is.EqualTo("example-flag-brush"));
        Assert.That(shell.RailItems.Any(item => item.Key == "example-save-browser"), Is.False, "分节页不该再占侧栏入口");
        Assert.That(shell.NavItems.Any(item => item.Key == "example-save-browser"), Is.True, "选中态按 NavItems 找自己");
    }

    [Test]
    public void Shell_EmbedsToolsSectionPagesWithoutGrowingTheRail()
    {
        var withoutPlugin = new ShellViewModel(new NavigationService(_ => null), key => "T:" + key);
        var railCountWithoutPlugin = withoutPlugin.RailItems.Count;

        var plugins = new FakePluginService();
        plugins.ActiveItems.Add(Plugin(
            "Music",
            new PluginPageDescriptor(
                "music",
                "音乐",
                "CkanUI.ViewModels.SomePageViewModel",
                Glyph: "Music",
                Section: "plugins",
                Order: 20)));

        var shell = new ShellViewModel(new NavigationService(_ => null), key => "T:" + key, plugins: plugins);

        var tools = shell.Sections.Single(section => section.Key == "plugins");
        Assert.That(tools.Tabs.Select(tab => tab.Key), Does.Contain("music"));
        Assert.That(tools.Tabs[^1].Key, Is.EqualTo("music"), "槽位之外的分节页按 Order 追加在末尾");
        Assert.That(shell.RailItems.Count, Is.EqualTo(railCountWithoutPlugin), "工具分节多一个页签不该多出一个侧栏入口");
        Assert.That(shell.RailItems.Any(item => item.Key == "music"), Is.False);
        Assert.That(shell.NavItems.Any(item => item.Key == "music"), Is.True, "选中态按 NavItems 找自己");
    }

    [Test]
    public void Shell_UnknownSection_FallsBackToRailEntry_InsteadOfDisappearing()
    {
        var plugins = new FakePluginService();
        plugins.ActiveItems.Add(Plugin("Odd", new PluginPageDescriptor(
            "odd-section-page",
            "野页",
            "CkanUI.ViewModels.SomePageViewModel",
            Section: "no-such-section")));

        var shell = new ShellViewModel(new NavigationService(_ => null), key => "T:" + key, plugins: plugins);

        Assert.That(shell.RailItems.Any(item => item.Key == "odd-section-page"), Is.True);
        Assert.That(shell.Sections.SelectMany(section => section.Tabs).Any(tab => tab.Key == "odd-section-page"), Is.False);
    }

    [Test]
    public async Task Shell_DeactivatingSectionPagePlugin_RefreshesTabsAndFallsBackToTheSectionHome()
    {
        var plugins = new FakePluginService();
        plugins.ActiveItems.Add(Plugin("Saves", new PluginPageDescriptor(
            "example-saves",
            "存档",
            "CkanUI.ViewModels.SomePageViewModel",
            Section: "instances")));
        // 回退目标「instances」也必须有页面，否则导航服务会按「找不到目标页」拒绝跳转（真实宿主两页都在）。
        var navigation = new NavigationService(key => key is "example-saves" or "instances" ? new object() : null);
        var shell = new ShellViewModel(navigation, key => "T:" + key, plugins: plugins);

        await shell.NavigateCommand.ExecuteAsync("example-saves");
        Assert.That(shell.SectionTabs.Any(tab => tab.Key == "example-saves"), Is.True);

        plugins.ActiveItems.Clear();
        plugins.RaiseChanged();

        Assert.That(shell.SectionTabs.Any(tab => tab.Key == "example-saves"), Is.False, "停用后页签条不能停在死页签上");
        Assert.That(navigation.CurrentKey, Is.EqualTo("instances"), "停用后要退回分节首页，不是插件管理页");
    }

    [Test]
    public void Shell_TogglingSectionEmbeddedPlugin_DoesNotTouchTheRail()
    {
        // 外壳（MainWindow.RebuildNavigation）按 RailItems 逐项销毁重建整套侧栏菜单，
        // 所以「激活一个分节内嵌的插件」不该让侧栏集合发出任何一次变更通知——
        // 一次 Clear 加 N 次 Add 会让 8~10 次全量重建排在同一个调度回合里，界面就卡住了。
        var plugins = new FakePluginService();
        var shell = new ShellViewModel(new NavigationService(_ => null), key => "T:" + key, plugins: plugins);
        var railEvents = 0;
        shell.RailItems.CollectionChanged += (_, _) => railEvents++;

        plugins.ActiveItems.Add(Plugin("Saves", new PluginPageDescriptor(
            "example-saves",
            "存档",
            "CkanUI.ViewModels.SomePageViewModel",
            Section: "instances")));
        plugins.RaiseChanged();

        // 看 Sections 而不是 SectionTabs：后者只反映「当前所在分节」，而这里没导航进实例页。
        var instances = shell.Sections.Single(section => section.Key == "instances");
        Assert.That(instances.Tabs.Any(tab => tab.Key == "example-saves"), Is.True, "页签条要收到这一页");
        Assert.That(railEvents, Is.Zero, "分节内嵌的插件页只改页签条，侧栏一项都不该动");

        plugins.ActiveItems.Clear();
        plugins.RaiseChanged();

        Assert.That(railEvents, Is.Zero, "停用分节内嵌插件同样不该动侧栏");
    }

    [Test]
    public void Shell_TogglingRailPlugin_TouchesTheRailExactlyOncePerToggle()
    {
        // 侧栏入口的插件（没声明分节）确实要增删一项，但只能是最小代价的一次变更。
        var plugins = new FakePluginService();
        var shell = new ShellViewModel(new NavigationService(_ => null), key => "T:" + key, plugins: plugins);
        var added = 0;
        var removed = 0;
        shell.RailItems.CollectionChanged += (_, args) =>
        {
            added += args.NewItems?.Count ?? 0;
            removed += args.OldItems?.Count ?? 0;
        };

        plugins.ActiveItems.Add(Plugin("Hello", Page("hello-page", "Hello 页")));
        plugins.RaiseChanged();

        Assert.That(shell.RailItems.Any(item => item.Key == "hello-page"), Is.True);
        Assert.That(added, Is.EqualTo(1), "新增一个侧栏入口就是一次 Add，不能靠清空整表再重填");
        Assert.That(removed, Is.Zero, "新增不该顺手删掉别的侧栏项");

        added = 0;
        plugins.ActiveItems.Clear();
        plugins.RaiseChanged();

        Assert.That(shell.RailItems.Any(item => item.Key == "hello-page"), Is.False);
        Assert.That(removed, Is.EqualTo(1), "去掉一个侧栏入口就该只删这一项");
        Assert.That(added, Is.Zero, "删除不该重建整张侧栏表");
    }

    [Test]
    public void Shell_IndependentPluginPages_AppearAsRailItems_AndNotInToolsSectionTabs()
    {
        var plugins = new FakePluginService();
        plugins.ActiveItems.Add(Plugin("Ai", new PluginPageDescriptor("ai-assistant", "AI 助手", "VM.Ai", Glyph: "Sparkle")));
        plugins.ActiveItems.Add(Plugin("Music", new PluginPageDescriptor("music", "音乐", "VM.Music", Glyph: "Music")));
        plugins.ActiveItems.Add(Plugin("Kos", new PluginPageDescriptor("kos", "kOS 脚本", "VM.Kos", Glyph: "Code")));
        plugins.ActiveItems.Add(Plugin("Rp1", new PluginPageDescriptor("rp1-career", "RP-1 生涯日志", "VM.Rp1", Glyph: "History")));

        var shell = new ShellViewModel(new NavigationService(_ => null), key => "T:" + key, plugins: plugins);

        var tools = shell.Sections.Single(section => section.Key == "plugins");
        Assert.That(tools.Tabs.Select(tab => tab.Key), Is.EqualTo(new[] { "plugins" }), "工具分节下不应包含这些独立插件页");

        var railKeys = shell.RailItems.Select(item => item.Key).ToList();
        Assert.That(railKeys, Does.Contain("ai-assistant"));
        Assert.That(railKeys, Does.Contain("music"));
        Assert.That(railKeys, Does.Contain("kos"));
        Assert.That(railKeys, Does.Contain("rp1-career"));
    }
}
