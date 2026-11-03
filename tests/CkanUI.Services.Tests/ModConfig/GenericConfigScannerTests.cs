using CkanUI.Services.Abstractions;
using CkanUI.Services.Abstractions.ModConfig;
using CkanUI.Services.ModConfig;
using NUnit.Framework;

namespace CkanUI.Services.Tests.ModConfig;

/// <summary>
/// <see cref="GenericConfigScanner"/> 的用例：跳过规则、候选规则、上限与诊断。
/// 用例里造的都是最小可解析 cfg，不依赖本机 KSP。
/// </summary>
[TestFixture]
public sealed class GenericConfigScannerTests
{
    [Test]
    public void Scan_CollectsPluginDataSettingsFile()
    {
        using var temp = new TempDirectory("scanner-plugindata");
        var gameDir = CreateGameDir(temp);
        temp.File(
            "game/GameData/FancyMod/PluginData/settings.cfg",
            "SETTINGS\n{\n  volume = 0.5\n  mute = false\n}\n");

        var result = Scan(gameDir);

        Assert.That(result.IsSuccess, Is.True, result.Message);
        var catalog = result.Value!;
        Assert.Multiple(() =>
        {
            Assert.That(catalog.GameDir, Is.EqualTo(gameDir).IgnoreCase);
            Assert.That(catalog.Files, Has.Count.EqualTo(1));
            Assert.That(catalog.SkippedFileCount, Is.Zero);
            Assert.That(catalog.Diagnostics, Is.Empty);

            var file = catalog.Files[0];
            Assert.That(file.ModName, Is.EqualTo("FancyMod"));
            Assert.That(file.RelativePath, Is.EqualTo("FancyMod/PluginData/settings.cfg"));
            Assert.That(file.DisplayName, Is.EqualTo("FancyMod · FancyMod/PluginData/settings.cfg"));
            Assert.That(file.RootNode, Is.EqualTo("SETTINGS"));
            Assert.That(file.PairCount, Is.EqualTo(2), "候选文件要全量解析，PairCount 不能是占位 0");
            Assert.That(file.SizeBytes, Is.GreaterThan(0));
            Assert.That(file.LastWriteUtc, Is.Not.EqualTo(default(DateTime)));
            Assert.That(file.IsWritable, Is.True, file.NotWritableReason);
            Assert.That(file.NotWritableReason, Is.Null);
        });
    }

    [Test]
    public void Scan_SkipsPartDefinitionUnderParts()
    {
        using var temp = new TempDirectory("scanner-parts");
        var gameDir = CreateGameDir(temp);
        temp.File("game/GameData/MyMod/Parts/engine.cfg", "PART\n{\n  name = myEngine\n}\n");

        var result = Scan(gameDir);

        Assert.That(result.IsSuccess, Is.True, result.Message);
        Assert.Multiple(() =>
        {
            Assert.That(result.Value!.Files, Is.Empty);
            Assert.That(result.Value!.SkippedFileCount, Is.EqualTo(1));
        });
    }

    [Test]
    public void Scan_SkipsVanillaRootNodeOutsideParts()
    {
        using var temp = new TempDirectory("scanner-vanilla");
        var gameDir = CreateGameDir(temp);
        temp.File("game/GameData/MyMod/Configs/resource.cfg", "RESOURCE\n{\n  name = Ore\n}\n");

        var result = Scan(gameDir);

        Assert.That(result.IsSuccess, Is.True, result.Message);
        Assert.That(result.Value!.Files, Is.Empty);
        Assert.That(result.Value!.SkippedFileCount, Is.EqualTo(1));
    }

    [Test]
    public void Scan_SkipsModuleManagerPatchFile()
    {
        using var temp = new TempDirectory("scanner-mm");
        var gameDir = CreateGameDir(temp);
        // 放在 PluginData 下：位置规则本来会命中，必须被 patch 语法先拦掉。
        temp.File(
            "game/GameData/MyMod/PluginData/patches.cfg",
            "@PART[fuelTank]\n{\n  @mass = 1\n}\n");

        var result = Scan(gameDir);

        Assert.That(result.IsSuccess, Is.True, result.Message);
        Assert.That(result.Value!.Files, Is.Empty, "ModuleManager 补丁不是可编辑配置");
        Assert.That(result.Value!.SkippedFileCount, Is.EqualTo(1));
    }

