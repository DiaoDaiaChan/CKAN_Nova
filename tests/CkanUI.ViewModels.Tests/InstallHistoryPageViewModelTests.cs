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
/// 安装历史页视图模型用例（12 篇 F-44 / 13 篇 UI-23 / 04 篇 §InstallHistoryViewModel）：
/// 时间戳列表、已装/未装分组、重装按钮启用条件、重装前展示被变更数量、失败文案可重试。
/// 全部走替身，不联网、不落盘。
/// </summary>
[TestFixture]
public sealed class InstallHistoryPageViewModelTests
{
    private static readonly DateTimeOffset Base = new(2024, 5, 1, 10, 0, 0, TimeSpan.Zero);

    private FakeInstallHistoryService _history = null!;
    private ModPacksFakeErrorReporter _errors = null!;

    [SetUp]
    public void SetUp()
    {
        _history = new FakeInstallHistoryService();
        _errors = new ModPacksFakeErrorReporter();
    }

    private InstallHistoryPageViewModel CreateViewModel(params InstallHistoryEntry[] entries)
    {
        _history.Entries = entries;
        return new InstallHistoryPageViewModel(_history, _errors);
    }

    // ---- 构造数据 ----------------------------------------------------------

    private static InstallHistoryEntry Entry(
        int seconds,
        InstallHistoryOperation operation = InstallHistoryOperation.Install,
        InstallHistoryOutcome outcome = InstallHistoryOutcome.Succeeded,
        params InstallHistoryModule[] modules)
    {
        var stamp = Base.AddSeconds(seconds);
        var snapshot = modules.Length == 0
            ? null
            : new InstallHistorySnapshot(stamp, "测试实例", modules);
        return new InstallHistoryEntry(
            stamp,
            "测试实例",
            operation,
            outcome,
            new[] { new InstallHistoryChange("A", ChangeAction.Install, null, "1.0") })
        {
            Snapshot = snapshot,
        };
    }

    private static InstallHistoryModule Installed(string identifier, string version) =>
        new(identifier, version, IsInstalled: true, InstalledVersion: version);

    private static InstallHistoryModule Missing(string identifier, string version) =>
        new(identifier, version);

    private static InstallHistoryModule OtherVersion(string identifier, string version, string current) =>
        new(identifier, version, IsInstalled: true, InstalledVersion: current);

    private static InstallHistoryRestorePlan Plan(params ChangeItemDto[] items) =>
        new(Base, items, Array.Empty<string>(), 0);

    private static ChangeItemDto Item(string identifier, ChangeAction action, string? from, string? to) =>
        new(identifier, identifier, action, from, to, ChangeOrigin.UserRequested, 0);

    // ---- 列表与空状态 ------------------------------------------------------

    [Test]
    public async Task Load_PopulatesTimestampList_AndSelectsNewest()
    {
        var vm = CreateViewModel(
            Entry(10, InstallHistoryOperation.Upgrade, InstallHistoryOutcome.Succeeded, Installed("A", "2.0")),
            Entry(20, InstallHistoryOperation.Remove, InstallHistoryOutcome.Succeeded, Installed("B", "3.0")));

        await vm.LoadCommand.ExecuteAsync(null);

        Assert.That(vm.HasEntries, Is.True);
        Assert.That(vm.ShowEmptyState, Is.False);
        Assert.That(vm.CountText, Is.EqualTo("共 2 条历史记录"));
        Assert.That(vm.Entries.Count, Is.EqualTo(2));
        Assert.That(vm.SelectedEntry?.Timestamp, Is.EqualTo(Base.AddSeconds(10)), "默认选中最新一条");
        Assert.That(vm.Entries[0].TimestampText, Is.Not.Empty);
        Assert.That(vm.Entries[0].SummaryText, Does.Contain("升级"));
        Assert.That(vm.InstalledGroupTitle, Is.EqualTo("已安装（1）"));
    }

