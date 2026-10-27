using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using CkanUI.Services.Abstractions;
using CkanUI.Services.Abstractions.Dtos;
using CkanUI.ViewModels.Pages;
using CkanUI.ViewModels.Pages.ModPacks;
using NUnit.Framework;

namespace CkanUI.ViewModels.Tests;

/// <summary>
/// 导入导出页视图模型用例（03 篇 §6 V-11 / 06 篇 UI-20 / UI-24）：导出表单与结果、导入预览与勾选、
/// 导入执行、实例对比、事件订阅生命周期，以及异常一律转文案不抛给界面。全部走替身，不联网、不落盘。
/// </summary>
[TestFixture]
public sealed class ModPacksPageViewModelTests
{
    private ModPacksFakeModPackService _modPacks = null!;
    private ModPacksFakeInstanceService _instances = null!;
    private ModPacksFakeErrorReporter _errors = null!;

    [SetUp]
    public void SetUp()
    {
        _modPacks = new ModPacksFakeModPackService();
        _instances = new ModPacksFakeInstanceService();
        _errors = new ModPacksFakeErrorReporter();
    }

    private ModPacksPageViewModel CreateViewModel(params string[] instanceNames)
    {
        _instances.Seed(instanceNames);
        return new ModPacksPageViewModel(_modPacks, _instances, _errors);
    }

    private static ModPackEntry Entry(
        string identifier,
        ModPackEntryState state,
        string? requested = null,
        string? installed = null,
        string? target = null,
        string? reason = null,
        string source = "列表.ckan")
        => new(identifier, source, requested, installed, target, state, reason);

    private static ModPackPreview Preview(string name, params ModPackEntry[] entries)
        => new(new[] { "D:/in/" + name }, name, entries, Array.Empty<string>(), Array.Empty<string>());

    // ---- 构造与文案 --------------------------------------------------------

    [Test]
    public void Ctor_SetsTitleAndInstanceText()
    {
        var vm = CreateViewModel("主实例", "副本实例");

        Assert.That(vm.Title, Is.EqualTo("导入导出"));
        Assert.That(vm.InstanceTitleText, Is.EqualTo("当前实例：测试实例"));
        Assert.That(vm.StatusText, Is.Not.Empty);
        Assert.That(vm.ExportForm.WithVersions, Is.True);
        Assert.That(vm.ExportForm.BundleDownloads, Is.False);
        Assert.That(vm.CanExport, Is.True);
    }

    [Test]
    public void Ctor_WithoutInstance_DisablesExport()
    {
        _modPacks.HasActiveInstance = false;
        _modPacks.InstanceName = null;
        var vm = CreateViewModel();

        Assert.That(vm.CanExport, Is.False);
        Assert.That(vm.InstanceTitleText, Does.Contain("没有选中的游戏实例"));
        Assert.That(vm.ShowPreviewEmptyState, Is.True);
        Assert.That(vm.ShowDiffEmptyState, Is.True);
    }

    [Test]
    public void Ctor_PicksLeftAndRightInstancesFromService()
    {
        var vm = CreateViewModel("A", "B", "C");

        Assert.That(vm.InstanceNames, Is.EqualTo(new[] { "A", "B", "C" }));
        Assert.That(vm.LeftInstance, Is.EqualTo("A"));
        Assert.That(vm.RightInstance, Is.EqualTo("B"));
        Assert.That(vm.CanCompare, Is.True);
    }

    // ---- 导出 --------------------------------------------------------------

    [Test]
    public async Task Export_WithoutInstance_ShowsErrorAndSkipsService()
    {
        _modPacks.HasActiveInstance = false;
        var vm = CreateViewModel();
        vm.ExportForm.TargetPath = "D:/out/mods.ckan";

        await vm.ExportCommand.ExecuteAsync(null);

        Assert.That(_modPacks.ExportCalls, Is.Zero);
        Assert.That(vm.ErrorText, Does.Contain("没有选中的游戏实例"));
        Assert.That(vm.HasError, Is.True);
    }

