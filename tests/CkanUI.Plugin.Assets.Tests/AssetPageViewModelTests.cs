using CkanUI.Services.Abstractions;
using CkanUI.Services.Abstractions.Assets;
using CkanUI.Services.Abstractions.Dtos;
using CkanUI.Plugin.Assets;
using CkanUI.ViewModels.Pages.Instances;

using NUnit.Framework;

namespace CkanUI.Plugin.Assets.Tests;

/// <summary>
/// 资产工作台三个页面的行为契约：无实例时收口、失败不冒泡、动作前必须有选中项。
/// </summary>
/// <remarks>
/// 这三页都是「文件很贵、误删不可撤销」的类型，所以测试盯的是删除前的确认、
/// 未选中时不发命令、以及服务层失败必须落到 ErrorText 而不是让异常冒到 UI 线程。
/// </remarks>
[TestFixture]
public sealed class AssetPageViewModelTests
{
    [Test]
    public async Task Ships_WithoutInstance_ReportsMissingInstance()
    {
        var instances = new InstancesFakeService();
        var crafts = new FakeCraftLibrary();
        var page = new ShipsPageViewModel(instances, crafts, new InstancesFakeSaveFiles(), new InstancesFakeErrorReporter(), new InstancesFakeDialogs());

        await page.RefreshCommand.ExecuteAsync(null);

        Assert.That(page.ErrorText, Is.EqualTo("还没有可用的游戏实例。"), "没有实例时要给一句人话，而不是空列表。");
        Assert.That(crafts.ScanCalls, Is.Zero, "没有游戏目录就不该去扫盘。");
        Assert.That(page.ShowEmptyState, Is.True);
    }

    [Test]
    public async Task Ships_Refresh_LoadsCraftsAndSelectsFirst()
    {
        var page = NewShipsPage(out var crafts);
        crafts.Assets.Add(Craft("Apollo", CraftKind.Vab));
        crafts.Assets.Add(Craft("Sparrow", CraftKind.Sph));

        await page.RefreshCommand.ExecuteAsync(null);

        Assert.That(page.Crafts.Count, Is.EqualTo(2));
        Assert.That(page.Crafts[1].KindLabel, Is.EqualTo("飞机"), "分类要按 CraftKind 翻译，不能显示枚举名。");
        Assert.That(page.SelectedCraft, Is.SameAs(page.Crafts[0]), "刷新后默认选中第一项，详情栏才不会空着。");
        Assert.That(page.StatusText, Is.EqualTo("共 2 个载具"));
    }

    [Test]
    public async Task Ships_RefreshFailure_ShowsErrorAndKeepsListEmpty()
    {
        var page = NewShipsPage(out var crafts);
        crafts.ScanFailure = OperationResult<IReadOnlyList<CraftAsset>>.Fail("目录被占用。");

        await page.RefreshCommand.ExecuteAsync(null);

        Assert.That(page.ErrorText, Is.EqualTo("目录被占用。"), "服务层给了人话就直接显示，不要用泛化文案盖掉它。");
        Assert.That(page.ErrorDetail, Is.Null);
        Assert.That(page.Crafts, Is.Empty);
        Assert.That(page.ShowEmptyState, Is.True);
    }

    [Test]
    public async Task Ships_Delete_AsksBeforeTouchingDisk()
    {
        var crafts = new FakeCraftLibrary();
        var dialogs = new InstancesFakeDialogs();
        var page = new ShipsPageViewModel(GameInstances(), crafts, new InstancesFakeSaveFiles(), new InstancesFakeErrorReporter(), dialogs);
        crafts.Assets.Add(Craft("Apollo", CraftKind.Vab));
        await page.RefreshCommand.ExecuteAsync(null);

        dialogs.ConfirmResult = false;
        await page.DeleteCommand.ExecuteAsync(null);

        Assert.That(crafts.DeleteCalls, Is.Zero, "用户点了取消，绝不能删。");
        Assert.That(dialogs.ConfirmTitles, Is.Not.Empty, "删除前必须有确认框。");

        dialogs.ConfirmResult = true;
        await page.DeleteCommand.ExecuteAsync(null);

        Assert.That(crafts.DeleteCalls, Is.EqualTo(1));
        Assert.That(page.StatusText, Is.EqualTo("已删除「Apollo」"));
    }

    [Test]
    public async Task Ships_Rename_UsesTrimmedPromptText()
    {
        var page = NewShipsPage(out var crafts);
        var prompt = new InstancesFakeTextPrompt();
        prompt.Enqueue("  Apollo II  ");
        page.TextPrompt = prompt;
        crafts.Assets.Add(Craft("Apollo", CraftKind.Vab));
        await page.RefreshCommand.ExecuteAsync(null);

        await page.RenameCommand.ExecuteAsync(null);

        Assert.That(crafts.NewNames, Is.EqualTo(new[] { "Apollo II" }), "输入要先去首尾空白再交给服务层。");
        Assert.That(page.StatusText, Is.EqualTo("已重命名为「Apollo II」"));
    }

