using System.Collections.ObjectModel;
using System.Windows.Input;
using Avalonia.Threading;
using CkanUI.Localization;
using CkanUI.Services.Abstractions;
using CkanUI.Services.Abstractions.KspConfig;
using CkanUI.ViewModels;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace CkanUI.Plugin.KspConfig;

/// <summary>
/// KSP 原生配置（settings.cfg）编辑页面视图模型。
/// 直接读写 KSP 原生配置项，并直接从 Squad 官方 dictionary.cfg 抽取官方译文展示。
/// </summary>
public sealed partial class KspConfigPageViewModel : PageViewModel, IUnsavedChangesAware, IPluginPage
{
    private readonly IKspConfigService _configService;
    private readonly IInstanceService? _instanceService;
    private readonly IErrorReporter? _errorReporter;

    private readonly List<KspSettingItemViewModel> _allItems = [];
    private KspConfigDocument? _document;

    /// <summary>
    /// 无参构造：供 Avalonia XAML 设计时预览或 ViewLocator 反射回退。
    /// </summary>
    public KspConfigPageViewModel()
        : this(new DesignTimeKspConfigService())
    {
    }

    /// <summary>
    /// 注入服务构造函数。
    /// </summary>
    public KspConfigPageViewModel(
        IKspConfigService configService,
        IInstanceService? instanceService = null,
        IErrorReporter? errorReporter = null)
    {
        _configService = configService ?? throw new ArgumentNullException(nameof(configService));
        _instanceService = instanceService;
        _errorReporter = errorReporter;

        InitTabs();
    }

    /// <inheritdoc />
    public override string Title => Text("KSP 游戏原生设置", LocalizationKeys.KspConfigPageTitle);

    /// <summary>
    /// 设置项名称的语言包键前缀。键由 settings.cfg 的数据决定（实例里有什么键就有什么键），
    /// 不可能为每个键写一个常量，因此这一族键在 <c>LocalizationKeyDriftTests</c> 里走前缀白名单。
    /// </summary>
    public const string SettingNameKeyPrefix = "Ui_KspConfigPage_Setting_";

    /// <summary>把 KSP 原始键换成语言包键，例：<c>MISSION_SHOW_CREATE_VESSEL_WARNING</c>。</summary>
    public static string SettingNameKey(string kspKey) => SettingNameKeyPrefix + kspKey;

    /// <summary>
    /// 设置项显示名的译名：精选项走 dictionary.cfg 官方译文（已由服务填好，不再查包），
    /// 其余走语言包的人工译名；包里没有这条键时退回模型给的友好名，绝不把键名糊到表单上。
    /// </summary>
    private string? LocalizedSettingName(KspSettingItem item)
    {
        if (!string.IsNullOrEmpty(item.LocKey))
        {
            return null;
        }

        var key = SettingNameKey(item.Key);
        var translated = Text(item.DisplayName, key);
        return string.Equals(translated, key, StringComparison.Ordinal) ? null : translated;
    }

    /// <summary>
    /// 分类页签集合。
    /// </summary>
    public ObservableCollection<KspConfigCategoryTabViewModel> Tabs { get; } = [];

    [ObservableProperty]
    private KspConfigCategoryTabViewModel? _selectedTab;

    partial void OnSelectedTabChanged(KspConfigCategoryTabViewModel? value)
    {
        OnPropertyChanged(nameof(CanResetFilters));
        ApplyFilter();
    }

    /// <summary>
    /// 经过分类和搜索过滤后的配置项集合。
    /// </summary>
    public ObservableCollection<KspSettingItemViewModel> FilteredItems { get; } = [];

