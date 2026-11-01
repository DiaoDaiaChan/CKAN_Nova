using System;
using System.Linq;

using CkanUI.Services.Abstractions.Dtos;
using CkanUI.TestSupport;
using CkanUI.ViewModels.Operations;
using NUnit.Framework;

namespace CkanUI.ViewModels.Tests;

/// <summary>
/// 操作队列浮层视图模型（06 篇 VM-05 / UI-08）：快照投影、聚合百分比、取消命令、
/// 空队列、线程封送、清除已完成、完成淡出、字节/速率推算。
/// </summary>
[TestFixture]
public sealed class OperationsQueueViewModelTests
{
    private FakeOperationQueue _queue = null!;
    private FakeDispatcher _dispatcher = null!;
    private FakeOperationClock _clock = null!;

    [SetUp]
    public void SetUp()
    {
        _queue = new FakeOperationQueue();
        _dispatcher = new FakeDispatcher();
        _clock = new FakeOperationClock();
    }

    [TearDown]
    public void TearDown() => _queue.Reset();

    [Test]
    public void EmptyQueue_HidesOverlayAndShowsEmptyText()
    {
        var vm = CreateViewModel();

        Assert.That(vm.IsVisible, Is.False);
        Assert.That(vm.Items, Is.Empty);
        Assert.That(vm.OverallPercent, Is.EqualTo(0));
        Assert.That(vm.OverallPercentText, Is.EqualTo("0%"));
        Assert.That(vm.SummaryText, Is.EqualTo("无后台任务"));
        Assert.That(vm.EmptyText, Is.EqualTo("无后台任务"));
        Assert.That(vm.HasCancellable, Is.False);
        Assert.That(vm.CancelAllCommand.CanExecute(null), Is.False);
        Assert.That(vm.ClearCompletedCommand.CanExecute(null), Is.False);
    }

    [Test]
    public void Constructor_ProjectsExistingSnapshotWithoutEvent()
    {
        _queue.Seed(Running("t1", "正在下载模组 A", 42, "download"));

        var vm = CreateViewModel();

        Assert.That(vm.Items, Has.Count.EqualTo(1));
        Assert.That(vm.Items[0].Title, Is.EqualTo("正在下载模组 A"));
        Assert.That(vm.Items[0].KindText, Is.EqualTo("下载"));
        Assert.That(vm.Items[0].Percent, Is.EqualTo(42));
        Assert.That(vm.Items[0].PercentText, Is.EqualTo("42%"));
        Assert.That(vm.Items[0].StateText, Is.EqualTo("进行中"));
        Assert.That(vm.IsVisible, Is.True);
        Assert.That(vm.IsExpanded, Is.True);
    }

    [Test]
    public void EmptyTitle_FallsBackToPlaceholder()
    {
        _queue.Seed(Running("t1", "   ", 10, "download"));

        var vm = CreateViewModel();

        Assert.That(vm.Items[0].Title, Is.EqualTo("未命名任务"));
    }

    [Test]
    public void Percent_IsClampedToRange()
    {
        _queue.Seed(Running("t1", "越界", 250, "download"));

        var vm = CreateViewModel();

        Assert.That(vm.Items[0].Percent, Is.EqualTo(100));
        Assert.That(vm.OverallPercent, Is.EqualTo(100));
    }

    [Test]
    public void AggregatePercent_AveragesFinishedAndRunningTasks()
    {
        _queue.Seed(
            Running("t1", "任务一", 50, "download"),
            Running("t2", "任务二", 50, "install"),
            Finished("t3", "任务三", OperationState.Succeeded));

        var vm = CreateViewModel();

        Assert.That(vm.OverallPercent, Is.EqualTo(66.666).Within(0.01));
        Assert.That(vm.TotalCount, Is.EqualTo(3));
        Assert.That(vm.ActiveCount, Is.EqualTo(2));
        Assert.That(vm.CompletedCount, Is.EqualTo(1));
        Assert.That(vm.IsAllCompleted, Is.False);
        Assert.That(vm.OverlayOpacity, Is.EqualTo(OperationQueueViewModel.ActiveOpacity));
    }

    [Test]
    public void AggregatePercent_CountsFailedTaskAsFinished()
    {
        _queue.Seed(
            Finished("t1", "成功", OperationState.Succeeded),
            Finished("t2", "失败", OperationState.Failed));

        var vm = CreateViewModel();

        Assert.That(vm.OverallPercent, Is.EqualTo(100));
        Assert.That(vm.FailedCount, Is.EqualTo(1));
        Assert.That(vm.IsAllCompleted, Is.True);
        Assert.That(vm.SummaryText, Does.Contain("失败 1"));
    }

