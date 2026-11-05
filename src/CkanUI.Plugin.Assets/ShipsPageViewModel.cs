using System.Collections.ObjectModel;
using CkanUI.Localization;
using CkanUI.Services.Abstractions;
using CkanUI.Services.Abstractions.Assets;
using CkanUI.Services.Abstractions.Dtos;
using CkanUI.ViewModels.Pages.Instances;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace CkanUI.Plugin.Assets;

/// <summary>飞船页：管理根目录与各存档的 <c>Ships/VAB</c>、<c>Ships/SPH</c>、<c>Ships/Subassemblies</c>。</summary>
/// <remarks>
/// 载具库有两级：根目录 <c>&lt;gameDir&gt;/Ships</c>（所有存档共用）与每个存档自己的
/// <c>&lt;gameDir&gt;/saves/&lt;存档&gt;/Ships</c>。页面顶部下拉框选库，复制 / 搬运在两个库之间搬载具。
/// </remarks>
public sealed partial class ShipsPageViewModel : AssetPageViewModel, IPluginPage, IPageThumbnailWarmup, ITextPromptSink
{
    /// <summary>载具库根目录名（磁盘约定：<c>Ships</c>，根库与存档库同形）。</summary>
    private const string ShipsFolderName = "Ships";

    private static readonly string[] CraftExtensions = [".craft"];

    private readonly ICraftLibraryService _crafts;

    private readonly ISaveFileService _saves;

    // 上一次扫描的结果：资产记录是 record，值相等就说明磁盘没变，不必重建列表（重建会让容器整片重排）。
    private IReadOnlyList<CraftAsset>? _lastAssets;

    // 上一次扫描的库：换库时资产可能恰好同值，必须靠它区分。
    private string? _lastShipsRoot;

    private bool _subscribed;

    // 重建库列表过程中会改 SelectedLibrary，不能让它反过来触发一轮扫描。
    private bool _suppressLibraryRefresh;

    /// <summary>构造页面。</summary>
    /// <param name="instances">实例服务。</param>
    /// <param name="crafts">载具库服务。</param>
    /// <param name="saves">存档服务（用来枚举各存档的载具库）。</param>
    /// <param name="errors">错误上报口。</param>
    /// <param name="dialogs">对话服务。</param>
    public ShipsPageViewModel(
        IInstanceService instances,
        ICraftLibraryService crafts,
        ISaveFileService saves,
        IErrorReporter errors,
        IDialogService dialogs)
        : base(instances, errors, dialogs)
    {
        _crafts = crafts ?? throw new ArgumentNullException(nameof(crafts));
        _saves = saves ?? throw new ArgumentNullException(nameof(saves));
    }

    /// <summary>文件选择通道，由视图注入。</summary>
    public IAssetFilePicker? FilePicker { get; set; }

    /// <summary>文本输入通道，由视图注入（重命名 / 复制用）。</summary>
    public IInstanceTextPrompt? TextPrompt { get; set; }

    /// <summary>当前库里的全部载具。</summary>
    public ObservableCollection<CraftRowViewModel> Crafts { get; } = [];

    /// <summary>可选的载具库：根目录在前，其余按存档文件夹名排序。</summary>
    public ObservableCollection<CraftLibraryOption> Libraries { get; } = [];

    /// <summary>当前正在浏览的载具库。</summary>
    [ObservableProperty]
    private CraftLibraryOption? _selectedLibrary;

    /// <inheritdoc />
    public void WarmUpThumbnails() =>
        PrewarmWhenLoaded(Crafts, static rows => CkanUI.Ui.Converters.PathToThumbnailConverter.Prewarm(
            rows.Select(static row => row.ThumbnailPath), 96));

    /// <summary>当前选中的载具。</summary>
    [ObservableProperty]
    private CraftRowViewModel? _selectedCraft;

    /// <inheritdoc />
    public override string Title => Text("飞船", LocalizationKeys.ShipsTitle);

    /// <inheritdoc />
    public override bool ShowEmptyState => !IsBusy && Crafts.Count == 0;

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

    /// <summary>刷新列表。</summary>
    [RelayCommand]
    private Task RefreshAsync() => RefreshCoreAsync();

    /// <summary>导入一个 .craft 到当前库。</summary>
    [RelayCommand]
    private async Task ImportAsync()
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
            var file = await FilePicker.PickFileAsync(Text("导入载具", LocalizationKeys.ShipsImportTitle), CraftExtensions).ConfigureAwait(true);
            if (string.IsNullOrWhiteSpace(file))
            {
                return;
            }

