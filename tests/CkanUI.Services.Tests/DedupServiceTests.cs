using CkanUI.Services.Abstractions;
using CkanUI.Services.Abstractions.Dtos;
using CkanUI.Services.Dedup;
using CkanUI.Services.Unmanaged;
using CkanUI.TestSupport;
using NUnit.Framework;

namespace CkanUI.Services.Tests;

/// <summary>
/// 跨实例去重（F-41 / UI-21 / INT-09）的服务层测试。
/// 全部在仓库内 <see cref="TempWorkspace"/> 里建真实目录与文件——去重是文件系统操作，
/// 只有真文件才能验证硬链接的身份（inode / 文件索引）。
/// </summary>
[TestFixture]
public sealed class DedupServiceTests
{
    private const string Payload = "content-that-is-identical-across-instances";

    private TempWorkspace _workspace = null!;
    private FakeInstanceService _instances = null!;
    private FakeManagedProvider _managed = null!;
    private FakeErrorReporter _errors = null!;

    [SetUp]
    public void SetUp()
    {
        _workspace = new TempWorkspace("dedup");
        _instances = new FakeInstanceService();
        _managed = new FakeManagedProvider();
        _errors = new FakeErrorReporter();
    }

    [TearDown]
    public void TearDown() => _workspace.Dispose();

    // ── 扫描口径 ────────────────────────────────────────────────────────────

    [Test]
    public void Scan_同一内容跨实例分组并能算出可释放空间()
    {
        var first = AddInstance("甲");
        var second = AddInstance("乙");
        WriteFile(first, "GameData/Squad/Parts/engine.cfg", Payload);
        WriteFile(second, "GameData/Squad/Parts/engine.cfg", Payload);

        var scan = Scan(new DedupScanRequest(Array.Empty<string>()) { MinimumFileSizeBytes = 0 });

        Assert.That(scan.Groups, Has.Count.EqualTo(1));
        var group = scan.Groups[0];
        Assert.That(group.Files, Has.Count.EqualTo(2));
        Assert.That(group.FileCount, Is.EqualTo(2));
        Assert.That(group.SizeBytes, Is.EqualTo(Payload.Length));
        Assert.That(group.ReclaimableBytes, Is.EqualTo(Payload.Length));
        Assert.That(group.InstanceNames, Is.EquivalentTo(new[] { "甲", "乙" }));
        Assert.That(scan.DuplicateFileCount, Is.EqualTo(2));
        Assert.That(scan.ReclaimableBytes, Is.EqualTo(Payload.Length));
        Assert.That(scan.HasDuplicates, Is.True);
        Assert.That(scan.Support, Is.EqualTo(DedupPlatformSupport.Supported));
    }

    [Test]
    public void Scan_同尺寸但内容不同不算重复()
    {
        var first = AddInstance("甲");
        var second = AddInstance("乙");
        WriteFile(first, "GameData/parts/a.cfg", "aaaa");
        WriteFile(second, "GameData/parts/a.cfg", "bbbb");

        var scan = Scan(new DedupScanRequest(Array.Empty<string>()) { MinimumFileSizeBytes = 0 });

        Assert.That(scan.Groups, Is.Empty);
        Assert.That(scan.HasDuplicates, Is.False);
    }

    [Test]
    public void Scan_默认门槛跳过小于128KiB的文件()
    {
        var first = AddInstance("甲");
        var second = AddInstance("乙");
        WriteFile(first, "GameData/tiny.cfg", "tiny");
        WriteFile(second, "GameData/tiny.cfg", "tiny");

        var scan = Scan(new DedupScanRequest(Array.Empty<string>()));

        Assert.That(new DedupScanRequest(Array.Empty<string>()).MinimumFileSizeBytes, Is.EqualTo(128 * 1024));
        Assert.That(scan.Groups, Is.Empty);
        Assert.That(scan.SmallFileCount, Is.EqualTo(2));
    }