    [Test]
    public async Task Ships_OpenFolder_RevealsSelectedFolder()
    {
        var page = NewShipsPage(out var crafts);
        var launcher = new FakeShellLauncher();
        page.ShellLauncher = launcher;
        crafts.Assets.Add(Craft("Apollo", CraftKind.Vab));
        await page.RefreshCommand.ExecuteAsync(null);

        page.OpenFolderCommand.Execute(null);

        Assert.That(launcher.Revealed, Is.EqualTo(new[] { page.SelectedCraft!.FolderPath }));
    }

    [Test]
    public async Task Ships_CommandsRequireSelection()
    {
        var page = NewShipsPage(out _);

        await page.RefreshCommand.ExecuteAsync(null);

        Assert.That(page.SelectedCraft, Is.Null);
        Assert.That(page.DeleteCommand.CanExecute(null), Is.False, "没选中时删除按钮该是灰的。");
        Assert.That(page.RenameCommand.CanExecute(null), Is.False);
        Assert.That(page.DuplicateCommand.CanExecute(null), Is.False);
        Assert.That(page.OpenFolderCommand.CanExecute(null), Is.False);
        Assert.That(page.CopyToLibraryCommand.CanExecute(null), Is.False);
        Assert.That(page.MoveToLibraryCommand.CanExecute(null), Is.False);
    }

    [Test]
    public async Task Ships_Libraries_ListRootAndEachSaveFolderOnce()
    {
        var saves = new InstancesFakeSaveFiles();
        saves.Saves.Add(Save("M6_FlightUI / persistent"));
        saves.Saves.Add(Save("M6_FlightUI / persistent (2026_10_31_16_06_27)", backup: true));
        saves.Saves.Add(Save("默认 / persistent"));
        var page = NewShipsPage(out _, saves);

        await page.RefreshCommand.ExecuteAsync(null);

        Assert.That(
            page.Libraries.Select(option => option.DisplayName),
            Is.EqualTo(new[] { "根目录（所有存档共用）", "M6_FlightUI", "默认" }),
            "一个存档文件夹只出现一次，根库永远排第一。");
        Assert.That(page.Libraries[0].ShipsRoot, Is.EqualTo(System.IO.Path.Combine("D:/KSP", "Ships")));
        Assert.That(
            page.Libraries[1].ShipsRoot,
            Is.EqualTo(System.IO.Path.Combine("D:/KSP/saves", "M6_FlightUI", "Ships")));
        Assert.That(page.SelectedLibrary, Is.SameAs(page.Libraries[0]), "默认浏览根库。");
    }

    [Test]
    public async Task Ships_SwitchingLibrary_ScansThatSaveFolder()
    {
        var saves = new InstancesFakeSaveFiles();
        saves.Saves.Add(Save("M6_FlightUI / persistent"));
        var page = NewShipsPage(out var crafts, saves);
        await page.RefreshCommand.ExecuteAsync(null);

        page.SelectedLibrary = page.Libraries[1];
        await page.RefreshCommand.ExecuteAsync(null);

        var saveRoot = System.IO.Path.Combine("D:/KSP/saves", "M6_FlightUI", "Ships");
        Assert.That(page.SelectedLibrary!.ShipsRoot, Is.EqualTo(saveRoot));
        Assert.That(crafts.LibraryScans, Has.Some.EqualTo(saveRoot), "切库后要扫那个存档的 Ships 目录。");
    }

    [Test]
    public async Task Ships_CopyToLibrary_CopiesCraftToChosenSaveFolder()
    {
        var saves = new InstancesFakeSaveFiles();
        saves.Saves.Add(Save("M6_FlightUI / persistent"));
        var dialogs = new InstancesFakeDialogs();
        var page = NewShipsPage(out var crafts, saves, dialogs);
        crafts.Assets.Add(Craft("Apollo", CraftKind.Vab));
        await page.RefreshCommand.ExecuteAsync(null);

        await page.CopyToLibraryCommand.ExecuteAsync(null);

        var saveRoot = System.IO.Path.Combine("D:/KSP/saves", "M6_FlightUI", "Ships");
        Assert.That(dialogs.SelectOptions[^1], Is.EqualTo(new[] { "M6_FlightUI" }), "目标列表里不该出现当前库。");
        Assert.That(crafts.Transfers, Has.Count.EqualTo(1));
        Assert.That(crafts.Transfers[0].TargetShipsRoot, Is.EqualTo(saveRoot));
        Assert.That(crafts.Transfers[0].Move, Is.False);
        Assert.That(page.StatusText, Is.EqualTo("已复制「Apollo」到「M6_FlightUI」"));
    }