    [Test]
    public async Task Export_EmptyPath_ShowsValidationText()
    {
        var vm = CreateViewModel("主实例");

        await vm.ExportCommand.ExecuteAsync(null);

        Assert.That(_modPacks.ExportCalls, Is.Zero);
        Assert.That(vm.ExportForm.HasValidation, Is.True);
        Assert.That(vm.ExportForm.ValidationText, Does.Contain("目标路径"));
        Assert.That(vm.ErrorText, Does.Contain("目标路径"));
    }

    [Test]
    public async Task Export_Success_PassesRequestAndReportsResult()
    {
        _modPacks.ExportResult = OperationResult<ModPackExportResult>.Ok(
            ModPacksFakeModPackService.Export("D:/out/mods.ckan", 3, "D:/out/mods.zip", 2));
        var vm = CreateViewModel("主实例");
        vm.ExportForm.TargetPath = "  D:/out/mods.ckan  ";
        vm.ExportForm.BundleDownloads = true;
        vm.ExportForm.UseRecommends = true;
        vm.ExportForm.WithVersions = false;

        await vm.ExportCommand.ExecuteAsync(null);

        Assert.That(_modPacks.LastRequest, Is.Not.Null);
        Assert.That(_modPacks.LastRequest!.TargetPath, Is.EqualTo("D:/out/mods.ckan"));
        Assert.That(_modPacks.LastRequest.BundleDownloads, Is.True);
        Assert.That(_modPacks.LastRequest.UseRecommends, Is.True);
        Assert.That(_modPacks.LastRequest.WithVersions, Is.False);
        Assert.That(vm.HasExportResult, Is.True);
        Assert.That(vm.ExportResultText, Does.Contain("D:/out/mods.ckan"));
        Assert.That(vm.ExportResultText, Does.Contain("包含 3 个模组"));
        Assert.That(vm.ExportResultText, Does.Contain("D:/out/mods.zip"));
        Assert.That(vm.ProgressPercent, Is.EqualTo(100).Within(0.001));
        Assert.That(vm.HasProgress, Is.True);
        Assert.That(vm.StatusText, Does.Contain("已导出 3 个模组"));
        Assert.That(vm.HasError, Is.False);
        Assert.That(vm.IsBusy, Is.False);
    }

    [Test]
    public async Task Export_RetryableFailure_ShowsRetryableText()
    {
        _modPacks.ExportResult = OperationResult<ModPackExportResult>.Fail(
            "写入导出文件失败：mods.ckan 可能正被占用。", "System.IO.IOException", retryable: true);
        var vm = CreateViewModel("主实例");
        vm.ExportForm.TargetPath = "D:/out/mods.ckan";

        await vm.ExportCommand.ExecuteAsync(null);

        Assert.That(vm.ErrorText, Does.Contain("可能正被占用"));
        Assert.That(vm.ErrorText, Does.EndWith("（可重试）"));
        Assert.That(vm.ErrorDetail, Is.EqualTo("System.IO.IOException"));
        Assert.That(vm.StatusText, Is.EqualTo(vm.ErrorText));
        Assert.That(vm.HasExportResult, Is.False);
    }

    [Test]
    public async Task Export_Exception_IsReportedAndNeverThrown()
    {
        _modPacks.ExportThrows = new InvalidOperationException("注册表损坏");
        var vm = CreateViewModel("主实例");
        vm.ExportForm.TargetPath = "D:/out/mods.ckan";

        Assert.DoesNotThrowAsync(async () => await vm.ExportCommand.ExecuteAsync(null));

        Assert.That(_errors.Entries, Has.Count.EqualTo(1));
        Assert.That(_errors.Entries[0].Context, Is.EqualTo("导出模组列表"));
        Assert.That(vm.ErrorText, Does.StartWith("导入导出操作失败。"));
        Assert.That(vm.ErrorText, Does.EndWith("（不可重试）"));
        Assert.That(vm.IsBusy, Is.False);
    }

    // ---- 导入预览 ----------------------------------------------------------

