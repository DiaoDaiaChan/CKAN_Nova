using CkanUI.Services.Abstractions;
using CkanUI.Services.Abstractions.Dtos;
using CkanUI.Services.History;
using CkanUI.TestSupport;

using NUnit.Framework;

namespace CkanUI.Services.Tests;

/// <summary>
/// <see cref="InstallHistoryService"/> 的离线用例（12 篇 F-44 / 13 篇 UI-23）：
/// 落盘格式、损坏行跳过、滚动截断、"从历史重装"必须走变更集 + 安装流水线。
/// </summary>
[TestFixture]
public sealed class InstallHistoryServiceTests
{
    private TempWorkspace _workspace = null!;
    private FakeInstanceService _instances = null!;
    private FakeRegistryService _registry = null!;
    private RecordingChangeSetService _changeSets = null!;
    private RecordingInstallerService _installer = null!;
    private InstallHistoryService _service = null!;
    private DateTimeOffset _now = new(2024, 5, 1, 10, 0, 0, TimeSpan.Zero);

    [SetUp]
    public void SetUp()
    {
        _workspace = new TempWorkspace("install-history");
        _instances = new FakeInstanceService(_workspace.Path);
        _registry = new FakeRegistryService("测试实例");
        _changeSets = new RecordingChangeSetService();
        _installer = new RecordingInstallerService();
        _service = new InstallHistoryService(_instances, _registry, _changeSets, _installer, 2000, Next);
    }

    [TearDown]
    public void TearDown() => _workspace.Dispose();

    private DateTimeOffset Next() => _now = _now.AddSeconds(1);

    private string FilePath => _workspace.Combine("CKAN", "install_history.jsonl");

    private static InstallHistoryRecordRequest Request(
        InstallHistoryOperation operation = InstallHistoryOperation.Install,
        string identifier = "A")
        => new(
            operation,
            new[] { new InstallHistoryChange(identifier, ChangeAction.Install, null, "1.0") },
            InstallHistoryOutcome.Succeeded);

    /// <summary>写一条带指定快照的历史（快照内容显式给定，不受当前注册表影响）。</summary>
    private void SeedSnapshot(params (string Identifier, string Version)[] modules)
        => _service.RecordAsync(
            new InstallHistoryRecordRequest(
                InstallHistoryOperation.Install,
                modules.Select(module => new InstallHistoryChange(
                    module.Identifier,
                    ChangeAction.Install,
                    null,
                    module.Version)).ToArray(),
                InstallHistoryOutcome.Succeeded)
            {
                InstalledModules = modules
                    .Select(module => new InstalledModuleVersion(module.Identifier, module.Version))
                    .ToList(),
            }).GetAwaiter().GetResult();

    private static IReadOnlyList<InstallHistoryEntry> Load(InstallHistoryService service)
    {
        var result = service.LoadAsync().GetAwaiter().GetResult();
        Assert.That(result.IsSuccess, Is.True, result.Message);
        return result.Value!;
    }

    [Test]
    public void Record_WritesOneEntryAndOneSnapshotLine_UnderCkanDirectory()
    {
        _registry.Set("A", "1.0");

        var recorded = _service.RecordAsync(Request()).GetAwaiter().GetResult();

        Assert.That(recorded.IsSuccess, Is.True, recorded.Message);
        Assert.That(File.Exists(FilePath), Is.True, "历史必须落在 <GameDir>/CKAN/install_history.jsonl");
        var lines = File.ReadAllLines(FilePath);
        Assert.That(lines.Length, Is.EqualTo(2), "一次操作 = entry 行 + snapshot 行");
        Assert.That(lines[0], Does.Contain("\"kind\":\"entry\""));
        Assert.That(lines[1], Does.Contain("\"kind\":\"snapshot\""));
    }

