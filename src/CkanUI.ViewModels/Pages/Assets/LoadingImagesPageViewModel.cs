using System.Collections.ObjectModel;
using CkanUI.Localization;
using CkanUI.Services.Abstractions;
using CkanUI.Services.Abstractions.Assets;
using CkanUI.Services.Abstractions.Dtos;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace CkanUI.ViewModels.Pages.Assets;

/// <summary>加载图页：维护目录清单，管理目录里的加载图。</summary>
/// <remarks>
/// 原版加载图内嵌在游戏资源里，所以这里管的是「哪些目录算加载图来源」+ 目录里的图片。
/// 停用一张图靠改扩展名（<c>a.dds</c> → <c>a.dds.disabled</c>）：两个加载图模组都只认原扩展名，
/// 改完游戏自然跳过，而且随时能原样改回来，不删文件。
/// </remarks>
public sealed partial class LoadingImagesPageViewModel : AssetPageViewModel
{
    private static readonly string[] ImageExtensions = [".png", ".jpg", ".jpeg", ".dds"];

    private readonly ILoadingImageService _images;

    // 上一次扫描到的资产：值不变就不重建卡片列表。
    private IReadOnlyList<LoadingImageAsset>? _lastAssets;

    // 上一次画廊用的目录路径与来源表，切目录时直接按缓存过滤，不用重新扫盘。
    private string? _lastFilterFolder;

    private IReadOnlyDictionary<string, LoadingImageFolderInfo>? _lastProviders;

    // 刷新时重建目录行会改选中项，不该顺带触发一次「切目录」过滤。
    private bool _suppressFolderFilter;

    private bool _subscribed;

    /// <summary>构造页面。</summary>
    /// <param name="instances">实例服务。</param>
    /// <param name="images">加载图库服务。</param>
    /// <param name="errors">错误上报口。</param>
    /// <param name="dialogs">对话服务。</param>
    public LoadingImagesPageViewModel(
        IInstanceService instances,
        ILoadingImageService images,
        IErrorReporter errors,
        IDialogService dialogs)
        : base(instances, errors, dialogs) =>
        _images = images ?? throw new ArgumentNullException(nameof(images));

    /// <summary>文件选择通道，由视图注入。</summary>
    public IAssetFilePicker? FilePicker { get; set; }

    /// <summary>目录清单。</summary>
    public ObservableCollection<LoadingImageFolderRowViewModel> Folders { get; } = [];

    /// <summary>当前目录下的加载图（含已停用的）。</summary>
    public ObservableCollection<LoadingImageRowViewModel> Images { get; } = [];

    /// <summary>当前选中的目录：既是导入落点，也是画廊的筛选条件。</summary>
    [ObservableProperty]
    private LoadingImageFolderRowViewModel? _selectedFolder;

    /// <summary>当前选中的图片。</summary>
    [ObservableProperty]
    private LoadingImageRowViewModel? _selectedImage;

    /// <inheritdoc />
    public override string Title => Text("加载图", LocalizationKeys.LoadingImagesTitle);

    /// <inheritdoc />
    public override bool ShowEmptyState => !IsBusy && Images.Count == 0;

    /// <inheritdoc />
    protected override void OnActivated()
    {
        if (!_subscribed)
        {
            Instances.CurrentChanged += OnInstanceChanged;
            _subscribed = true;
        }

        _ = RefreshCoreAsync();
    }

    /// <inheritdoc />
    protected override void OnDeactivated()
    {
        if (_subscribed)
        {
            Instances.CurrentChanged -= OnInstanceChanged;
            _subscribed = false;
        }
    }

    /// <summary>刷新目录清单与图片列表。</summary>
    [RelayCommand]
    private Task RefreshAsync() => RefreshCoreAsync();