    [Test]
    public void Scan_SkipsSquadDirectories()
    {
        using var temp = new TempDirectory("scanner-squad");
        var gameDir = CreateGameDir(temp);
        temp.File("game/GameData/Squad/PluginData/settings.cfg", "SETTINGS\n{\n  a = 1\n}\n");
        temp.File("game/GameData/SquadExpansion/PluginData/settings.cfg", "SETTINGS\n{\n  a = 1\n}\n");

        var result = Scan(gameDir);

        Assert.That(result.IsSuccess, Is.True, result.Message);
        Assert.That(result.Value!.Files, Is.Empty, "原版目录整个跳过");
    }

    [Test]
    public void Scan_SkipsBackupFiles()
    {
        using var temp = new TempDirectory("scanner-backup");
        var gameDir = CreateGameDir(temp);
        temp.File("game/GameData/MyMod/PluginData/.bak-settings.cfg", "SETTINGS\n{\n  a = 1\n}\n");
        temp.File("game/GameData/MyMod/PluginData/settings.bak-1.cfg", "SETTINGS\n{\n  a = 1\n}\n");

        var result = Scan(gameDir);

        Assert.That(result.IsSuccess, Is.True, result.Message);
        Assert.That(result.Value!.Files, Is.Empty);
        Assert.That(result.Value!.SkippedFileCount, Is.EqualTo(2));
    }

    [Test]
    public void Scan_CollectsByNameAndByRootNodeOutsidePluginData()
    {
        using var temp = new TempDirectory("scanner-rules");
        var gameDir = CreateGameDir(temp);
        temp.File("game/GameData/ModA/settings.cfg", "WHATEVER\n{\n  a = 1\n}\n");
        temp.File("game/GameData/ModB/State/persist.cfg", "PERSISTENT\n{\n  b = 2\n}\n");

        var result = Scan(gameDir);

        Assert.That(result.IsSuccess, Is.True, result.Message);
        var catalog = result.Value!;
        Assert.That(catalog.Files, Has.Count.EqualTo(2));
        Assert.That(
            catalog.Files.Select(static file => file.RelativePath),
            Is.EqualTo(new[] { "ModA/settings.cfg", "ModB/State/persist.cfg" }),
            "结果按相对路径升序");
        Assert.That(
            catalog.Files.Select(static file => file.ModName),
            Is.EqualTo(new[] { "ModA", "ModB" }));
    }

    [Test]
    public void Scan_StopsAtFileLimitAndWritesDiagnostic()
    {
        using var temp = new TempDirectory("scanner-limit");
        var gameDir = CreateGameDir(temp);
        for (var index = 0; index < 4; index++)
        {
            temp.File(
                "game/GameData/Mod" + index + "/PluginData/settings.cfg",
                "SETTINGS\n{\n  a = " + index + "\n}\n");
        }

        var result = Scan(gameDir, maxFiles: 2);

        Assert.That(result.IsSuccess, Is.True, result.Message);
        Assert.Multiple(() =>
        {
            Assert.That(result.Value!.Files, Has.Count.EqualTo(2), "到上限就停，不再继续列");
            Assert.That(
                result.Value!.Diagnostics.Any(static text => text.Contains("上限", StringComparison.Ordinal)),
                Is.True,
                "超上限必须记一条可展示的诊断");
        });
    }