    [Test]
    public void Scan_受管路径被跳过()
    {
        var first = AddInstance("甲");
        var second = AddInstance("乙");
        var third = AddInstance("丙");
        WriteFile(first, "GameData/Squad/Parts/a.cfg", Payload);
        WriteFile(second, "GameData/Squad/Parts/a.cfg", Payload);
        WriteFile(third, "GameData/Squad/Parts/a.cfg", Payload);
        _managed.Map["乙"] = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Squad/Parts/a.cfg" };

        var scan = Scan(new DedupScanRequest(Array.Empty<string>()) { MinimumFileSizeBytes = 0 });

        Assert.That(scan.Groups, Has.Count.EqualTo(1));
        Assert.That(scan.Groups[0].Files.Select(file => file.InstanceName), Is.EquivalentTo(new[] { "甲", "丙" }));
        Assert.That(scan.ManagedFileCount, Is.EqualTo(1));
        Assert.That(scan.Skipped.Select(skip => skip.Kind), Does.Contain(DedupSkipKind.Managed));
    }

    [Test]
    public void Scan_符号链接被跳过()
    {
        var first = AddInstance("甲");
        var second = AddInstance("乙");
        var target = WriteFile(first, "GameData/real.bin", Payload);
        WriteFile(second, "GameData/real.bin", Payload);
        var linkPath = Path.Combine(second, "GameData", "link.bin");
        Directory.CreateDirectory(Path.GetDirectoryName(linkPath)!);
        try
        {
            File.CreateSymbolicLink(linkPath, target);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or PlatformNotSupportedException)
        {
            Assert.Ignore($"当前环境不允许创建符号链接：{ex.Message}");
        }

        var scan = Scan(new DedupScanRequest(Array.Empty<string>()) { MinimumFileSizeBytes = 0 });

        Assert.That(scan.LinkFileCount, Is.EqualTo(1));
        Assert.That(scan.Skipped.Select(skip => skip.Kind), Does.Contain(DedupSkipKind.Link));
        Assert.That(scan.Groups.SelectMany(group => group.Files).Select(file => file.RelativePath),
            Has.None.EqualTo("GameData/link.bin"));
    }

    [Test]
    public void Scan_读不到受管清单的实例整实例跳过()
    {
        AddInstance("甲");
        var second = AddInstance("乙");
        WriteFile(second, "GameData/a.cfg", Payload);
        _managed.Failures.Add("乙");

        var scan = Scan(new DedupScanRequest(new[] { "甲", "乙" }) { MinimumFileSizeBytes = 0 });

        Assert.That(scan.InstanceNames, Is.EquivalentTo(new[] { "甲", "乙" }));
        Assert.That(scan.Skipped.Select(skip => skip.Kind), Does.Contain(DedupSkipKind.Unreadable));
        Assert.That(scan.Skipped.Any(skip => skip.Reason.Contains("受管文件清单", StringComparison.Ordinal)), Is.True);
    }

    [Test]
    public void Scan_只扫描请求里点名的实例()
    {
        var first = AddInstance("甲");
        var second = AddInstance("乙");
        WriteFile(first, "GameData/a.cfg", Payload);
        WriteFile(second, "GameData/a.cfg", Payload);

        var scan = Scan(new DedupScanRequest(new[] { "甲" }) { MinimumFileSizeBytes = 0 });

        Assert.That(scan.InstanceNames, Is.EquivalentTo(new[] { "甲" }));
        Assert.That(scan.Groups, Is.Empty);
    }

    // ── 执行口径 ────────────────────────────────────────────────────────────

    [Test]
    public void Execute_默认为预演且不写盘()
    {
        var first = AddInstance("甲");
        var second = AddInstance("乙");
        var keep = WriteFile(first, "GameData/a.cfg", Payload);
        var target = WriteFile(second, "GameData/a.cfg", Payload);
        var linker = new RecordingLinker();
        var scan = ScanWith(linker, new DedupScanRequest(Array.Empty<string>()) { MinimumFileSizeBytes = 0 });
        var request = DedupExecuteRequest.From(scan);

        Assert.That(request.DryRun, Is.True, "执行请求默认必须是预演");
        var report = ExecuteWith(linker, request);

        Assert.That(report.DryRun, Is.True);
        Assert.That(report.LinkedCount, Is.Zero);
        Assert.That(report.PlannedCount, Is.EqualTo(1));
        Assert.That(report.FreedBytes, Is.EqualTo(Payload.Length));
        Assert.That(linker.CreateLinkCalls, Is.Zero, "预演不允许创建任何链接");
        Assert.That(File.ReadAllText(keep), Is.EqualTo(Payload));
        Assert.That(File.ReadAllText(target), Is.EqualTo(Payload));
    }

