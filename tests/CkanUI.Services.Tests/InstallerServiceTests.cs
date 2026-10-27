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
        Assert.That(result.Message, Does.Contain("依赖解析失败"));
        Assert.That(result.Detail, Does.Contain("缺少依赖 X"));
        Assert.That(harness.Backend.Calls, Does.Not.Contain("install"));
        Assert.That(harness.Backend.Calls, Does.Not.Contain("rollback"));
        Assert.That(harness.Backend.Installed, Is.EqualTo(new[] { "old" }));
    }

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
