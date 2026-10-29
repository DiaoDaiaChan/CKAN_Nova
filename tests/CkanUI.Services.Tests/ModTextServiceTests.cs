using System;
using System.IO;
using System.Threading.Tasks;
using CkanUI.ModText;
using CkanUI.Services.Abstractions;
using CkanUI.Services.ModText;
using CkanUI.TestSupport;
using NUnit.Framework;

namespace CkanUI.Services.Tests;

[TestFixture]
public sealed class ModTextServiceTests
{
    [Test]
    public async Task ReloadPacksAsync_LoadsLanguageAndOverrides()
    {
        using var workspace = new TempWorkspace();
        string baseDir = workspace.Path;

        string zhDir = Path.Combine(baseDir, "packs", "zh-cn");
        Directory.CreateDirectory(zhDir);

        ulong h1 = UI18n.Core.Hashing.Fnv1a.Hash("An autopilot mod");
        string line1 = $@"{{""h"":""{h1:x16}"",""n"":""ckan:MechJeb2:abstract"",""s"":""An autopilot mod"",""t"":""自动驾驶模组"",""mod"":""CKANMeta"",""ctx"":""MechJeb2"",""src"":""human""}}";
        await File.WriteAllTextAsync(Path.Combine(zhDir, "CKANMeta.jsonl"), line1 + Environment.NewLine);

        ulong h2 = UI18n.Core.Hashing.Fnv1a.Hash("Another mod");
        string overrideLine = $@"{{""h"":""{h2:x16}"",""n"":""ckan:OtherMod:abstract"",""s"":""Another mod"",""t"":""人工覆盖文本"",""mod"":""CKANMeta"",""ctx"":""OtherMod"",""src"":""human""}}";
        await File.WriteAllTextAsync(Path.Combine(baseDir, "packs", "overrides.jsonl"), overrideLine + Environment.NewLine);

        var service = new ModTextService(baseDir);
        await service.ReloadPacksAsync("zh-cn");

        Assert.That(service.TotalEntries, Is.EqualTo(2));

        var m1 = service.Provider.Find("MechJeb2", "abstract", "An autopilot mod");
        Assert.That(m1, Is.Not.Null);
        Assert.That(m1!.Text, Is.EqualTo("自动驾驶模组"));
        Assert.That(m1.Source, Is.EqualTo(ModTextSource.Human));

        var m2 = service.Provider.Find("OtherMod", "abstract", "Another mod");
        Assert.That(m2, Is.Not.Null);
        Assert.That(m2!.Text, Is.EqualTo("人工覆盖文本"));
    }

    [Test]
    public async Task PutOverrideAsync_WritesToFileAndUpdatesProvider()
    {
        using var workspace = new TempWorkspace();
        string baseDir = workspace.Path;

        var service = new ModTextService(baseDir);
        await service.ReloadPacksAsync("zh-cn");

        await service.PutOverrideAsync("MechJeb2", "abstract", "An autopilot mod", "自动驾驶模组覆盖");

        var found = service.Provider.Find("MechJeb2", "abstract", "An autopilot mod");
        Assert.That(found, Is.Not.Null);
        Assert.That(found!.Text, Is.EqualTo("自动驾驶模组覆盖"));

        string overridesPath = Path.Combine(baseDir, "packs", "overrides.jsonl");
        Assert.That(File.Exists(overridesPath), Is.True);
        string content = await File.ReadAllTextAsync(overridesPath);
        Assert.That(content, Does.Contain("自动驾驶模组覆盖"));
        Assert.That(content, Does.Contain("MechJeb2"));
    }

