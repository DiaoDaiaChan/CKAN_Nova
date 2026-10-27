using CkanUI.Services.Abstractions;
using CkanUI.Services.Abstractions.Dtos;
using CkanUI.ViewModels.Pages;
using NUnit.Framework;

namespace CkanUI.ViewModels.Tests;

/// <summary>跨实例去重页（F-41 / UI-21）的视图模型口径：实例多选、预演默认、两次确认。</summary>
[TestFixture]
public sealed class DedupPageViewModelTests
{
    private DedupFakeService _service = null!;
    private UnmanagedFakeInstanceService _instances = null!;
    private UnmanagedFakeErrorReporter _errors = null!;
    private UnmanagedFakeDialogService _dialogs = null!;

    [SetUp]
    public void SetUp()
    {
        _service = new DedupFakeService();
        _instances = new UnmanagedFakeInstanceService();
        _errors = new UnmanagedFakeErrorReporter();
        _dialogs = new UnmanagedFakeDialogService();
        _instances.Add(Instance("甲"));
        _instances.Add(Instance("乙"));
        _instances.Current = _instances.Instances[0];
    }

    [Test]
    public void 打开页面默认预演且实例全选()
    {
        var vm = Create();

        Assert.That(vm.Title, Is.EqualTo("跨实例去重"));
        Assert.That(vm.DryRun, Is.True, "默认必须是预演，不许直接写盘");
        Assert.That(vm.Instances.Select(option => option.Name), Is.EquivalentTo(new[] { "甲", "乙" }));
        Assert.That(vm.SelectedInstanceCount, Is.EqualTo(2));
        Assert.That(vm.CanScan, Is.True);
        Assert.That(vm.CanExecute, Is.False);
        Assert.That(vm.ExecuteText, Is.EqualTo("预演去重"));
        Assert.That(vm.SelectionText, Is.EqualTo("已选 2 / 2 个实例"));
    }

    [Test]
    public void 扫描后表格按组展示文件数与可释放字节()
    {
        _service.ScanOverride = OperationResult<DedupScanReport>.Ok(DedupFakeService.DuplicateReport());
        var vm = Create();

        vm.ScanCommand.Execute(null);
        Wait(vm);

        Assert.That(vm.Groups, Has.Count.EqualTo(1));
        Assert.That(vm.GroupCount, Is.EqualTo(1));
        Assert.That(vm.DuplicateFileCount, Is.EqualTo(2));
        Assert.That(vm.ReclaimableBytes, Is.EqualTo(4096));
        Assert.That(vm.Groups[0].FileCount, Is.EqualTo(2));
        Assert.That(vm.Groups[0].ReclaimableText, Is.EqualTo("4 KB"));
        Assert.That(vm.Groups[0].InstanceNamesText, Is.EqualTo("甲、乙"));
        Assert.That(vm.HasGroups, Is.True);
        Assert.That(vm.HasSelectedGroup, Is.True);
        Assert.That(vm.SelectedGroupFiles, Has.Count.EqualTo(2));
        Assert.That(vm.CanExecute, Is.True);
        Assert.That(vm.StatsText, Does.Contain("可释放"));
        Assert.That(vm.StatusText, Does.Contain("扫描完成"));
    }

    [Test]
    public void 扫描只带上勾选的实例与最小尺寸()
    {
        var vm = Create();
        vm.MinimumFileSizeKiB = 64;
        vm.Instances.First(option => option.Name == "乙").IsSelected = false;

        vm.ScanCommand.Execute(null);
        Wait(vm);

        Assert.That(_service.Scans, Has.Count.EqualTo(1));
        Assert.That(_service.Scans[0].InstanceNames, Is.EquivalentTo(new[] { "甲" }));
        Assert.That(_service.Scans[0].MinimumFileSizeBytes, Is.EqualTo(64 * 1024));
    }

    [Test]
    public void 没有勾选实例时不能扫描()
    {
        var vm = Create();

        vm.ToggleAllCommand.Execute(null);

        Assert.That(vm.SelectedInstanceCount, Is.Zero);
        Assert.That(vm.CanScan, Is.False);
    }

    [Test]
    public void 预演执行不需要确认也不改盘()
    {
        _service.ScanOverride = OperationResult<DedupScanReport>.Ok(DedupFakeService.DuplicateReport());
        _service.ExecuteOverride = OperationResult<DedupExecuteReport>.Ok(new DedupExecuteReport(
            true,
            DedupPlatformSupport.Supported,
            new[]
            {
                new DedupLinkResult("乙", "GameData/a.cfg", "/x/乙/GameData/a.cfg", "/x/甲/GameData/a.cfg",
                    DedupLinkOutcome.Planned, "预演：将改成硬链接。", 4096),
            },
            4096));
        var vm = Create();
        vm.ScanCommand.Execute(null);
        Wait(vm);

        vm.ExecuteCommand.Execute(null);
        Wait(vm);

        Assert.That(_dialogs.ConfirmCount, Is.Zero, "预演不写盘，不应该弹确认");
        Assert.That(_service.Executes, Has.Count.EqualTo(1));
        Assert.That(_service.Executes[0].DryRun, Is.True);
        Assert.That(vm.Results, Has.Count.EqualTo(1));
        Assert.That(vm.Results[0].OutcomeText, Is.EqualTo("预演"));
        Assert.That(vm.StatusText, Does.Contain("预演完成"));
        Assert.That(vm.Groups, Is.Not.Empty, "预演不改变结果表");
    }