    [Test]
    public void AllCompleted_FadesOutButStaysVisible()
    {
        _queue.Seed(Finished("t1", "下载完成", OperationState.Succeeded));

        var vm = CreateViewModel();

        Assert.That(vm.IsAllCompleted, Is.True);
        Assert.That(vm.OverlayOpacity, Is.EqualTo(OperationQueueViewModel.FadedOpacity));
        Assert.That(vm.IsVisible, Is.True);
        Assert.That(vm.SummaryText, Does.Contain("全部完成"));
    }

    [Test]
    public void UpdatedEvent_IsMarshalledThroughDispatcher()
    {
        _dispatcher = new FakeDispatcher(queuePosts: true);
        var vm = CreateViewModel();

        _queue.Publish(Running("t1", "任务一", 30, "download"));

        Assert.That(vm.Items, Is.Empty, "Post 未执行前不得改状态");
        Assert.That(_dispatcher.Posted, Has.Count.EqualTo(1));

        _dispatcher.PumpPosts();

        Assert.That(vm.Items, Has.Count.EqualTo(1));
        Assert.That(vm.Items[0].Percent, Is.EqualTo(30));
    }

    [Test]
    public void UpdatedEvent_FromBackgroundThread_StillLandsOnDispatcher()
    {
        _dispatcher = new FakeDispatcher(queuePosts: true) { IsOnUiThread = false };
        var vm = CreateViewModel();

        _queue.Publish(Running("t1", "后台任务", 12, "download"));
        _queue.Publish(Running("t1", "后台任务", 48, "download"));

        Assert.That(vm.Items, Is.Empty);
        Assert.That(_dispatcher.Posted, Has.Count.EqualTo(2));

        _dispatcher.PumpPosts();

        Assert.That(vm.Items[0].Percent, Is.EqualTo(48));
    }

    [Test]
    public void Items_AreReplacedWholesaleOnEachSnapshot()
    {
        var vm = CreateViewModel();
        var before = vm.Items;

        _queue.Publish(Running("t1", "任务一", 10, "download"));

        Assert.That(vm.Items, Is.Not.SameAs(before), "列表整体替换，不做增量集合变更");
    }

    [Test]
    public void CancelCommand_RequestsCancelForSingleTask()
    {
        _queue.Seed(Running("t1", "任务一", 10, "download"));
        var vm = CreateViewModel();

        vm.Items[0].CancelCommand.Execute(null);

        Assert.That(_queue.Cancelled, Is.EqualTo(new[] { "t1" }));
    }

    [Test]
    public void CancelAll_OnlyCancelsCancellableRunningTasks()
    {
        _queue.Seed(
            Running("t1", "可取消", 10, "download"),
            Running("t2", "不可取消", 10, "download", canCancel: false),
            Finished("t3", "已结束", OperationState.Succeeded));
        var vm = CreateViewModel();

        Assert.That(vm.CancelAllCommand.CanExecute(null), Is.True);
        vm.CancelAllCommand.Execute(null);

        Assert.That(_queue.Cancelled, Is.EqualTo(new[] { "t1" }));
        Assert.That(vm.Items.Single(i => i.TaskId == "t3").IsCancellable, Is.False);
    }

    [Test]
    public void ClearCompleted_RemovesFinishedRowsOnly()
    {
        _queue.Seed(
            Running("t1", "进行中", 40, "download"),
            Finished("t2", "已完成", OperationState.Succeeded));
        var vm = CreateViewModel();

        Assert.That(vm.ClearCompletedCommand.CanExecute(null), Is.True);
        vm.ClearCompletedCommand.Execute(null);

        Assert.That(vm.Items, Has.Count.EqualTo(1));
        Assert.That(vm.Items[0].TaskId, Is.EqualTo("t1"));
        Assert.That(vm.TotalCount, Is.EqualTo(1));
        Assert.That(vm.ClearCompletedCommand.CanExecute(null), Is.False);
    }

    [Test]
    public void ClearedTask_ReappearsWhenItBecomesActiveAgain()
    {
        _queue.Seed(Finished("t1", "已完成", OperationState.Succeeded));
        var vm = CreateViewModel();
        vm.ClearCompletedCommand.Execute(null);
        Assert.That(vm.Items, Is.Empty);

        _queue.Publish(Running("t1", "重试中", 5, "download"));

        Assert.That(vm.Items, Has.Count.EqualTo(1));
        Assert.That(vm.Items[0].StateText, Is.EqualTo("进行中"));
    }


