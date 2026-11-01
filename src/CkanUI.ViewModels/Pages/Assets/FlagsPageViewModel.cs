using System.Collections.ObjectModel;
using CkanUI.Localization;
using CkanUI.Services.Abstractions;
using CkanUI.Services.Abstractions.Assets;
using CkanUI.Services.Abstractions.Dtos;
using CkanUI.ViewModels.Pages.Instances;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace CkanUI.ViewModels.Pages.Assets;

/// <summary>旗帜页：GameData 下所有 <c>Flags</c> 目录里的图片，可设默认、导入、改名、删除。</summary>
public sealed partial class FlagsPageViewModel : AssetPageViewModel
{
    private static readonly string[] FlagExtensions = [".png", ".jpg", ".jpeg"];

    private readonly IFlagLibraryService _flags;

    // 上一次扫描到的资产与默认旗帜：值不变就不重建卡片列表。
    private IReadOnlyList<FlagAsset>? _lastAssets;
    private string? _lastDefaultConfig;

    private bool _subscribed;

    /// <summary>构造页面。</summary>
    /// <param name="instances">实例服务。</param>
    /// <param name="flags">旗帜库服务。</param>
    /// <param name="errors">错误上报口。</param>
    /// <param name="dialogs">对话服务。</param>
    public FlagsPageViewModel(
        IInstanceService instances,
        IFlagLibraryService flags,
        IErrorReporter errors,
        IDialogService dialogs)
        : base(instances, errors, dialogs) =>
        _flags = flags ?? throw new ArgumentNullException(nameof(flags));

    /// <summary>文件选择通道，由视图注入。</summary>
    public IAssetFilePicker? FilePicker { get; set; }

    /// <summary>文本输入通道，由视图注入。</summary>
    public IInstanceTextPrompt? TextPrompt { get; set; }

    /// <summary>全部旗帜。</summary>
    public ObservableCollection<FlagRowViewModel> Flags { get; } = [];

    /// <summary>当前选中的旗帜。</summary>
    [ObservableProperty]
    private FlagRowViewModel? _selectedFlag;

    /// <summary>当前默认旗帜的说明文本。</summary>
    [ObservableProperty]
    private string _currentDefaultText = string.Empty;

    /// <inheritdoc />
    public override string Title => Text("旗帜", LocalizationKeys.FlagsTitle);

    /// <inheritdoc />
    public override bool ShowEmptyState => !IsBusy && Flags.Count == 0;

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

    /// <summary>把选中旗帜设为默认。</summary>
    [RelayCommand(CanExecute = nameof(HasSelection))]
    private async Task SetDefaultAsync()
    {
        var row = SelectedFlag;
        var gameDir = CurrentGameDir();
        if (row is null || gameDir is null || row.IsDefault)
        {
            return;
        }

        var result = await RunBusyAsync(
            Text("处理中…", LocalizationKeys.AssetsBusyWorking),
            () => _flags.SetDefaultFlagAsync(gameDir, row.Asset),
            Text("设置默认旗帜失败。", LocalizationKeys.FlagsErrorSetDefault)).ConfigureAwait(true);
        if (result is not { IsSuccess: true })
        {
            return;
        }

        await RefreshCoreAsync().ConfigureAwait(true);
        SelectedFlag = Flags.FirstOrDefault(flag => string.Equals(flag.ConfigPath, row.ConfigPath, StringComparison.OrdinalIgnoreCase));
        StatusText = Text("已把「{0}」设为默认旗帜", LocalizationKeys.FlagsSetDefaultDone, row.Name);
    }

    /// <summary>导入一张旗帜。</summary>
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
            var file = await FilePicker.PickFileAsync(Text("导入旗帜", LocalizationKeys.FlagsImportTitle), FlagExtensions).ConfigureAwait(true);
            if (string.IsNullOrWhiteSpace(file))
            {
                return;
            }

            var result = await RunBusyAsync(
                Text("正在导入…", LocalizationKeys.AssetsBusyImport),
                () => _flags.ImportAsync(gameDir, file),
                Text("导入失败。", LocalizationKeys.AssetsErrorImport)).ConfigureAwait(true);
            if (result is not { IsSuccess: true, Value: { } flag })
            {
                return;
            }

