using CkanUI.Services.Abstractions;
using CkanUI.Services.Abstractions.Dtos;
using CkanUI.Services.ModPacks;
using CkanUI.TestSupport;

using NUnit.Framework;

namespace CkanUI.Services.Tests;

/// <summary>
/// <see cref="ModPackService"/> 的离线用例（12 篇 F-39/F-40）：导入必须先预览再执行、
/// 执行必须复用 <see cref="IInstallerService.ApplyAsync"/>、冲突不得静默安装、取消与失败都要落在中文文案上。
/// </summary>
[TestFixture]
public sealed class ModPackServiceTests
{
    private TempWorkspace _workspace = null!;
    private FakeModPackStore _store = null!;
    private RecordingChangeSetService _changeSets = null!;
    private RecordingInstallerService _installer = null!;
    private ModPackService _service = null!;

    [SetUp]
    public void SetUp()
    {
        _workspace = new TempWorkspace("modpack-service");
        _store = new FakeModPackStore();
        _changeSets = new RecordingChangeSetService();
        _installer = new RecordingInstallerService();
        _service = new ModPackService(new ModPackCatalog(_store), _changeSets, _installer);
    }

    [TearDown]
    public void TearDown() => _workspace.Dispose();

    [Test]
    public void Export_NoActiveInstance_FailsRetryable()
    {
        _store.HasActiveInstance = false;

        var result = _service.ExportAsync(new ModPackExportRequest(_workspace.Combine("out.ckan"))).GetAwaiter().GetResult();

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Message, Is.EqualTo(ModPackCatalog.NoInstanceMessage));
        Assert.That(result.Retryable, Is.True);
    }

    [Test]
    public void Export_Succeeds_AndReportsProgress()
    {
        _store.SeedInstalled("A", "1.0");
        var reports = new List<OperationProgress>();

        var result = _service.ExportAsync(
            new ModPackExportRequest(_workspace.Combine("out.ckan")),
            new SynchronousProgress(reports)).GetAwaiter().GetResult();

        Assert.That(result.IsSuccess, Is.True, result.Message);
        Assert.That(File.Exists(result.Value!.Path), Is.True);
        Assert.That(reports, Is.Not.Empty);
        Assert.That(reports[^1].State, Is.EqualTo(OperationState.Succeeded));
        Assert.That(reports[^1].Percent, Is.EqualTo(100d));
    }

    [Test]
    public void Export_FailureOnBadPath_DoesNotThrow()
    {
        _store.SeedInstalled("A", "1.0");

        var result = _service.ExportAsync(new ModPackExportRequest("   ")).GetAwaiter().GetResult();

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Message, Is.EqualTo("导出路径不能为空。"));
    }

    [Test]
    public void PreviewImport_NoSources_Fails()
    {
        var result = _service.PreviewImportAsync(Array.Empty<string>()).GetAwaiter().GetResult();

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Message, Is.EqualTo("请至少选择一个要导入的 .ckan 文件。"));
    }

    [Test]
    public void Import_NullPreview_Fails()
    {
        var result = _service.ImportAsync(null!).GetAwaiter().GetResult();

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Message, Does.Contain("请先选择 .ckan 文件"));
        Assert.That(_installer.ApplyCount, Is.Zero);
    }

    [Test]
    public void Import_NoSelectableEntries_Fails()
    {
        var preview = PreviewOf(Entry("A", ModPackEntryState.SameVersion));

        var result = _service.ImportAsync(preview).GetAwaiter().GetResult();

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Message, Is.EqualTo("没有勾选任何可安装的条目。"));
        Assert.That(_changeSets.BuildCount, Is.Zero);
    }

    [Test]
    public void Import_SelectionFiltersEntries()
    {
        var preview = PreviewOf(
            Entry("A", ModPackEntryState.Install),
            Entry("B", ModPackEntryState.Install));

        var result = _service.ImportAsync(preview, new[] { "B" }).GetAwaiter().GetResult();

        Assert.That(result.IsSuccess, Is.True, result.Message);
        Assert.That(_changeSets.Received.Select(request => request.Identifier), Is.EqualTo(new[] { "B" }));
        Assert.That(_installer.ApplyCount, Is.EqualTo(1));
    }

    [Test]
    public void Import_MapsEntryStatesToChangeActions()
    {
        var preview = PreviewOf(
            Entry("New", ModPackEntryState.Install, target: "1.0"),
            Entry("Up", ModPackEntryState.Upgrade, installed: "1.0", target: "2.0"),
            Entry("Down", ModPackEntryState.Downgrade, installed: "2.0", target: "1.5"));

        var result = _service.ImportAsync(preview).GetAwaiter().GetResult();

        Assert.That(result.IsSuccess, Is.True, result.Message);
        var actions = _changeSets.Received.ToDictionary(request => request.Identifier, request => request.Action);
        Assert.That(actions["New"], Is.EqualTo(ChangeAction.Install));
        Assert.That(actions["Up"], Is.EqualTo(ChangeAction.Upgrade));
        Assert.That(actions["Down"], Is.EqualTo(ChangeAction.Downgrade));
        Assert.That(_changeSets.Received.Single(r => r.Identifier == "Up").TargetVersion, Is.EqualTo("2.0"));
    }

    [Test]
    public void Import_BuildFailure_PropagatesMessage()
    {
        _changeSets.Result = OperationResult<ChangeSetPreview>.Fail("依赖求解失败。", "缺失 X", retryable: true);

        var result = _service.ImportAsync(PreviewOf(Entry("A", ModPackEntryState.Install))).GetAwaiter().GetResult();

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Message, Is.EqualTo("依赖求解失败。"));
        Assert.That(result.Detail, Is.EqualTo("缺失 X"));
        Assert.That(_installer.ApplyCount, Is.Zero);
    }

    [Test]
    public void Import_ResolverConflicts_AbortsWithoutInstalling()
    {
        _changeSets.Result = OperationResult<ChangeSetPreview>.Ok(new ChangeSetPreview(
            Array.Empty<ChangeItemDto>(), new[] { "A 与 B 冲突" }, 0, Array.Empty<string>()));

        var result = _service.ImportAsync(PreviewOf(Entry("A", ModPackEntryState.Install))).GetAwaiter().GetResult();

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Message, Is.EqualTo("依赖求解发现冲突，已取消导入。"));
        Assert.That(result.Detail, Does.Contain("A 与 B 冲突"));
        Assert.That(_installer.ApplyCount, Is.Zero, "有冲突时必须中止，绝不能静默安装。");
    }

    [Test]
    public void Import_AppliesBuiltChangeSetAsIs()
    {
        var built = new ChangeSetPreview(
            new[] { new ChangeItemDto("A", "A", ChangeAction.Install, null, "1.0", ChangeOrigin.UserRequested, 1024) },
            Array.Empty<string>(), 1024, Array.Empty<string>());
        _changeSets.Result = OperationResult<ChangeSetPreview>.Ok(built);

        var result = _service.ImportAsync(PreviewOf(Entry("A", ModPackEntryState.Install))).GetAwaiter().GetResult();

        Assert.That(result.IsSuccess, Is.True, result.Message);
        Assert.That(_installer.Received, Is.SameAs(built), "执行必须复用变更集服务产出的预览，不能自己拼一份。");
    }

    [Test]
    public void Import_SuccessReport_MergesWarnings()
    {
        var preview = new ModPackPreview(
            new[] { "a.ckan" }, "a.ckan",
            new[] { Entry("A", ModPackEntryState.Install) },
            Array.Empty<string>(),
            new[] { "预览警告" });
        _changeSets.Result = OperationResult<ChangeSetPreview>.Ok(new ChangeSetPreview(
            Array.Empty<ChangeItemDto>(), Array.Empty<string>(), 0, new[] { "求解警告" }));
        _installer.Result = OperationResult<InstallReport>.Ok(new InstallReport(
            new[] { "A" }, Array.Empty<string>(), Array.Empty<string>(), new[] { "安装警告" }));

        var result = _service.ImportAsync(preview).GetAwaiter().GetResult();

        Assert.That(result.IsSuccess, Is.True, result.Message);
        Assert.That(result.Value!.Installed, Is.EqualTo(new[] { "A" }));
        Assert.That(result.Value.IsFullSuccess, Is.True);
        Assert.That(result.Value.Warnings, Is.EqualTo(new[] { "预览警告", "求解警告", "安装警告" }));
    }

    [Test]
    public void Import_InstallerFailure_PropagatesMessage()
    {
        _installer.Result = OperationResult<InstallReport>.Fail("安装被取消。", retryable: true);

        var result = _service.ImportAsync(PreviewOf(Entry("A", ModPackEntryState.Install))).GetAwaiter().GetResult();

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Message, Is.EqualTo("安装被取消。"));
        Assert.That(result.Retryable, Is.True);
    }

    [Test]
    public void Import_PartialFailureReport_KeepsFailedList()
    {
        _installer.Result = OperationResult<InstallReport>.Ok(new InstallReport(
            new[] { "A" }, Array.Empty<string>(), new[] { "B" }, Array.Empty<string>()));

        var result = _service.ImportAsync(PreviewOf(
            Entry("A", ModPackEntryState.Install),
            Entry("B", ModPackEntryState.Install))).GetAwaiter().GetResult();

        Assert.That(result.IsSuccess, Is.True, result.Message);
        Assert.That(result.Value!.IsFullSuccess, Is.False);
        Assert.That(result.Value.Failed, Is.EqualTo(new[] { "B" }));
    }

    [Test]
    public void Import_Cancelled_ReturnsRetryableCancellation()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var result = _service.ImportAsync(
            PreviewOf(Entry("A", ModPackEntryState.Install)),
            selectedIdentifiers: null,
            progress: null,
            ct: cts.Token).GetAwaiter().GetResult();

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Message, Is.EqualTo("操作已取消。"));
    }

    [Test]
    public void Compare_TwoInstances_ReturnsDiff()
    {
        _store.SeedInstance("A", ("Shared", "1.0"));
        _store.SeedInstance("B", ("Shared", "2.0"));

        var result = _service.CompareAsync("A", "B").GetAwaiter().GetResult();

        Assert.That(result.IsSuccess, Is.True, result.Message);
        Assert.That(result.Value!.Items.Single().Kind, Is.EqualTo(InstanceDiffKind.VersionDiffers));
    }

    [Test]
    public void Compare_UnknownInstance_FailsWithoutThrowing()
    {
        _store.SeedInstance("A", ("Shared", "1.0"));

        var result = _service.CompareAsync("A", "Nope").GetAwaiter().GetResult();

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Message, Does.Contain("Nope"));
    }

    private static ModPackPreview PreviewOf(params ModPackEntry[] entries)
        => new(new[] { "f.ckan" }, "f.ckan", entries, Array.Empty<string>(), Array.Empty<string>());

    private static ModPackEntry Entry(
        string identifier,
        ModPackEntryState state,
        string? requested = "1.0",
        string? installed = null,
        string? target = "1.0")
        => new(identifier, "f.ckan", requested, installed, target, state, null);

    private sealed class SynchronousProgress : IProgress<OperationProgress>
    {
        private readonly List<OperationProgress> _sink;

        public SynchronousProgress(List<OperationProgress> sink) => _sink = sink;

        public void Report(OperationProgress value) => _sink.Add(value);
    }
}

