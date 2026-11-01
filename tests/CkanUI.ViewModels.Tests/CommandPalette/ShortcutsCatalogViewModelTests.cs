using CkanUI.Services.Abstractions.Commands;
using CkanUI.Services.Commands;
using CkanUI.ViewModels.CommandPalette;
using NUnit.Framework;

namespace CkanUI.ViewModels.Tests.CommandPalette;

/// <summary>
/// 快捷键目录：条目收集（命令目录 + 面板/对话框自身手势）、过滤、分组、纯文本导出与复制事件。
/// </summary>
[TestFixture]
public sealed class ShortcutsCatalogViewModelTests
{
    private static CommandCatalog CreateCatalogWithDefaultActions()
    {
        var catalog = new CommandCatalog();
        catalog.Register(DefaultCommands.CreateActions());
        return catalog;
    }

    [Test]
    public void Constructor_WithoutCatalog_ListsPaletteAndDialogGestures()
    {
        var viewModel = new ShortcutsCatalogViewModel();

        Assert.That(viewModel.Entries.Count, Is.EqualTo(5), "面板 4 个手势 + 对话框 1 个手势");
        Assert.That(viewModel.Groups.Count, Is.EqualTo(2), "没有命令目录时不出现「全局」分组");
        Assert.That(viewModel.Groups[0].Category, Is.EqualTo("命令面板"));
        Assert.That(viewModel.Groups[1].Category, Is.EqualTo("快捷键目录"));
        Assert.That(viewModel.Groups[0].Count, Is.EqualTo(4));
        Assert.That(viewModel.TotalCountText, Is.EqualTo("共 5 项"));
    }

    [Test]
    public void Constructor_WithCatalog_IncludesCommandShortcuts()
    {
        var viewModel = new ShortcutsCatalogViewModel(CreateCatalogWithDefaultActions());

        Assert.That(viewModel.Entries.Count, Is.EqualTo(8), "内置动作里 Ctrl+K / Ctrl+/ / F1 也要进目录");
        Assert.That(viewModel.Entries[0].Gesture, Is.EqualTo("Ctrl+K"));
        Assert.That(viewModel.Entries[0].Description, Is.EqualTo("命令面板"));
        Assert.That(viewModel.Entries[0].Category, Is.EqualTo("全局"));
        Assert.That(
            viewModel.Groups.Select(group => group.Category),
            Is.EqualTo(new[] { "全局", "命令面板", "快捷键目录" }),
            "分组顺序按条目首次出现，不做字母排序");
    }

    [Test]
    public void Filter_ByGesture_NarrowsEntries()
    {
        var viewModel = new ShortcutsCatalogViewModel(CreateCatalogWithDefaultActions());

        viewModel.Filter = "ctrl";

        Assert.That(viewModel.Entries.Count, Is.EqualTo(8), "未过滤全集不受过滤词影响");
        Assert.That(viewModel.FilteredEntries.Count, Is.EqualTo(2), "过滤不区分大小写");
        Assert.That(viewModel.HasEntries, Is.True);
        Assert.That(viewModel.TotalCountText, Is.EqualTo("共 2 项"));
    }

    [Test]
    public void Filter_ByDescriptionOrCategory_NarrowsEntries()
    {
        var viewModel = new ShortcutsCatalogViewModel(CreateCatalogWithDefaultActions());

        viewModel.Filter = "上一条";
        Assert.That(viewModel.FilteredEntries.Count, Is.EqualTo(1));

        viewModel.Filter = "Esc";
        Assert.That(viewModel.FilteredEntries.Count, Is.EqualTo(2), "面板与对话框各有一个 Esc");
    }

