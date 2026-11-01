using CkanUI.Services.Abstractions;
using CkanUI.Services.Abstractions.ModConfig;
using CkanUI.ViewModels.Pages;
using CkanUI.ViewModels.Pages.ModConfig;
using NUnit.Framework;

namespace CkanUI.ViewModels.Tests.ModConfig;

/// <summary>
/// Mod 配置页的编辑与写盘编排（M9 / CFG-04）：脏标记与底部改动条、逐文件调用 IAdapterWriter、
/// 游戏运行中与高风险字段的两道确认、部分失败逐文件报错、.bak 快照回滚、放弃草稿、筛选后草稿不丢。
/// </summary>
/// <remarks>
/// 这里一律用假 IAdapterWriter / IDialogService / IModConfigBackupRestorer：
/// 页面 VM 不直接碰文件，也不自己 new 对话框。
/// </remarks>
[TestFixture]
public sealed class ModConfigPageEditingTests
{
    private const string SettingsPath = "/game/GameData/Demo/settings.cfg";
    private const string OtherPath = "/game/GameData/Demo/other.cfg";

    [Test]
    public void EditingRow_MarksDirtyAndUpdatesBar()
    {
        var harness = Create();

        harness.Row("enabled").IsToggleOn = false;

        Assert.That(harness.Vm.HasPendingChanges, Is.True);
        Assert.That(harness.Vm.PendingChangeCount, Is.EqualTo(1));
        Assert.That(harness.Vm.ModifiedBarText, Is.EqualTo("待保存 1 项"));
        Assert.That(harness.Vm.SaveButtonText, Is.EqualTo("保存 1 项改动"));
        Assert.That(harness.Vm.CanSave, Is.True);
        Assert.That(harness.Vm.CanDiscardPending, Is.True);
        Assert.That(harness.Vm.ShowActionBar, Is.True);
        Assert.That(harness.Row("enabled").HasPendingChange, Is.True);
    }

    [Test]
    public void EditingBackToDiskValue_ClearsDirty()
    {
        var harness = Create();
        var row = harness.Row("enabled");

        row.IsToggleOn = false;
        row.IsToggleOn = true;

        Assert.That(harness.Vm.HasPendingChanges, Is.False);
        Assert.That(harness.Vm.ModifiedBarText, Is.Empty);
        Assert.That(harness.Vm.CanSave, Is.False);
    }

    [Test]
    public void ModifiedBarText_ShowsBothCounts()
    {
        var harness = Create(mods: [ModConfigTestData.Mod(
            files: [SettingsFile()],
            modifiedCount: 2)]);

        harness.Row("limit").EditValue = "20";

        Assert.That(harness.Vm.ModifiedBarText, Is.EqualTo("改动 2 项（待保存 1 项）"));
    }

    [Test]
    public async Task Save_WithNoPendingChanges_ReportsStatus()
    {
        var harness = Create();

        await harness.Vm.SaveCommand.ExecuteAsync(null);

        Assert.That(harness.Vm.StatusText, Is.EqualTo("没有需要保存的改动。"));
        Assert.That(harness.Writer.Calls, Is.Empty);
    }

    [Test]
    public async Task Save_WithoutWriter_GivesReadableError()
    {
        var harness = Create(withWriter: false);
        harness.Row("limit").EditValue = "20";

        Assert.That(harness.Vm.CanSave, Is.True);

        await harness.Vm.SaveCommand.ExecuteAsync(null);

        Assert.That(harness.Vm.ErrorText, Does.Contain("没有写盘入口"));
        Assert.That(harness.Vm.PendingChangeCount, Is.EqualTo(1));
    }

    [Test]
    public async Task Save_WritesOncePerFile()
    {
        var harness = Create(mods: [TwoFileMod()]);
        harness.Row("enabled").IsToggleOn = false;
        harness.Row("limit").EditValue = "20";
        harness.Row("name").EditValue = "新名字";

        await harness.Vm.SaveCommand.ExecuteAsync(null);

        Assert.That(harness.Writer.Calls.Count, Is.EqualTo(2));

        var settings = harness.Writer.Calls.Single(call => call.Changes[0].FilePath == SettingsPath);
        Assert.That(settings.InstanceName, Is.EqualTo("默认"));
        Assert.That(settings.GameDir, Is.EqualTo("C:/game"));
        Assert.That(settings.AdapterId, Is.EqualTo("demo-mod"));
        Assert.That(settings.Changes.Select(change => change.Key), Is.EqualTo(new[] { "enabled", "limit" }));
        Assert.That(settings.Changes.Select(change => change.NewValue), Is.EqualTo(new[] { "false", "20" }));

        var other = harness.Writer.Calls.Single(call => call.Changes[0].FilePath == OtherPath);
        Assert.That(other.Changes.Single().Key, Is.EqualTo("name"));
        Assert.That(other.Changes.Single().NewValue, Is.EqualTo("新名字"));
    }

