using CkanUI.Services.Abstractions;
using CkanUI.Services.Abstractions.Dtos;
using CkanUI.ViewModels.Pages;
using CkanUI.ViewModels.Pages.Unmanaged;
using NUnit.Framework;

namespace CkanUI.ViewModels.Tests;

/// <summary>
/// 未管理文件页 VM（12 篇 F-42 / UI-10）：树展开折叠、选中统计、无实例提示、删除二次确认、dry-run 预演。
/// </summary>
[TestFixture]
public sealed class UnmanagedPageViewModelTests
{
    private const string GameDir = @"C:\KSP";

    private UnmanagedFakeService _service = null!;
    private UnmanagedFakeInstanceService _instances = null!;
    private UnmanagedFakeErrorReporter _errors = null!;
    private UnmanagedFakeDialogService _dialogs = null!;

    [SetUp]
    public void SetUp()
    {
        _service = new UnmanagedFakeService();
        _instances = new UnmanagedFakeInstanceService { Current = Instance() };
        _errors = new UnmanagedFakeErrorReporter();
        _dialogs = new UnmanagedFakeDialogService();
    }

    [Test]
    public void Constructor_WithoutInstance_ShowsNoInstanceCard()
    {
        _instances.Current = null;

        var vm = CreateViewModel();

        Assert.That(vm.Title, Is.EqualTo("未管理文件"));
        Assert.That(vm.HasInstance, Is.False);
        Assert.That(vm.ShowNoInstance, Is.True);
        Assert.That(vm.NoInstanceTitle, Is.EqualTo("还没有选定游戏实例"));
        Assert.That(vm.NoInstanceHint, Does.Contain("实例页"));
        Assert.That(vm.ShowEmptyState, Is.False, "无实例时不能同时显示空状态");
    }

    [Test]
    public async Task Refresh_WithoutInstance_ShowsHintAndSkipsScan()
    {
        _instances.Current = null;
        var vm = CreateViewModel();

        await vm.RefreshCommand.ExecuteAsync(null);

        Assert.That(vm.StatusText, Is.EqualTo("还没有选定游戏实例。"));
        Assert.That(_service.ScanCount, Is.EqualTo(0));
        Assert.That(vm.Nodes, Is.Empty);
    }

    [Test]
    public async Task Refresh_BuildsTreeWithStatsAndStatus()
    {
        FillFixture();
        var vm = CreateViewModel();

        await vm.RefreshCommand.ExecuteAsync(null);

        Assert.That(vm.Nodes.Select(node => node.Name), Is.EqualTo(new[] { "Leftover", "Mixed", "loose.cfg" }),
            "目录在前，文件在后");
        Assert.That(vm.Nodes[0].Children.Select(node => node.Name), Is.EqualTo(new[] { "old.txt" }));
        Assert.That(vm.FileCount, Is.EqualTo(3));
        Assert.That(vm.DirectoryCount, Is.EqualTo(2));
        Assert.That(vm.TotalSizeBytes, Is.EqualTo(11), "目录尺寸不计入统计，避免与子文件重复");
        Assert.That(vm.StatsText, Is.EqualTo("未管理：3 个文件、2 个目录，合计 11 B"));
        Assert.That(vm.StatusText, Is.EqualTo("已扫描到 3 个未管理文件、2 个目录。"));
        Assert.That(vm.HasNodes, Is.True);
        Assert.That(vm.ShowEmptyState, Is.False);
        Assert.That(vm.HasError, Is.False);
    }

    [Test]
    public async Task Refresh_EmptyGameData_ShowsEmptyState()
    {
        var vm = CreateViewModel();

        await vm.RefreshCommand.ExecuteAsync(null);

        Assert.That(vm.ShowEmptyState, Is.True);
        Assert.That(vm.HasNodes, Is.False);
        Assert.That(vm.EmptyStateTitle, Is.EqualTo("GameData 里没有未管理的内容"));
        Assert.That(vm.StatusText, Is.EqualTo("GameData 里没有未管理的内容。"));
    }

    [Test]
    public async Task Refresh_ScanFailure_ShowsRetryableErrorAndClearErrorResets()
    {
        _service.ScanOverride = OperationResult<IReadOnlyList<UnmanagedFileInfo>>.Fail(
            "读取注册表失败，无法判断哪些内容受管。", "registry.locked", retryable: true);
        var vm = CreateViewModel();

        await vm.RefreshCommand.ExecuteAsync(null);

        Assert.That(vm.ErrorText, Is.EqualTo("读取注册表失败，无法判断哪些内容受管。（可重试）"));
        Assert.That(vm.ErrorDetail, Is.EqualTo("registry.locked"));
        Assert.That(vm.HasError, Is.True);
        Assert.That(vm.ShowEmptyState, Is.False, "扫描失败不算空状态");

        vm.ClearErrorCommand.Execute(null);

        Assert.That(vm.HasError, Is.False);
        Assert.That(vm.HasErrorDetail, Is.False);
    }

