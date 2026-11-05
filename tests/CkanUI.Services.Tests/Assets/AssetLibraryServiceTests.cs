using CkanUI.Services.Abstractions.Assets;
using CkanUI.Services.Assets;
using CkanUI.TestSupport;
using NUnit.Framework;

namespace CkanUI.Services.Tests;

/// <summary>
/// 资产工作台（飞船 / 旗帜 / 加载图）的服务层：目录布局、伴生文件、配置行、
/// 以及「找不到就回落默认值」这类边界。全部在临时目录里跑，不碰真实游戏安装。
/// </summary>
[TestFixture]
public sealed class AssetLibraryServiceTests
{
    private TempWorkspace? _workspace;
    private string _root = string.Empty;

    [SetUp]
    public void SetUp()
    {
        // 用仓库内 artifacts/tmp 而不是系统临时目录：受限沙箱下 GetTempPath 可能拒绝建子目录。
        _workspace = new TempWorkspace("assets");
        _root = _workspace.Path;
    }

    [TearDown]
    public void TearDown()
    {
        _workspace?.Dispose();
        _workspace = null;
    }

    private static CraftLibraryService NewCraftLibrary() => new(new ChangeSetFakeErrorReporter());

    private static FlagLibraryService NewFlagLibrary() => new(new ChangeSetFakeErrorReporter());

    private static LoadingImageService NewLoadingImages() => new(new ChangeSetFakeErrorReporter());

