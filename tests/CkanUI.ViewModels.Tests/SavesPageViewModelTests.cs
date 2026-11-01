using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using CkanUI.Services.Abstractions;
using CkanUI.Services.Abstractions.Dtos;
using CkanUI.ViewModels.Pages;
using CkanUI.ViewModels.Pages.Saves;
using NUnit.Framework;

namespace CkanUI.ViewModels.Tests;

/// <summary>
/// 存档工作台页面 VM 测试（03 篇 §14 / 06 篇 E1）：加载、空态、诊断展示与命令禁用条件。
/// </summary>
[TestFixture]
public sealed class SavesPageViewModelTests
{
    [Test]
    public async Task NoInstance_ShowsNoInstanceState_AndDisablesCommands()
    {
        var (vm, _, _, _, instances) = Create();
        instances.Current = null;

        await vm.RefreshCommand.ExecuteAsync(null);

        Assert.That(vm.HasInstance, Is.False);
        Assert.That(vm.ShowNoInstanceState, Is.True);
        Assert.That(vm.ShowEmptyState, Is.False);
        Assert.That(vm.InstanceText, Is.EqualTo("当前实例：未选择"));
        Assert.That(vm.SavesDirectoryText, Is.EqualTo("—"));
        Assert.That(vm.Rows, Is.Empty);
        Assert.That(vm.StatusText, Does.Contain("当前没有选择游戏实例"));
        Assert.That(vm.ValidateCommand.CanExecute(null), Is.False);
        Assert.That(vm.FormatCopyCommand.CanExecute(null), Is.False);
        Assert.That(vm.FormatInPlaceCommand.CanExecute(null), Is.False);
        Assert.That(vm.OpenFolderCommand.CanExecute(null), Is.False);
        Assert.That(vm.RefreshCommand.CanExecute(null), Is.True);
    }

    [Test]
    public async Task EmptyDirectory_ShowsEmptyStateWithSavesFolderAction()
    {
        var (vm, _, _, saves, _) = Create();
        saves.ListResult = OperationResult<IReadOnlyList<SaveGameInfo>>.Ok(Array.Empty<SaveGameInfo>());

        await vm.RefreshCommand.ExecuteAsync(null);

        Assert.That(vm.HasInstance, Is.True);
        Assert.That(vm.HasRows, Is.False);
        Assert.That(vm.ShowEmptyState, Is.True);
        Assert.That(vm.ShowNoInstanceState, Is.False);
        Assert.That(vm.EmptyStateTitle, Is.EqualTo("这个实例还没有存档"));
        Assert.That(vm.EmptyStateActionText, Is.EqualTo("打开 saves/ 目录"));
        Assert.That(vm.CountText, Is.EqualTo("共 0 个存档"));
        Assert.That(vm.SelectedRow, Is.Null);
        Assert.That(vm.HasSelection, Is.False);
        Assert.That(vm.SavesDirectoryText, Is.EqualTo(Path.Combine(@"C:\Games\KSP", "saves")));
        Assert.That(vm.OpenFolderCommand.CanExecute(null), Is.True);
        Assert.That(vm.ValidateCommand.CanExecute(null), Is.False);
    }

    [Test]
    public async Task Load_SelectsFirstRow_AndAutoValidatesIt()
    {
        var (vm, _, _, saves, _) = Create();
        var first = SavesFixtures.Save("persistent");
        var second = SavesFixtures.Save("quicksave");
        saves.ListResult = OperationResult<IReadOnlyList<SaveGameInfo>>.Ok(new[] { first, second });

        await vm.RefreshCommand.ExecuteAsync(null);

        Assert.That(vm.Rows.Count, Is.EqualTo(2));
        Assert.That(vm.SelectedRow!.SlotName, Is.EqualTo("persistent"));
        Assert.That(vm.CountText, Is.EqualTo("共 2 个存档"));
        // 首行自动选中后会立刻校验，状态栏停在最后一次动作的结果上。
        Assert.That(vm.StatusText, Is.EqualTo("persistent：校验通过，没有发现问题"));
        Assert.That(vm.IsValidated, Is.True);
        Assert.That(saves.ValidateRequests, Does.Contain(first.FilePath));
        Assert.That(vm.HasDiagnostics, Is.False);
        Assert.That(vm.ShowDiagnosticsEmpty, Is.True);
        Assert.That(vm.IsValidationClean, Is.True);
        Assert.That(vm.SelectionStatsText, Is.EqualTo("节点 2 / 键值 9 / 行 14 / 最大深度 2"));
    }

