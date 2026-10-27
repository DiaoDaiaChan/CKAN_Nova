using System.Threading.Tasks;
using CkanUI.Services.Abstractions;
using CkanUI.Services.Abstractions.Dtos;
using CkanUI.ViewModels.Pages;
using CkanUI.ViewModels.Pages.Mods;
using NUnit.Framework;

namespace CkanUI.ViewModels.Tests;

/// <summary>F-25 选中历史：详情面板的「上一个/下一个选中模组」。</summary>
[TestFixture]
public sealed class ModsSelectionHistoryTests
{
    private ModsPageViewModel CreateActivated()
    {
        var repositories = new FakeRepositoryService
        {
            Current = ModsRowFactory.Repository(
                ModsRowFactory.Create("Alpha", "Alpha", "zoe"),
                ModsRowFactory.Create("Bravo", "Bravo", "adam"),
                ModsRowFactory.Create("Charlie", "Charlie", "mike")),
        };
        var instances = new FakeInstanceService { Current = ModsRowFactory.Instance() };
        var vm = new ModsPageViewModel(repositories, new FakeRegistryService(), instances,
            new RecordingErrorReporter())
        {
            Scheduler = new ImmediateDelayScheduler(),
        };
        vm.Activate();
        return vm;
    }

    private static void Select(ModsPageViewModel vm, string identifier)
    {
        vm.SelectedRow = System.Linq.Enumerable.FirstOrDefault(vm.Rows, row => row.Identifier == identifier);
    }

    [Test]
    public void FreshViewModel_CannotGoBackOrForward()
    {
        var vm = CreateActivated();
        Assert.That(vm.CanGoBackSelection, Is.False);
        Assert.That(vm.CanGoForwardSelection, Is.False);
    }

    [Test]
    public void SelectingSecondMod_EnablesBack_AndBackReturnsFirst()
    {
        var vm = CreateActivated();
        Select(vm, "Alpha");
        Assert.That(vm.CanGoBackSelection, Is.False);

        Select(vm, "Bravo");
        Assert.That(vm.CanGoBackSelection, Is.True);

        vm.GoBackSelectionCommand.Execute(null);
        Assert.That(vm.SelectedRow?.Identifier, Is.EqualTo("Alpha"));
        Assert.That(vm.CanGoForwardSelection, Is.True);
    }

    [Test]
    public void GoForward_RestoresTheAbandonedSelection()
    {
        var vm = CreateActivated();
        Select(vm, "Alpha");
        Select(vm, "Bravo");
        vm.GoBackSelectionCommand.Execute(null);

        vm.GoForwardSelectionCommand.Execute(null);
        Assert.That(vm.SelectedRow?.Identifier, Is.EqualTo("Bravo"));
        Assert.That(vm.CanGoForwardSelection, Is.False);
    }

    [Test]
    public void NewSelectionAfterBack_ClearsForwardHistory()
    {
        var vm = CreateActivated();
        Select(vm, "Alpha");
        Select(vm, "Bravo");
        vm.GoBackSelectionCommand.Execute(null);
        Assert.That(vm.CanGoForwardSelection, Is.True);

        Select(vm, "Charlie");
        Assert.That(vm.CanGoForwardSelection, Is.False);
    }

    [Test]
    public void ReselectingSameRow_DoesNotGrowHistory()
    {
        var vm = CreateActivated();
        Select(vm, "Alpha");
        Select(vm, "Alpha");
        Assert.That(vm.CanGoBackSelection, Is.False);
    }

    [Test]
    public void HistoryRestore_DoesNotRecordFurtherEntries()
    {
        var vm = CreateActivated();
        Select(vm, "Alpha");
        Select(vm, "Bravo");
        vm.GoBackSelectionCommand.Execute(null);

        // 回退本身不能再压栈，否则点击一次就会把 Alpha 重复记入。
        Assert.That(vm.CanGoBackSelection, Is.False);
    }

    [Test]
    public void GoBack_WhenHistoryTargetLeftTheList_IsNoOp()
    {
        var repositories = new FakeRepositoryService
        {
            Current = ModsRowFactory.Repository(
                ModsRowFactory.Create("Alpha", "Alpha", "zoe"),
                ModsRowFactory.Create("Bravo", "Bravo", "adam")),
        };
        var vm = new ModsPageViewModel(repositories, new FakeRegistryService(),
            new FakeInstanceService { Current = ModsRowFactory.Instance() }, new RecordingErrorReporter())
        {
            Scheduler = new ImmediateDelayScheduler(),
        };
        vm.Activate();
        Select(vm, "Alpha");
        Select(vm, "Bravo");

        // 仓库索引被清空后列表里已经没有 Alpha，回退只能原地不动。
        repositories.Current = ModsRowFactory.Repository();
        vm.Deactivate();
        vm.Activate();
        Assert.That(vm.Rows, Is.Empty);

        vm.GoBackSelectionCommand.Execute(null);
        Assert.That(vm.CanGoForwardSelection, Is.False);
    }

    [Test]
    public void DetailPanel_ReflectsHistorySelection()
    {
        var vm = CreateActivated();
        Select(vm, "Alpha");
        Select(vm, "Bravo");
        vm.GoBackSelectionCommand.Execute(null);

        Assert.That(vm.DetailName, Is.EqualTo("Alpha"));
    }
}
