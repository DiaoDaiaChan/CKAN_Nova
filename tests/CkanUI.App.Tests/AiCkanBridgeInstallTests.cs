using CkanUI.App.Ai;
using CkanUI.Services.Abstractions;
using CkanUI.Services.Abstractions.Ai;
using CkanUI.Services.Abstractions.Dtos;
using NUnit.Framework;

namespace CkanUI.App.Tests;

/// <summary>
/// 宿主桥的安装链路：计划投影、冲突/空计划拦截、确认对话框、用户拒绝、经后台队列执行与安装器异常。
/// 仍然全部用手写替身——桥不碰 UI，测试也不该为它拉起窗口或碰真实磁盘。
/// </summary>
[TestFixture]
public class AiCkanBridgeInstallTests
{
    private static readonly string[] NoStrings = [];

    // ---- 计划 ---------------------------------------------------------------

    [Test]
    public async Task PlanInstall_WithoutChangeSetService_FailsWithConnectionMessage()
    {
        var bridge = new AiCkanBridge();

        var result = await bridge.PlanInstallAsync("B9PartSwitch");

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Message, Does.Contain("安装能力未连接"));
    }

    [Test]
    public async Task PlanInstall_BlankIdentifier_Fails()
    {
        var changeSet = new FakeChangeSet(OperationResult<ChangeSetPreview>.Ok(Preview(Item("A", "Alpha"))));
        var bridge = new AiCkanBridge(changeSets: changeSet);

        var blank = await bridge.PlanInstallAsync("   ");
        var missing = await bridge.PlanInstallAsync(null!);

        Assert.That(blank.IsSuccess, Is.False);
        Assert.That(missing.IsSuccess, Is.False);
        Assert.That(blank.Message, Does.Contain("缺少模组标识符"));
        // 坏输入不该打到变更集服务。
        Assert.That(changeSet.Requests, Is.Empty);
    }

    [Test]
    public async Task PlanInstall_MapsStepsInOrderAndCarriesDiagnosticsVerbatim()
    {
        var changeSet = new FakeChangeSet(OperationResult<ChangeSetPreview>.Ok(new ChangeSetPreview(
            [
                Item("ModuleManager", "Module Manager", to: "5.0.0"),
                Item("B9PartSwitch", "B9 Part Switch", ChangeAction.Upgrade, from: "1.0.0", to: "2.20.0"),
            ],
            ["与 OldB9 冲突"],
            5_242_880,
            ["会顺带装上 ModuleManager"])));
        var bridge = new AiCkanBridge(changeSets: changeSet);

        var result = await bridge.PlanInstallAsync("  B9PartSwitch  ");

        Assert.That(result.IsSuccess, Is.True);
        var plan = result.Value!;
        Assert.That(plan.Identifier, Is.EqualTo("B9PartSwitch"));
        Assert.That(plan.Steps.Select(step => step.Identifier), Is.EqualTo(new[] { "ModuleManager", "B9PartSwitch" }));
        Assert.That(plan.Steps[0].Action, Is.EqualTo("Install"));
        Assert.That(plan.Steps[1].Action, Is.EqualTo("Upgrade"));
        Assert.That(plan.Steps[1].FromVersion, Is.EqualTo("1.0.0"));
        Assert.That(plan.Steps[1].ToVersion, Is.EqualTo("2.20.0"));
        Assert.That(plan.Conflicts, Is.EqualTo(new[] { "与 OldB9 冲突" }));
        Assert.That(plan.Warnings, Is.EqualTo(new[] { "会顺带装上 ModuleManager" }));
        Assert.That(plan.EstimatedDownloadBytes, Is.EqualTo(5_242_880));
        Assert.That(plan.IsEmpty, Is.False);

        // 请求体必须是一次 Install，标识符已经去掉首尾空白。
        var request = changeSet.Requests.Single().Single();
        Assert.That(request.Identifier, Is.EqualTo("B9PartSwitch"));
        Assert.That(request.Action, Is.EqualTo(ChangeAction.Install));
    }

    [Test]
    public async Task PlanInstall_WhenChangeSetThrows_ReturnsFailureInsteadOfThrowing()
    {
        var bridge = new AiCkanBridge(changeSets: new FakeChangeSet { Failure = new InvalidOperationException("解析依赖时炸了") });

        var result = await bridge.PlanInstallAsync("B9PartSwitch");

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Detail, Does.Contain("解析依赖时炸了"));
    }

    // ---- 安装前拦截 ---------------------------------------------------------

    [Test]
    public async Task Install_WithConflicts_FailsAndNeverTouchesInstaller()
    {
        var changeSet = new FakeChangeSet(OperationResult<ChangeSetPreview>.Ok(new ChangeSetPreview(
            [Item("B9PartSwitch", "B9 Part Switch")],
            ["目标版本需要 KSP 1.13"],
            0,
            NoStrings)));
        var installer = new FakeInstaller();
        var dialogs = new FakeDialogs();
        var bridge = new AiCkanBridge(changeSets: changeSet, installer: installer, dialogs: dialogs);

        var result = await bridge.InstallAsync("B9PartSwitch");

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Message, Does.Contain("安装被冲突挡住"));
        Assert.That(result.Message, Does.Contain("目标版本需要 KSP 1.13"));
        Assert.That(installer.Applied, Is.Empty);
        Assert.That(dialogs.Confirmations, Is.Empty);
    }

    [Test]
    public async Task Install_WhenPlanIsEmpty_FailsAndNeverTouchesInstaller()
    {
        var changeSet = new FakeChangeSet(OperationResult<ChangeSetPreview>.Ok(Preview()));
        var installer = new FakeInstaller();
        var bridge = new AiCkanBridge(changeSets: changeSet, installer: installer, dialogs: new FakeDialogs());

        var result = await bridge.InstallAsync("B9PartSwitch");

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Message, Does.Contain("没有可执行的安装步骤"));
        Assert.That(installer.Applied, Is.Empty);
    }

    [Test]
    public async Task Install_WithoutDialogs_FailsAndNeverTouchesInstaller()
    {
        var changeSet = new FakeChangeSet(OperationResult<ChangeSetPreview>.Ok(Preview(Item("B9PartSwitch", "B9 Part Switch"))));
        var installer = new FakeInstaller();
        var bridge = new AiCkanBridge(changeSets: changeSet, installer: installer);

        var result = await bridge.InstallAsync("B9PartSwitch");

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Message, Does.Contain("没有可用的确认对话框"));
        Assert.That(installer.Applied, Is.Empty);
    }

    // ---- 确认 ---------------------------------------------------------------

    [Test]
    public async Task Install_WhenUserDeclines_ReturnsSuccessWithoutApplying()
    {
        var changeSet = new FakeChangeSet(OperationResult<ChangeSetPreview>.Ok(new ChangeSetPreview(
            [
                Item("ModuleManager", "Module Manager", to: "5.0.0"),
                Item("B9PartSwitch", "B9 Part Switch", ChangeAction.Upgrade, from: "1.0.0", to: "2.20.0"),
            ],
            NoStrings,
            5_242_880,
            ["会顺带装上 ModuleManager"])));
        var installer = new FakeInstaller();
        var dialogs = new FakeDialogs { Answer = false };
        var bridge = new AiCkanBridge(changeSets: changeSet, installer: installer, dialogs: dialogs);

        var result = await bridge.InstallAsync("B9PartSwitch");

        Assert.That(result.IsSuccess, Is.True);
        Assert.That(result.Value!.Applied, Is.False);
        Assert.That(result.Value.Summary, Does.Contain("拒绝"));
        Assert.That(result.Value.Installed, Is.Empty);
        Assert.That(installer.Applied, Is.Empty);

        var confirmation = dialogs.Confirmations.Single();
        Assert.That(confirmation.Title, Is.EqualTo("AI 想安装模组"));
        Assert.That(confirmation.Message, Does.Contain("「B9PartSwitch」"));
        Assert.That(confirmation.Message, Does.Contain("共 2 步"));
        Assert.That(confirmation.Message, Does.Contain("5.0 MB"));
        Assert.That(confirmation.Detail, Does.Contain("Install Module Manager (ModuleManager)"));
        Assert.That(confirmation.Detail, Does.Contain("Upgrade B9 Part Switch (B9PartSwitch) 1.0.0 -> 2.20.0"));
        Assert.That(confirmation.Detail, Does.Contain("会顺带装上 ModuleManager"));
        // 拒绝时不该再算第二遍计划（没有第二份要交给安装器）。
        Assert.That(changeSet.Requests, Has.Count.EqualTo(1));
    }

    [Test]
    public async Task Install_WhenUserConfirms_AppliesRefreshedPreviewThroughQueue()
    {
        var changeSet = new FakeChangeSet(
            OperationResult<ChangeSetPreview>.Ok(Preview(Item("B9PartSwitch", "B9 Part Switch"))),
            OperationResult<ChangeSetPreview>.Ok(Preview(
                Item("B9PartSwitch", "B9 Part Switch"),
                Item("ModuleManager", "Module Manager"))));
        var installer = new FakeInstaller
        {
            Result = OperationResult<InstallReport>.Ok(new InstallReport(
                ["B9PartSwitch", "ModuleManager"],
                ["OldB9"],
                [],
                ["顺手升级了一个依赖"])),
        };
        var queue = new FakeQueue();
        var dialogs = new FakeDialogs { Answer = true };
        var bridge = new AiCkanBridge(changeSets: changeSet, installer: installer, queue: queue, dialogs: dialogs);

        var result = await bridge.InstallAsync("B9PartSwitch");

        Assert.That(result.IsSuccess, Is.True);
        var outcome = result.Value!;
        Assert.That(outcome.Applied, Is.True);
        Assert.That(outcome.Identifier, Is.EqualTo("B9PartSwitch"));
        Assert.That(outcome.Installed, Is.EqualTo(new[] { "B9PartSwitch", "ModuleManager" }));
        Assert.That(outcome.Removed, Is.EqualTo(new[] { "OldB9" }));
        Assert.That(outcome.Failed, Is.Empty);
        Assert.That(outcome.Warnings, Is.EqualTo(new[] { "顺手升级了一个依赖" }));
        Assert.That(outcome.Summary, Does.Contain("已安装 2 个"));
        Assert.That(outcome.Summary, Does.Contain("移除 1 个"));

        // 关键：交给安装器的是确认之后重算的那一份，不是弹窗前那份。
        Assert.That(changeSet.Requests, Has.Count.EqualTo(2));
        Assert.That(installer.Applied, Has.Count.EqualTo(1));
        Assert.That(installer.Applied[0].Items.Select(item => item.Identifier), Is.EqualTo(new[] { "B9PartSwitch", "ModuleManager" }));

        var queued = queue.Enqueued.Single();
        Assert.That(queued.Kind, Is.EqualTo("install"));
        Assert.That(queued.Title, Does.Contain("B9PartSwitch"));
    }

    [Test]
    public async Task Install_WithoutQueue_AppliesDirectly()
    {
        var changeSet = new FakeChangeSet(OperationResult<ChangeSetPreview>.Ok(Preview(Item("B9PartSwitch", "B9 Part Switch"))));
        var installer = new FakeInstaller();
        var bridge = new AiCkanBridge(changeSets: changeSet, installer: installer, dialogs: new FakeDialogs());

        var result = await bridge.InstallAsync("B9PartSwitch");

        Assert.That(result.IsSuccess, Is.True);
        Assert.That(result.Value!.Applied, Is.True);
        Assert.That(installer.Applied, Has.Count.EqualTo(1));
    }

    // ---- 安装器返回 ---------------------------------------------------------

    [Test]
    public async Task Install_WhenInstallerThrows_ReturnsFailureInsteadOfThrowing()
    {
        var changeSet = new FakeChangeSet(OperationResult<ChangeSetPreview>.Ok(Preview(Item("B9PartSwitch", "B9 Part Switch"))));
        var installer = new FakeInstaller { Failure = new IOException("磁盘满了") };
        var queue = new FakeQueue();
        var bridge = new AiCkanBridge(changeSets: changeSet, installer: installer, queue: queue, dialogs: new FakeDialogs());

        var result = await bridge.InstallAsync("B9PartSwitch");

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Detail, Does.Contain("磁盘满了"));
    }

    [Test]
    public async Task Install_WhenInstallerReportsFailure_PassesMessageThrough()
    {
        var changeSet = new FakeChangeSet(
            OperationResult<ChangeSetPreview>.Ok(Preview(Item("B9PartSwitch", "B9 Part Switch"))));
        var installer = new FakeInstaller
        {
            Result = OperationResult<InstallReport>.Fail("下载校验失败。", "sha256 不一致"),
        };
        var bridge = new AiCkanBridge(changeSets: changeSet, installer: installer, dialogs: new FakeDialogs());

        var result = await bridge.InstallAsync("B9PartSwitch");

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Message, Is.EqualTo("下载校验失败。"));
        Assert.That(result.Detail, Is.EqualTo("sha256 不一致"));
    }

    // ---- 组装 ---------------------------------------------------------------

    private static ChangeSetPreview Preview(params ChangeItemDto[] items) =>
        new(items, NoStrings, 0, NoStrings);

    private static ChangeItemDto Item(
        string identifier,
        string name,
        ChangeAction action = ChangeAction.Install,
        string? from = null,
        string? to = "1.0.0",
        ChangeOrigin origin = ChangeOrigin.Dependency) =>
        new(identifier, name, action, from, to, origin, DownloadBytes: 1024);

    // ---- 替身 ---------------------------------------------------------------

    private sealed class FakeChangeSet : IChangeSetService
    {
        private readonly Queue<OperationResult<ChangeSetPreview>> _results;
        private OperationResult<ChangeSetPreview>? _last;

        public FakeChangeSet(params OperationResult<ChangeSetPreview>[] results) => _results = new Queue<OperationResult<ChangeSetPreview>>(results);

        /// <summary>
        /// 配置几份就依次给几份；用完之后重复最后一份。
        /// 安装会算两次计划（确认前 + 确认后），只配一份的用例不该因此炸掉。
        /// </summary>
        private OperationResult<ChangeSetPreview> Next() =>
            _results.Count > 0 ? _last = _results.Dequeue() : _last ?? throw new InvalidOperationException("FakeChangeSet 没有配置返回值。");

        public List<IReadOnlyCollection<ChangeRequest>> Requests { get; } = [];

        public Exception? Failure { get; init; }

        public Task<OperationResult<ChangeSetPreview>> BuildAsync(
            IReadOnlyCollection<ChangeRequest> requests,
            CancellationToken ct = default)
        {
            Requests.Add(requests);
            if (Failure is not null)
            {
                throw Failure;
            }

            return Task.FromResult(Next());
        }

        public OperationResult<ChangeSetPreview> BuildLocal(IReadOnlyCollection<ChangeRequest> requests) =>
            OperationResult<ChangeSetPreview>.Fail("测试替身不支持本地预览。");
    }

    private sealed class FakeInstaller : IInstallerService
    {
        public List<ChangeSetPreview> Applied { get; } = [];

        public OperationResult<InstallReport> Result { get; init; } =
            OperationResult<InstallReport>.Ok(new InstallReport(["B9PartSwitch"], NoStrings, NoStrings, NoStrings));

        public Exception? Failure { get; init; }

        public Task<OperationResult<InstallReport>> ApplyAsync(
            ChangeSetPreview changeSet,
            IProgress<OperationProgress>? progress = null,
            CancellationToken ct = default)
        {
            Applied.Add(changeSet);
            return Failure is null ? Task.FromResult(Result) : Task.FromException<OperationResult<InstallReport>>(Failure);
        }
    }

    /// <summary>入队即执行：把 fire-and-forget 的队列语义压成同步，好让断言看得见安装器调用。</summary>
    private sealed class FakeQueue : IOperationQueue
    {
        public List<(string Kind, string Title)> Enqueued { get; } = [];

        public IReadOnlyList<OperationProgress> Operations => Array.Empty<OperationProgress>();

        public bool IsBusy => false;

        public event Action<IReadOnlyList<OperationProgress>>? Updated
        {
            add { }
            remove { }
        }

        public string Enqueue(string kind, string title, Func<CancellationToken, Task> work)
        {
            Enqueued.Add((kind, title));
            work(CancellationToken.None).GetAwaiter().GetResult();
            return "task-" + Enqueued.Count;
        }

        /// <inheritdoc />
        public string Enqueue(string kind, string title, Func<CancellationToken, Task<OperationOutcome>> work)
        {
            Enqueued.Add((kind, title));
            work(CancellationToken.None).GetAwaiter().GetResult();
            return "task-" + Enqueued.Count;
        }

        public void Cancel(string taskId)
        {
        }
    }

    private sealed class FakeDialogs : IDialogService
    {
        public List<(string Title, string Message, string? Detail)> Confirmations { get; } = [];

        public bool Answer { get; init; } = true;

        public Task<bool> ConfirmAsync(string title, string message, string? detail = null)
        {
            Confirmations.Add((title, message, detail));
            return Task.FromResult(Answer);
        }

        public Task<int> SelectAsync(string title, string message, IReadOnlyList<string> options, int defaultIndex = 0) =>
            Task.FromResult(-1);

        public Task ShowMessageAsync(string title, string message) => Task.CompletedTask;

        public Task ShowErrorAsync(string title, string message, string? detail = null) => Task.CompletedTask;

        public Task<OperationResult<bool>> RunWithProgressAsync(
            string title,
            Func<IProgress<double>, CancellationToken, Task> work) =>
            Task.FromResult(OperationResult<bool>.Fail("测试替身不支持进度对话框。"));
    }
}
