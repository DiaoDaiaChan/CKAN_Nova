using System.Text;
using CkanUI.Services.Abstractions;
using CkanUI.Services.Abstractions.Dtos;
using CkanUI.Services.Saves;
using CkanUI.Sfs;
using CkanUI.TestSupport;
using NUnit.Framework;

namespace CkanUI.Services.Tests;

/// <summary>
/// 存档服务测试（13 篇 §7 / 06 篇 E1）：枚举、诊断汇总、格式化落盘与快照保护。
/// </summary>
[TestFixture]
public sealed class SaveFileServiceTests
{
    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    [Test]
    public async Task ListSaves_MissingDirectory_IsEmptyNotError()
    {
        using var temp = new TempWorkspace("saves-list-missing");
        var service = new SaveFileService(new ChangeSetFakeErrorReporter());
        var gameDir = temp.Combine("game");

        var result = await service.ListSavesAsync(gameDir);

        Assert.That(result.IsSuccess, Is.True);
        Assert.That(result.Value, Is.Empty);
        Assert.That(service.GetSavesDirectory(gameDir), Is.EqualTo(Path.Combine(gameDir, "saves")));
    }

    [Test]
    public async Task ListSaves_IndexesSavesAndBackups_SkipsToolOutput()
    {
        using var temp = new TempWorkspace("saves-list-index");
        var saves = Directory.CreateDirectory(temp.Combine("game", "saves")).FullName;
        Write(Path.Combine(saves, "persistent.sfs"), ValidSave("主力存档"));
        Write(Path.Combine(saves, "persistent.sfs.bak"), ValidSave("旧备份"));
        Write(Path.Combine(saves, "persistent.formatted.sfs"), ValidSave("工具产物"));
        Write(Path.Combine(saves, "persistent.pre-edit-20240101-000000.sfs"), ValidSave("写前快照"));
        Write(Path.Combine(saves, "persistent.tmp-deadbeef.sfs"), ValidSave("临时文件"));
        Directory.CreateDirectory(Path.Combine(saves, "Backup"));
        Write(Path.Combine(saves, "Backup", "old.sfs"), ValidSave("KSP 备份"));
        Directory.CreateDirectory(Path.Combine(saves, "slot2"));
        Write(Path.Combine(saves, "slot2", "quicksave.sfs"), ValidSave("快速存档"));
        Write(Path.Combine(saves, "notes.txt"), "不是存档");

        var service = new SaveFileService(new ChangeSetFakeErrorReporter());
        var result = await service.ListSavesAsync(temp.Combine("game"));

        Assert.That(result.IsSuccess, Is.True);
        var rows = result.Value!;
        Assert.That(
            rows.Select(row => Path.GetFileName(row.FilePath)),
            Is.EquivalentTo(new[] { "persistent.sfs", "persistent.sfs.bak", "quicksave.sfs" }));
        Assert.That(rows.Single(row => row.IsBackup).FilePath, Does.EndWith("persistent.sfs.bak"));
        Assert.That(
            rows.Single(row => Path.GetFileName(row.FilePath) == "quicksave.sfs").SlotName,
            Is.EqualTo("slot2 / quicksave"));
        // 快照数 = 写前快照（pre-edit）+ Backup/ 里的 .sfs，两类合计。
        Assert.That(rows.Single(row => Path.GetFileName(row.FilePath) == "persistent.sfs").SnapshotCount,
            Is.EqualTo(2));
    }

    [Test]
    public async Task ListSaves_MergesLoadMetaWithHeader()
    {
        using var temp = new TempWorkspace("saves-list-meta");
        var saves = Directory.CreateDirectory(temp.Combine("game", "saves")).FullName;
        var savePath = Path.Combine(saves, "persistent.sfs");
        Write(savePath, ValidSave("远航号"));
        Write(
            Path.ChangeExtension(savePath, ".loadmeta"),
            string.Join("\r\n", new[]
            {
                "vesselCount = 3",
                "UT = 7000.5",
                "gameMode = CAREER",
                "kspVersion = 1.12.5",
                "funds = 98765.43",
                string.Empty,
            }));

        var service = new SaveFileService(new ChangeSetFakeErrorReporter());
        var result = await service.ListSavesAsync(temp.Combine("game"));

        Assert.That(result.IsSuccess, Is.True);
        var row = result.Value!.Single();
        Assert.That(row.HasLoadMeta, Is.True);
        Assert.That(row.Title, Is.EqualTo("远航号"));
        Assert.That(row.Mode, Is.EqualTo("CAREER"));
        Assert.That(row.GameVersion, Is.EqualTo("1.12.5"));
        Assert.That(row.Scene, Is.EqualTo("space"));
        Assert.That(row.UniversalTime, Is.EqualTo("7000.5"));
        Assert.That(row.VesselCount, Is.EqualTo(3));
        Assert.That(row.Funds, Is.EqualTo("98765.43"));
        Assert.That(row.SizeBytes, Is.GreaterThan(0));
    }