    /// <summary>往目录清单里加一个目录。</summary>
    [RelayCommand]
    private async Task AddFolderAsync()
    {
        if (FilePicker is null)
        {
            SetError(Text("当前环境没有可用的文件选择器。", LocalizationKeys.AssetsErrorNoPicker));
            return;
        }

        var gameDir = CurrentGameDir();
        if (gameDir is null)
        {
            return;
        }

        try
        {
            var folder = await FilePicker.PickFolderAsync(
                Text("添加目录", LocalizationKeys.LoadingImagesAddFolder),
                SelectedFolder?.FolderPath).ConfigureAwait(true);
            if (string.IsNullOrWhiteSpace(folder))
            {
                return;
            }

            if (!Folders.Any(row => string.Equals(row.FolderPath, folder, StringComparison.OrdinalIgnoreCase)))
            {
                Folders.Add(new LoadingImageFolderRowViewModel(folder));
                var saved = SaveFolders(gameDir);
                if (!saved.IsSuccess)
                {
                    return;
                }
            }

            _suppressFolderFilter = true;
            try
            {
                SelectedFolder = Folders.FirstOrDefault(row => string.Equals(row.FolderPath, folder, StringComparison.OrdinalIgnoreCase));
            }
            finally
            {
                _suppressFolderFilter = false;
            }

            await RefreshCoreAsync().ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            Errors.Report(ex, "加载图页", "添加目录失败");
            SetError(Text("保存目录清单失败。", LocalizationKeys.LoadingImagesErrorSaveFolders), ex.Message);
        }
    }

    /// <summary>从目录清单里移除选中目录。</summary>
    [RelayCommand]
    private async Task RemoveFolderAsync()
    {
        var gameDir = CurrentGameDir();
        var folder = SelectedFolder;
        if (gameDir is null || folder is null)
        {
            return;
        }

        if (Folders.Count <= 1)
        {
            SetError(Text("至少要保留一个目录。", LocalizationKeys.LoadingImagesRemoveFolderLast));
            return;
        }

        Folders.Remove(folder);
        var saved = SaveFolders(gameDir);
        if (!saved.IsSuccess)
        {
            return;
        }

        SelectedFolder = Folders.FirstOrDefault();
        await RefreshCoreAsync().ConfigureAwait(true);
    }

    /// <summary>导入一张加载图到选中目录。</summary>
    [RelayCommand]
    private async Task ImportAsync()
    {
        if (FilePicker is null)
        {
            SetError(Text("当前环境没有可用的文件选择器。", LocalizationKeys.AssetsErrorNoPicker));
            return;
        }

        var target = SelectedFolder?.FolderPath;
        if (string.IsNullOrWhiteSpace(target))
        {
            SetError(Text("先选一个目录再导入。", LocalizationKeys.LoadingImagesImportNoFolder));
            return;
        }

        try
        {
            var file = await FilePicker.PickFileAsync(Text("导入加载图", LocalizationKeys.LoadingImagesImportTitle), ImageExtensions).ConfigureAwait(true);
            if (string.IsNullOrWhiteSpace(file))
            {
                return;
            }

            var result = await RunBusyAsync(
                Text("正在导入…", LocalizationKeys.AssetsBusyImport),
                () => _images.ImportAsync(target, file),
                Text("导入失败。", LocalizationKeys.AssetsErrorImport)).ConfigureAwait(true);
            if (result is not { IsSuccess: true, Value: { } image })
            {
                return;
            }

            await RefreshCoreAsync().ConfigureAwait(true);
            SelectedImage = Images.FirstOrDefault(row => string.Equals(row.FilePath, image.FilePath, StringComparison.OrdinalIgnoreCase));
            StatusText = Text("已导入「{0}」", LocalizationKeys.LoadingImagesImportDone, image.FileName);
        }
        catch (Exception ex)
        {
            Errors.Report(ex, "加载图页", "导入加载图失败");
            SetError(Text("导入失败。", LocalizationKeys.AssetsErrorImport), ex.Message);
        }
    }

    /// <summary>删除选中图片。</summary>
    [RelayCommand(CanExecute = nameof(HasSelection))]
    private async Task DeleteAsync()
    {
        var row = SelectedImage;
        if (row is null)
        {
            return;
        }

        var confirmed = await Dialogs.ConfirmAsync(
            Text("删除确认", LocalizationKeys.AssetsDeleteTitle),
            Text("删除图片「{0}」？", LocalizationKeys.LoadingImagesDeleteMessage, row.FileName)).ConfigureAwait(true);
        if (!confirmed)
        {
            return;
        }

        var result = await RunBusyAsync(
            Text("处理中…", LocalizationKeys.AssetsBusyWorking),
            () => _images.DeleteAsync(row.Asset),
            Text("删除失败。", LocalizationKeys.AssetsErrorDelete)).ConfigureAwait(true);
        if (result is not { IsSuccess: true })
        {
            return;
        }

        await RefreshCoreAsync().ConfigureAwait(true);
        StatusText = Text("已删除「{0}」", LocalizationKeys.LoadingImagesDeleteDone, row.FileName);
    }