    [Test]
    public async Task ProblemReport_FillsDiagnosticsAndBlocksInPlaceRewrite()
    {
        var (vm, _, _, saves, _) = Create();
        var save = SavesFixtures.Save("persistent");
        saves.ListResult = OperationResult<IReadOnlyList<SaveGameInfo>>.Ok(new[] { save });
        saves.ValidateResults[save.FilePath] =
            OperationResult<SaveValidationReport>.Ok(SavesFixtures.ProblemReport(save.FilePath));

        await vm.RefreshCommand.ExecuteAsync(null);

        Assert.That(vm.Diagnostics.Count, Is.EqualTo(2));
        Assert.That(vm.DiagnosticCount, Is.EqualTo(2));
        Assert.That(vm.ErrorCount, Is.EqualTo(1));
        Assert.That(vm.WarningCount, Is.EqualTo(1));
        Assert.That(vm.HasDiagnostics, Is.True);
        Assert.That(vm.ShowDiagnosticsEmpty, Is.False);
        Assert.That(vm.IsValidationClean, Is.False);
        Assert.That(vm.DiagnosticsHeader, Does.Contain("1 个错误"));
        Assert.That(vm.StatusText, Does.Contain("persistent"));
        Assert.That(vm.Diagnostics[0].SeverityText, Is.EqualTo("错误"));
        Assert.That(vm.Diagnostics[0].Code, Is.EqualTo("V-01"));
        Assert.That(vm.Diagnostics[0].LocationText, Is.EqualTo("第 12 行第 3 列"));
        Assert.That(vm.Diagnostics[0].Snippet, Is.Not.Null);
        // 有错误级诊断时拒绝"先写后看"，但允许写出副本。
        Assert.That(vm.FormatInPlaceCommand.CanExecute(null), Is.False);
        Assert.That(vm.FormatCopyCommand.CanExecute(null), Is.True);
    }

    [Test]
    public async Task BackupRow_CannotBeFormatted()
    {
        var (vm, _, _, saves, _) = Create();
        var backup = SavesFixtures.Save("persistent", isBackup: true);
        saves.ListResult = OperationResult<IReadOnlyList<SaveGameInfo>>.Ok(new[] { backup });

        await vm.RefreshCommand.ExecuteAsync(null);

        Assert.That(vm.SelectedRow!.IsBackup, Is.True);
        Assert.That(vm.SelectedRow.CanFormat, Is.False);
        Assert.That(vm.CanFormatSelection, Is.False);
        Assert.That(vm.FormatCopyCommand.CanExecute(null), Is.False);
        Assert.That(vm.FormatInPlaceCommand.CanExecute(null), Is.False);
        Assert.That(vm.ValidateCommand.CanExecute(null), Is.True);
    }

    [Test]
    public async Task SelectingAnotherRow_ValidatesThatFile()
    {
        var (vm, _, _, saves, _) = Create();
        var first = SavesFixtures.Save("persistent");
        var second = SavesFixtures.Save("quicksave");
        saves.ListResult = OperationResult<IReadOnlyList<SaveGameInfo>>.Ok(new[] { first, second });
        await vm.RefreshCommand.ExecuteAsync(null);
        saves.ValidateRequests.Clear();

        vm.SelectedRow = vm.Rows[1];

        Assert.That(saves.ValidateRequests, Is.EqualTo(new[] { second.FilePath }));
        Assert.That(vm.SelectionTitle, Is.EqualTo("quicksave"));
        Assert.That(vm.SelectionPath, Is.EqualTo(second.FilePath));
        Assert.That(vm.HasSelection, Is.True);
    }

