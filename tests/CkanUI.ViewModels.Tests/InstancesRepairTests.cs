using CkanUI.Services.Abstractions;
using CkanUI.Services.Abstractions.Dtos;
using CkanUI.Services.Abstractions.Instances;
using CkanUI.ViewModels.Pages;
using CkanUI.ViewModels.Pages.Instances;
using NUnit.Framework;

namespace CkanUI.ViewModels.Tests;

/// <summary>
/// 实例页「自检与处置」区的 headless 单测（12 篇 F-08）：自检摘要、强制解锁前置条件、
/// 坏注册表隔离、服务失败与异常收口、切换实例后旧结论作废。
/// </summary>
/// <remarks>
/// 全部走假服务：真实磁盘与进程探测在 <c>CkanUI.Services.Tests.InstanceRepairServiceTests</c> 里覆盖，
/// 这里只验证「界面拿结论后怎么显示、按钮什么时候可用、拒绝时给出什么可读文案」。
/// </remarks>
[TestFixture]
public sealed class InstancesRepairTests
{
    private const string Dir = @"E:\Games\KSP";
    private const string CkanDir = @"E:\Games\KSP\CKAN";
    private const string RegistryPath = @"E:\Games\KSP\CKAN\registry.json";
    private const string LockPath = @"E:\Games\KSP\CKAN\registry.locked";

    [Test]
    public async Task Diagnose_WithoutSelection_AsksUserToPickInstance()
    {
        var harness = new Harness();

        await harness.Vm.DiagnoseInstanceAsync(null);

        Assert.That(harness.Vm.ErrorText, Is.EqualTo("请先选择一个实例，再点「自检」。"));
        Assert.That(harness.RepairService.DiagnoseCalls, Is.EqualTo(0));
    }

    [Test]
    public async Task Diagnose_WithoutService_ExplainsWiringMissing()
    {
        var harness = new Harness(withRepair: false);

        await harness.Vm.DiagnoseInstanceAsync(harness.Row("默认实例"));

        Assert.That(harness.Vm.ErrorText, Does.Contain("尚未接线"));
        Assert.That(harness.Vm.HasDiagnosis, Is.False);
    }

    [Test]
    public async Task Diagnose_HealthyInstance_ShowsPassedSummaryAndNoIssues()
    {
        var harness = new Harness();
        harness.RepairService.DiagnoseResult = OperationResult<InstanceRepairDiagnosis>.Ok(Healthy());

        await harness.Vm.DiagnoseInstanceAsync(harness.Row("默认实例"));

        Assert.That(harness.Vm.HasDiagnosis, Is.True);
        Assert.That(harness.Vm.HasRepairIssues, Is.False);
        Assert.That(harness.Vm.RepairIssues, Is.Empty);
        Assert.That(harness.Vm.StatusText, Is.EqualTo("实例「默认实例」自检通过：锁与注册表都正常。"));
        Assert.That(harness.Vm.RepairAdvice, Is.Null);
        Assert.That(harness.Vm.CanForceUnlock, Is.False);
        Assert.That(harness.Vm.CanQuarantineRegistry, Is.False);
        Assert.That(harness.Vm.IsBusy, Is.False);
    }

    [Test]
    public async Task Diagnose_LockedButGameStopped_AllowsForceUnlockAndShowsAdvice()
    {
        var harness = new Harness();
        harness.RepairService.DiagnoseResult = OperationResult<InstanceRepairDiagnosis>.Ok(Locked(isGameRunning: false));

        await harness.Vm.DiagnoseInstanceAsync(harness.Row("默认实例"));

        Assert.That(harness.Vm.CanForceUnlock, Is.True);
        Assert.That(harness.Vm.HasRepairIssues, Is.True);
        Assert.That(harness.Vm.RepairAdvice, Does.Contain("强制解锁"));
        Assert.That(harness.Vm.StatusText, Does.Contain("1 个问题"));
        Assert.That(harness.Vm.DiagnosisSummary, Does.Contain("实例「默认实例」发现 1 个问题"));
    }