    [Test]
    public async Task Ships_MoveToLibrary_MovesCraftToChosenSaveFolder()
    {
        var saves = new InstancesFakeSaveFiles();
        saves.Saves.Add(Save("M6_FlightUI / persistent"));
        var page = NewShipsPage(out var crafts, saves);
        crafts.Assets.Add(Craft("Apollo", CraftKind.Vab));
        await page.RefreshCommand.ExecuteAsync(null);

        await page.MoveToLibraryCommand.ExecuteAsync(null);

        Assert.That(crafts.Transfers, Has.Count.EqualTo(1));
        Assert.That(crafts.Transfers[0].Move, Is.True);
        Assert.That(page.StatusText, Is.EqualTo("已移动「Apollo」到「M6_FlightUI」"));
    }

    [Test]
    public async Task Ships_CopyToLibrary_WithoutAnotherLibrary_ExplainsInsteadOfPrompting()
    {
        var dialogs = new InstancesFakeDialogs();
        var page = NewShipsPage(out var crafts, dialogs: dialogs);
        crafts.Assets.Add(Craft("Apollo", CraftKind.Vab));
        await page.RefreshCommand.ExecuteAsync(null);

        await page.CopyToLibraryCommand.ExecuteAsync(null);

        Assert.That(page.ErrorText, Is.EqualTo("没有别的载具库可以放。"));
        Assert.That(dialogs.SelectCalls, Is.Zero, "没有可选项时不该弹一个空选择框。");
        Assert.That(crafts.Transfers, Is.Empty);
    }

    [Test]
    public async Task Flags_Refresh_MarksCurrentDefaultFlag()
    {
        var page = NewFlagsPage(out var flags);
        flags.Assets.Add(Flag("default", "Squad/Flags/default"));
        flags.Assets.Add(Flag("red", "CkanUI/Flags/red"));
        flags.CurrentFlag = "Squad/Flags/default";

        await page.RefreshCommand.ExecuteAsync(null);

        Assert.That(page.Flags[0].IsDefault, Is.True, "settings.cfg 里记的那张才是默认旗帜。");
        Assert.That(page.Flags[1].IsDefault, Is.False);
        Assert.That(page.CurrentDefaultText, Is.EqualTo("当前默认：Squad/Flags/default"));
        Assert.That(page.StatusText, Is.EqualTo("共 2 张旗帜"));
    }

    [Test]
    public async Task Flags_Refresh_WithoutDefault_SaysSo()
    {
        var page = NewFlagsPage(out var flags);
        flags.CurrentFlag = null;

        await page.RefreshCommand.ExecuteAsync(null);

        Assert.That(page.CurrentDefaultText, Is.EqualTo("当前默认：未设置"));
    }

    [Test]
    public async Task Flags_SetDefault_SkipsWhenAlreadyDefault()
    {
        var page = NewFlagsPage(out var flags);
        flags.Assets.Add(Flag("default", "Squad/Flags/default"));
        flags.Assets.Add(Flag("red", "CkanUI/Flags/red"));
        flags.CurrentFlag = "Squad/Flags/default";
        await page.RefreshCommand.ExecuteAsync(null);

        await page.SetDefaultCommand.ExecuteAsync(null);
        Assert.That(flags.SetDefaultCalls, Is.Zero, "已经是默认了就别再写一遍 settings.cfg。");

        page.SelectedFlag = page.Flags[1];
        flags.CurrentFlag = "CkanUI/Flags/red";
        await page.SetDefaultCommand.ExecuteAsync(null);

        Assert.That(flags.SetDefaultCalls, Is.EqualTo(1));
        Assert.That(page.StatusText, Is.EqualTo("已把「red」设为默认旗帜"));
    }

    [Test]
    public async Task Flags_Import_WithoutPicker_ReportsInsteadOfThrowing()
    {
        var page = NewFlagsPage(out _);

        await page.ImportCommand.ExecuteAsync(null);

        Assert.That(page.ErrorText, Is.EqualTo("当前环境没有可用的文件选择器。"));
    }

    [Test]
    public async Task Loading_Refresh_ShowsOnlySelectedFolder()
    {
        var page = NewLoadingPage(out var images);
        images.Folders.Add("D:/KSP/Screenshots");
        images.Folders.Add("D:/KSP/GameData/LSM");
        images.Images.Add(Image("a.png", "D:/KSP/Screenshots"));
        images.Images.Add(Image("b.jpg", "D:/KSP/GameData/LSM"));

        await page.RefreshCommand.ExecuteAsync(null);

        Assert.That(page.Folders.Select(row => row.FolderPath), Is.EqualTo(new[] { "D:/KSP/Screenshots", "D:/KSP/GameData/LSM" }));
        Assert.That(page.SelectedFolder?.FolderPath, Is.EqualTo("D:/KSP/Screenshots"), "刷新后选中第一个目录，画廊才不会空。");
        Assert.That(page.Images.Select(row => row.FileName), Is.EqualTo(new[] { "a.png" }), "画廊只显示当前目录的图，否则点目录看不出任何变化。");
        Assert.That(page.SelectedImage, Is.SameAs(page.Images[0]));
        Assert.That(page.StatusText, Is.EqualTo("共 1 张 · 已启用 1 张"));
    }