    [Test]
    public void Execute_写盘后两路径内容一致且文件标识相同()
    {
        var native = NativeDedupLinker.Instance;
        if (native.Support != DedupPlatformSupport.Supported)
        {
            Assert.Ignore("当前平台不支持硬链接。");
        }

        var first = AddInstance("甲");
        var second = AddInstance("乙");
        var keep = WriteFile(first, "GameData/a.cfg", Payload);
        var target = WriteFile(second, "GameData/a.cfg", Payload);
        var scan = ScanWith(native, new DedupScanRequest(Array.Empty<string>()) { MinimumFileSizeBytes = 0 });
        var request = DedupExecuteRequest.From(scan, dryRun: false);

        var report = ExecuteWith(native, request);

        Assert.That(report.HasFailures, Is.False, string.Join("；", report.Results.Select(r => r.Message)));
        Assert.That(report.LinkedCount, Is.EqualTo(1));
        Assert.That(report.FreedBytes, Is.EqualTo(Payload.Length));
        Assert.That(File.ReadAllText(keep), Is.EqualTo(Payload));
        Assert.That(File.ReadAllText(target), Is.EqualTo(Payload));

        if (native.TryGetIdentity(keep, out var keepIdentity) && native.TryGetIdentity(target, out var targetIdentity))
        {
            Assert.That(targetIdentity.SameAs(keepIdentity), Is.True, "写盘后两个路径必须指向同一个文件（同一 inode / 文件索引）");
        }
        else
        {
            Assert.Ignore("当前平台读不到文件标识（非 Windows），只验证了内容一致。");
        }
    }

    [Test]
    public void Execute_单文件失败不影响其它组()
    {
        var good = AddInstance("好实例");
        var alsoGood = AddInstance("也是好实例");
        var bad = AddInstance("坏实例");
        WriteFile(good, "GameData/a.cfg", Payload);
        WriteFile(alsoGood, "GameData/a.cfg", Payload);
        WriteFile(bad, "GameData/b.cfg", "another-payload");
        WriteFile(alsoGood, "GameData/b.cfg", "another-payload");

        var linker = new RecordingLinker
        {
            FailWhen = linkPath => Path.GetDirectoryName(linkPath)!.Contains("坏实例", StringComparison.Ordinal),
        };
        var scan = ScanWith(linker, new DedupScanRequest(Array.Empty<string>()) { MinimumFileSizeBytes = 0 });
        Assert.That(scan.Groups, Has.Count.EqualTo(2), "需要两组才能验证「一组失败不拖累另一组」");

        var report = ExecuteWith(linker, DedupExecuteRequest.From(scan, dryRun: false));

        Assert.That(report.HasFailures, Is.True);
        Assert.That(report.LinkedCount, Is.GreaterThanOrEqualTo(1), "另一组必须仍然建链成功");
        Assert.That(report.Results.Where(result => result.Outcome == DedupLinkOutcome.Failed), Is.Not.Empty);
        Assert.That(report.Results.Any(result => result.Message.Contains("权限不足", StringComparison.Ordinal)), Is.True,
            string.Join("；", report.Results.Select(result => result.Message)));
    }