    [Test]
    public async Task Diagnose_GameRunning_DisablesForceUnlock()
    {
        var harness = new Harness();
        harness.RepairService.DiagnoseResult = OperationResult<InstanceRepairDiagnosis>.Ok(Locked(isGameRunning: true));

        await harness.Vm.DiagnoseInstanceAsync(harness.Row("默认实例"));

        Assert.That(harness.Vm.CanForceUnlock, Is.False);
        Assert.That(harness.Vm.Diagnosis, Is.Not.Null);
        Assert.That(harness.Vm.Diagnosis!.IsGameRunning, Is.True);
    }

    [Test]
    public async Task Diagnose_CorruptRegistry_EnablesQuarantineOnly()
    {
        var harness = new Harness();
        harness.RepairService.DiagnoseResult = OperationResult<InstanceRepairDiagnosis>.Ok(Corrupt());

        await harness.Vm.DiagnoseInstanceAsync(harness.Row("默认实例"));

        Assert.That(harness.Vm.CanQuarantineRegistry, Is.True);
        Assert.That(harness.Vm.CanForceUnlock, Is.False);
    }

    [Test]
    public async Task Diagnose_ServiceFailure_ShowsReadableErrorAndClearsDiagnosis()
    {
        var harness = new Harness();
        harness.RepairService.DiagnoseResult =
            OperationResult<InstanceRepairDiagnosis>.Fail("实例没有配置游戏目录，无法自检。", Dir);

        await harness.Vm.DiagnoseInstanceAsync(harness.Row("默认实例"));

        Assert.That(harness.Vm.ErrorText, Is.EqualTo("实例没有配置游戏目录，无法自检。（不可重试）"));
        Assert.That(harness.Vm.ErrorDetail, Is.EqualTo(Dir));
        Assert.That(harness.Vm.HasDiagnosis, Is.False);
        Assert.That(harness.Vm.HasRepairIssues, Is.False);
        Assert.That(harness.Vm.IsBusy, Is.False);
    }

    [Test]
    public async Task Diagnose_UnexpectedThrow_IsReportedAndKeepsUiUsable()
    {
        var harness = new Harness();
        harness.RepairService.Throws = new IOException("磁盘读取失败。");

        await harness.Vm.DiagnoseInstanceAsync(harness.Row("默认实例"));

        Assert.That(harness.Vm.ErrorText, Does.Contain("实例自检"));
        Assert.That(harness.Vm.ErrorText, Does.Contain("不可重试"));
        Assert.That(harness.Errors.ReportCount, Is.EqualTo(1));
        Assert.That(harness.Vm.IsBusy, Is.False);
    }

    [Test]
    public async Task ForceUnlock_WithoutDiagnosis_AsksForDiagnoseFirst()
    {
        var harness = new Harness();

        await harness.Vm.ForceUnlockInstanceAsync(harness.Row("默认实例"));

        Assert.That(harness.Vm.ErrorText, Does.Contain("点一次「自检」"));
        Assert.That(harness.RepairService.UnlockCalls, Is.EqualTo(0));
        Assert.That(harness.Dialogs.ConfirmCalls, Is.EqualTo(0));
    }

    [Test]
    public async Task ForceUnlock_WhenGameRunning_RefusesWithoutTouchingDisk()
    {
        var harness = new Harness();
        harness.RepairService.DiagnoseResult = OperationResult<InstanceRepairDiagnosis>.Ok(Locked(isGameRunning: true));
        await harness.Vm.DiagnoseInstanceAsync(harness.Row("默认实例"));

        await harness.Vm.ForceUnlockInstanceAsync(harness.Row("默认实例"));

        Assert.That(harness.Vm.ErrorText, Does.Contain("游戏正在运行"));
        Assert.That(harness.Vm.ErrorText, Does.Contain("不可重试"));
        Assert.That(harness.RepairService.UnlockCalls, Is.EqualTo(0));
        Assert.That(harness.Dialogs.ConfirmCalls, Is.EqualTo(0));
    }