            var fileName = Path.GetFileNameWithoutExtension(file);
            var options = new[] { KindLabel(CraftKind.Vab), KindLabel(CraftKind.Sph), KindLabel(CraftKind.Subassembly) };
            var index = await Dialogs.SelectAsync(
                Text("导入载具", LocalizationKeys.ShipsImportTitle),
                Text("把「{0}」放进哪个分类？", LocalizationKeys.ShipsImportKind, fileName),
                options,
                0).ConfigureAwait(true);
            if (index < 0)
            {
                return;
            }

            var kind = index switch
            {
                1 => CraftKind.Sph,
                2 => CraftKind.Subassembly,
                _ => CraftKind.Vab,
            };

            var shipsRoot = CurrentShipsRoot(gameDir);
            var result = await RunBusyAsync(
                Text("正在导入…", LocalizationKeys.AssetsBusyImport),
                () => _crafts.ImportToLibraryAsync(shipsRoot, file, kind),
                Text("导入失败。", LocalizationKeys.AssetsErrorImport)).ConfigureAwait(true);
            if (result is not { IsSuccess: true, Value: { } asset })
            {
                return;
            }

            await RefreshCoreAsync().ConfigureAwait(true);
            SelectedCraft = Crafts.FirstOrDefault(row => string.Equals(row.FilePath, asset.FilePath, StringComparison.OrdinalIgnoreCase));
            StatusText = Text("已导入「{0}」", LocalizationKeys.ShipsImportDone, asset.Name);
        }
        catch (Exception ex)
        {
            Errors.Report(ex, "飞船页", "导入载具失败");
            SetError(Text("导入失败。", LocalizationKeys.AssetsErrorImport), ex.Message);
        }
    }

    /// <summary>重命名选中的载具。</summary>
    [RelayCommand(CanExecute = nameof(HasSelection))]
    private async Task RenameAsync()
    {
        var row = SelectedCraft;
        var gameDir = CurrentGameDir();
        if (row is null || gameDir is null)
        {
            return;
        }

        var name = await PromptAsync(Text("重命名", LocalizationKeys.AssetsRenameTitle), row.Name).ConfigureAwait(true);
        if (string.IsNullOrWhiteSpace(name) || string.Equals(name.Trim(), row.Name, StringComparison.Ordinal))
        {
            return;
        }

        var result = await RunBusyAsync(
            Text("处理中…", LocalizationKeys.AssetsBusyWorking),
            () => _crafts.RenameAsync(gameDir, row.Asset, name.Trim()),
            Text("重命名失败。", LocalizationKeys.AssetsErrorRename)).ConfigureAwait(true);
        if (result is not { IsSuccess: true, Value: { } renamed })
        {
            return;
        }

        await RefreshCoreAsync().ConfigureAwait(true);
        SelectedCraft = Crafts.FirstOrDefault(item => string.Equals(item.FilePath, renamed.FilePath, StringComparison.OrdinalIgnoreCase));
        StatusText = Text("已重命名为「{0}」", LocalizationKeys.ShipsRenameDone, renamed.Name);
    }

    /// <summary>在当前库里复制选中的载具。</summary>
    [RelayCommand(CanExecute = nameof(HasSelection))]
    private async Task DuplicateAsync()
    {
        var row = SelectedCraft;
        var gameDir = CurrentGameDir();
        if (row is null || gameDir is null)
        {
            return;
        }

        var name = await PromptAsync(Text("复制载具", LocalizationKeys.ShipsDuplicateTitle), row.Name + " copy").ConfigureAwait(true);
        if (string.IsNullOrWhiteSpace(name))
        {
            return;
        }

        var result = await RunBusyAsync(
            Text("处理中…", LocalizationKeys.AssetsBusyWorking),
            () => _crafts.DuplicateAsync(gameDir, row.Asset, name.Trim()),
            Text("操作失败。", LocalizationKeys.AssetsErrorOperationFailed)).ConfigureAwait(true);
        if (result is not { IsSuccess: true, Value: { } copy })
        {
            return;
        }

        await RefreshCoreAsync().ConfigureAwait(true);
        SelectedCraft = Crafts.FirstOrDefault(item => string.Equals(item.FilePath, copy.FilePath, StringComparison.OrdinalIgnoreCase));
        StatusText = Text("已复制为「{0}」", LocalizationKeys.ShipsDuplicateDone, copy.Name);
    }

    /// <summary>把选中的载具复制到另一个载具库。</summary>
    [RelayCommand(CanExecute = nameof(HasSelection))]
    private Task CopyToLibraryAsync() => TransferAsync(move: false);

    /// <summary>把选中的载具搬运到另一个载具库（源文件消失）。</summary>
    [RelayCommand(CanExecute = nameof(HasSelection))]
    private Task MoveToLibraryAsync() => TransferAsync(move: true);

    /// <summary>删除选中的载具。</summary>
    [RelayCommand(CanExecute = nameof(HasSelection))]
    private async Task DeleteAsync()
    {
        var row = SelectedCraft;
        var gameDir = CurrentGameDir();
        if (row is null || gameDir is null)
        {
            return;
        }

        var confirmed = await Dialogs.ConfirmAsync(
            Text("删除确认", LocalizationKeys.AssetsDeleteTitle),
            Text("删除载具「{0}」？同名缩略图与版本信息会一起删除，无法撤销。", LocalizationKeys.ShipsDeleteMessage, row.Name)).ConfigureAwait(true);
        if (!confirmed)
        {
            return;
        }

        var result = await RunBusyAsync(
            Text("处理中…", LocalizationKeys.AssetsBusyWorking),
            () => _crafts.DeleteAsync(gameDir, row.Asset),
            Text("删除失败。", LocalizationKeys.AssetsErrorDelete)).ConfigureAwait(true);
        if (result is not { IsSuccess: true })
        {
            return;
        }

        await RefreshCoreAsync().ConfigureAwait(true);
        StatusText = Text("已删除「{0}」", LocalizationKeys.ShipsDeleteDone, row.Name);
    }

    /// <summary>在系统文件管理器里打开选中载具所在目录。</summary>
    [RelayCommand(CanExecute = nameof(HasSelection))]
    private void OpenFolder()
    {
        if (SelectedCraft is { } row)
        {
            Reveal(row.FolderPath);
        }
    }

    private bool HasSelection() => SelectedCraft is not null;

    partial void OnSelectedCraftChanged(CraftRowViewModel? value)
    {
        RenameCommand.NotifyCanExecuteChanged();
        DuplicateCommand.NotifyCanExecuteChanged();
        CopyToLibraryCommand.NotifyCanExecuteChanged();
        MoveToLibraryCommand.NotifyCanExecuteChanged();
        DeleteCommand.NotifyCanExecuteChanged();
        OpenFolderCommand.NotifyCanExecuteChanged();
    }

    partial void OnSelectedLibraryChanged(CraftLibraryOption? value)
    {
        if (_suppressLibraryRefresh)
        {
            return;
        }

        _ = RefreshCoreAsync();
    }

    private void OnInstanceChanged(InstanceInfo? instance) => _ = RefreshCoreAsync();

    private string CurrentShipsRoot(string gameDir) =>
        SelectedLibrary?.ShipsRoot ?? Path.Combine(gameDir, ShipsFolderName);

    private async Task RefreshCoreAsync()
    {
        if (IsBusy)
        {
            return;
        }

        var gameDir = CurrentGameDir();
        if (gameDir is null)
        {
            Crafts.Clear();
            Libraries.Clear();
            _lastAssets = null;
            _lastShipsRoot = null;
            NotifyEmptyState();
            return;
        }

        try
        {
            await ReloadLibrariesAsync(gameDir).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            // 枚举存档失败不该让飞船列表整个不可用：退回根库继续。
            Errors.Report(ex, "飞船页", "读取存档列表失败");
        }

        var shipsRoot = CurrentShipsRoot(gameDir);
        var result = await RunBusyAsync(
            Text("正在扫描载具…", LocalizationKeys.ShipsBusyScanning),
            () => _crafts.ScanLibraryAsync(shipsRoot),
            Text("刷新载具列表失败。", LocalizationKeys.ShipsErrorScan)).ConfigureAwait(true);
        if (result is not { IsSuccess: true, Value: { } items })
        {
            NotifyEmptyState();
            return;
        }

        if (_lastAssets is not null && _lastAssets.Count == items.Count && _lastAssets.SequenceEqual(items)
            && Crafts.Count == items.Count
            && string.Equals(_lastShipsRoot, shipsRoot, StringComparison.OrdinalIgnoreCase))
        {
            // 磁盘没变化：保留已实例化的行容器与选中项，切回本页不再重排列表。
            NotifyEmptyState();
            return;
        }

        _lastAssets = items.ToArray();
        _lastShipsRoot = shipsRoot;
        var selectedPath = SelectedCraft?.FilePath;
        Crafts.Clear();
        foreach (var asset in items)
        {
            Crafts.Add(new CraftRowViewModel(asset, KindLabel(asset.Kind)));
        }

        SelectedCraft = selectedPath is null
            ? Crafts.FirstOrDefault()
            : Crafts.FirstOrDefault(row => string.Equals(row.FilePath, selectedPath, StringComparison.OrdinalIgnoreCase)) ?? Crafts.FirstOrDefault();
        StatusText = Text("共 {0} 个载具", LocalizationKeys.ShipsCount, Crafts.Count);
        NotifyEmptyState();
    }

    /// <summary>重建载具库下拉框：根目录 + 每个出现过的存档文件夹。</summary>
    private async Task ReloadLibrariesAsync(string gameDir)
    {
        var desired = new List<CraftLibraryOption>
        {
            new(Text("根目录（所有存档共用）", LocalizationKeys.ShipsLibraryRoot), Path.Combine(gameDir, ShipsFolderName), isRoot: true),
        };

        var saves = await _saves.ListSavesAsync(gameDir).ConfigureAwait(true);
        if (saves is { IsSuccess: true, Value: { } items })
        {
            var savesDir = _saves.GetSavesDirectory(gameDir);
            var folders = items
                .Select(static item => Path.GetFileName(Path.GetDirectoryName(item.FilePath)))
                .Where(static folder => !string.IsNullOrWhiteSpace(folder))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(static folder => folder, StringComparer.OrdinalIgnoreCase);
            foreach (var folder in folders)
            {
                desired.Add(new CraftLibraryOption(
                    folder!,
                    Path.Combine(savesDir, folder!, ShipsFolderName),
                    isRoot: false));
            }
        }
        else if (!saves.IsSuccess && !string.IsNullOrWhiteSpace(saves.Message))
        {
            // 存档列表读失败：只影响下拉框里的存档库，根库照常可用。
            SetError(saves.Message, saves.Detail);
        }

        if (Libraries.Count == desired.Count && Libraries.Zip(desired).All(static pair =>
                string.Equals(pair.First.ShipsRoot, pair.Second.ShipsRoot, StringComparison.OrdinalIgnoreCase)
                && string.Equals(pair.First.DisplayName, pair.Second.DisplayName, StringComparison.Ordinal)))
        {
            return;
        }

        var previous = SelectedLibrary;
        _suppressLibraryRefresh = true;
        try
        {
            Libraries.Clear();
            foreach (var option in desired)
            {
                Libraries.Add(option);
            }

            SelectedLibrary = previous is null
                ? Libraries[0]
                : Libraries.FirstOrDefault(option => string.Equals(option.ShipsRoot, previous.ShipsRoot, StringComparison.OrdinalIgnoreCase)) ?? Libraries[0];
        }
        finally
        {
            _suppressLibraryRefresh = false;
        }
    }

    /// <summary>把一个载具复制 / 搬运到别的库。</summary>
    private async Task TransferAsync(bool move)
    {
        var row = SelectedCraft;
        if (row is null)
        {
            return;
        }

        var current = SelectedLibrary?.ShipsRoot;
        var targets = Libraries
            .Where(option => !string.Equals(option.ShipsRoot, current, StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (targets.Count == 0)
        {
            SetError(Text("没有别的载具库可以放。", LocalizationKeys.ShipsTransferNoTarget));
            return;
        }

        var index = await Dialogs.SelectAsync(
            Text("搬运载具", LocalizationKeys.ShipsTransferTitle),
            Text("把「{0}」放到哪个库？", LocalizationKeys.ShipsTransferTarget, row.Name),
            targets.Select(static option => option.DisplayName).ToArray(),
            0).ConfigureAwait(true);
        if (index < 0 || index >= targets.Count)
        {
            return;
        }

        var target = targets[index];
        var result = await RunBusyAsync(
            Text("处理中…", LocalizationKeys.AssetsBusyWorking),
            () => _crafts.TransferAsync(row.Asset, target.ShipsRoot, row.Asset.Kind, move),
            Text("搬运载具失败。", LocalizationKeys.ShipsTransferFailed)).ConfigureAwait(true);
        if (result is not { IsSuccess: true, Value: { } moved })
        {
            return;
        }

        await RefreshCoreAsync().ConfigureAwait(true);
        StatusText = move
            ? Text("已移动「{0}」到「{1}」", LocalizationKeys.ShipsTransferMoveDone, moved.Name, target.DisplayName)
            : Text("已复制「{0}」到「{1}」", LocalizationKeys.ShipsTransferCopyDone, moved.Name, target.DisplayName);
    }

    private string KindLabel(CraftKind kind) => kind switch
    {
        CraftKind.Sph => Text("飞机", LocalizationKeys.ShipsKindSph),
        CraftKind.Subassembly => Text("子装配", LocalizationKeys.ShipsKindSubassembly),
        _ => Text("火箭", LocalizationKeys.ShipsKindVab),
    };

    private async Task<string?> PromptAsync(string title, string initialValue)
    {
        if (TextPrompt is null)
        {
            SetError(Text("当前环境无法输入文本。", LocalizationKeys.AssetsErrorNoPrompt));
            return null;
        }

        try
        {
            return await TextPrompt.PromptAsync(
                title,
                Text("输入新的名字：", LocalizationKeys.AssetsRenameMessage),
                initialValue,
                string.Empty,
                CancellationToken.None).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            Errors.Report(ex, "飞船页", "输入文本失败");
            SetError(Text("操作失败。", LocalizationKeys.AssetsErrorOperationFailed), ex.Message);
            return null;
        }
    }
}