            await RefreshCoreAsync().ConfigureAwait(true);
            SelectedFlag = Flags.FirstOrDefault(row => string.Equals(row.ConfigPath, flag.ConfigPath, StringComparison.OrdinalIgnoreCase));
            StatusText = Text("已导入「{0}」", LocalizationKeys.FlagsImportDone, flag.Name);
        }
        catch (Exception ex)
        {
            Errors.Report(ex, "旗帜页", "导入旗帜失败");
            SetError(Text("导入失败。", LocalizationKeys.AssetsErrorImport), ex.Message);
        }
    }

    /// <summary>重命名选中旗帜。</summary>
    [RelayCommand(CanExecute = nameof(HasSelection))]
    private async Task RenameAsync()
    {
        var row = SelectedFlag;
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
            () => _flags.RenameAsync(gameDir, row.Asset, name.Trim()),
            Text("重命名失败。", LocalizationKeys.AssetsErrorRename)).ConfigureAwait(true);
        if (result is not { IsSuccess: true, Value: { } renamed })
        {
            return;
        }

        await RefreshCoreAsync().ConfigureAwait(true);
        SelectedFlag = Flags.FirstOrDefault(flag => string.Equals(flag.ConfigPath, renamed.ConfigPath, StringComparison.OrdinalIgnoreCase));
        StatusText = Text("已重命名为「{0}」", LocalizationKeys.FlagsRenameDone, renamed.Name);
    }

    /// <summary>删除选中旗帜。</summary>
    [RelayCommand(CanExecute = nameof(HasSelection))]
    private async Task DeleteAsync()
    {
        var row = SelectedFlag;
        var gameDir = CurrentGameDir();
        if (row is null || gameDir is null)
        {
            return;
        }

        var confirmed = await Dialogs.ConfirmAsync(
            Text("删除确认", LocalizationKeys.AssetsDeleteTitle),
            Text("删除旗帜「{0}」？", LocalizationKeys.FlagsDeleteMessage, row.Name)).ConfigureAwait(true);
        if (!confirmed)
        {
            return;
        }

        var result = await RunBusyAsync(
            Text("处理中…", LocalizationKeys.AssetsBusyWorking),
            () => _flags.DeleteAsync(gameDir, row.Asset),
            Text("删除失败。", LocalizationKeys.AssetsErrorDelete)).ConfigureAwait(true);
        if (result is not { IsSuccess: true })
        {
            return;
        }

        await RefreshCoreAsync().ConfigureAwait(true);
        StatusText = Text("已删除「{0}」", LocalizationKeys.FlagsDeleteDone, row.Name);
    }

    /// <summary>在系统文件管理器里打开选中旗帜所在目录。</summary>
    [RelayCommand(CanExecute = nameof(HasSelection))]
    private void OpenFolder()
    {
        if (SelectedFlag is { } row)
        {
            Reveal(Path.GetDirectoryName(row.FilePath) ?? row.FilePath);
        }
    }

    private bool HasSelection() => SelectedFlag is not null;

    partial void OnSelectedFlagChanged(FlagRowViewModel? value)
    {
        SetDefaultCommand.NotifyCanExecuteChanged();
        RenameCommand.NotifyCanExecuteChanged();
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
            Flags.Clear();
            CurrentDefaultText = string.Empty;
            NotifyEmptyState();
            return;
        }

        var result = await RunBusyAsync(
            Text("正在扫描旗帜…", LocalizationKeys.FlagsBusyScanning),
            () => _flags.ScanAsync(gameDir),
            Text("刷新旗帜列表失败。", LocalizationKeys.FlagsErrorScan)).ConfigureAwait(true);
        if (result is not { IsSuccess: true, Value: { } items })
        {
            NotifyEmptyState();
            return;
        }

        var current = _flags.ReadDefaultFlag(gameDir);
        var currentConfig = current.IsSuccess ? current.Value : null;
        // 磁盘与默认旗帜都没变（FlagAsset 是 record，值相等即可判定）：不必重建卡片，重排整片 WrapPanel 才是卡顿来源。
        var unchanged = _lastAssets is not null
            && _lastAssets.Count == items.Count
            && _lastAssets.SequenceEqual(items)
            && Flags.Count == items.Count
            && string.Equals(_lastDefaultConfig, currentConfig, StringComparison.OrdinalIgnoreCase);
        _lastAssets = items.ToArray();
        _lastDefaultConfig = currentConfig;
        if (unchanged)
        {
            NotifyEmptyState();
            return;
        }

        var selectedPath = SelectedFlag?.ConfigPath;

        Flags.Clear();
        foreach (var asset in items)
        {
            Flags.Add(new FlagRowViewModel(
                asset,
                currentConfig is not null && string.Equals(asset.ConfigPath, currentConfig, StringComparison.OrdinalIgnoreCase)));
        }

        CurrentDefaultText = string.IsNullOrWhiteSpace(currentConfig)
            ? Text("当前默认：未设置", LocalizationKeys.FlagsDefaultNone)
            : Text("当前默认：{0}", LocalizationKeys.FlagsDefaultCurrent, currentConfig);
        SelectedFlag = selectedPath is null
            ? Flags.FirstOrDefault()
            : Flags.FirstOrDefault(flag => string.Equals(flag.ConfigPath, selectedPath, StringComparison.OrdinalIgnoreCase)) ?? Flags.FirstOrDefault();
        StatusText = Text("共 {0} 张旗帜", LocalizationKeys.FlagsCount, Flags.Count);
        NotifyEmptyState();
    }

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
            Errors.Report(ex, "旗帜页", "输入文本失败");
            SetError(Text("操作失败。", LocalizationKeys.AssetsErrorOperationFailed), ex.Message);
            return null;
        }
    }
}
