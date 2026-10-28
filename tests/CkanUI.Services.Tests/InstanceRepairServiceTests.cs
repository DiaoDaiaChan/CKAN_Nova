using System.Text.Json;

using CkanUI.Services.Abstractions.Instances;
using CkanUI.Services.Instances;
using CkanUI.TestSupport;
using NUnit.Framework;

namespace CkanUI.Services.Tests;

/// <summary>
/// 12 篇 F-08：实例自检 / 强制解锁 / 注册表隔离 / 版本过旧判定。
/// <para>
/// 全部用例只碰临时目录，不依赖真实进程表：进程探测用替身注入，时钟固定，操作记录写到临时文件。
/// </para>
/// </summary>
[TestFixture]
public sealed class InstanceRepairServiceTests
{
    private static readonly DateTimeOffset FixedNow = new(2025, 1, 2, 3, 4, 5, TimeSpan.FromHours(8));

    private static Func<DateTimeOffset> FixedClock => static () => FixedNow;

    // ---------- 自检 ----------

    [Test]
    public async Task Diagnose_ReportsLockFileWithPathAndTimestamp()
    {
        using var workspace = new TempWorkspace("instance-repair-lock");
        var (gameDir, ckanDir) = CreateInstance(workspace);
        var lockPath = Path.Combine(ckanDir, "registry.locked");
        File.WriteAllText(lockPath, string.Empty);
        var service = NewService(workspace, new FakeProcessProbe());

        var result = await service.DiagnoseAsync(new InstanceRepairTarget("KSP", gameDir, "1.12.3"));

        Assert.That(result.IsSuccess, Is.True, result.Message);
        var diagnosis = result.Value!;
        Assert.That(diagnosis.IsLocked, Is.True);
        Assert.That(diagnosis.CanForceUnlock, Is.True);
        Assert.That(diagnosis.Locks[0].Path, Is.EqualTo(lockPath));
        Assert.That(diagnosis.Locks[0].Kind, Is.EqualTo(InstanceLockKind.RegistryLockFile));
        Assert.That(diagnosis.Locks[0].LastWriteTimeUtc, Is.Not.Null);
        Assert.That(diagnosis.Locks[0].OccupantHint, Does.Contain("未定位到占用进程"));
        Assert.That(diagnosis.Issues.Select(static issue => issue.Kind),
            Does.Contain(InstanceIssueKind.RegistryLocked));
        Assert.That(diagnosis.Headline, Does.Contain("1 个问题"));
    }

    [Test]
    public async Task Diagnose_GivesCkanProcessHintWhenProbeFindsOne()
    {
        using var workspace = new TempWorkspace("instance-repair-hint");
        var (gameDir, ckanDir) = CreateInstance(workspace);
        File.WriteAllText(Path.Combine(ckanDir, "registry.locked"), string.Empty);
        var ckanExe = Path.Combine("C:", "Program Files", "CKAN", "CKAN-GUI.exe");
        var service = NewService(workspace, new FakeProcessProbe(ckanExe));

        var result = await service.DiagnoseAsync(new InstanceRepairTarget("KSP", gameDir, "1.12.3"));

        Assert.That(result.IsSuccess, Is.True, result.Message);
        Assert.That(result.Value!.Locks[0].OccupantHint, Does.Contain("CKAN-GUI"));
        Assert.That(result.Value!.Locks[0].OccupantHint, Does.Contain(ckanExe));
    }

    [Test]
    public async Task Diagnose_IsHealthyWhenRegistryParsesAndNoLock()
    {
        using var workspace = new TempWorkspace("instance-repair-healthy");
        var (gameDir, ckanDir) = CreateInstance(workspace);
        File.WriteAllText(Path.Combine(ckanDir, "registry.json"), "{\"registry_version\":3}");
        var service = NewService(workspace, new FakeProcessProbe());

        var result = await service.DiagnoseAsync(new InstanceRepairTarget("KSP", gameDir, "1.12.3"));

        Assert.That(result.IsSuccess, Is.True, result.Message);
        Assert.That(result.Value!.IsHealthy, Is.True);
        Assert.That(result.Value!.RegistryReadable, Is.True);
        Assert.That(result.Value!.RegistryVersion, Is.EqualTo(3));
        Assert.That(result.Value!.Headline, Does.Contain("自检通过"));
    }

