using CkanUI.Services.Abstractions.Commands;
using CkanUI.Services.Commands;
using CkanUI.ViewModels.CommandPalette;
using NUnit.Framework;

namespace CkanUI.ViewModels.Tests.CommandPalette;

/// <summary>
/// 命令面板视图模型：开合、输入即搜、上下选择、回车执行、计数与上限。
/// 只依赖 <see cref="ICommandCatalog"/>，所以测试用真实目录实现即可，不需要替身。
/// </summary>
[TestFixture]
public sealed class CommandPaletteViewModelTests
{
    private static CommandPaletteViewModel CreateWithCommands(params CommandDescriptor[] commands)
    {
        var catalog = new CommandCatalog();
        foreach (var command in commands)
        {
            catalog.Register(command);
        }

        return new CommandPaletteViewModel(catalog);
    }

    private static CommandPaletteViewModel CreateWithBuiltInCommands()
    {
        var catalog = new CommandCatalog();
        catalog.Register(DefaultCommands.CreateAll());
        return new CommandPaletteViewModel(catalog);
    }

    [Test]
    public void Open_SetsIsOpenAndSelectsFirstResult()
    {
        var viewModel = CreateWithCommands(
            new CommandDescriptor("nav:mods", "管理模组"),
            new CommandDescriptor("nav:logs", "日志"));

        viewModel.OpenCommand.Execute(null);

        Assert.That(viewModel.IsOpen, Is.True);
        Assert.That(viewModel.HasResults, Is.True);
        Assert.That(viewModel.Results.Count, Is.EqualTo(2), "空查询时列出全部命令");
        Assert.That(viewModel.SelectedIndex, Is.EqualTo(0), "打开后默认选中第一条，回车即执行");
        Assert.That(viewModel.SelectedItem, Is.Not.Null);
    }

    [Test]
    public void Open_ResetsQueryToEmpty()
    {
        var viewModel = CreateWithCommands(new CommandDescriptor("nav:logs", "日志"));
        viewModel.Query = "日";

        viewModel.OpenCommand.Execute(null);

        Assert.That(viewModel.Query, Is.Empty, "每次打开都是干净状态，不残留上次的输入");
    }

    [Test]
    public void Query_FiltersResultsAndHighlightsTitle()
    {
        var viewModel = CreateWithBuiltInCommands();
        viewModel.OpenCommand.Execute(null);

        viewModel.Query = "日志";

        Assert.That(viewModel.Results, Is.Not.Empty, "内置命令里有多条与「日志」相关");
        Assert.That(viewModel.Results[0].Id, Is.EqualTo("nav:logs"), "标题完全相同的那条排第一");
        Assert.That(viewModel.Results[0].HasHighlight, Is.True, "命中片段要能高亮");
        Assert.That(viewModel.SelectedIndex, Is.EqualTo(0), "查询变化后选中项回到第一条");
    }

    [Test]
    public void Query_WithoutMatch_ShowsEmptyHint()
    {
        var viewModel = CreateWithCommands(new CommandDescriptor("nav:logs", "日志"));
        viewModel.OpenCommand.Execute(null);

        viewModel.Query = "zzzz";

        Assert.That(viewModel.Results, Is.Empty);
        Assert.That(viewModel.HasResults, Is.False);
        Assert.That(viewModel.ShowEmptyHint, Is.True, "面板打开且输入了查询词却无结果时才提示空态");
        Assert.That(viewModel.SelectedIndex, Is.EqualTo(-1));
        Assert.That(viewModel.SelectedItem, Is.Null);
    }

    [Test]
    public void Close_ResetsIsOpenAndQuery()
    {
        var viewModel = CreateWithBuiltInCommands();
        viewModel.OpenCommand.Execute(null);
        viewModel.Query = "日志";

        viewModel.CloseCommand.Execute(null);

        Assert.That(viewModel.IsOpen, Is.False);
        Assert.That(viewModel.Query, Is.Empty, "关闭时清空查询，下次打开不会带着旧结果");
        Assert.That(viewModel.ShowEmptyHint, Is.False, "面板关着时不该显示空态提示");
    }

