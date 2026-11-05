using CkanUI.Services.Abstractions;
using CkanUI.Services.Abstractions.Dtos;
using CkanUI.ViewModels.Pages;
using CkanUI.ViewModels.Pages.Changes;
using NUnit.Framework;

namespace CkanUI.ViewModels.Tests;

/// <summary>
/// 变更集页的"结果语义 / 冲突结构化 / 进度留痕"三条收口（B0·B1·B2·E3）。
/// 这些用例刻意放在独立文件里：它们断言的是界面契约，不是某个实现细节。
/// </summary>
[TestFixture]
public sealed class ChangesWarningsAndProgressTests
{
    private ChangesFakeChangeSetService _changeSets = null!;
    private ChangesFakeInstallerService _installer = null!;
    private ChangesCountingErrorReporter _errors = null!;

    [SetUp]
    public void SetUp()
    {
        _changeSets = new ChangesFakeChangeSetService();
        _installer = new ChangesFakeInstallerService();
        _errors = new ChangesCountingErrorReporter();
    }

    private ChangesPageViewModel Create()
    {
        var vm = new ChangesPageViewModel(
            _changeSets,
            _installer,
            _errors,
            null,
            new ChangesImmediateDispatcher());
        vm.Activate();
        return vm;
    }

    [Test]
    public async Task ApplyWarningsOnly_EndsInDoneWithoutRetry()
    {
        var preview = ChangesFakes.Preview(new[] { ChangesFakes.Item("a", "Alpha") }, 1_000);
        _changeSets.NextResult = OperationResult<ChangeSetPreview>.Ok(preview);
        // 只有提示、零失败：这是一次成功，不能落失败态，也不能给「重试」。
        _installer.Result = OperationResult<InstallReport>.Ok(new InstallReport(
            new[] { "a" },
            Array.Empty<string>(),
            Array.Empty<string>(),
            new[] { "未能读取下载缓存目录，预计下载体积可能重复计算已缓存的文件。" }));

        var vm = Create();
        vm.SetRequests(ChangesFakes.Requests("a"));
        await vm.PendingWork;
        await vm.ApplyCommand.ExecuteAsync(null);

        Assert.That(vm.State, Is.EqualTo(ChangeExecutionState.Done));
        Assert.That(vm.CanRetry, Is.False, "有提示无失败不该出现重试入口");
        Assert.That(vm.ResultText, Does.Contain("提示："), "提示仍要原样展示，只是不阻断结果");
        Assert.That(vm.ResultText, Does.Contain("未能读取下载缓存目录"));
        Assert.That(vm.StatusText, Does.Contain("全部成功（含 1 条提示）"));
        Assert.That(_errors.MessageReports, Is.Zero);
    }

    [Test]
    public async Task ApplyDone_KeepsProgressBarVisibleWithFinalPercent()
    {
        var preview = ChangesFakes.Preview(new[] { ChangesFakes.Item("a", "Alpha") }, 1_000);
        _changeSets.NextResult = OperationResult<ChangeSetPreview>.Ok(preview);
        _installer.Result = OperationResult<InstallReport>.Ok(ChangesFakes.SuccessReport());

        var vm = Create();
        vm.SetRequests(ChangesFakes.Requests("a"));
        await vm.PendingWork;
        await vm.ApplyCommand.ExecuteAsync(null);

        Assert.That(vm.State, Is.EqualTo(ChangeExecutionState.Done));
        Assert.That(vm.IsProgressVisible, Is.True, "执行结束后进度条要保留最终进度，不能抽走");
        Assert.That(vm.ProgressText, Is.EqualTo("100%"));
        Assert.That(vm.IsApplying, Is.False);
    }

