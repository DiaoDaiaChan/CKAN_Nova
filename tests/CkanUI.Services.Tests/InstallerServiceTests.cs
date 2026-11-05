using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using CkanUI.Services.Abstractions;
using CkanUI.Services.Abstractions.Dtos;
using CkanUI.Services.Installers;
using CkanUI.Services.Operations;
using CkanUI.TestSupport;
using NUnit.Framework;

namespace CkanUI.Services.Tests;

/// <summary>
/// <see cref="InstallerService"/> 的编排测试（INT-08 / 06 篇 TST-03）：
/// 覆盖成功安装、依赖缺失、取消、失败回滚、卸载、并发拒绝与进度回报。
/// 后端全部用 <see cref="InstallerFakeBackend"/>，不下载、不写磁盘。
/// </summary>
[TestFixture]
public sealed class InstallerServiceTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    [Test]
    public async Task ApplyAsync_EmptyChangeSet_SucceedsWithoutTouchingBackend()
    {
        using var harness = new Harness();

        var result = await harness.Service.ApplyAsync(EmptyChangeSet()).WaitAsync(Timeout);

        Assert.That(result.IsSuccess, Is.True);
        Assert.That(result.Value!.Installed, Is.Empty);
        Assert.That(harness.Backend.Calls, Is.Empty);
    }

    [Test]
    public async Task ApplyAsync_Conflicts_FailsWithoutTouchingBackend()
    {
        using var harness = new Harness();
        var changeSet = new ChangeSetPreview(new[] { Item("a", ChangeAction.Install) },
                                             new[] { "a 与 b 不兼容" }, 0, Array.Empty<string>());

        var result = await harness.Service.ApplyAsync(changeSet).WaitAsync(Timeout);

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Message, Does.Contain("冲突"));
        Assert.That(result.Detail, Does.Contain("a 与 b 不兼容"));
        Assert.That(harness.Backend.Calls, Is.Empty);
    }

    [Test]
    public async Task ApplyAsync_NoActiveInstance_Fails()
    {
        using var harness = new Harness(backendAvailable: false);

        var result = await harness.Service.ApplyAsync(
            new ChangeSetPreview(new[] { Item("a", ChangeAction.Install) }, Array.Empty<string>(), 0,
                                 Array.Empty<string>())).WaitAsync(Timeout);

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Message, Does.Contain("尚未选定游戏实例"));
        Assert.That(harness.Factory.CreateCount, Is.EqualTo(1));
    }

    [Test]
    public async Task ApplyAsync_InstallAndRemove_ReportsBothSides()
    {
        using var harness = new Harness();
        harness.Backend.Installed.Add("old");
        var changeSet = new ChangeSetPreview(
            new[] { Item("old", ChangeAction.Remove), Item("new", ChangeAction.Install) },
            Array.Empty<string>(), 0, Array.Empty<string>());

        var result = await harness.Service.ApplyAsync(changeSet).WaitAsync(Timeout);

        Assert.That(result.IsSuccess, Is.True);
        Assert.That(result.Value!.Installed, Is.EqualTo(new[] { "new" }));
        Assert.That(result.Value.Removed, Is.EqualTo(new[] { "old" }));
        Assert.That(result.Value.IsFullSuccess, Is.True);
        Assert.That(harness.Backend.Installed, Is.EqualTo(new[] { "new" }));
        // 先卸后装，顺序必须与上游 MainInstall 一致
        Assert.That(harness.Backend.Calls, Is.EqualTo(new[] { "snapshot", "resolve", "remove", "install" }));
        Assert.That(harness.Backend.RollbackCalls, Is.Zero);
    }

    [Test]
    public async Task ApplyAsync_DependencyMissing_FailsWithoutRollbackOrInstall()
    {
        using var harness = new Harness();
        harness.Backend.Installed.Add("old");
        harness.Backend.ResolveHandler = (_, _) =>
            (InstallerBackendResult.Fail("依赖不满足，未执行任何操作。", "缺少依赖 X", new[] { "new" }), null);

        var result = await harness.Service.ApplyAsync(
            new ChangeSetPreview(new[] { Item("new", ChangeAction.Install) }, Array.Empty<string>(), 0,
                                 Array.Empty<string>())).WaitAsync(Timeout);

        Assert.That(result.IsSuccess, Is.False);
        // 失败文案只保留后端那句具体原因：以前是「依赖解析失败… 依赖不满足…」两句同义拼接，
        // 页面里同一件事出现四次，读起来就是信息过载。
        Assert.That(result.Message, Does.Contain("依赖不满足"));
        Assert.That(result.Detail, Does.Contain("缺少依赖 X"));
        Assert.That(harness.Backend.Calls, Does.Not.Contain("install"));
        Assert.That(harness.Backend.Calls, Does.Not.Contain("rollback"));
        Assert.That(harness.Backend.Installed, Is.EqualTo(new[] { "old" }));
    }

    [Test]
    public async Task ApplyAsync_Failure_MarksQueueItemFailedInsteadOfCompleted()
    {
        // 以前队列在 work 正常返回后无条件写「已完成 100%」，而安装失败按契约就是正常返回——
        // 于是页面红着失败态、后台浮层写着「全部完成」，同一件事两副面孔。
        using var harness = new Harness();
        harness.Backend.ResolveHandler = (_, _) =>
            (InstallerBackendResult.Fail("依赖不满足，未执行任何操作。", "缺少依赖 X"), null);

        await harness.Service.ApplyAsync(
            new ChangeSetPreview(new[] { Item("new", ChangeAction.Install) }, Array.Empty<string>(), 0,
                                 Array.Empty<string>())).WaitAsync(Timeout);

        var queued = await WaitForFinishedQueueItemAsync(harness.Queue);
        Assert.That(queued.State, Is.EqualTo(OperationState.Failed));
        Assert.That(queued.Message, Does.Contain("依赖不满足"));
        Assert.That(queued.Percent, Is.Not.EqualTo(100), "失败不该被补成 100%，那会让人以为全装上了");
    }

    [Test]
    public async Task ApplyAsync_Success_MarksQueueItemSucceededAtFullProgress()
    {
        using var harness = new Harness();

        await harness.Service.ApplyAsync(
            new ChangeSetPreview(new[] { Item("new", ChangeAction.Install) }, Array.Empty<string>(), 0,
                                 Array.Empty<string>())).WaitAsync(Timeout);

        var queued = await WaitForFinishedQueueItemAsync(harness.Queue);
        Assert.That(queued.State, Is.EqualTo(OperationState.Succeeded));
        Assert.That(queued.Percent, Is.EqualTo(100));
    }

    [Test]
    public async Task ApplyAsync_ResolveNeedsChoice_IsNotFailureAndTouchesNothing()
    {
        using var harness = new Harness();
        harness.Backend.Installed.Add("old");
        harness.Backend.ResolveHandler = (_, _) =>
            (InstallerBackendResult.NeedsChoice(SampleChoice()), null);

        var result = await harness.Service.ApplyAsync(
            new ChangeSetPreview(new[] { Item("new", ChangeAction.Install) }, Array.Empty<string>(), 0,
                                 Array.Empty<string>())).WaitAsync(Timeout);

        // 「依赖有多个提供者」不是失败，是「还差一次用户裁决」：不落失败态、不碰磁盘。
        Assert.That(result.PendingChoice, Is.Not.Null);
        Assert.That(result.PendingChoice!.Providers, Has.Count.EqualTo(2));
        Assert.That(result.Message, Does.Contain("多个提供者"));
        Assert.That(harness.Backend.Calls, Does.Not.Contain("install"));
        Assert.That(harness.Backend.Calls, Does.Not.Contain("rollback"));
        Assert.That(harness.Backend.Installed, Is.EqualTo(new[] { "old" }));
    }

    [Test]
    public async Task ApplyAsync_InstallNeedsChoice_RollsBackBeforeHandingBack()
    {
        using var harness = new Harness();
        harness.Backend.Installed.Add("old");
        harness.Backend.InstallHandler = (_, _, _) => InstallerBackendResult.NeedsChoice(SampleChoice());

        var result = await harness.Service.ApplyAsync(
            new ChangeSetPreview(
                new[] { Item("old", ChangeAction.Remove), Item("new", ChangeAction.Install) },
                Array.Empty<string>(), 0, Array.Empty<string>())).WaitAsync(Timeout);

        Assert.That(result.PendingChoice, Is.Not.Null);
        // 卸载已经发生过，必须先还原到操作前再让用户选，否则用户会看到「少了东西但什么都没做」。
        Assert.That(harness.Backend.RollbackCalls, Is.EqualTo(1));
        Assert.That(harness.Backend.Installed, Is.EqualTo(new[] { "old" }));
    }

    /// <summary>等队列把工作项的终态写进去（工作项先回 TCS，队列状态随后落）。</summary>
    private static async Task<OperationProgress> WaitForFinishedQueueItemAsync(OperationQueue queue)
    {
        for (var attempt = 0; attempt < 200; attempt++)
        {
            var item = queue.Operations.FirstOrDefault();
            if (item is not null && item.State is OperationState.Succeeded
                                    or OperationState.Failed
                                    or OperationState.Cancelled)
            {
                return item;
            }

            await Task.Delay(10);
        }

        return queue.Operations.First();
    }

    private static VirtualModuleRequest SampleChoice()
        => new("「AIES-Antenna-Patches」有 2 个模组可以提供，请选择一个继续。",
               new[]
               {
                   new VirtualModuleCandidate("AIES-Antenna-Patches", "AIES Antenna Patches", IsCached: true),
                   new VirtualModuleCandidate("RP-1-Antenna-Patches", "RP-1 Antenna Patches"),
               },
               "AIES-Antenna-Patches");

    [Test]
    public async Task ApplyAsync_InstallFailure_CleansUpHalfInstalled()
    {
        using var harness = new Harness();
        harness.Backend.Installed.Add("old");
        harness.Backend.InstallHandler = (plan, _, _) =>
        {
            // 半成品：文件已经落了一半才失败
            harness.Backend.Installed.Add("half");
            return InstallerBackendResult.Fail("安装失败。", "磁盘已满", plan.ToInstall);
        };

        var result = await harness.Service.ApplyAsync(
            new ChangeSetPreview(new[] { Item("half", ChangeAction.Install) }, Array.Empty<string>(), 0,
                                 Array.Empty<string>())).WaitAsync(Timeout);

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Message, Does.Contain("安装失败，已尝试还原"));
        Assert.That(result.Message, Does.Contain("half"));
        Assert.That(harness.Backend.RollbackCalls, Is.EqualTo(1));
        Assert.That(harness.Backend.LastRollbackCleanup, Is.EqualTo(new[] { "half" }));
        Assert.That(harness.Backend.LastRollbackRestore, Is.Empty);
        Assert.That(harness.Backend.Installed, Is.EqualTo(new[] { "old" }));
    }

    [Test]
    public async Task ApplyAsync_RemoveFailure_RestoresUninstalledModules()
    {
        using var harness = new Harness();
        harness.Backend.Installed.Add("old");
        harness.Backend.RemoveHandler = (plan, _, _) =>
        {
            // 已经删掉一个才失败：回滚必须把它装回去
            harness.Backend.Installed.Remove("old");
            return InstallerBackendResult.Fail("卸载失败。", "文件被占用", plan.ToRemove);
        };

        var result = await harness.Service.ApplyAsync(
            new ChangeSetPreview(new[] { Item("old", ChangeAction.Remove) }, Array.Empty<string>(), 0,
                                 Array.Empty<string>())).WaitAsync(Timeout);

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Message, Does.Contain("卸载失败，已尝试还原"));
        Assert.That(harness.Backend.RollbackCalls, Is.EqualTo(1));
        Assert.That(harness.Backend.LastRollbackRestore, Is.EqualTo(new[] { "old" }));
        Assert.That(harness.Backend.Installed, Is.EqualTo(new[] { "old" }));
    }

    [Test]
    public async Task ApplyAsync_AlreadyCancelledToken_FailsWithCancelledMessage()
    {
        using var harness = new Harness();
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        var result = await harness.Service.ApplyAsync(
            new ChangeSetPreview(new[] { Item("a", ChangeAction.Install) }, Array.Empty<string>(), 0,
                                 Array.Empty<string>()), null, cts.Token).WaitAsync(Timeout);

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Message, Is.EqualTo("已取消，未做改动。"));
        Assert.That(harness.Backend.Calls, Is.Empty);
    }

    [Test]
    public async Task ApplyAsync_CancelDuringInstall_RollsBackAndReportsCancelled()
    {
        using var harness = new Harness();
        harness.Backend.Installed.Add("old");
        harness.Backend.InstallGate = new ManualResetEventSlim(false);
        var changeSet = new ChangeSetPreview(
            new[] { Item("old", ChangeAction.Remove), Item("new", ChangeAction.Install) },
            Array.Empty<string>(), 0, Array.Empty<string>());

        var running = harness.Service.ApplyAsync(changeSet);
        Assert.That(harness.Backend.InstallStarted.Wait(Timeout), Is.True, "后台未走到安装阶段");

        // 走队列取消：这正是 UI 的取消按钮路径（05 篇 §4）
        var taskId = harness.Queue.Operations.Single().TaskId;
        harness.Queue.Cancel(taskId);
        harness.Backend.InstallGate.Set();

        var result = await running.WaitAsync(Timeout);

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Message, Is.EqualTo("已取消，未做改动。"));
        Assert.That(harness.Backend.RollbackCalls, Is.EqualTo(1));
        // old 已被卸掉，必须装回
        Assert.That(harness.Backend.LastRollbackRestore, Is.EqualTo(new[] { "old" }));
        Assert.That(harness.Backend.Installed, Is.EqualTo(new[] { "old" }));
    }

    [Test]
    public async Task ApplyAsync_ExternalTokenCancellation_PropagatesToQueue()
    {
        using var harness = new Harness();
        harness.Backend.InstallGate = new ManualResetEventSlim(false);
        using var cts = new CancellationTokenSource();

        var running = harness.Service.ApplyAsync(
            new ChangeSetPreview(new[] { Item("new", ChangeAction.Install) }, Array.Empty<string>(), 0,
                                 Array.Empty<string>()), null, cts.Token);
        Assert.That(harness.Backend.InstallStarted.Wait(Timeout), Is.True, "后台未走到安装阶段");

        await cts.CancelAsync();
        harness.Backend.InstallGate.Set();

        var result = await running.WaitAsync(Timeout);

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Message, Is.EqualTo("已取消，未做改动。"));
        Assert.That(harness.Backend.RollbackCalls, Is.EqualTo(1));
        Assert.That(harness.Backend.Installed, Is.Empty);
    }

    [Test]
    public async Task ApplyAsync_SecondCallWhileRunning_IsRejected()
    {
        using var harness = new Harness();
        harness.Backend.InstallGate = new ManualResetEventSlim(false);
        var changeSet = new ChangeSetPreview(new[] { Item("new", ChangeAction.Install) },
                                             Array.Empty<string>(), 0, Array.Empty<string>());

        var running = harness.Service.ApplyAsync(changeSet);
        Assert.That(harness.Backend.InstallStarted.Wait(Timeout), Is.True, "后台未走到安装阶段");

        var rejected = await harness.Service.ApplyAsync(changeSet).WaitAsync(Timeout);

        harness.Backend.InstallGate.Set();
        var first = await running.WaitAsync(Timeout);

        Assert.That(rejected.IsSuccess, Is.False);
        Assert.That(rejected.Message, Does.Contain("已有正在进行的安装/卸载任务"));
        Assert.That(first.IsSuccess, Is.True);
    }

    [Test]
    public async Task ApplyAsync_ReportsProgressUnderQueueTaskId()
    {
        using var harness = new Harness();
        harness.Backend.Installed.Add("old");
        var changeSet = new ChangeSetPreview(
            new[] { Item("old", ChangeAction.Remove), Item("new", ChangeAction.Install) },
            Array.Empty<string>(), 0, Array.Empty<string>());

        var result = await harness.Service.ApplyAsync(changeSet, harness.ProgressSink).WaitAsync(Timeout);
        var reports = harness.Reports;

        Assert.That(result.IsSuccess, Is.True);
        Assert.That(reports, Is.Not.Empty);
        Assert.That(reports.All(report => report.Kind == InstallerService.TaskKind), Is.True);
        // 所有进度必须挂在队列分配的同一个任务 id 上，否则 UI 无法把取消按钮对上任务
        Assert.That(reports.Select(report => report.TaskId).Distinct().Count(), Is.EqualTo(1));
        Assert.That(reports.Last().State, Is.EqualTo(OperationState.Succeeded));
        Assert.That(reports.Last().Percent, Is.EqualTo(100));
        Assert.That(reports.Last().CanCancel, Is.False);
        Assert.That(reports.Any(report => report.State == OperationState.Running), Is.True);
    }

    [Test]
    public async Task ApplyAsync_BackendProgressCarriesPhaseAndItemCounts()
    {
        using var harness = new Harness();
        var changeSet = new ChangeSetPreview(
            new[] { Item("alpha", ChangeAction.Install), Item("beta", ChangeAction.Install) },
            Array.Empty<string>(), 0, Array.Empty<string>());

        var result = await harness.Service.ApplyAsync(changeSet, harness.ProgressSink).WaitAsync(Timeout);

        Assert.That(result.IsSuccess, Is.True);
        var sent = harness.Backend.SentProgress;
        // 下载段 → 安装段：两条消息都带 n/N，且 Phase 明确指出是哪一段
        var download = sent.Single(item => item.Phase == InstallerPhase.Download);
        Assert.That(download.Message, Does.Contain("正在下载 1/2"), "下载段每条消息都要带 n/N");
        Assert.That(download.ItemIndex, Is.EqualTo(1));
        Assert.That(download.ItemTotal, Is.EqualTo(2));

        var install = sent.Single(item => item.Phase == InstallerPhase.Install);
        Assert.That(install.Message, Does.Contain("正在安装 1/2"), "安装段每条消息都要带 m/M");
        Assert.That(install.ItemIndex, Is.EqualTo(1));
        Assert.That(install.ItemTotal, Is.EqualTo(2));

        // 桥接到 VM：阶段徽标必须真的进 OperationProgress，否则改造只是"后端自嗨"
        var stages = harness.Reports
            .Select(report => report.StageLabel)
            .Where(label => !string.IsNullOrWhiteSpace(label))
            .ToArray();
        Assert.That(stages, Does.Contain("下载 1/2"), "下载阶段要带 n/N 徽标");
        Assert.That(stages, Does.Contain("安装 1/2"), "安装阶段要带 m/M 徽标");
        Assert.That(harness.Reports.Any(report => report.StageLabel is null), Is.True,
                    "没有条目计数的进度（解析依赖/准备就绪）不应伪造阶段徽标");

        // 位置参数构造的老代码不能炸：Phase 默认 Install，条目计数默认 null
        var legacy = new InstallerBackendProgress("旧构造点", 42);
        Assert.That(legacy.Phase, Is.EqualTo(InstallerPhase.Install));
        Assert.That(legacy.ItemIndex, Is.Null);
        Assert.That(legacy.ItemTotal, Is.Null);
        Assert.That(legacy.Percent, Is.EqualTo(42));
    }

    [Test]
    public async Task ApplyAsync_ReportsProgressMonotonicallyNonDecreasing()
    {
        using var harness = new Harness();
        harness.Backend.Installed.Add("old");
        var changeSet = new ChangeSetPreview(
            new[] { Item("old", ChangeAction.Remove), Item("new", ChangeAction.Install) },
            Array.Empty<string>(), 0, Array.Empty<string>());

        var result = await harness.Service.ApplyAsync(changeSet, harness.ProgressSink).WaitAsync(Timeout);
        var reports = harness.Reports;

        Assert.That(result.IsSuccess, Is.True);
        Assert.That(reports, Is.Not.Empty);
        // 先卸后装，但百分比只增不减：8% → 卸载段 → 下载/安装段 → 100%
        for (var i = 1; i < reports.Count; i++)
        {
            Assert.That(reports[i].Percent, Is.GreaterThanOrEqualTo(reports[i - 1].Percent),
                        $"进度回退了：{reports[i - 1].Percent} → {reports[i].Percent}（{reports[i].Message}）");
        }

        // 队列自己会先报一条排队进度，所以只断言 8% 起点确实出现过
        Assert.That(reports.Any(report => report.Percent == 8), Is.True);
        Assert.That(reports.Any(report => report.Message?.Contains("正在安装", StringComparison.Ordinal) == true),
                    Is.True);
        Assert.That(reports.Last().Percent, Is.EqualTo(100));
    }

    [Test]
    public async Task ApplyAsync_ProgressIsMarshalledToUiThread()
    {
        // 模拟后台线程 + 排队式封送：没有 PumpPosts 之前 UI 一条进度都不该收到
        using var harness = new Harness(queuePosts: true, onUiThread: false);

        var result = await harness.Service.ApplyAsync(
            new ChangeSetPreview(new[] { Item("new", ChangeAction.Install) }, Array.Empty<string>(), 0,
                                 Array.Empty<string>()), harness.ProgressSink).WaitAsync(Timeout);

        Assert.That(result.IsSuccess, Is.True);
        Assert.That(harness.Reports, Is.Empty, "未封送前不应直接回调 UI");

        harness.Dispatcher.PumpPosts();

        Assert.That(harness.Reports, Is.Not.Empty);
        Assert.That(harness.Reports.Last().State, Is.EqualTo(OperationState.Succeeded));
    }

    [Test]
    public async Task ApplyAsync_RollbackFailure_IsReportedInResult()
    {
        using var harness = new Harness();
        harness.Backend.InstallHandler = (plan, _, _) => InstallerBackendResult.Fail("安装失败。", null, plan.ToInstall);
        harness.Backend.RollbackResult = InstallerBackendResult.Fail("回滚未完全成功。", null, new[] { "new" });

        var result = await harness.Service.ApplyAsync(
            new ChangeSetPreview(new[] { Item("new", ChangeAction.Install) }, Array.Empty<string>(), 0,
                                 Array.Empty<string>())).WaitAsync(Timeout);

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Message, Does.Contain("部分模组未能还原"));
        Assert.That(harness.Backend.RollbackCalls, Is.EqualTo(1));
    }

    private static ChangeSetPreview EmptyChangeSet()
        => new(Array.Empty<ChangeItemDto>(), Array.Empty<string>(), 0, Array.Empty<string>());

    private static ChangeItemDto Item(string identifier, ChangeAction action,
                                      ChangeOrigin origin = ChangeOrigin.UserRequested)
        => new(identifier, identifier, action, null, null, origin, null);

    /// <summary>
    /// 同步收集进度：不用 <see cref="Progress{T}"/>，因为它会把回调转投线程池，
    /// 那样就分不清「封送是否发生过」了（本测试要断言的正是这一点）。
    /// </summary>
    private sealed class Collector : IProgress<OperationProgress>
    {
        private readonly ConcurrentQueue<OperationProgress> _sink;

        public Collector(ConcurrentQueue<OperationProgress> sink) => _sink = sink;

        public void Report(OperationProgress value) => _sink.Enqueue(value);
    }

    /// <summary>把服务、真实队列、假后端与假调度器组装起来，负责释放队列。</summary>
    private sealed class Harness : IDisposable
    {
        private readonly ConcurrentQueue<OperationProgress> _reports = new();

        public Harness(bool backendAvailable = true, bool queuePosts = false, bool onUiThread = true)
        {
            Dispatcher = new FakeDispatcher(queuePosts) { IsOnUiThread = onUiThread };
            Queue = new OperationQueue(Dispatcher);
            Factory = new InstallerFakeBackendFactory { Backend = backendAvailable ? Backend : null };
            Service = new InstallerService(Factory, Queue, Dispatcher);
            ProgressSink = new Collector(_reports);
        }

        public FakeDispatcher Dispatcher { get; }

        public OperationQueue Queue { get; }

        public InstallerFakeBackend Backend { get; } = new();

        public InstallerFakeBackendFactory Factory { get; }

        public InstallerService Service { get; }

        public IProgress<OperationProgress> ProgressSink { get; }

        public IReadOnlyList<OperationProgress> Reports => _reports.ToArray();

        public void Dispose()
        {
            Queue.Dispose();
            Backend.RemoveStarted.Dispose();
            Backend.InstallStarted.Dispose();
            Backend.RemoveGate?.Dispose();
            Backend.InstallGate?.Dispose();
        }
    }
}
