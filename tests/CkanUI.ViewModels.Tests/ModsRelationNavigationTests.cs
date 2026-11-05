using System.Linq;
using CkanUI.Services.Abstractions.Dtos;
using CkanUI.ViewModels.Dialogs;
using CkanUI.ViewModels.Pages;
using CkanUI.ViewModels.Relations;
using NUnit.Framework;

namespace CkanUI.ViewModels.Tests;

/// <summary>
/// 详情面板里的关系树跳转必须回到模组页的选中链路（F-25 / F-28）。
/// 回归点：早先 <c>ModDetailsViewModel.Reload</c> 直接把 <c>Select(id)</c> 传给关系树，
/// 跳出面板后左侧列表高亮不动、前进/后退历史不记录，面板左上角的返回键成了死键。
/// </summary>
[TestFixture]
public sealed class ModsRelationNavigationTests
{
    private static (ModsPageViewModel Page, ModDetailsViewModel Details) CreateActivated()
    {
        var repositories = new FakeRepositoryService
        {
            Current = ModsRowFactory.Repository(
                ModsRowFactory.Create(
                    "Alpha",
                    "Alpha 模组",
                    "zoe",
                    facts: new ModSearchFacts { DependsOn = ["Bravo", "Charlie"] }),
                ModsRowFactory.Create(
                    "Bravo",
                    "Bravo 模组",
                    "adam",
                    facts: new ModSearchFacts { Recommends = ["Charlie"] }),
                ModsRowFactory.Create("Charlie", "Charlie 模组", "mike")),
        };
        var registry = new FakeRegistryService();
        var instances = new FakeInstanceService { Current = ModsRowFactory.Instance() };
        var page = new ModsPageViewModel(repositories, registry, instances, new RecordingErrorReporter())
        {
            Scheduler = new ImmediateDelayScheduler(),
        };
        page.Activate();

        var details = new ModDetailsViewModel(repositories, registry);
        page.Details = details;
        return (page, details);
    }

    private static void Select(ModsPageViewModel page, string identifier) =>
        page.SelectedRow = page.Rows.First(row => row.Identifier == identifier);

    private static RelationNodeViewModel Leaf(RelationTreeViewModel tree, string identifier) =>
        tree.Roots[0].Children
            .First(category => category.Kind == RelationKind.Depends)
            .Children
            .First(leaf => leaf.Identifier == identifier);

    [Test]
    public void RelationTreeJump_SelectsTheTargetOnThePage_AndRecordsHistory()
    {
        var (page, details) = CreateActivated();
        Select(page, "Alpha");

        var tree = details.RelationTree;
        Assert.That(tree, Is.Not.Null, "选中模组后详情面板应构出关系树");

        tree!.Navigate(Leaf(tree, "Bravo"));

        Assert.That(page.SelectedRow?.Identifier, Is.EqualTo("Bravo"),
            "关系树跳转必须走页面选中链路，左侧列表要高亮到目标模组");
        Assert.That(page.CanGoBackSelection, Is.True, "跳转要进历史，返回键才有东西可回");

        page.GoBackSelectionCommand.Execute(null);
        Assert.That(page.SelectedRow?.Identifier, Is.EqualTo("Alpha"));
        Assert.That(page.Details?.RelationTree, Is.Not.Null, "回到 Alpha 后面板要重新构出它的关系树");
    }

    [Test]
    public void RelationTreeJump_KeepsTheRelationsTab()
    {
        var (page, details) = CreateActivated();
        Select(page, "Alpha");
        details.SelectedTabIndex = 1;

        var tree = details.RelationTree!;
        tree.Navigate(Leaf(tree, "Bravo"));

        Assert.That(details.SelectedTabIndex, Is.EqualTo(1),
            "用户正在读关系，跳到另一个模组不该被弹回元数据页");
        Assert.That(details.RelationTree, Is.Not.Null, "Bravo 也有关系，页签应当仍有内容");
    }

    [Test]
    public void RelationTreeJump_ToModWithoutRelations_FallsBackToMetadataTab()
    {
        var (page, details) = CreateActivated();
        Select(page, "Alpha");
        details.SelectedTabIndex = 1;

        var tree = details.RelationTree!;
        tree.Navigate(Leaf(tree, "Charlie"));

        Assert.That(page.SelectedRow?.Identifier, Is.EqualTo("Charlie"));
        Assert.That(details.RelationTree, Is.Null, "Charlie 没有关系");
        Assert.That(details.SelectedTabIndex, Is.EqualTo(0),
            "目标没有关系可读时，别把用户扔在一片空白的依赖关系页签上");
    }

    [Test]
    public void PlainSelection_StillResetsToTheMetadataTab()
    {
        var (page, details) = CreateActivated();
        Select(page, "Alpha");
        details.SelectedTabIndex = 1;

        Select(page, "Bravo");

        Assert.That(details.SelectedTabIndex, Is.EqualTo(0),
            "只有关系树跳转保留页签，列表点选仍回到元数据页");
    }
}