    [Test]
    public async Task Load_EmptyHistory_ShowsEmptyStateAndNoSelection()
    {
        var vm = CreateViewModel();

        await vm.LoadCommand.ExecuteAsync(null);

        Assert.That(vm.HasEntries, Is.False);
        Assert.That(vm.ShowEmptyState, Is.True, "空历史要给出空状态而不是空白页");
        Assert.That(vm.CountText, Is.EqualTo("暂无历史记录"));
        Assert.That(vm.SelectedEntry, Is.Null);
        Assert.That(vm.CanRestore, Is.False);
        Assert.That(vm.RestoreHintText, Is.EqualTo("先在左侧选中一条历史记录。"));
        Assert.That(vm.StatusText, Does.Contain("还没有历史记录"));
    }

    [Test]
    public async Task Load_Failure_SetsRetryableChineseErrorText()
    {
        _history.LoadResult = OperationResult<IReadOnlyList<InstallHistoryEntry>>.Fail(
            "读取安装历史失败，请检查实例目录的写权限后重试。",
            "UnauthorizedAccessException",
            retryable: true);
        var vm = CreateViewModel();

        await vm.LoadCommand.ExecuteAsync(null);

        Assert.That(vm.HasError, Is.True);
        Assert.That(vm.ErrorText, Does.Contain("读取安装历史失败"));
        Assert.That(vm.ErrorText, Does.Contain("可重试"));
        Assert.That(vm.ErrorText, Does.Not.Contain("不可重试"));
        Assert.That(vm.ErrorDetail, Is.EqualTo("UnauthorizedAccessException"));
        Assert.That(vm.HasEntries, Is.False);
        Assert.That(vm.IsIdle, Is.True, "失败后必须回到空闲态");
    }

    // ---- 分组 --------------------------------------------------------------

    [Test]
    public void ApplyEntries_SplitsInstalledAndMissingGroups()
    {
        var vm = CreateViewModel();
        var entry = Entry(
            5,
            InstallHistoryOperation.Install,
            InstallHistoryOutcome.Succeeded,
            Installed("A", "1.0"),
            OtherVersion("B", "1.0", "2.0"),
            Missing("C", "1.0"));

        vm.ApplyEntries(new[] { entry });

        // 分组口径与旧 GUI 一致：按"当前是否装着"分，版本不同的仍算已装，但状态文案点出来。
        Assert.That(vm.InstalledModules.Count, Is.EqualTo(2));
        Assert.That(vm.MissingModules.Count, Is.EqualTo(1));
        Assert.That(vm.MissingModules.Select(row => row.Identifier), Is.EquivalentTo(new[] { "C" }));
        Assert.That(vm.InstalledGroupTitle, Is.EqualTo("已安装（2）"));
        Assert.That(vm.MissingGroupTitle, Is.EqualTo("未安装（1）"));
        Assert.That(vm.HasInstalledModules, Is.True);
        Assert.That(vm.HasMissingModules, Is.True);
        Assert.That(vm.InstalledModules.Single(row => row.Identifier == "B").StateText, Does.Contain("版本不同"));
        Assert.That(vm.InstalledModules.Single(row => row.Identifier == "B").IsChanged, Is.True);
        Assert.That(vm.SelectionSummaryText, Does.Contain("快照共 3 个模组"));
    }

    [Test]
    public void ApplyEntries_KeepsPreviouslySelectedTimestamp()
    {
        var vm = CreateViewModel();
        var older = Entry(1, modules: new[] { Installed("A", "1.0") });
        var newer = Entry(2, modules: new[] { Installed("B", "1.0") });
        vm.ApplyEntries(new[] { older, newer }, preferredTimestamp: older.Timestamp);

        vm.ApplyEntries(new[] { older, newer });

        Assert.That(vm.SelectedEntry?.Timestamp, Is.EqualTo(older.Timestamp), "重新读取后保留原选中项");
    }

    // ---- 重装按钮启用条件 --------------------------------------------------

    [Test]
    public void CanRestore_RequiresSnapshotWithModules()
    {
        var vm = CreateViewModel();
        vm.ApplyEntries(new[] { Entry(1) });

        Assert.That(vm.CanRestore, Is.False, "没有快照不能重装");
        Assert.That(vm.RestoreHintText, Is.EqualTo("这条历史没有已装集合快照，无法重装。"));
        Assert.That(vm.RestoreCommand.CanExecute(null), Is.False);

        var empty = new InstallHistoryEntry(
            Base.AddSeconds(2),
            "测试实例",
            InstallHistoryOperation.Install,
            InstallHistoryOutcome.Succeeded,
            Array.Empty<InstallHistoryChange>())
        {
            Snapshot = new InstallHistorySnapshot(Base.AddSeconds(2), "测试实例", Array.Empty<InstallHistoryModule>()),
        };
        vm.ApplyEntries(new[] { empty });

        Assert.That(vm.CanRestore, Is.False, "空快照无需重装");
        Assert.That(vm.RestoreHintText, Does.Contain("没有任何模组"));
        Assert.That(vm.HasRestorePreview, Is.False);
    }