/// <summary>变更集服务替身：记录收到的请求，返回可配置的结果。</summary>
internal sealed class RecordingChangeSetService : IChangeSetService
{
    public OperationResult<ChangeSetPreview> Result { get; set; } = OperationResult<ChangeSetPreview>.Ok(
        new ChangeSetPreview(Array.Empty<ChangeItemDto>(), Array.Empty<string>(), 0, Array.Empty<string>()));

    public List<ChangeRequest> Received { get; } = new();

    public int BuildCount { get; private set; }

    public Task<OperationResult<ChangeSetPreview>> BuildAsync(
        IReadOnlyCollection<ChangeRequest> requests,
        CancellationToken ct = default)
    {
        BuildCount++;
        Received.Clear();
        Received.AddRange(requests);
        return Task.FromResult(Result);
    }

    public OperationResult<ChangeSetPreview> BuildLocal(IReadOnlyCollection<ChangeRequest> requests)
    {
        Received.Clear();
        Received.AddRange(requests);
        return Result;
    }
}

/// <summary>安装器替身：记录收到的变更集与调用次数。</summary>
internal sealed class RecordingInstallerService : IInstallerService
{
    public OperationResult<InstallReport> Result { get; set; } = OperationResult<InstallReport>.Ok(
        new InstallReport(Array.Empty<string>(), Array.Empty<string>(), Array.Empty<string>(), Array.Empty<string>()));

    public ChangeSetPreview? Received { get; private set; }

    public int ApplyCount { get; private set; }

    public Task<OperationResult<InstallReport>> ApplyAsync(
        ChangeSetPreview changeSet,
        IProgress<OperationProgress>? progress = null,
        CancellationToken ct = default)
    {
        ApplyCount++;
        Received = changeSet;
        return Task.FromResult(Result);
    }
}