    [Test]
    public async Task PreviewImport_EmptyPath_ShowsError()
    {
        var vm = CreateViewModel("主实例");

        await vm.PreviewImportCommand.ExecuteAsync(null);

        Assert.That(_modPacks.PreviewCalls, Is.Zero);
        Assert.That(vm.ErrorText, Does.Contain("至少一个 .ckan 文件路径"));
    }

    [Test]
    public async Task PreviewImport_SplitsLinesAndSemicolons()
    {
        var vm = CreateViewModel("主实例");
        vm.ImportPathText = "  D:/in/a.ckan \r\n D:/in/b.ckan;D:/in/c.ckan  ";

        await vm.PreviewImportCommand.ExecuteAsync(null);

        Assert.That(_modPacks.LastPaths, Is.EqualTo(new[] { "D:/in/a.ckan", "D:/in/b.ckan", "D:/in/c.ckan" }));
    }

    [Test]
    public async Task PreviewImport_ProjectsRowsAndPreselectsSelectableOnes()
    {
        _modPacks.PreviewResult = OperationResult<ModPackPreview>.Ok(Preview(
            "列表.ckan",
            Entry("Installed", ModPackEntryState.SameVersion, "1.0", "1.0"),
            Entry("Missing", ModPackEntryState.Missing, "2.0"),
            Entry("Installable", ModPackEntryState.Install, "1.1", null, "1.1"),
            Entry("Upgradable", ModPackEntryState.Upgrade, "2.0", "1.0", "2.0")));
        var vm = CreateViewModel("主实例");
        vm.ImportPathText = "D:/in/列表.ckan";

        await vm.PreviewImportCommand.ExecuteAsync(null);

        Assert.That(_modPacks.PreviewCalls, Is.EqualTo(1));
        Assert.That(vm.PreviewRows.Select(row => row.Identifier),
            Is.EqualTo(new[] { "Installed", "Missing", "Installable", "Upgradable" }));
        Assert.That(vm.HasPreviewRows, Is.True);
        Assert.That(vm.ShowPreviewEmptyState, Is.False);
        Assert.That(vm.SelectableCount, Is.EqualTo(2));
        Assert.That(vm.SelectedCount, Is.EqualTo(2));
        Assert.That(vm.CanImport, Is.True);
        Assert.That(vm.PreviewCountText, Is.EqualTo("共 4 条，可导入 2 条"));
        Assert.That(vm.PreviewSummaryText, Does.Contain("将安装 1"));
        Assert.That(vm.PreviewSummaryText, Does.Contain("找不到 1"));
        Assert.That(vm.SelectedPreviewRow, Is.Null);
        Assert.That(vm.PreviewEmptyStateTitle, Is.EqualTo("还没有导入预览"));
    }

    [Test]
    public async Task PreviewImport_RowTextsAreChinese()
    {
        _modPacks.PreviewResult = OperationResult<ModPackPreview>.Ok(Preview(
            "列表.ckan",
            Entry("A", ModPackEntryState.Upgrade, "2.0", "1.0", "2.0"),
            Entry("B", ModPackEntryState.Missing, "9.9"),
            Entry("C", ModPackEntryState.SameVersion, "1.0", "1.0")));
        var vm = CreateViewModel("主实例");
        vm.ImportPathText = "D:/in/列表.ckan";

        await vm.PreviewImportCommand.ExecuteAsync(null);

        var upgrade = vm.PreviewRows.Single(row => row.Identifier == "A");
        Assert.That(upgrade.StateText, Is.EqualTo("将升级"));
        Assert.That(upgrade.StatusText, Is.EqualTo("1.0 → 2.0"));
        Assert.That(upgrade.CanSelect, Is.True);
        Assert.That(upgrade.IsSelected, Is.True);
        Assert.That(upgrade.SourceName, Is.EqualTo("列表.ckan"));

        var missing = vm.PreviewRows.Single(row => row.Identifier == "B");
        Assert.That(missing.StateText, Is.EqualTo("找不到"));
        Assert.That(missing.CanSelect, Is.False);
        Assert.That(missing.IsProblem, Is.True);
        Assert.That(missing.IsSelected, Is.False);

        var same = vm.PreviewRows.Single(row => row.Identifier == "C");
        Assert.That(same.StateText, Is.EqualTo("已安装"));
        Assert.That(same.ReasonText, Is.Not.Null);
    }