    [Test]
    public async Task ExpandAllAndCollapseAll_ToggleWholeTree()
    {
        FillFixture();
        var vm = CreateViewModel();
        await vm.RefreshCommand.ExecuteAsync(null);

        vm.ExpandAllCommand.Execute(null);

        Assert.That(vm.Nodes.Where(node => node.IsDirectory).All(node => node.IsExpanded), Is.True);
        Assert.That(vm.StatusText, Is.EqualTo("已展开全部目录（共 2 个）。"));

        vm.CollapseAllCommand.Execute(null);

        Assert.That(vm.Nodes.Where(node => node.IsDirectory).Any(node => node.IsExpanded), Is.False);
        Assert.That(vm.StatusText, Is.EqualTo("已折叠全部目录（共 2 个）。"));
    }

    [Test]
    public async Task Filter_PrunesTreeAndExpandsMatches_ResetRestores()
    {
        FillFixture();
        var vm = CreateViewModel();
        await vm.RefreshCommand.ExecuteAsync(null);

        vm.FilterText = "old";

        Assert.That(vm.HasFilter, Is.True);
        Assert.That(vm.Nodes.Select(node => node.Name), Is.EqualTo(new[] { "Leftover" }));
        Assert.That(vm.Nodes[0].IsExpanded, Is.True, "筛选命中后自动展开以便看到子项");
        Assert.That(vm.Nodes[0].Children.Select(node => node.Name), Is.EqualTo(new[] { "old.txt" }));

        vm.ResetFilterCommand.Execute(null);

        Assert.That(vm.HasFilter, Is.False);
        Assert.That(vm.Nodes, Has.Count.EqualTo(3));
        Assert.That(vm.StatusText, Is.EqualTo("已重置筛选。"));
    }

    [Test]
    public async Task Selection_CountsTopMostOnce_AndIgnoresChildren()
    {
        FillFixture();
        var vm = CreateViewModel();
        await vm.RefreshCommand.ExecuteAsync(null);
        var leftover = vm.Nodes.Single(node => node.Name == "Leftover");

        leftover.Children[0].IsSelected = true;

        Assert.That(vm.SelectedCount, Is.EqualTo(1));
        Assert.That(vm.SelectedPaths, Is.EqualTo(new[] { "Leftover/old.txt" }));
        Assert.That(vm.SelectionText, Is.EqualTo("已选中 1 项，合计 3 B"));

        leftover.IsSelected = true;

        Assert.That(vm.SelectedCount, Is.EqualTo(1), "父目录被选中后子项不再重复计数");
        Assert.That(vm.SelectedPaths, Is.EqualTo(new[] { "Leftover" }));
        Assert.That(vm.CanDeleteSelected, Is.True);
    }

    [Test]
    public async Task ToggleSelectAll_SelectsTopLevelOnly()
    {
        FillFixture();
        var vm = CreateViewModel();
        await vm.RefreshCommand.ExecuteAsync(null);

        vm.ToggleSelectAllCommand.Execute(null);

        Assert.That(vm.SelectedCount, Is.EqualTo(3));
        Assert.That(vm.SelectedPaths, Is.EquivalentTo(new[] { "Leftover", "Mixed", "loose.cfg" }));
        Assert.That(vm.SelectedSizeBytes, Is.EqualTo(11));
        Assert.That(vm.StatusText, Is.EqualTo("已选中 3 项。"));

        vm.ToggleSelectAllCommand.Execute(null);

        Assert.That(vm.SelectedCount, Is.EqualTo(0));
        Assert.That(vm.StatusText, Is.EqualTo("已取消全部选中。"));
    }

    [Test]
    public async Task PreviewDelete_UsesDryRun()
    {
        FillFixture();
        var vm = CreateViewModel();
        await vm.RefreshCommand.ExecuteAsync(null);
        vm.Nodes.Single(node => node.Name == "loose.cfg").IsSelected = true;

        await vm.PreviewDeleteCommand.ExecuteAsync(null);

        Assert.That(_service.Deletes, Has.Count.EqualTo(1));
        Assert.That(_service.Deletes[0].DryRun, Is.True, "预演必须走 dry-run");
        Assert.That(_service.Deletes[0].Paths, Is.EqualTo(new[] { "loose.cfg" }));
        Assert.That(vm.PreviewPaths, Is.EqualTo(new[] { "loose.cfg" }));
        Assert.That(vm.StatusText, Is.EqualTo("预演：将删除 1 项（未落盘）。"));
        Assert.That(_dialogs.ConfirmCount, Is.EqualTo(0), "预演不弹确认");
    }