    [Test]
    public async Task Save_GroupsByAdapterAcrossMods()
    {
        var harness = Create(mods:
        [
            ModConfigTestData.Mod(adapterId: "mod-a", name: "A Mod", files:
                [EditableFile("/game/GameData/A/settings.cfg", ModConfigTestData.Field("aKey", value: "1"))]),
            ModConfigTestData.Mod(adapterId: "mod-b", name: "B Mod", files:
                [EditableFile("/game/GameData/B/settings.cfg", ModConfigTestData.Field("bKey", value: "1"))]),
        ]);
        harness.Vm.SelectMod(harness.Vm.Mods[0]);
        harness.Row("aKey").EditValue = "2";
        harness.Vm.SelectMod(harness.Vm.Mods[1]);
        harness.Row("bKey").EditValue = "3";

        await harness.Vm.SaveCommand.ExecuteAsync(null);

        Assert.That(harness.Writer.Calls.Select(call => call.AdapterId), Is.EqualTo(new[] { "mod-a", "mod-b" }));
    }

    [Test]
    public async Task Save_NoConfirmationNeeded_FlagsStayFalse()
    {
        var harness = Create();
        harness.Row("limit").EditValue = "20";

        await harness.Vm.SaveCommand.ExecuteAsync(null);

        Assert.That(harness.Dialogs!.ConfirmationTitles, Is.Empty);
        Assert.That(harness.Writer.Calls.Single().ConfirmedGameRunning, Is.False);
        Assert.That(harness.Writer.Calls.Single().ConfirmedRiskHigh, Is.False);
    }

    [Test]
    public async Task Save_GameRunning_Confirmed_WritesWithFlag()
    {
        var harness = Create(isGameRunning: true, confirmAnswers: [true]);
        harness.Row("limit").EditValue = "20";

        await harness.Vm.SaveCommand.ExecuteAsync(null);

        Assert.That(harness.Dialogs!.ConfirmationTitles, Is.EqualTo(new[] { "KSP 正在运行" }));
        Assert.That(harness.Dialogs!.ConfirmationMessages[0], Does.Contain("重启"));
        Assert.That(harness.Writer.Calls.Single().ConfirmedGameRunning, Is.True);
        Assert.That(harness.Writer.Calls.Single().ConfirmedRiskHigh, Is.False);
    }

    [Test]
    public async Task Save_GameRunning_Refused_DoesNotWrite()
    {
        var harness = Create(isGameRunning: true, confirmAnswers: [false]);
        harness.Row("limit").EditValue = "20";

        await harness.Vm.SaveCommand.ExecuteAsync(null);

        Assert.That(harness.Writer.Calls, Is.Empty);
        Assert.That(harness.Vm.StatusText, Is.EqualTo("已取消保存：KSP 正在运行时的写盘需要确认。"));
        Assert.That(harness.Vm.PendingChangeCount, Is.EqualTo(1));
        Assert.That(harness.Row("limit").EditValue, Is.EqualTo("20"));
    }

    [Test]
    public async Task Save_HighRisk_Refused_DoesNotWrite()
    {
        var harness = Create(mods: [RiskyMod()], confirmAnswers: [false]);
        harness.Row("pad").EditValue = "2";

        await harness.Vm.SaveCommand.ExecuteAsync(null);

        Assert.That(harness.Dialogs!.ConfirmationTitles, Is.EqualTo(new[] { "确认修改高风险字段" }));
        Assert.That(harness.Dialogs!.ConfirmationMessages[0], Does.Contain("pad"));
        Assert.That(harness.Writer.Calls, Is.Empty);
        Assert.That(harness.Vm.StatusText, Is.EqualTo("已取消保存：高风险字段的改动需要二次确认。"));
    }