    private string Write(string relativePath, string content)
    {
        var path = Path.Combine(_root, relativePath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
        return path;
    }

    [Test]
    public void GetKindRelativePath_MapsAllThreeKinds()
    {
        var service = NewCraftLibrary();

        Assert.Multiple(() =>
        {
            Assert.That(service.GetKindRelativePath(CraftKind.Vab), Is.EqualTo("Ships/VAB"));
            Assert.That(service.GetKindRelativePath(CraftKind.Sph), Is.EqualTo("Ships/SPH"));
            Assert.That(service.GetKindRelativePath(CraftKind.Subassembly), Is.EqualTo("Ships/Subassemblies"));
        });
    }

    [Test]
    public async Task Scan_ReadsLoadMetaAndThumbnail_ForEachKind()
    {
        Write("Ships/VAB/Alpha.craft", "ship");
        Write("Ships/VAB/Alpha.loadmeta", "shipName = Alpha\nversion = 1.12.5\npartCount = 42\nstageCount = 3\n");
        Write("Ships/@thumbs/VAB/Alpha.png", "png");
        Write("Ships/SPH/Beta.craft", "ship");
        Write("Ships/Subassemblies/Gamma.craft", "ship");

        var result = await NewCraftLibrary().ScanAsync(_root);

        Assert.That(result.IsSuccess, Is.True, result.Message);
        var crafts = result.Value!;
        Assert.That(crafts.Select(craft => craft.Name), Is.EquivalentTo(new[] { "Alpha", "Beta", "Gamma" }));

        var alpha = crafts.Single(craft => craft.Name == "Alpha");
        Assert.Multiple(() =>
        {
            Assert.That(alpha.Kind, Is.EqualTo(CraftKind.Vab));
            Assert.That(alpha.Version, Is.EqualTo("1.12.5"));
            Assert.That(alpha.PartCount, Is.EqualTo(42));
            Assert.That(alpha.StageCount, Is.EqualTo(3));
            Assert.That(alpha.ThumbnailPath, Is.Not.Null, "有 @thumbs 缩略图就要带上，否则列表没图。");
            Assert.That(alpha.SizeBytes, Is.GreaterThan(0));
        });

        var beta = crafts.Single(craft => craft.Name == "Beta");
        Assert.Multiple(() =>
        {
            Assert.That(beta.Kind, Is.EqualTo(CraftKind.Sph));
            Assert.That(beta.ThumbnailPath, Is.Null, "没有缩略图必须给 null，不能编一个不存在的路径。");
            Assert.That(beta.Version, Is.Null);
            Assert.That(beta.PartCount, Is.Null);
        });

        Assert.That(crafts.Single(craft => craft.Name == "Gamma").Kind, Is.EqualTo(CraftKind.Subassembly));
    }

    [Test]
    public async Task Duplicate_CopiesCompanionLoadMetaAndThumbnail()
    {
        Write("Ships/VAB/Alpha.craft", "ship");
        Write("Ships/VAB/Alpha.loadmeta", "version = 1.12.5\npartCount = 7\n");
        Write("Ships/@thumbs/VAB/Alpha.png", "png");
        var service = NewCraftLibrary();
        var alpha = (await service.ScanAsync(_root)).Value!.Single();

        var duplicated = await service.DuplicateAsync(_root, alpha, "Alpha copy");

        Assert.That(duplicated.IsSuccess, Is.True, duplicated.Message);
        Assert.Multiple(() =>
        {
            Assert.That(File.Exists(Path.Combine(_root, "Ships", "VAB", "Alpha copy.craft")), Is.True);
            Assert.That(File.Exists(Path.Combine(_root, "Ships", "VAB", "Alpha copy.loadmeta")), Is.True, "少拷 .loadmeta 会让副本丢版本号和零件数。");
            Assert.That(File.Exists(Path.Combine(_root, "Ships", "@thumbs", "VAB", "Alpha copy.png")), Is.True, "少拷缩略图副本在游戏里就是空白卡片。");
            Assert.That(duplicated.Value!.PartCount, Is.EqualTo(7));
        });
    }

    [Test]
    public async Task Rename_MovesCraftAndCompanions_AndKeepsSourceGone()
    {
        Write("Ships/VAB/Alpha.craft", "ship");
        Write("Ships/VAB/Alpha.loadmeta", "version = 1.12.5\n");
        Write("Ships/@thumbs/VAB/Alpha.png", "png");
        var service = NewCraftLibrary();
        var alpha = (await service.ScanAsync(_root)).Value!.Single();

        var renamed = await service.RenameAsync(_root, alpha, "Beta");

        Assert.That(renamed.IsSuccess, Is.True, renamed.Message);
        Assert.Multiple(() =>
        {
            Assert.That(File.Exists(Path.Combine(_root, "Ships", "VAB", "Beta.craft")), Is.True);
            Assert.That(File.Exists(Path.Combine(_root, "Ships", "VAB", "Alpha.craft")), Is.False);
            Assert.That(File.Exists(Path.Combine(_root, "Ships", "VAB", "Beta.loadmeta")), Is.True);
            Assert.That(File.Exists(Path.Combine(_root, "Ships", "@thumbs", "VAB", "Beta.png")), Is.True);
        });
    }

    [Test]
    public async Task ScanLibraryAsync_ReadsSaveLevelShipsFolder()
    {
        Write("saves/M6/Ships/VAB/Delta.craft", "ship");
        Write("saves/M6/Ships/@thumbs/VAB/Delta.png", "png");
        var shipsRoot = Path.Combine(_root, "saves", "M6", "Ships");

        var result = await NewCraftLibrary().ScanLibraryAsync(shipsRoot);

        Assert.That(result.IsSuccess, Is.True, result.Message);
        var delta = result.Value!.Single();
        Assert.Multiple(() =>
        {
            Assert.That(delta.Name, Is.EqualTo("Delta"));
            Assert.That(delta.FilePath, Is.EqualTo(Path.Combine(shipsRoot, "VAB", "Delta.craft")));
            Assert.That(
                delta.ThumbnailPath,
                Is.EqualTo(Path.Combine(shipsRoot, "@thumbs", "VAB", "Delta.png")),
                "存档库的缩略图要在该存档自己的 @thumbs 下找，不能跑去根库。");
        });
    }

    [Test]
    public async Task Transfer_CopiesCraftAndCompanionsIntoAnotherLibrary()
    {
        Write("Ships/VAB/Alpha.craft", "ship");
        Write("Ships/VAB/Alpha.loadmeta", "version = 1.12.5\npartCount = 7\n");
        Write("Ships/@thumbs/VAB/Alpha.png", "png");
        var service = NewCraftLibrary();
        var alpha = (await service.ScanAsync(_root)).Value!.Single();
        var saveShips = Path.Combine(_root, "saves", "M6", "Ships");

        var copied = await service.TransferAsync(alpha, saveShips, CraftKind.Vab, move: false);

        Assert.That(copied.IsSuccess, Is.True, copied.Message);
        Assert.Multiple(() =>
        {
            Assert.That(File.Exists(Path.Combine(saveShips, "VAB", "Alpha.craft")), Is.True);
            Assert.That(File.Exists(Path.Combine(saveShips, "VAB", "Alpha.loadmeta")), Is.True);
            Assert.That(
                File.Exists(Path.Combine(saveShips, "@thumbs", "VAB", "Alpha.png")),
                Is.True,
                "缩略图要跟着走，否则存档库里的副本是空白卡。");
            Assert.That(File.Exists(Path.Combine(_root, "Ships", "VAB", "Alpha.craft")), Is.True, "复制不能动源文件。");
            Assert.That(copied.Value!.PartCount, Is.EqualTo(7));
            Assert.That(copied.Value!.FilePath, Is.EqualTo(Path.Combine(saveShips, "VAB", "Alpha.craft")));
        });
    }

    [Test]
    public async Task Transfer_Move_RemovesTheSourceCraftAndCompanions()
    {
        Write("Ships/VAB/Alpha.craft", "ship");
        Write("Ships/VAB/Alpha.loadmeta", "version = 1.12.5\n");
        Write("Ships/@thumbs/VAB/Alpha.png", "png");
        var service = NewCraftLibrary();
        var alpha = (await service.ScanAsync(_root)).Value!.Single();
        var saveShips = Path.Combine(_root, "saves", "M6", "Ships");

        var moved = await service.TransferAsync(alpha, saveShips, CraftKind.Vab, move: true);

        Assert.That(moved.IsSuccess, Is.True, moved.Message);
        Assert.Multiple(() =>
        {
            Assert.That(File.Exists(Path.Combine(saveShips, "VAB", "Alpha.craft")), Is.True);
            Assert.That(File.Exists(Path.Combine(_root, "Ships", "VAB", "Alpha.craft")), Is.False);
            Assert.That(File.Exists(Path.Combine(_root, "Ships", "VAB", "Alpha.loadmeta")), Is.False);
            Assert.That(File.Exists(Path.Combine(_root, "Ships", "@thumbs", "VAB", "Alpha.png")), Is.False);
        });
    }

    [Test]
    public async Task Transfer_WhenTargetHasSameName_AddsSuffixInsteadOfOverwriting()
    {
        Write("Ships/VAB/Alpha.craft", "ship");
        Write("saves/M6/Ships/VAB/Alpha.craft", "existing");
        var service = NewCraftLibrary();
        var alpha = (await service.ScanAsync(_root)).Value!.Single();
        var saveShips = Path.Combine(_root, "saves", "M6", "Ships");

        var copied = await service.TransferAsync(alpha, saveShips, CraftKind.Vab, move: false);

        Assert.That(copied.IsSuccess, Is.True, copied.Message);
        Assert.Multiple(() =>
        {
            Assert.That(copied.Value!.Name, Is.EqualTo("Alpha (2)"));
            Assert.That(
                File.ReadAllText(Path.Combine(saveShips, "VAB", "Alpha.craft")),
                Is.EqualTo("existing"),
                "目标库里的同名载具不能被覆盖。");
            Assert.That(File.Exists(Path.Combine(saveShips, "VAB", "Alpha (2).craft")), Is.True);
        });
    }

    [Test]
    public async Task Transfer_IntoTheSameFolder_FailsWithExplanation()
    {
        Write("Ships/VAB/Alpha.craft", "ship");
        var service = NewCraftLibrary();
        var alpha = (await service.ScanAsync(_root)).Value!.Single();

        var result = await service.TransferAsync(alpha, Path.Combine(_root, "Ships"), CraftKind.Vab, move: true);

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Message, Is.EqualTo("源与目标是同一个目录，不需要搬运。"));
        Assert.That(File.Exists(Path.Combine(_root, "Ships", "VAB", "Alpha.craft")), Is.True, "失败不能把源文件弄丢。");
    }

