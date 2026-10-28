using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CkanUI.Services.Abstractions;
using CkanUI.Services.Abstractions.Dtos;
using CkanUI.ViewModels.Pages;
using CkanUI.ViewModels.Pages.InstallHistory;
using NUnit.Framework;

namespace CkanUI.ViewModels.Tests;

/// <summary>
/// 安装历史页「删除一条 / 清空全部」的视图模型用例（12 篇 F-44 剩余两条缺口）：
/// 破坏性操作必须先过确认对话框、取消时一个字节都不动、删完重读并重算统计文案、
/// 服务不支持或没有对话框时按钮不可用（宁可不给入口，也不静默删数据）。
/// 全部走替身，不落盘。
/// </summary>
[TestFixture]
public sealed class InstallHistoryMaintenancePageViewModelTests
{
    private static readonly DateTimeOffset Base = new(2024, 5, 1, 10, 0, 0, TimeSpan.Zero);

    private FakeHistoryService _history = null!;
    private FakeDialogs _dialogs = null!;
    private ModPacksFakeErrorReporter _errors = null!;

    [SetUp]
    public void SetUp()
    {
        _history = new FakeHistoryService();
        _dialogs = new FakeDialogs();
        _errors = new ModPacksFakeErrorReporter();
    }

    private InstallHistoryPageViewModel CreateViewModel(params InstallHistoryEntry[] entries)
    {
        _history.Entries = entries.ToList();
        return new InstallHistoryPageViewModel(_history, _errors, _dialogs);
    }

    private static InstallHistoryEntry Entry(int seconds, InstallHistoryOperation operation = InstallHistoryOperation.Install)
    {
        var stamp = Base.AddSeconds(seconds);
        return new InstallHistoryEntry(
            stamp,
            "测试实例",
            operation,
            InstallHistoryOutcome.Succeeded,
            new[] { new InstallHistoryChange("A", ChangeAction.Install, null, "1.0") });
    }

    // ---- 能力探测 ----------------------------------------------------------

    [Test]
    public void WithoutDialogService_DeleteAndClearAreUnavailable()
    {
        _history.Entries = new List<InstallHistoryEntry> { Entry(10) };
        var vm = new InstallHistoryPageViewModel(_history, _errors);

        Assert.Multiple(() =>
        {
            // 没有确认对话框就没有「用户确认」这一环：按钮直接不给，避免静默删数据。
            Assert.That(vm.CanEditHistory, Is.False);
            Assert.That(vm.CanDeleteSelected, Is.False);
            Assert.That(vm.CanClearHistory, Is.False);
        });
    }

    [Test]
    public async Task WithoutMaintenanceCapability_DeleteIsUnavailable()
    {
        _history.Entries = new List<InstallHistoryEntry> { Entry(10) };
        var readOnly = new ReadOnlyHistoryService(_history);
        var vm = new InstallHistoryPageViewModel(readOnly, _errors, _dialogs);
        await vm.LoadCommand.ExecuteAsync(null);

        Assert.Multiple(() =>
        {
            Assert.That(vm.CanEditHistory, Is.False);
            Assert.That(vm.CanDeleteSelected, Is.False);
            Assert.That(vm.CanClearHistory, Is.False);
        });
    }

    // ---- 删除一条 ----------------------------------------------------------

    [Test]
    public async Task DeleteSelected_RequiresConfirmation()
    {
        var vm = CreateViewModel(Entry(10), Entry(20, InstallHistoryOperation.Remove));
        await vm.LoadCommand.ExecuteAsync(null);
        vm.SelectedEntry = vm.Entries.Single(row => row.Entry.Operation == InstallHistoryOperation.Install);

        _dialogs.ConfirmResult = false;
        await vm.DeleteSelectedCommand.ExecuteAsync(null);

        Assert.Multiple(() =>
        {
            Assert.That(_dialogs.ConfirmCalls, Is.EqualTo(1));
            Assert.That(_dialogs.LastTitle, Is.EqualTo("删除这条历史？"));
            Assert.That(_dialogs.LastMessage, Does.Contain("删除后无法撤销"));
            // 取消 = 什么都不做：服务不被调用，列表也不变。
            Assert.That(_history.DeleteCalls, Is.Zero);
            Assert.That(vm.Entries, Has.Count.EqualTo(2));
        });
    }