    /// <summary>在系统文件管理器里打开选中图片所在目录。</summary>
    [RelayCommand(CanExecute = nameof(HasSelection))]
    private void OpenFolder()
    {
        if (SelectedImage is { } row)
        {
            Reveal(row.Folder);
        }
    }

    private bool HasSelection() => SelectedImage is not null;

    partial void OnSelectedImageChanged(LoadingImageRowViewModel? value)
    {
        DeleteCommand.NotifyCanExecuteChanged();
        OpenFolderCommand.NotifyCanExecuteChanged();
    }

    partial void OnSelectedFolderChanged(LoadingImageFolderRowViewModel? value)
    {
        if (_suppressFolderFilter || IsBusy || _lastAssets is null)
        {
            return;
        }

        RebuildGallery(_lastAssets, _lastProviders ?? new Dictionary<string, LoadingImageFolderInfo>(), selectFirst: true);
    }

    private void OnInstanceChanged(InstanceInfo? instance) => _ = RefreshCoreAsync();

    private OperationResult SaveFolders(string gameDir)
    {
        var result = _images.SaveFolders(gameDir, Folders.Select(row => row.FolderPath).ToList());
        if (!result.IsSuccess)
        {
            SetError(result.Message ?? Text("保存目录清单失败。", LocalizationKeys.LoadingImagesErrorSaveFolders), result.Detail);
        }

        return result;
    }

    /// <summary>启用 / 停用一张图：改扩展名，然后重扫并把选中项落回这张图。</summary>
    /// <param name="row">被点的卡片。</param>
    private async Task ToggleImageAsync(LoadingImageRowViewModel row)
    {
        if (IsBusy)
        {
            return;
        }

        var enable = !row.IsEnabled;
        var result = await RunBusyAsync(
            enable
                ? Text("正在启用…", LocalizationKeys.LoadingImagesBusyEnable)
                : Text("正在停用…", LocalizationKeys.LoadingImagesBusyDisable),
            () => _images.SetEnabledAsync(row.Asset, enable),
            Text("调整加载图启停失败。", LocalizationKeys.LoadingImagesErrorToggle)).ConfigureAwait(true);
        if (result is not { IsSuccess: true, Value: { } updated })
        {
            return;
        }

        await RefreshCoreAsync().ConfigureAwait(true);

        // 启停会改文件名，按路径找不回来，只能按「文件名 + 目录」落选中。
        SelectedImage = Images.FirstOrDefault(item =>
            string.Equals(item.FileName, updated.FileName, StringComparison.OrdinalIgnoreCase)
            && string.Equals(item.Folder, updated.Folder, StringComparison.OrdinalIgnoreCase));

        StatusText = enable
            ? Text("已启用「{0}」", LocalizationKeys.LoadingImagesEnableDone, updated.FileName)
            : Text("已停用「{0}」，游戏不再读取它", LocalizationKeys.LoadingImagesDisableDone, updated.FileName);
    }

    private static LoadingImageAsset WithProvider(LoadingImageAsset asset, IReadOnlyDictionary<string, LoadingImageFolderInfo> providers)
    {
        if (providers.Count == 0)
        {
            return asset;
        }

        try
        {
            var key = Path.GetFullPath(asset.Folder).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            return providers.TryGetValue(key, out var info) ? asset with { Provider = info.Provider } : asset;
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return asset;
        }
    }

    private static LoadingImageFolderInfo? ResolveInfo(string folder, IReadOnlyDictionary<string, LoadingImageFolderInfo> providers)
    {
        if (providers.Count == 0)
        {
            return null;
        }

        try
        {
            var key = Path.GetFullPath(folder).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            return providers.TryGetValue(key, out var info) ? info : null;
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return null;
        }
    }