    [Test]
    public async Task ListSaves_WithoutLoadMeta_FallsBackToHeaderOnly()
    {
        using var temp = new TempWorkspace("saves-list-header");
        var saves = Directory.CreateDirectory(temp.Combine("game", "saves")).FullName;
        Write(Path.Combine(saves, "persistent.sfs"), ValidSave("无元数据"));

        var service = new SaveFileService(new ChangeSetFakeErrorReporter());
        var result = await service.ListSavesAsync(temp.Combine("game"));

        var row = result.Value!.Single();
        Assert.That(row.HasLoadMeta, Is.False);
        Assert.That(row.Title, Is.EqualTo("无元数据"));
        Assert.That(row.Mode, Is.EqualTo("CAREER"));
        Assert.That(row.VesselCount, Is.Null);
        Assert.That(row.Funds, Is.Null);
    }

    [Test]
    public async Task Validate_CorruptSave_ReportsDiagnosticsInsteadOfThrowing()
    {
        using var temp = new TempWorkspace("saves-validate-broken");
        var saves = Directory.CreateDirectory(temp.Combine("game", "saves")).FullName;
        var savePath = Path.Combine(saves, "broken.sfs");
        Write(savePath, string.Join("\r\n", new[] { "GAME", "{", "\tname = broken", "\tTitle = 坏存档", string.Empty }));

        var service = new SaveFileService(new ChangeSetFakeErrorReporter());
        var result = await service.ValidateAsync(savePath);

        Assert.That(result.IsSuccess, Is.True);
        var report = result.Value!;
        Assert.That(report.HasErrors, Is.True);
        Assert.That(report.Diagnostics, Is.Not.Empty);
        Assert.That(report.Diagnostics.Select(diagnostic => diagnostic.Code), Does.Contain("V-01"));
        Assert.That(report.Diagnostics.Any(diagnostic => diagnostic.Line > 0), Is.True);
        Assert.That(report.Diagnostics.Any(diagnostic => diagnostic.Severity == SaveDiagnosticSeverity.Error), Is.True);
    }

    [Test]
    public async Task Validate_CleanSave_IsCleanAndCountsNodes()
    {
        using var temp = new TempWorkspace("saves-validate-clean");
        var saves = Directory.CreateDirectory(temp.Combine("game", "saves")).FullName;
        var savePath = Path.Combine(saves, "persistent.sfs");
        Write(savePath, ValidSave("干净存档"));

        var service = new SaveFileService(new ChangeSetFakeErrorReporter());
        var result = await service.ValidateAsync(savePath);

        Assert.That(result.IsSuccess, Is.True);
        var report = result.Value!;
        Assert.That(report.IsClean, Is.True, string.Join(" | ", report.Diagnostics.Select(diagnostic => diagnostic.ToString())));
        Assert.That(report.ErrorCount, Is.EqualTo(0));
        Assert.That(report.NodeCount, Is.EqualTo(2));
        Assert.That(report.PairCount, Is.EqualTo(8));
        Assert.That(report.MaxDepth, Is.EqualTo(2));
        Assert.That(report.HasBom, Is.False);
        Assert.That(report.DominantNewLine, Is.EqualTo("\r\n"));
    }

