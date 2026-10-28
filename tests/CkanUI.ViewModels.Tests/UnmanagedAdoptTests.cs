using CkanUI.Services.Abstractions;
using CkanUI.Services.Abstractions.Dtos;
using CkanUI.ViewModels.Pages;
using CkanUI.ViewModels.Pages.Unmanaged;
using NUnit.Framework;

namespace CkanUI.ViewModels.Tests;

/// <summary>
/// 未管理文件页的接管命令（12 篇 F-42 / INT-09）：一次确认、成功后重扫、拒绝项聚合进错误条。
/// </summary>
[TestFixture]
public sealed class UnmanagedAdoptTests
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
        _service.Entries.Add(new UnmanagedFileInfo("Stray", 4, true, null)
        {
            ManagedChildCount = 0,
            UnmanagedChildCount = 1,
        });
        _service.Entries.Add(new UnmanagedFileInfo("Stray/JunkMod.dll", 4, false, "JunkMod"));
        _service.Entries.Add(new UnmanagedFileInfo("loose.cfg", 3, false, null));
    }

    [Test]
    public async Task AdoptSelected_CancelledAtConfirmation_DoesNotWrite()
    {
        var vm = CreateViewModel();
        await vm.RefreshCommand.ExecuteAsync(null);
        vm.Nodes.Single(node => node.Name == "Stray").IsSelected = true;
        _dialogs.ConfirmResult = false;

        await vm.AdoptSelectedCommand.ExecuteAsync(null);

        Assert.That(_dialogs.ConfirmCount, Is.EqualTo(1), "接管只要一次确认（写前会自动快照）");
        Assert.That(_dialogs.Titles[0], Is.EqualTo("接管为受管安装"));
        Assert.That(_dialogs.LastDetail, Does.Contain("registry.json"));
        Assert.That(_service.Adopts, Is.Empty);
        Assert.That(vm.StatusText, Is.EqualTo("已取消接管。"));
    }

    [Test]
    public async Task AdoptSelected_ReportsCountsAndRescans()
    {
        var vm = CreateViewModel();
        await vm.RefreshCommand.ExecuteAsync(null);
        vm.Nodes.Single(node => node.Name == "Stray").IsSelected = true;
        _dialogs.ConfirmResult = true;
        _service.AdoptHandler = (paths, dryRun) => OperationResult<UnmanagedAdoptResult>.Ok(new UnmanagedAdoptResult(
            new[]
            {
                new UnmanagedAdoptItem("Stray/JunkMod.dll", "JunkMod", UnmanagedAdoptOutcome.Adopted),
                new UnmanagedAdoptItem("Stray/old.dll", "old", UnmanagedAdoptOutcome.Skipped, "该文件已由注册表登记为受管。"),
            },
            new[] { "registry.json.bak-20261028-101010" },
            Applied: !dryRun));

        await vm.AdoptSelectedCommand.ExecuteAsync(null);

        Assert.That(_service.Adopts, Has.Count.EqualTo(1));
        Assert.That(_service.Adopts[0].DryRun, Is.False, "确认后要真正写注册表");
        Assert.That(_service.Adopts[0].Paths, Is.EqualTo(new[] { "Stray" }), "传的是选中项，目录由服务展开");
        Assert.That(_service.ScanCount, Is.EqualTo(2), "接管成功后重新扫描");
        Assert.That(vm.StatusText, Is.EqualTo("已接管 1 个、跳过 1 个、拒绝 0 个。"));
        Assert.That(vm.HasError, Is.False, "没有拒绝项就不该报错");
    }

    [Test]
    public async Task AdoptSelected_PartialRefusal_ListsTheReasons()
    {
        var vm = CreateViewModel();
        await vm.RefreshCommand.ExecuteAsync(null);
        vm.Nodes.Single(node => node.Name == "Stray").IsSelected = true;
        _dialogs.ConfirmResult = true;
        _service.AdoptHandler = (_, _) => OperationResult<UnmanagedAdoptResult>.Ok(new UnmanagedAdoptResult(
            new[]
            {
                new UnmanagedAdoptItem("Stray/JunkMod.dll", "JunkMod", UnmanagedAdoptOutcome.Adopted),
                new UnmanagedAdoptItem("Stray/readme.txt", null, UnmanagedAdoptOutcome.Refused, "只有 .dll 文件可以接管为受管安装。"),
                new UnmanagedAdoptItem("Stray/JunkMod2.dll", "JunkMod", UnmanagedAdoptOutcome.Refused, "标识符“JunkMod”在本批里重复。"),
            },
            Applied: true));

        await vm.AdoptSelectedCommand.ExecuteAsync(null);

        Assert.That(vm.ErrorText, Is.EqualTo("有 2 项被拒绝接管。"));
        Assert.That(vm.ErrorDetail, Does.Contain("readme.txt"));
        Assert.That(vm.ErrorDetail, Does.Contain("重复"));
        Assert.That(vm.StatusText, Is.EqualTo("已接管 1 个、跳过 0 个、拒绝 2 个。"));
    }

    [Test]
    public async Task AdoptSelected_WithoutSelection_DoesNotAskForConfirmation()
    {
        var vm = CreateViewModel();
        await vm.RefreshCommand.ExecuteAsync(null);

        await vm.AdoptSelectedCommand.ExecuteAsync(null);

        Assert.That(_dialogs.ConfirmCount, Is.EqualTo(0));
        Assert.That(_service.Adopts, Is.Empty);
        Assert.That(vm.ErrorText, Is.EqualTo("没有选中要接管的内容。"));
    }

    [Test]
    public async Task AdoptSelected_ServiceFailure_IsSurfacedAsRetryable()
    {
        var vm = CreateViewModel();
        await vm.RefreshCommand.ExecuteAsync(null);
        vm.Nodes.Single(node => node.Name == "Stray").IsSelected = true;
        _dialogs.ConfirmResult = true;
        _service.AdoptHandler = (_, _) => OperationResult<UnmanagedAdoptResult>.Fail(
            "写入注册表失败。", "磁盘只读。", retryable: true);

        await vm.AdoptSelectedCommand.ExecuteAsync(null);

        Assert.That(vm.ErrorText, Is.EqualTo("写入注册表失败。（可重试）"));
        Assert.That(vm.ErrorDetail, Is.EqualTo("磁盘只读。"));
        Assert.That(_service.ScanCount, Is.EqualTo(1), "失败时不重新扫描");
    }

    [Test]
    public async Task AdoptSelected_WithoutInstance_ShowsHint()
    {
        _instances.Current = null;
        var vm = CreateViewModel();

        await vm.AdoptSelectedCommand.ExecuteAsync(null);

        Assert.That(_dialogs.ConfirmCount, Is.EqualTo(0));
        Assert.That(vm.ErrorText, Is.EqualTo("还没有选定游戏实例。"));
    }

    [Test]
    public async Task CanAdoptSelected_TracksSelectionAndBusyState()
    {
        var vm = CreateViewModel();
        await vm.RefreshCommand.ExecuteAsync(null);

        Assert.That(vm.CanAdoptSelected, Is.False, "没选中时按钮要禁用");

        vm.Nodes.Single(node => node.Name == "loose.cfg").IsSelected = true;

        Assert.That(vm.CanAdoptSelected, Is.True);
        Assert.That(vm.CanDeleteSelected, Is.True, "接管与删除共用同一份选中状态");
    }

    private UnmanagedPageViewModel CreateViewModel() => new(_service, _instances, _errors, _dialogs);

    private static InstanceInfo Instance() =>
        new("测试实例", GameDir, "1.12.5", IsDefault: true, IsLocked: false, ModuleCount: 1);
}