    [Test]
    public async Task FormatCopyCommand_WritesFormattedCopy()
    {
        var (vm, _, _, saves, _) = Create();
        var save = SavesFixtures.Save("persistent");
        saves.ListResult = OperationResult<IReadOnlyList<SaveGameInfo>>.Ok(new[] { save });
        await vm.RefreshCommand.ExecuteAsync(null);

        await vm.FormatCopyCommand.ExecuteAsync(null);

        Assert.That(saves.FormatRequests.Count, Is.EqualTo(1));
        Assert.That(saves.FormatRequests[0].Mode, Is.EqualTo(SaveFormatMode.NewFile));
        Assert.That(saves.FormatRequests[0].FilePath, Is.EqualTo(save.FilePath));
        Assert.That(vm.HasLastOutcome, Is.True);
        Assert.That(vm.LastOutcomeText, Does.Contain("persistent.formatted.sfs"));
        Assert.That(vm.LastOutcomeText, Does.Contain("原文件未改动"));
        Assert.That(vm.HasError, Is.False);
    }

    [Test]
    public async Task FormatInPlace_DeclinedConfirm_DoesNotWrite()
    {
        var (vm, dialogs, _, saves, _) = Create();
        var save = SavesFixtures.Save("persistent");
        saves.ListResult = OperationResult<IReadOnlyList<SaveGameInfo>>.Ok(new[] { save });
        await vm.RefreshCommand.ExecuteAsync(null);
        dialogs.ConfirmResult = false;

        await vm.FormatInPlaceCommand.ExecuteAsync(null);

        Assert.That(dialogs.ConfirmCalls, Is.EqualTo(1));
        Assert.That(saves.FormatRequests, Is.Empty);
        Assert.That(vm.StatusText, Does.Contain("已取消"));
        Assert.That(vm.HasError, Is.False);
    }

    [Test]
    public async Task FormatInPlace_Confirmed_WritesOverwrite()
    {
        var (vm, dialogs, _, saves, _) = Create();
        var save = SavesFixtures.Save("persistent");
        saves.ListResult = OperationResult<IReadOnlyList<SaveGameInfo>>.Ok(new[] { save });
        await vm.RefreshCommand.ExecuteAsync(null);
        dialogs.ConfirmResult = true;

        await vm.FormatInPlaceCommand.ExecuteAsync(null);

        Assert.That(dialogs.ConfirmCalls, Is.EqualTo(1));
        Assert.That(saves.FormatRequests.Single().Mode, Is.EqualTo(SaveFormatMode.Overwrite));
    }

    [Test]
    public async Task FormatRefused_ShowsErrorBarInsteadOfSilentWrite()
    {
        var (vm, _, _, saves, _) = Create();
        var save = SavesFixtures.Save("persistent");
        saves.ListResult = OperationResult<IReadOnlyList<SaveGameInfo>>.Ok(new[] { save });
        await vm.RefreshCommand.ExecuteAsync(null);
        saves.FormatResult = OperationResult<SaveFormatOutcome>.Ok(
            SavesFixtures.RefusedOutcome(new SaveFormatRequest(save.FilePath, SaveFormatMode.NewFile)));

        await vm.FormatCopyCommand.ExecuteAsync(null);

        Assert.That(vm.HasError, Is.True);
        Assert.That(vm.ErrorText, Does.Contain("未改动"));
        Assert.That(vm.ErrorDetail, Does.Contain("V-01"));
        Assert.That(vm.HasLastOutcome, Is.False);
    }

    [Test]
    public async Task ListFailure_ShowsErrorAndKeepsRowsEmpty()
    {
        var (vm, _, _, saves, _) = Create();
        saves.ListResult = OperationResult<IReadOnlyList<SaveGameInfo>>.Fail(
            "读取存档目录失败：目录可能被其他程序占用或没有权限。",
            @"C:\Games\KSP\saves：拒绝访问",
            retryable: true);

        await vm.RefreshCommand.ExecuteAsync(null);

        Assert.That(vm.HasError, Is.True);
        Assert.That(vm.ErrorText, Does.Contain("可重试"));
        Assert.That(vm.ErrorDetail, Does.Contain("拒绝访问"));
        Assert.That(vm.Rows, Is.Empty);
        Assert.That(vm.SelectedRow, Is.Null);
        Assert.That(vm.ShowEmptyState, Is.True);

        vm.ClearErrorCommand.Execute(null);
        Assert.That(vm.HasError, Is.False);
        Assert.That(vm.StatusText, Is.EqualTo("就绪"));
    }

