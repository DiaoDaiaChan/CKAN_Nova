using System.IO.Compression;
using System.Text.Json;

using CkanUI.Services.Abstractions.Dtos;
using CkanUI.Services.ModPacks;
using CkanUI.TestSupport;

using NUnit.Framework;

namespace CkanUI.Services.Tests;

/// <summary>
/// <see cref="ModPackCatalog"/> 的离线用例（12 篇 F-39/F-45、UI-20/UI-24）：
/// 导出往返、路径校验、写入失败可重试、预览分类（会装/会升/会降/已装/找不到/冲突）、实例比对三分支。
/// 全部走 <see cref="FakeModPackStore"/>，不联网、不碰真实注册表、不写系统临时目录。
/// </summary>
[TestFixture]
public sealed class ModPackCatalogTests
{
    private TempWorkspace _workspace = null!;
    private FakeModPackStore _store = null!;
    private ModPackCatalog _catalog = null!;

    [SetUp]
    public void SetUp()
    {
        _workspace = new TempWorkspace("modpack-catalog");
        _store = new FakeModPackStore();
        _catalog = new ModPackCatalog(_store);
    }

    [TearDown]
    public void TearDown() => _workspace.Dispose();

    // ---------------- 导出 ----------------

    [Test]
    public void Export_NullRequest_Fails()
    {
        var result = _catalog.Export(null!);

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Message, Is.EqualTo("导出请求不能为空。"));
    }

    [Test]
    public void Export_NoActiveInstance_FailsRetryable()
    {
        _store.HasActiveInstance = false;

        var result = _catalog.Export(new ModPackExportRequest(_workspace.Combine("out.ckan")));

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Message, Is.EqualTo(ModPackCatalog.NoInstanceMessage));
        Assert.That(result.Retryable, Is.True);
    }

    [Test]
    public void Export_EmptyTargetPath_Fails()
    {
        _store.SeedInstalled("A", "1.0");

        var result = _catalog.Export(new ModPackExportRequest("   "));

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Message, Is.EqualTo("导出路径不能为空。"));
    }

    [Test]
    public void Export_InvalidPathCharacters_Fails()
    {
        _store.SeedInstalled("A", "1.0");

        var result = _catalog.Export(new ModPackExportRequest(_workspace.Combine("bad|name.ckan")));

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Message, Is.EqualTo("导出路径含有非法字符。"));
    }

    [Test]
    public void Export_MissingDirectory_Fails()
    {
        _store.SeedInstalled("A", "1.0");

        var result = _catalog.Export(new ModPackExportRequest(_workspace.Combine("没有这个目录", "out.ckan")));

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Message, Does.StartWith("导出目录不存在："));
    }

    [Test]
    public void Export_EmptyInstance_Fails()
    {
        var result = _catalog.Export(new ModPackExportRequest(_workspace.Combine("out.ckan")));

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Message, Is.EqualTo("当前实例还没有已安装的模组，没有可导出的内容。"));
    }

    [Test]
    public void Export_WritesUtf8WithoutBomAndCountsModules()
    {
        _store.SeedInstalled("B", "2.0");
        _store.SeedInstalled("A", "1.0");
        var target = _workspace.Combine("mods.ckan");

        var result = _catalog.Export(new ModPackExportRequest(target));

        Assert.That(result.IsSuccess, Is.True, result.Message);
        Assert.That(result.Value!.ModuleCount, Is.EqualTo(2));
        Assert.That(result.Value.Path, Is.EqualTo(target));
        Assert.That(File.Exists(target), Is.True);
        var bytes = File.ReadAllBytes(target);
        Assert.That(bytes.Length > 2 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF, Is.False,
                    "导出的 .ckan 不应带 UTF-8 BOM（上游 CkanModule.ToJson 不带）。");
        Assert.That(result.Value.FileBytes, Is.GreaterThan(0));
    }

    [Test]
    public void Export_AppendsCkanExtensionWhenMissing()
    {
        _store.SeedInstalled("A", "1.0");

        var result = _catalog.Export(new ModPackExportRequest(_workspace.Combine("modpack")));

        Assert.That(result.IsSuccess, Is.True, result.Message);
        Assert.That(result.Value!.Path, Does.EndWith("modpack.ckan"));
        Assert.That(File.Exists(result.Value.Path), Is.True);
    }

    [Test]
    public void Export_SkipsAutoInstalledModules()
    {
        _store.SeedInstalled("Manual", "1.0");
        _store.SeedInstalled("Auto", "1.0", autoInstalled: true);

        var result = _catalog.Export(new ModPackExportRequest(_workspace.Combine("mods.ckan")));

        Assert.That(result.IsSuccess, Is.True, result.Message);
        Assert.That(result.Value!.ModuleCount, Is.EqualTo(1));
        var text = File.ReadAllText(result.Value.Path);
        Assert.That(text, Does.Contain("Manual"));
        Assert.That(text, Does.Not.Contain("\"Auto\""));
    }


    [TestCase(ExportListFormat.PlainText, ".txt")]
    [TestCase(ExportListFormat.Markdown, ".md")]
    [TestCase(ExportListFormat.BbCode, ".txt")]
    [TestCase(ExportListFormat.Csv, ".csv")]
    [TestCase(ExportListFormat.Tsv, ".tsv")]
    public void Export_NonCkanFormats_AppendsDefaultExtensionAndWritesContent(ExportListFormat format, string expectedExtension)
    {
        _store.SeedInstalled("ModA", "1.0");
        _store.SeedInstalled("ModB", "2.0");

        var targetWithoutExt = _workspace.Combine("export_test");
        var result = _catalog.Export(new ModPackExportRequest(targetWithoutExt, Format: format));

        Assert.That(result.IsSuccess, Is.True, result.Message);
        Assert.That(result.Value!.Path, Does.EndWith(expectedExtension));
        Assert.That(File.Exists(result.Value.Path), Is.True);
        Assert.That(result.Value.ModuleCount, Is.EqualTo(2));
        var content = File.ReadAllText(result.Value.Path);
        Assert.That(content, Does.Contain($"Exported:{format}"));
    }

    [Test]
    public void Export_PassesFlagsToStore()
    {
        _store.SeedInstalled("A", "1.0");

        var result = _catalog.Export(new ModPackExportRequest(
            _workspace.Combine("mods.ckan"), WithVersions: false, BundleDownloads: false, UseRecommends: true));

        Assert.That(result.IsSuccess, Is.True, result.Message);
        Assert.That(_store.LastUseRecommends, Is.True);
        Assert.That(_store.LastWithVersions, Is.False);
        var text = File.ReadAllText(result.Value!.Path);
        Assert.That(text, Does.Contain("recommends"));
        Assert.That(text, Does.Not.Contain("depends"));
    }

    [Test]
    public void Export_InstalledReadFailure_IsRetryable()
    {
        _store.InstalledFailure = new IOException("注册表被占用");

        var result = _catalog.Export(new ModPackExportRequest(_workspace.Combine("mods.ckan")));

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Message, Is.EqualTo("读取当前实例的已安装模组失败。"));
        Assert.That(result.Retryable, Is.True);
    }

    [Test]
    public void Export_TargetIsExistingDirectory_FailsRetryable()
    {
        _store.SeedInstalled("A", "1.0");
        var blocked = _workspace.Combine("blocked.ckan");
        Directory.CreateDirectory(blocked);

        var result = _catalog.Export(new ModPackExportRequest(blocked));

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Retryable, Is.True, "写入失败必须标记可重试，UI 才能提示重试。");
    }

    [Test]
    public void Export_BundleDownloads_WritesZipWithMetadataAndArchives()
    {
        _store.SeedInstalled("A", "1.0");
        _store.SeedInstalled("B", "2.0");
        _store.SeedArchive("A", "1.0", _workspace.Combine("A-1.0.zip"));
        _store.SeedArchive("B", "2.0", _workspace.Combine("B-2.0.zip"));
        var target = _workspace.Combine("mods.ckan");

        var result = _catalog.Export(new ModPackExportRequest(target, BundleDownloads: true));

        Assert.That(result.IsSuccess, Is.True, result.Message);
        Assert.That(result.Value!.BundledCount, Is.EqualTo(2));
        Assert.That(result.Value.ArchivePath, Is.Not.Null);
        Assert.That(File.Exists(result.Value.ArchivePath!), Is.True);
        using var zip = ZipFile.OpenRead(result.Value.ArchivePath!);
        var names = zip.Entries.Select(entry => entry.FullName).ToList();
        Assert.That(names, Has.Some.EndsWith("mods.ckan"));
        Assert.That(names, Has.Some.EqualTo("Downloads/A-1.0.zip"));
        Assert.That(names, Has.Some.EqualTo("Downloads/B-2.0.zip"));
        Assert.That(result.Value.Warnings, Is.Empty);
    }

    [Test]
    public void Export_BundleDownloads_MissingArchiveWarnsButSucceeds()
    {
        _store.SeedInstalled("A", "1.0");
        _store.SeedInstalled("B", "2.0");
        _store.SeedArchive("A", "1.0", _workspace.Combine("A-1.0.zip"));

        var result = _catalog.Export(new ModPackExportRequest(_workspace.Combine("mods.ckan"), BundleDownloads: true));

        Assert.That(result.IsSuccess, Is.True, result.Message);
        Assert.That(result.Value!.BundledCount, Is.EqualTo(1));
        Assert.That(result.Value.Warnings.Count, Is.EqualTo(1));
        Assert.That(result.Value.Warnings[0], Does.Contain("B"));
    }

    [Test]
    public void Export_BundleDownloads_NoCachedArchive_AddsHint()
    {
        _store.SeedInstalled("A", "1.0");

        var result = _catalog.Export(new ModPackExportRequest(_workspace.Combine("mods.ckan"), BundleDownloads: true));

        Assert.That(result.IsSuccess, Is.True, result.Message);
        Assert.That(result.Value!.BundledCount, Is.EqualTo(0));
        Assert.That(result.Value.Warnings, Has.Some.Contains("只有元数据包本身"));
    }

    [Test]
    public void Export_ThenPreview_YieldsSameSet()
    {
        _store.SeedInstalled("Alpha", "1.2.3");
        _store.SeedInstalled("Beta", "0.9");
        var export = _catalog.Export(new ModPackExportRequest(_workspace.Combine("mods.ckan")));
        Assert.That(export.IsSuccess, Is.True, export.Message);

        // 模拟「在另一个干净实例上导入」：当前实例没有任何已装模组，但仓库索引里有这两个版本。
        _store.Installed.Clear();
        _store.SeedAvailable("Alpha", "1.2.3");
        _store.SeedAvailable("Beta", "0.9");

        var preview = _catalog.Preview(new[] { export.Value!.Path });

        Assert.That(preview.IsSuccess, Is.True, preview.Message);
        var entries = preview.Value!.Entries;
        Assert.That(entries.Select(entry => entry.Identifier), Is.EqualTo(new[] { "Alpha", "Beta" }));
        Assert.That(entries.Select(entry => entry.RequestedVersion), Is.EqualTo(new[] { "1.2.3", "0.9" }));
        Assert.That(entries.All(entry => entry.State == ModPackEntryState.Install), Is.True);
        Assert.That(entries.All(entry => entry.IsSelectable), Is.True);
    }

    // ---------------- 导入预览 ----------------

    [Test]
    public void Preview_NoSources_Fails()
    {
        var result = _catalog.Preview(Array.Empty<string>());

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Message, Is.EqualTo("请至少选择一个要导入的 .ckan 文件。"));
    }

    [Test]
    public void Preview_OnlyUnusablePaths_FailsWithWarningsInDetail()
    {
        var textFile = _workspace.Combine("notes.txt");
        File.WriteAllText(textFile, "hello");

        var result = _catalog.Preview(new[]
        {
            "   ",
            _workspace.Combine("missing.ckan"),
            textFile,
        });

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Message, Is.EqualTo("没有可导入的 .ckan 文件。"));
        Assert.That(result.Detail, Does.Contain("跳过不存在的文件"));
        Assert.That(result.Detail, Does.Contain("跳过不是 .ckan 的文件"));
    }

    [Test]
    public void Preview_NewModule_ClassifiedAsInstall()
    {
        _store.SeedAvailable("NewMod", "3.1");
        var file = WriteModPack("new.ckan", ("NewMod", "3.1"));

        var result = _catalog.Preview(new[] { file });

        Assert.That(result.IsSuccess, Is.True, result.Message);
        var entry = result.Value!.Entries.Single();
        Assert.That(entry.State, Is.EqualTo(ModPackEntryState.Install));
        Assert.That(entry.TargetVersion, Is.EqualTo("3.1"));
        Assert.That(entry.SourceName, Is.EqualTo("new.ckan"));
    }

    [Test]
    public void Preview_UnknownModule_ClassifiedAsMissing()
    {
        var file = WriteModPack("ghost.ckan", ("GhostMod", "1.0"));

        var result = _catalog.Preview(new[] { file });

        Assert.That(result.IsSuccess, Is.True, result.Message);
        var entry = result.Value!.Entries.Single();
        Assert.That(entry.State, Is.EqualTo(ModPackEntryState.Missing));
        Assert.That(entry.Reason, Is.EqualTo("仓库索引里找不到这个模组。"));
        Assert.That(entry.IsSelectable, Is.False);
    }

    [Test]
    public void Preview_VersionNotInRepository_ClassifiedAsConflict()
    {
        _store.SeedInstalled("Mod", "1.0");
        _store.SeedAvailable("Mod", latest: "2.0", requestedExists: false);
        var file = WriteModPack("old.ckan", ("Mod", "0.5"));

        var result = _catalog.Preview(new[] { file });

        Assert.That(result.IsSuccess, Is.True, result.Message);
        var entry = result.Value!.Entries.Single();
        Assert.That(entry.State, Is.EqualTo(ModPackEntryState.Conflict));
        Assert.That(entry.Reason, Does.Contain("没有 0.5 这个版本"));
        Assert.That(entry.Reason, Does.Contain("2.0"));
    }

    [Test]
    public void Preview_HigherRequestedVersion_ClassifiedAsUpgrade()
    {
        _store.SeedInstalled("Mod", "1.0");
        _store.SeedAvailable("Mod", "2.0");
        var file = WriteModPack("up.ckan", ("Mod", "2.0"));

        var result = _catalog.Preview(new[] { file });

        var entry = result.Value!.Entries.Single();
        Assert.That(entry.State, Is.EqualTo(ModPackEntryState.Upgrade));
        Assert.That(entry.InstalledVersion, Is.EqualTo("1.0"));
        Assert.That(entry.TargetVersion, Is.EqualTo("2.0"));
    }

    [Test]
    public void Preview_LowerRequestedVersion_ClassifiedAsDowngrade()
    {
        _store.SeedInstalled("Mod", "2.0");
        _store.SeedAvailable("Mod", "2.0");
        var file = WriteModPack("down.ckan", ("Mod", "1.0"));

        var result = _catalog.Preview(new[] { file });

        var entry = result.Value!.Entries.Single();
        Assert.That(entry.State, Is.EqualTo(ModPackEntryState.Downgrade));
        Assert.That(entry.Reason, Does.Contain("降级"));
    }

    [Test]
    public void Preview_SameVersion_ClassifiedAsSameVersionAndNotSelectable()
    {
        _store.SeedInstalled("Mod", "1.0");
        _store.SeedAvailable("Mod", "1.0");
        var file = WriteModPack("same.ckan", ("Mod", "1.0"));

        var result = _catalog.Preview(new[] { file });

        var entry = result.Value!.Entries.Single();
        Assert.That(entry.State, Is.EqualTo(ModPackEntryState.SameVersion));
        Assert.That(entry.IsSelectable, Is.False);
    }

    [Test]
    public void Preview_UnpinnedReference_UsesLatestVersion()
    {
        _store.SeedAvailable("Mod", "4.2");
        var file = WriteModPack("loose.ckan", ("Mod", null));

        var result = _catalog.Preview(new[] { file });

        var entry = result.Value!.Entries.Single();
        Assert.That(entry.State, Is.EqualTo(ModPackEntryState.Install));
        Assert.That(entry.TargetVersion, Is.EqualTo("4.2"));
    }

    [Test]
    public void Preview_TwoSourcesWithDifferentVersions_ReportsConflictOnce()
    {
        _store.SeedAvailable("Mod", "2.0");
        var first = WriteModPack("first.ckan", ("Mod", "1.0"));
        var second = WriteModPack("second.ckan", ("Mod", "2.0"));

        var result = _catalog.Preview(new[] { first, second });

        Assert.That(result.IsSuccess, Is.True, result.Message);
        var preview = result.Value!;
        Assert.That(preview.Conflicts.Count, Is.EqualTo(1));
        Assert.That(preview.Conflicts[0], Does.Contain("不同版本"));
        Assert.That(preview.HasConflicts, Is.True);
        var conflict = preview.Entries.Single(entry => entry.State == ModPackEntryState.Conflict);
        Assert.That(conflict.Identifier, Is.EqualTo("Mod"));
    }

    [Test]
    public void Preview_DuplicateSameVersionAcrossFiles_EmitsSingleRow()
    {
        _store.SeedAvailable("Mod", "1.0");
        var first = WriteModPack("a.ckan", ("Mod", "1.0"));
        var second = WriteModPack("b.ckan", ("Mod", "1.0"));

        var result = _catalog.Preview(new[] { first, second });

        Assert.That(result.Value!.Entries.Count, Is.EqualTo(1));
        Assert.That(result.Value.Conflicts, Is.Empty);
    }

    [Test]
    public void Preview_ProblemsSortBeforeInstallableRows()
    {
        _store.SeedAvailable("Good", "1.0");
        var file = WriteModPack("mixed.ckan", ("Ghost", "1.0"), ("Good", "1.0"));

        var result = _catalog.Preview(new[] { file });

        Assert.That(result.Value!.Entries[0].Identifier, Is.EqualTo("Ghost"));
        Assert.That(result.Value.Entries[0].State, Is.EqualTo(ModPackEntryState.Missing));
        Assert.That(result.Value.Entries[1].Identifier, Is.EqualTo("Good"));
    }

    [Test]
    public void Preview_BrokenFile_WarnsAndKeepsGoodFile()
    {
        _store.SeedAvailable("Good", "1.0");
        var broken = WriteModPack("broken.ckan", ("X", "1.0"));
        _store.BrokenFiles.Add("broken.ckan");
        var good = WriteModPack("good.ckan", ("Good", "1.0"));

        var result = _catalog.Preview(new[] { broken, good });

        Assert.That(result.IsSuccess, Is.True, result.Message);
        Assert.That(result.Value!.Warnings, Has.Some.Contains("无法解析 broken.ckan"));
        Assert.That(result.Value.Entries.Single().Identifier, Is.EqualTo("Good"));
    }

    [Test]
    public void Preview_MultipleFiles_NamesPreviewByCount()
    {
        _store.SeedAvailable("A", "1.0");
        _store.SeedAvailable("B", "1.0");
        var first = WriteModPack("a.ckan", ("A", "1.0"));
        var second = WriteModPack("b.ckan", ("B", "1.0"));

        var result = _catalog.Preview(new[] { first, second });

        Assert.That(result.Value!.Name, Is.EqualTo("2 个元数据包"));
        Assert.That(result.Value.Sources.Count, Is.EqualTo(2));
        Assert.That(result.Value.SelectableCount, Is.EqualTo(2));
    }

    [Test]
    public void Preview_EmptyInstance_AllEntriesInstallable()
    {
        _store.SeedAvailable("A", "1.0");
        _store.SeedAvailable("B", "2.0");
        var file = WriteModPack("pair.ckan", ("A", "1.0"), ("B", "2.0"));

        var result = _catalog.Preview(new[] { file });

        Assert.That(result.Value!.Entries.Count, Is.EqualTo(2));
        Assert.That(result.Value.SelectableCount, Is.EqualTo(2));
        Assert.That(result.Value.IsEmpty, Is.False);
    }

    // ---------------- 实例比对 ----------------

    [Test]
    public void Compare_BlankArguments_Fails()
    {
        var result = _catalog.Compare("  ", "B");

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Message, Is.EqualTo("请选择要对比的两个实例。"));
    }

    [Test]
    public void Compare_SameInstance_Fails()
    {
        var result = _catalog.Compare("Same", "same");

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Message, Is.EqualTo("请选择两个不同的实例进行对比。"));
    }

    [Test]
    public void Compare_UnknownInstance_FailsRetryable()
    {
        _store.SeedInstance("A", ("X", "1.0"));

        var result = _catalog.Compare("A", "Missing");

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Message, Does.Contain("Missing"));
        Assert.That(result.Retryable, Is.True);
    }

    [Test]
    public void Compare_ThreeBranches()
    {
        _store.SeedInstance("A", ("OnlyLeft", "1.0"), ("Shared", "1.0"), ("Moved", "2.0"));
        _store.SeedInstance("B", ("OnlyRight", "1.0"), ("Shared", "1.0"), ("Moved", "3.0"));

        var result = _catalog.Compare("A", "B");

        Assert.That(result.IsSuccess, Is.True, result.Message);
        var comparison = result.Value!;
        Assert.That(comparison.IsIdentical, Is.False);
        Assert.That(comparison.Items.Count, Is.EqualTo(3));
        Assert.That(comparison.Items.Select(item => item.Identifier), Is.EqualTo(new[] { "Moved", "OnlyLeft", "OnlyRight" }));
        Assert.That(comparison.Items[0].Kind, Is.EqualTo(InstanceDiffKind.VersionDiffers));
        Assert.That(comparison.Items[0].LeftVersion, Is.EqualTo("2.0"));
        Assert.That(comparison.Items[0].RightVersion, Is.EqualTo("3.0"));
        Assert.That(comparison.Items[1].Kind, Is.EqualTo(InstanceDiffKind.OnlyInLeft));
        Assert.That(comparison.Items[2].Kind, Is.EqualTo(InstanceDiffKind.OnlyInRight));
    }

    [Test]
    public void Compare_BothEmpty_IsIdentical()
    {
        _store.SeedInstance("A");
        _store.SeedInstance("B");

        var result = _catalog.Compare("A", "B");

        Assert.That(result.IsSuccess, Is.True, result.Message);
        Assert.That(result.Value!.IsIdentical, Is.True);
        Assert.That(result.Value.Items, Is.Empty);
    }

    [Test]
    public void CompareSnapshots_TreatsTrailingZeroVersionAsEqual()
    {
        var left = new[] { new InstalledModuleInfo("Mod", "1.0", false, null, null) };
        var right = new[] { new InstalledModuleInfo("Mod", "1.0.0", false, null, null) };

        var items = ModPackCatalog.CompareSnapshots(left, right);

        Assert.That(items, Is.Empty);
    }

    [Test]
    public void CompareSnapshots_IgnoresCaseDifferencesInIdentifier()
    {
        var left = new[] { new InstalledModuleInfo("ModA", "1.0", false, null, null) };
        var right = new[] { new InstalledModuleInfo("moda", "1.0", false, null, null) };

        Assert.That(ModPackCatalog.CompareSnapshots(left, right), Is.Empty);
    }

    private string WriteModPack(string fileName, params (string Identifier, string? Version)[] entries)
    {
        var depends = entries
            .Select(entry => entry.Version is null
                ? new Dictionary<string, string> { ["name"] = entry.Identifier }
                : new Dictionary<string, string> { ["name"] = entry.Identifier, ["version"] = entry.Version })
            .ToList();

        var document = new Dictionary<string, object>
        {
            ["spec_version"] = "v1.6",
            ["identifier"] = "TestModpack",
            ["name"] = "测试元数据包",
            ["version"] = "1.0",
            ["kind"] = "metapackage",
            ["depends"] = depends,
        };

        var path = _workspace.Combine(fileName);
        File.WriteAllText(path, JsonSerializer.Serialize(document));
        return path;
    }
}