    [Test]
    public void Filter_WithoutMatch_ShowsEmptyHint()
    {
        var viewModel = new ShortcutsCatalogViewModel(CreateCatalogWithDefaultActions());

        viewModel.Filter = "zzzz";

        Assert.That(viewModel.FilteredEntries, Is.Empty);
        Assert.That(viewModel.HasEntries, Is.False);
        Assert.That(viewModel.ShowEmptyHint, Is.True);
        Assert.That(viewModel.TotalCountText, Is.EqualTo("共 0 项"));
    }

    [Test]
    public void Filter_BlankInput_ListsEverything()
    {
        var viewModel = new ShortcutsCatalogViewModel(CreateCatalogWithDefaultActions());

        viewModel.Filter = "   ";

        Assert.That(viewModel.FilteredEntries.Count, Is.EqualTo(8), "只有空白时按未过滤处理");
    }

    [Test]
    public void BuildPlainTextReport_ContainsTitleCategoriesAndGestures()
    {
        var viewModel = new ShortcutsCatalogViewModel(CreateCatalogWithDefaultActions());

        var report = viewModel.BuildPlainTextReport();

        Assert.That(report, Does.Contain("快捷键目录"), "首行是标题");
        Assert.That(report, Does.Contain("共 8 项"));
        Assert.That(report, Does.Contain("[全局]"), "每个分类一段");
        Assert.That(report, Does.Contain("Ctrl+K    命令面板"), "手势与说明之间用 4 个空格对齐");
        Assert.That(report, Does.Contain("↑    上一条"));
    }

    [Test]
    public void BuildPlainTextReport_FollowsCurrentFilter()
    {
        var viewModel = new ShortcutsCatalogViewModel(CreateCatalogWithDefaultActions());
        viewModel.Filter = "ctrl";

        var report = viewModel.BuildPlainTextReport();

        Assert.That(report, Does.Contain("共 2 项"), "导出的是当前过滤结果");
        Assert.That(report, Does.Not.Contain("上一条"));
    }

    [Test]
    public void CopyCommand_RaisesCopyRequestedWithReport()
    {
        var viewModel = new ShortcutsCatalogViewModel();
        string? copied = null;
        viewModel.CopyRequested += text => copied = text;

        viewModel.CopyCommand.Execute(null);

        Assert.That(copied, Is.Not.Null, "视图模型不碰剪贴板，只抛事件给视图");
        Assert.That(copied, Is.EqualTo(viewModel.BuildPlainTextReport()));
    }

    [Test]
    public void TitleAndButtons_ComeFromTranslator()
    {
        var viewModel = new ShortcutsCatalogViewModel(null, key => "T:" + key);

        Assert.That(viewModel.Title, Is.EqualTo("T:Ui_Cmd_Shortcuts_Title"));
        Assert.That(viewModel.PrimaryButtonText, Is.EqualTo("T:Ui_Cmd_Shortcuts_Close"));
        Assert.That(viewModel.SecondaryButtonText, Is.Empty, "目录是只读的，只需要一个关闭按钮");
        Assert.That(viewModel.HasSecondaryButton, Is.False);
        Assert.That(viewModel.CopyButtonText, Is.EqualTo("T:Ui_Cmd_Shortcuts_Copy"));
        Assert.That(viewModel.FilterWatermark, Is.EqualTo("T:Ui_Cmd_Shortcuts_FilterWatermark"));
    }

    [Test]
    public void Refresh_AfterCatalogGrows_PicksUpNewShortcut()
    {
        var catalog = new CommandCatalog();
        var viewModel = new ShortcutsCatalogViewModel(catalog);
        Assert.That(viewModel.Entries.Count, Is.EqualTo(5));

        catalog.Register(new CommandDescriptor("action:refresh", "刷新列表") { ShortcutText = "F5" });
        viewModel.Refresh();

        Assert.That(viewModel.Entries.Count, Is.EqualTo(6), "宿主注册新命令后刷新即可出现在目录里");
        Assert.That(
            viewModel.FilteredEntries.Any(entry => entry.Gesture == "F5" && entry.Category == "全局"),
            Is.True);
    }
}