    [Test]
    public void CanRestore_TrueForSnapshotEntry_AndHintExplainsScope()
    {
        var vm = CreateViewModel();
        vm.ApplyEntries(new[] { Entry(1, modules: new[] { Missing("A", "1.0") }) });

        Assert.That(vm.CanRestore, Is.True);
        Assert.That(vm.RestoreCommand.CanExecute(null), Is.True);
        Assert.That(vm.RestoreHintText, Does.Contain("不会卸载快照之外的模组"));
    }

    [Test]
    public void BuildRestorePreview_CountsNewInstallsAndVersionChanges()
    {
        var vm = CreateViewModel();
        vm.ApplyEntries(new[]
        {
            Entry(1, modules: new[] { Installed("A", "1.0"), OtherVersion("B", "1.0", "2.0"), Missing("C", "1.0") }),
        });

        Assert.That(vm.RestorePreviewText, Is.EqualTo("该时间点共 3 个模组｜已装 2 个｜需变更 2 个（新装 1，换版本 1）。"));
        Assert.That(vm.HasRestorePreview, Is.True);
    }

    // ---- 「同时卸载快照之外的模组」开关 ------------------------------------

    /// <summary>开关默认关闭，且默认值要原样带进预览与执行两份请求（不能被误置成 true）。</summary>
    [Test]
    public async Task RemoveExtras_DefaultsToFalseAndIsCarriedAsFalse()
    {
        var vm = CreateViewModel(Entry(1, modules: new[] { Missing("A", "1.0") }));
        await vm.LoadCommand.ExecuteAsync(null);
        _history.PreviewResult = OperationResult<InstallHistoryRestorePlan>.Ok(Plan(Item("A", ChangeAction.Install, null, "1.0")));

        Assert.That(vm.RemoveExtras, Is.False, "默认不动快照之外的模组");

        await vm.RestoreCommand.ExecuteAsync(null);

        Assert.That(_history.LastPreviewRequest, Is.Not.Null);
        Assert.That(_history.LastPreviewRequest?.RemoveExtras, Is.False);
        Assert.That(_history.LastRequest?.RemoveExtras, Is.False);
    }

    /// <summary>勾选后预览与执行都带 RemoveExtras=true，文案也要提示会真的删文件。</summary>
    [Test]
    public async Task RemoveExtras_CheckedIsCarriedIntoPreviewAndRestore()
    {
        var vm = CreateViewModel(Entry(1, modules: new[] { Missing("A", "1.0") }));
        await vm.LoadCommand.ExecuteAsync(null);
        _history.PreviewResult = OperationResult<InstallHistoryRestorePlan>.Ok(Plan(Item("A", ChangeAction.Install, null, "1.0")));

        vm.RemoveExtras = true;

        Assert.That(vm.RestoreHintText, Does.Contain("并卸载快照之外的模组"));
        Assert.That(vm.RestorePreviewText, Does.Contain("同时卸载快照之外的模组"));

        await vm.RestoreCommand.ExecuteAsync(null);

        Assert.That(_history.LastPreviewRequest?.RemoveExtras, Is.True);
        Assert.That(_history.LastRequest?.RemoveExtras, Is.True);
    }

    // ---- 重装流程 ----------------------------------------------------------