    [Test]
    public async Task PreviewImport_Failure_ShowsErrorMessage()
    {
        _modPacks.PreviewResult = OperationResult<ModPackPreview>.Fail("没有可导入的 .ckan 文件。", "跳过 2 个文件");
        var vm = CreateViewModel("主实例");
        vm.ImportPathText = "D:/in/坏文件.ckan";

        await vm.PreviewImportCommand.ExecuteAsync(null);

        Assert.That(vm.ErrorText, Is.EqualTo("没有可导入的 .ckan 文件。（不可重试）"));
        Assert.That(vm.ErrorDetail, Is.EqualTo("跳过 2 个文件"));
        Assert.That(vm.HasPreviewRows, Is.False);
        Assert.That(vm.CanImport, Is.False);
    }

    [Test]
    public async Task PreviewImport_Warnings_AreShown()
    {
        _modPacks.PreviewResult = OperationResult<ModPackPreview>.Ok(new ModPackPreview(
            new[] { "D:/in/a.ckan" },
            "a.ckan",
            new[] { Entry("A", ModPackEntryState.Install, "1.0", null, "1.0") },
            Array.Empty<string>(),
            new[] { "跳过不存在的文件：D:/in/b.ckan" }));
        var vm = CreateViewModel("主实例");
        vm.ImportPathText = "D:/in/a.ckan";

        await vm.PreviewImportCommand.ExecuteAsync(null);

        Assert.That(vm.HasPreviewWarning, Is.True);
        Assert.That(vm.PreviewWarningText, Does.Contain("跳过不存在的文件"));
    }

    // ---- 导入执行 ----------------------------------------------------------

    [Test]
    public async Task Import_WithoutPreview_ShowsError()
    {
        var vm = CreateViewModel("主实例");

        await vm.ImportCommand.ExecuteAsync(null);

        Assert.That(_modPacks.ImportCalls, Is.Zero);
        Assert.That(vm.ErrorText, Does.Contain("请先预览"));
    }

    [Test]
    public async Task Import_NothingSelected_ShowsError()
    {
        _modPacks.PreviewResult = OperationResult<ModPackPreview>.Ok(Preview(
            "列表.ckan",
            Entry("A", ModPackEntryState.Install, "1.0", null, "1.0")));
        var vm = CreateViewModel("主实例");
        vm.ImportPathText = "D:/in/列表.ckan";
        await vm.PreviewImportCommand.ExecuteAsync(null);

        vm.ClearSelectionCommand.Execute(null);
        await vm.ImportCommand.ExecuteAsync(null);

        Assert.That(_modPacks.ImportCalls, Is.Zero);
        Assert.That(vm.ErrorText, Does.Contain("至少勾选"));
        Assert.That(vm.CanImport, Is.False);
    }

