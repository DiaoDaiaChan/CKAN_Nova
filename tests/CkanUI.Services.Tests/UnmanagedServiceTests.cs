using CkanUI.Services.Abstractions;
using CkanUI.Services.Abstractions.Dtos;
using CkanUI.Services.Unmanaged;
using CkanUI.TestSupport;
using NUnit.Framework;

namespace CkanUI.Services.Tests;

/// <summary>
/// 未管理文件服务（12 篇 F-42 / UI-10）：扫描正确性、受管路径拒删、dry-run 不落盘、可重试失败路径、空 GameData。
/// </summary>
/// <remarks>全部用 <see cref="TempWorkspace"/> 造假 GameData（04 篇 §3：测试不碰系统临时目录，也不碰真实 KSP 实例）。</remarks>
[TestFixture]
public sealed class UnmanagedServiceTests
{
    [Test]
    public async Task ScanAsync_ListsUnmanagedContentWithManagedCounts()
    {
        using var workspace = PrepareGameData("unmanaged-scan");
        Write(workspace.Path, "ManagedMod/ManagedMod.dll");
        Write(workspace.Path, "ManagedMod/ManagedMod.version");
        Write(workspace.Path, "Leftover/old.txt", "abc");
        Write(workspace.Path, "Mixed/managed.dll");
        Write(workspace.Path, "Mixed/junk.txt", "zzz");
        Write(workspace.Path, "Stray/JunkMod.dll", "dll");
        Write(workspace.Path, "loose.cfg", "cfg");

        var provider = new FakeManagedProvider();
        provider.Managed.Add("ManagedMod/ManagedMod.dll");
        provider.Managed.Add("ManagedMod/ManagedMod.version");
        provider.Managed.Add("Mixed/managed.dll");
        var service = new UnmanagedService(provider, new FakeRegistrar(), new RecordingErrorReporter());

        var result = await service.ScanAsync(Instance(workspace.Path));

        Assert.That(result.IsSuccess, Is.True, result.Message);
        var entries = result.Value!;
        Assert.That(
            entries.Select(entry => entry.RelativePath),
            Is.EquivalentTo(new[]
            {
                "Leftover", "Leftover/old.txt", "Mixed", "Mixed/junk.txt", "Stray", "Stray/JunkMod.dll", "loose.cfg",
            }));
        Assert.That(entries.Select(entry => entry.RelativePath), Does.Not.Contain("ManagedMod"));

        var leftover = entries.Single(entry => entry.RelativePath == "Leftover");
        Assert.That(leftover.IsDirectory, Is.True);
        Assert.That(leftover.ManagedChildCount, Is.EqualTo(0));
        Assert.That(leftover.UnmanagedChildCount, Is.EqualTo(1));
        Assert.That(leftover.SizeBytes, Is.EqualTo(3), "目录尺寸是未管理文件的递归合计");
        Assert.That(leftover.LastWriteTimeUtc, Is.Not.Null);

        var mixed = entries.Single(entry => entry.RelativePath == "Mixed");
        Assert.That(mixed.ManagedChildCount, Is.EqualTo(1), "混着受管文件的目录要报出受管子项数");
        Assert.That(mixed.UnmanagedChildCount, Is.EqualTo(1));
        Assert.That(mixed.SizeBytes, Is.EqualTo(3), "受管文件不计入未管理尺寸");

        var loose = entries.Single(entry => entry.RelativePath == "loose.cfg");
        Assert.That(loose.IsDirectory, Is.False);
        Assert.That(loose.SizeBytes, Is.EqualTo(3));
        Assert.That(loose.GuessIdentifier, Is.Null);

        var stray = entries.Single(entry => entry.RelativePath == "Stray/JunkMod.dll");
        Assert.That(stray.GuessIdentifier, Is.EqualTo("JunkMod"), "游离 DLL 要能猜出模块标识符");
    }

    [Test]
    public async Task ScanAsync_GameDataWithOnlyManagedFiles_ReturnsEmptyList()
    {
        using var workspace = PrepareGameData("unmanaged-only-managed");
        Write(workspace.Path, "ManagedMod/ManagedMod.dll");
        var provider = new FakeManagedProvider();
        provider.Managed.Add("ManagedMod/ManagedMod.dll");
        var reporter = new RecordingErrorReporter();
        var service = new UnmanagedService(provider, new FakeRegistrar(), reporter);

        var result = await service.ScanAsync(Instance(workspace.Path));

        Assert.That(result.IsSuccess, Is.True, result.Message);
        Assert.That(result.Value, Is.Empty);
        Assert.That(reporter.Entries, Is.Empty);
    }

