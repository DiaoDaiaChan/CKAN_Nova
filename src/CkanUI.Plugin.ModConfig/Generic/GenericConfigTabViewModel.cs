using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CkanUI.Services.Abstractions.ModConfig;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace CkanUI.Plugin.ModConfig;

/// <summary>
/// 「Mod 配置」页的通用面板：不需要 adapter 包，直接读写 mod 自带的 cfg。
/// </summary>
/// <remarks>
/// 与适配器面板的分工：适配器面板回答「这个 mod 暴露了哪些**语义**配置项」（需要 adapter 描述类型、范围、风险）；
/// 这里回答「这个 mod 的 cfg 里有哪些键值」。后者不需要任何 adapter，代价是只有键与值，没有类型与解释。
/// 本类不写盘：草稿按行号收集后交给 <see cref="IGenericConfigEditService"/>（备份 + 原子替换 + 运行中拦截都在服务层）。
/// </remarks>
public sealed partial class GenericConfigTabViewModel : ObservableObject
{
    private readonly IGenericConfigEditService? _service;
    private readonly Func<string, string>? _translate;

    private string _gameDir = string.Empty;
    private string _instanceName = string.Empty;

    public GenericConfigTabViewModel(IGenericConfigEditService? service, Func<string, string>? translate = null)
    {
        _service = service;
        _translate = translate;
    }

    // ---- 状态 --------------------------------------------------------------

    /// <summary>扫到的候选 cfg（按 mod 分组前的平铺列表）。</summary>
    [ObservableProperty]
    private IReadOnlyList<GenericConfigFileCardViewModel> _files = [];

    /// <summary>当前选中的 cfg。</summary>
    [ObservableProperty]
    private GenericConfigFileCardViewModel? _selectedFile;

    /// <summary>选中 cfg 的键值行。</summary>
    [ObservableProperty]
    private IReadOnlyList<GenericConfigRowViewModel> _rows = [];

    /// <summary>文件筛选框。</summary>
    [ObservableProperty]
    private string _searchText = string.Empty;

    /// <summary>正在读盘/写盘。</summary>
    [ObservableProperty]
    private bool _isBusy;

    /// <summary>状态栏文案。</summary>
    [ObservableProperty]
    private string _statusText = string.Empty;

    /// <summary>错误文案。</summary>
    [ObservableProperty]
    private string? _errorText;

    /// <summary>当前文件能不能写（文件只读/解析错误时为 false）。</summary>
    [ObservableProperty]
    private bool _canWrite;

    /// <summary>不可写原因（人话）。</summary>
    [ObservableProperty]
    private string? _readonlyReason;

    /// <summary>是否配了服务（没配就只显示空态，不假装能读）。</summary>
    public bool IsServiceAvailable => _service is not null;

    /// <summary>扫到过文件没有（空态判定）。</summary>
    public bool HasFiles => Files.Count > 0;

    /// <summary>有选中文件。</summary>
    public bool HasSelection => SelectedFile is not null;

    /// <summary>是否有选中文件（供 XAML 绑定）。</summary>
    public bool HasSelectedFile => HasSelection;

    /// <summary>文件是否只读（供 XAML 绑定）。</summary>
    public bool IsReadonly => !CanWrite;

    /// <summary>是否有未保存改动（供 XAML 绑定与未保存检查）。</summary>
    public bool HasUnsavedChanges => IsDirty;

    /// <summary>选中文件有行。</summary>
    public bool HasRows => Rows.Count > 0;

    /// <summary>有未保存改动。</summary>
    public bool IsDirty => PendingChangeCount > 0;

    /// <summary>未保存改动条数。</summary>
    public int PendingChangeCount => Rows.Count(row => row.IsModified);

    /// <summary>是否有不可写原因要显示。</summary>
    public bool HasReadonlyReason => !string.IsNullOrWhiteSpace(ReadonlyReason);

    /// <summary>把当前文件按行改写一遍（没有改动就禁用）。</summary>
    public bool CanSave => !IsBusy && CanWrite && IsDirty && IsServiceAvailable;

    /// <summary>撤销未保存改动。</summary>
    public bool CanRevert => !IsBusy && IsDirty;

    /// <summary>按搜索词过滤后的文件列表。</summary>
    public IReadOnlyList<GenericConfigFileCardViewModel> FilteredFiles
    {
        get
        {
            if (string.IsNullOrWhiteSpace(SearchText))
            {
                return Files;
            }

            var query = SearchText.Trim();
            return Files
                .Where(f => f.ModName.Contains(query, StringComparison.OrdinalIgnoreCase)
                    || f.Subtitle.Contains(query, StringComparison.OrdinalIgnoreCase)
                    || f.Title.Contains(query, StringComparison.OrdinalIgnoreCase))
                .ToArray();
        }
    }