    [Test]
    public async Task Scan_OnlyTreatsDirectoriesNamedFlagsAsFlags()
    {
        Write("GameData/Squad/Flags/default.png", "png");
        Write("GameData/ModA/Flags/red.jpg", "jpg");
        Write("GameData/ModA/Flags/readme.txt", "text");
        Write("GameData/ModB/Textures/blue.png", "png");

        var result = await NewFlagLibrary().ScanAsync(_root);

        Assert.That(result.IsSuccess, Is.True, result.Message);
        var flags = result.Value!;
        Assert.That(flags.Select(flag => flag.ConfigPath),
            Is.EqualTo(new[] { "ModA/Flags/red", "Squad/Flags/default" }),
            "只有 Flags 目录下的图片算旗帜，且按配置值排序。");
        var squad = flags.Single(flag => flag.Name == "default");
        Assert.That(squad.PackName, Is.EqualTo("Squad"), "来源包取 GameData 下第一段目录，用来分组。");
    }

    [Test]
    public void SetDefaultFlag_WritesFlagLine_AndPreservesOtherSettings()
    {
        Write("settings.cfg", "// KSP Game Settings\nSETTINGS_FILE_VERSION = 1.3.0\nSHOW_KERBAL_PORTRAITS = True\n");
        Write("GameData/ModA/Flags/red.png", "png");
        var service = NewFlagLibrary();
        var flag = service.ScanAsync(_root).GetAwaiter().GetResult().Value!.Single();

        var written = service.SetDefaultFlagAsync(_root, flag).GetAwaiter().GetResult();

        Assert.That(written.IsSuccess, Is.True, written.Message);
        var lines = File.ReadAllLines(Path.Combine(_root, "settings.cfg"));
        Assert.Multiple(() =>
        {
            Assert.That(lines, Does.Contain("FLAG = ModA/Flags/red"));
            Assert.That(lines, Does.Contain("SETTINGS_FILE_VERSION = 1.3.0"), "写默认旗帜不能碰设置文件的其他行。");
            Assert.That(lines, Does.Contain("SHOW_KERBAL_PORTRAITS = True"));
            Assert.That(service.ReadDefaultFlag(_root).Value, Is.EqualTo("ModA/Flags/red"));
        });
    }