    [Test]
    public async Task Diagnose_FlagsRegistryWrittenByNewerCkan()
    {
        using var workspace = new TempWorkspace("instance-repair-newer");
        var (gameDir, ckanDir) = CreateInstance(workspace);
        File.WriteAllText(Path.Combine(ckanDir, "registry.json"), "{\"registry_version\":9}");
        var service = NewService(workspace, new FakeProcessProbe());

        var result = await service.DiagnoseAsync(new InstanceRepairTarget("KSP", gameDir, "1.12.3"));

        Assert.That(result.IsSuccess, Is.True, result.Message);
        var issue = result.Value!.Issues.Single(static item => item.Kind == InstanceIssueKind.RegistryFromNewerCkan);
        Assert.That(issue.Message, Does.Contain("registry_version=9"));
        Assert.That(issue.FixHint, Does.Contain("升级 CKAN"));
    }

    [Test]
    public async Task Diagnose_FlagsCorruptRegistryWithoutTouchingTheFile()
    {
        using var workspace = new TempWorkspace("instance-repair-corrupt");
        var (gameDir, ckanDir) = CreateInstance(workspace);
        var registryPath = Path.Combine(ckanDir, "registry.json");
        File.WriteAllText(registryPath, "{ not json at all ");
        var service = NewService(workspace, new FakeProcessProbe());

        var result = await service.DiagnoseAsync(new InstanceRepairTarget("KSP", gameDir, "1.12.3"));

        Assert.That(result.IsSuccess, Is.True, result.Message);
        Assert.That(result.Value!.HasCorruptedRegistry, Is.True);
        Assert.That(result.Value!.RegistryExists, Is.True);
        Assert.That(result.Value!.RegistryError, Is.Not.Null.And.Not.Empty);
        Assert.That(File.ReadAllText(registryPath), Is.EqualTo("{ not json at all "));
    }

    [Test]
    public async Task Diagnose_FlagsGameVersionBelowDeclaredCompatibleVersion()
    {
        using var workspace = new TempWorkspace("instance-repair-old-ksp");
        var (gameDir, ckanDir) = CreateInstance(workspace);
        File.WriteAllText(Path.Combine(ckanDir, "registry.json"),
            "{\"registry_version\":3,\"compatible_ksp_version\":\"1.12.3\"}");
        var service = NewService(workspace, new FakeProcessProbe());

        var result = await service.DiagnoseAsync(new InstanceRepairTarget("KSP", gameDir, "1.11.2"));

        Assert.That(result.IsSuccess, Is.True, result.Message);
        Assert.That(result.Value!.RequiredKspVersion, Is.EqualTo("1.12.3"));
        var issue = result.Value!.Issues.Single(static item => item.Kind == InstanceIssueKind.GameVersionTooOld);
        Assert.That(issue.Message, Does.Contain("1.12.3"));
        Assert.That(issue.Message, Does.Contain("1.11.2"));
    }

    [Test]
    public async Task Diagnose_AcceptsEqualGameVersion()
    {
        using var workspace = new TempWorkspace("instance-repair-same-ksp");
        var (gameDir, ckanDir) = CreateInstance(workspace);
        File.WriteAllText(Path.Combine(ckanDir, "registry.json"),
            "{\"registry_version\":3,\"compatible_ksp_version\":\"1.12.3\"}");
        var service = NewService(workspace, new FakeProcessProbe());

        var result = await service.DiagnoseAsync(new InstanceRepairTarget("KSP", gameDir, "1.12.3.1234"));

        Assert.That(result.IsSuccess, Is.True, result.Message);
        Assert.That(result.Value!.Issues.Select(static issue => issue.Kind),
            Has.No.Member(InstanceIssueKind.GameVersionTooOld));
    }

