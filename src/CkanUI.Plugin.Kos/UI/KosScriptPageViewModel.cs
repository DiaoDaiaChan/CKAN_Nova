using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using CkanUI.Plugin.Kos.Scripts;
using CkanUI.Services.Abstractions;
using CkanUI.Services.Abstractions.Dtos;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace CkanUI.Plugin.Kos.UI;

/// <summary>
/// 「kOS 脚本」页：管理 kOS Archive 卷（&lt;gameDir&gt;/Ships/Script/*.ks）并编辑脚本正文。
/// </summary>
/// <remarks>
/// <para>页面只通过 <see cref="IKosScriptService"/> 读写文件，自己不做任何 File/Directory 操作；
/// 目录是否存在、模组是否安装、外部改动都相信服务返回的目录/文档快照。</para>
/// <para>编辑器脏状态比较“当前正文”和“打开/保存时的正文”，改回原样会如实归零，而不是逼用户再存一次。
/// 切换脚本、重新载入、刷新目录时若有未保存修改，一律先挂起动作并显示页内确认条，原稿留在编辑器里。</para>
/// </remarks>
public sealed partial class KosScriptPageViewModel : ObservableObject, IPluginPage, IDisposable
{
    private const string ShipsFolderName = "Ships";
    private const string ScriptFolderName = "Script";

    private readonly IInstanceService? _instances;
    private readonly IKosScriptService? _scripts;
    private readonly Action<string> _openPath;
    private readonly List<KosScriptRowViewModel> _allRows = [];

    private KosScriptCatalog? _catalog;
    private KosScriptDocument? _document;
    private KosScriptRowViewModel? _selectedScript;
    private string _baselineContent = string.Empty;
    private string? _gameDir;
    private DateTime? _expectedLastWriteUtc;
    private bool _directoryExists;
    private bool _modInstalled;
    private bool _started;
    private bool _disposed;
    private bool _isDirty;

    private KosScriptRowViewModel? _pendingScript;
    private PendingAction _pendingAction;
    private NamePromptMode _namePromptMode;
    private string _namePromptOriginalName = string.Empty;

    /// <summary>
    /// 宿主只注册 IInstanceService 时的构造：脚本服务由插件自己造。
    /// 页面不依赖 CkanUI.App / CkanUI.Services / CkanUI.ViewModels，宿主用 ActivatorUtilities 也能直接构造。
    /// </summary>
    public KosScriptPageViewModel(IInstanceService instances)
        : this(instances, new KosScriptService())
    {
    }

    /// <summary>测试与宿主 DI 入口；两个服务都允许为 null，页面会明确显示缺什么而不是构造失败。</summary>
    public KosScriptPageViewModel(IInstanceService? instances, IKosScriptService? scripts)
        : this(instances, scripts, OpenWithShell)
    {
    }

    private KosScriptPageViewModel(
        IInstanceService? instances,
        IKosScriptService? scripts,
        Action<string> openPath)
    {
        _instances = instances;
        _scripts = scripts;
        _openPath = openPath ?? throw new ArgumentNullException(nameof(openPath));

        if (_instances is not null)
        {
            _instances.CurrentChanged += OnCurrentChanged;
            _instances.InstancesChanged += OnInstancesChanged;
        }
    }

    /// <summary>单测入口：把“打开所在文件夹”换成回调，用例不必真的弹资源管理器。</summary>
    public static KosScriptPageViewModel CreateForTests(
        IKosScriptService? scripts,
        IInstanceService? instances = null,
        Action<string>? openPath = null)
        => new(instances, scripts, openPath ?? OpenWithShell);

    /// <summary>左栏当前可见的脚本行（受 <see cref="SearchText"/> 过滤，选中行始终保留）。</summary>
    public ObservableCollection<KosScriptRowViewModel> Scripts { get; } = [];

    [ObservableProperty]
    private string _instanceName = "未选择实例";

    [ObservableProperty]
    private string _instanceGameDir = "—";

    [ObservableProperty]
    private string _scriptDirectory = "—";

    [ObservableProperty]
    private string _modStatusText = "未检测";

    [ObservableProperty]
    private bool _isModInstalled;

    [ObservableProperty]
    private string _searchText = string.Empty;

