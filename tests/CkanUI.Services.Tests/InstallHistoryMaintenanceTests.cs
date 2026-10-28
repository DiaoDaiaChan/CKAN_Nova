using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CkanUI.Services.Abstractions;
using CkanUI.Services.Abstractions.Dtos;
using CkanUI.Services.History;
using CkanUI.TestSupport;
using NUnit.Framework;

namespace CkanUI.Services.Tests;

/// <summary>
/// 安装历史删除 / 清空用例（12 篇 F-44 的两条剩余缺口）：按时间戳删掉「条目 + 快照」两行、
/// 坏行不受牵连、删不到东西不写盘、清空只清内容不删文件，以及无实例时的中文失败原因。
/// 全部落在临时工作目录，不碰真实实例。
/// </summary>
[TestFixture]
public sealed class InstallHistoryMaintenanceTests
{
    private TempWorkspace _workspace = null!;
    private FakeInstanceService _instances = null!;
    private FakeRegistryService _registry = null!;
    private RecordingChangeSetService _changeSets = null!;
    private RecordingInstallerService _installer = null!;
    private DateTimeOffset _now = new(2024, 5, 1, 10, 0, 0, TimeSpan.Zero);

    private string HistoryFile => Path.Combine(_workspace.Path, "CKAN", InstallHistoryStore.FileName);

    [SetUp]
    public void SetUp()
    {
        _workspace = new TempWorkspace("install-history-maintenance");
        _instances = new FakeInstanceService(_workspace.Path);
        _registry = new FakeRegistryService("测试实例");
        _changeSets = new RecordingChangeSetService();
        _installer = new RecordingInstallerService();
        _now = new DateTimeOffset(2024, 5, 1, 10, 0, 0, TimeSpan.Zero);
    }

    [TearDown]
    public void TearDown() => _workspace.Dispose();

    private DateTimeOffset Next() => _now = _now.AddSeconds(1);

    private InstallHistoryRecorder CreateRecorder() => new(_instances, InstallHistoryStore.DefaultMaxLines, Next);

    private InstallHistoryService CreateService() =>
        new(_instances, _registry, _changeSets, _installer, InstallHistoryStore.DefaultMaxLines, Next);

    /// <summary>写一条带快照的历史（条目行 + 快照行），返回它的时间戳。</summary>
    private DateTimeOffset Seed(InstallHistoryOperation operation = InstallHistoryOperation.Install, string identifier = "A")
    {
        var request = new InstallHistoryRecordRequest(
            operation,
            new[] { new InstallHistoryChange(identifier, ChangeAction.Install, null, "1.0") },
            InstallHistoryOutcome.Succeeded)
        {
            InstalledModules = new[] { new InstalledModuleVersion(identifier, "1.0") },
        };

        var recorded = CreateRecorder().Record(request, request.InstalledModules);
        Assert.That(recorded.IsSuccess, Is.True, recorded.Message);
        return recorded.Value!.Timestamp;
    }

    private string[] RawLines() => File.ReadAllLines(HistoryFile);

    // ---- 存储层：按时间戳删除 -------------------------------------------------

    [Test]
    public void DeleteAt_RemovesEntryAndSnapshotRowsTogether()
    {
        var first = Seed();
        var second = Seed(InstallHistoryOperation.Upgrade);
        var store = new InstallHistoryStore(HistoryFile);
        Assert.That(RawLines(), Has.Length.EqualTo(4));

        var removed = store.DeleteAt(first);

        Assert.Multiple(() =>
        {
            // 一次操作两行（条目 + 快照），删一次必须一起删掉，否则会留下没有归属的快照行。
            Assert.That(removed, Is.EqualTo(2));
            // 剩下的是第二条的「条目 + 快照」，两行时间戳都等于 second。
            Assert.That(store.Read().Select(line => line.At).Distinct().ToArray(), Is.EqualTo(new[] { second }));
            Assert.That(RawLines(), Has.Length.EqualTo(2));
        });
    }

    [Test]
    public void DeleteAt_UnknownTimestamp_ReturnsZeroAndLeavesFileUntouched()
    {
        Seed();
        var before = File.ReadAllBytes(HistoryFile);
        var store = new InstallHistoryStore(HistoryFile);

        var removed = store.DeleteAt(new DateTimeOffset(2030, 1, 1, 0, 0, 0, TimeSpan.Zero));

        Assert.Multiple(() =>
        {
            Assert.That(removed, Is.Zero);
            // 删不到东西就不写盘：避免无意义的重写把文件时间戳和内容都翻一遍。
            Assert.That(File.ReadAllBytes(HistoryFile), Is.EqualTo(before));
        });
    }

    [Test]
    public void DeleteAt_KeepsUnparsableRows()
    {
        var first = Seed();
        File.AppendAllText(HistoryFile, "这是一行坏数据" + Environment.NewLine);
        var store = new InstallHistoryStore(HistoryFile);

        var removed = store.DeleteAt(first);

        Assert.Multiple(() =>
        {
            Assert.That(removed, Is.EqualTo(2));
            // 坏行不是我们写的，静态读取本来就跳过它；删除更不该顺手把它清掉（那是数据修复，需要人来定）。
            Assert.That(RawLines(), Is.EqualTo(new[] { "这是一行坏数据" }));
            Assert.That(store.Read(), Is.Empty);
        });
    }