    [Test]
    public async Task Restore_ShowsChangeCountBeforeApplying_ThenResult()
    {
        var vm = CreateViewModel(Entry(1, modules: new[] { Missing("A", "1.0"), Missing("B", "2.0") }));
        await vm.LoadCommand.ExecuteAsync(null);
        _history.PreviewResult = OperationResult<InstallHistoryRestorePlan>.Ok(Plan(
            Item("A", ChangeAction.Install, null, "1.0"),
            Item("B", ChangeAction.Install, null, "2.0")));
        _history.RestoreResult = OperationResult<InstallHistoryRestoreResult>.Ok(
            new InstallHistoryRestoreResult(
                Plan(Item("A", ChangeAction.Install, null, "1.0"), Item("B", ChangeAction.Install, null, "2.0")),
                new[] { "A", "B" },
                Array.Empty<string>(),
                Array.Empty<string>(),
                Array.Empty<string>()));

        await vm.RestoreCommand.ExecuteAsync(null);

        Assert.That(vm.RestorePreviewText, Does.Contain("重装将变更 2 个模组"), "重装前后都能看到将被变更的数量");
        Assert.That(vm.RestorePreviewText, Does.Contain("新装 2"));
        Assert.That(vm.HasRestoreResult, Is.True);
        Assert.That(vm.RestoreResultText, Is.EqualTo("重装结果：安装 2 个，卸载 0 个，失败 0 个。"));
        Assert.That(vm.StatusText, Does.Contain("重装完成"));
        Assert.That(vm.HasError, Is.False);
        Assert.That(_history.RestoreCalls, Is.EqualTo(1));
        Assert.That(_history.LastRequest?.Timestamp, Is.EqualTo(Base.AddSeconds(1)));
        Assert.That(vm.IsIdle, Is.True);
    }

    [Test]
    public async Task Restore_Conflicts_AbortsWithoutCallingRestore()
    {
        var vm = CreateViewModel(Entry(1, modules: new[] { Missing("A", "1.0") }));
        await vm.LoadCommand.ExecuteAsync(null);
        _history.PreviewResult = OperationResult<InstallHistoryRestorePlan>.Ok(
            new InstallHistoryRestorePlan(
                Base.AddSeconds(1),
                new[] { Item("A", ChangeAction.Install, null, "1.0") },
                new[] { "A 与 B 冲突" },
                0));

        await vm.RestoreCommand.ExecuteAsync(null);

        Assert.That(vm.HasError, Is.True);
        Assert.That(vm.ErrorText, Does.Contain("依赖冲突"));
        Assert.That(vm.ErrorText, Does.Contain("A 与 B 冲突"));
        Assert.That(_history.RestoreCalls, Is.Zero, "冲突时不得调用安装");
        Assert.That(vm.HasRestoreResult, Is.False);
    }

    [Test]
    public async Task Restore_EmptyPlan_SaysNoNeedAndSkipsInstall()
    {
        var vm = CreateViewModel(Entry(1, modules: new[] { Installed("A", "1.0") }));
        await vm.LoadCommand.ExecuteAsync(null);
        _history.PreviewResult = OperationResult<InstallHistoryRestorePlan>.Ok(Plan());

        await vm.RestoreCommand.ExecuteAsync(null);

        Assert.That(vm.RestorePreviewText, Is.EqualTo("该时间点与当前已装集合一致，无需重装。"));
        Assert.That(vm.StatusText, Does.Contain("无需重装"));
        Assert.That(_history.RestoreCalls, Is.Zero);
        Assert.That(vm.HasError, Is.False);
    }

    [Test]
    public async Task Restore_Failure_GivesRetryableChineseText()
    {
        var vm = CreateViewModel(Entry(1, modules: new[] { Missing("A", "1.0") }));
        await vm.LoadCommand.ExecuteAsync(null);
        _history.PreviewResult = OperationResult<InstallHistoryRestorePlan>.Ok(Plan(Item("A", ChangeAction.Install, null, "1.0")));
        _history.RestoreResult = OperationResult<InstallHistoryRestoreResult>.Fail(
            "重装过程出现异常，请重试。",
            "连接被重置",
            retryable: true);

        await vm.RestoreCommand.ExecuteAsync(null);

        Assert.That(vm.HasError, Is.True);
        Assert.That(vm.ErrorText, Does.Contain("重装过程出现异常"));
        Assert.That(vm.ErrorText, Does.Contain("可重试"));
        Assert.That(vm.ErrorDetail, Is.EqualTo("连接被重置"));
        Assert.That(vm.HasRestoreResult, Is.False);
        Assert.That(vm.IsIdle, Is.True);
        Assert.That(_history.RestoreCalls, Is.EqualTo(1));
    }

