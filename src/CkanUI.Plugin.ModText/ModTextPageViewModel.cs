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
using CkanUI.ViewModels;
using CkanUI.ViewModels.Pages;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace CkanUI.Plugin.ModText;

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

    /// <summary>上一次建投影时的语言包版本；版本没变就整段跳过重建（见 <see cref="IModTextService.Revision"/>）。</summary>
    private int _loadedRevision = -1;

    // 筛选项里的「全部/未标注」文案在构造时解析一次：之后所有比较都用同一份字符串，
    // 不随界面语言变化而失配（下拉框显示的也是这份文案）。
    private readonly string _anyFieldLabel;
    private readonly string _unlabeledModLabel;
    private readonly string _unlabeledFieldLabel;
    private readonly string _kindLabelAny;
    private readonly string _kindLabelTranslated;
    private readonly string _kindLabelKept;
    private readonly string _kindLabelManual;
    private readonly string _groupAllLabel;
    private readonly string _scopeAllLabel;
    private readonly string _scopeMetadataLabel;
    private readonly string _scopeModsLabel;

    // 分组列表与筛选下拉会互相回写选中项，必须防重入，否则两边会反复触发 ApplyFilter 抖动。
    private bool _syncingGroupSelection;
    private List<ModTextGroupItem> _allGroups = new();

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
        var initialLanguages = _service.GetAvailableLanguages();
        AvailableLanguages = initialLanguages != null && initialLanguages.Count > 0 ? initialLanguages : new[] { "zh-cn" };

        _anyFieldLabel = Text("全部字段", LocalizationKeys.ModTextPageFilterAllFields);
        _unlabeledModLabel = Text("未标注", LocalizationKeys.ModTextPageFilterUnlabeledMod);
        _unlabeledFieldLabel = Text("无字段", LocalizationKeys.ModTextPageFilterUnlabeledField);
        _kindLabelAny = Text("全部", LocalizationKeys.ModTextPageFilterAllKinds);
        _kindLabelTranslated = Text("已翻译", LocalizationKeys.ModTextPageFilterKindTranslated);
        _kindLabelKept = Text("保留原文", LocalizationKeys.ModTextPageFilterKindKeep);
        _kindLabelManual = Text("人工覆盖", LocalizationKeys.ModTextPageFilterKindOverride);
        _groupAllLabel = Text("全部模组", LocalizationKeys.ModTextPageGroupAll);
        _scopeAllLabel = Text("全部范围", LocalizationKeys.ModTextPageScopeAll);
        _scopeMetadataLabel = Text("CKAN 元数据", LocalizationKeys.ModTextPageScopeMetadata);
        _scopeModsLabel = Text("模组翻译", LocalizationKeys.ModTextPageScopeMods);
        ScopeFilterOptions = new[] { _scopeAllLabel, _scopeMetadataLabel, _scopeModsLabel };
        SelectedScopeFilter = _scopeAllLabel;
        SelectedFieldFilter = _anyFieldLabel;
        SelectedKindFilter = _kindLabelAny;
        FieldFilterOptions = new[] { _anyFieldLabel };
        KindFilterOptions = new[] { _kindLabelAny, _kindLabelTranslated, _kindLabelKept, _kindLabelManual };

        SaveOverrideCommand = new AsyncRelayCommand(ExecuteSaveOverrideAsync, () => CanSaveOverride);
        DeleteOverrideCommand = new AsyncRelayCommand(ExecuteDeleteOverrideAsync, () => CanDeleteOverride);
        SyncFromUniversalI18nCommand = new AsyncRelayCommand(ExecuteSyncFromUniversalI18nAsync);
        ReloadCommand = new AsyncRelayCommand(ExecuteReloadAsync);
        OpenFolderCommand = new RelayCommand(ExecuteOpenFolder);
        ClearFiltersCommand = new RelayCommand(ExecuteClearFilters);
        ToggleOverridePanelCommand = new RelayCommand(ExecuteToggleOverridePanel);
        SelectScopeCommand = new RelayCommand<string>(ExecuteSelectScope);
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
    [NotifyPropertyChangedFor(nameof(HasPackFiles))]
    private IReadOnlyList<ModPackFileInfo> _packFiles = Array.Empty<ModPackFileInfo>();

    /// <summary>是否发现了词库文件（决定文件清单卡的显示）。</summary>
    public bool HasPackFiles => PackFiles.Count > 0;

    /// <summary>过滤后的展示条目列表。</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasEntries))]
    private IReadOnlyList<ModTextEntryItemViewModel> _entries = Array.Empty<ModTextEntryItemViewModel>();

    /// <summary>当前过滤结果是否为空（决定列表区显示空状态）。</summary>
    public bool HasEntries => Entries.Count > 0;

    /// <summary>当前选中的条目。</summary>
    [ObservableProperty]
    private ModTextEntryItemViewModel? _selectedEntry;

    /// <summary>搜索框过滤关键字。</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasActiveFilters))]
    private string _searchQuery = string.Empty;

    /// <summary>按字段筛选（abstract / description / …）；首项是「全部字段」。</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasActiveFilters))]
    private string _selectedFieldFilter = string.Empty;

    /// <summary>字段筛选项（首项「全部字段」）。</summary>
    [ObservableProperty]
    private IReadOnlyList<string> _fieldFilterOptions = Array.Empty<string>();

    /// <summary>按条目类型筛选（全部 / 已翻译 / 保留原文 / 人工覆盖）。</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasActiveFilters))]
    private string _selectedKindFilter = string.Empty;

    /// <summary>类型筛选项。</summary>
    [ObservableProperty]
    private IReadOnlyList<string> _kindFilterOptions = Array.Empty<string>();

    /// <summary>左栏「按模组查看」的分组列表（首项「全部模组」；受 <see cref="GroupFilterQuery"/> 过滤）。</summary>
    [ObservableProperty]
    private IReadOnlyList<ModTextGroupItem> _modGroups = Array.Empty<ModTextGroupItem>();

    /// <summary>分组栏自身的搜索词：全量语言包有三千多个模组，没有它只能滚。空串表示不过滤。</summary>
    [ObservableProperty]
    private string _groupFilterQuery = string.Empty;

    /// <summary>当前选中的分组；<c>null</c> 表示还没有分组。</summary>
    [ObservableProperty]
    private ModTextGroupItem? _selectedGroup;

    /// <summary>当前分组键；空字符串表示「全部」。</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasActiveFilters))]
    private string _selectedGroupKey = string.Empty;

    /// <summary>范围筛选：全部 / CKAN 元数据 / 模组翻译。两套语料混在一起时，左边分组、右边列表都读不出重点。</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasActiveFilters))]
    [NotifyPropertyChangedFor(nameof(IsScopeAll))]
    [NotifyPropertyChangedFor(nameof(IsScopeMetadata))]
    [NotifyPropertyChangedFor(nameof(IsScopeMods))]
    private string _selectedScopeFilter = string.Empty;

    /// <summary>范围筛选项。</summary>
    [ObservableProperty]
    private IReadOnlyList<string> _scopeFilterOptions = Array.Empty<string>();

    /// <summary>范围三态，供视图上的显式按钮做高亮（下拉框藏得太深，用户找不到"只看元数据"）。</summary>
    public bool IsScopeAll => string.Equals(SelectedScopeFilter, _scopeAllLabel, StringComparison.Ordinal);

    public bool IsScopeMetadata => string.Equals(SelectedScopeFilter, _scopeMetadataLabel, StringComparison.Ordinal);

    public bool IsScopeMods => string.Equals(SelectedScopeFilter, _scopeModsLabel, StringComparison.Ordinal);

    /// <summary>按范围按钮切换；参数是 <see cref="ScopeFilterOptions"/> 里的展示文本。</summary>
    public IRelayCommand<string> SelectScopeCommand { get; }

    /// <summary>右侧人工覆盖面板是否显示（隐藏后列表能占满窗口）。</summary>
    [ObservableProperty]
    private bool _isOverridePanelVisible = true;

    /// <summary>是否正在筛选（决定「清除筛选」可用与页脚是否加「已筛选」）。</summary>
    public bool HasActiveFilters =>
        !string.IsNullOrWhiteSpace(SearchQuery) ||
        !string.IsNullOrEmpty(SelectedGroupKey) ||
        !string.Equals(SelectedScopeFilter, _scopeAllLabel, StringComparison.Ordinal) ||
        !string.Equals(SelectedFieldFilter, _anyFieldLabel, StringComparison.Ordinal) ||
        !string.Equals(SelectedKindFilter, _kindLabelAny, StringComparison.Ordinal);

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
    public IRelayCommand ClearFiltersCommand { get; }

    /// <summary>显示/隐藏右侧人工覆盖面板（隐藏后词条列表占满窗口）。</summary>
    public IRelayCommand ToggleOverridePanelCommand { get; }

    /// <summary>当前结果里存在"非默认来源"条目时才显示类型列（默认词典条目每行挂徽标是纯噪声）。</summary>
    [ObservableProperty]
    private bool _showsSourceBadgeColumn = true;

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
        // 正在显示的页面不释放：投影丢完 OnActivated 不会再触发，用户面前就是一张空表，
        // 状态栏却还留着「载入完成」的旧文案（启动游戏前的 L1 释放正是这么撞上的）。
        if (IsActive)
        {
            return 0;
        }

        var released = _allEntries.Count + Entries.Count;
        if (released == 0)
        {
            return 0;
        }

        _allEntries = new List<ModTextEntryItemViewModel>();
        _loadedRevision = -1;
        Entries = Array.Empty<ModTextEntryItemViewModel>();
        _allGroups = new List<ModTextGroupItem>();
        ModGroups = Array.Empty<ModTextGroupItem>();
        SelectedGroup = null;
        SelectedEntry = null;
        SummaryText = string.Empty;
        return released;
    }

    private void OnPackChanged()
    {
        // 重载过程中自己就会重建投影，别在这里再来一遍（一次激活两次 19k 条投影）。
        if (IsLoading)
        {
            return;
        }

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

    partial void OnGroupFilterQueryChanged(string value)
    {
        ApplyGroupFilter();
    }

    partial void OnSelectedGroupChanged(ModTextGroupItem? value)
    {
        if (_syncingGroupSelection)
        {
            return;
        }

        _syncingGroupSelection = true;
        try
        {
            SelectedGroupKey = value?.Key ?? string.Empty;
        }
        finally
        {
            _syncingGroupSelection = false;
        }

        ApplyFilter();
    }

    partial void OnSelectedFieldFilterChanged(string value)
    {
        ApplyFilter();
    }

    partial void OnSelectedKindFilterChanged(string value)
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
            // 语言包没变：词条、分组、筛选结果都还是上一次那份，整段跳过。
            if (_loadedRevision == _service.Revision && _allEntries.Count > 0)
            {
                StatusMessage = Text("载入完成，共 {0} 条词条。", LocalizationKeys.ModTextStatusLoaded, _allEntries.Count);
                return;
            }

            await RefreshEntriesAsync();
            _loadedRevision = _service.Revision;

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
                RebuildFilterOptions();
                RebuildGroups();
                ApplyFilter();
            });
        });
    }

    /// <summary>
    /// 重建字段下拉框的候选项：只列当前语言包里真实存在的值，按条目数降序，首项是「全部」。
    /// </summary>
    private void RebuildFilterOptions()
    {
        var fields = BuildFacet(_allEntries.Select(x => x.Field), _unlabeledFieldLabel);
        fields.Insert(0, _anyFieldLabel);
        FieldFilterOptions = fields;
        if (!FieldFilterOptions.Contains(SelectedFieldFilter, StringComparer.Ordinal))
        {
            SelectedFieldFilter = _anyFieldLabel;
        }
    }

    private static List<string> BuildFacet(IEnumerable<string> values, string unlabeledLabel)
    {
        var list = new List<string>();
        int unlabeled = 0;
        foreach (var group in values
                     .GroupBy(v => v ?? string.Empty, StringComparer.OrdinalIgnoreCase)
                     .OrderByDescending(g => g.Count())
                     .ThenBy(g => g.Key, StringComparer.OrdinalIgnoreCase))
        {
            if (group.Key.Length == 0)
            {
                unlabeled = group.Count();
                continue;
            }

            list.Add(group.Key);
        }

        if (unlabeled > 0)
        {
            list.Add(unlabeledLabel);
        }

        return list;
    }

    /// <summary>
    /// 重建左栏的模组分组：以词条的 CKAN identifier（老数据退回语言包 mod 名）归堆，
    /// 按条目数降序，首项是「全部模组」。选中项按 Key 找回，找不到就回落「全部」。
    /// </summary>
    private void RebuildGroups()
    {
        var scoped = ScopeSource().ToList();
        var groups = new List<ModTextGroupItem>(Math.Min(scoped.Count, 4096) + 1)
        {
            new(string.Empty, _groupAllLabel, scoped.Count,
                Text("{0} 条", LocalizationKeys.ModTextPageGroupCount, scoped.Count), isAll: true),
        };

        foreach (var group in scoped
                     .GroupBy(GroupKeyOf, StringComparer.OrdinalIgnoreCase)
                     .OrderByDescending(g => g.Count())
                     .ThenBy(g => g.Key, StringComparer.OrdinalIgnoreCase))
        {
            var count = group.Count();
            var name = group.Key.Length == 0 ? _unlabeledModLabel : group.Key;
            groups.Add(new ModTextGroupItem(group.Key, name, count,
                Text("{0} 条", LocalizationKeys.ModTextPageGroupCount, count), isAll: false));
        }

        _allGroups = groups;
        ApplyGroupFilter();
    }

    /// <summary>
    /// 按 <see cref="GroupFilterQuery"/> 收窄左栏：首项「全部模组」常驻，其余按名字包含匹配；
    /// 选中的组被过滤掉时回落到「全部」，否则界面显示的组与筛选条件会各说各话。
    /// </summary>
    private void ApplyGroupFilter()
    {
        string query = GroupFilterQuery?.Trim() ?? string.Empty;
        IReadOnlyList<ModTextGroupItem> visible = query.Length == 0
            ? _allGroups
            : _allGroups.Where(x => x.IsAll || x.DisplayName.Contains(query, StringComparison.OrdinalIgnoreCase)).ToList();

        if (visible.Count == 0)
        {
            visible = _allGroups.Take(1).ToList();
        }

        ModGroups = visible;

        string previousKey = SelectedGroupKey;
        var selected = visible.FirstOrDefault(x => !x.IsAll && string.Equals(x.Key, SelectedGroupKey, StringComparison.OrdinalIgnoreCase))
                       ?? visible[0];
        _syncingGroupSelection = true;
        try
        {
            SelectedGroup = selected;
            SelectedGroupKey = selected.Key;
        }
        finally
        {
            _syncingGroupSelection = false;
        }

        // 分组被过滤掉时键会回落到空串，而键是普通属性（没有生成回调），
        // 不在这里补一次 ApplyFilter，列表还会停在上一组的筛选结果上。
        if (!string.Equals(previousKey, SelectedGroupKey, StringComparison.Ordinal))
        {
            ApplyFilter();
        }
    }

    /// <summary>词条归堆用的键：优先 CKAN identifier，其次语言包 mod 名，都没有就是空（未标注）。</summary>
    private static string GroupKeyOf(ModTextEntryItemViewModel entry) =>
        !string.IsNullOrEmpty(entry.Identifier) ? entry.Identifier
        : !string.IsNullOrEmpty(entry.Mod) ? entry.Mod
        : string.Empty;

    /// <summary>当前范围下的条目（元数据 / 模组翻译 / 全部）。分组与列表都从这一份派生，避免两处口径打架。</summary>
    private IEnumerable<ModTextEntryItemViewModel> ScopeSource()
    {
        if (string.Equals(SelectedScopeFilter, _scopeMetadataLabel, StringComparison.Ordinal))
        {
            return _allEntries.Where(IsMetadataEntry);
        }

        if (string.Equals(SelectedScopeFilter, _scopeModsLabel, StringComparison.Ordinal))
        {
            return _allEntries.Where(x => !IsMetadataEntry(x));
        }

        return _allEntries;
    }

    /// <summary>CKAN 元数据条目的判据：带 <c>ckan:</c> 稳定键，或来自 <c>CKANMeta</c> 包。</summary>
    private static bool IsMetadataEntry(ModTextEntryItemViewModel entry) =>
        entry.Key.StartsWith("ckan:", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(entry.Mod, "CKANMeta", StringComparison.OrdinalIgnoreCase);

    private void ApplyFilter()
    {
        IEnumerable<ModTextEntryItemViewModel> query = ScopeSource();

        if (!string.IsNullOrEmpty(SelectedGroupKey))
        {
            query = query.Where(x => string.Equals(GroupKeyOf(x), SelectedGroupKey, StringComparison.OrdinalIgnoreCase));
        }

        if (!string.Equals(SelectedFieldFilter, _anyFieldLabel, StringComparison.Ordinal))
        {
            bool wantUnlabeled = string.Equals(SelectedFieldFilter, _unlabeledFieldLabel, StringComparison.Ordinal);
            query = query.Where(x => wantUnlabeled
                ? string.IsNullOrEmpty(x.Field)
                : string.Equals(x.Field, SelectedFieldFilter, StringComparison.OrdinalIgnoreCase));
        }

        if (string.Equals(SelectedKindFilter, _kindLabelKept, StringComparison.Ordinal))
        {
            query = query.Where(x => x.IsKeep);
        }
        else if (string.Equals(SelectedKindFilter, _kindLabelManual, StringComparison.Ordinal))
        {
            query = query.Where(x => x.IsHumanOverride);
        }
        else if (string.Equals(SelectedKindFilter, _kindLabelTranslated, StringComparison.Ordinal))
        {
            query = query.Where(x => !x.IsKeep && !x.IsHumanOverride && !string.IsNullOrEmpty(x.Translation));
        }

        if (!string.IsNullOrWhiteSpace(SearchQuery))
        {
            string q = SearchQuery.Trim();
            query = query.Where(x =>
                x.Source.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                x.Translation.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                x.Identifier.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                x.Normalized.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                x.Mod.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                x.Field.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                x.Context.Contains(q, StringComparison.OrdinalIgnoreCase));
        }

        var result = query.ToList();
        Entries = result;
        ShowsSourceBadgeColumn = result.Any(entry => entry.ShowSourceBadge);
        int scopedTotal = _allEntries.Count;
        int metadataCount = _allEntries.Count(IsMetadataEntry);
        if (string.Equals(SelectedScopeFilter, _scopeMetadataLabel, StringComparison.Ordinal))
        {
            scopedTotal = metadataCount;
        }
        else if (string.Equals(SelectedScopeFilter, _scopeModsLabel, StringComparison.Ordinal))
        {
            scopedTotal = _allEntries.Count - metadataCount;
        }
        SummaryText = HasActiveFilters
            ? Text("已显示 {0} / {1} 条 · 已筛选", LocalizationKeys.ModTextSummaryFiltered, result.Count, scopedTotal)
            : Text("已显示 {0} / {1} 条", LocalizationKeys.ModTextSummaryShown, result.Count, scopedTotal);
    }

    /// <summary>范围变了就要重算分组并把分组选择归零，否则会停在上一范围的分组上筛出空列表。</summary>
    partial void OnSelectedScopeFilterChanged(string value)
    {
        _syncingGroupSelection = true;
        try
        {
            SelectedGroup = null;
            SelectedGroupKey = string.Empty;
        }
        finally
        {
            _syncingGroupSelection = false;
        }

        RebuildGroups();
        ApplyFilter();
    }

    private void ExecuteToggleOverridePanel() => IsOverridePanelVisible = !IsOverridePanelVisible;

    private void ExecuteSelectScope(string? scope)
    {
        if (string.IsNullOrEmpty(scope) || string.Equals(scope, SelectedScopeFilter, StringComparison.Ordinal))
        {
            return;
        }

        SelectedScopeFilter = scope;
    }

    private void ExecuteClearFilters()
    {
        SearchQuery = string.Empty;
        if (!string.Equals(SelectedScopeFilter, _scopeAllLabel, StringComparison.Ordinal))
        {
            SelectedScopeFilter = _scopeAllLabel;
        }

        SelectedGroupKey = string.Empty;
        _syncingGroupSelection = true;
        try
        {
            SelectedGroup = ModGroups.Count > 0 ? ModGroups[0] : null;
        }
        finally
        {
            _syncingGroupSelection = false;
        }

        SelectedFieldFilter = _anyFieldLabel;
        SelectedKindFilter = _kindLabelAny;

        // 三个下拉可能本来就是默认值：属性值没变就不会触发各自的回调，这里显式刷一次。
        ApplyFilter();
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

        public int Revision => 0;
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