    /// <summary>文件计数文案。</summary>
    public string FileCountText => T($"共 {Files.Count} 个文件", "Ui_ModConfigGeneric_FileCount", Files.Count);

    /// <summary>脏计数文案。</summary>
    public string DirtyText => T($"有 {PendingChangeCount} 处改动未保存", "Ui_ModConfigGeneric_DirtyCount", PendingChangeCount);

    /// <summary>选中文件标题。</summary>
    public string SelectedTitle => SelectedFile?.Title ?? T("选一个配置文件", "Ui_ModConfigGeneric_SelectFileHint");

    /// <summary>选中文件副标题（相对路径 + 项数）。</summary>
    public string SelectedSubtitle => SelectedFile is null
        ? string.Empty
        : SelectedFile.Subtitle + " · " + T($"{Rows.Count} 项", "Ui_ModConfigGeneric_PairCount", Rows.Count);

    /// <summary>空态标题（一处文案两处用：没服务、没文件、没选中）。</summary>
    public string EmptyTitle
    {
        get
        {
            if (!IsServiceAvailable)
            {
                return T("通用配置读写不可用", "Ui_ModConfigGeneric_UnavailableTitle");
            }

            return HasFiles
                ? T("选一个配置文件", "Ui_ModConfigGeneric_SelectFileHint")
                : T("没有发现可读的 cfg", "Ui_ModConfigGeneric_EmptyTitle");
        }
    }

    /// <summary>空态说明。</summary>
    public string EmptyDetail
    {
        get
        {
            if (!IsServiceAvailable)
            {
                return T("宿主没有注册通用配置服务，这一页只能看适配器面板。", "Ui_ModConfigGeneric_UnavailableDetail");
            }

            return HasFiles
                ? T("左侧列出的是 GameData 下的 PluginData 与 settings 一类文件；原版配置与零件定义不在此列。", "Ui_ModConfigGeneric_SelectFileDetail")
                : T("扫的是 GameData/journal 里的 PluginData 目录与 settings.cfg、config.cfg 一类文件。没有实例或实例没选好时这里会是空的。", "Ui_ModConfigGeneric_EmptyDetail");
        }
    }

    // ---- 输入：谁来决定读哪个实例 ------------------------------------------

    /// <summary>换实例/换游戏目录时整体重读（同目录不重复扫）。</summary>
    public void SetContext(string? gameDir, string? instanceName)
    {
        var next = gameDir?.Trim() ?? string.Empty;
        var same = string.Equals(next, _gameDir, StringComparison.OrdinalIgnoreCase)
            && string.Equals(instanceName ?? string.Empty, _instanceName, StringComparison.Ordinal);

        _gameDir = next;
        _instanceName = instanceName ?? string.Empty;
        if (same)
        {
            return;
        }

        // 换了实例：旧文件列表与草稿都作废（草稿按行号定位，跨实例毫无意义）。
        SelectedFile = null;
        Rows = [];
        Files = [];
        ErrorText = null;
        StatusText = string.Empty;
        NotifyAll();
    }

    /// <summary>扫描并选中第一个文件（进页面/切到通用面板时调用）。</summary>
    public async Task RefreshAsync(bool selectFirst = true, CancellationToken cancellationToken = default)
    {
        if (_service is null || string.IsNullOrWhiteSpace(_gameDir))
        {
            NotifyAll();
            return;
        }

        IsBusy = true;
        ErrorText = null;
        try
        {
            var discovered = await _service
                .DiscoverAsync(_gameDir, _instanceName, cancellationToken)
                .ConfigureAwait(true);

            if (!discovered.IsSuccess || discovered.Value is null)
            {
                Files = [];
                SelectedFile = null;
                Rows = [];
                ErrorText = discovered.Message ?? T("扫描失败。", "Ui_ModConfigGeneric_ScanFailed");
                return;
            }

            var cards = discovered.Value.Files
                .Select(file => new GenericConfigFileCardViewModel(file))
                .OrderBy(card => card.ModName, StringComparer.OrdinalIgnoreCase)
                .ThenBy(card => card.Subtitle, StringComparer.OrdinalIgnoreCase)
                .ToArray();

            Files = cards;
            StatusText = discovered.Value.Diagnostics.Count > 0
                ? string.Join("；", discovered.Value.Diagnostics)
                : string.Empty;

            var preferred = SelectedFile is null
                ? null
                : cards.FirstOrDefault(card => string.Equals(card.FilePath, SelectedFile.FilePath, StringComparison.OrdinalIgnoreCase));

            if (preferred is not null)
            {
                SelectedFile = preferred;
            }
            else if (selectFirst && cards.Length > 0)
            {
                SelectedFile = cards[0];
            }
            else
            {
                SelectedFile = null;
            }
        }
        catch (OperationCanceledException)
        {
            ErrorText = T("扫描已取消。", "Ui_ModConfigGeneric_ScanCancelled");
        }
        finally
        {
            IsBusy = false;
            NotifyAll();
        }
    }