    [Test]
    public void LoadAsync_ReturnsEntriesNewestFirst_AndMarksInstalledAgainstCurrentRegistry()
    {
        _registry.Set("A", "1.0");
        SeedSnapshot(("A", "1.0"));
        _registry.Set("A", "2.0");
        SeedSnapshot(("A", "2.0"));

        var entries = Load(_service);

        Assert.That(entries.Count, Is.EqualTo(2));
        Assert.That(entries[0].Timestamp, Is.GreaterThan(entries[1].Timestamp), "最新的在前面");
        Assert.That(entries[0].Snapshot, Is.Not.Null);
        var first = entries[0].Snapshot!.Modules.Single();
        Assert.That(first.Identifier, Is.EqualTo("A"));
        Assert.That(first.IsInstalled, Is.True);
        Assert.That(first.InstalledVersion, Is.EqualTo("2.0"));
        // 旧快照里写的是 1.0，当前是 2.0 → 版本不同。
        var old = entries[1].Snapshot!.Modules.Single();
        Assert.That(old.Version, Is.EqualTo("1.0"));
        Assert.That(old.IsChanged, Is.True);
    }

    [Test]
    public void LoadAsync_SkipsCorruptedLines_AndKeepsThemOnDisk()
    {
        SeedSnapshot(("A", "1.0"));
        File.AppendAllText(FilePath, "这不是 JSON" + Environment.NewLine);
        File.AppendAllText(FilePath, "{\"kind\":\"wat\",\"at\":\"2024-05-01T10:00:00.0000000+00:00\"}" + Environment.NewLine);
        File.AppendAllText(FilePath, "{\"kind\":\"entry\"}" + Environment.NewLine);
        SeedSnapshot(("B", "3.0"));

        var entries = Load(_service);

        Assert.That(entries.Count, Is.EqualTo(2), "坏行跳过但不抛，两条正常历史都要读到");
        Assert.That(entries.Select(entry => entry.Snapshot!.Modules.Single().Identifier),
            Is.EquivalentTo(new[] { "A", "B" }));
        var raw = File.ReadAllText(FilePath);
        Assert.That(raw, Does.Contain("这不是 JSON"), "读不清的行必须原样保留，便于人工排查");
    }

    [Test]
    public void Append_RollsOver_WhenLineCountExceedsTheCap()
    {
        var service = new InstallHistoryService(_instances, _registry, _changeSets, _installer, maxLines: 8, Next);
        for (var index = 0; index < 10; index++)
        {
            var recorded = service.RecordAsync(Request(identifier: "M" + index)).GetAwaiter().GetResult();
            Assert.That(recorded.IsSuccess, Is.True, recorded.Message);
        }

        var lines = File.ReadAllLines(FilePath);
        Assert.That(lines.Length, Is.EqualTo(8), "超过上限必须滚动截断到上限");
        var entries = Load(service);
        Assert.That(entries.Count, Is.EqualTo(8));
        Assert.That(entries[0].Changes.Single().Identifier, Is.EqualTo("M9"), "保留最新的记录");
        Assert.That(entries.Any(entry => entry.Changes.Single().Identifier == "M0"), Is.False, "最旧的被截掉");
        Assert.That(entries.Any(entry => entry.Changes.Single().Identifier == "M1"), Is.False);
    }