    [Test]
    public void EmptyQueue_WhenUserExpandsManually_DoesNotCollapseOnTimerSnapshot()
    {
        var vm = CreateViewModel();
        Assert.That(vm.IsExpanded, Is.False, "默认初始无任务时不展开");

        // 用户主动点击底栏胶囊展开详细窗口
        vm.ToggleExpandCommand.Execute(null);
        Assert.That(vm.IsExpanded, Is.True, "点击后展开");

        // 模拟后台定时器 100ms 推动的空快照
        _queue.Publish();

        // 断言：空任务快照绝不能强行把用户手动展开的窗口关掉
        Assert.That(vm.IsExpanded, Is.True, "用户手动点开查看时，空队列快照不能强行关闭窗口");
    }

    [Test]
    public void EmptyQueue_AfterLastTaskLeaves_CollapsesAndResetsPercent()
    {
        _queue.Seed(Running("t1", "任务一", 30, "download"));
        var vm = CreateViewModel();
        Assert.That(vm.IsExpanded, Is.True);

        _queue.Publish();

        Assert.That(vm.IsVisible, Is.False);
        Assert.That(vm.IsExpanded, Is.False);
        Assert.That(vm.OverallPercent, Is.EqualTo(0));
        Assert.That(vm.OverlayOpacity, Is.EqualTo(OperationQueueViewModel.ActiveOpacity));
    }

    [Test]
    public void ByteProgress_AndRate_AreDerivedFromMessageAndClock()
    {
        _queue.Seed(Running("t1", "恢复数据", 32, "restore", message: "正在恢复数据 12.4 MB / 37.9 MB"));
        var vm = CreateViewModel();

        // 换时钟会丢弃旧采样，因此这里用假时钟重新采两次样：第一次只记基线，第二次才出速率
        vm.Clock = _clock;
        _queue.Publish(Running("t1", "恢复数据", 32, "restore", message: "正在恢复数据 12.4 MB / 37.9 MB"));
        _clock.Advance(1000);
        _queue.Publish(Running("t1", "恢复数据", 65, "restore", message: "正在恢复数据 24.8 MB / 37.9 MB"));

        var item = vm.Items[0];
        Assert.That(item.HasByteProgress, Is.True);
        Assert.That(item.BytesText, Is.EqualTo("24.8 MB / 37.9 MB"));
        Assert.That(item.SpeedText, Does.EndWith("/s"));
        Assert.That(item.RemainingText, Does.StartWith("剩余 "));
    }

    [Test]
    public void MissingByteProgress_HidesByteColumnWithPlaceholder()
    {
        _queue.Seed(Running("t1", "安装模组 A", 40, "install", message: "正在安装模组 A"));

        var vm = CreateViewModel();

        Assert.That(vm.Items[0].HasByteProgress, Is.False);
        Assert.That(vm.Items[0].BytesText, Is.EqualTo(OperationDisplayFormatter.Placeholder));
        Assert.That(vm.Items[0].SpeedText, Is.EqualTo(OperationDisplayFormatter.Placeholder));
    }

    [Test]
    public void RunningTasksSortBeforeFinishedTasks()
    {
        _queue.Seed(
            Finished("t1", "已完成", OperationState.Succeeded),
            Running("t2", "进行中", 10, "download"),
            Finished("t3", "失败", OperationState.Failed));

        var vm = CreateViewModel();

        Assert.That(vm.Items.Select(i => i.TaskId), Is.EqualTo(new[] { "t2", "t3", "t1" }));
    }

    [Test]
    public void Deactivate_StopsReceivingUpdates()
    {
        var vm = CreateViewModel();
        vm.Deactivate();

        _queue.Publish(Running("t1", "任务一", 20, "download"));

        Assert.That(vm.Items, Is.Empty);

        vm.Activate();
        _queue.Publish(Running("t1", "任务一", 30, "download"));

        Assert.That(vm.Items, Has.Count.EqualTo(1));
    }

    [Test]
    public void Dispose_UnsubscribesAndIgnoresLateEvents()
    {
        var vm = CreateViewModel();
        vm.Dispose();

        Assert.DoesNotThrow(() => _queue.Publish(Running("t1", "任务一", 20, "download")));
        Assert.That(vm.Items, Is.Empty);
    }

    private OperationQueueViewModel CreateViewModel() => new(_queue, _dispatcher);

    private static OperationProgress Running(
        string id,
        string title,
        double percent,
        string kind,
        bool canCancel = true,
        string? message = null)
        => new(id, kind, title, message, percent, canCancel, OperationState.Running);

    private static OperationProgress Finished(string id, string title, OperationState state)
        => new(id, "download", title, null, 100, false, state);
}