    [Test]
    public async Task Save_GameRunningAndHighRisk_AsksTwice()
    {
        var harness = Create(mods: [RiskyMod()], isGameRunning: true, confirmAnswers: [true, true]);
        harness.Row("pad").EditValue = "2";

        await harness.Vm.SaveCommand.ExecuteAsync(null);

        Assert.That(harness.Dialogs!.ConfirmationTitles,
            Is.EqualTo(new[] { "KSP 正在运行", "确认修改高风险字段" }));
        Assert.That(harness.Writer.Calls.Single().ConfirmedGameRunning, Is.True);
        Assert.That(harness.Writer.Calls.Single().ConfirmedRiskHigh, Is.True);
    }

    [Test]
    public async Task Save_WithoutDialogService_DoesNotWrite()
    {
        var harness = Create(isGameRunning: true, withDialogs: false);
        harness.Row("limit").EditValue = "20";

        await harness.Vm.SaveCommand.ExecuteAsync(null);

        Assert.That(harness.Writer.Calls, Is.Empty);
        Assert.That(harness.Vm.ErrorText, Does.Contain("没有确认对话框"));
    }

    [Test]
    public async Task Save_Success_ReportsSummary()
    {
        var harness = Create();
        harness.Writer.Responder = request => Wrote(request, restartCount: 1);
        harness.Row("limit").EditValue = "20";

        await harness.Vm.SaveCommand.ExecuteAsync(null);

        Assert.That(harness.Vm.StatusText, Is.EqualTo("已保存 1 项改动（1 个文件）；1 项需要重启 KSP 后生效。"));
        Assert.That(harness.Vm.ErrorText, Is.Null);
        Assert.That(harness.Vm.HasPendingChanges, Is.False);
        Assert.That(harness.Vm.HasSaveFailures, Is.False);
    }

    [Test]
    public async Task Save_PartialFailure_KeepsFailedDraft()
    {
        var harness = Create(mods: [TwoFileMod()]);
        harness.Writer.Responder = request => request.Changes[0].FilePath == OtherPath
            ? OperationResult<AdapterWriteResult>.Fail("写不进去。", "io")
            : Wrote(request);
        harness.Row("enabled").IsToggleOn = false;
        harness.Row("name").EditValue = "新名字";

        await harness.Vm.SaveCommand.ExecuteAsync(null);

        Assert.That(harness.Vm.ErrorText, Is.EqualTo("部分文件保存失败：1 / 2 个文件没写成，改动已保留。"));
        Assert.That(harness.Vm.HasSaveFailures, Is.True);
        Assert.That(harness.Vm.SaveFailures.Count, Is.EqualTo(1));
        Assert.That(harness.Vm.SaveFailures[0], Does.Contain("other.cfg"));
        Assert.That(harness.Vm.SaveFailures[0], Does.Contain("写不进去。"));
        Assert.That(harness.Vm.PendingChangeCount, Is.EqualTo(1));
        Assert.That(harness.Row("name").EditValue, Is.EqualTo("新名字"));
        Assert.That(harness.Row("name").HasPendingChange, Is.True);
        Assert.That(harness.Row("enabled").HasPendingChange, Is.False);
    }

    [Test]
    public async Task Save_AllFilesFail_KeepsEveryDraft()
    {
        var harness = Create(mods: [TwoFileMod()]);
        harness.Writer.Responder = request => OperationResult<AdapterWriteResult>.Fail("磁盘满。", null);
        harness.Row("enabled").IsToggleOn = false;
        harness.Row("name").EditValue = "新名字";

        await harness.Vm.SaveCommand.ExecuteAsync(null);

        Assert.That(harness.Vm.ErrorText, Is.EqualTo("保存失败：这次没有任何文件写成，改动已保留。"));
        Assert.That(harness.Vm.PendingChangeCount, Is.EqualTo(2));
        Assert.That(harness.Vm.SaveFailures.Count, Is.EqualTo(2));
    }

    [Test]
    public async Task Save_WhenWriterThrows_ReportsPerFile()
    {
        var harness = Create();
        harness.Writer.Throws = true;
        harness.Row("limit").EditValue = "20";

        await harness.Vm.SaveCommand.ExecuteAsync(null);

        Assert.That(harness.Vm.SaveFailures.Count, Is.EqualTo(1));
        Assert.That(harness.Vm.SaveFailures[0], Does.Contain("写入配置失败："));
        Assert.That(harness.Vm.PendingChangeCount, Is.EqualTo(1));
    }

