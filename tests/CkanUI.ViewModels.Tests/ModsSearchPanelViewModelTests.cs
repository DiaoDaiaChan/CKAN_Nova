using System.Collections.Generic;
using System.Linq;
using CkanUI.ViewModels.Pages;
using CkanUI.ViewModels.Pages.Mods;
using NUnit.Framework;

namespace CkanUI.ViewModels.Tests;

/// <summary>
/// 组合搜索面板（12 篇 F-20/F-21 / UI-14）。
/// 面板是搜索框的可视化编辑面：字段改动要产出同一套语法文本，搜索框改动要能回填字段，
/// 而面板自己写进去的文本不能再被规范化改写（否则用户正在输入的词会被吞掉）。
/// </summary>
[TestFixture]
public sealed class ModsSearchPanelViewModelTests
{
    private static ModsSearchPanelViewModel CreatePanel() => new(Search.Formatter);

    private static ModsSearchFieldViewModel Field(ModsSearchPanelViewModel panel, string key)
        => panel.Fields.Single(field => field.Key == key);

    private static ModsPageViewModel CreatePage()
    {
        var repositories = new FakeRepositoryService
        {
            Current = ModsRowFactory.Repository(
                ModsRowFactory.Create("Alpha", "Alpha", "zoe", latestVersion: "1.0.0"),
                ModsRowFactory.Create("Bravo", "Bravo", "adam", latestVersion: "2.0.0")),
        };
        var instances = new FakeInstanceService { Current = ModsRowFactory.Instance() };
        return new ModsPageViewModel(
            repositories,
            new FakeRegistryService(),
            instances,
            new RecordingErrorReporter(),
            searchParser: Search.Parser,
            searchFormatter: Search.Formatter)
        {
            Scheduler = new ImmediateDelayScheduler(),
        };
    }

    [Test]
    public void Fields_MatchUpstreamFieldList()
    {
        var panel = CreatePanel();

        Assert.That(panel.Fields, Has.Count.EqualTo(18));
        Assert.That(panel.Fields.Count(field => field.IsText), Is.EqualTo(12));
        Assert.That(panel.Fields.Count(field => field.IsState), Is.EqualTo(6));
        Assert.That(
            panel.Fields.Select(field => field.Key),
            Is.EqualTo(new[]
            {
                "name", "author", "desc", "lic", "lang", "dep", "rec", "sug", "conf", "sup", "tag", "label",
                "compatible", "installed", "cached", "newly-compatible", "upgradeable", "replaceable",
            }));
    }

    [Test]
    public void TextField_ProducesSyntaxText_AndStructuredSpec()
    {
        var panel = CreatePanel();
        string? raised = null;
        panel.CombinedChanged += text => raised = text;

        Field(panel, "author").Text = "sarbian";

        Assert.That(raised, Is.EqualTo("@sarbian"));
        Assert.That(panel.Preview, Is.EqualTo("@sarbian"));
        Assert.That(panel.BuildSpec().Authors, Is.EquivalentTo(new[] { "sarbian" }));
        Assert.That(panel.ActiveCount, Is.EqualTo(1));
        Assert.That(panel.SummaryText, Is.EqualTo("筛选（1）"));
    }

    [Test]
    public void TextField_RaisesOnEveryKeystroke()
    {
        // 只在空↔非空时通知会让查询文本停在第一个字符上。
        var panel = CreatePanel();
        var texts = new List<string>();
        panel.CombinedChanged += text => texts.Add(text);

        Field(panel, "name").Text = "eng";
        Field(panel, "name").Text = "engineer";

        Assert.That(texts, Is.EqualTo(new[] { "eng", "engineer" }));
    }

    [Test]
    public void StateField_MapsToIsAndNot()
    {
        var panel = CreatePanel();
        var installed = Field(panel, "installed");

        installed.StateIndex = 1;
        Assert.That(panel.BuildCombined(), Is.EqualTo("is:installed"));
        Assert.That(panel.BuildSpec().Installed, Is.True);

        installed.StateIndex = 2;
        Assert.That(panel.BuildCombined(), Is.EqualTo("not:installed"));
        Assert.That(panel.BuildSpec().Installed, Is.False);

        installed.StateIndex = 0;
        Assert.That(panel.BuildCombined(), Is.Empty);
        Assert.That(panel.BuildSpec().Installed, Is.Null);
        Assert.That(panel.HasActiveFilters, Is.False);
    }

