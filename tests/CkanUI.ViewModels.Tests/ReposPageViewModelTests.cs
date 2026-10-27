using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using CkanUI.Services.Abstractions;
using CkanUI.Services.Abstractions.Dtos;
using CkanUI.ViewModels.Pages;
using CkanUI.ViewModels.Pages.Repos;
using NUnit.Framework;

namespace CkanUI.ViewModels.Tests;

/// <summary>
/// 仓库页视图模型用例（06 篇 UI-06 / VM-06）：投影、选中保持、订阅生命周期、命令与错误文案。
/// 全部走替身接口，不联网、不落盘。
/// </summary>
[TestFixture]
public sealed class ReposPageViewModelTests
{
    private ReposFakeRepositoryService _service = null!;
    private ReposFakeErrorReporter _errors = null!;
    private ReposFakeDialogService _dialogs = null!;

    [SetUp]
    public void SetUp()
    {
        _service = new ReposFakeRepositoryService();
        _errors = new ReposFakeErrorReporter();
        _dialogs = new ReposFakeDialogService();
    }

    private ReposPageViewModel CreateViewModel(params RepositoryEntryInfo[] rows)
    {
        _service.Rows = rows.ToList();
        return new ReposPageViewModel(_service, _errors, _dialogs);
    }

    private static RepositoryEntryInfo Entry(
        string name,
        int priority,
        bool stale = false,
        bool veryStale = false,
        bool failure = false,
        string? failureMessage = null,
        bool isDefault = false)
        => new(Name: name,
               Uri: $"https://example.com/{name}.json",
               Priority: priority,
               LastUpdatedAt: stale || veryStale ? DateTime.UtcNow.AddDays(-5) : null,
               IsStale: stale,
               IsVeryStale: veryStale,
               IsDefault: isDefault,
               HasFailure: failure,
               FailureMessage: failureMessage);

    [Test]
    public void Ctor_ProjectsRowsAndSelectsFirst()
    {
        var vm = CreateViewModel(Entry("B", 1), Entry("A", 0));

        Assert.That(vm.Title, Is.EqualTo("仓库"));
        Assert.That(vm.Rows.Select(row => row.Name), Is.EqualTo(new[] { "A", "B" }));
        Assert.That(vm.SelectedRow?.Name, Is.EqualTo("A"));
        Assert.That(vm.HasRows, Is.True);
        Assert.That(vm.ShowEmptyState, Is.False);
        Assert.That(vm.CountText, Is.EqualTo("共 2 个仓库"));
        Assert.That(vm.StatusText, Is.EqualTo("仓库列表已从本地注册表载入。"));
    }

    [Test]
    public void Ctor_WithoutRows_ShowsEmptyState()
    {
        var vm = CreateViewModel();

        Assert.That(vm.ShowEmptyState, Is.True);
        Assert.That(vm.HasRows, Is.False);
        Assert.That(vm.EmptyStateTitle, Is.EqualTo("还没有配置任何仓库"));
        Assert.That(vm.CanRemoveSelected, Is.False);
        Assert.That(vm.SelectedDetailText, Is.EqualTo("选择一个仓库查看详情。"));
        Assert.That(vm.SelectedUriText, Is.Empty);
    }

    [Test]
    public void Ctor_RowFlagsFollowPosition()
    {
        var vm = CreateViewModel(Entry("A", 0), Entry("B", 1), Entry("C", 2));

        Assert.That(vm.Rows[0].CanMoveUp, Is.False);
        Assert.That(vm.Rows[0].CanMoveDown, Is.True);
        Assert.That(vm.Rows[1].CanMoveUp, Is.True);
        Assert.That(vm.Rows[1].CanMoveDown, Is.True);
        Assert.That(vm.Rows[2].CanMoveUp, Is.True);
        Assert.That(vm.Rows[2].CanMoveDown, Is.False);
        Assert.That(vm.Rows[0].PriorityText, Is.EqualTo("第 1 位"));
        Assert.That(vm.Rows[2].PriorityText, Is.EqualTo("第 3 位"));
    }