    [Test]
    public async Task ValidateFailure_ShowsErrorAndClearsValidation()
    {
        var (vm, _, _, saves, _) = Create();
        var save = SavesFixtures.Save("persistent");
        saves.ListResult = OperationResult<IReadOnlyList<SaveGameInfo>>.Ok(new[] { save });
        await vm.RefreshCommand.ExecuteAsync(null);
        saves.ValidateResults[save.FilePath] = OperationResult<SaveValidationReport>.Fail(
            "读取存档失败：文件可能被其他程序占用。",
            save.FilePath + "：另一个进程正在使用此文件",
            retryable: false);

        await vm.ValidateCommand.ExecuteAsync(null);

        Assert.That(vm.HasError, Is.True);
        Assert.That(vm.ErrorText, Does.Contain("文件可能被其他程序占用"));
        Assert.That(vm.IsValidated, Is.False);
        Assert.That(vm.Diagnostics, Is.Empty);
        Assert.That(vm.FormatInPlaceCommand.CanExecute(null), Is.False);
    }

    [Test]
    public async Task ServiceException_IsReportedAndTurnedIntoErrorBar()
    {
        var (vm, _, errors, saves, _) = Create();
        saves.ListResult = null!;

        await vm.RefreshCommand.ExecuteAsync(null);

        Assert.That(errors.ReportCount, Is.GreaterThan(0));
        Assert.That(vm.HasError, Is.True);
        Assert.That(vm.Rows, Is.Empty);
        Assert.That(vm.IsBusy, Is.False);
    }

    [Test]
    public async Task OpenFolder_WithoutLauncher_SaysHostNotInjected()
    {
        var (vm, _, _, saves, _) = Create();
        var save = SavesFixtures.Save("persistent");
        saves.ListResult = OperationResult<IReadOnlyList<SaveGameInfo>>.Ok(new[] { save });
        await vm.RefreshCommand.ExecuteAsync(null);

        await vm.OpenFolderCommand.ExecuteAsync(null);

        Assert.That(vm.HasError, Is.True);
        Assert.That(vm.ErrorText, Does.Contain("宿主未注入"));
    }

    [Test]
    public async Task OpenFolder_UsesSelectedFileDirectory()
    {
        var (vm, _, _, saves, _) = Create();
        var save = SavesFixtures.Save("persistent");
        saves.ListResult = OperationResult<IReadOnlyList<SaveGameInfo>>.Ok(new[] { save });
        var launcher = new SavesFakeShellLauncher();
        vm.ShellLauncher = launcher;
        await vm.RefreshCommand.ExecuteAsync(null);

        await vm.OpenFolderCommand.ExecuteAsync(null);

        Assert.That(launcher.Paths, Is.EqualTo(new[] { Path.GetDirectoryName(save.FilePath) }));
        Assert.That(vm.HasError, Is.False);
        Assert.That(vm.StatusText, Does.Contain("已在文件管理器中打开"));
    }

    [Test]
    public async Task OpenFolder_LauncherFailure_SurfacesError()
    {
        var (vm, _, _, saves, _) = Create();
        var launcher = new SavesFakeShellLauncher
        {
            Result = OperationResult.Fail("目录不存在：C:\nope", null, retryable: false),
        };
        saves.ListResult = OperationResult<IReadOnlyList<SaveGameInfo>>.Ok(new[] { SavesFixtures.Save() });
        vm.ShellLauncher = launcher;
        await vm.RefreshCommand.ExecuteAsync(null);

        await vm.OpenFolderCommand.ExecuteAsync(null);

        Assert.That(vm.HasError, Is.True);
        Assert.That(vm.ErrorText, Does.Contain("目录不存在"));
    }

    [Test]
    public async Task Activate_RefreshesAndSubscribesToInstanceSwitch()
    {
        var (vm, _, _, saves, instances) = Create();
        saves.ListResult = OperationResult<IReadOnlyList<SaveGameInfo>>.Ok(Array.Empty<SaveGameInfo>());

        vm.Activate();
        Assert.That(vm.IsActive, Is.True);

        instances.Current = SavesFixtures.Instance("二队", @"D:\Games\KSP2");
        instances.RaiseCurrentChanged();

        Assert.That(vm.InstanceText, Does.Contain("二队"));
        Assert.That(vm.SavesDirectoryText, Is.EqualTo(Path.Combine(@"D:\Games\KSP2", "saves")));
        Assert.That(vm.CountText, Is.EqualTo("共 0 个存档"));

        vm.Deactivate();
        Assert.That(vm.IsActive, Is.False);
    }