    [ObservableProperty]
    private string _editorText = string.Empty;

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    private string _errorMessage = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasStatusMessage))]
    private string _statusMessage = string.Empty;

    [ObservableProperty]
    private string _emptyTitle = "正在扫描 Archive 卷…";

    [ObservableProperty]
    private string _emptyDetail = string.Empty;

    [ObservableProperty]
    private bool _showEmptyState = true;

    [ObservableProperty]
    private bool _showEmptyAction;

    [ObservableProperty]
    private bool _showContent;

    [ObservableProperty]
    private bool _showUnsavedPrompt;

    [ObservableProperty]
    private string _unsavedPromptText = string.Empty;

    [ObservableProperty]
    private string _unsavedSaveButtonText = "保存并切换";

    [ObservableProperty]
    private bool _isNamePromptOpen;

    [ObservableProperty]
    private string _namePromptTitle = string.Empty;

    [ObservableProperty]
    private string _namePromptText = string.Empty;

    [ObservableProperty]
    private string _namePromptHint = string.Empty;

    [ObservableProperty]
    private string _namePromptConfirmText = "确定";

    [ObservableProperty]
    private bool _isDeletePromptOpen;

    [ObservableProperty]
    private string _deletePromptText = string.Empty;

    [ObservableProperty]
    private string _diagnosticsText = string.Empty;

    [ObservableProperty]
    private bool _hasDiagnostics;

    /// <summary>当前是否有打开的脚本正文（不表示已有未保存修改）。</summary>
    public bool HasDocument => _document is not null;

    /// <summary>左栏有脚本但当前没有可编辑正文时给右侧占位提示。</summary>
    public bool ShowEditorPlaceholder => ShowContent && _document is null;

    /// <summary>当前选中的行；有未保存修改时 setter 不会真的切换，而是挂起并弹页内确认条。</summary>
    public KosScriptRowViewModel? SelectedScript
    {
        get => _selectedScript;
        set
        {
            if (ReferenceEquals(_selectedScript, value))
            {
                return;
            }

            if (_disposed)
            {
                return;
            }

            if (IsBusy)
            {
                OnPropertyChanged();
                return;
            }

            if (value is null)
            {
                if (IsDirty)
                {
                    OnPropertyChanged();
                    return;
                }

                ClearDocument();
                SetSelectedRow(null);
                return;
            }

            if (IsDirty && _document is not null)
            {
                // 不静默换稿：记住用户想去哪，等确认条上三选一之后再动。
                _pendingScript = value;
                _pendingAction = PendingAction.Switch;
                UnsavedPromptText = BuildUnsavedPrompt(PendingAction.Switch, value);
                UnsavedSaveButtonText = "保存并切换";
                ShowUnsavedPrompt = true;
                OnPropertyChanged();
                return;
            }

            _ = SelectScriptAsync(value);
        }
    }

    /// <summary>是否有未保存修改。</summary>
    public bool IsDirty
    {
        get => _isDirty;
        private set
        {
            if (!SetProperty(ref _isDirty, value))
            {
                return;
            }

            OnPropertyChanged(nameof(DirtyText));
            OnPropertyChanged(nameof(HasUnsavedChanges));
            NotifyCommandStates();
        }
    }

    /// <summary>宿主/页面可读的脏标记别名。</summary>
    public bool HasUnsavedChanges => IsDirty;

    public string DirtyText => IsDirty ? "未保存" : "已保存";

    public bool HasError => ErrorMessage.Length > 0;

    public bool HasStatusMessage => StatusMessage.Length > 0;

    public bool CanInteract => !IsBusy;

    public bool HasSelection => _selectedScript is not null;

    public string DocumentName => _document?.Name ?? string.Empty;

    public string DocumentPath => _document?.FullPath ?? string.Empty;

    public bool IsBootDocument => _document?.IsBoot ?? false;

    public string LineCountText => $"行数 {CountLines(EditorText)}";

    public string CharacterCountText => $"字符 {EditorText.Length}";

    public string NewLineText => _document is null
        ? "换行 —"
        : _document.NewLine == "\r\n" ? "换行 CRLF" : "换行 LF";

    public string EncodingText => _document is null
        ? "编码 —"
        : _document.HadBom ? "编码 UTF-8 BOM" : "编码 UTF-8";

    public string DocumentSizeText => _document is null ? "—" : KosSizeText.Format(_document.SizeBytes);

    public string ScriptCountText => _allRows.Count == 0
        ? "0 个脚本"
        : SearchText.Trim().Length == 0
            ? $"{_allRows.Count} 个脚本"
            : $"显示 {Scripts.Count} / {_allRows.Count} 个";

    /// <summary>
    /// 视图首次挂到可视树时调用：只加载一次，之后重新挂载走受未保存保护的刷新。
    /// </summary>
    public void Start()
    {
        if (_disposed)
        {
            return;
        }

        if (_started)
        {
            Refresh();
            return;
        }

        _started = true;
        _ = LoadCatalogAsync();
    }

    /// <summary>显式刷新目录；有未保存修改时只弹确认条，不会偷偷重扫覆盖正文。</summary>
    public void Refresh()
    {
        if (_disposed)
        {
            return;
        }

        _ = RequestRefreshAsync();
    }

    /// <summary>视图离开可视树：不轮询、不持定时器，未保存正文继续留在页面实例里。</summary>
    public void Stop()
    {
    }

    /// <summary>刷新 Archive 列表；有未保存修改时挂起为确认条动作。</summary>
    [RelayCommand(CanExecute = nameof(CanRefresh))]
    public async Task RefreshCatalogAsync()
    {
        await RequestRefreshAsync().ConfigureAwait(true);
    }

    /// <summary>重新载入当前脚本；有未保存修改时先确认。</summary>
    [RelayCommand(CanExecute = nameof(CanReloadDocument))]
    public async Task ReloadDocumentAsync()
    {
        if (IsDirty)
        {
            RequestPending(PendingAction.ReloadDocument, null);
            return;
        }

        await ReloadSelectedDocumentAsync().ConfigureAwait(true);
    }

    /// <summary>保存当前脚本；服务失败时保留正文与脏标记，绝不自己宣布成功。</summary>
    [RelayCommand(CanExecute = nameof(CanSave))]
    public async Task SaveAsync()
    {
        await SaveCoreAsync().ConfigureAwait(true);
    }

    /// <summary>放弃编辑器里的修改，回到打开时读到的正文。</summary>
    [RelayCommand(CanExecute = nameof(CanDiscardChanges))]
    public void DiscardChanges()
    {
        if (_document is null)
        {
            return;
        }

        EditorText = _baselineContent;
        IsDirty = false;
        ShowStatus("已放弃未保存的修改，编辑器回到打开时的正文。");
    }

    /// <summary>新建脚本入口：先收名字，确认后才调服务。</summary>
    [RelayCommand(CanExecute = nameof(CanCreateScript))]
    public void NewScript()
    {
        if (!CanCreateScript() || !EnsureNoUnsavedChanges("新建脚本"))
        {
            return;
        }

        _namePromptMode = NamePromptMode.Create;
        _namePromptOriginalName = string.Empty;
        NamePromptTitle = "新建 kOS 脚本";
        NamePromptText = string.Empty;
        NamePromptHint = $"名字不以 .ks 结尾会自动补上；文件写到 {ScriptDirectory}";
        NamePromptConfirmText = "创建";
        IsDeletePromptOpen = false;
        IsNamePromptOpen = true;
        ErrorMessage = string.Empty;
        StatusMessage = string.Empty;
    }

    /// <summary>重命名当前脚本入口。</summary>
    [RelayCommand(CanExecute = nameof(CanRenameScript))]
    public void RenameScript()
    {
        if (!EnsureNoUnsavedChanges("重命名"))
        {
            return;
        }

        var name = _document?.Name ?? _selectedScript?.Name;
        if (string.IsNullOrWhiteSpace(name))
        {
            ShowError("先在左栏选中一个脚本，再重命名。");
            return;
        }

        _namePromptMode = NamePromptMode.Rename;
        _namePromptOriginalName = name;
        NamePromptTitle = "重命名脚本";
        NamePromptText = name;
        NamePromptHint = $"当前文件：{DocumentPath}；目标已存在时服务会拒绝，不会覆盖。";
        NamePromptConfirmText = "重命名";
        IsDeletePromptOpen = false;
        IsNamePromptOpen = true;
        ErrorMessage = string.Empty;
        StatusMessage = string.Empty;
    }

    /// <summary>删除入口：先确认，确认之前不碰服务。</summary>
    [RelayCommand(CanExecute = nameof(CanDeleteScript))]
    public void DeleteScript()
    {
        if (!EnsureNoUnsavedChanges("删除"))
        {
            return;
        }

        var name = _document?.Name ?? _selectedScript?.Name;
        if (string.IsNullOrWhiteSpace(name))
        {
            ShowError("先在左栏选中一个脚本，再删除。");
            return;
        }

        DeletePromptText = $"确定删除「{name}」吗？服务会先在同目录备份成 .bak-时间戳，再删除原文件；删除后可以从备份回滚。";
        IsNamePromptOpen = false;
        IsDeletePromptOpen = true;
        ErrorMessage = string.Empty;
        StatusMessage = string.Empty;
    }

    /// <summary>确认新建/重命名。失败时保留输入框与编辑器正文，让用户改个名字再试。</summary>
    [RelayCommand(CanExecute = nameof(CanConfirmNamePrompt))]
    public async Task ConfirmNamePromptAsync()
    {
        if (!IsNamePromptOpen || _scripts is null || _gameDir is null)
        {
            return;
        }

        var typed = NamePromptText.Trim();
        if (typed.Length == 0)
        {
            ShowError("脚本名不能为空。");
            return;
        }

        var name = EnsureKsExtension(typed);
        if (!TryValidateName(name, out var validationError))
        {
            ShowError(validationError);
            return;
        }

        IsBusy = true;
        ErrorMessage = string.Empty;
        StatusMessage = string.Empty;
        try
        {
            if (_namePromptMode == NamePromptMode.Create)
            {
                var create = await Task.Run(() => _scripts.Create(_gameDir, name, string.Empty)).ConfigureAwait(true);
                if (!create.IsSuccess)
                {
                    ShowError($"新建失败：{create.Message ?? "服务没有给出原因"}");
                    return;
                }

                var createdName = create.Value?.Name ?? name;
                IsNamePromptOpen = false;
                IsBusy = false;
                await LoadCatalogAsync(createdName).ConfigureAwait(true);
                ShowStatus($"已新建「{createdName}」。它现在就是 Archive 卷里的真实 .ks 文件。");
                return;
            }

            var rename = await Task.Run(() => _scripts.Rename(_gameDir, _namePromptOriginalName, name)).ConfigureAwait(true);
            if (!rename.IsSuccess)
            {
                // 重名冲突是最常见的失败：保留输入框，用户改个名字就能继续。
                ShowError($"重命名失败：{rename.Message ?? "服务没有给出原因"}");
                return;
            }

            var renamed = rename.Value ?? name;
            IsNamePromptOpen = false;
            IsBusy = false;
            await LoadCatalogAsync(renamed).ConfigureAwait(true);
            ShowStatus($"已重命名为「{renamed}」。");
        }
        catch (Exception ex)
        {
            ShowError(_namePromptMode == NamePromptMode.Create
                ? $"新建失败：{ex.Message}"
                : $"重命名失败：{ex.Message}");
        }
        finally
        {
            IsBusy = false;
            NotifyCommandStates();
        }
    }

    /// <summary>取消新建/重命名，不改磁盘也不改编辑器。</summary>
    [RelayCommand]
    public void CancelNamePrompt()
    {
        IsNamePromptOpen = false;
        NamePromptText = string.Empty;
        ErrorMessage = string.Empty;
    }

    /// <summary>确认删除；服务失败时保留编辑器正文与行选中。</summary>
    [RelayCommand(CanExecute = nameof(CanConfirmDelete))]
    public async Task ConfirmDeleteAsync()
    {
        if (!IsDeletePromptOpen || _scripts is null || _gameDir is null)
        {
            return;
        }

        var name = _document?.Name ?? _selectedScript?.Name;
        if (string.IsNullOrWhiteSpace(name))
        {
            ShowError("没有可删除的脚本。");
            return;
        }

        IsBusy = true;
        ErrorMessage = string.Empty;
        StatusMessage = string.Empty;
        try
        {
            var result = await Task.Run(() => _scripts.Delete(_gameDir, name)).ConfigureAwait(true);
            if (!result.IsSuccess)
            {
                ShowError($"删除失败：{result.Message ?? "服务没有给出原因"}");
                return;
            }

            IsDeletePromptOpen = false;
            IsBusy = false;
            await LoadCatalogAsync().ConfigureAwait(true);
            ShowStatus(result.Value is { Length: > 0 } backup
                ? $"已删除「{name}」，备份在 {backup}。"
                : $"已删除「{name}」，服务没有返回备份路径。");
        }
        catch (Exception ex)
        {
            ShowError($"删除失败：{ex.Message}");
        }
        finally
        {
            IsBusy = false;
            NotifyCommandStates();
        }
    }

    /// <summary>取消删除，文件与编辑器都不动。</summary>
    [RelayCommand]
    public void CancelDelete()
    {
        IsDeletePromptOpen = false;
        ErrorMessage = string.Empty;
    }

    /// <summary>确认条上的“保存并切换/保存并继续”。保存失败则留在确认条，原稿不丢。</summary>
    [RelayCommand(CanExecute = nameof(CanResolvePending))]
    public async Task SaveAndContinueAsync()
    {
        if (!ShowUnsavedPrompt || _pendingAction == PendingAction.None)
        {
            return;
        }

        var action = _pendingAction;
        var target = _pendingScript;
        if (!await SaveCoreAsync().ConfigureAwait(true))
        {
            return;
        }

        ClearPending();
        await ContinuePendingAsync(action, target).ConfigureAwait(true);
    }

    /// <summary>确认条上的“放弃修改”：明确丢弃编辑器里的改动后继续挂起的动作。</summary>
    [RelayCommand(CanExecute = nameof(CanResolvePending))]
    public async Task DiscardAndContinueAsync()
    {
        if (!ShowUnsavedPrompt || _pendingAction == PendingAction.None)
        {
            return;
        }

        var action = _pendingAction;
        var target = _pendingScript;
        // 先真的把正文退回到已保存版本，再继续后续动作。
        // 否则后续加载若失败，用户以为已经“放弃”了草稿，编辑器里却还留着它。
        EditorText = _baselineContent ?? string.Empty;
        IsDirty = false;
        ClearPending();
        await ContinuePendingAsync(action, target).ConfigureAwait(true);
    }

    /// <summary>确认条上的“取消”：留在原脚本，原稿一字不动。</summary>
    [RelayCommand]
    public void CancelPendingAction()
    {
        ClearPending();
        ShowStatus("已取消切换，当前脚本和未保存的正文都还在。");
    }

    /// <summary>打开 Ships/Script 所在文件夹；不碰文件内容。</summary>
    [RelayCommand(CanExecute = nameof(CanOpenFolder))]
    public void OpenFolder()
    {
        if (_gameDir is null)
        {
            ShowError("先选择一个游戏实例，才能定位 Ships/Script。");
            return;
        }

        if (!_directoryExists)
        {
            ShowError($"目录还不存在：{ScriptDirectory}。先新建一个脚本，服务会创建它。");
            return;
        }

        try
        {
            _openPath(ScriptDirectory);
            ShowStatus($"已在资源管理器里打开 {ScriptDirectory}。");
        }
        catch (Exception ex)
        {
            ShowError($"打开文件夹失败：{ex.Message}");
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        if (_instances is not null)
        {
            _instances.CurrentChanged -= OnCurrentChanged;
            _instances.InstancesChanged -= OnInstancesChanged;
        }
    }

    partial void OnSearchTextChanged(string value)
    {
        ApplyFilter();
        OnPropertyChanged(nameof(ScriptCountText));
    }

    partial void OnEditorTextChanged(string value)
    {
        // 服务契约要求正文用 \n；TextBox 在 Windows 上可能塞回 \r\n，这里统一后再算行数/脏状态。
        if (value.IndexOf('\r') >= 0)
        {
            EditorText = value.Replace("\r\n", "\n", StringComparison.Ordinal)
                              .Replace('\r', '\n');
            return;
        }

        OnPropertyChanged(nameof(LineCountText));
        OnPropertyChanged(nameof(CharacterCountText));
        RefreshDirty();
    }

    partial void OnIsBusyChanged(bool value)
    {
        OnPropertyChanged(nameof(CanInteract));
        NotifyCommandStates();
    }

    partial void OnShowContentChanged(bool value)
    {
        OnPropertyChanged(nameof(ShowEditorPlaceholder));
    }

    private bool CanRefresh() => !IsBusy;

    private bool CanReloadDocument() => !IsBusy && _document is not null;

    private bool CanSave() => !IsBusy && _document is not null && IsDirty;

    private bool CanDiscardChanges() => !IsBusy && _document is not null && IsDirty;

    private bool CanCreateScript()
        => !IsBusy && _scripts is not null && _gameDir is not null && _modInstalled;

    private bool CanRenameScript() => !IsBusy && (_document is not null || _selectedScript is not null);

    private bool CanDeleteScript() => !IsBusy && (_document is not null || _selectedScript is not null);

    private bool CanConfirmNamePrompt() => !IsBusy && IsNamePromptOpen;

    private bool CanConfirmDelete() => !IsBusy && IsDeletePromptOpen;

    private bool CanResolvePending() => !IsBusy && ShowUnsavedPrompt;

    private bool CanOpenFolder() => !IsBusy && _gameDir is not null;

    private async Task RequestRefreshAsync()
    {
        if (_disposed || IsBusy)
        {
            return;
        }

        if (IsDirty)
        {
            RequestPending(PendingAction.ReloadCatalog, null);
            return;
        }

        await LoadCatalogAsync().ConfigureAwait(true);
    }

    private async Task LoadCatalogAsync(string? preferredName = null)
    {
        if (_disposed || IsBusy)
        {
            return;
        }

        var gameDir = _instances?.Current?.GameDir;
        _gameDir = string.IsNullOrWhiteSpace(gameDir) ? null : gameDir;
        ApplyInstanceHeader();

        ErrorMessage = string.Empty;
        StatusMessage = string.Empty;

        if (_scripts is null)
        {
            ApplyServiceMissingState();
            return;
        }

        if (_gameDir is null)
        {
            ApplyNoInstanceState();
            return;
        }

        IsBusy = true;
        try
        {
            var catalog = await Task.Run(() => _scripts.LoadCatalog(_gameDir)).ConfigureAwait(true);
            ApplyCatalogFacts(catalog);

            if (!catalog.ModInstalled)
            {
                ApplyEmptyState(
                    "这个实例没有安装 kOS",
                    $"在 {Path.Combine(catalog.GameDir, "GameData", "kOS")} 没找到 kOS。先通过 CKAN 安装 kOS，再回来管理 Archive 脚本。",
                    showCreateAction: false);
                return;
            }

            if (!catalog.DirectoryExists)
            {
                ApplyEmptyState(
                    "kOS 已安装，但没有 Ships/Script 目录",
                    $"kOS 的 Archive 卷对应 {catalog.ScriptDirectory}，这个目录现在不存在。新建一个脚本时服务会创建它。",
                    showCreateAction: true);
                return;
            }

            if (catalog.Scripts.Count == 0)
            {
                ApplyEmptyState(
                    "Archive 卷里还没有脚本",
                    $"目录 {catalog.ScriptDirectory} 存在，但里面没有 .ks 文件。新建一个脚本，它就会出现在 kOS 的 Archive 卷里。",
                    showCreateAction: true);
                return;
            }

            _catalog = catalog;
            ShowContent = true;
            ShowEmptyState = false;
            ShowEmptyAction = false;
            EmptyTitle = string.Empty;
            EmptyDetail = string.Empty;
            ApplyRows(catalog);

            var wantedName = preferredName
                ?? _selectedScript?.Model.Name
                ?? _document?.Name;
            var row = FindRow(wantedName) ?? _allRows[0];
            SetSelectedRow(row);
            var loaded = await LoadDocumentCoreAsync(row).ConfigureAwait(true);
            if (!loaded && _document is null)
            {
                OnPropertyChanged(nameof(ShowEditorPlaceholder));
            }
        }
        catch (Exception ex)
        {
            // 服务契约不抛异常，但页面是最后一道防线：保留已打开正文，只把原因说清楚。
            ShowError($"扫描 Ships/Script 失败：{ex.Message}");
            if (_document is null)
            {
                // 扫描没有完成时不能拿上一次的行冒充本次结果。
                ResetRows();
                ShowContent = false;
                ShowEmptyState = true;
                EmptyTitle = "读取 Archive 卷失败";
                EmptyDetail = $"扫描 {ScriptDirectory} 时出错：{ex.Message}";
                ShowEmptyAction = false;
            }
        }
        finally
        {
            IsBusy = false;
            RefreshDerivedState();
            NotifyCommandStates();
        }
    }

    private async Task SelectScriptAsync(KosScriptRowViewModel row)
    {
        if (_scripts is null || _gameDir is null)
        {
            ShowError("脚本服务未就绪，暂时不能切换脚本。");
            OnPropertyChanged(nameof(SelectedScript));
            return;
        }

        IsBusy = true;
        ErrorMessage = string.Empty;
        StatusMessage = string.Empty;
        try
        {
            var loaded = await LoadDocumentCoreAsync(row).ConfigureAwait(true);
            if (!loaded)
            {
                // 读失败不换选中项：编辑器里原来的正文还在，用户还能保存/放弃。
                OnPropertyChanged(nameof(SelectedScript));
            }
        }
        finally
        {
            IsBusy = false;
            NotifyCommandStates();
        }
    }

    private async Task<bool> LoadDocumentCoreAsync(KosScriptRowViewModel row)
    {
        if (_scripts is null || _gameDir is null)
        {
            return false;
        }

        var result = await Task.Run(() => _scripts.Load(_gameDir, row.Model.Name)).ConfigureAwait(true);
        if (!result.IsSuccess || result.Value is null)
        {
            ShowError($"读取「{row.Name}」失败：{result.Message ?? "服务没有给出原因"}。编辑器里原有的正文没有改动。");
            return false;
        }

        ApplyDocument(result.Value, row);
        return true;
    }

    private async Task ReloadSelectedDocumentAsync()
    {
        if (_selectedScript is null || _document is null)
        {
            return;
        }

        IsBusy = true;
        ErrorMessage = string.Empty;
        StatusMessage = string.Empty;
        try
        {
            if (await LoadDocumentCoreAsync(_selectedScript).ConfigureAwait(true))
            {
                ShowStatus($"已从磁盘重新载入「{_document!.Name}」。");
            }
        }
        catch (Exception ex)
        {
            ShowError($"重新载入失败：{ex.Message}。编辑器里的正文没有改动。");
        }
        finally
        {
            IsBusy = false;
            NotifyCommandStates();
        }
    }

    private async Task<bool> SaveCoreAsync()
    {
        if (_scripts is null || _gameDir is null || _document is null)
        {
            ShowError("没有可保存的脚本：先选择一个游戏实例并打开一个脚本。");
            return false;
        }

        var content = EditorText;
        var name = _document.Name;
        var expected = _expectedLastWriteUtc;

        IsBusy = true;
        ErrorMessage = string.Empty;
        StatusMessage = string.Empty;
        try
        {
            var result = await Task.Run(() => _scripts.Save(_gameDir, name, content, expected)).ConfigureAwait(true);
            if (!result.IsSuccess)
            {
                // 外部编辑器改过文件时服务会拒绝；正文必须留在编辑器里，用户可重载或另存。
                ShowError($"保存失败：{result.Message ?? "服务没有给出原因"}。编辑器里的正文没有改动。");
                return false;
            }

            var savedName = result.Value?.Name ?? name;

            // 保存结果没带新的 LastWriteUtc，必须重新读一次，下一次保存的外部改动检测才有正确基线。
            var reload = await Task.Run(() => _scripts.Load(_gameDir, savedName)).ConfigureAwait(true);
            if (reload.IsSuccess && reload.Value is not null)
            {
                ApplyDocument(reload.Value, _selectedScript);
            }
            else
            {
                _baselineContent = EditorText;
                IsDirty = false;
                _expectedLastWriteUtc = null;
                ShowError($"已写入「{savedName}」，但保存后重新读取失败：{reload.Message ?? "服务没有给出原因"}。编辑器内容仍然保留。");
                return true;
            }

            var backup = result.Value?.BackupPath;
            ShowStatus(backup is { Length: > 0 }
                ? $"已保存「{savedName}」，旧文件备份在 {backup}。"
                : $"已保存「{savedName}」（第一次写入，没有旧文件需要备份）。");
            return true;
        }
        catch (Exception ex)
        {
            ShowError($"保存失败：{ex.Message}。编辑器里的正文没有改动。");
            return false;
        }
        finally
        {
            IsBusy = false;
            NotifyCommandStates();
        }
    }

    private void RequestPending(PendingAction action, KosScriptRowViewModel? target)
    {
        _pendingAction = action;
        _pendingScript = target;
        UnsavedPromptText = BuildUnsavedPrompt(action, target);
        UnsavedSaveButtonText = action == PendingAction.Switch ? "保存并切换" : "保存并继续";
        ShowUnsavedPrompt = true;
        NotifyCommandStates();
    }

    private async Task ContinuePendingAsync(PendingAction action, KosScriptRowViewModel? target)
    {
        switch (action)
        {
            case PendingAction.Switch when target is not null:
                await SelectScriptAsync(target).ConfigureAwait(true);
                break;

            case PendingAction.ReloadDocument:
                await ReloadSelectedDocumentAsync().ConfigureAwait(true);
                break;

            case PendingAction.ReloadCatalog:
                await LoadCatalogAsync().ConfigureAwait(true);
                break;
        }
    }

    private void ClearPending()
    {
        _pendingAction = PendingAction.None;
        _pendingScript = null;
        ShowUnsavedPrompt = false;
        UnsavedPromptText = string.Empty;
        NotifyCommandStates();
        // 列表控件可能已经把 SelectedItem 移到了目标行；强制通知把它拉回真实选中项。
        OnPropertyChanged(nameof(SelectedScript));
    }

    private string BuildUnsavedPrompt(PendingAction action, KosScriptRowViewModel? target)
    {
        var current = _document?.Name ?? _selectedScript?.Name ?? "当前脚本";
        return action switch
        {
            PendingAction.Switch when target is not null =>
                $"「{current}」有未保存的修改。切到「{target.Name}」前，先选择保存并切换、放弃修改，或取消留在原稿。",
            PendingAction.ReloadDocument =>
                $"「{current}」有未保存的修改。重新载入会用磁盘上的版本覆盖编辑器，先决定怎么处理原稿。",
            PendingAction.ReloadCatalog =>
                $"「{current}」有未保存的修改。刷新 Archive 列表前先决定怎么处理原稿。",
            _ => "当前脚本有未保存的修改。",
        };
    }

    private void ApplyCatalogFacts(KosScriptCatalog catalog)
    {
        _catalog = catalog;
        _modInstalled = catalog.ModInstalled;
        _directoryExists = catalog.DirectoryExists;
        IsModInstalled = catalog.ModInstalled;
        ScriptDirectory = string.IsNullOrWhiteSpace(catalog.ScriptDirectory)
            ? ResolveScriptDirectory(catalog.GameDir)
            : catalog.ScriptDirectory;
        ModStatusText = catalog.ModInstalled
            ? $"kOS 已安装（{Path.Combine(catalog.GameDir, "GameData", "kOS")}）"
            : "未安装（GameData/kOS 不存在）";
        DiagnosticsText = catalog.Diagnostics.Count == 0
            ? string.Empty
            : string.Join("\n", catalog.Diagnostics);
        HasDiagnostics = DiagnosticsText.Length > 0;
    }

    private void ApplyRows(KosScriptCatalog catalog)
    {
        _allRows.Clear();
        foreach (var file in catalog.Scripts)
        {
            _allRows.Add(new KosScriptRowViewModel(file));
        }

        ApplyFilter();
    }

    private void ApplyFilter()
    {
        var query = SearchText.Trim();
        Scripts.Clear();
        foreach (var row in _allRows)
        {
            if (query.Length == 0
                || ReferenceEquals(row, _selectedScript)
                || row.Name.Contains(query, StringComparison.OrdinalIgnoreCase))
            {
                Scripts.Add(row);
            }
        }
    }

    private KosScriptRowViewModel? FindRow(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return null;
        }

        return _allRows.FirstOrDefault(row =>
            string.Equals(row.Name, name, StringComparison.OrdinalIgnoreCase));
    }

    private void ApplyDocument(KosScriptDocument document, KosScriptRowViewModel? row)
    {
        _document = document;
        _baselineContent = document.Content;
        _expectedLastWriteUtc = document.LastWriteUtc;

        if (row is not null)
        {
            SetSelectedRow(row);
            row.Update(new KosScriptFile(
                document.Name,
                document.FullPath,
                document.SizeBytes,
                document.LastWriteUtc,
                CountLines(document.Content),
                document.IsBoot));
        }

        EditorText = document.Content;
        IsDirty = false;
        ShowContent = true;
        ShowEmptyState = false;
        EmptyTitle = string.Empty;
        EmptyDetail = string.Empty;
        ShowEmptyAction = false;
        IsDeletePromptOpen = false;
        RefreshDerivedState();
        NotifyCommandStates();
    }

    private void ApplyInstanceHeader()
    {
        var current = _instances?.Current;
        InstanceName = string.IsNullOrWhiteSpace(current?.Name) ? "未选择实例" : current.Name;
        InstanceGameDir = string.IsNullOrWhiteSpace(current?.GameDir) ? "—" : current.GameDir;
        ScriptDirectory = string.IsNullOrWhiteSpace(current?.GameDir)
            ? "—"
            : ResolveScriptDirectory(current.GameDir);
    }

    private void ApplyNoInstanceState()
    {
        ResetRows();
        _catalog = null;
        _modInstalled = false;
        _directoryExists = false;
        IsModInstalled = false;
        ModStatusText = "未检测";
        DiagnosticsText = string.Empty;
        HasDiagnostics = false;
        ResetDocumentAndSelection();
        ShowContent = false;
        ShowEmptyState = true;
        EmptyTitle = "还没有选择游戏实例";
        EmptyDetail = "在「实例」页选择一个 KSP 实例后回到这里，页面才知道该读写哪个 Ships/Script。";
        ShowEmptyAction = false;
        RefreshDerivedState();
        NotifyCommandStates();
    }

    private void ApplyServiceMissingState()
    {
        ResetRows();
        _catalog = null;
        _modInstalled = false;
        _directoryExists = false;
        IsModInstalled = false;
        ModStatusText = "未检测";
        DiagnosticsText = string.Empty;
        HasDiagnostics = false;
        ResetDocumentAndSelection();
        ShowContent = false;
        ShowEmptyState = true;
        EmptyTitle = "脚本服务未就绪";
        EmptyDetail = "页面没有拿到 IKosScriptService，暂时无法读写 Ships/Script。这通常是宿主注册缺失。";
        ShowEmptyAction = false;
        RefreshDerivedState();
        NotifyCommandStates();
    }

    private void ApplyEmptyState(string title, string detail, bool showCreateAction)
    {
        ResetRows();
        ResetDocumentAndSelection();
        ShowContent = false;
        ShowEmptyState = true;
        EmptyTitle = title;
        EmptyDetail = detail;
        // 这里还在 LoadCatalogAsync 的 try 里，IsBusy=true；不要用带 !IsBusy 的 CanCreateScript，
        // 否则空态的新建按钮会被永久藏掉。命令本身的 CanExecute 仍会在 finally 后刷新。
        ShowEmptyAction = showCreateAction && _scripts is not null && _gameDir is not null && _modInstalled;
        RefreshDerivedState();
        NotifyCommandStates();
    }

    private void ResetRows()
    {
        _allRows.Clear();
        Scripts.Clear();
    }

    private bool EnsureNoUnsavedChanges(string action)
    {
        if (!IsDirty)
        {
            return true;
        }

        ShowError($"「{DocumentName}」有未保存的修改。先保存或放弃修改，再{action}。");
        return false;
    }

    private void ResetDocumentAndSelection()
    {
        _document = null;
        _baselineContent = string.Empty;
        _expectedLastWriteUtc = null;
        EditorText = string.Empty;
        IsDirty = false;
        SetSelectedRow(null);
        RefreshDerivedState();
    }

    private void ClearDocument()
    {
        _document = null;
        _baselineContent = string.Empty;
        _expectedLastWriteUtc = null;
        EditorText = string.Empty;
        IsDirty = false;
        RefreshDerivedState();
    }

    private void RefreshDerivedState()
    {
        OnPropertyChanged(nameof(HasDocument));
        OnPropertyChanged(nameof(ShowEditorPlaceholder));
        OnPropertyChanged(nameof(DocumentName));
        OnPropertyChanged(nameof(DocumentPath));
        OnPropertyChanged(nameof(IsBootDocument));
        OnPropertyChanged(nameof(LineCountText));
        OnPropertyChanged(nameof(CharacterCountText));
        OnPropertyChanged(nameof(NewLineText));
        OnPropertyChanged(nameof(EncodingText));
        OnPropertyChanged(nameof(DocumentSizeText));
        OnPropertyChanged(nameof(ScriptCountText));
        OnPropertyChanged(nameof(HasSelection));
        OnPropertyChanged(nameof(DirtyText));
        OnPropertyChanged(nameof(HasUnsavedChanges));
    }

    private void SetSelectedRow(KosScriptRowViewModel? row)
    {
        if (ReferenceEquals(_selectedScript, row))
        {
            if (row is not null)
            {
                row.IsSelected = true;
            }

            OnPropertyChanged(nameof(HasSelection));
            return;
        }

        if (_selectedScript is not null)
        {
            _selectedScript.IsSelected = false;
        }

        _selectedScript = row;
        if (_selectedScript is not null)
        {
            _selectedScript.IsSelected = true;
        }

        OnPropertyChanged(nameof(SelectedScript));
        OnPropertyChanged(nameof(HasSelection));
        NotifyCommandStates();
    }

    private void RefreshDirty()
    {
        if (_document is null)
        {
            IsDirty = false;
            return;
        }

        IsDirty = !string.Equals(_baselineContent, EditorText, StringComparison.Ordinal);
    }

    private void OnCurrentChanged(InstanceInfo? current)
    {
        if (_disposed)
        {
            return;
        }

        if (IsDirty)
        {
            // 实例切换不能替用户丢稿：保留旧实例的正文，提示用户先保存/放弃，再回到新实例。
            ShowError("当前实例已经切换，但编辑器里还有未保存的修改。先保存或「放弃修改」，页面才会切到新实例的 Archive。");
            return;
        }

        _ = LoadCatalogAsync();
    }

    private void OnInstancesChanged()
    {
        if (_disposed || IsDirty)
        {
            return;
        }

        _ = LoadCatalogAsync();
    }

    private static string EnsureKsExtension(string name)
        => name.EndsWith(".ks", StringComparison.OrdinalIgnoreCase) ? name : name + ".ks";

    private static bool TryValidateName(string name, out string error)
    {
        if (name.Length > 128)
        {
            error = "脚本名太长（最多 128 个字符）。";
            return false;
        }

        if (name.Contains('/') || name.Contains('\\') || name.Contains(':') || name.Contains("..", StringComparison.Ordinal))
        {
            error = "脚本名只能是一个文件名，不能包含路径分隔符、冒号或 ..。";
            return false;
        }

        if (!name.EndsWith(".ks", StringComparison.OrdinalIgnoreCase))
        {
            error = "脚本名必须以 .ks 结尾。";
            return false;
        }

        error = string.Empty;
        return true;
    }

    private static int CountLines(string text)
    {
        if (text.Length == 0)
        {
            return 0;
        }

        var lines = 1;
        foreach (var ch in text)
        {
            if (ch == '\n')
            {
                lines++;
            }
        }

        return lines;
    }

    private static string ResolveScriptDirectory(string? gameDir)
        => string.IsNullOrWhiteSpace(gameDir)
            ? "—"
            : Path.Combine(gameDir, ShipsFolderName, ScriptFolderName);

    private static void OpenWithShell(string path)
    {
        using var process = Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
    }

    private void ShowStatus(string message)
    {
        StatusMessage = message;
        ErrorMessage = string.Empty;
    }

    private void ShowError(string message)
    {
        ErrorMessage = message;
        StatusMessage = string.Empty;
    }

    private void NotifyCommandStates()
    {
        RefreshCatalogCommand.NotifyCanExecuteChanged();
        ReloadDocumentCommand.NotifyCanExecuteChanged();
        SaveCommand.NotifyCanExecuteChanged();
        DiscardChangesCommand.NotifyCanExecuteChanged();
        NewScriptCommand.NotifyCanExecuteChanged();
        RenameScriptCommand.NotifyCanExecuteChanged();
        DeleteScriptCommand.NotifyCanExecuteChanged();
        ConfirmNamePromptCommand.NotifyCanExecuteChanged();
        ConfirmDeleteCommand.NotifyCanExecuteChanged();
        SaveAndContinueCommand.NotifyCanExecuteChanged();
        DiscardAndContinueCommand.NotifyCanExecuteChanged();
        OpenFolderCommand.NotifyCanExecuteChanged();
    }

    private enum PendingAction
    {
        None,
        Switch,
        ReloadDocument,
        ReloadCatalog,
    }

    private enum NamePromptMode
    {
        Create,
        Rename,
    }
}