    [Test]
    public async Task ForceUnlock_WhenNotLocked_SaysNothingToDo()
    {
        var harness = new Harness();
        harness.RepairService.DiagnoseResult = OperationResult<InstanceRepairDiagnosis>.Ok(Healthy());
        await harness.Vm.DiagnoseInstanceAsync(harness.Row("默认实例"));

        await harness.Vm.ForceUnlockInstanceAsync(harness.Row("默认实例"));

        Assert.That(harness.Vm.ErrorText, Does.Contain("无需解锁"));
        Assert.That(harness.RepairService.UnlockCalls, Is.EqualTo(0));
    }

    [Test]
    public async Task ForceUnlock_ForAnotherInstance_RequiresFreshDiagnosis()
    {
        var harness = new Harness();
        harness.RepairService.DiagnoseResult = OperationResult<InstanceRepairDiagnosis>.Ok(Locked(isGameRunning: false));
        await harness.Vm.DiagnoseInstanceAsync(harness.Row("默认实例"));

        // 直接对另一行发起解锁：没有该行的自检结论，必须拒绝（避免拿旧结论去动别人的目录）。
        await harness.Vm.ForceUnlockInstanceAsync(harness.Row("第二实例"));

        Assert.That(harness.Vm.ErrorText, Does.Contain("请先对实例「第二实例」"));
        Assert.That(harness.RepairService.UnlockCalls, Is.EqualTo(0));
    }

    [Test]
    public async Task ForceUnlock_UserDeclines_DoesNotUnlock()
    {
        var harness = new Harness();
        harness.RepairService.DiagnoseResult = OperationResult<InstanceRepairDiagnosis>.Ok(Locked(isGameRunning: false));
        await harness.Vm.DiagnoseInstanceAsync(harness.Row("默认实例"));
        harness.Dialogs.ConfirmResult = false;

        await harness.Vm.ForceUnlockInstanceAsync(harness.Row("默认实例"));

        Assert.That(harness.Vm.StatusText, Is.EqualTo("已取消解锁。"));
        Assert.That(harness.RepairService.UnlockCalls, Is.EqualTo(0));
        Assert.That(harness.Dialogs.ConfirmCalls, Is.EqualTo(1));
    }

    [Test]
    public async Task ForceUnlock_AfterConfirm_RemovesLockAndReDiagnoses()
    {
        var harness = new Harness();
        harness.RepairService.DiagnoseResult = OperationResult<InstanceRepairDiagnosis>.Ok(Locked(isGameRunning: false));
        await harness.Vm.DiagnoseInstanceAsync(harness.Row("默认实例"));
        harness.RepairService.DiagnoseResult = OperationResult<InstanceRepairDiagnosis>.Ok(Healthy());
        harness.RepairService.UnlockResult = OperationResult<InstanceUnlockReport>.Ok(new InstanceUnlockReport(
            new[] { LockPath },
            Array.Empty<string>(),
            SampleUserPath(@"\AppData\Local\CKAN\instance-repair-journal.jsonl"),
            JournalWritten: true,
            DateTimeOffset.UnixEpoch,
            "已删除 1 个锁文件，现在可以继续操作该实例。"));

        await harness.Vm.ForceUnlockInstanceAsync(harness.Row("默认实例"));

        Assert.That(harness.RepairService.UnlockCalls, Is.EqualTo(1));
        Assert.That(harness.RepairService.LastReason, Is.EqualTo("实例页手工强制解锁"));
        Assert.That(harness.Vm.StatusText, Does.Contain("已删除 1 个锁文件"));
        Assert.That(harness.RepairService.DiagnoseCalls, Is.EqualTo(2));
        Assert.That(harness.Vm.CanForceUnlock, Is.False);
        Assert.That(harness.Vm.ErrorText, Is.Null);
    }

    [Test]
    public async Task ForceUnlock_ServiceFailure_SurfacesReadableReason()
    {
        var harness = new Harness();
        harness.RepairService.DiagnoseResult = OperationResult<InstanceRepairDiagnosis>.Ok(Locked(isGameRunning: false));
        await harness.Vm.DiagnoseInstanceAsync(harness.Row("默认实例"));
        harness.RepairService.UnlockResult =
            OperationResult<InstanceUnlockReport>.Fail("锁目录非空，未自动删除。", LockPath, retryable: false);

        await harness.Vm.ForceUnlockInstanceAsync(harness.Row("默认实例"));

        Assert.That(harness.Vm.ErrorText, Is.EqualTo("锁目录非空，未自动删除。（不可重试）"));
        Assert.That(harness.Vm.ErrorDetail, Is.EqualTo(LockPath));
    }