    [Test]
    public void SetDefaultFlag_ReplacesExistingFlagLine_InsteadOfAppending()
    {
        Write("settings.cfg", "SETTINGS_FILE_VERSION = 1.3.0\nFLAG = Squad/Flags/default\n");
        Write("GameData/ModA/Flags/red.png", "png");
        var service = NewFlagLibrary();
        var flag = service.ScanAsync(_root).GetAwaiter().GetResult().Value!.Single();

        service.SetDefaultFlagAsync(_root, flag).GetAwaiter().GetResult();

        var flagLines = File.ReadAllLines(Path.Combine(_root, "settings.cfg"))
            .Count(line => line.TrimStart().StartsWith("FLAG", StringComparison.OrdinalIgnoreCase));
        Assert.That(flagLines, Is.EqualTo(1), "重复的 FLAG 行会让游戏读到哪一行变成运气问题。");
        Assert.That(service.ReadDefaultFlag(_root).Value, Is.EqualTo("ModA/Flags/red"));
    }

    [Test]
    public void ReadDefaultFlag_ReturnsNull_WhenSettingsFileMissing()
    {
        Assert.That(NewFlagLibrary().ReadDefaultFlag(_root).Value, Is.Null);
    }

    [Test]
    public async Task ImportFlag_LandsInOwnedFolder_AndAvoidsNameClash()
    {
        var source = Path.Combine(_root, "incoming", "banner.png");
        Directory.CreateDirectory(Path.GetDirectoryName(source)!);
        File.WriteAllText(source, "png");
        var service = NewFlagLibrary();

        var first = await service.ImportAsync(_root, source);
        var second = await service.ImportAsync(_root, source);

        Assert.That(first.IsSuccess, Is.True, first.Message);
        Assert.Multiple(() =>
        {
            Assert.That(first.Value!.ConfigPath, Is.EqualTo("CkanUI/Flags/banner"), "导入的旗帜要落在本工具自有目录，不能污染官方目录。");
            Assert.That(second.Value!.ConfigPath, Is.EqualTo("CkanUI/Flags/banner (2)"), "同名导入要自动改名，不能覆盖已有旗帜。");
            Assert.That(File.Exists(first.Value.FilePath), Is.True);
            Assert.That(File.Exists(second.Value.FilePath), Is.True);
        });
    }