    [Test]
    public async Task Loading_SwitchingFolder_FiltersGalleryInPlace()
    {
        var page = NewLoadingPage(out var images);
        images.Folders.Add("D:/KSP/Screenshots");
        images.Folders.Add("D:/KSP/GameData/LSM");
        images.Images.Add(Image("a.png", "D:/KSP/Screenshots"));
        images.Images.Add(Image("b.jpg", "D:/KSP/GameData/LSM"));
        await page.RefreshCommand.ExecuteAsync(null);

        page.SelectedFolder = page.Folders[1];

        Assert.That(page.Images.Select(row => row.FileName), Is.EqualTo(new[] { "b.jpg" }), "切目录必须换画廊，这是用户点路径时期待的反馈。");
        Assert.That(page.SelectedImage, Is.SameAs(page.Images[0]), "换目录后自动选中第一张，详情区才不会空。");
        Assert.That(page.StatusText, Is.EqualTo("共 1 张 · 已启用 1 张"), "计数跟着可见集合走。");
    }

    [Test]
    public async Task Loading_RemoveFolder_KeepsAtLeastOne()
    {
        var page = NewLoadingPage(out var images);
        images.Folders.Add("D:/KSP/Screenshots");
        await page.RefreshCommand.ExecuteAsync(null);

        await page.RemoveFolderCommand.ExecuteAsync(null);

        Assert.That(page.Folders.Count, Is.EqualTo(1), "最后一个目录删掉就没有可管理的地方了。");
        Assert.That(page.ErrorText, Is.EqualTo("至少要保留一个目录。"));
        Assert.That(images.SavedFolders, Is.Empty, "被拒的移除不该落盘。");
    }

    [Test]
    public async Task Loading_AddFolder_PersistsAndSelects()
    {
        var page = NewLoadingPage(out var images);
        var picker = new FakeAssetPicker { FolderResult = "D:/Shots" };
        page.FilePicker = picker;
        images.Folders.Add("D:/KSP/Screenshots");
        await page.RefreshCommand.ExecuteAsync(null);

        await page.AddFolderCommand.ExecuteAsync(null);

        Assert.That(page.Folders.Select(row => row.FolderPath), Does.Contain("D:/Shots"));
        Assert.That(page.SelectedFolder?.FolderPath, Is.EqualTo("D:/Shots"));
        Assert.That(images.SavedFolders, Has.Count.EqualTo(1), "新增目录必须落盘，否则重启就丢。");
        Assert.That(images.SavedFolders[0], Does.Contain("D:/Shots"));
    }

    [Test]
    public async Task Loading_Toggle_DisablesFileWithoutLosingTheCard()
    {
        var page = NewLoadingPage(out var images);
        images.Folders.Add("D:/KSP/Screenshots");
        images.Images.Add(Image("a.png", "D:/KSP/Screenshots"));
        await page.RefreshCommand.ExecuteAsync(null);

        Assert.That(page.Images[0].ToggleText, Is.EqualTo("停用"), "启用中的图，按钮该说「停用」。");

        await page.Images[0].ToggleCommand.ExecuteAsync(null);

        Assert.That(images.ToggleCalls, Is.EqualTo(new[] { ("D:/KSP/Screenshots/a.png", false) }),
            "停用要落到服务层，页面不能只改自己的显示状态。");
        Assert.That(page.Images, Has.Count.EqualTo(1), "停用后卡片必须还在，否则用户再也点不回来。");
        Assert.That(page.Images[0].IsDisabled, Is.True);
        Assert.That(page.Images[0].FileName, Is.EqualTo("a.png"), "文件名不带 .disabled，用户看到的还是他那张图。");
        Assert.That(page.Images[0].ToggleText, Is.EqualTo("启用"));
        Assert.That(page.SelectedImage, Is.SameAs(page.Images[0]), "启停会改路径，选中要按文件名+目录落回同一张。");
        Assert.That(page.StatusText, Is.EqualTo("已停用「a.png」，游戏不再读取它"), "动作完要给回执，而不是只留一个计数。");

        await page.Images[0].ToggleCommand.ExecuteAsync(null);

        Assert.That(page.Images[0].IsEnabled, Is.True);
        Assert.That(images.ToggleCalls[1].Enabled, Is.True);
    }

    [Test]
    public async Task Loading_Import_WithoutFolder_Reports()
    {
        var page = NewLoadingPage(out _);
        page.FilePicker = new FakeAssetPicker { FileResult = "D:/x.png" };

        await page.ImportCommand.ExecuteAsync(null);

        Assert.That(page.ErrorText, Is.EqualTo("先选一个目录再导入。"));
    }