    [Test]
    public void ConflictedPreview_UsesStructuredEntriesForCountAndRows()
    {
        var preview = ChangesFakes.Preview(
            new[] { ChangesFakes.Item("a", "Alpha") },
            conflicts: new[] { "RP-1 4.7.0.0 与 CommunityTechTree 3.4.5 冲突" }) with
        {
            ConflictEntries = new[]
            {
                new ConflictEntry(
                    "RP-1 4.7.0.0 与 CommunityTechTree 3.4.5 冲突",
                    ModuleIdentifier: "RP-1",
                    ModuleName: "Realistic Progression One"),
            },
        };

        var vm = Create();
        vm.LoadPreview(preview);

        Assert.That(vm.HasConflicts, Is.True);
        Assert.That(vm.ConflictCount, Is.EqualTo(1), "冲突条数以结构化条目为准，不能和字符串列表重复计");
        Assert.That(vm.ConflictTitle, Does.Contain("1"));
        var row = vm.ConflictRows.Single();
        Assert.That(row.HasModule, Is.True, "冲突行必须能落到具体模组");
        Assert.That(row.ModuleText, Does.Contain("RP-1"));
        Assert.That(row.Description, Does.Contain("冲突"));
        Assert.That(vm.CanApply, Is.False, "有冲突时不允许开始");
    }

    [Test]
    public void PlainStringConflicts_StillRenderAsRowsWithoutModule()
    {
        var preview = ChangesFakes.Preview(
            new[] { ChangesFakes.Item("a", "Alpha") },
            conflicts: new[] { "某条没有结构的冲突说明" });

        var vm = Create();
        vm.LoadPreview(preview);

        Assert.That(vm.ConflictRows, Has.Count.EqualTo(1));
        Assert.That(vm.ConflictRows[0].HasModule, Is.False);
        Assert.That(vm.ConflictRows[0].Description, Is.EqualTo("某条没有结构的冲突说明"));
    }

    [Test]
    public async Task Applying_ProgressStageLabelBecomesItsOwnBadge()
    {
        var preview = ChangesFakes.Preview(
            new[]
            {
                ChangesFakes.Item("a", "Alpha"),
                ChangesFakes.Item("b", "Beta"),
            }, 1_000);
        _changeSets.NextResult = OperationResult<ChangeSetPreview>.Ok(preview);
        _installer.Result = OperationResult<InstallReport>.Ok(ChangesFakes.SuccessReport());
        _installer.ProgressToReport = new[]
        {
            new OperationProgress("t", "install", "正在下载 1/2：Alpha", "正在下载 1/2：Alpha", 20, true,
                OperationState.Running) { StageLabel = "下载 1/2" },
            new OperationProgress("t", "install", "正在安装 2/2：Beta", "正在安装 2/2：Beta", 70, true,
                OperationState.Running) { StageLabel = "安装 2/2" },
        };

        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _installer.HangAfterProgress = gate;

        var vm = Create();
        vm.SetRequests(ChangesFakes.Requests("a", "b"));
        await vm.PendingWork;

        var apply = vm.ApplyCommand.ExecuteAsync(null);

        // Progress<T> 没有同步上下文时会在线程池上回调，这里等它把中间态刷进来。
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (vm.ProgressStageText.Length == 0 && DateTime.UtcNow < deadline)
        {
            await Task.Delay(10);
        }

        Assert.That(vm.IsApplying, Is.True, "断言发生在执行中，而不是执行后");
        Assert.That(vm.HasProgressStage, Is.True, "阶段徽标要在执行期出现");
        Assert.That(vm.ProgressStageText, Is.EqualTo("安装 2/2"));
        Assert.That(vm.IsProgressVisible, Is.True);
        Assert.That(vm.ExecutionPercent, Is.EqualTo(70), "进度条跟着阶段百分比走");
        // 阶段徽标只出现在进度行，状态文字里不重复（同一句话里说两遍等于没说）
        Assert.That(vm.StateText, Does.Not.Contain("安装 2/2"));
        Assert.That(vm.StatusText, Does.Contain("正在安装 2/2：Beta"));

        gate.SetResult();
        await apply;

        Assert.That(vm.State, Is.EqualTo(ChangeExecutionState.Done));
        Assert.That(vm.ExecutionPercent, Is.EqualTo(100), "终态由结果收口到 100%");
        Assert.That(vm.IsProgressVisible, Is.True, "执行结束后保留最终进度条");
    }