    [Test]
    public async Task Diagnose_FlagsMissingGameDirectory()
    {
        using var workspace = new TempWorkspace("instance-repair-missing-dir");
        var service = NewService(workspace, new FakeProcessProbe());
        var missing = workspace.Combine("Gone");

        var result = await service.DiagnoseAsync(new InstanceRepairTarget("KSP", missing, null));

        Assert.That(result.IsSuccess, Is.True, result.Message);
        Assert.That(result.Value!.GameDirectoryExists, Is.False);
        Assert.That(result.Value!.Issues.Select(static issue => issue.Kind),
            Does.Contain(InstanceIssueKind.GameDirectoryMissing));
    }

    [Test]
    public async Task Diagnose_RejectsNullTarget()
    {
        using var workspace = new TempWorkspace("instance-repair-null");
        var service = NewService(workspace, new FakeProcessProbe());

        var result = await service.DiagnoseAsync(null!);

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Message, Does.Contain("必须提供"));
    }

    [Test]
    public async Task Diagnose_SurvivesThrowingProcessProbe()
    {
        using var workspace = new TempWorkspace("instance-repair-throwing-probe");
        var (gameDir, ckanDir) = CreateInstance(workspace);
        File.WriteAllText(Path.Combine(ckanDir, "registry.locked"), string.Empty);
        var service = NewService(workspace, new ThrowingProcessProbe());

        var result = await service.DiagnoseAsync(new InstanceRepairTarget("KSP", gameDir, "1.12.3"));

        Assert.That(result.IsSuccess, Is.True, result.Message);
        Assert.That(result.Value!.IsLocked, Is.True);
        Assert.That(result.Value!.IsGameRunning, Is.False);
    }

    // ---------- 强制解锁 ----------

    [Test]
    public async Task ForceUnlock_DeletesLockAndLeavesJournalRecord()
    {
        using var workspace = new TempWorkspace("instance-repair-unlock");
        var (gameDir, ckanDir) = CreateInstance(workspace);
        var lockPath = Path.Combine(ckanDir, "registry.locked");
        File.WriteAllText(lockPath, string.Empty);
        var journalPath = workspace.Combine("journal", "instance-repair.jsonl");
        var service = NewService(workspace, new FakeProcessProbe(), journalPath);

        var result = await service.ForceUnlockAsync(new InstanceRepairTarget("KSP", gameDir, "1.12.3"), "测试原因");

        Assert.That(result.IsSuccess, Is.True, result.Message);
        Assert.That(File.Exists(lockPath), Is.False);
        var report = result.Value!;
        Assert.That(report.RemovedPaths, Is.EqualTo(new[] { lockPath }));
        Assert.That(report.SkippedPaths, Is.Empty);
        Assert.That(report.JournalWritten, Is.True);
        Assert.That(report.JournalPath, Is.EqualTo(journalPath));
        Assert.That(report.Timestamp, Is.EqualTo(FixedNow));
        Assert.That(report.Message, Does.Contain("强制").Or.Contain("已删除"));
        var journal = File.ReadAllText(journalPath);
        Assert.That(journal, Does.Contain("force-unlock"));
        using var document = JsonDocument.Parse(journal);
        var entry = document.RootElement;
        Assert.That(entry.GetProperty("Instance").GetString(), Is.EqualTo("KSP"));
        Assert.That(entry.GetProperty("Reason").GetString(), Is.EqualTo("测试原因"));
        Assert.That(entry.GetProperty("Action").GetString(), Is.EqualTo("force-unlock"));
        Assert.That(entry.GetProperty("Paths")[0].GetString(), Is.EqualTo(lockPath));
    }

    [Test]
    public async Task ForceUnlock_RefusesWhileGameIsRunning()
    {
        using var workspace = new TempWorkspace("instance-repair-unlock-running");
        var (gameDir, ckanDir) = CreateInstance(workspace);
        var lockPath = Path.Combine(ckanDir, "registry.locked");
        File.WriteAllText(lockPath, string.Empty);
        var gameExe = Path.Combine(gameDir, "KSP_x64.exe");
        var service = NewService(workspace, new FakeProcessProbe(gameExe));

        var result = await service.ForceUnlockAsync(new InstanceRepairTarget("KSP", gameDir, "1.12.3"));

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Message, Does.Contain("游戏正在运行"));
        Assert.That(result.Retryable, Is.True);
        Assert.That(File.Exists(lockPath), Is.True);
    }

    [Test]
    public async Task ForceUnlock_RefusesWhenNothingIsLocked()
    {
        using var workspace = new TempWorkspace("instance-repair-unlock-none");
        var (gameDir, _) = CreateInstance(workspace);
        var service = NewService(workspace, new FakeProcessProbe());

        var result = await service.ForceUnlockAsync(new InstanceRepairTarget("KSP", gameDir, "1.12.3"));

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Message, Does.Contain("无需解锁"));
    }

    [Test]
    public async Task ForceUnlock_DeletesEmptyLockDirectory()
    {
        using var workspace = new TempWorkspace("instance-repair-unlock-dir");
        var (gameDir, ckanDir) = CreateInstance(workspace);
        var lockDir = Path.Combine(ckanDir, "registry.locked");
        Directory.CreateDirectory(lockDir);
        var service = NewService(workspace, new FakeProcessProbe());

        var result = await service.ForceUnlockAsync(new InstanceRepairTarget("KSP", gameDir, "1.12.3"));

        Assert.That(result.IsSuccess, Is.True, result.Message);
        Assert.That(Directory.Exists(lockDir), Is.False);
    }

    [Test]
    public async Task ForceUnlock_NeverRecursivelyDeletesNonEmptyLockDirectory()
    {
        using var workspace = new TempWorkspace("instance-repair-unlock-dir-full");
        var (gameDir, ckanDir) = CreateInstance(workspace);
        var lockDir = Path.Combine(ckanDir, "registry.locked");
        Directory.CreateDirectory(lockDir);
        var inside = Path.Combine(lockDir, "user-file.txt");
        File.WriteAllText(inside, "keep me");
        var service = NewService(workspace, new FakeProcessProbe());

        var result = await service.ForceUnlockAsync(new InstanceRepairTarget("KSP", gameDir, "1.12.3"));

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Detail, Does.Contain("非空锁目录"));
        Assert.That(File.ReadAllText(inside), Is.EqualTo("keep me"));
    }

    [Test]
    public async Task ForceUnlock_SucceedsEvenWhenJournalCannotBeWritten()
    {
        using var workspace = new TempWorkspace("instance-repair-unlock-journal-fail");
        var (gameDir, ckanDir) = CreateInstance(workspace);
        File.WriteAllText(Path.Combine(ckanDir, "registry.locked"), string.Empty);
        var blocker = workspace.Combine("blocker");
        File.WriteAllText(blocker, "not a directory");
        var service = NewService(workspace, new FakeProcessProbe(), Path.Combine(blocker, "journal.jsonl"));

        var result = await service.ForceUnlockAsync(new InstanceRepairTarget("KSP", gameDir, "1.12.3"));

        Assert.That(result.IsSuccess, Is.True, result.Message);
        Assert.That(result.Value!.JournalWritten, Is.False);
        Assert.That(File.Exists(Path.Combine(ckanDir, "registry.locked")), Is.False);
    }

    [Test]
    public void ForceUnlock_HonoursPreCancelledToken()
    {
        using var workspace = new TempWorkspace("instance-repair-unlock-cancel");
        var (gameDir, ckanDir) = CreateInstance(workspace);
        File.WriteAllText(Path.Combine(ckanDir, "registry.locked"), string.Empty);
        var service = NewService(workspace, new FakeProcessProbe());
        using var source = new CancellationTokenSource();
        source.Cancel();

        var result = service.ForceUnlockAsync(new InstanceRepairTarget("KSP", gameDir, "1.12.3"), null, source.Token)
            .GetAwaiter().GetResult();

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Message, Is.EqualTo("操作已取消。"));
        Assert.That(File.Exists(Path.Combine(ckanDir, "registry.locked")), Is.True);
    }

    // ---------- 注册表隔离 ----------

    [Test]
    public async Task Quarantine_RenamesCorruptRegistryAndKeepsContent()
    {
        using var workspace = new TempWorkspace("instance-repair-quarantine");
        var (gameDir, ckanDir) = CreateInstance(workspace);
        var registryPath = Path.Combine(ckanDir, "registry.json");
        const string corrupt = "{ broken json";
        File.WriteAllText(registryPath, corrupt);
        var service = NewService(workspace, new FakeProcessProbe());

        var result = await service.QuarantineRegistryAsync(new InstanceRepairTarget("KSP", gameDir, "1.12.3"));

        Assert.That(result.IsSuccess, Is.True, result.Message);
        var quarantine = result.Value!;
        Assert.That(quarantine.OriginalPath, Is.EqualTo(registryPath));
        Assert.That(File.Exists(registryPath), Is.False);
        Assert.That(quarantine.QuarantinePath, Does.Contain("registry.json.corrupt-"));
        Assert.That(File.Exists(quarantine.QuarantinePath), Is.True);
        Assert.That(File.ReadAllText(quarantine.QuarantinePath), Is.EqualTo(corrupt));
        Assert.That(quarantine.Bytes, Is.GreaterThan(0));
        Assert.That(quarantine.JournalWritten, Is.True);
        Assert.That(quarantine.Message, Does.Contain("空注册表"));
    }

    [Test]
    public async Task Quarantine_ProducesUniqueNamesForSameTimestamp()
    {
        using var workspace = new TempWorkspace("instance-repair-quarantine-twice");
        var (gameDir, ckanDir) = CreateInstance(workspace);
        var registryPath = Path.Combine(ckanDir, "registry.json");
        var service = NewService(workspace, new FakeProcessProbe());

        File.WriteAllText(registryPath, "{ first ");
        var first = await service.QuarantineRegistryAsync(new InstanceRepairTarget("KSP", gameDir, "1.12.3"));
        File.WriteAllText(registryPath, "{ second ");
        var second = await service.QuarantineRegistryAsync(new InstanceRepairTarget("KSP", gameDir, "1.12.3"));

        Assert.That(first.IsSuccess, Is.True, first.Message);
        Assert.That(second.IsSuccess, Is.True, second.Message);
        Assert.That(first.Value!.QuarantinePath, Is.Not.EqualTo(second.Value!.QuarantinePath));
        Assert.That(File.ReadAllText(first.Value!.QuarantinePath), Is.EqualTo("{ first "));
        Assert.That(File.ReadAllText(second.Value!.QuarantinePath), Is.EqualTo("{ second "));
    }

    [Test]
    public async Task Quarantine_RefusesHealthyRegistry()
    {
        using var workspace = new TempWorkspace("instance-repair-quarantine-healthy");
        var (gameDir, ckanDir) = CreateInstance(workspace);
        var registryPath = Path.Combine(ckanDir, "registry.json");
        File.WriteAllText(registryPath, "{\"registry_version\":3}");
        var service = NewService(workspace, new FakeProcessProbe());

        var result = await service.QuarantineRegistryAsync(new InstanceRepairTarget("KSP", gameDir, "1.12.3"));

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Message, Does.Contain("不需要隔离"));
        Assert.That(File.Exists(registryPath), Is.True);
    }

    [Test]
    public async Task Quarantine_RefusesMissingRegistry()
    {
        using var workspace = new TempWorkspace("instance-repair-quarantine-missing");
        var (gameDir, _) = CreateInstance(workspace);
        var service = NewService(workspace, new FakeProcessProbe());

        var result = await service.QuarantineRegistryAsync(new InstanceRepairTarget("KSP", gameDir, "1.12.3"));

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Message, Does.Contain("没有 registry.json"));
    }

    // ---------- 版本兼容判定 ----------

    [Test]
    public async Task CheckCompatibility_AdvisesUpgradingCkan()
    {
        using var workspace = new TempWorkspace("instance-repair-advice-ckan");
        var (gameDir, ckanDir) = CreateInstance(workspace);
        File.WriteAllText(Path.Combine(ckanDir, "registry.json"), "{\"registry_version\":4}");
        var service = NewService(workspace, new FakeProcessProbe());

        var result = await service.CheckCompatibilityAsync(new InstanceRepairTarget("KSP", gameDir, "1.12.3"));

        Assert.That(result.IsSuccess, Is.True, result.Message);
        Assert.That(result.Value!.CkanTooOld, Is.True);
        Assert.That(result.Value!.IsCompatible, Is.False);
        Assert.That(result.Value!.Advice, Does.Contain("升级 CKAN"));
    }

    [Test]
    public async Task CheckCompatibility_AdvisesUpgradingGame()
    {
        using var workspace = new TempWorkspace("instance-repair-advice-game");
        var (gameDir, ckanDir) = CreateInstance(workspace);
        File.WriteAllText(Path.Combine(ckanDir, "registry.json"),
            "{\"registry_version\":3,\"compatible_ksp_version\":\"1.12.3\"}");
        var service = NewService(workspace, new FakeProcessProbe());

        var result = await service.CheckCompatibilityAsync(new InstanceRepairTarget("KSP", gameDir, "1.10.1"));

        Assert.That(result.IsSuccess, Is.True, result.Message);
        Assert.That(result.Value!.GameVersionTooOld, Is.True);
        Assert.That(result.Value!.Advice, Does.Contain("1.12.3"));
    }

    [Test]
    public async Task CheckCompatibility_IsCompatibleWhenRegistryIsAbsent()
    {
        using var workspace = new TempWorkspace("instance-repair-advice-none");
        var (gameDir, _) = CreateInstance(workspace);
        var service = NewService(workspace, new FakeProcessProbe());

        var result = await service.CheckCompatibilityAsync(new InstanceRepairTarget("KSP", gameDir, "1.12.3"));

        Assert.That(result.IsSuccess, Is.True, result.Message);
        Assert.That(result.Value!.IsCompatible, Is.True);
        Assert.That(result.Value!.Advice, Is.Empty);
        Assert.That(result.Value!.SupportedRegistryVersion, Is.EqualTo(InstanceRepairService.SupportedRegistryVersion));
    }

    // ---------- 辅助 ----------

    private static InstanceRepairService NewService(TempWorkspace workspace, IGameProcessProbe probe,
        string? journalPath = null) =>
        new(probe, FixedClock, journalPath ?? workspace.Combine("instance-repair-journal.jsonl"));

    private static (string GameDir, string CkanDir) CreateInstance(TempWorkspace workspace)
    {
        var gameDir = workspace.Combine("KSP");
        var ckanDir = Path.Combine(gameDir, "CKAN");
        Directory.CreateDirectory(ckanDir);
        return (gameDir, ckanDir);
    }

    private sealed class FakeProcessProbe : IGameProcessProbe
    {
        private readonly IReadOnlyList<string> _paths;

        public FakeProcessProbe(params string[] paths) => _paths = paths;

        public IReadOnlyList<string> GetProcessImagePaths() => _paths;
    }

    private sealed class ThrowingProcessProbe : IGameProcessProbe
    {
        public IReadOnlyList<string> GetProcessImagePaths() =>
            throw new InvalidOperationException("探测失败（测试替身）");
    }
}