    [Test]
    public async Task Import_PassesOnlyCheckedEntriesAndPreview()
    {
        _modPacks.PreviewResult = OperationResult<ModPackPreview>.Ok(Preview(
            "列表.ckan",
            Entry("Keep", ModPackEntryState.Install, "1.0", null, "1.0"),
            Entry("Skip", ModPackEntryState.Upgrade, "2.0", "1.0", "2.0"),
            Entry("Same", ModPackEntryState.SameVersion, "1.0", "1.0")));
        _modPacks.ImportResult = OperationResult<ModPackImportReport>.Ok(new ModPackImportReport(
            new[] { "Keep" },
            Array.Empty<string>(),
            Array.Empty<string>()));
        var vm = CreateViewModel("主实例");
        vm.ImportPathText = "D:/in/列表.ckan";
        await vm.PreviewImportCommand.ExecuteAsync(null);

        vm.PreviewRows.Single(row => row.Identifier == "Skip").IsSelected = false;
        await vm.ImportCommand.ExecuteAsync(null);

        Assert.That(_modPacks.ImportCalls, Is.EqualTo(1));
        Assert.That(_modPacks.LastSelected, Is.EqualTo(new[] { "Keep" }));
        Assert.That(_modPacks.LastPreview, Is.Not.Null);
        Assert.That(_modPacks.LastPreview!.Entries, Has.Count.EqualTo(3));
        Assert.That(vm.HasImportResult, Is.True);
        Assert.That(vm.ImportResultText, Does.Contain("成功 1 个"));
        Assert.That(vm.StatusText, Does.Contain("导入完成"));
        Assert.That(vm.ProgressText, Is.EqualTo("导入结束。"));
    }

    [Test]
    public async Task Import_PartialFailure_IsSurfacedInStatusAndResult()
    {
        _modPacks.PreviewResult = OperationResult<ModPackPreview>.Ok(Preview(
            "列表.ckan",
            Entry("A", ModPackEntryState.Install, "1.0", null, "1.0"),
            Entry("B", ModPackEntryState.Install, "1.0", null, "1.0")));
        _modPacks.ImportResult = OperationResult<ModPackImportReport>.Ok(new ModPackImportReport(
            new[] { "A" },
            new[] { "B" },
            new[] { "B 的下载包校验失败。" }));
        var vm = CreateViewModel("主实例");
        vm.ImportPathText = "D:/in/列表.ckan";
        await vm.PreviewImportCommand.ExecuteAsync(null);

        await vm.ImportCommand.ExecuteAsync(null);

        Assert.That(vm.ImportResultText, Does.Contain("失败清单：B"));
        Assert.That(vm.ImportResultText, Does.Contain("B 的下载包校验失败。"));
        Assert.That(vm.StatusText, Does.Contain("成功 1 个，失败 1 个"));
        Assert.That(vm.ErrorText, Is.Null);
    }

    [Test]
    public async Task Import_Failure_ShowsErrorAndKeepsPreview()
    {
        _modPacks.PreviewResult = OperationResult<ModPackPreview>.Ok(Preview(
            "列表.ckan",
            Entry("A", ModPackEntryState.Install, "1.0", null, "1.0")));
        _modPacks.ImportResult = OperationResult<ModPackImportReport>.Fail(
            "依赖求解发现冲突，已取消导入。", "A 需要 B", retryable: true);
        var vm = CreateViewModel("主实例");
        vm.ImportPathText = "D:/in/列表.ckan";
        await vm.PreviewImportCommand.ExecuteAsync(null);

        await vm.ImportCommand.ExecuteAsync(null);

        Assert.That(vm.ErrorText, Does.Contain("依赖求解发现冲突"));
        Assert.That(vm.ErrorText, Does.EndWith("（可重试）"));
        Assert.That(vm.HasImportResult, Is.False);
        Assert.That(vm.HasPreviewRows, Is.True);
    }

    [Test]
    public async Task Import_FailedProgress_UpdatesErrorBar()
    {
        _modPacks.PreviewResult = OperationResult<ModPackPreview>.Ok(Preview(
            "列表.ckan",
            Entry("A", ModPackEntryState.Install, "1.0", null, "1.0")));
        _modPacks.ImportReportsFailureProgress = true;
        var vm = CreateViewModel("主实例");
        vm.ImportPathText = "D:/in/列表.ckan";
        await vm.PreviewImportCommand.ExecuteAsync(null);

        await vm.ImportCommand.ExecuteAsync(null);

        Assert.That(vm.ErrorText, Is.EqualTo("磁盘已满"));
        Assert.That(vm.HasProgress, Is.True);
    }