    [Test]
    public async Task LateOlderProgress_DoesNotRewindPercentOrStage()
    {
        var preview = ChangesFakes.Preview(
            new[]
            {
                ChangesFakes.Item("a", "Alpha"),
                ChangesFakes.Item("b", "Beta"),
            }, 1_000);
        _changeSets.NextResult = OperationResult<ChangeSetPreview>.Ok(preview);
        _installer.Result = OperationResult<InstallReport>.Ok(ChangesFakes.SuccessReport());

        // 线程池回调可能乱序：一条迟到的旧进度（20% / 下载 1/2）不能把已经走到
        // 70% / 安装 2/2 的界面拽回去——进度回滚比不刷新更让人怀疑程序坏了。
        _installer.ProgressToReport = new[]
        {
            new OperationProgress("t", "install", "正在安装 2/2：Beta", "正在安装 2/2：Beta", 70, true,
                OperationState.Running) { StageLabel = "安装 2/2" },
            new OperationProgress("t", "install", "正在下载 1/2：Alpha", "正在下载 1/2：Alpha", 20, true,
                OperationState.Running) { StageLabel = "下载 1/2" },
        };

        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _installer.HangAfterProgress = gate;

        var vm = Create();
        vm.SetRequests(ChangesFakes.Requests("a", "b"));
        await vm.PendingWork;

        var apply = vm.ApplyCommand.ExecuteAsync(null);

        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (vm.ExecutionPercent < 70 && DateTime.UtcNow < deadline)
        {
            await Task.Delay(10);
        }

        Assert.That(vm.ExecutionPercent, Is.EqualTo(70), "迟到的旧进度不能让百分比倒退");
        Assert.That(vm.ProgressStageText, Is.EqualTo("安装 2/2"), "阶段徽标同样不能被旧进度覆盖");

        gate.SetResult();
        await apply;
    }

    [Test]
    public async Task PendingChoice_PromptsUserThenRetriesWithoutSecondClick()
    {
        var preview = ChangesFakes.Preview(new[] { ChangesFakes.Item("a", "Alpha") }, 1_000);
        _changeSets.NextResult = OperationResult<ChangeSetPreview>.Ok(preview);

        var dialogs = new FakeModDialogService
        {
            NextVirtualModuleChoice = new VirtualModuleChoice("a-provider", "Alpha Provider"),
        };

        // 第一轮：后端说「有多个提供者，你来选」；第二轮（选完重跑）成功。
        _installer.Results.Enqueue(OperationResult<InstallReport>.NeedsChoice("依赖有多个提供者。", SampleChoice()));
        _installer.Results.Enqueue(OperationResult<InstallReport>.Ok(ChangesFakes.SuccessReport()));

        var vm = CreateWithDialogs(dialogs);
        vm.SetRequests(ChangesFakes.Requests("a"));
        await vm.PendingWork;

        await vm.ApplyCommand.ExecuteAsync(null);

        Assert.That(dialogs.SelectVirtualModuleCallCount, Is.EqualTo(1), "提供者歧义必须弹选择框，不能直接判失败");
        Assert.That(_installer.ApplyCount, Is.EqualTo(2), "选完应当自动重跑，不该让用户再点一次开始");
        Assert.That(vm.State, Is.EqualTo(ChangeExecutionState.Done));
        Assert.That(_errors.MessageReports, Is.Zero, "这不是错误，不该往错误栏里塞一条");
        Assert.That(vm.HasErrorText, Is.False);
    }

    [Test]
    public async Task PendingChoice_UserCancels_ReturnsToReadyWithoutError()
    {
        var preview = ChangesFakes.Preview(new[] { ChangesFakes.Item("a", "Alpha") }, 1_000);
        _changeSets.NextResult = OperationResult<ChangeSetPreview>.Ok(preview);
        _installer.Results.Enqueue(OperationResult<InstallReport>.NeedsChoice("依赖有多个提供者。", SampleChoice()));

        var dialogs = new FakeModDialogService { NextVirtualModuleChoice = null };

        var vm = CreateWithDialogs(dialogs);
        vm.SetRequests(ChangesFakes.Requests("a"));
        await vm.PendingWork;

        await vm.ApplyCommand.ExecuteAsync(null);

        Assert.That(vm.State, Is.EqualTo(ChangeExecutionState.Ready), "用户主动放弃不是故障，不能落失败态");
        Assert.That(_errors.MessageReports, Is.Zero);
        Assert.That(_installer.ApplyCount, Is.EqualTo(1));
        Assert.That(vm.CanApply, Is.True, "放弃后必须还能再开始");
    }