    /// <summary>读一个文件的行。</summary>
    public async Task LoadSelectedAsync(CancellationToken cancellationToken = default)
    {
        if (_service is null || SelectedFile is null)
        {
            Rows = [];
            CanWrite = false;
            ReadonlyReason = null;
            NotifyAll();
            return;
        }

        IsBusy = true;
        ErrorText = null;
        try
        {
            var loaded = await _service
                .LoadAsync(_gameDir, SelectedFile.FilePath, cancellationToken)
                .ConfigureAwait(true);

            if (!loaded.IsSuccess || loaded.Value is null)
            {
                Rows = [];
                CanWrite = false;
                ReadonlyReason = null;
                ErrorText = loaded.Message ?? T("读取失败。", "Ui_ModConfigGeneric_LoadFailed");
                return;
            }

            CanWrite = loaded.Value.CanWrite;
            ReadonlyReason = loaded.Value.ReadonlyReason;

            var rows = new List<GenericConfigRowViewModel>(loaded.Value.Rows.Count);
            var currentNode = string.Empty;
            for (var index = 0; index < loaded.Value.Rows.Count; index++)
            {
                var row = loaded.Value.Rows[index];
                var isBoundary = index == 0 || !string.Equals(row.NodePath, currentNode, StringComparison.Ordinal);
                currentNode = row.NodePath;
                var vm = new GenericConfigRowViewModel(row, loaded.Value.CanWrite) { IsBoundary = isBoundary };
                vm.PropertyChanged += OnRowPropertyChanged;
                rows.Add(vm);
            }

            Rows = rows;
        }
        catch (OperationCanceledException)
        {
            ErrorText = T("读取已取消。", "Ui_ModConfigGeneric_LoadCancelled");
        }
        finally
        {
            IsBusy = false;
            NotifyAll();
        }
    }

    // ---- 动作 --------------------------------------------------------------

    [RelayCommand]
    private async Task RefreshFilesAsync() => await RefreshAsync(selectFirst: false).ConfigureAwait(true);

    [RelayCommand]
    private async Task ReloadFileAsync() => await LoadSelectedAsync().ConfigureAwait(true);

    [RelayCommand]
    private async Task SaveAsync()
    {
        if (_service is null || SelectedFile is null || !CanSave)
        {
            return;
        }

        var changes = Rows
            .Where(row => row.IsModified)
            .Select(row => new GenericConfigChange(row.LineNumber, row.CurrentValue))
            .ToArray();

        IsBusy = true;
        ErrorText = null;
        try
        {
            var saved = await _service
                .SaveAsync(_gameDir, _instanceName, SelectedFile.FilePath, changes, confirmedGameRunning: false)
                .ConfigureAwait(true);

            if (!saved.IsSuccess || saved.Value is null)
            {
                ErrorText = saved.Message ?? T("保存失败。", "Ui_ModConfigGeneric_SaveFailed");
                return;
            }

            StatusText = saved.Value.BackupPath.Length > 0
                ? T($"已写入 {saved.Value.ChangedLineCount} 行；写前备份：{saved.Value.BackupPath}", "Ui_ModConfigGeneric_Saved", saved.Value.ChangedLineCount, saved.Value.BackupPath)
                : T($"已写入 {saved.Value.ChangedLineCount} 行。", "Ui_ModConfigGeneric_SavedNoBackup", saved.Value.ChangedLineCount);
        }
        finally
        {
            IsBusy = false;
        }

        await LoadSelectedAsync().ConfigureAwait(true);
    }

    [RelayCommand]
    private void Revert()
    {
        foreach (var row in Rows)
        {
            row.PropertyChanged -= OnRowPropertyChanged;
        }

        foreach (var row in Rows)
        {
            row.Revert();
            row.PropertyChanged += OnRowPropertyChanged;
        }

        NotifyAll();
    }