    [Test]
    public async Task DeleteSelected_RemovesEntryAndRecalculatesCount()
    {
        var vm = CreateViewModel(Entry(10), Entry(20, InstallHistoryOperation.Remove));
        await vm.LoadCommand.ExecuteAsync(null);
        vm.SelectedEntry = vm.Entries.Single(row => row.Entry.Operation == InstallHistoryOperation.Remove);

        _dialogs.ConfirmResult = true;
        await vm.DeleteSelectedCommand.ExecuteAsync(null);

        Assert.Multiple(() =>
        {
            Assert.That(_history.DeleteCalls, Is.EqualTo(1));
            Assert.That(_history.LastDeleted, Is.EqualTo(Base.AddSeconds(20)));
            Assert.That(vm.Entries, Has.Count.EqualTo(1));
            Assert.That(vm.CountText, Is.EqualTo("共 1 条历史记录"));
            Assert.That(vm.StatusText, Does.Contain("已删除 1 条历史记录"));
            Assert.That(vm.HasError, Is.False);
        });
    }

    [Test]
    public async Task DeleteSelected_AlreadyGone_ReportsRefreshInsteadOfError()
    {
        var vm = CreateViewModel(Entry(10));
        await vm.LoadCommand.ExecuteAsync(null);
        vm.SelectedEntry = vm.Entries[0];

        // 服务说一行都没删：并发下另一个窗口删掉了，这不算错误。
        _history.DeleteResult = OperationResult<int>.Ok(0);
        _dialogs.ConfirmResult = true;
        await vm.DeleteSelectedCommand.ExecuteAsync(null);

        Assert.Multiple(() =>
        {
            Assert.That(vm.HasError, Is.False);
            Assert.That(vm.StatusText, Does.Contain("已经不存在"));
        });
    }

    [Test]
    public async Task DeleteSelected_Failure_ShowsServiceMessage()
    {
        var vm = CreateViewModel(Entry(10));
        await vm.LoadCommand.ExecuteAsync(null);
        vm.SelectedEntry = vm.Entries[0];

        _history.DeleteResult = OperationResult<int>.Fail(
            "删除安装历史失败，请检查实例目录的写权限后重试。",
            "拒绝访问",
            retryable: true);
        _dialogs.ConfirmResult = true;
        await vm.DeleteSelectedCommand.ExecuteAsync(null);

        Assert.Multiple(() =>
        {
            Assert.That(vm.ErrorText, Does.Contain("删除安装历史失败"));
            Assert.That(vm.ErrorText, Does.Contain("重试"));
            Assert.That(vm.ErrorDetail, Is.EqualTo("拒绝访问"));
            Assert.That(vm.Entries, Has.Count.EqualTo(1), "失败时列表不能被清空");
        });
    }

    [Test]
    public async Task DeleteSelected_WithoutSelection_IsDisabled()
    {
        var vm = CreateViewModel(Entry(10));
        await vm.LoadCommand.ExecuteAsync(null);
        vm.SelectedEntry = null;

        Assert.Multiple(() =>
        {
            Assert.That(vm.CanDeleteSelected, Is.False);
            Assert.That(vm.DeleteSelectedCommand.CanExecute(null), Is.False);
        });
    }

    // ---- 清空全部 ----------------------------------------------------------

    [Test]
    public async Task ClearHistory_RequiresConfirmation()
    {
        var vm = CreateViewModel(Entry(10), Entry(20));
        await vm.LoadCommand.ExecuteAsync(null);

        _dialogs.ConfirmResult = false;
        await vm.ClearHistoryCommand.ExecuteAsync(null);

        Assert.Multiple(() =>
        {
            Assert.That(_dialogs.LastTitle, Is.EqualTo("清空安装历史？"));
            Assert.That(_dialogs.LastMessage, Does.Contain("共 2 条历史记录"));
            Assert.That(_history.ClearCalls, Is.Zero);
            Assert.That(vm.Entries, Has.Count.EqualTo(2));
        });
    }