    [Test]
    public async Task Failure_IsSaidOnceAndDetailDoesNotImpersonateResult()
    {
        var preview = ChangesFakes.Preview(new[] { ChangesFakes.Item("a", "Alpha") }, 1_000);
        _changeSets.NextResult = OperationResult<ChangeSetPreview>.Ok(preview);
        _installer.Result = OperationResult<InstallReport>.Fail(
            "依赖解析失败，未执行任何操作。", "AIES Patches 需要 AIES-Antenna-Patches，可以由多个模组提供。");

        var vm = Create();
        vm.SetRequests(ChangesFakes.Requests("a"));
        await vm.PendingWork;

        await vm.ApplyCommand.ExecuteAsync(null);

        Assert.That(vm.State, Is.EqualTo(ChangeExecutionState.Failed));
        Assert.That(vm.HasResult, Is.False, "上游的原始异常文本不能冒充「执行结果」");
        Assert.That(vm.ResultText, Is.Empty);
        Assert.That(_errors.MessageReports, Is.EqualTo(1));
        Assert.That(vm.LastErrorText, Is.EqualTo("依赖解析失败，未执行任何操作。"));
        Assert.That(vm.StatusText, Is.Empty, "同一句话不该在状态栏里再说一遍");
        Assert.That(vm.StateText, Is.EqualTo("应用变更失败，原因见错误栏。"), "状态栏第一行只说「失败了」，不说原因");
    }

    /// <summary>
    /// 「失败只在一处说」的不变量：原因只出现在错误栏，状态栏与结果区分工明确。
    /// 计算失败、执行失败（含部分失败）、执行异常三种收口都必须满足，否则又会回到「同一句话四遍」。
    /// </summary>
    [Test]
    public async Task EveryFailurePath_SaysTheReasonOnlyInTheErrorBar()
    {
        var preview = ChangesFakes.Preview(new[] { ChangesFakes.Item("a", "Alpha") }, 1_000);

        // ① 计算失败
        _changeSets.NextResult = OperationResult<ChangeSetPreview>.Fail("仓库索引已过期，请先刷新", "index.json > 7 天");
        var build = Create();
        build.SetRequests(ChangesFakes.Requests("a"));
        await build.PendingWork;
        AssertSaidOnce(build, "仓库索引已过期，请先刷新", "计算变更集失败，原因见错误栏。");

        // ② 执行失败
        _changeSets.NextResult = OperationResult<ChangeSetPreview>.Ok(preview);
        _installer.Result = OperationResult<InstallReport>.Fail("依赖解析失败，未执行任何操作。", "上游异常原文");
        var apply = Create();
        apply.SetRequests(ChangesFakes.Requests("a"));
        await apply.PendingWork;
        await apply.ApplyCommand.ExecuteAsync(null);
        AssertSaidOnce(apply, "依赖解析失败，未执行任何操作。", "应用变更失败，原因见错误栏。");

        // ③ 执行异常
        _installer.Result = OperationResult<InstallReport>.Ok(ChangesFakes.SuccessReport());
        _installer.ThrowOnApply = new IOException("磁盘写入失败");
        var boom = Create();
        boom.SetRequests(ChangesFakes.Requests("a"));
        await boom.PendingWork;
        await boom.ApplyCommand.ExecuteAsync(null);
        Assert.That(boom.ResultText, Is.Empty, "异常消息不能冒充执行结果");
        Assert.That(boom.LastErrorText, Does.Contain("磁盘写入失败"), "原因必须出现在错误栏");
        Assert.That(boom.StateText, Does.Not.Contain("磁盘写入失败"), "状态栏不重复原因");
        Assert.That(boom.StatusText, Is.Empty);
    }