    /// <summary>
    /// 按分类分节后的列表（「全部」页签每个分类一节，单分类页签只有一节）。
    /// 与 <see cref="FilteredItems"/> 同源同序，视图两栏都从这一批实例渲染。
    /// </summary>
    public ObservableCollection<KspConfigSectionViewModel> Sections { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSearchText))]
    private string _searchText = string.Empty;

    /// <summary>搜索框里是否有内容（「清除」按钮的可用性）。</summary>
    public bool HasSearchText => !string.IsNullOrWhiteSpace(SearchText);

    partial void OnSearchTextChanged(string value)
    {
        OnPropertyChanged(nameof(CanResetFilters));
        ApplyFilter();
    }

    /// <summary>
    /// 只显示已修改项。勾上之后列表立刻只剩改动过的行，改回去的行会即时消失。
    /// </summary>
    [ObservableProperty]
    private bool _showOnlyModified;

    partial void OnShowOnlyModifiedChanged(bool value)
    {
        OnPropertyChanged(nameof(CanResetFilters));
        ApplyFilter();
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasEmptyResult))]
    private bool _isLoading;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasStatus))]
    private string _statusMessage = string.Empty;

    /// <summary>是否有需要展示的状态文案；没话说时整条 banner 不占高度。</summary>
    public bool HasStatus => !string.IsNullOrWhiteSpace(StatusMessage);

    /// <summary>清空搜索词；搜索框是空的就什么都不做。</summary>
    [RelayCommand]
    private void ClearSearch()
    {
        if (!string.IsNullOrEmpty(SearchText))
        {
            SearchText = string.Empty;
        }
    }

    /// <summary>空结果卡上的一键复原：搜索、仅看已改、分类三样一起退回默认。</summary>
    [RelayCommand]
    private void ResetFilters()
    {
        ShowOnlyModified = false;
        SearchText = string.Empty;
        if (SelectedTab != Tabs[0])
        {
            SelectedTab = Tabs[0];
        }
        else
        {
            ApplyFilter();
        }
    }

    [ObservableProperty]
    private string _filePath = string.Empty;

    [ObservableProperty]
    private string _language = "zh-cn";

    [ObservableProperty]
    private bool _hasBackup;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasUnsavedChanges))]
    [NotifyPropertyChangedFor(nameof(SaveButtonText))]
    private int _modifiedCount;

    /// <summary>保存按钮文案：有待保存项时把数量写进按钮，用户不用回头数徽章。</summary>
    public string SaveButtonText => ModifiedCount > 0
        ? Text("保存 {0} 项", LocalizationKeys.KspConfigPageSaveWithCount, ModifiedCount)
        : Text("保存修改", LocalizationKeys.KspConfigPageSave);

    /// <inheritdoc />
    public bool HasUnsavedChanges => ModifiedCount > 0;

    /// <inheritdoc />
    public ICommand? SaveChangesCommand => SaveConfigCommand;

    /// <inheritdoc />
    ICommand? IUnsavedChangesAware.DiscardChangesCommand => DiscardChangesCommand;

    protected override void OnActivated()
    {
        base.OnActivated();
        if (_allItems.Count == 0 && !IsLoading)
        {
            _ = LoadConfigAsync();
        }
    }

    /// <summary>
    /// 异步加载 KSP 配置与 Squad 官方字典。
    /// </summary>
    [RelayCommand]
    public async Task LoadConfigAsync()
    {
        if (IsLoading) return;

        IsLoading = true;
        StatusMessage = Text("正在读取 KSP 原生配置与官方本地化字典...", LocalizationKeys.KspConfigPageLoading);

        try
        {
            var gameDir = _instanceService?.Current?.GameDir;
            _document = await _configService.LoadConfigAsync(gameDir).ConfigureAwait(true);

            FilePath = _document.FilePath;
            Language = _document.Language;
            HasBackup = _document.HasBackup;

            _allItems.Clear();
            foreach (var item in _document.Settings)
            {
                var itemViewModel = new KspSettingItemViewModel(item, OnItemModifiedStateChanged);
                itemViewModel.ApplyLocalizedName(LocalizedSettingName(item));
                _allItems.Add(itemViewModel);
            }

            UpdateTabCounts();
            ApplyFilter();
            UpdateModifiedCount();

            StatusMessage = Text(
                "已加载 {0} 项配置（官方词条 {1} 条）",
                LocalizationKeys.KspConfigPageLoadedSummary,
                _allItems.Count,
                _document.LocalizationTable.Count);
        }
        catch (Exception ex)
        {
            StatusMessage = Text("加载配置失败: {0}", LocalizationKeys.KspConfigPageLoadFailed, ex.Message);
            _errorReporter?.Report(ex, "KspConfigPageViewModel", "读取 KSP 原生设置异常");
        }
        finally
        {
            IsLoading = false;
        }
    }

    /// <summary>
    /// 保存已修改的配置项到 settings.cfg。
    /// </summary>
    [RelayCommand]
    public async Task SaveConfigAsync()
    {
        if (_document is null || IsLoading) return;

        var modified = _allItems.Where(i => i.IsModified).Select(i => i.Model).ToList();
        if (modified.Count == 0)
        {
            StatusMessage = Text("没有待保存的修改项。", LocalizationKeys.KspConfigPageNoModifications);
            return;
        }

        IsLoading = true;
        StatusMessage = Text("正在保存 {0} 项设置并生成备份...", LocalizationKeys.KspConfigPageSaving, modified.Count);

        try
        {
            var success = await _configService.SaveConfigAsync(_document, modified).ConfigureAwait(true);
            if (success)
            {
                // 重新对齐 OriginalValue
                foreach (var item in _allItems.Where(i => i.IsModified))
                {
                    // 重新封装或同步原始值
                    var newProp = item.GetType().GetProperty(nameof(KspSettingItemViewModel.OriginalValue));
                    // 由于 OriginalValue 是只读属性，重新构造或通过重新拉取保持干净
                }

                // 重新加载以确保读盘一致性与最新备份状态
                await LoadConfigAsync().ConfigureAwait(true);

                StatusMessage = Text("成功保存 {0} 项设置到 settings.cfg，并已创建 .bak 备份。", LocalizationKeys.KspConfigPageSaveSuccess, modified.Count);
            }
            else
            {
                StatusMessage = Text("保存设置失败，请查看日志获取详细信息。", LocalizationKeys.KspConfigPageSaveFailed);
            }
        }
        catch (Exception ex)
        {
            StatusMessage = Text("保存设置失败: {0}", LocalizationKeys.KspConfigPageSaveError, ex.Message);
            _errorReporter?.Report(ex, "KspConfigPageViewModel", "写入 settings.cfg 失败");
        }
        finally
        {
            IsLoading = false;
        }
    }

    /// <summary>
    /// 放弃所有修改，重置为读盘时的原始值。
    /// </summary>
    [RelayCommand]
    public void DiscardChanges()
    {
        foreach (var item in _allItems)
        {
            if (item.IsModified)
            {
                item.ResetCommand.Execute(null);
            }
        }
        UpdateModifiedCount();
        StatusMessage = Text("已撤回所有修改。", LocalizationKeys.KspConfigPageDiscarded);
    }

    /// <summary>
    /// 从 settings.cfg.bak 恢复备份。
    /// </summary>
    [RelayCommand]
    public async Task RestoreBackupAsync()
    {
        if (IsLoading) return;

        IsLoading = true;
        StatusMessage = Text("正在从 settings.cfg.bak 恢复...", LocalizationKeys.KspConfigPageRestoring);

        try
        {
            var gameDir = _instanceService?.Current?.GameDir;
            var success = await _configService.RestoreBackupAsync(gameDir).ConfigureAwait(true);
            if (success)
            {
                await LoadConfigAsync().ConfigureAwait(true);
                StatusMessage = Text("已成功从 .bak 备份恢复 settings.cfg！", LocalizationKeys.KspConfigPageRestoreSuccess);
            }
            else
            {
                StatusMessage = Text("未找到 settings.cfg.bak 备份文件或恢复失败。", LocalizationKeys.KspConfigPageRestoreFailed);
            }
        }
        catch (Exception ex)
        {
            StatusMessage = Text("恢复备份失败: {0}", LocalizationKeys.KspConfigPageRestoreError, ex.Message);
            _errorReporter?.Report(ex, "KspConfigPageViewModel", "恢复 settings.cfg 备份失败");
        }
        finally
        {
            IsLoading = false;
        }
    }

    // ---- 内部过滤与计数 ---------------------------------------------------

    private void InitTabs()
    {
        Tabs.Add(new("全部", null, "Layers"));
        Tabs.Add(new("图像画质", KspSettingCategory.Graphics, "Monitor"));
        Tabs.Add(new("声音音效", KspSettingCategory.Audio, "Volume"));
        Tabs.Add(new("游戏常规", KspSettingCategory.Gameplay, "Gamepad"));
        Tabs.Add(new("高级调试", KspSettingCategory.Advanced, "Wrench"));
        Tabs.Add(new("其它项", KspSettingCategory.Other, "More"));

        SelectedTab = Tabs[0];
    }

    private void UpdateTabCounts()
    {
        foreach (var tab in Tabs)
        {
            if (tab.Category is null)
            {
                tab.Count = _allItems.Count;
            }
            else
            {
                tab.Count = _allItems.Count(i => i.Category == tab.Category.Value);
            }
        }
    }

    private void ApplyFilter()
    {
        var category = SelectedTab?.Category;
        var query = SearchText?.Trim();

        var filtered = _allItems.AsEnumerable();

        if (category is not null)
        {
            filtered = filtered.Where(i => i.Category == category.Value);
        }

        if (ShowOnlyModified)
        {
            filtered = filtered.Where(i => i.IsModified);
        }

        if (!string.IsNullOrWhiteSpace(query))
        {
            filtered = filtered.Where(i =>
                i.Key.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                i.DisplayName.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                (i.Description != null && i.Description.Contains(query, StringComparison.OrdinalIgnoreCase)) ||
                (i.LocKey != null && i.LocKey.Contains(query, StringComparison.OrdinalIgnoreCase)));
        }

        // 常用设置置顶：按优先级权重降序，优先让核心设置位于前面
        filtered = filtered
            .OrderByDescending(i => i.Priority)
            .ThenBy(i => i.Key, StringComparer.OrdinalIgnoreCase);

        FilteredItems.Clear();
        foreach (var item in filtered)
        {
            FilteredItems.Add(item);
        }

        RebuildSections(FilteredItems);

        RaiseListSummaryChanged();
    }

    /// <summary>
    /// 按当前页签决定分节方式：选中的是「全部」就按分类各一节，选中的是具体分类就只给一节。
    /// 空分组不出现——「全部」页签下搜索到 3 项，不该留 4 个空节。
    /// </summary>
    private void RebuildSections(IReadOnlyList<KspSettingItemViewModel> items)
    {
        Sections.Clear();

        var selected = SelectedTab;
        if (selected?.Category is not null)
        {
            if (items.Count > 0)
            {
                Sections.Add(new(selected.Title, selected.Icon, items.Count, items));
            }

            return;
        }

        foreach (var tab in Tabs)
        {
            if (tab.Category is null)
            {
                continue;
            }

            var group = items.Where(item => item.Category == tab.Category.Value).ToArray();
            if (group.Length > 0)
            {
                Sections.Add(new(tab.Title, tab.Icon, group.Length, group));
            }
        }
    }

    // ---- 列表摘要（工具条右侧的计数条 + 空结果说明） -----------------------

    /// <summary>配置项总数（不随筛选变化）。</summary>
    public int TotalCount => _allItems.Count;

    /// <summary>精选常用项数量。</summary>
    public int CommonCount => _allItems.Count(i => i.IsCommon);

    /// <summary>当前列表条数 / 总数，放在页签行右侧当状态条用。</summary>
    public string VisibleCountText => Text(
        "{0} / {1} 项",
        LocalizationKeys.KspConfigPageVisibleCount,
        FilteredItems.Count,
        _allItems.Count);

    private void RaiseListSummaryChanged()
    {
        OnPropertyChanged(nameof(TotalCount));
        OnPropertyChanged(nameof(CommonCount));
        OnPropertyChanged(nameof(VisibleCountText));
        OnPropertyChanged(nameof(CanResetFilters));
        RaiseEmptyResultChanged();
    }

    private void RaiseEmptyResultChanged()
    {
        OnPropertyChanged(nameof(HasEmptyResult));
        OnPropertyChanged(nameof(EmptyResultText));
    }

    /// <summary>
    /// 当前视图为空时的说明：区分「本来就没有这类设置」与「被搜索/仅看已改筛掉了」。
    /// </summary>
    public string EmptyResultText
    {
        get
        {
            if (_allItems.Count == 0)
            {
                return Text("这个实例还没有读到任何 KSP 原生设置项。", LocalizationKeys.KspConfigPageEmptyNoItems);
            }

            if (ShowOnlyModified && ModifiedCount == 0)
            {
                return Text("还没有任何改动；勾选「仅看已改」后这里只会列出改动过的项。", LocalizationKeys.KspConfigPageEmptyNoModifications);
            }

            var query = SearchText?.Trim();
            if (!string.IsNullOrEmpty(query))
            {
                return Text("没有匹配「{0}」的配置项。", LocalizationKeys.KspConfigPageEmptyNoMatch, query);
            }

            return SelectedTab?.Category is null
                ? Text("没有可显示的配置项。", LocalizationKeys.KspConfigPageEmptyGeneric)
                : Text("「{0}」分类下没有配置项。", LocalizationKeys.KspConfigPageEmptyCategory, SelectedTab.Title);
        }
    }

    /// <summary>列表为空时展示说明卡而不是一片空白。</summary>
    public bool HasEmptyResult => !IsLoading && FilteredItems.Count == 0;

    /// <summary>空结果卡上的复原按钮是否可用（什么都没筛的时候不该出现一个按不动的按钮）。</summary>
    public bool CanResetFilters => ShowOnlyModified
        || !string.IsNullOrWhiteSpace(SearchText)
        || SelectedTab?.Category is not null;

    private void OnItemModifiedStateChanged()
    {
        UpdateModifiedCount();

        if (ShowOnlyModified)
        {
            // 「仅看已改」下要把改回去的行摘掉；重建列表会移除正在派发事件的控件，
            // 所以投递到下一轮再重算。
            Dispatcher.UIThread.Post(ApplyFilter, DispatcherPriority.Background);
        }
    }

    private void UpdateModifiedCount()
    {
        ModifiedCount = _allItems.Count(i => i.IsModified);
        RaiseEmptyResultChanged();
    }

    // ---- 设计时 Mock -------------------------------------------------------

    private sealed class DesignTimeKspConfigService : IKspConfigService
    {
        public Task<KspConfigDocument> LoadConfigAsync(string? instancePath = null, CancellationToken cancellationToken = default)
        {
            var mockSettings = new List<KspSettingItem>
            {
                new()
                {
                    Key = "SCREEN_RESOLUTION_WIDTH",
                    Value = "1920",
                    OriginalValue = "1920",
                    DisplayName = "屏幕分辨率 (宽)",
                    Category = KspSettingCategory.Graphics,
                    SettingType = KspSettingType.Integer,
                    MinValue = 800,
                    MaxValue = 3840
                },
                new()
                {
                    Key = "FULLSCREEN",
                    Value = "True",
                    OriginalValue = "True",
                    DisplayName = "全屏显示",
                    Category = KspSettingCategory.Graphics,
                    SettingType = KspSettingType.Boolean
                },
                new()
                {
                    Key = "MASTER_VOLUME",
                    Value = "0.8",
                    OriginalValue = "0.8",
                    DisplayName = "主音量",
                    Category = KspSettingCategory.Audio,
                    SettingType = KspSettingType.Float,
                    MinValue = 0,
                    MaxValue = 1,
                    Step = 0.05
                }
            };

            return Task.FromResult(new KspConfigDocument
            {
                FilePath = @"D:\Kerbal Space Program\settings.cfg",
                Settings = mockSettings,
                RawLines = ["SCREEN_RESOLUTION_WIDTH = 1920", "FULLSCREEN = True"],
                LocalizationTable = new Dictionary<string, string>(),
                Language = "zh-cn",
                HasBackup = true
            });
        }

        public Task<bool> SaveConfigAsync(KspConfigDocument document, IEnumerable<KspSettingItem> modifiedSettings, CancellationToken cancellationToken = default) => Task.FromResult(true);

        public Task<IReadOnlyDictionary<string, string>> LoadOfficialLocalizationAsync(string? instancePath = null, string? language = null, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyDictionary<string, string>>(new Dictionary<string, string>());

        public Task<bool> RestoreBackupAsync(string? instancePath = null, CancellationToken cancellationToken = default) => Task.FromResult(true);

        public string? GetBackupPath(string? instancePath = null) => null;
    }
}