    [Test]
    public async Task Ships_RefreshTwice_KeepsRowInstancesWhenDiskUnchanged()
    {
        var page = NewShipsPage(out var crafts);
        crafts.Assets.Add(Craft("Apollo", CraftKind.Vab));
        crafts.Assets.Add(Craft("Sparrow", CraftKind.Sph));
        await page.RefreshCommand.ExecuteAsync(null);

        var firstRow = page.Crafts[0];
        page.SelectedCraft = page.Crafts[1];
        var changes = 0;
        page.Crafts.CollectionChanged += (_, _) => changes++;

        await page.RefreshCommand.ExecuteAsync(null);

        Assert.That(changes, Is.Zero, "磁盘没变就不该重加行，否则列表容器整片重排、切回本页掉帧。");
        Assert.That(page.Crafts[0], Is.SameAs(firstRow), "行实例要复用，选中与滚动位置才不会被重置。");
        Assert.That(page.SelectedCraft?.FilePath, Is.EqualTo(page.Crafts[1].FilePath), "刷新不该改变用户选中的载具。");
    }

    [Test]
    public async Task Ships_RefreshTwice_RebuildsWhenAssetChanged()
    {
        var page = NewShipsPage(out var crafts);
        crafts.Assets.Add(Craft("Apollo", CraftKind.Vab));
        await page.RefreshCommand.ExecuteAsync(null);

        crafts.Assets.Add(Craft("Sparrow", CraftKind.Sph));
        await page.RefreshCommand.ExecuteAsync(null);

        Assert.That(page.Crafts.Count, Is.EqualTo(2), "磁盘变了必须重建，新载具要出现在列表里。");
    }

    private static ShipsPageViewModel NewShipsPage(
        out FakeCraftLibrary crafts,
        InstancesFakeSaveFiles? saves = null,
        InstancesFakeDialogs? dialogs = null)
    {
        crafts = new FakeCraftLibrary();
        return new ShipsPageViewModel(
            GameInstances(),
            crafts,
            saves ?? new InstancesFakeSaveFiles(),
            new InstancesFakeErrorReporter(),
            dialogs ?? new InstancesFakeDialogs());
    }

    private static SaveGameInfo Save(string slotName, bool backup = false) =>
        new(slotName,
            "D:/KSP/saves/" + slotName.Split(" / ")[0] + (backup ? "/persistent.sfs.bak" : "/persistent.sfs"),
            1024,
            new DateTimeOffset(2024, 5, 1, 12, 0, 0, TimeSpan.Zero),
            "测试存档",
            "CAREER",
            "1.12.5",
            "FLIGHT",
            "1234.5",
            12,
            "1000000",
            0,
            backup,
            true);

    [Test]
    public async Task Flags_RefreshTwice_KeepsCardsWhenNothingChanged()
    {
        var page = NewFlagsPage(out var flags);
        flags.Assets.Add(Flag("default", "Squad/Flags/default"));
        flags.Assets.Add(Flag("red", "CkanUI/Flags/red"));
        await page.RefreshCommand.ExecuteAsync(null);

        var firstCard = page.Flags[0];
        var changes = 0;
        page.Flags.CollectionChanged += (_, _) => changes++;

        await page.RefreshCommand.ExecuteAsync(null);

        Assert.That(changes, Is.Zero, "磁盘与默认旗帜都没变就不该重排整片卡片。");
        Assert.That(page.Flags[0], Is.SameAs(firstCard), "卡片实例要复用，缩略图才不会重解码。");
    }

    [Test]
    public async Task Flags_RefreshTwice_RebuildsWhenDefaultFlagChanged()
    {
        var page = NewFlagsPage(out var flags);
        flags.Assets.Add(Flag("default", "Squad/Flags/default"));
        flags.Assets.Add(Flag("red", "CkanUI/Flags/red"));
        flags.CurrentFlag = "Squad/Flags/default";
        await page.RefreshCommand.ExecuteAsync(null);

        flags.CurrentFlag = "CkanUI/Flags/red";
        await page.RefreshCommand.ExecuteAsync(null);

        Assert.That(page.Flags[1].IsDefault, Is.True, "默认旗帜换了，徽标必须跟着换，不能被「没变化」的门挡住。");
    }

    [Test]
    public async Task Loading_RefreshTwice_KeepsCardsWhenNothingChanged()
    {
        var page = NewLoadingPage(out var images);
        images.Folders.Add("D:/KSP/Screenshots");
        images.Images.Add(Image("a.png", "D:/KSP/Screenshots"));
        await page.RefreshCommand.ExecuteAsync(null);

        var firstCard = page.Images[0];
        var changes = 0;
        page.Images.CollectionChanged += (_, _) => changes++;

        await page.RefreshCommand.ExecuteAsync(null);

        Assert.That(changes, Is.Zero, "扫描结果没变就不该重建画廊，否则每切回来都要重排整片卡片。");
        Assert.That(page.Images[0], Is.SameAs(firstCard));
    }