    /// <summary>
    /// 失败态只留一个「重试」：错误栏里那个贴着原因，状态栏不能再摆一个同绑定的按钮，
    /// 也不能留一个灰掉的「开始」让人猜两者差在哪。
    /// </summary>
    [Test]
    public async Task FailedState_OffersExactlyOneRetryAndHidesStart()
    {
        _changeSets.NextResult = OperationResult<ChangeSetPreview>.Ok(
            ChangesFakes.Preview(new[] { ChangesFakes.Item("a", "Alpha") }, 1_000));
        _installer.Result = OperationResult<InstallReport>.Fail("依赖解析失败，未执行任何操作。", "上游异常原文");

        var vm = Create();
        vm.SetRequests(ChangesFakes.Requests("a"));
        await vm.PendingWork;
        await vm.ApplyCommand.ExecuteAsync(null);

        Assert.That(vm.State, Is.EqualTo(ChangeExecutionState.Failed));
        Assert.That(vm.CanRetry, Is.True, "错误栏的「重试」必须可用");
        Assert.That(vm.ShowApplyButton, Is.False, "失败态不能同时摆「开始」，那是同一个动作换个名字");
        Assert.That(vm.CanApply, Is.False, "CanApply 与按钮可见性必须同口径");
    }

    [Test]
    public async Task ReadyState_StillShowsStartAndNoRetry()
    {
        _changeSets.NextResult = OperationResult<ChangeSetPreview>.Ok(
            ChangesFakes.Preview(new[] { ChangesFakes.Item("a", "Alpha") }, 1_000));

        var vm = Create();
        vm.SetRequests(ChangesFakes.Requests("a"));
        await vm.PendingWork;

        Assert.That(vm.State, Is.EqualTo(ChangeExecutionState.Ready));
        Assert.That(vm.CanApply, Is.True);
        Assert.That(vm.ShowApplyButton, Is.True);
        Assert.That(vm.CanRetry, Is.False);
    }

    private static void AssertSaidOnce(ChangesPageViewModel vm, string reason, string headline)
    {
        Assert.That(vm.LastErrorText, Is.EqualTo(reason), "原因是错误栏的唯一职责");
        Assert.That(vm.StateText, Is.EqualTo(headline), "状态栏第一行只说「哪个阶段失败了」");
        Assert.That(vm.StatusText, Is.Empty, "状态栏第二行不能把同一句话再说一遍");
        Assert.That(vm.ResultText, Is.Empty, "失败的原因不属于「执行结果」");
        Assert.That(vm.StateText, Does.Not.Contain(reason), "状态栏第一行不能把原因抄进来");
    }

    private static VirtualModuleRequest SampleChoice()
        => new("「AIES-Antenna-Patches」有 2 个模组可以提供，请选择一个继续。",
               new[]
               {
                   new VirtualModuleCandidate("a-provider", "Alpha Provider", IsCached: true),
                   new VirtualModuleCandidate("b-provider", "Beta Provider"),
               },
               "AIES-Antenna-Patches");

    private ChangesPageViewModel CreateWithDialogs(IModDialogService dialogs)
    {
        var vm = new ChangesPageViewModel(
            _changeSets,
            _installer,
            _errors,
            null,
            new ChangesImmediateDispatcher(),
            modDialogs: dialogs);
        vm.Activate();
        return vm;
    }

    [Test]
    public void Formatter_StateText_HasNoStageSlotAnymore()
    {
        var applying = ChangesFormatter.StateText(ChangeExecutionState.Applying, 42);
        Assert.That(applying, Does.Contain("42%"));
        Assert.That(applying, Does.Not.Contain("（）"), "阶段徽标不走状态文字，避免出现空括号");
        Assert.That(ChangesFormatter.StateText(ChangeExecutionState.Done, 100), Is.EqualTo("变更已全部应用完成。"));
    }
}