    private void OnRowPropertyChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName is nameof(GenericConfigRowViewModel.IsModified)
            or nameof(GenericConfigRowViewModel.ValueText)
            or nameof(GenericConfigRowViewModel.IsChecked))
        {
            NotifyEditState();
        }
    }

    partial void OnSelectedFileChanged(GenericConfigFileCardViewModel? value)
    {
        // 切文件就把上一份的草稿丢掉：行号只对原文件有意义。
        foreach (var row in Rows)
        {
            row.PropertyChanged -= OnRowPropertyChanged;
        }

        Rows = [];
        _ = LoadSelectedAsync();
        NotifyAll();
    }

    partial void OnSearchTextChanged(string value) => NotifyAll();

    partial void OnRowsChanged(IReadOnlyList<GenericConfigRowViewModel> value) => NotifyAll();

    partial void OnFilesChanged(IReadOnlyList<GenericConfigFileCardViewModel> value) => NotifyAll();

    partial void OnCanWriteChanged(bool value) => NotifyAll();

    partial void OnIsBusyChanged(bool value) => NotifyEditState();

    [RelayCommand]
    private void SelectFile(GenericConfigFileCardViewModel? file)
    {
        SelectedFile = file;
    }

    partial void OnErrorTextChanged(string? value)
    {
        OnPropertyChanged(nameof(HasErrorText));
        OnPropertyChanged(nameof(HasError));
    }

    /// <summary>有错误文案要显示。</summary>
    public bool HasErrorText => !string.IsNullOrWhiteSpace(ErrorText);

    /// <summary>是否有错误（供 XAML 绑定）。</summary>
    public bool HasError => HasErrorText;

    private void NotifyAll()
    {
        OnPropertyChanged(nameof(IsServiceAvailable));
        OnPropertyChanged(nameof(HasFiles));
        OnPropertyChanged(nameof(HasSelection));
        OnPropertyChanged(nameof(HasSelectedFile));
        OnPropertyChanged(nameof(IsReadonly));
        OnPropertyChanged(nameof(HasRows));
        OnPropertyChanged(nameof(FilteredFiles));
        OnPropertyChanged(nameof(FileCountText));
        OnPropertyChanged(nameof(SelectedTitle));
        OnPropertyChanged(nameof(SelectedSubtitle));
        OnPropertyChanged(nameof(EmptyTitle));
        OnPropertyChanged(nameof(EmptyDetail));
        OnPropertyChanged(nameof(HasReadonlyReason));
        OnPropertyChanged(nameof(HasError));
        NotifyEditState();
    }

    private void NotifyEditState()
    {
        OnPropertyChanged(nameof(PendingChangeCount));
        OnPropertyChanged(nameof(IsDirty));
        OnPropertyChanged(nameof(DirtyText));
        OnPropertyChanged(nameof(CanSave));
        OnPropertyChanged(nameof(CanRevert));
        SaveCommand.NotifyCanExecuteChanged();
        RevertCommand.NotifyCanExecuteChanged();
    }

    private string T(string fallback, string key, params object[] args)
    {
        var resolved = _translate?.Invoke(fallback);
        var text = string.IsNullOrWhiteSpace(resolved) ? fallback : resolved;
        return args.Length == 0 ? text : string.Format(System.Globalization.CultureInfo.CurrentCulture, text, args);
    }
}

/// <summary>左侧列表里的一张 cfg 卡片。</summary>
public sealed class GenericConfigFileCardViewModel
{
    public GenericConfigFileCardViewModel(GenericConfigFile file)
    {
        FilePath = file.FilePath;
        ModName = file.ModName;
        Title = string.IsNullOrWhiteSpace(file.DisplayName) ? file.RelativePath : file.DisplayName;
        Subtitle = file.RelativePath;
        IsWritable = file.IsWritable;
        MetaText = (file.IsWritable ? string.Empty : "只读 · ") + file.PairCount + " 项";
    }

    /// <summary>绝对路径（选中时用它去读）。</summary>
    public string FilePath { get; }

    /// <summary>所属 mod（GameData 下的一级目录）。</summary>
    public string ModName { get; }

    /// <summary>卡片标题。</summary>
    public string Title { get; }

    /// <summary>相对路径。</summary>
    public string Subtitle { get; }

    /// <summary>可写。</summary>
    public bool IsWritable { get; }

    /// <summary>右侧小字（只读 + 项数）。</summary>
    public string MetaText { get; }
}