    [Test]
    public async Task Loading_RecognizedModFolder_ShowsProviderBadge_AndHidesDdsPreview()
    {
        var page = NewLoadingPage(out var images);
        var screens = "D:/KSP/GameData/ROLoadingImages/PluginData/Screens";
        images.Folders.Add(screens);
        images.Images.Add(Image("rp1.dds", screens));
        images.Providers[screens] = new LoadingImageFolderInfo("ROLoadingImages");

        await page.RefreshCommand.ExecuteAsync(null);

        Assert.That(page.Images, Has.Count.EqualTo(1));
        Assert.That(page.Images[0].HasProvider, Is.True, "认出来源才能告诉用户这 72 张是 RP-1 在吃。");
        Assert.That(page.Images[0].ProviderText, Is.EqualTo("ROLoadingImages"));
        Assert.That(page.Images[0].FormatText, Is.EqualTo("DDS"));
        Assert.That(images.LastRecursiveFolders, Is.Empty, "ROLoadingImages 只读单层目录，别把子目录也扫进来。");
    }

    [Test]
    public async Task Loading_RecursiveFolder_IsHandedToScanner()
    {
        var page = NewLoadingPage(out var images);
        var screenshots = "D:/KSP/Screenshots";
        images.Folders.Add(screenshots);
        images.Images.Add(Image("shot.png", screenshots));
        images.Providers[screenshots] = new LoadingImageFolderInfo("LoadingScreenManager", Recursive: true);

        await page.RefreshCommand.ExecuteAsync(null);

        Assert.That(images.LastRecursiveFolders, Is.EqualTo(new[] { System.IO.Path.GetFullPath(screenshots) }),
            "LSM 的 ignoreSubfolders = False 意味着要连子目录一起扫，页面得把目录传给扫描器。");
    }

    private static FlagsPageViewModel NewFlagsPage(out FakeFlagLibrary flags)
    {
        flags = new FakeFlagLibrary();
        return new FlagsPageViewModel(GameInstances(), flags, new InstancesFakeErrorReporter(), new InstancesFakeDialogs());
    }

    private static LoadingImagesPageViewModel NewLoadingPage(out FakeLoadingImageLibrary images)
    {
        images = new FakeLoadingImageLibrary();
        return new LoadingImagesPageViewModel(GameInstances(), images, new InstancesFakeErrorReporter(), new InstancesFakeDialogs());
    }

    private static InstancesFakeService GameInstances()
    {
        var instances = new InstancesFakeService();
        var info = new InstanceInfo("测试实例", "D:/KSP", "1.12.5", true, false, 0);
        instances.Seed(info);
        instances.Current = info;
        return instances;
    }

    private static CraftAsset Craft(string name, CraftKind kind) =>
        new(name,
            "D:/KSP/" + (kind == CraftKind.Vab ? "Ships/VAB" : "Ships/SPH") + "/" + name + ".craft",
            kind,
            null,
            "1.12.5",
            42,
            3,
            2048,
            new DateTimeOffset(2024, 5, 1, 12, 0, 0, TimeSpan.Zero));

    private static FlagAsset Flag(string name, string configPath) =>
        new(name, "D:/KSP/GameData/" + configPath + ".png", configPath, configPath.Split('/')[0], 1024);

    private static LoadingImageAsset Image(string fileName, string folder) =>
        new(fileName, folder + "/" + fileName, folder, 4096, new DateTimeOffset(2024, 5, 1, 12, 0, 0, TimeSpan.Zero));
}

internal sealed class FakeCraftLibrary : ICraftLibraryService
{
    public List<CraftAsset> Assets { get; } = new();

    public OperationResult<IReadOnlyList<CraftAsset>>? ScanFailure { get; set; }

    public int ScanCalls { get; private set; }

    public int DeleteCalls { get; private set; }

    public List<string> ImportedSources { get; } = new();

    public List<string> NewNames { get; } = new();

    public string GetKindRelativePath(CraftKind kind) => kind switch
    {
        CraftKind.Vab => "Ships/VAB",
        CraftKind.Sph => "Ships/SPH",
        _ => "Ships/Subassemblies",
    };

    /// <summary>每次整库扫描收到的载具库根目录（&lt;游戏&gt;/Ships 或 &lt;游戏&gt;/saves/&lt;存档&gt;/Ships）。</summary>
    public List<string> LibraryScans { get; } = new();

    /// <summary>每次按库导入收到的参数。</summary>
    public List<(string SourceFile, string ShipsRoot, CraftKind Kind)> LibraryImports { get; } = new();

    /// <summary>每次跨库搬运收到的参数。</summary>
    public List<(string TargetShipsRoot, CraftKind TargetKind, bool Move)> Transfers { get; } = new();

    public Task<OperationResult<IReadOnlyList<CraftAsset>>> ScanAsync(string gameDir, CancellationToken cancellationToken = default) =>
        ScanLibraryAsync(System.IO.Path.Combine(gameDir, "Ships"), cancellationToken);

    public Task<OperationResult<IReadOnlyList<CraftAsset>>> ScanLibraryAsync(string shipsRoot, CancellationToken cancellationToken = default)
    {
        ScanCalls++;
        LibraryScans.Add(shipsRoot);
        return Task.FromResult(ScanFailure ?? OperationResult<IReadOnlyList<CraftAsset>>.Ok(Assets.ToList()));
    }