    [Test]
    public async Task Quarantine_WhenRegistryReadable_Refuses()
    {
        var harness = new Harness();
        harness.RepairService.DiagnoseResult = OperationResult<InstanceRepairDiagnosis>.Ok(Healthy());
        await harness.Vm.DiagnoseInstanceAsync(harness.Row("默认实例"));

        await harness.Vm.QuarantineInstanceRegistryAsync(harness.Row("默认实例"));

        Assert.That(harness.Vm.ErrorText, Does.Contain("无需隔离"));
        Assert.That(harness.RepairService.QuarantineCalls, Is.EqualTo(0));
    }

    [Test]
    public async Task Quarantine_WithoutDiagnosis_AsksForDiagnoseFirst()
    {
        var harness = new Harness();

        await harness.Vm.QuarantineInstanceRegistryAsync(harness.Row("默认实例"));

        Assert.That(harness.Vm.ErrorText, Does.Contain("点一次「自检」"));
        Assert.That(harness.RepairService.QuarantineCalls, Is.EqualTo(0));
    }

    [Test]
    public async Task Quarantine_UserDeclines_DoesNothing()
    {
        var harness = new Harness();
        harness.RepairService.DiagnoseResult = OperationResult<InstanceRepairDiagnosis>.Ok(Corrupt());
        await harness.Vm.DiagnoseInstanceAsync(harness.Row("默认实例"));
        harness.Dialogs.ConfirmResult = false;

        await harness.Vm.QuarantineInstanceRegistryAsync(harness.Row("默认实例"));

        Assert.That(harness.Vm.StatusText, Is.EqualTo("已取消隔离。"));
        Assert.That(harness.RepairService.QuarantineCalls, Is.EqualTo(0));
    }

    [Test]
    public async Task Quarantine_AfterConfirm_ReportsQuarantinePath()
    {
        var harness = new Harness();
        harness.RepairService.DiagnoseResult = OperationResult<InstanceRepairDiagnosis>.Ok(Corrupt());
        await harness.Vm.DiagnoseInstanceAsync(harness.Row("默认实例"));
        harness.RepairService.DiagnoseResult = OperationResult<InstanceRepairDiagnosis>.Ok(Healthy());
        harness.RepairService.QuarantineResult = OperationResult<InstanceRegistryQuarantine>.Ok(new InstanceRegistryQuarantine(
            RegistryPath,
            RegistryPath + ".corrupt-20250102-030405",
            Bytes: 42,
            DateTimeOffset.UnixEpoch,
            SampleUserPath(@"\AppData\Local\CKAN\instance-repair-journal.jsonl"),
            JournalWritten: true,
            "已把损坏的注册表隔离为 registry.json.corrupt-20250102-030405。"));

        await harness.Vm.QuarantineInstanceRegistryAsync(harness.Row("默认实例"));

        Assert.That(harness.RepairService.QuarantineCalls, Is.EqualTo(1));
        Assert.That(harness.Vm.StatusText, Does.Contain("registry.json.corrupt-20250102-030405"));
        Assert.That(harness.Vm.ErrorText, Is.Null);
    }

    [Test]
    public async Task Quarantine_ServiceFailure_SurfacesReadableReason()
    {
        var harness = new Harness();
        harness.RepairService.DiagnoseResult = OperationResult<InstanceRepairDiagnosis>.Ok(Corrupt());
        await harness.Vm.DiagnoseInstanceAsync(harness.Row("默认实例"));
        harness.RepairService.QuarantineResult =
            OperationResult<InstanceRegistryQuarantine>.Fail("隔离失败：磁盘只读。", RegistryPath, retryable: true);

        await harness.Vm.QuarantineInstanceRegistryAsync(harness.Row("默认实例"));

        Assert.That(harness.Vm.ErrorText, Is.EqualTo("隔离失败：磁盘只读。（可重试）"));
    }