    [Test]
    public void ShowEmptyHint_IsFalseBeforePanelIsOpened()
    {
        var viewModel = CreateWithCommands(new CommandDescriptor("nav:logs", "日志"));

        viewModel.Query = "zzzz";

        Assert.That(viewModel.Results, Is.Empty);
        Assert.That(viewModel.ShowEmptyHint, Is.False, "面板没打开时不提示空态");
    }

    [Test]
    public void Toggle_OpensThenCloses()
    {
        var viewModel = CreateWithCommands(new CommandDescriptor("nav:logs", "日志"));

        viewModel.ToggleCommand.Execute(null);
        Assert.That(viewModel.IsOpen, Is.True, "第一次 Ctrl+K 打开面板");

        viewModel.ToggleCommand.Execute(null);
        Assert.That(viewModel.IsOpen, Is.False, "再按一次关闭面板");
    }

    [Test]
    public void MoveDown_WrapsAroundToFirst()
    {
        var viewModel = CreateWithCommands(
            new CommandDescriptor("c1", "命令一"),
            new CommandDescriptor("c2", "命令二"),
            new CommandDescriptor("c3", "命令三"));
        viewModel.OpenCommand.Execute(null);

        viewModel.MoveDownCommand.Execute(null);
        Assert.That(viewModel.SelectedIndex, Is.EqualTo(1));

        viewModel.MoveDownCommand.Execute(null);
        Assert.That(viewModel.SelectedIndex, Is.EqualTo(2));

        viewModel.MoveDownCommand.Execute(null);
        Assert.That(viewModel.SelectedIndex, Is.EqualTo(0), "到底后回绕到第一条");
    }

    [Test]
    public void MoveUp_FromFirst_WrapsToLast()
    {
        var viewModel = CreateWithCommands(
            new CommandDescriptor("c1", "命令一"),
            new CommandDescriptor("c2", "命令二"),
            new CommandDescriptor("c3", "命令三"));
        viewModel.OpenCommand.Execute(null);

        viewModel.MoveUpCommand.Execute(null);

        Assert.That(viewModel.SelectedIndex, Is.EqualTo(2), "在第一条上按上移回绕到最后一条");
    }

    [Test]
    public void MoveUpAndDown_WithoutResults_KeepsMinusOne()
    {
        var viewModel = CreateWithCommands(new CommandDescriptor("nav:logs", "日志"));
        viewModel.OpenCommand.Execute(null);
        viewModel.Query = "zzzz";

        viewModel.MoveDownCommand.Execute(null);

        Assert.That(viewModel.SelectedIndex, Is.EqualTo(-1), "没有结果时移动不产生非法下标");
    }

    [Test]
    public void ExecuteSelected_RaisesCommandInvokedWithIdAndCloses()
    {
        var viewModel = CreateWithCommands(
            new CommandDescriptor("nav:mods", "管理模组"),
            new CommandDescriptor("nav:logs", "日志"));
        string? invoked = null;
        viewModel.CommandInvoked += id => invoked = id;
        viewModel.OpenCommand.Execute(null);
        viewModel.MoveDownCommand.Execute(null);

        viewModel.ExecuteSelectedCommand.Execute(null);

        Assert.That(invoked, Is.EqualTo("nav:logs"), "执行的是当前选中项，宿主拿 Id 去映射导航或动作");
        Assert.That(viewModel.IsOpen, Is.False, "先关闭再执行，浮层不会压在新内容上");
    }

    [Test]
    public void ExecuteSelected_DisabledCommand_DoesNotInvoke()
    {
        var viewModel = CreateWithCommands(
            new CommandDescriptor("action:open-instance-folder", "打开游戏实例目录") { IsEnabled = false });
        string? invoked = null;
        viewModel.CommandInvoked += id => invoked = id;
        viewModel.OpenCommand.Execute(null);

        viewModel.ExecuteSelectedCommand.Execute(null);

        Assert.That(invoked, Is.Null, "不可执行的命令静默忽略，不打断输入");
        Assert.That(viewModel.IsOpen, Is.True, "面板也不关闭");
    }