    [Test]
    public async Task Validate_MissingFile_FailsWithoutThrowing()
    {
        using var temp = new TempWorkspace("saves-validate-missing");
        var service = new SaveFileService(new ChangeSetFakeErrorReporter());

        var result = await service.ValidateAsync(temp.Combine("game", "saves", "nope.sfs"));

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Message, Does.Contain("不存在"));
        Assert.That(result.Retryable, Is.False);
    }

    [Test]
    public async Task Validate_IsCheckSemantics_AndNeverWritesFiles()
    {
        using var temp = new TempWorkspace("saves-validate-nowrite");
        var saves = Directory.CreateDirectory(temp.Combine("game", "saves")).FullName;
        var savePath = Path.Combine(saves, "persistent.sfs");
        var original = ValidSave("只校验");
        Write(savePath, original);
        var before = Directory.GetFiles(saves).OrderBy(name => name, StringComparer.Ordinal).ToArray();

        var service = new SaveFileService(new ChangeSetFakeErrorReporter());
        await service.ValidateAsync(savePath);

        Assert.That(Directory.GetFiles(saves).OrderBy(name => name, StringComparer.Ordinal), Is.EqualTo(before));
        Assert.That(File.ReadAllText(savePath), Is.EqualTo(original));
    }

    [Test]
    public async Task Format_Preview_DoesNotTouchDisk()
    {
        using var temp = new TempWorkspace("saves-format-preview");
        var saves = Directory.CreateDirectory(temp.Combine("game", "saves")).FullName;
        var savePath = Path.Combine(saves, "persistent.sfs");
        var original = ValidSave("预览");
        Write(savePath, original);

        var service = new SaveFileService(new ChangeSetFakeErrorReporter());
        var result = await service.FormatAsync(new SaveFormatRequest(savePath, SaveFormatMode.Preview));

        Assert.That(result.IsSuccess, Is.True);
        var outcome = result.Value!;
        Assert.That(outcome.Refused, Is.False);
        Assert.That(outcome.WroteFile, Is.False);
        Assert.That(outcome.Preview, Is.Not.Null.And.Not.Empty);
        Assert.That(Directory.GetFiles(saves).Length, Is.EqualTo(1));
        Assert.That(File.ReadAllText(savePath), Is.EqualTo(original));
    }

    [Test]
    public async Task Format_NewFile_WritesCopyAndLeavesOriginalUntouched()
    {
        using var temp = new TempWorkspace("saves-format-newfile");
        var saves = Directory.CreateDirectory(temp.Combine("game", "saves")).FullName;
        var savePath = Path.Combine(saves, "persistent.sfs");
        var original = ValidSave("副本");
        Write(savePath, original);

        var service = new SaveFileService(new ChangeSetFakeErrorReporter());
        var result = await service.FormatAsync(new SaveFormatRequest(savePath));

        Assert.That(result.IsSuccess, Is.True);
        var outcome = result.Value!;
        Assert.That(outcome.Refused, Is.False);
        Assert.That(outcome.WroteFile, Is.True);
        Assert.That(outcome.WrittenPath, Is.EqualTo(Path.Combine(saves, "persistent.formatted.sfs")));
        Assert.That(outcome.SemanticsPreserved, Is.True);
        Assert.That(outcome.BackupPath, Is.Null);
        Assert.That(File.Exists(outcome.WrittenPath!), Is.True);
        Assert.That(File.ReadAllText(savePath), Is.EqualTo(original));
    }

    [Test]
    public async Task Format_PreservesSemanticValues()
    {
        using var temp = new TempWorkspace("saves-format-semantics");
        var saves = Directory.CreateDirectory(temp.Combine("game", "saves")).FullName;
        var savePath = Path.Combine(saves, "persistent.sfs");
        Write(savePath, ValidSave("语义值"));

        var service = new SaveFileService(new ChangeSetFakeErrorReporter());
        var result = await service.FormatAsync(new SaveFormatRequest(savePath));
        Assert.That(result.IsSuccess, Is.True);

        var before = SfsValueIndex.Build(SfsParser.ParseFile(savePath).Document!);
        var after = SfsValueIndex.Build(SfsParser.ParseFile(result.Value!.WrittenPath!).Document!);

        Assert.That(after, Is.EqualTo(before));
        // 值索引格式：path \u001F key \u001F value；根节点自身的键路径为空串，
        // 子节点路径形如 /SCENARIO#0（同层同名节点带出现序号）。
        Assert.That(after, Does.Contain("\u001FTitle\u001F语义值"));
        Assert.That(after, Does.Contain("/SCENARIO#0\u001Ffunds\u001F123456.789"));
    }

    [Test]
    public async Task Format_Overwrite_SnapshotsFirstAndKeepsExistingBackup()
    {
        using var temp = new TempWorkspace("saves-format-overwrite");
        var saves = Directory.CreateDirectory(temp.Combine("game", "saves")).FullName;
        var savePath = Path.Combine(saves, "persistent.sfs");
        Write(savePath, ValidSave("覆盖写"));
        var backupPath = Path.Combine(saves, "persistent.sfs.bak");
        Write(backupPath, "原备份内容");

        var service = new SaveFileService(new ChangeSetFakeErrorReporter());
        var result = await service.FormatAsync(new SaveFormatRequest(savePath, SaveFormatMode.Overwrite));

        Assert.That(result.IsSuccess, Is.True);
        var outcome = result.Value!;
        Assert.That(outcome.Refused, Is.False);
        Assert.That(outcome.WroteFile, Is.True);
        Assert.That(outcome.WrittenPath, Is.EqualTo(savePath));
        Assert.That(outcome.BackupPath, Is.Not.Null);
        Assert.That(Path.GetFileName(outcome.BackupPath!), Does.StartWith("persistent.pre-edit-"));
        Assert.That(File.Exists(outcome.BackupPath!), Is.True);
        // 既有 .sfs.bak 是 KSP 自己的备份，本服务绝不覆盖它。
        Assert.That(File.ReadAllText(backupPath), Is.EqualTo("原备份内容"));
    }

    [Test]
    public async Task Format_Overwrite_PreservesOriginalLastWriteTime()
    {
        using var temp = new TempWorkspace("saves-format-stamp");
        var saves = Directory.CreateDirectory(temp.Combine("game", "saves")).FullName;
        var savePath = Path.Combine(saves, "persistent.sfs");
        Write(savePath, ValidSave("时间戳"));
        var stamp = new DateTime(2023, 5, 6, 7, 8, 9, DateTimeKind.Utc);
        File.SetLastWriteTimeUtc(savePath, stamp);

        var service = new SaveFileService(new ChangeSetFakeErrorReporter());
        var result = await service.FormatAsync(new SaveFormatRequest(savePath, SaveFormatMode.Overwrite));

        Assert.That(result.IsSuccess, Is.True);
        Assert.That(File.GetLastWriteTimeUtc(savePath), Is.EqualTo(stamp));
    }

    [Test]
    public async Task Format_RefusesBrokenStructure_AndWritesNothing()
    {
        using var temp = new TempWorkspace("saves-format-refuse");
        var saves = Directory.CreateDirectory(temp.Combine("game", "saves")).FullName;
        var savePath = Path.Combine(saves, "broken.sfs");
        var original = string.Join("\r\n", new[] { "GAME", "{", "\tname = broken", string.Empty });
        Write(savePath, original);

        var service = new SaveFileService(new ChangeSetFakeErrorReporter());
        var result = await service.FormatAsync(new SaveFormatRequest(savePath, SaveFormatMode.Overwrite));

        Assert.That(result.IsSuccess, Is.True);
        var outcome = result.Value!;
        Assert.That(outcome.Refused, Is.True);
        Assert.That(outcome.CanWrite, Is.False);
        Assert.That(outcome.WroteFile, Is.False);
        Assert.That(outcome.RefusalReason, Does.Contain("拒绝格式化"));
        Assert.That(Directory.GetFiles(saves).Length, Is.EqualTo(1));
        Assert.That(File.ReadAllText(savePath), Is.EqualTo(original));
    }

    [Test]
    public async Task Format_MissingFile_FailsWithoutThrowing()
    {
        using var temp = new TempWorkspace("saves-format-missing");
        var service = new SaveFileService(new ChangeSetFakeErrorReporter());

        var result = await service.FormatAsync(new SaveFormatRequest(temp.Combine("nope.sfs")));

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Message, Does.Contain("不存在"));
    }

    /// <summary>一份结构干净的最小存档：CRLF + Tab 缩进 + 无 BOM。</summary>
    private static string ValidSave(string title)
    {
        return string.Join("\r\n", new[]
        {
            "GAME",
            "{",
            "\tname = persistent",
            "\tTitle = " + title,
            "\tMode = CAREER",
            "\tversion = 1.12.5",
            "\tscene = space",
            "\tUT = 5005129.6190400701",
            "\tSCENARIO",
            "\t{",
            "\t\tname = ProgressTracking",
            "\t\tfunds = 123456.789",
            "\t}",
            "}",
            string.Empty,
        });
    }

    private static void Write(string path, string text)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text, Utf8NoBom);
    }
}