    [Test]
    public async Task Import_Exception_IsReportedAndNeverThrown()
    {
        _modPacks.PreviewResult = OperationResult<ModPackPreview>.Ok(Preview(
            "列表.ckan",
            Entry("A", ModPackEntryState.Install, "1.0", null, "1.0")));
        var vm = CreateViewModel("主实例");
        vm.ImportPathText = "D:/in/列表.ckan";
        await vm.PreviewImportCommand.ExecuteAsync(null);
        _modPacks.ImportThrows = new InvalidOperationException("锁被占用");

        Assert.DoesNotThrowAsync(async () => await vm.ImportCommand.ExecuteAsync(null));

        Assert.That(_errors.Entries, Has.Count.EqualTo(1));
        Assert.That(_errors.Entries[0].Context, Is.EqualTo("导入模组"));
        Assert.That(vm.ErrorText, Does.Contain("锁被占用"));
    }

    // ---- 勾选 --------------------------------------------------------------

    [Test]
    public async Task SelectAllEntries_OnlySelectsSelectableRows()
    {
        _modPacks.PreviewResult = OperationResult<ModPackPreview>.Ok(Preview(
            "列表.ckan",
            Entry("A", ModPackEntryState.Install, "1.0", null, "1.0"),
            Entry("B", ModPackEntryState.Conflict, "0.5"),
            Entry("C", ModPackEntryState.SameVersion, "1.0", "1.0")));
        var vm = CreateViewModel("主实例");
        vm.ImportPathText = "D:/in/列表.ckan";
        await vm.PreviewImportCommand.ExecuteAsync(null);
        vm.ClearSelectionCommand.Execute(null);

        vm.SelectAllEntriesCommand.Execute(null);

        Assert.That(vm.SelectedCount, Is.EqualTo(1));
        Assert.That(vm.PreviewRows.Single(row => row.Identifier == "B").IsSelected, Is.False);
        Assert.That(vm.PreviewRows.Single(row => row.Identifier == "C").IsSelected, Is.False);
        Assert.That(vm.SelectionText, Is.EqualTo("已勾选 1 条"));
    }

    [Test]
    public async Task RowToggle_UpdatesCountsAndCanImport()
    {
        _modPacks.PreviewResult = OperationResult<ModPackPreview>.Ok(Preview(
            "列表.ckan",
            Entry("A", ModPackEntryState.Install, "1.0", null, "1.0")));
        var vm = CreateViewModel("主实例");
        vm.ImportPathText = "D:/in/列表.ckan";
        await vm.PreviewImportCommand.ExecuteAsync(null);
        var changed = new List<string?>();
        vm.PropertyChanged += (_, args) => changed.Add(args.PropertyName);

        vm.PreviewRows[0].IsSelected = false;

        Assert.That(vm.SelectedCount, Is.Zero);
        Assert.That(vm.CanImport, Is.False);
        Assert.That(changed, Does.Contain(nameof(ModPacksPageViewModel.SelectedCount)));
    }

    // ---- 实例对比 ----------------------------------------------------------

    private static InstanceDiffItem Diff(string identifier, InstanceDiffKind kind, string? left, string? right)
        => new(identifier, kind, left, right);

    [Test]
    public async Task Compare_ProjectsThreeKindsOfDiff()
    {
        _modPacks.CompareResult = OperationResult<InstanceComparison>.Ok(new InstanceComparison(
            "左实例",
            "右实例",
            new[]
            {
                Diff("OnlyLeft", InstanceDiffKind.OnlyInLeft, "1.0", null),
                Diff("OnlyRight", InstanceDiffKind.OnlyInRight, null, "2.0"),
                Diff("Changed", InstanceDiffKind.VersionDiffers, "1.0", "2.0")
            }));
        var vm = CreateViewModel("左实例", "右实例");

        await vm.CompareCommand.ExecuteAsync(null);

        Assert.That(_modPacks.LastLeft, Is.EqualTo("左实例"));
        Assert.That(_modPacks.LastRight, Is.EqualTo("右实例"));
        Assert.That(vm.HasDiffRows, Is.True);
        Assert.That(vm.ShowDiffEmptyState, Is.False);
        Assert.That(vm.DiffCountText, Is.EqualTo("共 3 处差异"));
        Assert.That(vm.DiffRows.Select(row => row.KindText),
            Is.EqualTo(new[] { "仅左侧有", "仅右侧有", "版本不同" }));
        Assert.That(vm.DiffRows[0].VersionsText, Is.EqualTo("左侧 1.0"));
        Assert.That(vm.DiffRows[1].LeftVersionText, Is.EqualTo("—"));
        Assert.That(vm.DiffRows[2].VersionsText, Is.EqualTo("1.0 → 2.0"));
        Assert.That(vm.DiffSummaryText, Does.Contain("左实例"));
        Assert.That(vm.StatusText, Does.Contain("共 3 处差异"));
    }

