using CkanUI.Services.Abstractions;
using CkanUI.TestSupport;
using CkanUI.Plugin.ModConfig;
using NUnit.Framework;

namespace CkanUI.Plugin.ModConfig.Tests;

/// <summary>
/// .bak 快照恢复器（M9 / CFG-04 的回滚入口）：从快照文件名反推原文件、按字节原子写回、
/// 快照缺失逐条报错、部分成功保留失败原因。
/// </summary>
[TestFixture]
public sealed class ModConfigBackupRestorerTests
{
    [Test]
    public void TryResolveOriginalPath_ParsesTimestampBackup()
    {
        const string backupPath = "/game/GameData/Demo/settings.cfg.ckan-ui-20240102-030405.bak";
        var resolved = ModConfigBackupRestorer.TryResolveOriginalPath(backupPath, out var original);

        Assert.That(resolved, Is.True);
        Assert.That(original, Is.EqualTo(Path.Combine(Path.GetDirectoryName(backupPath)!, "settings.cfg")));
    }

    [Test]
    public void TryResolveOriginalPath_ParsesSuffixedBackup()
    {
        const string backupPath = "/game/GameData/Demo/settings.cfg.ckan-ui-20240102-030405-2.bak";
        var resolved = ModConfigBackupRestorer.TryResolveOriginalPath(backupPath, out var original);

        Assert.That(resolved, Is.True);
        Assert.That(original, Is.EqualTo(Path.Combine(Path.GetDirectoryName(backupPath)!, "settings.cfg")));
    }

    [Test]
    public void TryResolveOriginalPath_RejectsOtherFiles()
    {
        Assert.That(ModConfigBackupRestorer.TryResolveOriginalPath("/game/settings.cfg", out _), Is.False);
        Assert.That(ModConfigBackupRestorer.TryResolveOriginalPath("/game/settings.cfg.ckan-ui-backup.bak", out _), Is.False);
        Assert.That(ModConfigBackupRestorer.TryResolveOriginalPath("/game/.ckan-ui-20240102-030405.bak", out _), Is.False);
        Assert.That(ModConfigBackupRestorer.TryResolveOriginalPath(null, out _), Is.False);
        Assert.That(ModConfigBackupRestorer.TryResolveOriginalPath(string.Empty, out _), Is.False);
    }

    [Test]
    public async Task RestoreAsync_WritesSnapshotBytesBack()
    {
        using var workspace = new TempWorkspace("modconfig-restore");
        var original = workspace.Combine("settings.cfg");
        var backup = original + ".ckan-ui-20240102-030405.bak";
        await File.WriteAllTextAsync(original, "enabled = false");
        await File.WriteAllTextAsync(backup, "enabled = true");

        var result = await new ModConfigBackupRestorer().RestoreAsync([backup]);

        Assert.That(result.IsSuccess, Is.True);
        Assert.That(result.Value, Is.Not.Null);
        Assert.That(result.Value!.Failures, Is.Empty);
        Assert.That(result.Value.RestoredPaths.Single(), Is.EqualTo(original));
        Assert.That(await File.ReadAllTextAsync(original), Is.EqualTo("enabled = true"));
        Assert.That(Directory.GetFiles(workspace.Path, "*.restore-*"), Is.Empty);
    }

    [Test]
    public async Task RestoreAsync_MissingSnapshot_ReportsReadableFailure()
    {
        using var workspace = new TempWorkspace("modconfig-restore-missing");
        var original = workspace.Combine("settings.cfg");
        var backup = original + ".ckan-ui-20240102-030405.bak";

        var result = await new ModConfigBackupRestorer().RestoreAsync([backup]);

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Message, Is.EqualTo("恢复快照失败，没有文件被还原。"));
        Assert.That(result.Detail, Does.Contain("settings.cfg"));
        Assert.That(result.Detail, Does.Contain("快照文件已不存在"));
    }

    [Test]
    public async Task RestoreAsync_MalformedName_KeepsOriginalUntouched()
    {
        using var workspace = new TempWorkspace("modconfig-restore-badname");
        var original = workspace.Combine("settings.cfg");
        await File.WriteAllTextAsync(original, "enabled = false");
        var bogus = original + ".bak";
        await File.WriteAllTextAsync(bogus, "enabled = true");

        var result = await new ModConfigBackupRestorer().RestoreAsync([bogus]);

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Detail, Does.Contain("无法定位原文件"));
        Assert.That(await File.ReadAllTextAsync(original), Is.EqualTo("enabled = false"));
    }

    [Test]
    public async Task RestoreAsync_EmptyList_Fails()
    {
        var result = await new ModConfigBackupRestorer().RestoreAsync([]);

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Message, Is.EqualTo("没有可恢复的快照。"));
    }

    [Test]
    public async Task RestoreAsync_PartialSuccess_KeepsFailures()
    {
        using var workspace = new TempWorkspace("modconfig-restore-partial");
        var original = workspace.Combine("settings.cfg");
        var backup = original + ".ckan-ui-20240102-030405.bak";
        await File.WriteAllTextAsync(original, "enabled = false");
        await File.WriteAllTextAsync(backup, "enabled = true");
        var missing = workspace.Combine("other.cfg") + ".ckan-ui-20240102-030405.bak";

        var result = await new ModConfigBackupRestorer().RestoreAsync([backup, missing]);

        Assert.That(result.IsSuccess, Is.True);
        Assert.That(result.Value!.RestoredPaths, Is.EqualTo(new[] { original }));
        Assert.That(result.Value.Failures.Count, Is.EqualTo(1));
        Assert.That(result.Value.Failures[0], Does.Contain("other.cfg"));
    }

    [Test]
    public async Task RestoreAsync_RejectsNullList()
    {
        Assert.ThrowsAsync<ArgumentNullException>(
            async () => await new ModConfigBackupRestorer().RestoreAsync(null!));
    }
}