    [Test]
    public void LoadFrom_FillsFields_WithoutRaising()
    {
        var panel = CreatePanel();
        var raised = 0;
        panel.CombinedChanged += _ => raised++;

        panel.LoadFrom(Search.Parser.Parse("ModuleManager @sarbian tag:plugin is:installed not:upgradeable"));

        Assert.That(raised, Is.Zero);
        Assert.That(Field(panel, "name").Text, Is.EqualTo("ModuleManager"));
        Assert.That(Field(panel, "author").Text, Is.EqualTo("sarbian"));
        Assert.That(Field(panel, "tag").Text, Is.EqualTo("plugin"));
        Assert.That(Field(panel, "installed").StateIndex, Is.EqualTo(1));
        Assert.That(Field(panel, "upgradeable").StateIndex, Is.EqualTo(2));
        Assert.That(panel.ActiveCount, Is.EqualTo(5));

        // 回填后原样生成同一串语法（字段顺序与 getCombined 顺序一致）。
        Assert.That(
            panel.BuildCombined(),
            Is.EqualTo("ModuleManager @sarbian tag:plugin is:installed not:upgradeable"));
    }

    [Test]
    public void LoadFrom_EmptySpec_ClearsEverything()
    {
        var panel = CreatePanel();
        Field(panel, "lic").Text = "MIT";

        panel.LoadFrom(null);

        Assert.That(Field(panel, "lic").Text, Is.Empty);
        Assert.That(panel.ActiveCount, Is.Zero);
        Assert.That(panel.Preview, Is.Empty);
    }

    [Test]
    public void Clear_EmptiesFields_AndRaisesEmptyText()
    {
        var panel = CreatePanel();
        Field(panel, "name").Text = "engineer";
        Field(panel, "installed").StateIndex = 1;
        string? raised = "未触发";
        panel.CombinedChanged += text => raised = text;

        panel.Clear();

        Assert.That(raised, Is.Empty);
        Assert.That(panel.Fields.Any(field => field.HasValue), Is.False);
        Assert.That(panel.HasActiveFilters, Is.False);
    }

    [Test]
    public void ToggleExpanded_FlipsState()
    {
        var panel = CreatePanel();

        panel.ToggleExpandedCommand.Execute(null);
        Assert.That(panel.IsExpanded, Is.True);

        panel.ToggleExpandedCommand.Execute(null);
        Assert.That(panel.IsExpanded, Is.False);
    }

    [Test]
    public void Page_PanelEdit_DrivesSearchBox_AndFiltersRows()
    {
        var page = CreatePage();
        page.Activate();
        Assert.That(page.Rows, Has.Count.EqualTo(2));

        Field(page.SearchPanel, "author").Text = "adam";
        page.PendingSearch.GetAwaiter().GetResult();

        Assert.That(page.SearchText, Is.EqualTo("@adam"));
        Assert.That(page.Rows, Has.Count.EqualTo(1));
        Assert.That(page.Rows[0].Identifier, Is.EqualTo("Bravo"));
    }

    [Test]
    public void Page_PanelEdit_IsNotRewrittenByNormalization()
    {
        // 名称字段用空格分隔会被语法连成一个词，但面板里的原文必须原样留着，
        // 否则用户输入到一半就会被回填改写。
        var page = CreatePage();
        page.Activate();

        Field(page.SearchPanel, "name").Text = "Alpha Bravo";
        page.PendingSearch.GetAwaiter().GetResult();

        Assert.That(page.SearchText, Is.EqualTo("AlphaBravo"));
        Assert.That(Field(page.SearchPanel, "name").Text, Is.EqualTo("Alpha Bravo"));
        Assert.That(page.Rows, Is.Empty);
    }

    [Test]
    public void Page_SearchBoxEdit_RefillsPanel()
    {
        var page = CreatePage();
        page.Activate();

        page.SearchText = "@adam";
        page.PendingSearch.GetAwaiter().GetResult();

        Assert.That(Field(page.SearchPanel, "author").Text, Is.EqualTo("adam"));
        Assert.That(page.Rows, Has.Count.EqualTo(1));
    }

    [Test]
    public void Page_ClearSearch_AlsoClearsPanel()
    {
        var page = CreatePage();
        page.Activate();
        Field(page.SearchPanel, "author").Text = "adam";
        page.PendingSearch.GetAwaiter().GetResult();

        page.ClearSearch();
        page.PendingSearch.GetAwaiter().GetResult();

        Assert.That(Field(page.SearchPanel, "author").Text, Is.Empty);
        Assert.That(page.SearchPanel.HasActiveFilters, Is.False);
        Assert.That(page.Rows, Has.Count.EqualTo(2));
    }

}