    [Test]
    public async Task Compare_IdenticalInstances_ShowsIdenticalSummary()
    {
        _modPacks.CompareResult = OperationResult<InstanceComparison>.Ok(
            new InstanceComparison("左", "右", Array.Empty<InstanceDiffItem>()));
        var vm = CreateViewModel("左", "右");

        await vm.CompareCommand.ExecuteAsync(null);

        Assert.That(vm.HasDiffRows, Is.False);
        Assert.That(vm.ShowDiffEmptyState, Is.True);
        Assert.That(vm.DiffCountText, Is.EqualTo("两个实例的模组列表一致。"));
        Assert.That(vm.DiffSummaryText, Does.Contain("完全一致"));
        Assert.That(vm.StatusText, Does.Contain("一致"));
    }

    [Test]
    public async Task Compare_SameInstanceOnBothSides_ShowsError()
    {
        var vm = CreateViewModel("A", "B");
        vm.RightInstance = "A";

        Assert.That(vm.CanCompare, Is.False);
        await vm.CompareCommand.ExecuteAsync(null);

        Assert.That(_modPacks.CompareCalls, Is.Zero);
        Assert.That(vm.ErrorText, Does.Contain("两个不同的实例"));
    }

    [Test]
    public async Task Compare_WithoutInstances_ShowsError()
    {
        var vm = CreateViewModel();

        Assert.That(vm.CanCompare, Is.False);
        await vm.CompareCommand.ExecuteAsync(null);

        Assert.That(_modPacks.CompareCalls, Is.Zero);
        Assert.That(vm.ErrorText, Does.Contain("请选择要对比的两个实例"));
    }

    [Test]
    public async Task Compare_Failure_ShowsRetryableError()
    {
        _modPacks.CompareResult = OperationResult<InstanceComparison>.Fail(
            "读取实例「B」的模组列表失败。", null, retryable: true);
        var vm = CreateViewModel("A", "B");

        await vm.CompareCommand.ExecuteAsync(null);

        Assert.That(vm.ErrorText, Does.Contain("读取实例「B」的模组列表失败。"));
        Assert.That(vm.ErrorText, Does.EndWith("（可重试）"));
        Assert.That(vm.HasDiffRows, Is.False);
    }

    // ---- 生命周期与实例刷新 ------------------------------------------------

    [Test]
    public void Activate_SubscribesToInstanceEvents()
    {
        var vm = CreateViewModel("A");
        vm.Activate();

        _instances.Seed("A", "B");
        _instances.RaiseInstancesChanged();

        Assert.That(vm.InstanceNames, Is.EqualTo(new[] { "A", "B" }));
        Assert.That(vm.RightInstance, Is.EqualTo("B"));
        Assert.That(vm.CanCompare, Is.True);

        vm.Deactivate();
        _instances.Seed("A", "B", "C");
        _instances.RaiseInstancesChanged();

        Assert.That(vm.InstanceNames, Is.EqualTo(new[] { "A", "B" }), "取消激活后不应再跟随实例事件");
    }

    [Test]
    public void CurrentChanged_RefreshesInstanceText()
    {
        var vm = CreateViewModel("A");
        vm.Activate();

        _modPacks.InstanceName = "另一个实例";
        _instances.RaiseCurrentChanged(new InstanceInfo("另一个实例", "C:/games/另一个实例", "1.12.5", true, false, 0));

        Assert.That(vm.InstanceTitleText, Is.EqualTo("当前实例：另一个实例"));
    }

