using System.Collections.ObjectModel;
using CkanUI.Localization;
using CkanUI.Services.Abstractions;
using CkanUI.Services.Abstractions.Assets;
using CkanUI.Services.Abstractions.Dtos;
using CkanUI.ViewModels.Pages.Instances;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace CkanUI.ViewModels.Pages.Assets;

/// <summary>飞船页：管理当前实例的 <c>Ships/VAB</c>、<c>Ships/SPH</c>、<c>Ships/Subassemblies</c>。</summary>
public sealed partial class ShipsPageViewModel : AssetPageViewModel
{
    private static readonly string[] CraftExtensions = [".craft"];

    private readonly ICraftLibraryService _crafts;

    // 上一次扫描的结果：资产记录是 record，值相等就说明磁盘没变，不必重建列表（重建会让容器整片重排）。
    private IReadOnlyList<CraftAsset>? _lastAssets;

    private bool _subscribed;

    /// <summary>构造页面。</summary>
    /// <param name="instances">实例服务。</param>
    /// <param name="crafts">载具库服务。</param>
    /// <param name="errors">错误上报口。</param>
    /// <param name="dialogs">对话服务。</param>
    public ShipsPageViewModel(
        IInstanceService instances,
        ICraftLibraryService crafts,
        IErrorReporter errors,
        IDialogService dialogs)
        : base(instances, errors, dialogs) =>
        _crafts = crafts ?? throw new ArgumentNullException(nameof(crafts));

    /// <summary>文件选择通道，由视图注入。</summary>
    public IAssetFilePicker? FilePicker { get; set; }

    /// <summary>文本输入通道，由视图注入（重命名 / 复制用）。</summary>
    public IInstanceTextPrompt? TextPrompt { get; set; }

    /// <summary>当前实例的全部载具。</summary>
    public ObservableCollection<CraftRowViewModel> Crafts { get; } = [];

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

    /// <summary>导入一个 .craft。</summary>
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

            var result = await RunBusyAsync(
                Text("正在导入…", LocalizationKeys.AssetsBusyImport),
                () => _crafts.ImportAsync(gameDir, file, kind),
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

    /// <summary>复制选中的载具。</summary>
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
        DeleteCommand.NotifyCanExecuteChanged();
        OpenFolderCommand.NotifyCanExecuteChanged();
    }

    private void OnInstanceChanged(InstanceInfo? instance) => _ = RefreshCoreAsync();

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
            NotifyEmptyState();
            return;
        }

        var result = await RunBusyAsync(
            Text("正在扫描载具…", LocalizationKeys.ShipsBusyScanning),
            () => _crafts.ScanAsync(gameDir),
            Text("刷新载具列表失败。", LocalizationKeys.ShipsErrorScan)).ConfigureAwait(true);
        if (result is not { IsSuccess: true, Value: { } items })
        {
            NotifyEmptyState();
            return;
        }

        if (_lastAssets is not null && _lastAssets.Count == items.Count && _lastAssets.SequenceEqual(items)
            && Crafts.Count == items.Count)
        {
            // 磁盘没变化：保留已实例化的行容器与选中项，切回本页不再重排列表。
            NotifyEmptyState();
            return;
        }

        _lastAssets = items.ToArray();
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