    [Test]
    public void Row_StalenessAndFailureDriveStatusText()
    {
        var vm = CreateViewModel(
            Entry("Fresh", 0),
            Entry("Stale", 1, stale: true),
            Entry("VeryStale", 2, veryStale: true),
            Entry("Broken", 3, failure: true, failureMessage: "无法连接服务器。"));

        Assert.That(vm.Rows[0].StatusText, Is.EqualTo("正常"));
        Assert.That(vm.Rows[0].NeedsRefresh, Is.False);
        Assert.That(vm.Rows[1].StatusText, Is.EqualTo("需刷新"));
        Assert.That(vm.Rows[1].NeedsRefresh, Is.True);
        Assert.That(vm.Rows[2].StatusText, Is.EqualTo("已过期"));
        Assert.That(vm.Rows[3].StatusText, Is.EqualTo("刷新失败"));
        Assert.That(vm.Rows[3].NeedsRefresh, Is.False);
        Assert.That(vm.Rows[3].FailureText, Is.EqualTo("无法连接服务器。"));
        Assert.That(vm.Rows[0].FailureText, Is.Empty);
    }

    [Test]
    public void Row_UpdatedTextFallsBackToPlaceholder()
    {
        var vm = CreateViewModel(Entry("Fresh", 0), Entry("Stale", 1, stale: true));

        Assert.That(vm.Rows[0].HasUpdated, Is.False);
        Assert.That(vm.Rows[0].UpdatedText, Is.EqualTo("尚未刷新"));
        Assert.That(vm.Rows[1].HasUpdated, Is.True);
        Assert.That(vm.Rows[1].UpdatedText, Does.StartWith("上次刷新 "));
    }

    [Test]
    public void Row_DefaultRepositoryShowsBadge()
    {
        var vm = CreateViewModel(Entry("Official", 0, isDefault: true), Entry("Other", 1));

        Assert.That(vm.Rows[0].ShowDefaultBadge, Is.True);
        Assert.That(vm.Rows[0].DefaultText, Is.EqualTo("官方默认"));
        Assert.That(vm.Rows[1].ShowDefaultBadge, Is.False);
        Assert.That(vm.Rows[1].DefaultText, Is.Empty);
    }