    public Task<OperationResult<CraftAsset>> ImportAsync(string gameDir, string sourceFile, CraftKind kind, CancellationToken cancellationToken = default) =>
        ImportToLibraryAsync(System.IO.Path.Combine(gameDir, "Ships"), sourceFile, kind, cancellationToken);

    public Task<OperationResult<CraftAsset>> ImportToLibraryAsync(string shipsRoot, string sourceFile, CraftKind kind, CancellationToken cancellationToken = default)
    {
        ImportedSources.Add(sourceFile);
        LibraryImports.Add((sourceFile, shipsRoot, kind));
        var name = System.IO.Path.GetFileNameWithoutExtension(sourceFile);
        var asset = new CraftAsset(name, shipsRoot + "/" + KindFolder(kind) + "/" + name + ".craft", kind, null, null, null, null, 0, DateTimeOffset.UnixEpoch);
        Assets.Add(asset);
        return Task.FromResult(OperationResult<CraftAsset>.Ok(asset));
    }

    public Task<OperationResult<CraftAsset>> TransferAsync(
        CraftAsset craft,
        string targetShipsRoot,
        CraftKind targetKind,
        bool move,
        CancellationToken cancellationToken = default)
    {
        Transfers.Add((targetShipsRoot, targetKind, move));
        var moved = craft with
        {
            Kind = targetKind,
            FilePath = targetShipsRoot + "/" + KindFolder(targetKind) + "/" + craft.Name + ".craft",
        };
        if (move)
        {
            Assets.RemoveAll(item => item.FilePath == craft.FilePath);
        }

        Assets.Add(moved);
        return Task.FromResult(OperationResult<CraftAsset>.Ok(moved));
    }

    private static string KindFolder(CraftKind kind) => kind switch
    {
        CraftKind.Vab => "VAB",
        CraftKind.Sph => "SPH",
        _ => "Subassemblies",
    };

    public Task<OperationResult<CraftAsset>> DuplicateAsync(string gameDir, CraftAsset craft, string newName, CancellationToken cancellationToken = default)
    {
        NewNames.Add(newName);
        var copy = craft with { Name = newName, FilePath = craft.FilePath.Replace(craft.Name + ".craft", newName + ".craft", StringComparison.Ordinal) };
        Assets.Add(copy);
        return Task.FromResult(OperationResult<CraftAsset>.Ok(copy));
    }

    public Task<OperationResult<CraftAsset>> RenameAsync(string gameDir, CraftAsset craft, string newName, CancellationToken cancellationToken = default)
    {
        NewNames.Add(newName);
        var renamed = craft with { Name = newName, FilePath = craft.FilePath.Replace(craft.Name + ".craft", newName + ".craft", StringComparison.Ordinal) };
        var index = Assets.FindIndex(item => item.FilePath == craft.FilePath);
        if (index >= 0)
        {
            Assets[index] = renamed;
        }

        return Task.FromResult(OperationResult<CraftAsset>.Ok(renamed));
    }

    public Task<OperationResult> DeleteAsync(string gameDir, CraftAsset craft, CancellationToken cancellationToken = default)
    {
        DeleteCalls++;
        Assets.RemoveAll(item => item.FilePath == craft.FilePath);
        return Task.FromResult(OperationResult.Ok());
    }
}

internal sealed class FakeFlagLibrary : IFlagLibraryService
{
    public List<FlagAsset> Assets { get; } = new();

    public string? CurrentFlag { get; set; }

    public int SetDefaultCalls { get; private set; }

    public int DeleteCalls { get; private set; }

    public Task<OperationResult<IReadOnlyList<FlagAsset>>> ScanAsync(string gameDir, CancellationToken cancellationToken = default) =>
        Task.FromResult(OperationResult<IReadOnlyList<FlagAsset>>.Ok(Assets.ToList()));

    public OperationResult<string?> ReadDefaultFlag(string gameDir) => OperationResult<string?>.Ok(CurrentFlag);

    public Task<OperationResult> SetDefaultFlagAsync(string gameDir, FlagAsset flag, CancellationToken cancellationToken = default)
    {
        SetDefaultCalls++;
        CurrentFlag = flag.ConfigPath;
        return Task.FromResult(OperationResult.Ok());
    }

    public Task<OperationResult<FlagAsset>> ImportAsync(string gameDir, string sourceFile, CancellationToken cancellationToken = default)
    {
        var name = System.IO.Path.GetFileNameWithoutExtension(sourceFile);
        var asset = new FlagAsset(name, gameDir + "/GameData/CkanUI/Flags/" + name + ".png", "CkanUI/Flags/" + name, "CkanUI", 0);
        Assets.Add(asset);
        return Task.FromResult(OperationResult<FlagAsset>.Ok(asset));
    }

    public Task<OperationResult<FlagAsset>> RenameAsync(string gameDir, FlagAsset flag, string newName, CancellationToken cancellationToken = default) =>
        Task.FromResult(OperationResult<FlagAsset>.Ok(flag with { Name = newName }));