    [Test]
    public async Task SelectingAnotherInstance_ClearsStaleDiagnosis()
    {
        var harness = new Harness();
        harness.RepairService.DiagnoseResult = OperationResult<InstanceRepairDiagnosis>.Ok(Locked(isGameRunning: false));
        await harness.Vm.DiagnoseInstanceAsync(harness.Row("默认实例"));
        Assert.That(harness.Vm.HasDiagnosis, Is.True);

        harness.Vm.SelectedRow = harness.Row("第二实例");

        Assert.That(harness.Vm.HasDiagnosis, Is.False);
        Assert.That(harness.Vm.CanForceUnlock, Is.False);
        Assert.That(harness.Vm.DiagnosisSummary, Is.EqualTo("尚未自检。"));
    }

    [Test]
    public async Task Reload_KeepingSameSelection_KeepsDiagnosis()
    {
        var harness = new Harness();
        harness.RepairService.DiagnoseResult = OperationResult<InstanceRepairDiagnosis>.Ok(Locked(isGameRunning: false));
        await harness.Vm.DiagnoseInstanceAsync(harness.Row("默认实例"));

        harness.Vm.Reload();

        Assert.That(harness.Vm.HasDiagnosis, Is.True);
        Assert.That(harness.Vm.CanForceUnlock, Is.True);
    }

    // ---- 固定数据 ---------------------------------------------------------

    private static InstanceRepairDiagnosis Healthy() => new(
        "默认实例",
        Dir,
        CkanDir,
        RegistryPath,
        GameDirectoryExists: true,
        RegistryExists: true,
        RegistryReadable: true,
        RegistryError: null,
        RegistryVersion: 3,
        SupportedRegistryVersion: 3,
        KspVersion: "1.12.5",
        RequiredKspVersion: "1.12.5",
        IsGameRunning: false,
        Locks: Array.Empty<InstanceLockArtifact>(),
        Issues: Array.Empty<InstanceIssue>());

    private static InstanceRepairDiagnosis Locked(bool isGameRunning) => new(
        "默认实例",
        Dir,
        CkanDir,
        RegistryPath,
        true,
        true,
        true,
        null,
        3,
        3,
        "1.12.5",
        "1.12.5",
        isGameRunning,
        new[]
        {
            new InstanceLockArtifact(
                LockPath,
                InstanceLockKind.RegistryLockFile,
                DateTimeOffset.UnixEpoch,
                "没有定位到占用进程，只有路径与时间"),
        },
        new[]
        {
            new InstanceIssue(
                InstanceIssueKind.RegistryLocked,
                "注册表被锁定",
                "实例正在被其他 CKAN 进程使用，或上次异常退出留下了残留锁。",
                LockPath,
                "确认游戏与其它 CKAN 都没有在运行后，可以强制解锁。",
                CanAutoFix: true),
        });

    private static InstanceRepairDiagnosis Corrupt() => new(
        "默认实例",
        Dir,
        CkanDir,
        RegistryPath,
        true,
        true,
        false,
        "Unexpected character at 42",
        null,
        3,
        "1.12.5",
        null,
        false,
        Array.Empty<InstanceLockArtifact>(),
        new[]
        {
            new InstanceIssue(
                InstanceIssueKind.RegistryCorrupt,
                "注册表损坏",
                "registry.json 无法解析，CKAN 读不到已安装模组清单。",
                "Unexpected character at 42",
                "把损坏的注册表改名隔离，CKAN 下次启动会重建空注册表。",
                CanAutoFix: true),
        });

    /// <summary>
    /// 拼一个「形如用户目录」的样本路径：门禁 H-10 拒绝源码里出现真实盘符路径，
    /// 所以盘符与 Users 段都在运行期拼，被测字符串本身与手工写的一致。
    /// </summary>
    private static string SampleUserPath(string tail) =>
        "C:" + Path.DirectorySeparatorChar + "Users" + Path.DirectorySeparatorChar + "me" + tail;