    [Test]
    public async Task DeleteSelected_CancelledAtFirstConfirm_DoesNotTouchService()
    {
        FillFixture();
        var vm = CreateViewModel();
        await vm.RefreshCommand.ExecuteAsync(null);
        vm.Nodes.Single(node => node.Name == "loose.cfg").IsSelected = true;
        _dialogs.ConfirmResult = false;

        await vm.DeleteSelectedCommand.ExecuteAsync(null);

        Assert.That(_dialogs.ConfirmCount, Is.EqualTo(1));
        Assert.That(_dialogs.Titles[0], Is.EqualTo("删除未管理文件"));
        Assert.That(_service.Deletes, Is.Empty);
        Assert.That(vm.StatusText, Is.EqualTo("已取消删除。"));
    }

    [Test]
    public async Task DeleteSelected_NeedsSecondConfirmation()
    {
        FillFixture();
        var vm = CreateViewModel();
        await vm.RefreshCommand.ExecuteAsync(null);
        vm.Nodes.Single(node => node.Name == "loose.cfg").IsSelected = true;
        _dialogs.ConfirmResults.Enqueue(true);
        _dialogs.ConfirmResults.Enqueue(false);

        await vm.DeleteSelectedCommand.ExecuteAsync(null);

        Assert.That(_dialogs.ConfirmCount, Is.EqualTo(2));
        Assert.That(_dialogs.Titles[1], Is.EqualTo("再次确认删除"));
        Assert.That(_service.Deletes, Is.Empty, "二次确认被拒绝后不能落盘");
        Assert.That(vm.StatusText, Is.EqualTo("已取消删除。"));
    }

    [Test]
    public async Task DeleteSelected_AfterTwoConfirmations_DeletesAndRescans()
    {
        FillFixture();
        var vm = CreateViewModel();
        await vm.RefreshCommand.ExecuteAsync(null);
        vm.Nodes.Single(node => node.Name == "loose.cfg").IsSelected = true;
        _dialogs.ConfirmResults.Enqueue(true);
        _dialogs.ConfirmResults.Enqueue(true);

        await vm.DeleteSelectedCommand.ExecuteAsync(null);

        Assert.That(_dialogs.ConfirmCount, Is.EqualTo(2));
        Assert.That(_service.Deletes, Has.Count.EqualTo(1));
        Assert.That(_service.Deletes[0].DryRun, Is.False, "确认后必须真正删除");
        Assert.That(_service.Deletes[0].Paths, Is.EqualTo(new[] { "loose.cfg" }));
        Assert.That(_service.ScanCount, Is.EqualTo(2), "删除后重新扫描");
        Assert.That(vm.StatusText, Is.EqualTo("已删除 1 项未管理内容。"));
        Assert.That(vm.PreviewPaths, Is.Empty);
    }

    [Test]
    public async Task DeleteSelected_ServiceRefusal_ShowsNonRetryableError()
    {
        FillFixture();
        var vm = CreateViewModel();
        await vm.RefreshCommand.ExecuteAsync(null);
        vm.Nodes.Single(node => node.Name == "Mixed").IsSelected = true;
        _dialogs.ConfirmResult = true;
        _service.DeleteHandler = (_, _) => OperationResult<IReadOnlyList<string>>.Fail(
            "选中项包含受管内容，已拒绝删除：Mixed", "受管文件请到模组页卸载。", retryable: false);

        await vm.DeleteSelectedCommand.ExecuteAsync(null);

        Assert.That(vm.ErrorText, Is.EqualTo("选中项包含受管内容，已拒绝删除：Mixed（不可重试）"));
        Assert.That(vm.ErrorDetail, Is.EqualTo("受管文件请到模组页卸载。"));
        Assert.That(_service.ScanCount, Is.EqualTo(1), "失败时不重新扫描");
    }

    [Test]
    public async Task ShowInFolder_WithoutLauncher_ReportsError()
    {
        FillFixture();
        var vm = CreateViewModel();
        await vm.RefreshCommand.ExecuteAsync(null);
        vm.SelectedNode = vm.Nodes.Single(node => node.Name == "loose.cfg");

        Assert.That(vm.CanShowInFolder, Is.False);

        await vm.ShowInFolderCommand.ExecuteAsync(null);

        Assert.That(vm.ErrorText, Is.EqualTo("当前宿主没有提供「在文件夹中显示」能力。"));
    }