    public Task<OperationResult> DeleteAsync(string gameDir, FlagAsset flag, CancellationToken cancellationToken = default)
    {
        DeleteCalls++;
        Assets.RemoveAll(item => item.ConfigPath == flag.ConfigPath);
        return Task.FromResult(OperationResult.Ok());
    }
}

internal sealed class FakeLoadingImageLibrary : ILoadingImageService
{
    public List<string> Folders { get; } = new();

    public List<LoadingImageAsset> Images { get; } = new();

    public List<IReadOnlyList<string>> SavedFolders { get; } = new();

    public IReadOnlyList<string> DefaultFolders(string gameDir) => Folders.ToList();

    public Dictionary<string, LoadingImageFolderInfo> Providers { get; } = new(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyList<string> LastRecursiveFolders { get; private set; } = Array.Empty<string>();

    public IReadOnlyDictionary<string, LoadingImageFolderInfo> DescribeFolders(string gameDir) =>
        Providers.ToDictionary(pair => System.IO.Path.GetFullPath(pair.Key), pair => pair.Value, StringComparer.OrdinalIgnoreCase);

    public OperationResult<IReadOnlyList<string>> LoadFolders(string gameDir) => OperationResult<IReadOnlyList<string>>.Ok(Folders.ToList());

    public OperationResult SaveFolders(string gameDir, IReadOnlyList<string> folders)
    {
        SavedFolders.Add(folders.ToList());
        Folders.Clear();
        Folders.AddRange(folders);
        return OperationResult.Ok();
    }

    public Task<OperationResult<IReadOnlyList<LoadingImageAsset>>> ScanAsync(
        IReadOnlyList<string> folders,
        IReadOnlyCollection<string>? recursiveFolders = null,
        CancellationToken cancellationToken = default)
    {
        LastRecursiveFolders = recursiveFolders?.ToArray() ?? Array.Empty<string>();
        return Task.FromResult(OperationResult<IReadOnlyList<LoadingImageAsset>>.Ok(
            Images.Where(image => folders.Contains(image.Folder, StringComparer.OrdinalIgnoreCase)).ToList()));
    }

    public Task<OperationResult<LoadingImageAsset>> ImportAsync(string targetFolder, string sourceFile, CancellationToken cancellationToken = default)
    {
        var fileName = System.IO.Path.GetFileName(sourceFile);
        var asset = new LoadingImageAsset(fileName, targetFolder + "/" + fileName, targetFolder, 0, DateTimeOffset.UnixEpoch);
        Images.Add(asset);
        return Task.FromResult(OperationResult<LoadingImageAsset>.Ok(asset));
    }

    public List<(string FilePath, bool Enabled)> ToggleCalls { get; } = new();

    public Task<OperationResult<LoadingImageAsset>> SetEnabledAsync(LoadingImageAsset image, bool enabled, CancellationToken cancellationToken = default)
    {
        ToggleCalls.Add((image.FilePath, enabled));
        var path = image.FilePath.EndsWith(".disabled", StringComparison.OrdinalIgnoreCase)
            ? image.FilePath[..^".disabled".Length]
            : image.FilePath;
        if (!enabled)
        {
            path += ".disabled";
        }

        var updated = image with { FilePath = path, IsEnabled = enabled };
        var index = Images.FindIndex(item => item.FilePath == image.FilePath);
        if (index >= 0)
        {
            Images[index] = updated;
        }

        return Task.FromResult(OperationResult<LoadingImageAsset>.Ok(updated));
    }

    public Task<OperationResult> DeleteAsync(LoadingImageAsset image, CancellationToken cancellationToken = default)
    {
        Images.RemoveAll(item => item.FilePath == image.FilePath);
        return Task.FromResult(OperationResult.Ok());
    }

    public bool IsSupportedImage(string path) =>
        path.EndsWith(".png", StringComparison.OrdinalIgnoreCase)
        || path.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase)
        || path.EndsWith(".dds", StringComparison.OrdinalIgnoreCase);
}

internal sealed class FakeAssetPicker : IAssetFilePicker
{
    public string? FileResult { get; set; }

    public string? FolderResult { get; set; }

    public List<IReadOnlyList<string>> RequestedExtensions { get; } = new();

    public Task<string?> PickFileAsync(string title, IReadOnlyList<string> extensions, CancellationToken cancellationToken = default)
    {
        RequestedExtensions.Add(extensions);
        return Task.FromResult(FileResult);
    }

    public Task<string?> PickFolderAsync(string title, string? startPath = null, CancellationToken cancellationToken = default) =>
        Task.FromResult(FolderResult);
}

internal sealed class FakeShellLauncher : IAssetsShellLauncher
{
    public List<string> Revealed { get; } = new();

    public OperationResult Reveal(string path)
    {
        Revealed.Add(path);
        return OperationResult.Ok();
    }
}