    [Test]
    public async Task RefreshInstances_UpdatesNamesAndCallsService()
    {
        var vm = CreateViewModel("A");
        _instances.RefreshResult = OperationResult<IReadOnlyList<InstanceInfo>>.Ok(
            new[] { new InstanceInfo("X", "C:/x", "1.12.5", true, false, 0) });
        _instances.Seed("X");

        await vm.RefreshInstancesCommand.ExecuteAsync(null);

        Assert.That(_instances.RefreshCalls, Is.EqualTo(1));
        Assert.That(vm.InstanceNames, Is.EqualTo(new[] { "X" }));
        Assert.That(vm.StatusText, Does.Contain("已读取 1 个实例"));
    }

    [Test]
    public async Task RefreshInstances_Failure_ShowsError()
    {
        var vm = CreateViewModel("A");
        _instances.RefreshResult = OperationResult<IReadOnlyList<InstanceInfo>>.Fail("读取配置失败。");

        await vm.RefreshInstancesCommand.ExecuteAsync(null);

        Assert.That(vm.ErrorText, Does.Contain("读取配置失败。"));
        Assert.That(vm.ErrorText, Does.EndWith("（不可重试）"));
    }

    // ---- 空构造与错误条 ----------------------------------------------------

    [Test]
    public async Task ParameterlessCtor_UsesUnavailableDefaults()
    {
        var vm = new ModPacksPageViewModel();

        Assert.That(vm.Title, Is.EqualTo("导入导出"));
        Assert.That(vm.CanExport, Is.False);
        Assert.That(vm.InstanceNames, Is.Empty);

        await vm.PreviewImportCommand.ExecuteAsync(null);
        Assert.That(vm.ErrorText, Does.Contain("至少一个 .ckan 文件路径"));

        vm.ImportPathText = "D:/in/a.ckan";
        await vm.PreviewImportCommand.ExecuteAsync(null);

        Assert.That(vm.ErrorText, Does.Contain("当前导入导出服务不可用。"));
    }

    [Test]
    public async Task ClearError_ResetsErrorBar()
    {
        var vm = CreateViewModel("A");
        await vm.PreviewImportCommand.ExecuteAsync(null);
        Assert.That(vm.HasError, Is.True);

        vm.ClearErrorCommand.Execute(null);

        Assert.That(vm.HasError, Is.False);
        Assert.That(vm.HasErrorDetail, Is.False);
        Assert.That(vm.ErrorText, Is.Null);
    }

    [Test]
    public void ErrorText_IsMirroredIntoStatusBar()
    {
        var vm = CreateViewModel("A");
        vm.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(ModPacksPageViewModel.StatusText))
            {
                Assert.That(vm.StatusText, Is.Not.Null);
            }
        };

        Assert.DoesNotThrowAsync(async () => await vm.PreviewImportCommand.ExecuteAsync(null));
        Assert.That(vm.StatusText, Is.EqualTo(vm.ErrorText));
    }

    [Test]
    public void ExportForm_ResetClearsValidationAndFlags()
    {
        var form = new ExportFormViewModel();
        form.TargetPath = "D:/out/x.ckan";
        form.BundleDownloads = true;
        form.UseRecommends = true;
        form.WithVersions = false;
        form.ValidationText = "有问题";

        Assert.That(form.IsEmpty, Is.False);
        Assert.That(form.HasValidation, Is.True);
        Assert.That(form.HasBundleDownloads, Is.True);

        form.Reset();

        Assert.That(form.IsEmpty, Is.True);
        Assert.That(form.HasValidation, Is.False);
        Assert.That(form.TargetPath, Is.Empty);
        Assert.That(form.WithVersions, Is.True);
        Assert.That(form.BundleDownloads, Is.False);
        Assert.That(form.UseRecommends, Is.False);
    }
}