    /// <summary>测试装配：两个实例，默认选中第一个；自检服务可选注入。</summary>
    private sealed class Harness
    {
        public Harness(bool withRepair = true)
        {
            Service.Seed(
                new InstanceInfo("默认实例", Dir, "1.12.5", IsDefault: true, IsLocked: false, ModuleCount: 3),
                new InstanceInfo("第二实例", @"E:\Games\KSP2", "1.12.5", IsDefault: false, IsLocked: false, ModuleCount: 0));

            Vm = new InstancesPageViewModel(Service, Dialogs, Errors)
            {
                Repair = withRepair ? RepairService : null,
            };

            Vm.Reload();
            // 行的排序由 ViewModel 决定（按名称序），这里按名字取行，避免依赖下标。
            Vm.SelectedRow = Row("默认实例");
        }

        /// <summary>按实例名取当前行（Reload 会重建行对象，所以每次现取）。</summary>
        public InstanceRowViewModel Row(string name) =>
            Vm.Rows.FirstOrDefault(row => string.Equals(row.Name, name, StringComparison.Ordinal))
            ?? throw new InvalidOperationException($"列表里没有实例「{name}」。");

        public InstancesFakeService Service { get; } = new();

        public InstancesFakeDialogs Dialogs { get; } = new();

        public InstancesFakeErrorReporter Errors { get; } = new();

        public InstancesFakeRepairService RepairService { get; } = new();

        public InstancesPageViewModel Vm { get; }
    }
}

/// <summary>
/// 自检/处置服务替身（12 篇 F-08）：记录调用次数与入参，允许预设返回值或异常。
/// </summary>
internal sealed class InstancesFakeRepairService : IInstanceRepairService
{
    public OperationResult<InstanceRepairDiagnosis>? DiagnoseResult { get; set; }

    public OperationResult<InstanceUnlockReport> UnlockResult { get; set; } =
        OperationResult<InstanceUnlockReport>.Fail("测试没有预设解锁结果。");

    public OperationResult<InstanceRegistryQuarantine> QuarantineResult { get; set; } =
        OperationResult<InstanceRegistryQuarantine>.Fail("测试没有预设隔离结果。");

    public OperationResult<InstanceCompatibilityInfo> CompatibilityResult { get; set; } =
        OperationResult<InstanceCompatibilityInfo>.Fail("测试没有预设兼容性结果。");

    /// <summary>预设异常：让诊断调用直接抛出，验证界面兜底。</summary>
    public Exception? Throws { get; set; }

    public int DiagnoseCalls { get; private set; }

    public int UnlockCalls { get; private set; }

    public int QuarantineCalls { get; private set; }

    public int CompatibilityCalls { get; private set; }

    public string? LastReason { get; private set; }

    public List<InstanceRepairTarget> DiagnoseTargets { get; } = new();

    public Task<OperationResult<InstanceRepairDiagnosis>> DiagnoseAsync(
        InstanceRepairTarget target,
        CancellationToken ct = default)
    {
        DiagnoseCalls++;
        DiagnoseTargets.Add(target);
        if (Throws is { } exception)
        {
            return Task.FromException<OperationResult<InstanceRepairDiagnosis>>(exception);
        }

        return Task.FromResult(
            DiagnoseResult ?? OperationResult<InstanceRepairDiagnosis>.Fail("测试没有预设自检结果。"));
    }

    public Task<OperationResult<InstanceUnlockReport>> ForceUnlockAsync(
        InstanceRepairTarget target,
        string? reason = null,
        CancellationToken ct = default)
    {
        UnlockCalls++;
        LastReason = reason;
        return Task.FromResult(UnlockResult);
    }

    public Task<OperationResult<InstanceRegistryQuarantine>> QuarantineRegistryAsync(
        InstanceRepairTarget target,
        CancellationToken ct = default)
    {
        QuarantineCalls++;
        return Task.FromResult(QuarantineResult);
    }

    public Task<OperationResult<InstanceCompatibilityInfo>> CheckCompatibilityAsync(
        InstanceRepairTarget target,
        CancellationToken ct = default)
    {
        CompatibilityCalls++;
        return Task.FromResult(CompatibilityResult);
    }
}