    [Test]
    public void ExecuteSelected_WhenClosed_DoesNothing()
    {
        var viewModel = CreateWithCommands(new CommandDescriptor("nav:logs", "日志"));
        string? invoked = null;
        viewModel.CommandInvoked += id => invoked = id;

        viewModel.ExecuteSelectedCommand.Execute(null);

        Assert.That(invoked, Is.Null, "面板没打开时回车不执行任何命令");
    }

    [Test]
    public void ResultCountText_FormatsCount()
    {
        var viewModel = CreateWithCommands(
            new CommandDescriptor("c1", "命令一"),
            new CommandDescriptor("c2", "命令二"));
        viewModel.OpenCommand.Execute(null);

        Assert.That(viewModel.ResultCountText, Is.EqualTo("共 2 条"));

        viewModel.Query = "zzzz";
        Assert.That(viewModel.ResultCountText, Is.Empty, "没有结果时不显示计数");
    }

    [Test]
    public void ResultCountText_WhenCapped_AppendsTruncatedHint()
    {
        var catalog = new CommandCatalog();
        for (var index = 0; index < CommandPaletteViewModel.MaxResults + 10; index++)
        {
            catalog.Register(new CommandDescriptor("cmd:" + index, "命令" + index));
        }

        var viewModel = new CommandPaletteViewModel(catalog);
        viewModel.OpenCommand.Execute(null);

        Assert.That(viewModel.Results.Count, Is.EqualTo(CommandPaletteViewModel.MaxResults), "结果最多 50 条");
        Assert.That(viewModel.IsCapped, Is.True);
        Assert.That(viewModel.ResultCountText, Is.EqualTo("共 50 条（结果过多，已截断）"));
    }

    [Test]
    public void Texts_UseTranslatorWhenProvided()
    {
        var catalog = new CommandCatalog();
        var viewModel = new CommandPaletteViewModel(catalog, key => "T:" + key);

        Assert.That(viewModel.PlaceholderText, Is.EqualTo("T:Ui_Cmd_Palette_Placeholder"));
        Assert.That(viewModel.EmptyHintText, Is.EqualTo("T:Ui_Cmd_Palette_Empty"));
        Assert.That(viewModel.FooterHintText, Is.EqualTo("T:Ui_Cmd_Palette_FooterHint"));
    }

    [Test]
    public void Texts_WithoutTranslator_FallBackToChinese()
    {
        var viewModel = new CommandPaletteViewModel(new CommandCatalog());

        Assert.That(viewModel.PlaceholderText, Does.Contain("搜索命令"), "缺省兜底必须是中文，不能把键名显示出来");
        Assert.That(viewModel.FooterHintText, Does.Contain("Enter"));
    }

    [Test]
    public void ItemViewModel_ExposesDetailAndShortcut()
    {
        var viewModel = CreateWithCommands(
            new CommandDescriptor("action:export-diagnostics", "导出诊断包")
            {
                Category = "界面",
                Subtitle = "把日志与配置打包",
                ShortcutText = "Ctrl+E",
            });
        viewModel.OpenCommand.Execute(null);

        var item = viewModel.Results[0];
        Assert.That(item.Category, Is.EqualTo("界面"));
        Assert.That(item.DetailText, Is.EqualTo("界面 · 把日志与配置打包"), "分类与副标题拼成行内次要说明");
        Assert.That(item.HasDetail, Is.True);
        Assert.That(item.HasShortcut, Is.True);
        Assert.That(item.ShortcutText, Is.EqualTo("Ctrl+E"));
        Assert.That(item.IsEnabled, Is.True);
    }

    [Test]
    public void ItemViewModel_WithoutDetailText_HasNoDetail()
    {
        var viewModel = CreateWithCommands(
            new CommandDescriptor("c1", "命令一") { Category = string.Empty, Subtitle = string.Empty });
        viewModel.OpenCommand.Execute(null);

        Assert.That(viewModel.Results[0].DetailText, Is.Empty);
        Assert.That(viewModel.Results[0].HasDetail, Is.False);
        Assert.That(viewModel.Results[0].HasShortcut, Is.False);
    }
}