    [Test]
    public async Task GetAllEntries_ReturnsLoadedEntriesWithFilter()
    {
        using var workspace = new TempWorkspace();
        string baseDir = workspace.Path;

        string zhDir = Path.Combine(baseDir, "packs", "zh-cn");
        Directory.CreateDirectory(zhDir);

        ulong h1 = UI18n.Core.Hashing.Fnv1a.Hash("Pack mod");
        string line1 = $@"{{""h"":""{h1:x16}"",""n"":""ckan:PackMod:abstract"",""s"":""Pack mod"",""t"":""词包翻译"",""mod"":""CKANMeta"",""ctx"":""PackMod"",""src"":""human""}}";
        await File.WriteAllTextAsync(Path.Combine(zhDir, "CKANMeta.jsonl"), line1 + Environment.NewLine);

        var service = new ModTextService(baseDir);
        await service.ReloadPacksAsync("zh-cn");

        await service.PutOverrideAsync("OverrideMod", "description", "Override desc", "覆盖说明");

        var all = service.GetAllEntries();
        Assert.That(all.Count, Is.EqualTo(2));

        var filtered = service.GetAllEntries("覆盖说明");
        Assert.That(filtered.Count, Is.EqualTo(1));
        Assert.That(filtered[0].Translation, Is.EqualTo("覆盖说明"));
    }

    [Test]
    public async Task ImportAndExportJsonlAsync_RoundtripsSuccessfully()
    {
        using var workspace = new TempWorkspace();
        string baseDir = workspace.Path;

        var service = new ModTextService(baseDir);
        await service.ReloadPacksAsync("zh-cn");

        ulong h = UI18n.Core.Hashing.Fnv1a.Hash("Import text");
        string line = $@"{{""h"":""{h:x16}"",""n"":""ckan:ImportMod:abstract"",""s"":""Import text"",""t"":""导入文本"",""mod"":""CKANMeta"",""ctx"":""ImportMod""}}";

        await service.ImportJsonlAsync("CustomPack.jsonl", line + Environment.NewLine, "zh-cn");

        var found = service.Provider.Find("ImportMod", "abstract", "Import text");
        Assert.That(found, Is.Not.Null);
        Assert.That(found!.Text, Is.EqualTo("导入文本"));

        OperationResult<string> exported = await service.ExportJsonlAsync(Path.Combine("packs", "zh-cn", "CustomPack.jsonl"));
        Assert.That(exported.IsSuccess, Is.True, exported.Message);
        Assert.That(exported.Value, Does.Contain("导入文本"));
    }

    [Test]
    public async Task DeleteOverrideAsync_RemovesLineAndUpdatesProvider()
    {
        using var workspace = new TempWorkspace();
        string baseDir = workspace.Path;

        var service = new ModTextService(baseDir);
        await service.ReloadPacksAsync("zh-cn");

        await service.PutOverrideAsync("MechJeb2", "abstract", "An autopilot mod", "自动驾驶模组覆盖");
        Assert.That(service.Provider.Find("MechJeb2", "abstract", "An autopilot mod"), Is.Not.Null);

        await service.DeleteOverrideAsync("MechJeb2", "abstract", "An autopilot mod");

        Assert.That(service.Provider.Find("MechJeb2", "abstract", "An autopilot mod"), Is.Null);

        string overridesPath = Path.Combine(baseDir, "packs", "overrides.jsonl");
        string content = await File.ReadAllTextAsync(overridesPath);
        Assert.That(content, Does.Not.Contain("自动驾驶模组覆盖"));
    }