    [Test]
    public async Task ImportFlag_RejectsNonImage()
    {
        var source = Path.Combine(_root, "notes.txt");
        File.WriteAllText(source, "text");

        var result = await NewFlagLibrary().ImportAsync(_root, source);

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Message, Does.Contain("png").IgnoreCase.Or.Contain("图片"));
    }

    [Test]
    public void LoadFolders_FallsBackToDefaults_WhenListMissing()
    {
        Directory.CreateDirectory(Path.Combine(_root, "Screenshots"));

        var folders = NewLoadingImages().LoadFolders(_root);

        Assert.That(folders.IsSuccess, Is.True, folders.Message);
        Assert.That(folders.Value, Is.EqualTo(new[] { Path.Combine(_root, "Screenshots") }));
    }

    [Test]
    public void SaveFolders_RoundTrips_AndIgnoresDuplicates()
    {
        var service = NewLoadingImages();
        var one = Path.Combine(_root, "Shots1");
        var two = Path.Combine(_root, "Shots2");

        var saved = service.SaveFolders(_root, new[] { one, two, one });
        var loaded = service.LoadFolders(_root);

        Assert.That(saved.IsSuccess, Is.True, saved.Message);
        Assert.That(loaded.Value, Is.EqualTo(new[] { one, two }), "重复目录只留一份，顺序保持用户添加顺序。");
    }

    [Test]
    public async Task ScanImages_OnlyFirstLevel_AndOnlySupportedFormats()
    {
        var folder = Path.Combine(_root, "LoadingImages");
        Directory.CreateDirectory(Path.Combine(folder, "nested"));
        File.WriteAllText(Path.Combine(folder, "a.png"), "png");
        File.WriteAllText(Path.Combine(folder, "b.JPG"), "jpg");
        File.WriteAllText(Path.Combine(folder, "readme.txt"), "text");
        File.WriteAllText(Path.Combine(folder, "nested", "deep.png"), "png");

        var result = await NewLoadingImages().ScanAsync(new[] { folder });

        Assert.That(result.IsSuccess, Is.True, result.Message);
        Assert.That(result.Value!.Select(image => image.FileName), Is.EquivalentTo(new[] { "a.png", "b.JPG" }),
            "只扫第一层：递归扫 GameData 会把几十万张模组贴图拖进列表。");
        Assert.That(result.Value!.Single(image => image.FileName == "a.png").Folder, Is.EqualTo(folder));
    }

    [Test]
    public async Task ScanImages_SkipsMissingFolders_WithoutFailing()
    {
        var existing = Path.Combine(_root, "Existing");
        Directory.CreateDirectory(existing);
        File.WriteAllText(Path.Combine(existing, "a.png"), "png");

        var result = await NewLoadingImages().ScanAsync(new[] { existing, Path.Combine(_root, "Gone") });

        Assert.That(result.IsSuccess, Is.True, result.Message);
        Assert.That(result.Value!.Count, Is.EqualTo(1));
    }

    [Test]
    public void IsSupportedImage_AcceptsCommonFormats()
    {
        var service = NewLoadingImages();

        Assert.Multiple(() =>
        {
            Assert.That(service.IsSupportedImage("a.png"), Is.True);
            Assert.That(service.IsSupportedImage("a.JPEG"), Is.True);
            Assert.That(service.IsSupportedImage("a.jpg"), Is.True);
            Assert.That(service.IsSupportedImage("a.dds"), Is.True, "ROLoadingImages 只吃 dds，不认这个格式 RP-1 的 72 张一张都管不了。");
            Assert.That(service.IsSupportedImage("a.bmp"), Is.False);
            Assert.That(service.IsSupportedImage("a"), Is.False);
            Assert.That(service.IsSupportedImage("a.dds.disabled"), Is.True,
                "停用的图也要算可管理，否则停用后卡片从列表消失、用户再也点不回来。");
            Assert.That(service.IsSupportedImage("a.txt.disabled"), Is.False);
        });
    }

    [Test]
    public async Task SetEnabled_RenamesFileAndKeepsItVisible()
    {
        var folder = Path.Combine(_root, "Screenshots");
        Directory.CreateDirectory(folder);
        var original = Path.Combine(folder, "shot.png");
        File.WriteAllText(original, "png");
        var service = NewLoadingImages();
        var scanned = await service.ScanAsync(new[] { folder });
        var image = scanned.Value!.Single();

        Assert.That(image.IsEnabled, Is.True);

        var disabled = await service.SetEnabledAsync(image, enabled: false);

        Assert.That(disabled.IsSuccess, Is.True, disabled.Message);
        Assert.That(disabled.Value!.FileName, Is.EqualTo("shot.png"), "文件名不带 .disabled，用户看到的还是他那张图。");
        Assert.That(disabled.Value!.FilePath, Is.EqualTo(original + LoadingImageService.DisabledSuffix));
        Assert.That(disabled.Value!.IsEnabled, Is.False);
        Assert.That(File.Exists(original), Is.False);
        Assert.That(File.Exists(original + LoadingImageService.DisabledSuffix), Is.True, "停用只改扩展名，不删文件。");

        var rescan = await service.ScanAsync(new[] { folder });

        Assert.That(rescan.Value!.Select(item => item.FileName), Is.EqualTo(new[] { "shot.png" }), "停用后仍要出现在列表里。");
        Assert.That(rescan.Value!.Single().IsEnabled, Is.False);

        var enabled = await service.SetEnabledAsync(disabled.Value!, enabled: true);

        Assert.That(enabled.IsSuccess, Is.True, enabled.Message);
        Assert.That(enabled.Value!.FilePath, Is.EqualTo(original));
        Assert.That(File.Exists(original), Is.True, "启用回来必须原样还原，这是「不删图」承诺的另一半。");
    }

    [Test]
    public async Task SetEnabled_TargetAlreadyExists_FailsWithoutTouchingDisk()
    {
        var folder = Path.Combine(_root, "Screenshots");
        Directory.CreateDirectory(folder);
        var original = Path.Combine(folder, "shot.png");
        File.WriteAllText(original, "png");
        File.WriteAllText(original + LoadingImageService.DisabledSuffix, "png");
        var service = NewLoadingImages();
        var image = (await service.ScanAsync(new[] { folder })).Value!.Single();

        Assert.That(image.IsEnabled, Is.True, "同名双份只留一张卡片，优先游戏真正在用的那份。");

        var result = await service.SetEnabledAsync(image, enabled: false);

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Message, Is.EqualTo("同名文件已存在，先处理它再切换启停。"));
        Assert.That(File.Exists(original), Is.True, "失败不能把原文件弄丢。");
    }

    [Test]
    public async Task DefaultFolders_IncludesRoLoadingImagesScreens_AndScansDds()
    {
        var screens = Path.Combine(_root, "GameData", "ROLoadingImages", "PluginData", "Screens");
        Directory.CreateDirectory(screens);
        File.WriteAllText(Path.Combine(screens, "rp1.dds"), "dds");

        var service = NewLoadingImages();
        var folders = service.DefaultFolders(_root);
        var result = await service.ScanAsync(folders);

        Assert.That(folders, Does.Contain(screens), "ROLoadingImages 的目录名不带 Loading 前缀，靠模组目录匹配不到。");
        Assert.That(result.Value!.Select(image => image.FileName), Does.Contain("rp1.dds"));
    }

    [Test]
    public void DescribeFolders_RecognizesRoAndLoadingScreenManagerFolders()
    {
        var screens = Path.Combine(_root, "GameData", "ROLoadingImages", "PluginData", "Screens");
        var shots = Path.Combine(_root, "Screenshots");
        Directory.CreateDirectory(screens);
        Directory.CreateDirectory(shots);
        WriteLoadingScreenManagerConfig("LoadingScreenManager.cfg", "Screenshots", ignoreSubfolders: false);

        var folders = NewLoadingImages().DescribeFolders(_root);

        Assert.Multiple(() =>
        {
            Assert.That(folders[Path.GetFullPath(screens)].Provider, Is.EqualTo(LoadingImageService.RoLoadingImagesProvider));
            Assert.That(folders[Path.GetFullPath(shots)].Provider, Is.EqualTo(LoadingImageService.LoadingScreenManagerProvider));
            Assert.That(folders[Path.GetFullPath(shots)].Recursive, Is.True, "ignoreSubfolders = False 就是要连子目录一起扫。");
            Assert.That(folders[Path.GetFullPath(screens)].Recursive, Is.False, "ROLoadingImages 只读 Screens 这一层。");
        });
    }

    [Test]
    public void DescribeFolders_ReadsAddonConfigs_NotOnlyMainConfig()
    {
        var payload = Path.Combine(_root, "Mods", "Screens");
        Directory.CreateDirectory(payload);
        WriteLoadingScreenManagerConfig("Extra.cfg", "Mods/Screens", ignoreSubfolders: true);

        var folders = NewLoadingImages().DescribeFolders(_root);

        Assert.That(folders.ContainsKey(Path.GetFullPath(payload)), Is.True,
            "LSM 会读同目录下所有 cfg，只看主配置会漏掉别人的加载图目录。");
        Assert.That(folders[Path.GetFullPath(payload)].Recursive, Is.False, "ignoreSubfolders = True 就不进子目录。");
    }

    [Test]
    public async Task ScanAsync_RecursiveFolder_IncludesSubDirectories()
    {
        var root = Path.Combine(_root, "Screenshots");
        var nested = Path.Combine(root, "2024");
        Directory.CreateDirectory(nested);
        File.WriteAllText(Path.Combine(root, "top.png"), "png");
        File.WriteAllText(Path.Combine(nested, "deep.png"), "png");
        var service = NewLoadingImages();

        var flat = await service.ScanAsync(new[] { root });
        var deep = await service.ScanAsync(new[] { root }, new[] { root });

        Assert.That(flat.Value!.Select(image => image.FileName), Is.EqualTo(new[] { "top.png" }));
        Assert.That(deep.Value!.Select(image => image.FileName), Is.EquivalentTo(new[] { "top.png", "deep.png" }));
    }

    private void WriteLoadingScreenManagerConfig(string fileName, string path, bool ignoreSubfolders)
    {
        var config = Path.Combine(_root, "GameData", "LoadingScreenManager", "PluginData", fileName);
        Directory.CreateDirectory(Path.GetDirectoryName(config)!);
        File.WriteAllText(config, string.Join(Environment.NewLine, new[]
        {
            "FOLDER",
            "{",
            "\tpath = " + path,
            "\tfileMasks = *.png;*.jpg",
            "\tignoreSubfolders = " + (ignoreSubfolders ? "True" : "False"),
            "}",
            string.Empty,
        }));
    }
}