    private async Task RefreshCoreAsync()
    {
        if (IsBusy)
        {
            return;
        }

        var gameDir = CurrentGameDir();
        if (gameDir is null)
        {
            Folders.Clear();
            Images.Clear();
            NotifyEmptyState();
            return;
        }

        var loaded = _images.LoadFolders(gameDir);
        if (!loaded.IsSuccess || loaded.Value is not { } folders)
        {
            SetError(loaded.Message ?? Text("刷新加载图失败。", LocalizationKeys.LoadingImagesErrorScan), loaded.Detail);
            NotifyEmptyState();
            return;
        }

        // 目录 → 来源（ROLoadingImages 的 dds 目录、LoadingScreenManager 配置的目录），侧栏徽章和卡片徽章共用。
        var providers = _images.DescribeFolders(gameDir);

        var keepFolder = SelectedFolder?.FolderPath;
        _suppressFolderFilter = true;
        try
        {
            Folders.Clear();
            foreach (var folder in folders)
            {
                Folders.Add(new LoadingImageFolderRowViewModel(folder, ResolveInfo(folder, providers)));
            }

            SelectedFolder = keepFolder is null
                ? Folders.FirstOrDefault()
                : Folders.FirstOrDefault(row => string.Equals(row.FolderPath, keepFolder, StringComparison.OrdinalIgnoreCase)) ?? Folders.FirstOrDefault();
        }
        finally
        {
            _suppressFolderFilter = false;
        }

        var recursive = providers.Where(pair => pair.Value.Recursive).Select(pair => pair.Key).ToArray();

        var result = await RunBusyAsync(
            Text("正在扫描加载图…", LocalizationKeys.LoadingImagesBusyScanning),
            () => _images.ScanAsync(folders, recursive),
            Text("刷新加载图失败。", LocalizationKeys.LoadingImagesErrorScan)).ConfigureAwait(true);
        if (result is not { IsSuccess: true, Value: { } items })
        {
            NotifyEmptyState();
            return;
        }

        // 磁盘扫描结果没变、目录也没换：保留已实例化的卡片，重排整片 WrapPanel 才是切回本页的卡顿来源。
        var unchanged = _lastAssets is not null && _lastAssets.Count == items.Count && _lastAssets.SequenceEqual(items);
        _lastAssets = items.ToArray();
        _lastProviders = providers;
        if (unchanged
            && string.Equals(_lastFilterFolder, SelectedFolder?.FolderPath, StringComparison.OrdinalIgnoreCase)
            && Images.Count == CountVisible(_lastAssets, SelectedFolder))
        {
            NotifyEmptyState();
            return;
        }

        RebuildGallery(_lastAssets, providers, selectFirst: false);
    }

    private static int CountVisible(IReadOnlyList<LoadingImageAsset> assets, LoadingImageFolderRowViewModel? folder)
    {
        var path = folder?.FolderPath;
        return string.IsNullOrWhiteSpace(path)
            ? assets.Count
            : assets.Count(asset => string.Equals(asset.Folder, path, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>按当前目录重建画廊。切目录只过滤内存里的扫描结果，不重新读盘。</summary>
    private void RebuildGallery(
        IReadOnlyList<LoadingImageAsset> assets,
        IReadOnlyDictionary<string, LoadingImageFolderInfo> providers,
        bool selectFirst)
    {
        var folder = SelectedFolder?.FolderPath;
        var selectedPath = selectFirst ? null : SelectedImage?.FilePath;
        var disableLabel = Text("停用", LocalizationKeys.LoadingImagesActionDisable);
        var enableLabel = Text("启用", LocalizationKeys.LoadingImagesActionEnable);

        Images.Clear();
        foreach (var asset in assets)
        {
            if (!string.IsNullOrWhiteSpace(folder) && !string.Equals(asset.Folder, folder, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            Images.Add(new LoadingImageRowViewModel(WithProvider(asset, providers), ToggleImageAsync, disableLabel, enableLabel));
        }

        _lastFilterFolder = folder;
        SelectedImage = selectedPath is null
            ? Images.FirstOrDefault()
            : Images.FirstOrDefault(row => string.Equals(row.FilePath, selectedPath, StringComparison.OrdinalIgnoreCase)) ?? Images.FirstOrDefault();
        UpdateStatus();
        NotifyEmptyState();
    }

    private void UpdateStatus()
    {
        var enabled = Images.Count(row => row.IsEnabled);
        StatusText = Text(
            "共 {0} 张 · 已启用 {1} 张",
            LocalizationKeys.LoadingImagesCountEnabled,
            Images.Count,
            enabled);
    }
}