    [Test]
    public async Task ClearHistory_EmptiesListAndShowsEmptyState()
    {
        var vm = CreateViewModel(Entry(10), Entry(20));
        await vm.LoadCommand.ExecuteAsync(null);

        _dialogs.ConfirmResult = true;
        await vm.ClearHistoryCommand.ExecuteAsync(null);

        Assert.Multiple(() =>
        {
            Assert.That(_history.ClearCalls, Is.EqualTo(1));
            Assert.That(vm.Entries, Is.Empty);
            Assert.That(vm.ShowEmptyState, Is.True);
            Assert.That(vm.CountText, Is.EqualTo("暂无历史记录"));
            Assert.That(vm.StatusText, Does.Contain("已清空安装历史"));
            Assert.That(vm.CanClearHistory, Is.False, "空列表时清空按钮应不可用");
        });
    }

    [Test]
    public async Task ClearHistory_AlreadyEmpty_ReportsInsteadOfError()
    {
        var vm = CreateViewModel(Entry(10));
        await vm.LoadCommand.ExecuteAsync(null);
        _history.ClearResult = OperationResult<int>.Ok(0);

        _dialogs.ConfirmResult = true;
        await vm.ClearHistoryCommand.ExecuteAsync(null);

        Assert.Multiple(() =>
        {
            Assert.That(vm.HasError, Is.False);
            Assert.That(vm.StatusText, Does.Contain("本来就是空的"));
        });
    }

    [Test]
    public async Task EmptyHistory_ClearCommandIsDisabled()
    {
        var vm = CreateViewModel();
        await vm.LoadCommand.ExecuteAsync(null);

        Assert.Multiple(() =>
        {
            Assert.That(vm.CanClearHistory, Is.False);
            Assert.That(vm.ClearHistoryCommand.CanExecute(null), Is.False);
        });
    }

    [Test]
    public async Task BusyState_DisablesBothMaintenanceCommands()
    {
        var vm = CreateViewModel(Entry(10));
        await vm.LoadCommand.ExecuteAsync(null);
        vm.SelectedEntry = vm.Entries[0];

        // 刷新过程中命令入口必须整体关掉，否则两次删除会对着已经过期的选中项竞争。
        var gate = new TaskCompletionSource();
        _history.LoadGate = gate;
        var loading = vm.LoadCommand.ExecuteAsync(null);

        Assert.Multiple(() =>
        {
            Assert.That(vm.CanDeleteSelected, Is.False);
            Assert.That(vm.CanClearHistory, Is.False);
        });

        gate.SetResult();
        await loading;
    }

    // ---- 替身 --------------------------------------------------------------

    private sealed class FakeHistoryService : IInstallHistoryService, IInstallHistoryMaintenance
    {
        public List<InstallHistoryEntry> Entries { get; set; } = new();
        public OperationResult<int>? DeleteResult { get; set; }
        public OperationResult<int>? ClearResult { get; set; }
        public int DeleteCalls { get; private set; }
        public int ClearCalls { get; private set; }
        public DateTimeOffset? LastDeleted { get; private set; }
        public TaskCompletionSource? LoadGate { get; set; }

        public bool HasActiveInstance => true;

        public string? InstanceName => "测试实例";

        public async Task<OperationResult<IReadOnlyList<InstallHistoryEntry>>> LoadAsync(
            int? limit = null,
            CancellationToken ct = default)
        {
            if (LoadGate is { } gate)
            {
                LoadGate = null;
                await gate.Task;
            }

            return OperationResult<IReadOnlyList<InstallHistoryEntry>>.Ok(Entries.ToList());
        }