    [Test]
    public void Execute_跨卷的文件被跳过而不是抛异常()
    {
        var first = AddInstance("甲");
        var keep = WriteFile(first, "GameData/a.cfg", Payload);
        var target = WriteFile(first, "GameData/b/a.cfg", Payload);
        var linker = new RecordingLinker();
        var group = new DedupGroup(
            "hash",
            Payload.Length,
            "另一个卷",
            new[]
            {
                new DedupCandidateFile("甲", "GameData/a.cfg", keep, Payload.Length, "另一个卷", false),
                new DedupCandidateFile("甲", "GameData/b/a.cfg", target, Payload.Length, "另一个卷", false),
            });
        var contexts = new Dictionary<string, DedupInstanceContext>(StringComparer.Ordinal)
        {
            ["甲"] = new("甲", Path.Combine(first, "GameData"), new HashSet<string>(StringComparer.OrdinalIgnoreCase)),
        };

        var report = new DedupExecutor(linker).Execute(
            new DedupExecuteRequest(new[] { group }) { DryRun = false }, contexts);

        Assert.That(report.HasFailures, Is.False);
        Assert.That(report.LinkedCount, Is.Zero);
        Assert.That(report.Results.Any(result => result.Message.Contains("跨卷", StringComparison.Ordinal)), Is.True);
        Assert.That(linker.CreateLinkCalls, Is.Zero);
    }

    [Test]
    public void Execute_平台不支持硬链接时返回不支持结果且不写盘()
    {
        var first = AddInstance("甲");
        var second = AddInstance("乙");
        WriteFile(first, "GameData/a.cfg", Payload);
        WriteFile(second, "GameData/a.cfg", Payload);
        var linker = new RecordingLinker { Support = DedupPlatformSupport.UnsupportedPlatform };
        var scan = ScanWith(linker, new DedupScanRequest(Array.Empty<string>()) { MinimumFileSizeBytes = 0 });

        var report = ExecuteWith(linker, DedupExecuteRequest.From(scan, dryRun: false));

        Assert.That(report.IsUnsupported, Is.True);
        Assert.That(report.LinkedCount, Is.Zero);
        Assert.That(report.FreedBytes, Is.Zero);
        Assert.That(report.Results.All(result => result.Message.Contains("不支持", StringComparison.Ordinal)), Is.True);
        Assert.That(linker.CreateLinkCalls, Is.Zero);
    }

    [Test]
    public void Execute_已硬链接的文件不会再被处理()
    {
        var native = NativeDedupLinker.Instance;
        if (native.Support != DedupPlatformSupport.Supported)
        {
            Assert.Ignore("当前平台不支持硬链接。");
        }

        var first = AddInstance("甲");
        var keep = WriteFile(first, "GameData/a.cfg", Payload);
        var duplicate = Path.Combine(first, "GameData", "b.cfg");
        native.CreateLink(duplicate, keep);

        var scan = ScanWith(native, new DedupScanRequest(Array.Empty<string>()) { MinimumFileSizeBytes = 0 });

        Assert.That(scan.Groups, Is.Empty, "已经是同一份文件（同 inode）的一对不应该再算重复");
    }

    // ── 辅助 ────────────────────────────────────────────────────────────────

    private string AddInstance(string name)
    {
        var gameDir = _workspace.Combine(name);
        Directory.CreateDirectory(Path.Combine(gameDir, "GameData"));
        _instances.Add(new InstanceInfo(name, gameDir, "1.12.5", false, false, 3));
        return gameDir;
    }