    [Test]
    public void Title_IsLocalizedChinese()
    {
        var (vm, _, _, _, _) = Create();
        Assert.That(vm.Title, Is.EqualTo("存档"));
    }

    [Test]
    public void IsStockSlot_UsesFirstSegmentOfSlotName()
    {
        // 服务层拼出的槽位名带 " / " 分隔，首段就是 saves/ 下的目录名（注意首段带尾空格）。
        Assert.That(SaveRowViewModel.IsStockSlot("training / G_Docking"), Is.True);
        Assert.That(SaveRowViewModel.IsStockSlot("scenarios / Space Station 1"), Is.True);
        Assert.That(SaveRowViewModel.IsStockSlot("SCENARIOS / Mun Rover"), Is.True);
        Assert.That(SaveRowViewModel.IsStockSlot("M6_FlightUI / persistent"), Is.False);
        Assert.That(SaveRowViewModel.IsStockSlot("M6_FlightUI / Backup / persistent (2026_10_29_10_35_40)"), Is.False);
        Assert.That(SaveRowViewModel.IsStockSlot("trainingX / persistent"), Is.False);
        Assert.That(SaveRowViewModel.IsStockSlot("persistent"), Is.False);
        Assert.That(SaveRowViewModel.IsStockSlot(""), Is.False);
        Assert.That(SaveRowViewModel.IsStockSlot(null), Is.False);
    }

    [Test]
    public async Task StockSaves_AreHiddenByDefault_AndRevealedByCommand()
    {
        var (vm, _, _, saves, _) = Create();
        saves.ListResult = OperationResult<IReadOnlyList<SaveGameInfo>>.Ok(new[]
        {
            SavesFixtures.Save(slot: "M6_FlightUI / persistent", title: "远航号"),
            SavesFixtures.Save(slot: "training / G_Docking", title: "#autoLOC_1200093"),
        });

        await vm.RefreshCommand.ExecuteAsync(null);

        Assert.That(vm.Rows.Select(row => row.SlotName), Is.EqualTo(new[] { "M6_FlightUI / persistent" }));
        Assert.That(vm.HasStockSaves, Is.True);
        Assert.That(vm.StockCount, Is.EqualTo(1));
        Assert.That(vm.StockToggleText, Is.EqualTo("自带存档（1）"));
        Assert.That(vm.ShowStockOnlyState, Is.False);

        vm.RevealStockSavesCommand.Execute(null);

        Assert.That(vm.ShowStockSaves, Is.True);
        Assert.That(vm.Rows.Count, Is.EqualTo(2));
        Assert.That(vm.CountText, Is.EqualTo("共 2 个存档"));
        Assert.That(vm.Rows[1].IsStock, Is.True);
        // 未解析的本地化键不能当标题显示
        Assert.That(vm.Rows[1].TitleText, Is.EqualTo("—"));
    }

    [Test]
    public async Task OnlyStockSaves_ShowsStockOnlyStateWithRevealAction()
    {
        var (vm, _, _, saves, _) = Create();
        saves.ListResult = OperationResult<IReadOnlyList<SaveGameInfo>>.Ok(new[]
        {
            SavesFixtures.Save(slot: "scenarios / Jool Aerobrake"),
        });

        await vm.RefreshCommand.ExecuteAsync(null);

        Assert.That(vm.HasRows, Is.False);
        Assert.That(vm.ShowStockOnlyState, Is.True);
        Assert.That(vm.ShowEmptyState, Is.False);
        Assert.That(vm.StockOnlyActionText, Is.EqualTo("显示自带存档"));
        Assert.That(vm.RevealStockSavesCommand.CanExecute(null), Is.True);
    }

    private static (
        SavesPageViewModel ViewModel,
        InstancesFakeDialogs Dialogs,
        InstancesFakeErrorReporter Errors,
        SavesFakeSaveFileService Saves,
        InstancesFakeService Instances) Create()
    {
        var instances = new InstancesFakeService { Current = SavesFixtures.Instance() };
        var saves = new SavesFakeSaveFileService();
        var errors = new InstancesFakeErrorReporter();
        var dialogs = new InstancesFakeDialogs();
        return (new SavesPageViewModel(instances, saves, errors, dialogs), dialogs, errors, saves, instances);
    }
}