        public Task<OperationResult<InstallHistoryRestorePlan>> PreviewRestoreAsync(
            InstallHistoryRestoreRequest request,
            CancellationToken ct = default) =>
            Task.FromResult(OperationResult<InstallHistoryRestorePlan>.Fail("测试替身不支持重装预览。"));

        public Task<OperationResult<InstallHistoryRestoreResult>> RestoreAsync(
            InstallHistoryRestoreRequest request,
            IProgress<OperationProgress>? progress = null,
            CancellationToken ct = default) =>
            Task.FromResult(OperationResult<InstallHistoryRestoreResult>.Fail("测试替身不支持重装。"));

        public Task<OperationResult<InstallHistoryEntry>> RecordAsync(
            InstallHistoryRecordRequest request,
            CancellationToken ct = default) =>
            Task.FromResult(OperationResult<InstallHistoryEntry>.Ok(Entry(0)));

        public Task<OperationResult<int>> DeleteAsync(DateTimeOffset timestamp, CancellationToken ct = default)
        {
            DeleteCalls++;
            LastDeleted = timestamp;
            if (DeleteResult is { } canned)
            {
                return Task.FromResult(canned);
            }

            var removed = Entries.RemoveAll(entry => entry.Timestamp == timestamp);
            return Task.FromResult(OperationResult<int>.Ok(removed));
        }

        public Task<OperationResult<int>> ClearAsync(CancellationToken ct = default)
        {
            ClearCalls++;
            if (ClearResult is { } canned)
            {
                return Task.FromResult(canned);
            }

            var removed = Entries.Count;
            Entries.Clear();
            return Task.FromResult(OperationResult<int>.Ok(removed));
        }
    }

    /// <summary>只有读能力的历史服务：用来验证「服务不支持删除时不给出按钮」。</summary>
    private sealed class ReadOnlyHistoryService : IInstallHistoryService
    {
        private readonly FakeHistoryService _inner;

        public ReadOnlyHistoryService(FakeHistoryService inner) => _inner = inner;

        public bool HasActiveInstance => true;

        public string? InstanceName => "测试实例";

        public Task<OperationResult<IReadOnlyList<InstallHistoryEntry>>> LoadAsync(
            int? limit = null,
            CancellationToken ct = default) => _inner.LoadAsync(limit, ct);

        public Task<OperationResult<InstallHistoryRestorePlan>> PreviewRestoreAsync(
            InstallHistoryRestoreRequest request,
            CancellationToken ct = default) => _inner.PreviewRestoreAsync(request, ct);

        public Task<OperationResult<InstallHistoryRestoreResult>> RestoreAsync(
            InstallHistoryRestoreRequest request,
            IProgress<OperationProgress>? progress = null,
            CancellationToken ct = default) => _inner.RestoreAsync(request, progress, ct);

        public Task<OperationResult<InstallHistoryEntry>> RecordAsync(
            InstallHistoryRecordRequest request,
            CancellationToken ct = default) => _inner.RecordAsync(request, ct);
    }

    private sealed class FakeDialogs : IDialogService
    {
        public bool ConfirmResult { get; set; } = true;
        public int ConfirmCalls { get; private set; }
        public string? LastTitle { get; private set; }
        public string? LastMessage { get; private set; }

        public Task<bool> ConfirmAsync(string title, string message, string? detail = null)
        {
            ConfirmCalls++;
            LastTitle = title;
            LastMessage = message;
            return Task.FromResult(ConfirmResult);
        }

        public Task<int> SelectAsync(string title, string message, IReadOnlyList<string> options, int defaultIndex = 0)
            => Task.FromResult(-1);

        public Task ShowMessageAsync(string title, string message) => Task.CompletedTask;

        public Task ShowErrorAsync(string title, string message, string? detail = null) => Task.CompletedTask;

        public Task<OperationResult<bool>> RunWithProgressAsync(
            string title,
            Func<IProgress<double>, CancellationToken, Task> work) => Task.FromResult(OperationResult<bool>.Ok(true));
    }
}