    [Test]
    public void Scan_StopsAtDepthLimitAndWritesDiagnostic()
    {
        using var temp = new TempDirectory("scanner-depth");
        var gameDir = CreateGameDir(temp);
        temp.File("game/GameData/ModA/Deep/PluginData/settings.cfg", "SETTINGS\n{\n  a = 1\n}\n");

        var result = Scan(gameDir, maxDepth: 1);

        Assert.That(result.IsSuccess, Is.True, result.Message);
        Assert.That(result.Value!.Files, Is.Empty);
        Assert.That(
            result.Value!.Diagnostics.Any(static text => text.Contains("深度上限", StringComparison.Ordinal)),
            Is.True);
    }

    [Test]
    public void Scan_ReportsReadonlyFileAsNotWritable()
    {
        using var temp = new TempDirectory("scanner-readonly");
        var gameDir = CreateGameDir(temp);
        var path = temp.File(
            "game/GameData/MyMod/PluginData/settings.cfg",
            "SETTINGS\n{\n  volume = 0.5\n}\n");
        File.SetAttributes(path, FileAttributes.ReadOnly);

        try
        {
            var result = Scan(gameDir);

            Assert.That(result.IsSuccess, Is.True, result.Message);
            var file = result.Value!.Files[0];
            Assert.Multiple(() =>
            {
                Assert.That(file.IsWritable, Is.False);
                Assert.That(file.NotWritableReason, Does.Contain("只读"));
            });
        }
        finally
        {
            File.SetAttributes(path, FileAttributes.Normal);
        }
    }

    [Test]
    public void Scan_ReportsParseErrorsAsNotWritable()
    {
        using var temp = new TempDirectory("scanner-parse-error");
        var gameDir = CreateGameDir(temp);
        temp.File(
            "game/GameData/MyMod/PluginData/settings.cfg",
            "SETTINGS\n{\n  volume = 0.5\n  这不是键值对也没有花括号\n}\n");

        var result = Scan(gameDir);

        Assert.That(result.IsSuccess, Is.True, result.Message);
        var file = result.Value!.Files[0];
        Assert.Multiple(() =>
        {
            Assert.That(file.IsWritable, Is.False, "有解析错误就不能写");
            Assert.That(file.NotWritableReason, Does.Contain("解析"));
        });
    }

    [Test]
    public void Scan_AutomaticMode_CountsWideCandidatesWithoutListingThem()
    {
        using var temp = new TempDirectory("scanner-wide-hidden");
        var gameDir = CreateGameDir(temp);
        temp.File(
            "game/GameData/MyMod/Config/custom.cfg",
            "MYMOD_SETTINGS\n{\n  volume = 0.5\n}\n");

        var automatic = Scan(gameDir);

        Assert.That(automatic.IsSuccess, Is.True, automatic.Message);
        Assert.Multiple(() =>
        {
            Assert.That(automatic.Value!.Files, Is.Empty, "保守模式不该凭自定义根节点就收");
            Assert.That(automatic.Value!.SkippedFileCount, Is.EqualTo(1));
            Assert.That(automatic.Value!.HiddenWideCandidateCount, Is.EqualTo(1));
        });

        var wide = Scan(gameDir, options: GenericConfigScanOptions.Wide);

        Assert.That(wide.IsSuccess, Is.True, wide.Message);
        Assert.Multiple(() =>
        {
            Assert.That(wide.Value!.Files, Has.Count.EqualTo(1));
            Assert.That(wide.Value!.Files[0].RelativePath, Is.EqualTo("MyMod/Config/custom.cfg"));
            Assert.That(wide.Value!.Files[0].RootNode, Is.EqualTo("MYMOD_SETTINGS"));
            Assert.That(wide.Value!.HiddenWideCandidateCount, Is.Zero, "宽松模式自己不再藏东西");
        });
    }