    [Test]
    public async Task Save_ReloadsModelFromService()
    {
        var harness = Create();
        harness.Row("limit").EditValue = "20";
        harness.Config.Current = ModConfigTestData.Model(mods: [ModConfigTestData.Mod(files:
            [EditableFile(SettingsPath,
                ModConfigTestData.Field("enabled", ModConfigFieldType.Bool, "true"),
                ModConfigTestData.Field("limit", ModConfigFieldType.Int, "20"))])]);

        await harness.Vm.SaveCommand.ExecuteAsync(null);

        Assert.That(harness.Config.Calls, Is.EqualTo(new[] { string.Empty }));
        Assert.That(harness.Row("limit").EditValue, Is.EqualTo("20"));
        Assert.That(harness.Row("limit").ValueText, Is.EqualTo("20"));
        Assert.That(harness.Vm.SelectedMod, Is.Not.Null);
    }

    [Test]
    public async Task Save_ExposesBackupsForRestore()
    {
        var harness = Create();
        harness.Row("limit").EditValue = "20";

        await harness.Vm.SaveCommand.ExecuteAsync(null);

        Assert.That(harness.Vm.HasBackups, Is.True);
        Assert.That(harness.Vm.RestoreButtonText, Is.EqualTo("恢复上次快照（1 个文件）"));
        Assert.That(harness.Vm.BackupHintText, Does.Contain("settings.cfg.ckan-ui-20240102-030405.bak"));
        Assert.That(harness.Vm.CanRestoreBackups, Is.True);
    }

    [Test]
    public async Task Restore_Refused_DoesNotRestore()
    {
        var harness = Create(confirmAnswers: [false]);
        harness.Row("limit").EditValue = "20";
        await harness.Vm.SaveCommand.ExecuteAsync(null);

        await harness.Vm.RestoreBackupsCommand.ExecuteAsync(null);

        Assert.That(harness.Dialogs!.ConfirmationTitles, Is.EqualTo(new[] { "恢复上次快照" }));
        Assert.That(harness.Restorer.Calls, Is.Empty);
        Assert.That(harness.Vm.StatusText, Is.EqualTo("已取消恢复快照。"));
        Assert.That(harness.Vm.HasBackups, Is.True);
    }

    [Test]
    public async Task Restore_Success_ClearsBackupsAndDrafts()
    {
        var harness = Create(confirmAnswers: [true]);
        harness.Row("limit").EditValue = "20";
        await harness.Vm.SaveCommand.ExecuteAsync(null);
        harness.Row("enabled").IsToggleOn = false;

        await harness.Vm.RestoreBackupsCommand.ExecuteAsync(null);

        Assert.That(harness.Restorer.Calls.Count, Is.EqualTo(1));
        Assert.That(harness.Restorer.Calls[0].Count, Is.EqualTo(1));
        Assert.That(harness.Vm.HasBackups, Is.False);
        Assert.That(harness.Vm.HasPendingChanges, Is.False);
        Assert.That(harness.Vm.StatusText, Is.EqualTo("已恢复 1 个文件到上一次写盘前的快照。"));
    }

    [Test]
    public async Task Restore_Failure_KeepsBackupsAndReports()
    {
        var harness = Create(confirmAnswers: [true]);
        harness.Restorer.Succeeds = false;
        harness.Row("limit").EditValue = "20";
        await harness.Vm.SaveCommand.ExecuteAsync(null);

        await harness.Vm.RestoreBackupsCommand.ExecuteAsync(null);

        Assert.That(harness.Vm.ErrorText, Does.Contain("恢复快照失败"));
        Assert.That(harness.Vm.HasBackups, Is.True);
        Assert.That(harness.Vm.CanRestoreBackups, Is.True);
    }

    [Test]
    public void DiscardPending_ResetsRows()
    {
        var harness = Create();
        harness.Row("limit").EditValue = "20";

        harness.Vm.DiscardPendingCommand.Execute(null);

        Assert.That(harness.Vm.PendingChangeCount, Is.Zero);
        Assert.That(harness.Vm.StatusText, Is.EqualTo("已放弃 1 项未保存的改动。"));
        Assert.That(harness.Vm.CanDiscardPending, Is.False);
    }

