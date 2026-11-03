using System.Collections.ObjectModel;
using System.Windows.Input;
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
    public override string Title => Text("KSP 游戏原生设置", "Pages.KspConfig.Title");

    /// <summary>
    /// 分类页签集合。
    /// </summary>
    public ObservableCollection<KspConfigCategoryTabViewModel> Tabs { get; } = [];

    [ObservableProperty]
    private KspConfigCategoryTabViewModel? _selectedTab;

    partial void OnSelectedTabChanged(KspConfigCategoryTabViewModel? value)
    {
        ApplyFilter();
    }

    /// <summary>
    /// 经过分类和搜索过滤后的配置项集合。
    /// </summary>
    public ObservableCollection<KspSettingItemViewModel> FilteredItems { get; } = [];

    [ObservableProperty]
    private string _searchText = string.Empty;

    partial void OnSearchTextChanged(string value)
    {
        ApplyFilter();
    }

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    [ObservableProperty]
    private string _filePath = string.Empty;

    [ObservableProperty]
    private string _language = "zh-cn";

    [ObservableProperty]
    private bool _hasBackup;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasUnsavedChanges))]
    private int _modifiedCount;

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
        StatusMessage = Text("正在读取 KSP 原生配置与官方本地化字典...", "Pages.KspConfig.Loading");

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
                _allItems.Add(new KspSettingItemViewModel(item, OnItemModifiedStateChanged));
            }

            UpdateTabCounts();
            ApplyFilter();
            UpdateModifiedCount();

            StatusMessage = Text(
                $"已加载 {_allItems.Count} 项配置（官方词条 {_document.LocalizationTable.Count} 条）",
                "Pages.KspConfig.LoadedSummary",
                _allItems.Count,
                _document.LocalizationTable.Count);
        }
        catch (Exception ex)
        {
            StatusMessage = Text("加载配置失败: {0}", "Pages.KspConfig.LoadFailed", ex.Message);
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
            StatusMessage = Text("没有待保存的修改项。", "Pages.KspConfig.NoModifications");
            return;
        }

        IsLoading = true;
        StatusMessage = Text("正在保存 {0} 项设置并生成备份...", "Pages.KspConfig.Saving", modified.Count);

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

                StatusMessage = Text("成功保存 {0} 项设置到 settings.cfg，并已创建 .bak 备份。", "Pages.KspConfig.SaveSuccess", modified.Count);
            }
            else
            {
                StatusMessage = Text("保存设置失败，请查看日志获取详细信息。", "Pages.KspConfig.SaveFailed");
            }
        }
        catch (Exception ex)
        {
            StatusMessage = Text("保存设置失败: {0}", "Pages.KspConfig.SaveError", ex.Message);
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
        StatusMessage = Text("已撤回所有修改。", "Pages.KspConfig.Discarded");
    }

    /// <summary>
    /// 从 settings.cfg.bak 恢复备份。
    /// </summary>
    [RelayCommand]
    public async Task RestoreBackupAsync()
    {
        if (IsLoading) return;

        IsLoading = true;
        StatusMessage = Text("正在从 settings.cfg.bak 恢复...", "Pages.KspConfig.Restoring");

        try
        {
            var gameDir = _instanceService?.Current?.GameDir;
            var success = await _configService.RestoreBackupAsync(gameDir).ConfigureAwait(true);
            if (success)
            {
                await LoadConfigAsync().ConfigureAwait(true);
                StatusMessage = Text("已成功从 .bak 备份恢复 settings.cfg！", "Pages.KspConfig.RestoreSuccess");
            }
            else
            {
                StatusMessage = Text("未找到 settings.cfg.bak 备份文件或恢复失败。", "Pages.KspConfig.RestoreFailed");
            }
        }
        catch (Exception ex)
        {
            StatusMessage = Text("恢复备份失败: {0}", "Pages.KspConfig.RestoreError", ex.Message);
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
        Tabs.Add(new("全部", null, "All"));
        Tabs.Add(new("图像画质", KspSettingCategory.Graphics, "Display"));
        Tabs.Add(new("声音音效", KspSettingCategory.Audio, "Volume"));
        Tabs.Add(new("游戏常规", KspSettingCategory.Gameplay, "Gamepad"));
        Tabs.Add(new("高级调试", KspSettingCategory.Advanced, "Wrench"));
        Tabs.Add(new("其它项", KspSettingCategory.Other, "Settings"));

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
    }

    private void OnItemModifiedStateChanged()
    {
        UpdateModifiedCount();
    }

    private void UpdateModifiedCount()
    {
        ModifiedCount = _allItems.Count(i => i.IsModified);
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