    [Test]
    public void DeleteAt_OnMissingFile_ReturnsZero()
    {
        var store = new InstallHistoryStore(HistoryFile);

        Assert.Multiple(() =>
        {
            Assert.That(store.DeleteAt(new DateTimeOffset(2024, 5, 1, 10, 0, 1, TimeSpan.Zero)), Is.Zero);
            Assert.That(File.Exists(HistoryFile), Is.False);
        });
    }

    // ---- 存储层：清空 ---------------------------------------------------------

    [Test]
    public void Clear_ReturnsRawLineCountAndEmptiesFile()
    {
        Seed();
        Seed(InstallHistoryOperation.Remove);
        var store = new InstallHistoryStore(HistoryFile);

        var cleared = store.Clear();

        Assert.Multiple(() =>
        {
            Assert.That(cleared, Is.EqualTo(4));
            // 写空文件而不是删文件：同步盘/杀软下删文件更容易失败，且「文件在 = 历史已初始化」的语义要保住。
            Assert.That(File.Exists(HistoryFile), Is.True);
            Assert.That(store.Read(), Is.Empty);
            Assert.That(store.Append(new[]
            {
                InstallHistoryLine.Entry(
                    new DateTimeOffset(2024, 5, 2, 9, 0, 0, TimeSpan.Zero),
                    "测试实例",
                    InstallHistoryOperation.Install,
                    InstallHistoryOutcome.Succeeded,
                    new[] { new InstallHistoryChange("B", ChangeAction.Install, null, "2.0") }),
            }), Is.EqualTo(1));
        });
    }

    [Test]
    public void Clear_OnMissingFile_ReturnsZeroWithoutCreatingFile()
    {
        var store = new InstallHistoryStore(HistoryFile);

        Assert.Multiple(() =>
        {
            Assert.That(store.Clear(), Is.Zero);
            Assert.That(File.Exists(HistoryFile), Is.False);
        });
    }

    // ---- 记录器：没有实例时的中文失败原因 -------------------------------------

    [Test]
    public void Recorder_DeleteAt_WithoutInstance_FailsInChinese()
    {
        _instances.HasCurrent = false;
        var result = CreateRecorder().DeleteAt(new DateTimeOffset(2024, 5, 1, 10, 0, 0, TimeSpan.Zero));

        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.Message, Is.EqualTo("当前没有可用的游戏实例，无法删除安装历史。"));
        });
    }

    [Test]
    public void Recorder_Clear_WithoutInstance_FailsInChinese()
    {
        _instances.HasCurrent = false;
        var result = CreateRecorder().Clear();

        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.Message, Is.EqualTo("当前没有可用的游戏实例，无法清空安装历史。"));
        });
    }

    // ---- 服务层：委托记录器 + 取消语义 ---------------------------------------

    [Test]
    public void Service_DeleteAsync_RemovesOnlyThatEntry()
    {
        var first = Seed();
        Seed(InstallHistoryOperation.Upgrade);
        Seed(InstallHistoryOperation.Remove);
        var service = CreateService();

        var deleted = service.DeleteAsync(first).GetAwaiter().GetResult();

        Assert.That(deleted.IsSuccess, Is.True, deleted.Message);
        Assert.That(deleted.Value, Is.EqualTo(2));

        var remaining = service.LoadAsync().GetAwaiter().GetResult();
        Assert.That(remaining.IsSuccess, Is.True, remaining.Message);
        Assert.That(
            remaining.Value!.Select(entry => entry.Operation),
            Is.EqualTo(new[] { InstallHistoryOperation.Remove, InstallHistoryOperation.Upgrade }));
    }

    [Test]
    public void Service_ClearAsync_ClearsAllEntries()
    {
        Seed();
        Seed(InstallHistoryOperation.Remove);
        var service = CreateService();

        var cleared = service.ClearAsync().GetAwaiter().GetResult();

        Assert.That(cleared.IsSuccess, Is.True, cleared.Message);
        Assert.That(cleared.Value, Is.EqualTo(4));
        Assert.That(service.LoadAsync().GetAwaiter().GetResult().Value, Is.Empty);
    }

    [Test]
    public async Task Service_DeleteAsync_ReportsCancellation()
    {
        Seed();
        var service = CreateService();
        using var source = new CancellationTokenSource();
        await source.CancelAsync();

        var deleted = await service.DeleteAsync(new DateTimeOffset(2024, 5, 1, 10, 0, 1, TimeSpan.Zero), source.Token);

        Assert.Multiple(() =>
        {
            Assert.That(deleted.IsSuccess, Is.False);
            Assert.That(deleted.Message, Is.EqualTo("操作已取消。"));
            Assert.That(deleted.Retryable, Is.True);
            // 取消不能动数据。
            Assert.That(service.LoadAsync().GetAwaiter().GetResult().Value, Has.Count.EqualTo(1));
        });
    }

    [Test]
    public async Task Service_ClearAsync_ReportsCancellation()
    {
        Seed();
        var service = CreateService();
        using var source = new CancellationTokenSource();
        await source.CancelAsync();

        var cleared = await service.ClearAsync(source.Token);

        Assert.Multiple(() =>
        {
            Assert.That(cleared.IsSuccess, Is.False);
            Assert.That(cleared.Message, Is.EqualTo("操作已取消。"));
            Assert.That(service.LoadAsync().GetAwaiter().GetResult().Value, Has.Count.EqualTo(1));
        });
    }

    // ---- 替身 -----------------------------------------------------------------

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

        public FakeRegistryService(string instanceName) => Current = RegistrySnapshot.Empty(instanceName);

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