    [Test]
    public async Task Restore_ThrownException_IsReportedNotRethrown()
    {
        var vm = CreateViewModel(Entry(1, modules: new[] { Missing("A", "1.0") }));
        await vm.LoadCommand.ExecuteAsync(null);
        _history.ThrowOnPreview = new InvalidOperationException("注册表未打开");

        Assert.DoesNotThrowAsync(async () => await vm.RestoreCommand.ExecuteAsync(null));

        Assert.That(vm.HasError, Is.True);
        Assert.That(vm.ErrorText, Does.Contain("注册表未打开"));
        Assert.That(vm.ErrorText, Does.Contain("不可重试"));
        Assert.That(_errors.Entries.Count, Is.GreaterThan(0), "异常必须上报给错误条");
        Assert.That(vm.IsIdle, Is.True);
    }

    [Test]
    public void InstanceTitleText_WithoutInstance_ExplainsUnavailable()
    {
        _history.HasActiveInstance = false;
        var vm = new InstallHistoryPageViewModel(_history, _errors);

        Assert.That(vm.InstanceTitleText, Is.EqualTo("当前没有选中的游戏实例，安装历史不可用。"));

        _history.HasActiveInstance = true;
        _history.InstanceName = "测试实例";
        Assert.That(vm.InstanceTitleText, Does.Contain("当前实例：测试实例"));
        Assert.That(vm.InstanceTitleText, Does.Contain("install_history.jsonl"));
    }

    [Test]
    public void ParameterlessConstructor_IsUsableWithoutServices()
    {
        var vm = new InstallHistoryPageViewModel();

        Assert.That(vm.Title, Is.EqualTo("安装历史"));
        Assert.That(vm.HasEntries, Is.False);
        Assert.That(vm.CanRestore, Is.False);
    }

    // ---- 替身 --------------------------------------------------------------

    private sealed class FakeInstallHistoryService : IInstallHistoryService
    {
        public IReadOnlyList<InstallHistoryEntry> Entries { get; set; } = Array.Empty<InstallHistoryEntry>();
        public OperationResult<IReadOnlyList<InstallHistoryEntry>>? LoadResult { get; set; }
        public OperationResult<InstallHistoryRestorePlan>? PreviewResult { get; set; }
        public OperationResult<InstallHistoryRestoreResult>? RestoreResult { get; set; }
        public Exception? ThrowOnPreview { get; set; }
        public int RestoreCalls { get; private set; }
        public InstallHistoryRestoreRequest? LastRequest { get; private set; }
        public InstallHistoryRestoreRequest? LastPreviewRequest { get; private set; }

        public bool HasActiveInstance { get; set; } = true;
        public string? InstanceName { get; set; } = "测试实例";

        public Task<OperationResult<IReadOnlyList<InstallHistoryEntry>>> LoadAsync(
            int? limit = null,
            CancellationToken ct = default) =>
            Task.FromResult(LoadResult ?? OperationResult<IReadOnlyList<InstallHistoryEntry>>.Ok(Entries));

        public Task<OperationResult<InstallHistoryRestorePlan>> PreviewRestoreAsync(
            InstallHistoryRestoreRequest request,
            CancellationToken ct = default)
        {
            LastPreviewRequest = request;
            if (ThrowOnPreview is { } exception)
            {
                throw exception;
            }

            return Task.FromResult(PreviewResult ?? OperationResult<InstallHistoryRestorePlan>.Ok(Plan()));
        }

        public Task<OperationResult<InstallHistoryRestoreResult>> RestoreAsync(
            InstallHistoryRestoreRequest request,
            IProgress<OperationProgress>? progress = null,
            CancellationToken ct = default)
        {
            RestoreCalls++;
            LastRequest = request;
            return Task.FromResult(RestoreResult ?? OperationResult<InstallHistoryRestoreResult>.Ok(
                new InstallHistoryRestoreResult(
                    Plan(),
                    Array.Empty<string>(),
                    Array.Empty<string>(),
                    Array.Empty<string>(),
                    Array.Empty<string>())));
        }

        public Task<OperationResult<InstallHistoryEntry>> RecordAsync(
            InstallHistoryRecordRequest request,
            CancellationToken ct = default) =>
            Task.FromResult(OperationResult<InstallHistoryEntry>.Ok(Entry(0)));
    }
}