    [Test]
    public void Scan_WideMode_StillSkipsDefinitionsPatchesAndLocalization()
    {
        using var temp = new TempDirectory("scanner-wide-skip");
        var gameDir = CreateGameDir(temp);
        temp.File("game/GameData/MyMod/Parts/engine.cfg", "PART\n{\n  name = myEngine\n}\n");
        temp.File("game/GameData/MyMod/Effects/fx.cfg", "EFFECTTEMPLATE\n{\n  name = fx\n}\n");
        temp.File("game/GameData/MyMod/Localization/en.cfg", "MYMOD_STRINGS\n{\n  a = b\n}\n");
        temp.File("game/GameData/MyMod/Patches/p.cfg", "MYMOD_SETTINGS\n{\n  a = 1\n}\n");
        temp.File("game/GameData/MyMod/PluginData/patch.cfg", "@PART[fuelTank]\n{\n  @mass = 1\n}\n");

        var result = Scan(gameDir, options: GenericConfigScanOptions.Wide);

        Assert.That(result.IsSuccess, Is.True, result.Message);
        Assert.That(result.Value!.Files, Is.Empty, "宽松不等于把内容定义也当成配置");
    }

    [Test]
    public void Scan_WideMode_CollectsFlatAndVisualConfigs()
    {
        using var temp = new TempDirectory("scanner-wide-collect");
        var gameDir = CreateGameDir(temp);
        temp.File("game/GameData/ModA/PluginData/settings.cfg", "SETTINGS\n{\n  a = 1\n}\n");
        temp.File("game/GameData/ModB/Configs/flat.cfg", "volume = 0.8\nmute = false\n");
        temp.File("game/GameData/ModC/Configs/tufx.cfg", "TUFX_PROFILE\n{\n  name = Default\n}\n");

        var result = Scan(gameDir, options: GenericConfigScanOptions.Wide);

        Assert.That(result.IsSuccess, Is.True, result.Message);
        Assert.Multiple(() =>
        {
            Assert.That(
                result.Value!.Files.Select(static file => file.RelativePath),
                Is.EqualTo(new[] { "ModA/PluginData/settings.cfg", "ModB/Configs/flat.cfg", "ModC/Configs/tufx.cfg" }));
            Assert.That(
                result.Value!.Files.Select(static file => file.RootNode),
                Is.EqualTo(new string?[] { "SETTINGS", null, "TUFX_PROFILE" }));
        });
    }

    [Test]
    public void Scan_RecognizesRootWhenTheOpeningBraceIsOnTheSameLine()
    {
        using var temp = new TempDirectory("scanner-same-line-brace");
        var gameDir = CreateGameDir(temp);
        temp.File("game/GameData/ModA/settings.cfg", "SETTINGS {\n  volume = 0.8\n}\n");
        temp.File("game/GameData/ModB/Parts/engine.cfg", "PART {\n  name = myEngine\n}\n");

        var automatic = Scan(gameDir);
        var wide = Scan(gameDir, options: GenericConfigScanOptions.Wide);

        Assert.Multiple(() =>
        {
            Assert.That(automatic.Value!.Files, Has.Count.EqualTo(1));
            Assert.That(automatic.Value!.Files[0].RootNode, Is.EqualTo("SETTINGS"));
            Assert.That(wide.Value!.Files.Select(static file => file.RelativePath), Is.EqualTo(new[] { "ModA/settings.cfg" }),
                "PART { 也必须按零件定义跳过，而不是当成自定义根节点");
        });
    }

    [Test]
    public void Scan_FailsWhenGameDataDirectoryMissing()
    {
        using var temp = new TempDirectory("scanner-nogamedata");
        var gameDir = Path.Combine(temp.Path, "game");
        Directory.CreateDirectory(gameDir);

        var result = Scan(gameDir);

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Message, Does.Contain("GameData"));
    }

    private static string CreateGameDir(TempDirectory temp)
    {
        var gameDir = Path.Combine(temp.Path, "game");
        Directory.CreateDirectory(Path.Combine(gameDir, "GameData"));
        return gameDir;
    }

    private static OperationResult<GenericConfigCatalog> Scan(
        string gameDir,
        int maxFiles = GenericConfigScanner.DefaultMaxFiles,
        int maxDepth = GenericConfigScanner.DefaultMaxDepth,
        GenericConfigScanOptions? options = null)
        => GenericConfigScanner.Scan(gameDir, CancellationToken.None, maxFiles, maxDepth, options);
}
