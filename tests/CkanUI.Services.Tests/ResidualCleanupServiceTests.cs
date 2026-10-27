using System.Diagnostics;
using CkanUI.Services.Abstractions;
using CkanUI.Services.Abstractions.Dtos;
using CkanUI.Services.Cleanup;
using CkanUI.Services.Unmanaged;
using CkanUI.TestSupport;
using NUnit.Framework;

namespace CkanUI.Services.Tests;

/// <summary>
/// 卸载残留目录清理（12 篇 F-43）：列出/预览/删除、受管目录拒删、符号链接拒删、
/// 删除失败可重试、越权路径拒绝。
/// </summary>
/// <remarks>全部用 <see cref="TempWorkspace"/> 造假 GameData（04 篇 §3：测试不碰系统临时目录，也不碰真实 KSP 实例）。</remarks>
[TestFixture]
public sealed class ResidualCleanupServiceTests
{
    [Test]
    public async Task ListAsync_MergesHeuristicAndUninstallCandidates()
    {
        using var workspace = PrepareGameData("residual-list");
        Write(workspace.Path, "Leftover/old.cfg", "abc");
        Write(workspace.Path, "RealMod/RealMod.dll", "dll");
        Write(workspace.Path, "RealMod/settings.cfg", "cfg");
        Write(workspace.Path, "RemovedMod/settings.cfg", "cfg");
        Directory.CreateDirectory(Path.Combine(workspace.Path, "GameData", "EmptyResidual"));

        var service = Service();

        var result = await service.ListAsync(
            Instance(workspace.Path), new[] { "RemovedMod", "Leftover" });

        Assert.That(result.IsSuccess, Is.True, result.Message);
        var rows = result.Value!;
        Assert.That(
            rows.Select(row => row.RelativePath),
            Is.EqualTo(new[] { "EmptyResidual", "Leftover", "RemovedMod" }),
            "卸载结果与启发式合并后去重，并按路径排序");
        Assert.That(rows.Select(row => row.RelativePath), Does.Not.Contain("RealMod"), "带 DLL 的目录不是残留");

        var leftover = rows.Single(row => row.RelativePath == "Leftover");
        Assert.That(leftover.FileCount, Is.EqualTo(1));
        Assert.That(leftover.SizeBytes, Is.EqualTo(3));
        Assert.That(leftover.SampleFiles, Is.EquivalentTo(new[] { "old.cfg" }), "示例文件是目录内的相对路径");
        Assert.That(leftover.IsDeletable, Is.True);
        Assert.That(leftover.BlockReason, Is.Null);

        var empty = rows.Single(row => row.RelativePath == "EmptyResidual");
        Assert.That(empty.FileCount, Is.EqualTo(0));
        Assert.That(empty.SampleFiles, Is.Empty);
        Assert.That(empty.IsDeletable, Is.True, "空目录也要能清理");
    }

    [Test]
    public async Task ListAsync_DropsReportedDirectoriesThatNoLongerExist()
    {
        using var workspace = PrepareGameData("residual-list-missing");
        Write(workspace.Path, "Leftover/old.cfg", "abc");
        var service = Service();

        var result = await service.ListAsync(Instance(workspace.Path), new[] { "AlreadyGone", "Leftover" });

        Assert.That(result.IsSuccess, Is.True, result.Message);
        Assert.That(result.Value!.Select(row => row.RelativePath), Is.EqualTo(new[] { "Leftover" }));
    }

    [Test]
    public async Task ListAsync_ManagedDirectory_IsListedButBlocked()
    {
        using var workspace = PrepareGameData("residual-list-managed");
        Write(workspace.Path, "RemovedMod/settings.cfg", "cfg");
        var provider = new FakeManagedProvider();
        provider.Managed.Add("RemovedMod/settings.cfg");
        var service = Service(provider);

        var result = await service.ListAsync(Instance(workspace.Path), new[] { "RemovedMod" });

        Assert.That(result.IsSuccess, Is.True, result.Message);
        var row = result.Value!.Single();
        Assert.That(row.IsDeletable, Is.False, "含受管文件的目录要列出来但标不可删");
        Assert.That(row.BlockReason, Does.Contain("受管文件"));
    }