    private static string WriteFile(string gameDir, string relativePath, string content)
    {
        var path = Path.Combine(gameDir, relativePath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
        return path;
    }

    private DedupScanReport Scan(DedupScanRequest request) => ScanWith(NativeDedupLinker.Instance, request);

    private DedupScanReport ScanWith(IDedupLinker linker, DedupScanRequest request)
    {
        var service = new InstanceDedupService(_instances, _managed, linker, _errors);
        var result = service.ScanAsync(request).GetAwaiter().GetResult();
        Assert.That(result.IsSuccess, Is.True, result.Message);
        return result.Value!;
    }

    private DedupExecuteReport ExecuteWith(IDedupLinker linker, DedupExecuteRequest request) =>
        ExecuteAsync(linker, request).GetAwaiter().GetResult();

    private async Task<DedupExecuteReport> ExecuteAsync(IDedupLinker linker, DedupExecuteRequest request)
    {
        var service = new InstanceDedupService(_instances, _managed, linker, _errors);
        var result = await service.ExecuteAsync(request).ConfigureAwait(false);
        Assert.That(result.IsSuccess, Is.True, result.Message);
        return result.Value!;
    }

    private sealed class FakeInstanceService : IInstanceService
    {
        private readonly List<InstanceInfo> _instances = new();

        public IReadOnlyList<InstanceInfo> Instances => _instances;

        public InstanceInfo? Current => _instances.Count == 0 ? null : _instances[0];

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

        public void Add(InstanceInfo instance) => _instances.Add(instance);

        public Task<OperationResult<IReadOnlyList<InstanceInfo>>> RefreshAsync(CancellationToken ct = default)
            => Task.FromResult(OperationResult<IReadOnlyList<InstanceInfo>>.Ok(_instances.ToArray()));

        public Task<OperationResult<InstanceInfo>> SwitchAsync(string instanceName, CancellationToken ct = default)
            => Task.FromResult(OperationResult<InstanceInfo>.Fail("测试替身不支持切换实例。"));

        public Task<OperationResult<InstanceInfo>> AddAsync(string path, string? name = null, CancellationToken ct = default)
            => Task.FromResult(OperationResult<InstanceInfo>.Fail("测试替身不支持添加实例。"));

        public Task<OperationResult<InstanceInfo>> CloneAsync(string sourceName, string newName, CancellationToken ct = default)
            => Task.FromResult(OperationResult<InstanceInfo>.Fail("测试替身不支持克隆实例。"));

        public Task<OperationResult<bool>> RemoveAsync(string instanceName, CancellationToken ct = default)
            => Task.FromResult(OperationResult<bool>.Fail("测试替身不支持移除实例。"));

        public Task<OperationResult<InstanceInfo>> RenameAsync(string instanceName, string newName, CancellationToken ct = default)
            => Task.FromResult(OperationResult<InstanceInfo>.Fail("测试替身不支持重命名实例。"));
    }

    private sealed class FakeManagedProvider : IManagedPathProvider
    {
        public Dictionary<string, HashSet<string>> Map { get; } = new(StringComparer.Ordinal);

        public HashSet<string> Failures { get; } = new(StringComparer.Ordinal);

        public OperationResult<IReadOnlySet<string>> GetManagedPaths(InstanceInfo instance)
        {
            if (Failures.Contains(instance.Name))
            {
                return OperationResult<IReadOnlySet<string>>.Fail("测试故意让受管清单读失败。");
            }

            return OperationResult<IReadOnlySet<string>>.Ok(
                Map.TryGetValue(instance.Name, out var paths)
                    ? paths
                    : new HashSet<string>(StringComparer.OrdinalIgnoreCase));
        }
    }

    private sealed class FakeErrorReporter : IErrorReporter
    {
        public event Action<ErrorEntry>? Reported
        {
            add { }
            remove { }
        }

        public List<ErrorEntry> Entries { get; } = new();

        public IReadOnlyList<ErrorEntry> Recent => Entries;

        public void Report(Exception exception, string context, string? userMessage = null) =>
            Entries.Add(new ErrorEntry(DateTime.UtcNow, context, userMessage ?? exception.Message, exception.ToString(), exception));

        public void Report(string context, string userMessage, string? detail = null) =>
            Entries.Add(new ErrorEntry(DateTime.UtcNow, context, userMessage, detail, null));
    }

    /// <summary>记录调用并可注入失败的链接器（成功时委托给真实实现）。</summary>
    private sealed class RecordingLinker : IDedupLinker
    {
        public DedupPlatformSupport Support { get; set; } = NativeDedupLinker.Instance.Support;

        public Func<string, bool>? FailWhen { get; set; }

        public int CreateLinkCalls { get; private set; }

        public bool TryGetIdentity(string path, out DedupFileIdentity identity) =>
            NativeDedupLinker.Instance.TryGetIdentity(path, out identity);

        public void CreateLink(string linkPath, string existingPath)
        {
            CreateLinkCalls++;
            if (FailWhen is { } predicate && predicate(linkPath))
            {
                throw new UnauthorizedAccessException("测试注入的权限不足。");
            }

            NativeDedupLinker.Instance.CreateLink(linkPath, existingPath);
        }
    }
}
