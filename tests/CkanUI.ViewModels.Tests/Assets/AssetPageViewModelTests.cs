using CkanUI.Services.Abstractions;
using CkanUI.Services.Abstractions.Assets;
using CkanUI.Services.Abstractions.Dtos;
using CkanUI.ViewModels.Pages.Assets;
using CkanUI.ViewModels.Pages.Instances;

using NUnit.Framework;

namespace CkanUI.ViewModels.Tests.Assets;

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
        var page = new ShipsPageViewModel(instances, crafts, new InstancesFakeErrorReporter(), new InstancesFakeDialogs());

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
        var page = new ShipsPageViewModel(GameInstances(), crafts, new InstancesFakeErrorReporter(), dialogs);
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
    public async Task Loading_Refresh_LoadsFoldersAndImages()
    {
        var page = NewLoadingPage(out var images);
        images.Folders.Add("D:/KSP/Screenshots");
        images.Folders.Add("D:/KSP/GameData/LSM");
        images.Images.Add(Image("a.png", "D:/KSP/Screenshots"));
        images.Images.Add(Image("b.jpg", "D:/KSP/GameData/LSM"));

        await page.RefreshCommand.ExecuteAsync(null);

        Assert.That(page.Folders, Is.EqualTo(new[] { "D:/KSP/Screenshots", "D:/KSP/GameData/LSM" }));
        Assert.That(page.SelectedFolder, Is.EqualTo("D:/KSP/Screenshots"), "刷新后选中第一个目录，画廊才不会空。");
        Assert.That(page.Images.Count, Is.EqualTo(2));
        Assert.That(page.SelectedImage, Is.SameAs(page.Images[0]));
        Assert.That(page.StatusText, Is.EqualTo("共 2 张"));
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

        Assert.That(page.Folders, Does.Contain("D:/Shots"));
        Assert.That(page.SelectedFolder, Is.EqualTo("D:/Shots"));
        Assert.That(images.SavedFolders, Has.Count.EqualTo(1), "新增目录必须落盘，否则重启就丢。");
        Assert.That(images.SavedFolders[0], Does.Contain("D:/Shots"));
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

    private static ShipsPageViewModel NewShipsPage(out FakeCraftLibrary crafts)
    {
        crafts = new FakeCraftLibrary();
        return new ShipsPageViewModel(GameInstances(), crafts, new InstancesFakeErrorReporter(), new InstancesFakeDialogs());
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

    public Task<OperationResult<IReadOnlyList<CraftAsset>>> ScanAsync(string gameDir, CancellationToken cancellationToken = default)
    {
        ScanCalls++;
        return Task.FromResult(ScanFailure ?? OperationResult<IReadOnlyList<CraftAsset>>.Ok(Assets.ToList()));
    }

    public Task<OperationResult<CraftAsset>> ImportAsync(string gameDir, string sourceFile, CraftKind kind, CancellationToken cancellationToken = default)
    {
        ImportedSources.Add(sourceFile);
        var name = System.IO.Path.GetFileNameWithoutExtension(sourceFile);
        var asset = new CraftAsset(name, gameDir + "/" + GetKindRelativePath(kind) + "/" + name + ".craft", kind, null, null, null, null, 0, DateTimeOffset.UnixEpoch);
        Assets.Add(asset);
        return Task.FromResult(OperationResult<CraftAsset>.Ok(asset));
    }

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

    public OperationResult<IReadOnlyList<string>> LoadFolders(string gameDir) => OperationResult<IReadOnlyList<string>>.Ok(Folders.ToList());

    public OperationResult SaveFolders(string gameDir, IReadOnlyList<string> folders)
    {
        SavedFolders.Add(folders.ToList());
        Folders.Clear();
        Folders.AddRange(folders);
        return OperationResult.Ok();
    }

    public Task<OperationResult<IReadOnlyList<LoadingImageAsset>>> ScanAsync(IReadOnlyList<string> folders, CancellationToken cancellationToken = default) =>
        Task.FromResult(OperationResult<IReadOnlyList<LoadingImageAsset>>.Ok(
            Images.Where(image => folders.Contains(image.Folder, StringComparer.OrdinalIgnoreCase)).ToList()));

    public Task<OperationResult<LoadingImageAsset>> ImportAsync(string targetFolder, string sourceFile, CancellationToken cancellationToken = default)
    {
        var fileName = System.IO.Path.GetFileName(sourceFile);
        var asset = new LoadingImageAsset(fileName, targetFolder + "/" + fileName, targetFolder, 0, DateTimeOffset.UnixEpoch);
        Images.Add(asset);
        return Task.FromResult(OperationResult<LoadingImageAsset>.Ok(asset));
    }

    public Task<OperationResult> DeleteAsync(LoadingImageAsset image, CancellationToken cancellationToken = default)
    {
        Images.RemoveAll(item => item.FilePath == image.FilePath);
        return Task.FromResult(OperationResult.Ok());
    }

    public bool IsSupportedImage(string path) =>
        path.EndsWith(".png", StringComparison.OrdinalIgnoreCase) || path.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase);
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
