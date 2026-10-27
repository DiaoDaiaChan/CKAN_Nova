using CkanUI.Services.Abstractions;
using CkanUI.Services.Abstractions.Dtos;
using CkanUI.ViewModels.Pages;
using CkanUI.ViewModels.Pages.Changes;
using NUnit.Framework;

namespace CkanUI.ViewModels.Tests;

/// <summary>
/// 变更集页 VM（03 篇 §4 V-02 / §4.1 状态机 / §9 双通道 / §10 空状态；06 篇 UI-04·VM-02；04 篇 §2）。
/// </summary>
[TestFixture]
public sealed class ChangesPageViewModelTests
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

    private ChangesPageViewModel Create(bool withInstaller = true, bool withChangeSets = true)
    {
        var vm = new ChangesPageViewModel(
            withChangeSets ? _changeSets : null,
            withInstaller ? _installer : null,
            _errors,
            null,
            new ChangesImmediateDispatcher());
        vm.Activate();
        return vm;
    }

    [Test]
    public async Task EmptyRequests_ShowsEmptyStateWithoutCallingService()
    {
        var vm = Create();

        vm.SetRequests(Array.Empty<ChangeRequest>());
        await vm.PendingWork;

        Assert.That(vm.ShowEmptyState, Is.True);
        Assert.That(vm.HasRows, Is.False);
        Assert.That(vm.Rows, Is.Empty);
        Assert.That(vm.State, Is.EqualTo(ChangeExecutionState.Idle));
        Assert.That(vm.EmptyStateTitle, Is.EqualTo("还没有待应用的变更"));
        Assert.That(vm.SummaryText, Is.EqualTo("还没有待应用的变更"));
        Assert.That(vm.CanApply, Is.False);
        Assert.That(_changeSets.BuildCount, Is.Zero);
    }

    [Test]
    public async Task Preview_ProducesCountsSizeAndRows()
    {
        var preview = ChangesFakes.Preview(
            new[]
            {
                ChangesFakes.Item("a", "Alpha", ChangeAction.Install, bytes: 8_000_000),
                ChangesFakes.Item("b", "Beta", ChangeAction.Install, origin: ChangeOrigin.Dependency, bytes: 6_000_000),
                ChangesFakes.Item("c", "Gamma", ChangeAction.Upgrade, from: "1.0.0", to: "2.0.0", bytes: 6_000_000),
                ChangesFakes.Item("d", "Delta", ChangeAction.Remove, from: "1.2.0", to: null),
            },
            20_000_000);
        _changeSets.NextResult = OperationResult<ChangeSetPreview>.Ok(preview);

        var vm = Create();
        vm.SetRequests(ChangesFakes.Requests("a", "b", "c", "d"));
        await vm.PendingWork;

        Assert.That(vm.State, Is.EqualTo(ChangeExecutionState.Ready));
        Assert.That(vm.TotalCount, Is.EqualTo(4));
        Assert.That(vm.InstallCount, Is.EqualTo(2));
        Assert.That(vm.UpgradeCount, Is.EqualTo(1));
        Assert.That(vm.RemoveCount, Is.EqualTo(1));
        Assert.That(vm.Rows.Count, Is.EqualTo(4));
        Assert.That(vm.HasRows, Is.True);
        Assert.That(vm.ShowEmptyState, Is.False);
        Assert.That(vm.SummaryText, Does.Contain("将安装 2 个"));
        Assert.That(vm.SummaryText, Does.Contain("升级 1 个"));
        Assert.That(vm.SummaryText, Does.Contain("卸载 1 个"));
        Assert.That(vm.DownloadText, Is.EqualTo("预计下载 19.1 MB"));
        Assert.That(vm.SummaryText, Does.Contain("19.1 MB"));

        // 行投影：动作/来源/版本/体积文案都在 VM 侧（03 篇 §11）
        var upgradeRow = vm.Rows.Single(row => row.Identifier == "c");
        Assert.That(upgradeRow.ActionText, Is.EqualTo("升级"));
        Assert.That(upgradeRow.FromVersionText, Is.EqualTo("1.0.0"));
        Assert.That(upgradeRow.ToVersionText, Is.EqualTo("2.0.0"));
        var dependencyRow = vm.Rows.Single(row => row.Identifier == "b");
        Assert.That(dependencyRow.OriginBadgeText, Is.EqualTo("依赖"));
        Assert.That(dependencyRow.ShowDependencyBadge, Is.True);
        var removeRow = vm.Rows.Single(row => row.Identifier == "d");
        Assert.That(removeRow.IsRemoval, Is.True);
        Assert.That(removeRow.SizeText, Is.EqualTo("—"));

        // 明细表排序：安装 → 升级 → 降级 → 替换 → 卸载
        Assert.That(vm.Rows[0].Identifier, Is.EqualTo("a"));
        Assert.That(vm.Rows[^1].Identifier, Is.EqualTo("d"));
    }

    [Test]
    public async Task Conflicts_DisableApplyAndRequireText()
    {
        var preview = ChangesFakes.Preview(
            new[] { ChangesFakes.Item("a", "Alpha") },
            conflicts: new[] { "Alpha 1.0.0 与 Beta 2.0.0 依赖冲突", "Gamma 需要 KSP 1.12 但当前是 1.11" });
        _changeSets.NextResult = OperationResult<ChangeSetPreview>.Ok(preview);

        var vm = Create();
        vm.SetRequests(ChangesFakes.Requests("a"));
        await vm.PendingWork;

        Assert.That(vm.HasConflicts, Is.True);
        Assert.That(vm.Conflicts.Count, Is.EqualTo(2));
        Assert.That(vm.ConflictTitle, Is.Not.Empty);
        Assert.That(vm.ConflictTitle, Does.Contain("2 个冲突"));
        Assert.That(vm.ConflictHint, Is.Not.Empty);

        // 06 篇 UI-04 门禁：冲突时"开始"按钮必须禁用
        Assert.That(vm.CanApply, Is.False);
        Assert.That(vm.ApplyCommand.CanExecute(null), Is.False);
        Assert.That(vm.StatusText, Does.Contain("冲突"));
    }

    [Test]
    public async Task BuildFailure_ShowsReadableStatusAndReportsError()
    {
        _changeSets.NextResult = OperationResult<ChangeSetPreview>.Fail("仓库索引已过期，请先刷新", "index.json > 7 天");

        var vm = Create();
        vm.SetRequests(ChangesFakes.Requests("a"));
        await vm.PendingWork;

        Assert.That(vm.State, Is.EqualTo(ChangeExecutionState.Failed));
        Assert.That(vm.StatusText, Is.EqualTo("仓库索引已过期，请先刷新"));
        Assert.That(_errors.MessageReports, Is.EqualTo(1));
        Assert.That(_errors.Entries[0].UserMessage, Is.EqualTo("仓库索引已过期，请先刷新"));
        Assert.That(_errors.Entries[0].Detail, Is.EqualTo("index.json > 7 天"));

        // 03 篇 §9 双通道：错误必须同时有文字
        Assert.That(vm.HasErrorText, Is.True);
        Assert.That(vm.LastErrorText, Is.EqualTo("仓库索引已过期，请先刷新"));
        Assert.That(vm.ShowEmptyState, Is.True);
    }

    [Test]
    public async Task BuildThrows_ReportsExceptionAndShowsReadableStatus()
    {
        _changeSets.ThrowOnBuild = new InvalidOperationException("解析 registry.json 失败");

        var vm = Create();
        vm.SetRequests(ChangesFakes.Requests("a"));
        await vm.PendingWork;

        Assert.That(vm.State, Is.EqualTo(ChangeExecutionState.Failed));
        Assert.That(vm.StatusText, Does.Contain("失败"));
        Assert.That(_errors.ExceptionReports, Is.EqualTo(1));
        Assert.That(_errors.Entries[0].Exception, Is.TypeOf<InvalidOperationException>());
    }

    [Test]
    public async Task ApplyWithoutInstaller_ShowsM2HintOnly()
    {
        var preview = ChangesFakes.Preview(new[] { ChangesFakes.Item("a", "Alpha") }, 1_000);
        _changeSets.NextResult = OperationResult<ChangeSetPreview>.Ok(preview);

        var vm = Create(withInstaller: false);
        vm.SetRequests(ChangesFakes.Requests("a"));
        await vm.PendingWork;
        Assert.That(vm.CanApply, Is.True);

        await vm.ApplyCommand.ExecuteAsync(null);

        Assert.That(vm.StatusText, Is.EqualTo(ChangesPageViewModel.MissingInstallerText));
        Assert.That(vm.StatusText, Does.Contain("M2"));
        Assert.That(vm.State, Is.EqualTo(ChangeExecutionState.Ready));
        Assert.That(_installer.ApplyCount, Is.Zero);
        Assert.That(_errors.MessageReports, Is.Zero);
    }

    [Test]
    public async Task ApplySuccess_ReachesDoneWithResultDetail()
    {
        var preview = ChangesFakes.Preview(new[] { ChangesFakes.Item("a", "Alpha") }, 1_000);
        _changeSets.NextResult = OperationResult<ChangeSetPreview>.Ok(preview);
        _installer.Result = OperationResult<InstallReport>.Ok(ChangesFakes.SuccessReport());

        var vm = Create();
        vm.SetRequests(ChangesFakes.Requests("a"));
        await vm.PendingWork;

        await vm.ApplyCommand.ExecuteAsync(null);

        Assert.That(vm.State, Is.EqualTo(ChangeExecutionState.Done));
        Assert.That(vm.ExecutionPercent, Is.EqualTo(100));
        Assert.That(vm.HasResult, Is.True);
        Assert.That(vm.ResultText, Does.Contain("已安装"));
        Assert.That(_installer.ApplyCount, Is.EqualTo(1));
        Assert.That(_installer.Received, Is.SameAs(preview));
        Assert.That(vm.HasErrorText, Is.False);
    }

    [Test]
    public async Task ApplyPartialFailure_ReachesFailedAndReports()
    {
        var preview = ChangesFakes.Preview(new[] { ChangesFakes.Item("a", "Alpha") }, 1_000);
        _changeSets.NextResult = OperationResult<ChangeSetPreview>.Ok(preview);
        _installer.Result = OperationResult<InstallReport>.Ok(ChangesFakes.PartialFailureReport());

        var vm = Create();
        vm.SetRequests(ChangesFakes.Requests("a"));
        await vm.PendingWork;

        await vm.ApplyCommand.ExecuteAsync(null);

        Assert.That(vm.State, Is.EqualTo(ChangeExecutionState.Failed));
        Assert.That(vm.ResultText, Does.Contain("B"));
        Assert.That(_errors.MessageReports, Is.EqualTo(1));
        Assert.That(vm.StatusText, Does.Contain("失败"));
    }

    [Test]
    public async Task ApplyThrows_ReportsExceptionAndFails()
    {
        var preview = ChangesFakes.Preview(new[] { ChangesFakes.Item("a", "Alpha") }, 1_000);
        _changeSets.NextResult = OperationResult<ChangeSetPreview>.Ok(preview);
        _installer.ThrowOnApply = new IOException("磁盘写入失败");

        var vm = Create();
        vm.SetRequests(ChangesFakes.Requests("a"));
        await vm.PendingWork;

        // 05 篇：异常必须被 VM 收口成状态文本 + IErrorReporter，不得从命令里冒泡
        Assert.DoesNotThrowAsync(async () => await vm.ApplyCommand.ExecuteAsync(null));

        Assert.That(vm.State, Is.EqualTo(ChangeExecutionState.Failed));
        Assert.That(vm.StatusText, Is.EqualTo("应用变更时发生错误，请查看错误栏。"));
        Assert.That(vm.ResultText, Is.EqualTo("磁盘写入失败"));
        Assert.That(_errors.ExceptionReports, Is.EqualTo(1));
        Assert.That(_errors.Entries[0].UserMessage, Does.Contain("应用变更"));
    }

    [Test]
    public async Task CancelDuringApply_ReachesCancelled()
    {
        var preview = ChangesFakes.Preview(new[] { ChangesFakes.Item("a", "Alpha") }, 1_000);
        _changeSets.NextResult = OperationResult<ChangeSetPreview>.Ok(preview);
        _installer.WaitForCancellation = true;

        var vm = Create();
        vm.SetRequests(ChangesFakes.Requests("a"));
        await vm.PendingWork;

        var running = vm.ApplyCommand.ExecuteAsync(null);
        Assert.That(vm.State, Is.EqualTo(ChangeExecutionState.Applying));
        Assert.That(vm.IsApplying, Is.True);
        Assert.That(vm.CancelCommand.CanExecute(null), Is.True);

        vm.CancelCommand.Execute(null);
        await running;

        Assert.That(vm.State, Is.EqualTo(ChangeExecutionState.Cancelled));
        Assert.That(vm.ResultText, Is.Not.Empty);
        Assert.That(vm.StatusText, Does.Contain("回滚"));
    }

    [Test]
    public async Task Deactivate_ClearsSubscriptionAndStopsApplying()
    {
        var vm = Create();
        Assert.That(_errors.HandlerCount, Is.EqualTo(1));

        // IActivatable 幂等：重复 Activate 不重复订阅、不重复计算
        vm.Activate();
        Assert.That(_errors.HandlerCount, Is.EqualTo(1));

        var preview = ChangesFakes.Preview(new[] { ChangesFakes.Item("a", "Alpha") }, 1_000);
        _changeSets.NextResult = OperationResult<ChangeSetPreview>.Ok(preview);
        vm.SetRequests(ChangesFakes.Requests("a"));
        await vm.PendingWork;

        vm.Deactivate();
        Assert.That(_errors.HandlerCount, Is.Zero);

        _errors.Report("测试", "停用后不应再收到");
        Assert.That(vm.LastErrorText, Is.Empty);
        Assert.That(vm.HasErrorText, Is.False);

        // 再激活可以重新订阅
        vm.Activate();
        Assert.That(_errors.HandlerCount, Is.EqualTo(1));
    }

    [Test]
    public async Task RemoveItemCommand_DropsRowFromCurrentPreview()
    {
        var preview = ChangesFakes.Preview(
            new[] { ChangesFakes.Item("a", "Alpha", bytes: 3_000), ChangesFakes.Item("b", "Beta", bytes: 2_000) },
            5_000);
        _changeSets.NextResult = OperationResult<ChangeSetPreview>.Ok(preview);

        var vm = Create();
        vm.SetRequests(ChangesFakes.Requests("a", "b"));
        await vm.PendingWork;

        var target = vm.Rows.Single(row => row.Identifier == "a");
        vm.RemoveItemCommand.Execute(target);

        Assert.That(vm.Rows.Count, Is.EqualTo(1));
        Assert.That(vm.TotalCount, Is.EqualTo(1));
        Assert.That(vm.DownloadText, Is.EqualTo("预计下载 2.0 KB"));
        Assert.That(vm.StatusText, Does.Contain("Alpha"));
    }

    [Test]
    public async Task SetRequests_DeduplicatesIdentifiers()
    {
        var vm = Create();
        vm.SetRequests(new[]
        {
            new ChangeRequest("a", ChangeAction.Install),
            new ChangeRequest("A", ChangeAction.Install),
            new ChangeRequest("b", ChangeAction.Remove),
            new ChangeRequest("  ", ChangeAction.Install),
        });
        await vm.PendingWork;

        Assert.That(vm.Requests.Count, Is.EqualTo(2));
        Assert.That(_changeSets.ReceivedRequests[0].Count, Is.EqualTo(2));
    }

    [Test]
    public async Task MissingService_KeepsPageUsableWithExplicitHint()
    {
        var vm = Create(withChangeSets: false);
        vm.SetRequests(ChangesFakes.Requests("a"));
        await vm.PendingWork;

        Assert.That(vm.Rows, Is.Empty);
        Assert.That(vm.State, Is.EqualTo(ChangeExecutionState.Idle));
        Assert.That(vm.StatusText, Does.Contain("INT-07"));
        Assert.That(vm.EmptyStateHint, Does.Contain("INT-07"));
        Assert.That(_errors.MessageReports, Is.Zero);
    }

    [Test]
    public async Task LoadPreview_MarksReadyState()
    {
        var vm = Create();
        vm.LoadPreview(ChangesFakes.Preview(new[] { ChangesFakes.Item("a", "Alpha") }, 2_048));

        await vm.PendingWork;

        Assert.That(vm.State, Is.EqualTo(ChangeExecutionState.Ready));
        Assert.That(vm.CanApply, Is.True);
        Assert.That(vm.ApplyCommand.CanExecute(null), Is.True);
        Assert.That(vm.DownloadText, Is.EqualTo("预计下载 2.0 KB"));
    }
}