    [Test]
    public void LoadAsync_WithoutInstance_Fails()
    {
        _instances.HasCurrent = false;
        var service = new InstallHistoryService(_instances, _registry, _changeSets, _installer);

        var result = service.LoadAsync().GetAwaiter().GetResult();

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Message, Does.Contain("没有可用的游戏实例"));
        Assert.That(service.HasActiveInstance, Is.False);
        Assert.That(service.InstanceName, Is.Null);
    }

    [Test]
    public void PreviewRestore_SkipsModulesAlreadyAtTheSnapshotVersion()
    {
        _registry.Set("A", "1.0");
        SeedSnapshot(("A", "1.0"), ("B", "2.0"));
        _changeSets.Result = OperationResult<ChangeSetPreview>.Ok(new ChangeSetPreview(
            new[] { new ChangeItemDto("B", "B", ChangeAction.Install, null, "2.0", ChangeOrigin.UserRequested, 1024) },
            Array.Empty<string>(),
            1024,
            Array.Empty<string>()));
        var target = Load(_service)[0].Timestamp;

        var preview = _service.PreviewRestoreAsync(new InstallHistoryRestoreRequest(target)).GetAwaiter().GetResult();

        Assert.That(preview.IsSuccess, Is.True, preview.Message);
        Assert.That(preview.Value!.ChangeCount, Is.EqualTo(1));
        Assert.That(_changeSets.Received.Select(request => request.Identifier), Is.EqualTo(new[] { "B" }),
            "已同版本的 A 不进入变更集（变更数要诚实）");
        Assert.That(_changeSets.Received.Single().TargetVersion, Is.EqualTo("2.0"));
        Assert.That(_installer.ApplyCount, Is.Zero, "预览绝不安装");
    }

    [Test]
    public void Restore_GoesThroughChangeSetAndInstaller_ThenRecordsItsOwnHistory()
    {
        _registry.Set("A", "1.0");
        SeedSnapshot(("A", "1.0"), ("B", "2.0"));
        _changeSets.Result = OperationResult<ChangeSetPreview>.Ok(new ChangeSetPreview(
            new[] { new ChangeItemDto("B", "B", ChangeAction.Install, null, "2.0", ChangeOrigin.UserRequested, 2048) },
            Array.Empty<string>(),
            2048,
            Array.Empty<string>()));
        _installer.Result = OperationResult<InstallReport>.Ok(new InstallReport(
            new[] { "B" },
            Array.Empty<string>(),
            Array.Empty<string>(),
            Array.Empty<string>()));
        var target = Load(_service)[0].Timestamp;
        var reports = new List<OperationProgress>();

        var restored = _service
            .RestoreAsync(new InstallHistoryRestoreRequest(target), new RecordingProgress(reports))
            .GetAwaiter().GetResult();

        Assert.That(restored.IsSuccess, Is.True, restored.Message);
        Assert.That(restored.Value!.Installed, Is.EqualTo(new[] { "B" }));
        Assert.That(restored.Value.IsFullSuccess, Is.True);
        Assert.That(_installer.ApplyCount, Is.EqualTo(1));
        Assert.That(_installer.Received!.Items.Single().Identifier, Is.EqualTo("B"), "交给安装器的是变更集");

        var entries = Load(_service);
        Assert.That(entries[0].Operation, Is.EqualTo(InstallHistoryOperation.Restore));
        Assert.That(entries[0].Outcome, Is.EqualTo(InstallHistoryOutcome.Succeeded));
        Assert.That(entries[0].Message, Does.Contain("历史快照重装"));
    }

    [Test]
    public void Restore_WithConflicts_Aborts_WithoutCallingInstaller()
    {
        SeedSnapshot(("A", "1.0"));
        _changeSets.Result = OperationResult<ChangeSetPreview>.Ok(new ChangeSetPreview(
            Array.Empty<ChangeItemDto>(),
            new[] { "A 需要 B，但 B 不可用" },
            0,
            Array.Empty<string>()));
        var target = Load(_service)[0].Timestamp;

        var restored = _service.RestoreAsync(new InstallHistoryRestoreRequest(target)).GetAwaiter().GetResult();

        Assert.That(restored.IsSuccess, Is.False);
        Assert.That(restored.Message, Does.Contain("依赖冲突"));
        Assert.That(restored.Retryable, Is.True);
        Assert.That(_installer.ApplyCount, Is.Zero, "有冲突时绝不能静默安装");
    }

    [Test]
    public void Restore_WhenAlreadyIdentical_IsEmpty_AndSkipsChangeSetAndInstaller()
    {
        _registry.Set("A", "1.0");
        SeedSnapshot(("A", "1.0"));
        var target = Load(_service)[0].Timestamp;

        var restored = _service.RestoreAsync(new InstallHistoryRestoreRequest(target)).GetAwaiter().GetResult();

        Assert.That(restored.IsSuccess, Is.True, restored.Message);
        Assert.That(restored.Value!.Plan.IsEmpty, Is.True);
        Assert.That(_changeSets.BuildCount, Is.Zero);
        Assert.That(_installer.ApplyCount, Is.Zero);
    }

    [Test]
    public void Restore_UnknownTimestamp_Fails()
    {
        SeedSnapshot(("A", "1.0"));

        var restored = _service
            .RestoreAsync(new InstallHistoryRestoreRequest(DateTimeOffset.UnixEpoch))
            .GetAwaiter()
            .GetResult();

        Assert.That(restored.IsSuccess, Is.False);
        Assert.That(restored.Message, Does.Contain("找不到该时间点"));
    }

    [Test]
    public void Restore_WhenInstallerFails_ReturnsRetryableFailure()
    {
        SeedSnapshot(("A", "1.0"));
        _changeSets.Result = OperationResult<ChangeSetPreview>.Ok(new ChangeSetPreview(
            new[] { new ChangeItemDto("A", "A", ChangeAction.Install, null, "1.0", ChangeOrigin.UserRequested, 0) },
            Array.Empty<string>(),
            0,
            Array.Empty<string>()));
        _installer.Result = OperationResult<InstallReport>.Fail("下载失败：连接被重置。", retryable: true);
        var target = Load(_service)[0].Timestamp;

        var restored = _service.RestoreAsync(new InstallHistoryRestoreRequest(target)).GetAwaiter().GetResult();

        Assert.That(restored.IsSuccess, Is.False);
        Assert.That(restored.Message, Is.EqualTo("下载失败：连接被重置。"));
        Assert.That(restored.Retryable, Is.True);
    }

    [Test]
    public void HistoryRecordingInstaller_RecordsEveryApply_WithInferredOperation()
    {
        _registry.Set("A", "1.0");
        var recorder = new InstallHistoryRecorder(_instances, 2000, Next);
        var recording = new HistoryRecordingInstaller(_installer, recorder, _registry);
        var changeSet = new ChangeSetPreview(
            new[]
            {
                new ChangeItemDto("A", "A", ChangeAction.Upgrade, "1.0", "2.0", ChangeOrigin.UserRequested, 0),
            },
            Array.Empty<string>(),
            0,
            Array.Empty<string>());

        var applied = recording.ApplyAsync(changeSet).GetAwaiter().GetResult();

        Assert.That(applied.IsSuccess, Is.True, applied.Message);
        Assert.That(_installer.ApplyCount, Is.EqualTo(1));
        var entries = Load(_service);
        Assert.That(entries.Count, Is.EqualTo(1));
        Assert.That(entries[0].Operation, Is.EqualTo(InstallHistoryOperation.Upgrade), "全 Upgrade → 记成升级");
        Assert.That(entries[0].Changes.Single().ToVersion, Is.EqualTo("2.0"));
        Assert.That(entries[0].Snapshot, Is.Not.Null, "每次操作都带一份当时的已装集合快照");
    }

    [Test]
    public void HistoryRecordingInstaller_InsideRestore_DoesNotRecordTwice()
    {
        // 装饰器装在服务内部：重装只该留下一条 Restore 历史，装饰器不得再记一条 Install。
        _registry.Set("A", "1.0");
        var recorder = new InstallHistoryRecorder(_instances, 2000, Next);
        var recording = new HistoryRecordingInstaller(_installer, recorder, _registry);
        var service = new InstallHistoryService(recorder, _registry, _changeSets, recording);

        var seeded = service.RecordAsync(Request()).GetAwaiter().GetResult();
        Assert.That(seeded.IsSuccess, Is.True, seeded.Message);
        var target = Load(service)[0].Timestamp;

        _registry.Set("A", "2.0");
        _changeSets.Result = OperationResult<ChangeSetPreview>.Ok(new ChangeSetPreview(
            new[] { new ChangeItemDto("A", "A", ChangeAction.Install, null, "1.0", ChangeOrigin.UserRequested, 0) },
            Array.Empty<string>(),
            0,
            Array.Empty<string>()));

        var restored = service.RestoreAsync(new InstallHistoryRestoreRequest(target)).GetAwaiter().GetResult();

        Assert.That(restored.IsSuccess, Is.True, restored.Message);
        Assert.That(_installer.ApplyCount, Is.EqualTo(1));
        var entries = Load(service);
        Assert.That(entries.Count, Is.EqualTo(2), "只有原本那条 + 重装那条，装饰器在抑制范围内不得再记");
        Assert.That(entries[0].Operation, Is.EqualTo(InstallHistoryOperation.Restore));
    }

    [Test]
    public void Record_WhenTargetPathIsUnusable_FailsWithRetryableMessage()
    {
        // 在 <ws>/blocked 处放一个文件，于是 <ws>/blocked/CKAN 目录无法创建 —— 模拟不可写路径。
        var blocked = _workspace.Combine("blocked");
        File.WriteAllText(blocked, "占位文件");
        var missing = new InstallHistoryService(
            new FakeInstanceService(blocked),
            _registry,
            _changeSets,
            _installer);

        var recorded = missing.RecordAsync(Request()).GetAwaiter().GetResult();

        Assert.That(recorded.IsSuccess, Is.False);
        Assert.That(recorded.Retryable, Is.True);
        Assert.That(recorded.Message, Does.Contain("写入安装历史失败"));
    }

    private sealed class RecordingProgress : IProgress<OperationProgress>
    {
        private readonly List<OperationProgress> _sink;

        public RecordingProgress(List<OperationProgress> sink) => _sink = sink;

        public void Report(OperationProgress value) => _sink.Add(value);
    }

    private sealed class FakeInstanceService : IInstanceService
    {
        private readonly InstanceInfo _info;

        public FakeInstanceService(string gameDir)
        {
            _info = new InstanceInfo("测试实例", gameDir, "1.12.5", true, false, 2);
            Current = _info;
        }

        /// <summary>关掉后 <see cref="Current"/> 变 null，模拟"没有选中实例"。</summary>
        public bool HasCurrent
        {
            get => Current is not null;
            set => Current = value ? _info : null;
        }

        public IReadOnlyList<InstanceInfo> Instances => Current is null
            ? Array.Empty<InstanceInfo>()
            : new[] { _info };

        public InstanceInfo? Current { get; set; }

        public event Action? InstancesChanged
        {
            add { }
            remove { }
        }

        public event Action<InstanceInfo?>? CurrentChanged
        {
            add { }
            remove { }
        }

        public Task<OperationResult<IReadOnlyList<InstanceInfo>>> RefreshAsync(CancellationToken ct = default)
            => Task.FromResult(OperationResult<IReadOnlyList<InstanceInfo>>.Ok(Instances));

        public Task<OperationResult<InstanceInfo>> SwitchAsync(string instanceName, CancellationToken ct = default)
            => Task.FromResult(OperationResult<InstanceInfo>.Fail("测试替身不支持切换实例。"));

        public Task<OperationResult<InstanceInfo>> AddAsync(string path, string? name = null, CancellationToken ct = default)
            => Task.FromResult(OperationResult<InstanceInfo>.Fail("测试替身不支持新增实例。"));

        public Task<OperationResult<InstanceInfo>> CloneAsync(string sourceName, string newName, CancellationToken ct = default)
            => Task.FromResult(OperationResult<InstanceInfo>.Fail("测试替身不支持克隆实例。"));

        public Task<OperationResult<bool>> RemoveAsync(string instanceName, CancellationToken ct = default)
            => Task.FromResult(OperationResult<bool>.Fail("测试替身不支持删除实例。"));

        public Task<OperationResult<InstanceInfo>> RenameAsync(string instanceName, string newName, CancellationToken ct = default)
            => Task.FromResult(OperationResult<InstanceInfo>.Fail("测试替身不支持重命名实例。"));
    }

    private sealed class FakeRegistryService : IRegistryService
    {
        private readonly Dictionary<string, InstalledModuleInfo> _installed = new(StringComparer.Ordinal);

        public FakeRegistryService(string instanceName)
            => Current = RegistrySnapshot.Empty(instanceName);

        public RegistrySnapshot Current { get; private set; }

        public event Action<RegistrySnapshot>? Changed
        {
            add { }
            remove { }
        }

        public void Set(string identifier, string version)
        {
            _installed[identifier] = new InstalledModuleInfo(identifier, version, false, null, DateTime.UtcNow);
            Current = new RegistrySnapshot(
                Current.InstanceName,
                Current.RegistryVersion,
                new Dictionary<string, InstalledModuleInfo>(_installed, StringComparer.Ordinal),
                new HashSet<string>(StringComparer.OrdinalIgnoreCase),
                DateTime.UtcNow);
        }

        public Task<OperationResult<RegistrySnapshot>> OpenAsync(InstanceInfo instance, CancellationToken ct = default)
            => Task.FromResult(OperationResult<RegistrySnapshot>.Ok(Current));

        public Task<OperationResult<bool>> CloseAsync(CancellationToken ct = default)
            => Task.FromResult(OperationResult<bool>.Ok(true));

        public Task<OperationResult<RegistrySnapshot>> RefreshAsync(CancellationToken ct = default)
            => Task.FromResult(OperationResult<RegistrySnapshot>.Ok(Current));
    }
}