    [Test]
    public async Task ShowInFolder_UsesGameDataAbsolutePath()
    {
        FillFixture();
        var vm = CreateViewModel();
        await vm.RefreshCommand.ExecuteAsync(null);
        var launcher = new UnmanagedFakeShellLauncher();
        vm.ShellLauncher = launcher;
        vm.SelectedNode = vm.Nodes.Single(node => node.Name == "loose.cfg");

        Assert.That(vm.CanShowInFolder, Is.True);

        await vm.ShowInFolderCommand.ExecuteAsync(null);

        Assert.That(launcher.Calls, Is.EqualTo(1));
        Assert.That(launcher.LastPath, Is.EqualTo(Path.Combine(GameDir, "GameData", "loose.cfg")));
        Assert.That(vm.StatusText, Is.EqualTo("已在文件管理器中定位：loose.cfg"));
    }

    [Test]
    public void Activate_LoadsOnce_AndTracksInstanceChanges()
    {
        FillFixture();
        var vm = CreateViewModel();

        vm.Activate();
        Assert.That(_service.ScanCount, Is.EqualTo(1), "首次激活即扫描");

        vm.Activate();
        Assert.That(_service.ScanCount, Is.EqualTo(1), "重复激活不重复扫描");

        _instances.RaiseCurrentChanged(Instance());
        Assert.That(_service.ScanCount, Is.EqualTo(2), "切换实例后自动刷新");

        vm.Deactivate();
        _instances.RaiseCurrentChanged(null);
        Assert.That(_service.ScanCount, Is.EqualTo(2), "离开页面后不再响应实例变化");
    }

    [Test]
    public void NodeViewModel_FormatsFieldsAndGuardsManagedChildren()
    {
        var nodes = UnmanagedTreeBuilder.Build(new[]
        {
            Dir("Mixed", 5, managed: 1, unmanaged: 1),
            File("Mixed/junk.txt", 5),
            File("loose.cfg", 3),
            File("Stray/JunkMod.dll", 4, "JunkMod"),
            Dir("Stray", 4, managed: 0, unmanaged: 1),
        });

        var mixed = nodes.Single(node => node.Name == "Mixed");
        Assert.That(mixed.KindText, Is.EqualTo("目录"));
        Assert.That(mixed.ManagedText, Is.EqualTo("含受管 1 项"));
        Assert.That(mixed.HasManagedChildren, Is.True);
        Assert.That(mixed.CanDelete, Is.False, "含受管内容的目录不允许删除");
        Assert.That(mixed.SizeText, Is.EqualTo("5 B"));
        Assert.That(mixed.ModifiedText, Is.EqualTo("未知"));

        var loose = nodes.Single(node => node.Name == "loose.cfg");
        Assert.That(loose.KindText, Is.EqualTo("文件"));
        Assert.That(loose.ManagedText, Is.EqualTo("未管理"));
        Assert.That(loose.CanDelete, Is.True);
        Assert.That(loose.HasChildren, Is.False);

        var stray = nodes.Single(node => node.Name == "Stray");
        Assert.That(stray.ManagedText, Is.EqualTo("无受管内容"));
        Assert.That(stray.Children.Single().GuessIdentifier, Is.EqualTo("JunkMod"));
        Assert.That(stray.Children.Single().HasGuessIdentifier, Is.True);
    }

    private UnmanagedPageViewModel CreateViewModel() =>
        new(_service, _instances, _errors, _dialogs);

    private static InstanceInfo Instance() =>
        new("测试实例", GameDir, "1.12.5", IsDefault: true, IsLocked: false, ModuleCount: 1);

    private void FillFixture()
    {
        _service.Entries.Add(Dir("Leftover", 3, managed: 0, unmanaged: 1));
        _service.Entries.Add(File("Leftover/old.txt", 3));
        _service.Entries.Add(Dir("Mixed", 5, managed: 1, unmanaged: 1));
        _service.Entries.Add(File("Mixed/junk.txt", 5));
        _service.Entries.Add(File("loose.cfg", 3));
    }

    private static UnmanagedFileInfo Dir(string relativePath, long size, int managed, int unmanaged) =>
        new(relativePath, size, true, null)
        {
            ManagedChildCount = managed,
            UnmanagedChildCount = unmanaged,
        };

    private static UnmanagedFileInfo File(string relativePath, long size, string? guessIdentifier = null) =>
        new(relativePath, size, false, guessIdentifier) { UnmanagedChildCount = 1 };
}
