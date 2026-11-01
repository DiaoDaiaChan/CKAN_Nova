using System.Collections.ObjectModel;
using CkanUI.Localization;
using CkanUI.Services.Abstractions;
using CkanUI.Services.Abstractions.Assets;
using CkanUI.Services.Abstractions.Dtos;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace CkanUI.ViewModels.Pages.Assets;

/// <summary>加载图页：维护目录清单，管理目录里的加载图。</summary>
/// <remarks>原版加载图内嵌在游戏资源里，所以这里管的是「哪些目录算加载图来源」+ 目录里的图片。</remarks>
public sealed partial class LoadingImagesPageViewModel : AssetPageViewModel
{
    private static readonly string[] ImageExtensions = [".png", ".jpg", ".jpeg"];

    private readonly ILoadingImageService _images;

    // 上一次扫描到的资产：值不变就不重建卡片列表。
    private IReadOnlyList<LoadingImageAsset>? _lastAssets;

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
    public ObservableCollection<string> Folders { get; } = [];

    /// <summary>全部加载图。</summary>
    public ObservableCollection<LoadingImageRowViewModel> Images { get; } = [];

    /// <summary>当前选中的目录（导入的落点）。</summary>
    [ObservableProperty]
    private string? _selectedFolder;

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
                SelectedFolder).ConfigureAwait(true);
            if (string.IsNullOrWhiteSpace(folder))
            {
                return;
            }

            if (!Folders.Contains(folder, StringComparer.OrdinalIgnoreCase))
            {
                Folders.Add(folder);
                var saved = SaveFolders(gameDir);
                if (!saved.IsSuccess)
                {
                    return;
                }
            }

            SelectedFolder = folder;
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
        if (gameDir is null || string.IsNullOrWhiteSpace(folder))
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

        var target = SelectedFolder;
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

    private void OnInstanceChanged(InstanceInfo? instance) => _ = RefreshCoreAsync();

    private OperationResult SaveFolders(string gameDir)
    {
        var result = _images.SaveFolders(gameDir, Folders.ToList());
        if (!result.IsSuccess)
        {
            SetError(result.Message ?? Text("保存目录清单失败。", LocalizationKeys.LoadingImagesErrorSaveFolders), result.Detail);
        }

        return result;
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

        var keepFolder = SelectedFolder;
        Folders.Clear();
        foreach (var folder in folders)
        {
            Folders.Add(folder);
        }

        SelectedFolder = keepFolder is not null && Folders.Contains(keepFolder, StringComparer.OrdinalIgnoreCase)
            ? keepFolder
            : Folders.FirstOrDefault();

        var result = await RunBusyAsync(
            Text("正在扫描加载图…", LocalizationKeys.LoadingImagesBusyScanning),
            () => _images.ScanAsync(folders),
            Text("刷新加载图失败。", LocalizationKeys.LoadingImagesErrorScan)).ConfigureAwait(true);
        if (result is not { IsSuccess: true, Value: { } items })
        {
            NotifyEmptyState();
            return;
        }

        // 磁盘扫描结果没变：保留已实例化的卡片，重排整片 WrapPanel 才是切回本页的卡顿来源。
        if (_lastAssets is not null && _lastAssets.Count == items.Count && _lastAssets.SequenceEqual(items)
            && Images.Count == items.Count)
        {
            NotifyEmptyState();
            return;
        }

        _lastAssets = items.ToArray();
        var selectedPath = SelectedImage?.FilePath;
        Images.Clear();
        foreach (var asset in items)
        {
            Images.Add(new LoadingImageRowViewModel(asset));
        }

        SelectedImage = selectedPath is null
            ? Images.FirstOrDefault()
            : Images.FirstOrDefault(row => string.Equals(row.FilePath, selectedPath, StringComparison.OrdinalIgnoreCase)) ?? Images.FirstOrDefault();
        StatusText = Text("共 {0} 张", LocalizationKeys.LoadingImagesCount, Images.Count);
        NotifyEmptyState();
    }
}