    [Test]
    public void Activate_SubscribesAndAppliesWholeTableReplacement()
    {
        var vm = CreateViewModel(Entry("A", 0));
        var rowsChanged = 0;
        ((INotifyPropertyChanged)vm).PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(ReposPageViewModel.Rows))
            {
                rowsChanged++;
            }
        };

        vm.Activate();
        Assert.That(_service.RepositoriesChangedSubscribers, Is.EqualTo(1));

        var previousRows = vm.Rows;
        _service.RaiseRepositoriesChanged(new[] { Entry("A", 0), Entry("B", 1) });

        Assert.That(vm.Rows, Is.Not.SameAs(previousRows), "列表必须整体替换，不能就地增量修改。");
        Assert.That(vm.Rows.Count, Is.EqualTo(2));
        Assert.That(rowsChanged, Is.EqualTo(1));
        Assert.That(vm.StatusText, Is.EqualTo("仓库列表已更新。"));
    }

    [Test]
    public void Deactivate_UnsubscribesFromServiceEvents()
    {
        var vm = CreateViewModel(Entry("A", 0));
        vm.Activate();
        vm.Deactivate();

        Assert.That(_service.RepositoriesChangedSubscribers, Is.Zero);
        Assert.That(_service.ProgressSubscribers, Is.Zero);

        _service.RaiseRepositoriesChanged(new[] { Entry("A", 0), Entry("B", 1) });

        Assert.That(vm.Rows.Count, Is.EqualTo(1), "页面未激活时不应再接收服务层事件。");
    }

    [Test]
    public void Activate_IsIdempotent()
    {
        var vm = CreateViewModel(Entry("A", 0));

        vm.Activate();
        vm.Activate();

        Assert.That(_service.RepositoriesChangedSubscribers, Is.EqualTo(1));
        Assert.That(vm.IsActive, Is.True);
    }

    [Test]
    public async Task ReloadCommand_KeepsSelectedRepositoryByName()
    {
        var vm = CreateViewModel(Entry("A", 0), Entry("B", 1));
        vm.SelectedRow = vm.Rows[1];

        _service.Rows = new List<RepositoryEntryInfo> { Entry("B", 0), Entry("A", 1) };
        await vm.ReloadCommand.ExecuteAsync(null);

        Assert.That(_service.LoadCount, Is.EqualTo(1));
        Assert.That(vm.SelectedRow?.Name, Is.EqualTo("B"));
        Assert.That(vm.Rows[0].Name, Is.EqualTo("B"));
        Assert.That(vm.StatusText, Is.EqualTo("仓库列表已重新读取。"));
    }

    [Test]
    public async Task MoveUpCommand_SendsNegativeDelta()
    {
        var vm = CreateViewModel(Entry("A", 0), Entry("B", 1));
        vm.SelectedRow = vm.Rows[1];

        await vm.MoveUpCommand.ExecuteAsync(null);

        Assert.That(_service.LastMovedName, Is.EqualTo("B"));
        Assert.That(_service.LastMoveDelta, Is.EqualTo(-1));
        Assert.That(vm.StatusText, Is.EqualTo("已上移仓库「B」。"));
    }

    [Test]
    public async Task MoveDownCommand_SendsPositiveDelta()
    {
        var vm = CreateViewModel(Entry("A", 0), Entry("B", 1));
        vm.SelectedRow = vm.Rows[0];

        await vm.MoveDownCommand.ExecuteAsync(null);

        Assert.That(_service.LastMoveDelta, Is.EqualTo(1));
        Assert.That(vm.StatusText, Is.EqualTo("已下移仓库「A」。"));
    }

    [Test]
    public void SelectionFlags_TrackPositionAndCount()
    {
        var vm = CreateViewModel(Entry("A", 0), Entry("B", 1));

        Assert.That(vm.CanRemoveSelected, Is.True);
        Assert.That(vm.CanMoveUpSelected, Is.False);
        Assert.That(vm.CanMoveDownSelected, Is.True);

        vm.SelectedRow = vm.Rows[1];

        Assert.That(vm.CanMoveUpSelected, Is.True);
        Assert.That(vm.CanMoveDownSelected, Is.False);
        Assert.That(vm.SelectedDetailText, Does.Contain("B"));
        Assert.That(vm.SelectedUriText, Is.EqualTo("https://example.com/B.json"));

        vm.SelectedRow = null;

        Assert.That(vm.HasSelection, Is.False);
        Assert.That(vm.CanRemoveSelected, Is.False);
        Assert.That(vm.CanRefreshSelected, Is.False);
    }

    [Test]
    public void CanRemoveSelected_IsFalseForSingleRepository()
    {
        var vm = CreateViewModel(Entry("Only", 0));

        Assert.That(vm.HasSelection, Is.True);
        Assert.That(vm.CanRemoveSelected, Is.False);
    }

    [Test]
    public async Task RemoveCommand_WhenNotConfirmed_KeepsListAndReportsCancel()
    {
        var vm = CreateViewModel(Entry("A", 0), Entry("B", 1));
        vm.SelectedRow = vm.Rows[0];
        _dialogs.ConfirmResult = false;

        await vm.RemoveCommand.ExecuteAsync(null);

        Assert.That(_dialogs.ConfirmCount, Is.EqualTo(1));
        Assert.That(_dialogs.LastMessage, Does.Contain("A"));
        Assert.That(_service.LastRemovedName, Is.Null);
        Assert.That(vm.Rows.Count, Is.EqualTo(2));
        Assert.That(vm.StatusText, Is.EqualTo("已取消移除仓库。"));
    }

    [Test]
    public async Task RemoveCommand_WhenConfirmed_CallsService()
    {
        var vm = CreateViewModel(Entry("A", 0), Entry("B", 1));
        vm.SelectedRow = vm.Rows[1];

        await vm.RemoveCommand.ExecuteAsync(null);

        Assert.That(_service.LastRemovedName, Is.EqualTo("B"));
        Assert.That(vm.StatusText, Is.EqualTo("已移除仓库「B」。"));
    }

    [Test]
    public void BeginAdd_AndCancelAdd_ToggleForm()
    {
        var vm = CreateViewModel(Entry("A", 0));
        vm.Form.Name = "残留";

        vm.BeginAddCommand.Execute(null);

        Assert.That(vm.IsFormOpen, Is.True);
        Assert.That(vm.HasForm, Is.True);
        Assert.That(vm.Form.Name, Is.Empty);

        vm.Form.Name = "B";
        vm.CancelAddCommand.Execute(null);

        Assert.That(vm.IsFormOpen, Is.False);
        Assert.That(vm.Form.Name, Is.Empty);
    }

    [Test]
    public async Task AddCommand_WithEmptyForm_ShowsValidationWithoutCallingService()
    {
        var vm = CreateViewModel(Entry("A", 0));
        vm.BeginAddCommand.Execute(null);

        await vm.AddCommand.ExecuteAsync(null);

        Assert.That(_service.LastAdded, Is.Null);
        Assert.That(vm.Form.ValidationText, Is.EqualTo("请填写仓库名称和地址。"));
        Assert.That(vm.HasError, Is.True);
        Assert.That(vm.ErrorText, Is.EqualTo("请填写仓库名称和地址。"));
        Assert.That(vm.IsFormOpen, Is.True);
    }

    [Test]
    public async Task AddCommand_Success_TrimsInputAndClosesForm()
    {
        var vm = CreateViewModel(Entry("A", 0));
        vm.BeginAddCommand.Execute(null);
        vm.Form.Name = "  B  ";
        vm.Form.Uri = "  https://example.com/B.json  ";
        _service.Rows = new List<RepositoryEntryInfo> { Entry("A", 0), Entry("B", 1) };

        await vm.AddCommand.ExecuteAsync(null);

        Assert.That(_service.LastAdded?.Name, Is.EqualTo("B"));
        Assert.That(_service.LastAdded?.Uri, Is.EqualTo("https://example.com/B.json"));
        Assert.That(vm.IsFormOpen, Is.False);
        Assert.That(vm.Rows.Count, Is.EqualTo(2));
        Assert.That(vm.StatusText, Is.EqualTo("已添加仓库「B」。"));
    }

    [Test]
    public async Task AddCommand_Failure_ShowsRetryabilityAndKeepsFormOpen()
    {
        var vm = CreateViewModel(Entry("A", 0));
        vm.BeginAddCommand.Execute(null);
        vm.Form.Name = "A";
        vm.Form.Uri = "https://example.com/A.json";
        _service.EditResult = OperationResult<IReadOnlyList<RepositoryEntryInfo>>.Fail(
            "已存在同名仓库「A」，请换一个名称。", null, retryable: false);

        await vm.AddCommand.ExecuteAsync(null);

        Assert.That(vm.Form.ValidationText, Is.EqualTo("已存在同名仓库「A」，请换一个名称。"));
        Assert.That(vm.ErrorText, Is.EqualTo("已存在同名仓库「A」，请换一个名称。（不可重试）"));
        Assert.That(vm.IsFormOpen, Is.True);
    }

    [Test]
    public async Task RefreshAllCommand_WhenServiceFails_ShowsRetryableError()
    {
        var vm = CreateViewModel(Entry("A", 0));
        _service.RefreshAllResult = OperationResult<RepositorySnapshot>.Fail(
            "仓库索引刷新失败。", "连接超时", retryable: true);

        await vm.RefreshAllCommand.ExecuteAsync(null);

        Assert.That(_service.RefreshAllCount, Is.EqualTo(1));
        Assert.That(vm.ErrorText, Is.EqualTo("仓库索引刷新失败。（可重试）"));
        Assert.That(vm.ErrorDetail, Is.EqualTo("连接超时"));
        Assert.That(vm.HasError, Is.True);
        Assert.That(vm.HasErrorDetail, Is.True);
        Assert.That(vm.IsBusy, Is.False);
    }

    [Test]
    public async Task RefreshAllCommand_WhenSuccessful_ReloadsRows()
    {
        var vm = CreateViewModel(Entry("A", 0));
        _service.Rows = new List<RepositoryEntryInfo> { Entry("A", 0, stale: true) };

        await vm.RefreshAllCommand.ExecuteAsync(null);

        Assert.That(_service.LoadCount, Is.EqualTo(1));
        Assert.That(vm.Rows[0].StatusText, Is.EqualTo("需刷新"));
        Assert.That(vm.StatusText, Is.EqualTo("仓库索引已刷新。"));
    }

    [Test]
    public async Task RefreshSelectedCommand_RefreshesSelectedRepository()
    {
        var vm = CreateViewModel(Entry("A", 0), Entry("B", 1));
        vm.SelectedRow = vm.Rows[1];

        await vm.RefreshSelectedCommand.ExecuteAsync(null);

        Assert.That(_service.LastRefreshedName, Is.EqualTo("B"));
        Assert.That(vm.StatusText, Is.EqualTo("仓库「B」已刷新。"));
    }

    [Test]
    public async Task RestoreDefaultsCommand_RequiresConfirmation()
    {
        var vm = CreateViewModel(Entry("A", 0));
        _dialogs.ConfirmResult = false;

        await vm.RestoreDefaultsCommand.ExecuteAsync(null);

        Assert.That(_service.RestoreCount, Is.Zero);
        Assert.That(vm.StatusText, Is.EqualTo("已取消恢复默认仓库。"));

        _dialogs.ConfirmResult = true;
        await vm.RestoreDefaultsCommand.ExecuteAsync(null);

        Assert.That(_service.RestoreCount, Is.EqualTo(1));
        Assert.That(vm.StatusText, Is.EqualTo("已恢复为官方默认仓库列表。"));
    }

    [Test]
    public void RefreshProgress_TogglesBusyState()
    {
        var vm = CreateViewModel(Entry("A", 0));
        vm.Activate();

        _service.RaiseProgress(new OperationProgress("repository-refresh", "repository", "刷新仓库索引",
            "正在刷新仓库「A」…", 0, false, OperationState.Running));

        Assert.That(vm.IsBusy, Is.True);
        Assert.That(vm.BusyText, Is.EqualTo("正在刷新仓库「A」…"));
        Assert.That(vm.CanEdit, Is.False);
        Assert.That(vm.IsIdle, Is.False);

        _service.RaiseProgress(new OperationProgress("repository-refresh", "repository", "刷新仓库索引",
            "仓库索引已更新。", 100, false, OperationState.Succeeded));

        Assert.That(vm.IsBusy, Is.False);
        Assert.That(vm.CanEdit, Is.True);
        Assert.That(vm.BusyText, Is.EqualTo("仓库索引已更新。"));
    }

    [Test]
    public void RefreshProgress_FailedState_SetsPageError()
    {
        var vm = CreateViewModel(Entry("A", 0));
        vm.Activate();

        _service.RaiseProgress(new OperationProgress("repository-refresh", "repository", "刷新仓库索引",
            "仓库「A」刷新失败。", 0, false, OperationState.Failed));

        Assert.That(vm.HasError, Is.True);
        Assert.That(vm.ErrorText, Is.EqualTo("仓库「A」刷新失败。"));
        Assert.That(vm.IsBusy, Is.False);
    }

    [Test]
    public async Task Command_WhenServiceThrows_ReportsAndClearsBusy()
    {
        var vm = CreateViewModel(Entry("A", 0));
        vm.BeginAddCommand.Execute(null);
        vm.Form.Name = "B";
        vm.Form.Uri = "https://example.com/B.json";
        _service.EditException = new InvalidOperationException("注册表写入失败");

        await vm.AddCommand.ExecuteAsync(null);

        Assert.That(_errors.Entries.Count, Is.EqualTo(1));
        Assert.That(_errors.Entries[0].Context, Is.EqualTo("新增仓库"));
        Assert.That(vm.ErrorText, Is.EqualTo("仓库列表操作失败。：注册表写入失败（不可重试）"));
        Assert.That(vm.ErrorDetail, Is.EqualTo("注册表写入失败"));
        Assert.That(vm.IsBusy, Is.False);
    }

    [Test]
    public async Task ClearErrorCommand_RemovesPageError()
    {
        var vm = CreateViewModel(Entry("A", 0));
        _service.EditResult = OperationResult<IReadOnlyList<RepositoryEntryInfo>>.Fail("出错了。", null, false);
        await vm.MoveUpCommand.ExecuteAsync(null);
        Assert.That(vm.HasError, Is.True);

        vm.ClearErrorCommand.Execute(null);

        Assert.That(vm.HasError, Is.False);
        Assert.That(vm.HasErrorDetail, Is.False);
    }

    [Test]
    public void Form_ReportsEmptyAndValidationState()
    {
        var form = new RepositoryFormViewModel();

        Assert.That(form.IsEmpty, Is.True);
        Assert.That(form.HasValidation, Is.False);

        form.Name = "A";
        Assert.That(form.IsEmpty, Is.False);

        form.Name = string.Empty;
        form.Uri = "https://example.com/A.json";
        Assert.That(form.IsEmpty, Is.False);

        form.Uri = string.Empty;
        Assert.That(form.IsEmpty, Is.True);

        form.ValidationText = "仓库地址必须是 http 或 https 开头的完整链接。";
        Assert.That(form.HasValidation, Is.True);

        form.Reset();

        Assert.That(form.Name, Is.Empty);
        Assert.That(form.Uri, Is.Empty);
        Assert.That(form.ValidationText, Is.Null);
        Assert.That(form.IsEmpty, Is.True);
    }
}