    [Test]
    public async Task ListAsync_MissingGameData_FailsWithoutRetry()
    {
        using var workspace = new TempWorkspace("residual-no-gamedata");
        var service = Service();

        var result = await service.ListAsync(Instance(workspace.Path));

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Message, Does.Contain("找不到 GameData 目录"));
        Assert.That(result.Retryable, Is.False);
    }

    [Test]
    public async Task ListAsync_ManagedListUnavailable_Fails()
    {
        using var workspace = PrepareGameData("residual-no-managed");
        Write(workspace.Path, "Leftover/old.cfg", "abc");
        var provider = new FakeManagedProvider
        {
            Override = OperationResult<IReadOnlySet<string>>.Fail(
                "读取注册表失败，无法判断哪些内容受管。", "registry.locked", retryable: true),
        };
        var service = Service(provider);

        var result = await service.ListAsync(Instance(workspace.Path));

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Message, Is.EqualTo("读取注册表失败，无法判断哪些内容受管。"));
        Assert.That(result.Retryable, Is.True);
    }

    [Test]
    public async Task ListAsync_SourceFailure_Propagates()
    {
        using var workspace = PrepareGameData("residual-source-fail");
        var source = new FakeSource
        {
            Override = OperationResult<IReadOnlyList<string>>.Fail("扫描 GameData 失败。", "io", retryable: true),
        };
        var service = Service(source: source);

        var result = await service.ListAsync(Instance(workspace.Path));

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Message, Is.EqualTo("扫描 GameData 失败。"));
    }

    [Test]
    public async Task PreviewAsync_ReportsContentsWithoutDeleting()
    {
        using var workspace = PrepareGameData("residual-preview");
        Write(workspace.Path, "Leftover/old.cfg", "abc");
        var service = Service();

        var result = await service.PreviewAsync(Instance(workspace.Path), new[] { "Leftover", "Vanished" });

        Assert.That(result.IsSuccess, Is.True, result.Message);
        var rows = result.Value!;
        Assert.That(rows, Has.Count.EqualTo(2));

        var leftover = rows.Single(row => row.RelativePath == "Leftover");
        Assert.That(leftover.FileCount, Is.EqualTo(1));
        Assert.That(leftover.SampleFiles, Is.EquivalentTo(new[] { "old.cfg" }));

        var vanished = rows.Single(row => row.RelativePath == "Vanished");
        Assert.That(vanished.IsDeletable, Is.False, "预览要如实说出目录已不在");
        Assert.That(vanished.BlockReason, Does.Contain("已不存在"));

        Assert.That(Directory.Exists(Path.Combine(workspace.Path, "GameData", "Leftover")), Is.True, "预览不得落盘");
    }

    [Test]
    public async Task PreviewAsync_EmptySelection_Fails()
    {
        using var workspace = PrepareGameData("residual-preview-empty");
        var service = Service();

        var result = await service.PreviewAsync(Instance(workspace.Path), Array.Empty<string>());

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Message, Is.EqualTo("没有勾选要删除的目录。"));
        Assert.That(result.Retryable, Is.False);
    }

    [Test]
    public async Task DeleteAsync_RemovesSelectedDirectoriesAndReportsFreedBytes()
    {
        using var workspace = PrepareGameData("residual-delete");
        Write(workspace.Path, "Leftover/old.cfg", "abc");
        Write(workspace.Path, "EmptyResidual/placeholder.txt", string.Empty);
        Write(workspace.Path, "KeepMe/keep.cfg", "keep");
        var service = Service();

        var result = await service.DeleteAsync(Instance(workspace.Path), new[] { "Leftover", "EmptyResidual" });

        Assert.That(result.IsSuccess, Is.True, result.Message);
        var report = result.Value!;
        Assert.That(report.DeletedCount, Is.EqualTo(2));
        Assert.That(report.FailedCount, Is.EqualTo(0));
        Assert.That(report.SkippedCount, Is.EqualTo(0));
        Assert.That(report.Deleted, Is.EquivalentTo(new[] { "Leftover", "EmptyResidual" }));
        Assert.That(report.FreedBytes, Is.EqualTo(3), "释放字节数按删除前的统计累加");
        Assert.That(Directory.Exists(Path.Combine(workspace.Path, "GameData", "Leftover")), Is.False);
        Assert.That(Directory.Exists(Path.Combine(workspace.Path, "GameData", "KeepMe")), Is.True, "没勾的不动");
    }

    [Test]
    public async Task DeleteAsync_ManagedDirectory_IsSkippedNotDeleted()
    {
        using var workspace = PrepareGameData("residual-delete-managed");
        Write(workspace.Path, "RemovedMod/settings.cfg", "cfg");
        var provider = new FakeManagedProvider();
        provider.Managed.Add("RemovedMod/settings.cfg");
        var service = Service(provider);

        var result = await service.DeleteAsync(Instance(workspace.Path), new[] { "RemovedMod" });

        Assert.That(result.IsSuccess, Is.True, "跳过不是失败：能删多少删多少");
        Assert.That(result.Value!.SkippedCount, Is.EqualTo(1));
        Assert.That(result.Value!.Skipped, Is.EquivalentTo(new[] { "RemovedMod" }));
        Assert.That(result.Value!.DeletedCount, Is.EqualTo(0));
        Assert.That(
            File.Exists(Path.Combine(workspace.Path, "GameData", "RemovedMod", "settings.cfg")),
            Is.True,
            "受管内容一个字节都不许动");
    }

    [Test]
    public async Task DeleteAsync_SymlinkedDirectory_IsSkippedAndTargetSurvives()
    {
        using var workspace = PrepareGameData("residual-delete-symlink");
        var target = workspace.Combine("outside");
        Directory.CreateDirectory(target);
        File.WriteAllText(Path.Combine(target, "keep.cfg"), "keep");

        var link = Path.Combine(workspace.Path, "GameData", "Link");
        if (!TryCreateLink(link, target))
        {
            // 沙箱里既没符号链接权限也建不了目录联接时跳过这一条（不伪造通过）。
            return;
        }

        var service = Service();

        var preview = await service.PreviewAsync(Instance(workspace.Path), new[] { "Link" });
        Assert.That(preview.IsSuccess, Is.True, preview.Message);
        Assert.That(preview.Value!.Single().BlockReason, Does.Contain("符号链接"));

        var result = await service.DeleteAsync(Instance(workspace.Path), new[] { "Link" });

        Assert.That(result.IsSuccess, Is.True, result.Message);
        Assert.That(result.Value!.SkippedCount, Is.EqualTo(1));
        Assert.That(File.Exists(Path.Combine(target, "keep.cfg")), Is.True, "链接目标必须完好");
    }

    [Test]
    public async Task DeleteAsync_RejectsTraversalAndAbsolutePaths()
    {
        using var workspace = PrepareGameData("residual-traversal");
        Write(workspace.Path, "Leftover/old.cfg", "abc");
        var service = Service();
        var instance = Instance(workspace.Path);

        foreach (var bad in new[] { "../evil", "C:/Windows/system32", "Leftover/../../evil", "..", string.Empty })
        {
            var result = await service.DeleteAsync(instance, new[] { bad });
            Assert.That(result.IsSuccess, Is.False, bad);
            Assert.That(result.Message, Does.Contain("路径不合法"), bad);
            Assert.That(result.Retryable, Is.False, bad);
        }

        Assert.That(Directory.Exists(Path.Combine(workspace.Path, "GameData", "Leftover")), Is.True);
    }

    [Test]
    public async Task DeleteAsync_EmptySelection_Fails()
    {
        using var workspace = PrepareGameData("residual-delete-empty");
        var service = Service();

        var result = await service.DeleteAsync(Instance(workspace.Path), Array.Empty<string>());

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Message, Is.EqualTo("没有勾选要删除的目录。"));
    }

    [Test]
    public async Task DeleteAsync_LockedDirectory_FailsRetryableThenSucceedsAfterUnlock()
    {
        using var workspace = PrepareGameData("residual-delete-locked");
        Write(workspace.Path, "Locked/keep.txt", "keep");
        var lockedFile = Path.Combine(workspace.Path, "GameData", "Locked", "keep.txt");
        var reporter = new RecordingErrorReporter();
        var service = Service(reporter: reporter);
        var instance = Instance(workspace.Path);

        OperationResult<ResidualCleanupReport> first;
        using (new FileStream(lockedFile, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            first = await service.DeleteAsync(instance, new[] { "Locked" });
        }

        Assert.That(first.IsSuccess, Is.False);
        Assert.That(first.Message, Does.Contain("没能删除"));
        Assert.That(first.Retryable, Is.True, "占用/权限问题要允许重试");
        Assert.That(reporter.Entries, Has.Count.EqualTo(1));
        Assert.That(reporter.Entries[0].Context, Is.EqualTo("ResidualCleanupService.Delete"));

        var second = await service.DeleteAsync(instance, new[] { "Locked" });

        Assert.That(second.IsSuccess, Is.True, "占用解除后重试应当成功");
        Assert.That(second.Value!.DeletedCount, Is.EqualTo(1));
        Assert.That(Directory.Exists(Path.Combine(workspace.Path, "GameData", "Locked")), Is.False);
    }

    [Test]
    public async Task DeleteAsync_HeuristicCandidates_AreDeletableEndToEnd()
    {
        using var workspace = PrepareGameData("residual-end-to-end");
        Write(workspace.Path, "OldMod/OldMod.version", "v1");
        Write(workspace.Path, "OldMod/OldMod.cfg", "cfg");
        var service = Service();
        var instance = Instance(workspace.Path);

        var listed = await service.ListAsync(instance);
        Assert.That(listed.IsSuccess, Is.True, listed.Message);
        var paths = listed.Value!.Select(row => row.RelativePath).ToArray();
        Assert.That(paths, Is.EqualTo(new[] { "OldMod" }));

        var result = await service.DeleteAsync(instance, paths);

        Assert.That(result.IsSuccess, Is.True, result.Message);
        Assert.That(result.Value!.DeletedCount, Is.EqualTo(1));
        Assert.That(Directory.Exists(Path.Combine(workspace.Path, "GameData", "OldMod")), Is.False);

        var again = await service.ListAsync(instance);
        Assert.That(again.IsSuccess, Is.True, again.Message);
        Assert.That(again.Value, Is.Empty, "删干净后再列就是空的");
    }

    private static ResidualCleanupService Service(
        FakeManagedProvider? provider = null,
        IResidualDirectorySource? source = null,
        RecordingErrorReporter? reporter = null)
        => new(
            source ?? new HeuristicResidualDirectorySource(),
            provider ?? new FakeManagedProvider(),
            reporter ?? new RecordingErrorReporter());

    /// <summary>造一个重解析点：先试符号链接（需要权限），不行退回 Windows 目录联接（普通用户可用）。</summary>
    private static bool TryCreateLink(string link, string target)
    {
        try
        {
            Directory.CreateSymbolicLink(link, target);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or PlatformNotSupportedException)
        {
            TestContext.Out.WriteLine($"符号链接不可用（{ex.GetType().Name}），改用目录联接。");
        }

        if (!OperatingSystem.IsWindows())
        {
            return false;
        }

        try
        {
            using var process = Process.Start(new ProcessStartInfo("cmd.exe", $"/c mklink /J \"{link}\" \"{target}\"")
            {
                CreateNoWindow = true,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            });

            if (process is null)
            {
                return false;
            }

            process.WaitForExit(5000);
            return Directory.Exists(link);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
        {
            TestContext.Out.WriteLine($"跳过符号链接用例：{ex.GetType().Name} {ex.Message}");
            return false;
        }
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

    /// <summary>可脚本化的残留目录来源（真实来源是 <see cref="HeuristicResidualDirectorySource"/>）。</summary>
    private sealed class FakeSource : IResidualDirectorySource
    {
        public List<string> Candidates { get; } = new();

        public OperationResult<IReadOnlyList<string>>? Override { get; set; }

        public int Calls { get; private set; }

        public OperationResult<IReadOnlyList<string>> GetCandidates(InstanceInfo instance)
        {
            Calls++;
            return Override ?? OperationResult<IReadOnlyList<string>>.Ok(Candidates.ToArray());
        }
    }

    private sealed class FakeManagedProvider : IManagedPathProvider
    {
        public HashSet<string> Managed { get; } = new(StringComparer.OrdinalIgnoreCase);

        public OperationResult<IReadOnlySet<string>>? Override { get; set; }

        public OperationResult<IReadOnlySet<string>> GetManagedPaths(InstanceInfo instance)
            => Override ?? OperationResult<IReadOnlySet<string>>.Ok(Managed);
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