    [Test]
    public void EditsSurviveFiltering()
    {
        var harness = Create();
        harness.Row("limit").EditValue = "20";

        harness.Vm.SearchText = "enabled";

        Assert.That(harness.Vm.FieldGroups.SelectMany(group => group.Rows).Select(row => row.Key),
            Is.EqualTo(new[] { "enabled" }));

        harness.Vm.SearchText = string.Empty;

        Assert.That(harness.Row("limit").EditValue, Is.EqualTo("20"));
        Assert.That(harness.Row("limit").HasPendingChange, Is.True);
        Assert.That(harness.Vm.PendingChangeCount, Is.EqualTo(1));
    }

    [Test]
    public void ReadonlyFileRow_DoesNotProduceDraft()
    {
        var harness = Create(mods: [ModConfigTestData.Mod(files:
            [ModConfigTestData.ReadonlyFile("这个文件由 mod 自己生成。", ModConfigTestData.Field("heap", value: "256"))])]);

        var row = harness.Row("heap");
        row.EditValue = "512";

        Assert.That(row.CanEdit, Is.False);
        Assert.That(row.HasPendingChange, Is.False);
        Assert.That(harness.Vm.PendingChangeCount, Is.Zero);
        Assert.That(harness.Vm.CanSave, Is.False);
    }

    [Test]
    public async Task SaveRow_WritesOnlyThatFile()
    {
        var harness = Create(mods: [TwoFileMod()]);
        harness.Row("enabled").IsToggleOn = false;
        var nameRow = harness.Row("name");
        nameRow.EditValue = "新名字";

        await harness.Vm.SaveRowCommand.ExecuteAsync(nameRow);

        Assert.That(harness.Writer.Calls.Count, Is.EqualTo(1));
        Assert.That(harness.Writer.Calls[0].Changes.Single().FilePath, Is.EqualTo(OtherPath));
        Assert.That(harness.Vm.PendingChangeCount, Is.EqualTo(1));
        Assert.That(harness.Row("enabled").HasPendingChange, Is.True);
    }

    [Test]
    public async Task SaveRow_WithoutDraft_GivesHint()
    {
        var harness = Create();

        await harness.Vm.SaveRowCommand.ExecuteAsync(harness.Row("limit"));

        Assert.That(harness.Vm.StatusText, Is.EqualTo("这一行没有需要保存的改动。"));
        Assert.That(harness.Writer.Calls, Is.Empty);
    }

    // ---- 夹具 --------------------------------------------------------------

    private static ModConfigFileSnapshot EditableFile(string path, params ModConfigFieldState[] fields)
        => new(
            FilePath: path,
            RelativePath: "GameData/Demo/" + Path.GetFileName(path),
            Layout: ModConfigFileLayout.Node,
            Mode: ModConfigWriteMode.Editable,
            State: ModConfigFileState.Editable,
            Fields: fields);

    private static ModConfigFileSnapshot SettingsFile()
        => EditableFile(
            SettingsPath,
            ModConfigTestData.Field("enabled", ModConfigFieldType.Bool, "true"),
            ModConfigTestData.Field("limit", ModConfigFieldType.Int, "10"));

    private static ModConfigFileSnapshot OtherFile()
        => EditableFile(OtherPath, ModConfigTestData.Field("name", value: "old"));

    private static ModConfigModSnapshot WritableMod() => ModConfigTestData.Mod(files: [SettingsFile()]);

    private static ModConfigModSnapshot TwoFileMod() => ModConfigTestData.Mod(files: [SettingsFile(), OtherFile()]);

    private static ModConfigModSnapshot RiskyMod()
        => ModConfigTestData.Mod(files:
            [EditableFile(SettingsPath, ModConfigTestData.Field("pad", value: "1", risk: ModConfigFieldRisk.High))]);

    private static OperationResult<AdapterWriteResult> Wrote(AdapterWriteRequest request, int restartCount = 0)
        => OperationResult<AdapterWriteResult>.Ok(new AdapterWriteResult(
            request.Changes
                .Select(change => new ModConfigAppliedChange(change.FilePath, change.Key, string.Empty, change.NewValue))
                .ToArray(),
            [request.Changes[0].FilePath + ".ckan-ui-20240102-030405.bak"],
            restartCount));