    [Test]
    public async Task ScanAsync_EmptyGameData_ReturnsEmptyList()
    {
        using var workspace = PrepareGameData("unmanaged-empty");
        var service = new UnmanagedService(new FakeManagedProvider(), new FakeRegistrar(), new RecordingErrorReporter());

        var result = await service.ScanAsync(Instance(workspace.Path));

        Assert.That(result.IsSuccess, Is.True, result.Message);
        Assert.That(result.Value, Is.Empty);
    }

    [Test]
    public async Task ScanAsync_MissingGameData_ReturnsNonRetryableFailure()
    {
        using var workspace = new TempWorkspace("unmanaged-no-gamedata");
        var service = new UnmanagedService(new FakeManagedProvider(), new FakeRegistrar(), new RecordingErrorReporter());

        var result = await service.ScanAsync(Instance(workspace.Path));

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Message, Does.Contain("找不到 GameData 目录"));
        Assert.That(result.Retryable, Is.False);
    }

    [Test]
    public async Task ScanAsync_ProviderFailure_PropagatesMessageAndRetryableFlag()
    {
        using var workspace = PrepareGameData("unmanaged-provider-fail");
        var provider = new FakeManagedProvider
        {
            Override = OperationResult<IReadOnlySet<string>>.Fail(
                "读取注册表失败，无法判断哪些内容受管。", "registry.locked", retryable: true),
        };
        var reporter = new RecordingErrorReporter();
        var service = new UnmanagedService(provider, new FakeRegistrar(), reporter);

        var result = await service.ScanAsync(Instance(workspace.Path));

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Message, Is.EqualTo("读取注册表失败，无法判断哪些内容受管。"));
        Assert.That(result.Detail, Is.EqualTo("registry.locked"));
        Assert.That(result.Retryable, Is.True);
        Assert.That(reporter.Entries, Is.Empty, "服务返回失败不算异常，不写错误日志");
    }

    [Test]
    public async Task ScanAsync_ProviderThrows_IsReportedAsRetryableFailure()
    {
        using var workspace = PrepareGameData("unmanaged-provider-throw");
        var provider = new FakeManagedProvider { Throw = new InvalidOperationException("注册表句柄已释放") };
        var reporter = new RecordingErrorReporter();
        var service = new UnmanagedService(provider, new FakeRegistrar(), reporter);

        var result = await service.ScanAsync(Instance(workspace.Path));

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Message, Is.EqualTo("未管理文件操作失败。"));
        Assert.That(result.Retryable, Is.True);
        Assert.That(reporter.Entries, Has.Count.EqualTo(1));
        Assert.That(reporter.Entries[0].Context, Is.EqualTo("扫描未管理文件"));
    }

    [Test]
    public async Task DeleteAsync_DryRun_ReturnsPathsButWritesNothing()
    {
        using var workspace = PrepareGameData("unmanaged-dryrun");
        Write(workspace.Path, "Leftover/old.txt", "abc");
        Write(workspace.Path, "loose.cfg", "cfg");
        var service = new UnmanagedService(new FakeManagedProvider(), new FakeRegistrar(), new RecordingErrorReporter());

        var result = await service.DeleteAsync(
            Instance(workspace.Path), new[] { "loose.cfg", "Leftover" }, dryRun: true);

        Assert.That(result.IsSuccess, Is.True, result.Message);
        Assert.That(result.Value, Is.EquivalentTo(new[] { "loose.cfg", "Leftover" }));
        Assert.That(File.Exists(Path.Combine(workspace.Path, "GameData", "loose.cfg")), Is.True, "dry-run 不得落盘");
        Assert.That(Directory.Exists(Path.Combine(workspace.Path, "GameData", "Leftover")), Is.True, "dry-run 不得落盘");
    }

    [Test]
    public async Task DeleteAsync_ManagedPath_RefusesWholeBatch()
    {
        using var workspace = PrepareGameData("unmanaged-managed-refuse");
        Write(workspace.Path, "Mixed/managed.dll");
        Write(workspace.Path, "Mixed/junk.txt", "zzz");
        Write(workspace.Path, "loose.cfg", "cfg");
        var provider = new FakeManagedProvider();
        provider.Managed.Add("Mixed/managed.dll");
        var service = new UnmanagedService(provider, new FakeRegistrar(), new RecordingErrorReporter());

        var result = await service.DeleteAsync(
            Instance(workspace.Path), new[] { "loose.cfg", "Mixed" }, dryRun: false);

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Message, Does.Contain("受管内容"));
        Assert.That(result.Message, Does.Contain("Mixed"));
        Assert.That(result.Retryable, Is.False);
        Assert.That(File.Exists(Path.Combine(workspace.Path, "GameData", "loose.cfg")), Is.True, "整批拒绝，一个都不删");
        Assert.That(File.Exists(Path.Combine(workspace.Path, "GameData", "Mixed", "junk.txt")), Is.True);
    }

    [Test]
    public async Task DeleteAsync_ManagedFileItself_IsRefused()
    {
        using var workspace = PrepareGameData("unmanaged-managed-file");
        Write(workspace.Path, "ManagedMod/ManagedMod.dll");
        var provider = new FakeManagedProvider();
        provider.Managed.Add("ManagedMod/ManagedMod.dll");
        var service = new UnmanagedService(provider, new FakeRegistrar(), new RecordingErrorReporter());

        var result = await service.DeleteAsync(
            Instance(workspace.Path), new[] { "ManagedMod/ManagedMod.dll" }, dryRun: false);

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(File.Exists(Path.Combine(workspace.Path, "GameData", "ManagedMod", "ManagedMod.dll")), Is.True);
    }

    [Test]
    public async Task DeleteAsync_RealDelete_RemovesContent_ThenMissingBecomesRetryable()
    {
        using var workspace = PrepareGameData("unmanaged-real-delete");
        Write(workspace.Path, "Leftover/old.txt", "abc");
        Write(workspace.Path, "loose.cfg", "cfg");
        var service = new UnmanagedService(new FakeManagedProvider(), new FakeRegistrar(), new RecordingErrorReporter());
        var instance = Instance(workspace.Path);

        var first = await service.DeleteAsync(instance, new[] { "loose.cfg", "Leftover" }, dryRun: false);

        Assert.That(first.IsSuccess, Is.True, first.Message);
        Assert.That(File.Exists(Path.Combine(workspace.Path, "GameData", "loose.cfg")), Is.False);
        Assert.That(Directory.Exists(Path.Combine(workspace.Path, "GameData", "Leftover")), Is.False, "目录按递归删除");

        var second = await service.DeleteAsync(instance, new[] { "loose.cfg" }, dryRun: false);

        Assert.That(second.IsSuccess, Is.False);
        Assert.That(second.Message, Does.Contain("找不到要删除的内容"));
        Assert.That(second.Retryable, Is.True, "已消失的内容允许刷新后重试");
    }

    [Test]
    public async Task DeleteAsync_RejectsTraversalAndAbsolutePaths()
    {
        using var workspace = PrepareGameData("unmanaged-traversal");
        var service = new UnmanagedService(new FakeManagedProvider(), new FakeRegistrar(), new RecordingErrorReporter());
        var instance = Instance(workspace.Path);

        foreach (var bad in new[] { "../evil.txt", "C:/Windows/system32/drivers/etc/hosts", "Leftover/../../evil.txt" })
        {
            var result = await service.DeleteAsync(instance, new[] { bad }, dryRun: true);
            Assert.That(result.IsSuccess, Is.False, bad);
            Assert.That(result.Message, Does.Contain("路径不合法"), bad);
            Assert.That(result.Retryable, Is.False, bad);
        }
    }

    [Test]
    public async Task DeleteAsync_EmptySelection_Fails()
    {
        using var workspace = PrepareGameData("unmanaged-empty-selection");
        var service = new UnmanagedService(new FakeManagedProvider(), new FakeRegistrar(), new RecordingErrorReporter());

        var result = await service.DeleteAsync(Instance(workspace.Path), Array.Empty<string>(), dryRun: true);

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Message, Is.EqualTo("没有选中要删除的内容。"));
    }

    [Test]
    public async Task AdoptAsync_RegistersTheDllUnderGameData()
    {
        using var workspace = PrepareGameData("unmanaged-adopt");
        Write(workspace.Path, "Stray/JunkMod.dll", "dll");
        var registrar = new FakeRegistrar();
        var service = new UnmanagedService(new FakeManagedProvider(), registrar, new RecordingErrorReporter());

        var result = await service.AdoptAsync(Instance(workspace.Path), new[] { "Stray/JunkMod.dll" });

        Assert.That(result.IsSuccess, Is.True, result.Message);
        var outcome = result.Value!;
        Assert.That(outcome.AdoptedCount, Is.EqualTo(1));
        Assert.That(outcome.Applied, Is.True);
        Assert.That(outcome.Items.Single().Identifier, Is.EqualTo("JunkMod"));
        Assert.That(registrar.Merged, Is.Not.Null);
        Assert.That(registrar.Merged!["JunkMod"], Is.EqualTo("GameData/Stray/JunkMod.dll"));
    }

    [Test]
    public async Task AdoptAsync_DryRun_NeverWritesTheRegistry()
    {
        using var workspace = PrepareGameData("unmanaged-adopt-dry");
        Write(workspace.Path, "Stray/JunkMod.dll", "dll");
        var registrar = new FakeRegistrar();
        var service = new UnmanagedService(new FakeManagedProvider(), registrar, new RecordingErrorReporter());

        var result = await service.AdoptAsync(Instance(workspace.Path), new[] { "Stray/JunkMod.dll" }, dryRun: true);

        Assert.That(result.IsSuccess, Is.True, result.Message);
        Assert.That(result.Value!.AdoptedCount, Is.EqualTo(1));
        Assert.That(result.Value!.IsPreview, Is.True);
        Assert.That(registrar.Merged, Is.Null, "预演绝不能写注册表");
    }

    [Test]
    public async Task AdoptAsync_NonDll_IsRefused()
    {
        using var workspace = PrepareGameData("unmanaged-adopt-nondll");
        Write(workspace.Path, "loose.cfg", "cfg");
        var registrar = new FakeRegistrar();
        var service = new UnmanagedService(new FakeManagedProvider(), registrar, new RecordingErrorReporter());

        var result = await service.AdoptAsync(Instance(workspace.Path), new[] { "loose.cfg" });

        Assert.That(result.IsSuccess, Is.True, result.Message);
        Assert.That(result.Value!.RefusedCount, Is.EqualTo(1));
        Assert.That(result.Value!.Items.Single().Reason, Does.Contain(".dll"));
        Assert.That(registrar.Merged, Is.Null);
    }

    [Test]
    public async Task AdoptAsync_AlreadyManaged_IsSkipped()
    {
        using var workspace = PrepareGameData("unmanaged-adopt-skip");
        Write(workspace.Path, "ManagedMod/ManagedMod.dll", "dll");
        var provider = new FakeManagedProvider();
        provider.Managed.Add("ManagedMod/ManagedMod.dll");
        var registrar = new FakeRegistrar();
        var service = new UnmanagedService(provider, registrar, new RecordingErrorReporter());

        var result = await service.AdoptAsync(Instance(workspace.Path), new[] { "ManagedMod/ManagedMod.dll" });

        Assert.That(result.IsSuccess, Is.True, result.Message);
        Assert.That(result.Value!.SkippedCount, Is.EqualTo(1));
        Assert.That(registrar.Merged, Is.Null);
    }

    [Test]
    public async Task AdoptAsync_IdentifierTakenByAnotherPath_IsRefused()
    {
        using var workspace = PrepareGameData("unmanaged-adopt-conflict");
        Write(workspace.Path, "Stray/JunkMod.dll", "dll");
        var registrar = new FakeRegistrar();
        registrar.Installed.Add("JunkMod", "GameData/JunkMod/JunkMod.dll");
        var service = new UnmanagedService(new FakeManagedProvider(), registrar, new RecordingErrorReporter());

        var result = await service.AdoptAsync(Instance(workspace.Path), new[] { "Stray/JunkMod.dll" });

        Assert.That(result.IsSuccess, Is.True, result.Message);
        Assert.That(result.Value!.RefusedCount, Is.EqualTo(1));
        Assert.That(result.Value!.Items.Single().Reason, Does.Contain("JunkMod"));
        Assert.That(registrar.Merged, Is.Null);
    }

    [Test]
    public async Task AdoptAsync_MergeFailure_IsReportedAsRetryable()
    {
        using var workspace = PrepareGameData("unmanaged-adopt-merge-fail");
        Write(workspace.Path, "Stray/JunkMod.dll", "dll");
        var registrar = new FakeRegistrar { MergeFailure = "注册表写入失败。" };
        var service = new UnmanagedService(new FakeManagedProvider(), registrar, new RecordingErrorReporter());

        var result = await service.AdoptAsync(Instance(workspace.Path), new[] { "Stray/JunkMod.dll" });

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Message, Is.EqualTo("注册表写入失败。"));
        Assert.That(result.Retryable, Is.True);
    }

    [Test]
    public async Task AdoptAsync_WithoutSelection_Fails()
    {
        using var workspace = PrepareGameData("unmanaged-adopt-empty");
        var service = new UnmanagedService(new FakeManagedProvider(), new FakeRegistrar(), new RecordingErrorReporter());

        var result = await service.AdoptAsync(Instance(workspace.Path), Array.Empty<string>());

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Message, Does.Contain("没有选中"));
    }

    [Test]
    public void GuessIdentifier_OnlyForDll()
    {
        Assert.That(UnmanagedScanner.GuessIdentifier("Stray/JunkMod.dll"), Is.EqualTo("JunkMod"));
        Assert.That(UnmanagedScanner.GuessIdentifier("JunkMod.dll"), Is.EqualTo("JunkMod"));
        Assert.That(UnmanagedScanner.GuessIdentifier("Stray/readme.txt"), Is.Null);
    }

    private static InstanceInfo Instance(string gameDir) =>
        new("测试实例", gameDir, "1.12.5", IsDefault: true, IsLocked: false, ModuleCount: 1);

    private static TempWorkspace PrepareGameData(string label)
    {
        var workspace = new TempWorkspace(label);
        Directory.CreateDirectory(Path.Combine(workspace.Path, "GameData"));
        return workspace;
    }

    private static void Write(string gameDir, string relative, string content = "x")
    {
        var path = Path.Combine(gameDir, "GameData", relative.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
    }

    private sealed class FakeManagedProvider : IManagedPathProvider
    {
        public HashSet<string> Managed { get; } = new(StringComparer.OrdinalIgnoreCase);

        public OperationResult<IReadOnlySet<string>>? Override { get; set; }

        public Exception? Throw { get; set; }

        public int Calls { get; private set; }

        public OperationResult<IReadOnlySet<string>> GetManagedPaths(InstanceInfo instance)
        {
            Calls++;
            if (Throw is not null)
            {
                throw Throw;
            }

            return Override ?? OperationResult<IReadOnlySet<string>>.Ok(Managed);
        }
    }

    private sealed class FakeRegistrar : IManagedFileRegistrar
    {
        public Dictionary<string, string> Installed { get; } = new(StringComparer.Ordinal);

        public IReadOnlyDictionary<string, string>? Merged { get; private set; }

        public string? MergeFailure { get; init; }

        public string? GetFailure { get; init; }

        public OperationResult<IReadOnlyDictionary<string, string>> GetInstalledDlls(InstanceInfo instance)
            => GetFailure is null
                ? OperationResult<IReadOnlyDictionary<string, string>>.Ok(
                    new Dictionary<string, string>(Installed, StringComparer.Ordinal))
                : OperationResult<IReadOnlyDictionary<string, string>>.Fail(GetFailure, null, retryable: true);

        public OperationResult<DllMergeResult> MergeDlls(
            InstanceInfo instance, IReadOnlyDictionary<string, string> dlls, bool takeSnapshot = true)
        {
            if (MergeFailure is not null)
            {
                return OperationResult<DllMergeResult>.Fail(MergeFailure, null, retryable: true);
            }

            Merged = new Dictionary<string, string>(dlls, StringComparer.Ordinal);
            return OperationResult<DllMergeResult>.Ok(new DllMergeResult(true, "registry.json.bak-20261028-101010"));
        }

        public string NormalizeIdentifier(string raw) =>
            new string(raw.Trim().Select(c => char.IsLetterOrDigit(c) || c == '-' ? c : '-').ToArray()).Trim('-');
    }

    private sealed class RecordingErrorReporter : IErrorReporter
    {
        public event Action<ErrorEntry>? Reported;

        public List<ErrorEntry> Entries { get; } = new();

        public IReadOnlyList<ErrorEntry> Recent => Entries;

        public void Report(Exception exception, string context, string? userMessage = null)
        {
            var entry = new ErrorEntry(DateTime.UtcNow, context, userMessage ?? exception.Message, exception.ToString(), exception);
            Entries.Add(entry);
            Reported?.Invoke(entry);
        }

        public void Report(string context, string userMessage, string? detail = null)
        {
            var entry = new ErrorEntry(DateTime.UtcNow, context, userMessage, detail, null);
            Entries.Add(entry);
            Reported?.Invoke(entry);
        }
    }
}