    [Test]
    public void 写盘要两次确认且第一次拒绝就不执行()
    {
        _service.ScanOverride = OperationResult<DedupScanReport>.Ok(DedupFakeService.DuplicateReport());
        var vm = Create();
        vm.ScanCommand.Execute(null);
        Wait(vm);
        vm.DryRun = false;
        _dialogs.ConfirmResults.Enqueue(false);

        vm.ExecuteCommand.Execute(null);
        Wait(vm);

        Assert.That(_dialogs.ConfirmCount, Is.EqualTo(1));
        Assert.That(_service.Executes, Is.Empty, "用户拒绝后不许调用执行");
        Assert.That(vm.StatusText, Does.Contain("已取消"));
        Assert.That(vm.ExecuteText, Is.EqualTo("执行去重"));
        Assert.That(vm.ConfirmHintText, Does.Contain("共用同一份数据"));
    }

    [Test]
    public void 写盘第二次拒绝也不执行()
    {
        _service.ScanOverride = OperationResult<DedupScanReport>.Ok(DedupFakeService.DuplicateReport());
        var vm = Create();
        vm.ScanCommand.Execute(null);
        Wait(vm);
        vm.DryRun = false;
        _dialogs.ConfirmResults.Enqueue(true);
        _dialogs.ConfirmResults.Enqueue(false);

        vm.ExecuteCommand.Execute(null);
        Wait(vm);

        Assert.That(_dialogs.ConfirmCount, Is.EqualTo(2));
        Assert.That(_service.Executes, Is.Empty);
    }

    [Test]
    public void 两次确认后写盘并清空结果表要求重新扫描()
    {
        _service.ScanOverride = OperationResult<DedupScanReport>.Ok(DedupFakeService.DuplicateReport());
        _service.ExecuteOverride = OperationResult<DedupExecuteReport>.Ok(new DedupExecuteReport(
            false,
            DedupPlatformSupport.Supported,
            new[]
            {
                new DedupLinkResult("乙", "GameData/a.cfg", "/x/乙/GameData/a.cfg", "/x/甲/GameData/a.cfg",
                    DedupLinkOutcome.Linked, "已改成硬链接。", 4096),
            },
            4096));
        var vm = Create();
        vm.ScanCommand.Execute(null);
        Wait(vm);
        vm.DryRun = false;
        _dialogs.ConfirmResults.Enqueue(true);
        _dialogs.ConfirmResults.Enqueue(true);

        vm.ExecuteCommand.Execute(null);
        Wait(vm);

        Assert.That(_dialogs.ConfirmCount, Is.EqualTo(2));
        Assert.That(_service.Executes, Has.Count.EqualTo(1));
        Assert.That(_service.Executes[0].DryRun, Is.False);
        Assert.That(vm.Results, Has.Count.EqualTo(1));
        Assert.That(vm.Results[0].OutcomeText, Is.EqualTo("已建链"));
        Assert.That(vm.Groups, Is.Empty, "写盘后旧结果不再成立");
        Assert.That(vm.CanExecute, Is.False, "写盘后必须重新扫描才能再执行");
        Assert.That(vm.StatusText, Does.Contain("已建立 1 个硬链接"));
    }

    [Test]
    public void 扫描失败时显示可读错误()
    {
        _service.ScanOverride = OperationResult<DedupScanReport>.Fail("磁盘读不动了。", "detail", retryable: true);
        var vm = Create();

        vm.ScanCommand.Execute(null);
        Wait(vm);

        Assert.That(vm.HasError, Is.True);
        Assert.That(vm.ErrorText, Does.Contain("磁盘读不动了。"));
        Assert.That(vm.ErrorText, Does.Contain("可重试"));
        Assert.That(vm.ErrorDetail, Is.EqualTo("detail"));
        Assert.That(vm.ShowEmptyState, Is.False);
    }

    [Test]
    public void 平台不支持硬链接时执行按钮不可用并给出说明()
    {
        _service.ScanOverride = OperationResult<DedupScanReport>.Ok(
            DedupFakeService.DuplicateReport(DedupPlatformSupport.UnsupportedPlatform));
        var vm = Create();

        vm.ScanCommand.Execute(null);
        Wait(vm);

        Assert.That(vm.IsSupported, Is.False);
        Assert.That(vm.CanExecute, Is.False);
        Assert.That(vm.UnsupportedHint, Does.Contain("不支持创建硬链接"));
    }

    [Test]
    public void 没有重复内容时展示空态()
    {
        var vm = Create();

        vm.ScanCommand.Execute(null);
        Wait(vm);

        Assert.That(vm.ShowEmptyState, Is.True);
        Assert.That(vm.EmptyStateTitle, Does.Contain("没有发现"));
        Assert.That(vm.EmptyStateHint, Does.Contain("128 KiB"));
    }

    [Test]
    public void 实例清单变化会刷新可选项()
    {
        var vm = Create();
        vm.Activate();

        _instances.Add(Instance("丙"));
        _instances.RaiseInstancesChanged();

        Assert.That(vm.Instances.Select(option => option.Name), Is.EquivalentTo(new[] { "甲", "乙", "丙" }));
        Assert.That(vm.SelectedInstanceCount, Is.EqualTo(3));

        vm.Deactivate();
        _instances.Add(Instance("丁"));
        _instances.RaiseInstancesChanged();

        Assert.That(vm.Instances, Has.Count.EqualTo(3), "失活后不再跟着刷新");
    }

    private DedupPageViewModel Create() => new(_service, _instances, _errors, _dialogs);

    private static InstanceInfo Instance(string name) => new(name, $"C:/games/{name}", "1.12.5", false, false, 3);

    /// <summary>命令是 fire-and-forget 的 async void 入口，等 IsBusy 落回 false。</summary>
    private static void Wait(DedupPageViewModel vm)
    {
        for (var i = 0; i < 200 && vm.IsBusy; i++)
        {
            Thread.Sleep(10);
        }

        Thread.Sleep(10);
    }
}
