using CkanUI.Localization;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CkanUI.ModText;
using CkanUI.Services.Abstractions;
using CkanUI.ViewModels.Pages.ModText;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace CkanUI.ViewModels.Pages;

/// <summary>
/// 模组元数据翻译与词库管理页面视图模型（Plan 17）。
/// 支持查看当前语言包、词条检索、人工覆盖翻译编辑、文件导入导出与热重载。
/// </summary>
public sealed partial class ModTextPageViewModel : PageViewModel, IPageProjectionTrim
{
    private readonly IModTextService _service;
    private readonly IErrorReporter _errors;
    private readonly IDispatcher _dispatcher;

    private List<ModTextEntryItemViewModel> _allEntries = new();

    /// <summary>设计时构造函数。</summary>
    public ModTextPageViewModel()
        : this(new EmptyModTextService(), new EmptyErrorReporter(), new ImmediateDispatcher())
    {
    }

    /// <summary>运行时构造函数。</summary>
    public ModTextPageViewModel(IModTextService service, IErrorReporter errors, IDispatcher dispatcher)
    {
        _service = service ?? throw new ArgumentNullException(nameof(service));
        _errors = errors ?? throw new ArgumentNullException(nameof(errors));
        _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));

        StorageDirectory = _service.BaseDirectory;

        SaveOverrideCommand = new AsyncRelayCommand(ExecuteSaveOverrideAsync, () => CanSaveOverride);
        DeleteOverrideCommand = new AsyncRelayCommand(ExecuteDeleteOverrideAsync, () => CanDeleteOverride);
        SyncFromUniversalI18nCommand = new AsyncRelayCommand(ExecuteSyncFromUniversalI18nAsync);
        ReloadCommand = new AsyncRelayCommand(ExecuteReloadAsync);
        OpenFolderCommand = new RelayCommand(ExecuteOpenFolder);
    }

    /// <inheritdoc />
    public override string Title => Text("模组翻译", LocalizationKeys.ModTextTitle);

    /// <summary>基础存储路径。</summary>
    [ObservableProperty]
    private string _storageDirectory = string.Empty;

    /// <summary>可用语言列表。</summary>
    [ObservableProperty]
    private IReadOnlyList<string> _availableLanguages = Array.Empty<string>();

    /// <summary>当前选中的语言代码。</summary>
    [ObservableProperty]
    private string _selectedLanguage = "zh-cn";

    /// <summary>当前包文件列表。</summary>
    [ObservableProperty]
    private IReadOnlyList<ModPackFileInfo> _packFiles = Array.Empty<ModPackFileInfo>();

    /// <summary>过滤后的展示条目列表。</summary>
    [ObservableProperty]
    private IReadOnlyList<ModTextEntryItemViewModel> _entries = Array.Empty<ModTextEntryItemViewModel>();

    /// <summary>当前选中的条目。</summary>
    [ObservableProperty]
    private ModTextEntryItemViewModel? _selectedEntry;

    /// <summary>搜索框过滤关键字。</summary>
    [ObservableProperty]
    private string _searchQuery = string.Empty;

    /// <summary>仅展示人工覆盖项开关。</summary>
    [ObservableProperty]
    private bool _filterOverrideOnly;

    /// <summary>总条目数文本。</summary>
    [ObservableProperty]
    private string _summaryText = string.Empty;

    /// <summary>页面状态通知。</summary>
    [ObservableProperty]
    private string _statusMessage = string.Empty;

    /// <summary>正在加载状态。</summary>
    [ObservableProperty]
    private bool _isLoading;

    // --- 编辑区字段 ---

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveOverrideCommand))]
    [NotifyCanExecuteChangedFor(nameof(DeleteOverrideCommand))]
    private string _editIdentifier = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveOverrideCommand))]
    [NotifyCanExecuteChangedFor(nameof(DeleteOverrideCommand))]
    private string _editField = "abstract";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveOverrideCommand))]
    [NotifyCanExecuteChangedFor(nameof(DeleteOverrideCommand))]
    private string _editSource = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveOverrideCommand))]
    private string _editTranslation = string.Empty;

    public bool CanSaveOverride =>
        !string.IsNullOrWhiteSpace(EditIdentifier) &&
        !string.IsNullOrWhiteSpace(EditSource) &&
        !string.IsNullOrWhiteSpace(EditTranslation);

    public bool CanDeleteOverride =>
        !string.IsNullOrWhiteSpace(EditIdentifier) &&
        !string.IsNullOrWhiteSpace(EditSource);

    public IAsyncRelayCommand SaveOverrideCommand { get; }
    public IAsyncRelayCommand DeleteOverrideCommand { get; }
    public IAsyncRelayCommand SyncFromUniversalI18nCommand { get; }
    public IAsyncRelayCommand ReloadCommand { get; }
    public IRelayCommand OpenFolderCommand { get; }

    protected override void OnActivated()
    {
        base.OnActivated();
        _service.PackChanged += OnPackChanged;
        _ = LoadAsync();
    }

    protected override void OnDeactivated()
    {
        base.OnDeactivated();
        _service.PackChanged -= OnPackChanged;
    }

    /// <inheritdoc />
    public string PageName => Text("模组文本", LocalizationKeys.ModTextPageName);

    /// <summary>
    /// 丢掉词条投影（18 篇 §3 L1）：词条是从语言包文件解析出来的，<see cref="LoadAsync"/> 会重新解析。
    /// </summary>
    /// <remarks>
    /// 词条数是本页最容易膨胀的一份内存（多语言包合计上万条时每条都带一份视图模型），
    /// 而它 100% 可从磁盘重建，所以是 L1 的首选释放对象。搜索词与语言选择属于用户输入，保留。
    /// </remarks>
    public int TrimProjection()
    {
        var released = _allEntries.Count + Entries.Count;
        if (released == 0)
        {
            return 0;
        }

        _allEntries = new List<ModTextEntryItemViewModel>();
        Entries = Array.Empty<ModTextEntryItemViewModel>();
        SelectedEntry = null;
        SummaryText = string.Empty;
        return released;
    }

    private void OnPackChanged()
    {
        _dispatcher.Post(() => _ = RefreshEntriesAsync());
    }

    partial void OnSelectedLanguageChanged(string value)
    {
        if (!string.IsNullOrWhiteSpace(value) && !value.Equals(_service.CurrentLanguage, StringComparison.OrdinalIgnoreCase))
        {
            _ = SwitchLanguageAsync(value);
        }
    }

    partial void OnSearchQueryChanged(string value)
    {
        ApplyFilter();
    }

    partial void OnFilterOverrideOnlyChanged(bool value)
    {
        ApplyFilter();
    }

    partial void OnSelectedEntryChanged(ModTextEntryItemViewModel? value)
    {
        if (value is not null)
        {
            EditIdentifier = value.Identifier;
            EditField = string.IsNullOrEmpty(value.Field) ? "abstract" : value.Field;
            EditSource = value.Source;
            EditTranslation = value.Translation;
        }
    }

    public async Task LoadAsync(CancellationToken ct = default)
    {
        IsLoading = true;
        StatusMessage = Text("正在载入翻译词库...", LocalizationKeys.ModTextBusyLoading);
        try
        {
            await _service.ReloadPacksAsync(SelectedLanguage, ct);
            AvailableLanguages = _service.GetAvailableLanguages();
            SelectedLanguage = _service.CurrentLanguage;
            PackFiles = _service.GetPackFiles(SelectedLanguage);
            await RefreshEntriesAsync();
            StatusMessage = Text("载入完成，共 {0} 条词条。", LocalizationKeys.ModTextStatusLoaded, _allEntries.Count);
        }
        catch (Exception ex)
        {
            _errors.Report(ex, "ModTextPage.Load", Text("载入模组翻译包失败", LocalizationKeys.ModTextErrorLoad));
            StatusMessage = Text("载入失败: {0}", LocalizationKeys.ModTextStatusLoadFailed, ex.Message);
        }
        finally
        {
            IsLoading = false;
        }
    }

    private async Task SwitchLanguageAsync(string lang)
    {
        IsLoading = true;
        StatusMessage = Text("正在切换至语言: {0}...", LocalizationKeys.ModTextBusySwitching, lang);
        try
        {
            await _service.ReloadPacksAsync(lang);
            PackFiles = _service.GetPackFiles(lang);
            await RefreshEntriesAsync();
            StatusMessage = Text("已切换至 {0}，载入 {1} 条词条。", LocalizationKeys.ModTextStatusSwitched, lang, _allEntries.Count);
        }
        catch (Exception ex)
        {
            _errors.Report(ex, "ModTextPage.SwitchLanguage", Text("切换语言 '{0}' 失败", LocalizationKeys.ModTextErrorSwitchLanguage, lang));
            StatusMessage = Text("切换失败: {0}", LocalizationKeys.ModTextStatusSwitchFailed, ex.Message);
        }
        finally
        {
            IsLoading = false;
        }
    }

    private Task RefreshEntriesAsync()
    {
        return Task.Run(() =>
        {
            var raw = _service.GetAllEntries();
            var list = new List<ModTextEntryItemViewModel>(raw.Count);
            foreach (var item in raw)
            {
                list.Add(new ModTextEntryItemViewModel(item));
            }
            _allEntries = list;

            _dispatcher.Post(() =>
            {
                PackFiles = _service.GetPackFiles(_service.CurrentLanguage);
                ApplyFilter();
            });
        });
    }

    private void ApplyFilter()
    {
        IEnumerable<ModTextEntryItemViewModel> query = _allEntries;

        if (FilterOverrideOnly)
        {
            query = query.Where(x => x.IsHumanOverride);
        }

        if (!string.IsNullOrWhiteSpace(SearchQuery))
        {
            string q = SearchQuery.Trim();
            query = query.Where(x =>
                x.Source.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                x.Translation.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                x.Identifier.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                x.Normalized.Contains(q, StringComparison.OrdinalIgnoreCase));
        }

        var result = query.ToList();
        Entries = result;
        SummaryText = Text("已显示 {0} / {1} 条条目", LocalizationKeys.ModTextSummaryShown, result.Count, _allEntries.Count);
    }

    private async Task ExecuteSaveOverrideAsync()
    {
        if (!CanSaveOverride) return;

        IsLoading = true;
        StatusMessage = Text("正在保存 '{0}' 的人工覆盖翻译...", LocalizationKeys.ModTextBusySavingOverride, EditIdentifier);
        try
        {
            await _service.PutOverrideAsync(EditIdentifier.Trim(), EditField.Trim(), EditSource, EditTranslation);
            StatusMessage = Text("保存成功！已热更新至 overrides.jsonl 并生效。", LocalizationKeys.ModTextStatusOverrideSaved);
        }
        catch (Exception ex)
        {
            _errors.Report(ex, "ModTextPage.SaveOverride", Text("保存人工覆盖翻译失败", LocalizationKeys.ModTextErrorSaveOverride));
            StatusMessage = Text("保存失败: {0}", LocalizationKeys.ModTextStatusSaveFailed, ex.Message);
        }
        finally
        {
            IsLoading = false;
        }
    }

    private async Task ExecuteDeleteOverrideAsync()
    {
        if (!CanDeleteOverride) return;

        IsLoading = true;
        StatusMessage = Text("正在删除 '{0}' 的人工覆盖翻译...", LocalizationKeys.ModTextBusyDeletingOverride, EditIdentifier);
        try
        {
            await _service.DeleteOverrideAsync(EditIdentifier.Trim(), EditField.Trim(), EditSource);
            StatusMessage = Text("已成功从 overrides.jsonl 删除覆盖项并生效。", LocalizationKeys.ModTextStatusOverrideDeleted);
        }
        catch (Exception ex)
        {
            _errors.Report(ex, "ModTextPage.DeleteOverride", Text("删除人工覆盖翻译失败", LocalizationKeys.ModTextErrorDeleteOverride));
            StatusMessage = Text("删除失败: {0}", LocalizationKeys.ModTextStatusDeleteFailed, ex.Message);
        }
        finally
        {
            IsLoading = false;
        }
    }

    private async Task ExecuteSyncFromUniversalI18nAsync()
    {
        IsLoading = true;
        StatusMessage = Text("正在从 UniversalI18n 仓库同步翻译词库...", LocalizationKeys.ModTextBusySyncing);
        try
        {
            // 服务层不再抛异常表达失败（01 篇 §8）：失败结果走既有错误出口（错误中心 + 状态栏）。
            OperationResult<int> result = await _service.SyncFromUniversalI18nAsync();
            if (!result.IsSuccess)
            {
                _errors.Report(
                    "ModTextPage.SyncUniversalI18n",
                    result.Message ?? Text("同步 UniversalI18n 失败", LocalizationKeys.ModTextErrorSync),
                    result.Detail);
                StatusMessage = Text("同步失败: {0}", LocalizationKeys.ModTextStatusSyncFailed, result.Message);
                return;
            }

            StatusMessage = Text("同步成功！已同步 {0} 个词库文件并实时重载。", LocalizationKeys.ModTextStatusSynced, result.Value);
        }
        catch (Exception ex)
        {
            _errors.Report(ex, "ModTextPage.SyncUniversalI18n", Text("同步 UniversalI18n 失败", LocalizationKeys.ModTextErrorSync));
            StatusMessage = Text("同步失败: {0}", LocalizationKeys.ModTextStatusSyncFailed, ex.Message);
        }
        finally
        {
            IsLoading = false;
        }
    }

    private async Task ExecuteReloadAsync()
    {
        await LoadAsync();
    }

    private void ExecuteOpenFolder()
    {
        try
        {
            string dir = _service.BaseDirectory;
            Directory.CreateDirectory(dir);
            Process.Start(new ProcessStartInfo
            {
                FileName = dir,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            _errors.Report(ex, "ModTextPage.OpenFolder", Text("打开翻译文件目录失败", LocalizationKeys.ModTextErrorOpenFolder));
        }
    }

    // --- 设计时替身实现 ---

    private sealed class EmptyModTextService : IModTextService
    {
        public IModTextProvider Provider => new EmptyModTextProvider();
        public string CurrentLanguage => "zh-cn";
        public int TotalEntries => 0;
        public string BaseDirectory => string.Empty;
        public event Action? PackChanged { add { } remove { } }

        public Task ReloadPacksAsync(string? language = null, CancellationToken ct = default) => Task.CompletedTask;
        public IReadOnlyList<string> GetAvailableLanguages() => new[] { "zh-cn" };
        public IReadOnlyList<ModPackFileInfo> GetPackFiles(string? language = null) => Array.Empty<ModPackFileInfo>();
        public IReadOnlyList<ModTextEntryItem> GetAllEntries(string? filter = null) => Array.Empty<ModTextEntryItem>();
        public Task PutOverrideAsync(string identifier, string field, string sourceText, string translation, CancellationToken ct = default) => Task.CompletedTask;
        public Task DeleteOverrideAsync(string identifier, string field, string sourceText, CancellationToken ct = default) => Task.CompletedTask;
        public Task ImportJsonlAsync(string targetFileName, string content, string? language = null, CancellationToken ct = default) => Task.CompletedTask;
        public Task<OperationResult<string>> ExportJsonlAsync(string relativePath, CancellationToken ct = default) =>
            Task.FromResult(OperationResult<string>.Ok(string.Empty));

        public Task<OperationResult<int>> SyncFromUniversalI18nAsync(string? sourceDir = null, CancellationToken ct = default) =>
            Task.FromResult(OperationResult<int>.Ok(0));
    }

    private sealed class EmptyModTextProvider : IModTextProvider
    {
        public global::CkanUI.ModText.ModText? Find(string identifier, string field, string sourceText) => null;
    }

    private sealed class EmptyErrorReporter : IErrorReporter
    {
        public IReadOnlyList<ErrorEntry> Errors => Array.Empty<ErrorEntry>();
        public IReadOnlyList<ErrorEntry> Recent => Array.Empty<ErrorEntry>();
        public event Action<ErrorEntry>? Reported { add { } remove { } }
        public void Report(Exception exception, string context, string? userMessage = null) { }
        public void Report(string context, string userMessage, string? detail = null) { }
    }

    private sealed class ImmediateDispatcher : IDispatcher
    {
        public bool CheckAccess() => true;
        public bool IsOnUiThread => true;
        public void Post(Action action) => action();
        public Task InvokeAsync(Action action) { action(); return Task.CompletedTask; }
        public Task<T> InvokeAsync<T>(Func<T> func) => Task.FromResult(func());
    }
}
