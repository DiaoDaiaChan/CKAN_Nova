using CkanUI.Services.Abstractions.Commands;
using CkanUI.Services.Commands;
using NUnit.Framework;

namespace CkanUI.ViewModels.Tests.CommandPalette;

/// <summary>
/// 内置命令表：16 个宿主内置导航页（顺序与左侧导航栏一致）+ 10 个界面动作 + Id 约定。
/// 搬进插件的页面（存档 / 飞船 / 旗帜 / 加载图 / KSP 游戏设置 / 模组翻译）不在这里——
/// 插件可停用，留条目就是留一个点了没反应的面板项。
/// </summary>
[TestFixture]
public sealed class DefaultCommandsTests
{
    private static readonly string[] ExpectedPageKeys =
    {
        "mods", "content", "changes", "repos", "modpacks", "modpack-editor", "instances",
        "playtime", "unmanaged", "cache", "downloadstats", "dedup",
        "installhistory", "plugins", "logs", "settings",
    };

    [Test]
    public void CreateNavigation_Returns16CommandsInShellOrder()
    {
        var commands = DefaultCommands.CreateNavigation();

        Assert.That(commands.Count, Is.EqualTo(ExpectedPageKeys.Length), "内置导航页数量与 ShellViewModel 一致");
        Assert.That(
            commands.Select(command => command.Id),
            Is.EqualTo(ExpectedPageKeys.Select(pageKey => "nav:" + pageKey)),
            "顺序必须与左侧导航栏一致，否则同一入口在两处位置不同");
    }

    [Test]
    public void CreateNavigation_WithoutTranslator_UsesChineseFallback()
    {
        var commands = DefaultCommands.CreateNavigation();

        Assert.That(commands[0].Title, Is.EqualTo("管理模组"), "没注入翻译时用中文兜底");
        Assert.That(commands[^1].Title, Is.EqualTo("设置"), "KSP 游戏设置与 Mod 配置都搬进了插件，末尾回到「设置」");
        Assert.That(commands[0].Category, Is.EqualTo("导航"));
        Assert.That(
            commands.All(command => !command.Title.StartsWith("Ui_", StringComparison.Ordinal)),
            Is.True,
            "绝不能把本地化键名当成标题显示给用户");
    }

    [Test]
    public void CreateNavigation_UsesTranslator()
    {
        var commands = DefaultCommands.CreateNavigation(key => "T:" + key);

        Assert.That(commands[0].Title, Is.EqualTo("T:Ui_NavRail_ManageMods"), "导航标题复用 Ui_NavRail_* 既有键");
        Assert.That(commands[0].Category, Is.EqualTo("T:" + DefaultCommands.CategoryNavigationKey));
    }

    [Test]
    public void CreateNavigation_EveryCommandHasKeywords()
    {
        var commands = DefaultCommands.CreateNavigation();

        Assert.That(
            commands.All(command => command.Keywords.Count > 0),
            Is.True,
            "每条导航命令都要有中英同义词，否则「mods」「log」这类输入搜不到");
        Assert.That(
            commands.All(command => !command.HasShortcut),
            Is.True,
            "导航命令没有全局快捷键，快捷键目录里不该出现它们");
    }

    [Test]
    public void CreateActions_Returns11CommandsWithThreeShortcuts()
    {
        var actions = DefaultCommands.CreateActions();

        Assert.That(actions.Count, Is.EqualTo(11));
        Assert.That(actions[0].Id, Is.EqualTo("action:show-palette"));
        Assert.That(actions[0].ShortcutText, Is.EqualTo("Ctrl+K"));
        Assert.That(actions[1].Id, Is.EqualTo("action:show-shortcuts"));
        Assert.That(actions[1].ShortcutText, Is.EqualTo("Ctrl+/"));
        Assert.That(actions[2].Id, Is.EqualTo("action:open-onboarding-guide"));
        Assert.That(actions[2].ShortcutText, Is.EqualTo("F1"));
        Assert.That(
            actions.Skip(3).All(action => !action.HasShortcut),
            Is.True,
            "没接线的动作不写手势，免得快捷键目录里出现按了没反应的行");
        Assert.That(actions[0].Category, Is.EqualTo("界面"));
    }

    [Test]
    public void CreateActions_OnlyInstanceFolderRequiresSelectedInstance()
    {
        var actions = DefaultCommands.CreateActions();

        Assert.That(
            actions.Where(action => action.RequiresSelectedInstance).Select(action => action.Id),
            Is.EqualTo(new[] { "action:open-instance-folder" }),
            "只有「打开游戏实例目录」依赖当前实例");
    }

    [Test]
    public void NavigationId_And_ActionId_ComposePrefixes()
    {
        Assert.That(DefaultCommands.NavigationId("mods"), Is.EqualTo("nav:mods"));
        Assert.That(DefaultCommands.ActionId("go-back"), Is.EqualTo("action:go-back"));
        Assert.Throws<ArgumentException>(() => DefaultCommands.NavigationId(" "));
        Assert.Throws<ArgumentException>(() => DefaultCommands.ActionId(string.Empty));
    }

    [Test]
    public void TryGetNavigationPageKey_ResolvesOnlyNavigationIds()
    {
        Assert.That(DefaultCommands.TryGetNavigationPageKey("nav:modpack-editor", out var pageKey), Is.True);
        Assert.That(pageKey, Is.EqualTo("modpack-editor"));

        Assert.That(DefaultCommands.TryGetNavigationPageKey("action:go-back", out var actionKey), Is.False);
        Assert.That(actionKey, Is.Null);
        Assert.That(DefaultCommands.TryGetNavigationPageKey("nav:", out _), Is.False, "只有前缀没有页面键不算导航命令");
        Assert.That(DefaultCommands.TryGetNavigationPageKey(null, out _), Is.False);
    }

    [Test]
    public void CreateAll_Returns27UniqueCommands()
    {
        var commands = DefaultCommands.CreateAll();

        Assert.That(commands.Count, Is.EqualTo(27));
        Assert.That(
            commands.Select(command => command.Id).Distinct(StringComparer.Ordinal).Count(),
            Is.EqualTo(27),
            "Id 必须全目录唯一，否则批量注册会被 all-or-nothing 拒绝");
    }

    [Test]
    public void CreateAll_CanBeRegisteredAndSearched()
    {
        var catalog = new CommandCatalog();

        Assert.That(catalog.Register(DefaultCommands.CreateAll()).IsSuccess, Is.True, "内置命令表应当能整批注册");
        Assert.That(
            catalog.Search("log").Any(result => result.Command.Id == "nav:logs"),
            Is.True,
            "输入英文别名也要能搜到中文标题的命令");
        Assert.That(
            catalog.Search("日志").Any(result => result.Command.Id == "action:open-log-folder"),
            Is.True,
            "界面动作同样参与检索");
    }
}