    [Test]
    public async Task ExportJsonlAsync_MissingFile_ReturnsFailureWithoutThrowing()
    {
        using var workspace = new TempWorkspace();
        var service = new ModTextService(workspace.Path);
        await service.ReloadPacksAsync("zh-cn");

        OperationResult<string> result = null!;
        Assert.DoesNotThrowAsync(async () =>
        {
            result = await service.ExportJsonlAsync(Path.Combine("packs", "zh-cn", "NoSuchPack.jsonl"));
        });

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Value, Is.Null);
        Assert.That(result.Message, Does.Contain("文件不存在"));
        Assert.That(result.Retryable, Is.False);
    }

    [Test]
    public async Task ExportJsonlAsync_BlankPath_ReturnsFailureWithoutThrowing()
    {
        using var workspace = new TempWorkspace();
        var service = new ModTextService(workspace.Path);
        await service.ReloadPacksAsync("zh-cn");

        OperationResult<string> result = null!;
        Assert.DoesNotThrowAsync(async () =>
        {
            result = await service.ExportJsonlAsync("   ");
        });

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Message, Does.Contain("导出路径不能为空"));
    }

    [Test]
    public async Task SyncFromUniversalI18nAsync_MissingDirectory_ReturnsFailureWithoutThrowing()
    {
        using var workspace = new TempWorkspace();
        var service = new ModTextService(workspace.Path);
        await service.ReloadPacksAsync("zh-cn");

        string missing = Path.Combine(workspace.Path, "no-such-universal-i18n");

        OperationResult<int> result = null!;
        Assert.DoesNotThrowAsync(async () =>
        {
            result = await service.SyncFromUniversalI18nAsync(missing);
        });

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Value, Is.EqualTo(0));
        Assert.That(result.Message, Does.Contain("未找到 UniversalI18n"));
        Assert.That(result.Detail, Is.EqualTo(missing));
        // 失败不能回落到用户主目录的真实仓库，也不能顺手建出 packs 目录。
        Assert.That(Directory.Exists(Path.Combine(workspace.Path, "packs")), Is.False);
    }

    [Test]
    public async Task SyncFromUniversalI18nAsync_CopiesPacksAndMergesOverrides()
    {
        using var targetWs = new TempWorkspace();
        using var sourceWs = new TempWorkspace();

        string targetDir = targetWs.Path;
        string sourceDir = sourceWs.Path;

        // 在源目录创建 packs/zh-cn/CKANMeta.jsonl 与 packs/overrides.jsonl
        string srcPacksZh = Path.Combine(sourceDir, "packs", "zh-cn");
        Directory.CreateDirectory(srcPacksZh);

        ulong h1 = UI18n.Core.Hashing.Fnv1a.Hash("Sync source text");
        string srcLine = $@"{{""h"":""{h1:x16}"",""n"":""ckan:SyncMod:abstract"",""s"":""Sync source text"",""t"":""同步测试翻译"",""mod"":""CKANMeta"",""ctx"":""SyncMod""}}";
        await File.WriteAllTextAsync(Path.Combine(srcPacksZh, "CKANMeta.jsonl"), srcLine + Environment.NewLine);

        ulong h2 = UI18n.Core.Hashing.Fnv1a.Hash("Remote override text");
        string srcOverride = $@"{{""h"":""{h2:x16}"",""n"":""ckan:SyncMod:desc"",""s"":""Remote override text"",""t"":""远端覆盖"",""mod"":""CKANMeta"",""ctx"":""SyncMod""}}";
        await File.WriteAllTextAsync(Path.Combine(sourceDir, "packs", "overrides.jsonl"), srcOverride + Environment.NewLine);

        var service = new ModTextService(targetDir);
        await service.ReloadPacksAsync("zh-cn");

        // 目标目录预先存在一条本地 override
        await service.PutOverrideAsync("LocalMod", "abstract", "Local source", "本地独有覆盖");

        OperationResult<int> sync = await service.SyncFromUniversalI18nAsync(sourceDir);
        Assert.That(sync.IsSuccess, Is.True, sync.Message);
        Assert.That(sync.Value, Is.GreaterThanOrEqualTo(2));

        // 验证源包翻译已被载入
        var syncFound = service.Provider.Find("SyncMod", "abstract", "Sync source text");
        Assert.That(syncFound, Is.Not.Null);
        Assert.That(syncFound!.Text, Is.EqualTo("同步测试翻译"));

        // 验证远程与本地 override 都保留
        var localFound = service.Provider.Find("LocalMod", "abstract", "Local source");
        Assert.That(localFound, Is.Not.Null);
        Assert.That(localFound!.Text, Is.EqualTo("本地独有覆盖"));

        var remoteFound = service.Provider.Find("SyncMod", "desc", "Remote override text");
        Assert.That(remoteFound, Is.Not.Null);
        Assert.That(remoteFound!.Text, Is.EqualTo("远端覆盖"));
    }
}