    private static Harness Create(
        IReadOnlyList<ModConfigModSnapshot>? mods = null,
        bool isGameRunning = false,
        bool withWriter = true,
        bool withDialogs = true,
        params bool[] confirmAnswers)
    {
        var model = ModConfigTestData.Model(mods: mods ?? [WritableMod()], isGameRunning: isGameRunning);
        var config = new RecordingConfigService(model);
        var writer = new RecordingWriter();
        var dialogs = withDialogs ? new FakeDialogs(confirmAnswers) : null;
        var restorer = new RecordingRestorer();
        var vm = new ModConfigPageViewModel(
            config,
            writer: withWriter ? writer : null,
            dialogs: dialogs,
            restorer: restorer);
        vm.ApplyModel(model);
        return new Harness(vm, writer, dialogs, restorer, config);
    }

    private sealed class Harness
    {
        public Harness(
            ModConfigPageViewModel vm,
            RecordingWriter writer,
            FakeDialogs? dialogs,
            RecordingRestorer restorer,
            RecordingConfigService config)
        {
            Vm = vm;
            Writer = writer;
            Dialogs = dialogs;
            Restorer = restorer;
            Config = config;
        }

        public ModConfigPageViewModel Vm { get; }

        public RecordingWriter Writer { get; }

        public FakeDialogs? Dialogs { get; }

        public RecordingRestorer Restorer { get; }

        public RecordingConfigService Config { get; }

        public ModConfigFieldRowViewModel Row(string key)
            => Vm.FieldGroups.SelectMany(group => group.Rows).Single(row => row.Key == key);
    }

    private sealed class RecordingConfigService(ModConfigInstanceModel model) : IModConfigService
    {
        public ModConfigInstanceModel Current { get; set; } = model;

        public List<string> Calls { get; } = [];

        public Task<OperationResult<ModConfigInstanceModel>> LoadAsync(string instanceName, bool forceReload = false, CancellationToken cancellationToken = default)
        {
            Calls.Add(instanceName);
            return Task.FromResult(OperationResult<ModConfigInstanceModel>.Ok(Current));
        }
    }

    private sealed class RecordingWriter : IAdapterWriter
    {
        public List<AdapterWriteRequest> Calls { get; } = [];

        public Func<AdapterWriteRequest, OperationResult<AdapterWriteResult>> Responder { get; set; }
            = request => Wrote(request);

        public bool Throws { get; set; }

        public Task<OperationResult<AdapterWriteResult>> WriteAsync(
            AdapterWriteRequest request,
            CancellationToken cancellationToken = default)
        {
            Calls.Add(request);
            if (Throws)
            {
                throw new IOException("磁盘拒绝写入。");
            }

            return Task.FromResult(Responder(request));
        }
    }

    private sealed class RecordingRestorer : IModConfigBackupRestorer
    {
        public List<IReadOnlyList<string>> Calls { get; } = [];

        public bool Succeeds { get; set; } = true;

        public IReadOnlyList<string> Failures { get; set; } = [];

        public Task<OperationResult<ModConfigRestoreResult>> RestoreAsync(
            IReadOnlyList<string> backupPaths,
            CancellationToken cancellationToken = default)
        {
            Calls.Add(backupPaths);
            return Task.FromResult(Succeeds
                ? OperationResult<ModConfigRestoreResult>.Ok(new ModConfigRestoreResult(backupPaths, Failures))
                : OperationResult<ModConfigRestoreResult>.Fail("恢复快照失败，没有文件被还原。", "io"));
        }
    }

    private sealed class FakeDialogs : IDialogService
    {
        private readonly Queue<bool> _answers;

        public FakeDialogs(params bool[] answers) => _answers = new Queue<bool>(answers);

        public List<string> ConfirmationTitles { get; } = [];

        public List<string> ConfirmationMessages { get; } = [];

        public Task<bool> ConfirmAsync(string title, string message, string? detail = null)
        {
            ConfirmationTitles.Add(title);
            ConfirmationMessages.Add(message);
            return Task.FromResult(_answers.Count == 0 || _answers.Dequeue());
        }

        public Task<int> SelectAsync(string title, string message, IReadOnlyList<string> options, int defaultIndex = 0)
            => Task.FromResult(-1);

        public Task ShowMessageAsync(string title, string message) => Task.CompletedTask;

        public Task ShowErrorAsync(string title, string message, string? detail = null) => Task.CompletedTask;

        public Task<OperationResult<bool>> RunWithProgressAsync(
            string title,
            Func<IProgress<double>, CancellationToken, Task> work)
            => Task.FromResult(OperationResult<bool>.Fail("测试里不跑进度对话框。"));
    }
}
