using System.ComponentModel;
using System.Globalization;
using CkanUI.Services.Abstractions;
using CkanUI.ViewModels.Pages.Settings;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace CkanUI.ViewModels.Pages;

/// <summary>
/// 设置页面视图模型（03 篇 §7 / 04 篇 §2 / 06 篇 UI-07、VM-03）。
/// 七组设置全部逐项保存：改一项写一项，写完读回校验（02 篇 §3），失败在行内给错误文案；
/// 每组都带脏标记，重启后生效的项在行尾标注。
/// 保存去向分两类：标量走 <c>ISettingsService</c>（Core 的 config.json），
/// 外观走 <c>IUiSettingsStore</c>（ckan-ui.json，外观不进 Core 配置，04 篇 §17 约定③）。
/// 构造函数只做装配、不发起任何 IO；读取发生在 <see cref="LoadAsync"/>（由 OnActivated 触发）。
/// </summary>
public sealed partial class SettingsPageViewModel : PageViewModel
{
    private const long BytesPerMebibyte = 1024L * 1024L;

    /// <summary>缓存上限的界面输入上限（MiB）：够大又挡住误输入的夸张值。</summary>
    private const long MaxCacheLimitMebibytes = 1024L * 1024L;

    /// <summary>自动刷新间隔的界面输入上限（分钟）= 一整天。</summary>
    private const long MaxRefreshRateMinutes = 1440L;

    private readonly ISettingsService _settings;
    private readonly IUiSettingsStore _uiSettings;
    private readonly AuthTokenEditorViewModel _tokenEditor;
    private readonly List<SettingItemViewModel> _items = new();
    private readonly List<SettingGroupViewModel> _groups = new();

    /// <summary>创建设置页视图模型。</summary>
    /// <param name="settings">设置服务（Core 配置的唯一出口）。</param>
    /// <param name="uiSettings">UI 自有设置存储（外观类设置）。</param>
    public SettingsPageViewModel(ISettingsService settings, IUiSettingsStore uiSettings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(uiSettings);

        _settings = settings;
        _uiSettings = uiSettings;
        _tokenEditor = new AuthTokenEditorViewModel(settings);
        _tokenEditor.PropertyChanged += OnTokenEditorPropertyChanged;

        BuildGroups();

        foreach (var item in _items)
        {
            item.PropertyChanged += OnItemPropertyChanged;
        }
    }

    /// <inheritdoc />
    public override string Title => "设置";

    /// <summary>七个设置分组（顺序即界面顺序）。</summary>
    public IReadOnlyList<SettingGroupViewModel> Groups => _groups;

    /// <summary>全部标量设置项（拍平后便于遍历；数量级很小，直接现算）。</summary>
    public IReadOnlyList<SettingItemViewModel> Items => _items;

    /// <summary>认证令牌组的编辑器。</summary>
    public AuthTokenEditorViewModel TokenEditor => _tokenEditor;

    /// <summary>是否正在读取设置。</summary>
    [ObservableProperty]
    private bool _isLoading;

    /// <summary>页面上是否还有未保存的修改（任一设置项或令牌行脏）。</summary>
    [ObservableProperty]
    private bool _isDirty;

    /// <summary>状态栏文案。</summary>
    [ObservableProperty]
    private string _statusText = "尚未读取设置";

    /// <summary>读取失败的文案。</summary>
    [ObservableProperty]
    private string? _loadErrorText;

    /// <summary>当前是否有可重试的失败项。</summary>
    public bool HasRetryableFailures
    {
        get
        {
            foreach (var item in _items)
            {
                if (item.IsRetryable)
                {
                    return true;
                }
            }

            return _tokenEditor.HasRetryableFailures;
        }
    }

    /// <summary>是否有读取错误。</summary>
    public bool HasLoadError => !string.IsNullOrEmpty(LoadErrorText);

    /// <summary>重新读取全部设置（放弃未保存的修改）。</summary>
    [RelayCommand]
    private Task ReloadAsync(CancellationToken ct) => LoadAsync(ct);

    /// <summary>放弃全部未保存修改（回到已落盘的值）。</summary>
    [RelayCommand]
    private void Discard()
    {
        foreach (var item in _items)
        {
            item.Revert();
        }

        _tokenEditor.Discard();
        UpdateDirtyState();
        StatusText = "已放弃未保存的修改";
    }

    /// <summary>重试所有可重试的失败项（永久失败不会出现在这里，Retryable 语义见 01 篇 §8）。</summary>
    [RelayCommand]
    private async Task RetryFailedAsync(CancellationToken ct)
    {
        var retried = 0;
        foreach (var item in _items.ToArray())
        {
            if (!item.IsRetryable)
            {
                continue;
            }

            retried++;
            await SaveItemAsync(item, ct).ConfigureAwait(true);
        }

        if (_tokenEditor.HasRetryableFailures)
        {
            retried++;
            await _tokenEditor.RetryFailedAsync().ConfigureAwait(true);
        }

        StatusText = retried == 0 ? "没有可重试的失败项" : string.Create(CultureInfo.InvariantCulture, $"已重试 {retried} 项");
        UpdateDirtyState();
    }

    /// <summary>从服务读取全部设置并填充各组（分组读取，06 篇 UI-07）。</summary>
    /// <param name="ct">取消标记。</param>
    public async Task LoadAsync(CancellationToken ct = default)
    {
        IsLoading = true;
        LoadErrorText = null;
        OnPropertyChanged(nameof(HasLoadError));

        try
        {
            var all = await _settings.GetAllAsync(ct).ConfigureAwait(true);
            if (!all.IsSuccess || all.Value is null)
            {
                SetLoadError(all.Message ?? "读取设置失败", all.Detail);
                return;
            }

            ApplyCoreValues(all.Value);
            ApplyUiSettings();
            _tokenEditor.LoadFrom(all.Value);
            UpdateDirtyState();

            if (!HasLoadError)
            {
                StatusText = string.Create(CultureInfo.InvariantCulture, $"已读取 {_items.Count} 项设置、{_tokenEditor.Tokens.Count} 条令牌");
            }
        }
        catch (Exception ex)
        {
            // 服务契约不抛异常；真抛了也不能穿透到 UI 线程（01 篇 §8 异常收口）。
            SetLoadError("读取设置失败", ex.Message);
        }
        finally
        {
            IsLoading = false;
        }
    }

    /// <summary>保存单个设置项（逐项保存：一次只写一个键）。</summary>
    /// <param name="item">目标设置项。</param>
    /// <param name="ct">取消标记。</param>
    public async Task SaveItemAsync(SettingItemViewModel? item, CancellationToken ct = default)
    {
        if (item is null)
        {
            return;
        }

        if (item.Validate())
        {
            StatusText = $"{item.DisplayName}：请先修正输入";
            UpdateDirtyState();
            return;
        }

        if (!item.IsDirty)
        {
            StatusText = $"{item.DisplayName}：没有修改";
            return;
        }

        item.IsSaving = true;
        try
        {
            switch (item.Persistence)
            {
                case SettingPersistence.Core:
                    var result = await _settings.SetAndVerifyAsync(item.Key, item.WireValue, ct).ConfigureAwait(true);
                    if (result.IsSuccess)
                    {
                        item.MarkSaved();
                        StatusText = ComposeSavedStatus(item);
                    }
                    else
                    {
                        item.MarkFailed(result.Message ?? "保存失败", result.Detail, result.Retryable);
                        StatusText = ComposeFailedStatus(item, result.Retryable);
                    }

                    break;

                case SettingPersistence.UiSettings:
                    SaveToUiSettings(item);
                    break;

                default:
                    item.MarkFailed("未知的保存去向", null, retryable: false);
                    StatusText = $"{item.DisplayName}：保存失败";
                    break;
            }
        }
        catch (Exception ex)
        {
            item.MarkFailed("保存失败", ex.Message, retryable: true);
            StatusText = ComposeFailedStatus(item, retryable: true);
        }
        finally
        {
            item.IsSaving = false;
            UpdateDirtyState();
        }
    }

    /// <inheritdoc />
    protected override void OnActivated()
    {
        base.OnActivated();
        _ = LoadAsync();
    }

    private void BuildGroups()
    {
        var general = Group(
            SettingGroupKey.General,
            "通用",
            "与具体实例无关的行为。",
            new SettingItemViewModel(
                SettingsKeyNames.DevBuilds,
                "开发版更新通道",
                SettingItemKind.Boolean,
                SettingPersistence.Core,
                SettingGroupKey.General,
                description: "开启后检查更新时包含 dev 构建（F-52）；关闭则只跟踪正式版。",
                saveHandler: vm => SaveItemAsync(vm)));

        var instance = Group(
            SettingGroupKey.Instance,
            "实例",
            "启动该界面时自动打开的实例。",
            new SettingItemViewModel(
                SettingsKeyNames.AutoStartInstance,
                "自动打开的实例",
                SettingItemKind.Text,
                SettingPersistence.Core,
                SettingGroupKey.Instance,
                description: "填实例名（如 Default）；留空表示启动后不自动打开任何实例。",
                requiresRestart: true,
                saveHandler: vm => SaveItemAsync(vm)));

        var cache = Group(
            SettingGroupKey.DownloadCache,
            "下载缓存",
            "模组压缩包的缓存位置与容量上限（F-50）。",
            new SettingItemViewModel(
                SettingsKeyNames.DownloadCacheDir,
                "缓存目录",
                SettingItemKind.Path,
                SettingPersistence.Core,
                SettingGroupKey.DownloadCache,
                description: "留空表示使用 Core 的默认缓存目录。",
                saveHandler: vm => SaveItemAsync(vm)),
            new SettingItemViewModel(
                SettingsKeyNames.CacheSizeLimit,
                "缓存容量上限（MiB）",
                SettingItemKind.Integer,
                SettingPersistence.Core,
                SettingGroupKey.DownloadCache,
                description: "超过上限时由缓存管理执行「清理到上限」；留空表示不限。",
                requiresRestart: true,
                min: 0,
                max: MaxCacheLimitMebibytes,
                toWire: MebibytesToBytes,
                fromWire: BytesToMebibytes,
                saveHandler: vm => SaveItemAsync(vm)));

        var repository = Group(
            SettingGroupKey.Repository,
            "仓库与网络",
            "仓库元数据的自动刷新节奏与主机优先级（F-15）。",
            new SettingItemViewModel(
                SettingsKeyNames.RefreshRate,
                "自动刷新间隔（分钟）",
                SettingItemKind.Integer,
                SettingPersistence.Core,
                SettingGroupKey.Repository,
                description: "0 表示不自动刷新；留空表示使用默认值。",
                min: 0,
                max: MaxRefreshRateMinutes,
                saveHandler: vm => SaveItemAsync(vm)),
            new SettingItemViewModel(
                SettingsKeyNames.PreferredHosts,
                "首选主机顺序",
                SettingItemKind.LineList,
                SettingPersistence.Core,
                SettingGroupKey.Repository,
                description: "每行一个主机名，自上而下优先；空行表示「其余主机」的位置。",
                saveHandler: vm => SaveItemAsync(vm)));

        var authToken = new SettingGroupViewModel(
            SettingGroupKey.AuthToken,
            "认证令牌",
            "按主机配置访问令牌；界面只显示脱敏预览，明文不写日志（F-11）。",
            Array.Empty<SettingItemViewModel>(),
            _tokenEditor);

        var appearance = Group(
            SettingGroupKey.Appearance,
            "外观",
            "界面主题、语言与密度；只写 ckan-ui.json，不进 Core 配置。",
            new SettingItemViewModel(
                SettingsKeyNames.Theme,
                "主题",
                SettingItemKind.Choice,
                SettingPersistence.UiSettings,
                SettingGroupKey.Appearance,
                description: "高对比度主题在系统开启高对比度时会被强制使用（P-01）。",
                choices: new[]
                {
                    new SettingChoice("system", "跟随系统"),
                    new SettingChoice("light", "浅色"),
                    new SettingChoice("dark", "深色"),
                    new SettingChoice("highcontrast", "高对比度"),
                }),
            new SettingItemViewModel(
                SettingsKeyNames.Locale,
                "界面语言",
                SettingItemKind.Choice,
                SettingPersistence.UiSettings,
                SettingGroupKey.Appearance,
                description: "语言切换需要重启界面后生效（F-53）。",
                requiresRestart: true,
                choices: new[]
                {
                    new SettingChoice(string.Empty, "跟随系统"),
                    new SettingChoice("zh-CN", "简体中文"),
                    new SettingChoice("zh-TW", "繁體中文"),
                    new SettingChoice("en-US", "English"),
                    new SettingChoice("ja-JP", "日本語"),
                    new SettingChoice("de-DE", "Deutsch"),
                }),
            new SettingItemViewModel(
                SettingsKeyNames.Density,
                "界面密度",
                SettingItemKind.Choice,
                SettingPersistence.UiSettings,
                SettingGroupKey.Appearance,
                description: "紧凑/标准/舒适三档，影响行高与内边距（P-08）；保存后即时生效。",
                choices: new[]
                {
                    new SettingChoice("compact", "紧凑"),
                    new SettingChoice("standard", "标准"),
                    new SettingChoice("comfortable", "舒适"),
                }));

        var advanced = Group(
            SettingGroupKey.Advanced,
            "高级",
            "直接编辑 Core 的原始配置值，格式不对会在行内报错并拒绝写入。",
            new SettingItemViewModel(
                SettingsKeyNames.GlobalInstallFilters("KSP"),
                "安装过滤器（KSP）",
                SettingItemKind.LineList,
                SettingPersistence.Core,
                SettingGroupKey.Advanced,
                description: "每行一条过滤器表达式，例如 not:*.zip；留空表示不过滤。",
                saveHandler: vm => SaveItemAsync(vm)),
            new SettingItemViewModel(
                SettingsKeyNames.GlobalInstallFilters("KSP2"),
                "安装过滤器（KSP2）",
                SettingItemKind.LineList,
                SettingPersistence.Core,
                SettingGroupKey.Advanced,
                description: "每行一条过滤器表达式，例如 not:*.zip；留空表示不过滤。",
                saveHandler: vm => SaveItemAsync(vm)));

        _groups.Clear();
        _groups.Add(general);
        _groups.Add(instance);
        _groups.Add(cache);
        _groups.Add(repository);
        _groups.Add(authToken);
        _groups.Add(appearance);
        _groups.Add(advanced);
    }

    private SettingGroupViewModel Group(
        SettingGroupKey key,
        string title,
        string description,
        params SettingItemViewModel[] items)
    {
        _items.AddRange(items);
        return new SettingGroupViewModel(key, title, description, items);
    }

    private void ApplyCoreValues(IReadOnlyDictionary<string, string?> all)
    {
        foreach (var item in _items)
        {
            if (item.Persistence != SettingPersistence.Core)
            {
                continue;
            }

            all.TryGetValue(item.Key, out var value);
            item.Load(value);
        }
    }

    private void ApplyUiSettings()
    {
        UiSettings current;
        try
        {
            current = _uiSettings.Load();
        }
        catch (Exception ex)
        {
            SetLoadError("读取界面设置失败", ex.Message);
            return;
        }

        ApplyChoice(SettingsKeyNames.Theme, current.Theme, "dark");
        ApplyChoice(SettingsKeyNames.Locale, current.Locale, string.Empty);
        ApplyChoice(SettingsKeyNames.Density, current.Density, "standard");
    }

    private void ApplyChoice(string key, string? value, string fallback)
    {
        var item = FindItem(key);
        if (item is null)
        {
            return;
        }

        var candidate = value ?? string.Empty;
        var known = false;
        foreach (var choice in item.Choices)
        {
            if (string.Equals(choice.Value, candidate, StringComparison.Ordinal))
            {
                known = true;
                break;
            }
        }

        item.ApplyLoaded(known ? candidate : fallback);
    }

    private void SaveToUiSettings(SettingItemViewModel item)
    {
        var current = _uiSettings.Current;
        UiSettings next;
        switch (item.Key)
        {
            case SettingsKeyNames.Theme:
                next = current with { Theme = item.EditValue };
                break;

            case SettingsKeyNames.Locale:
                next = current with { Locale = item.EditValue.Length == 0 ? null : item.EditValue };
                break;

            case SettingsKeyNames.Density:
                next = current with { Density = item.EditValue };
                break;

            default:
                item.MarkFailed($"未知的界面设置项：{item.Key}", null, retryable: false);
                StatusText = $"{item.DisplayName}：保存失败";
                return;
        }

        var result = _uiSettings.Save(next);
        if (result.IsSuccess)
        {
            item.MarkSaved();
            StatusText = ComposeSavedStatus(item);
            return;
        }

        item.MarkFailed(result.Message ?? "保存界面设置失败", result.Detail, result.Retryable);
        StatusText = ComposeFailedStatus(item, result.Retryable);
    }

    private SettingItemViewModel? FindItem(string key)
    {
        foreach (var item in _items)
        {
            if (string.Equals(item.Key, key, StringComparison.Ordinal))
            {
                return item;
            }
        }

        return null;
    }

    private void OnItemPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(SettingItemViewModel.IsDirty) or nameof(SettingItemViewModel.IsRetryable))
        {
            UpdateDirtyState();
        }
    }

    private void OnTokenEditorPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(AuthTokenEditorViewModel.HasChanges) or nameof(AuthTokenEditorViewModel.HasRetryableFailures))
        {
            UpdateDirtyState();
        }
    }

    private void UpdateDirtyState()
    {
        var dirty = _tokenEditor.HasChanges;
        if (!dirty)
        {
            foreach (var item in _items)
            {
                if (item.IsDirty)
                {
                    dirty = true;
                    break;
                }
            }
        }

        IsDirty = dirty;
        OnPropertyChanged(nameof(HasRetryableFailures));
    }

    private void SetLoadError(string message, string? detail)
    {
        LoadErrorText = string.IsNullOrEmpty(detail) ? message : $"{message}（{detail}）";
        StatusText = "读取设置失败";
        OnPropertyChanged(nameof(HasLoadError));
    }

    private static string ComposeSavedStatus(SettingItemViewModel item) =>
        item.RequiresRestart ? $"{item.DisplayName}：已保存，重启后生效" : $"{item.DisplayName}：已保存";

    private static string ComposeFailedStatus(SettingItemViewModel item, bool retryable) =>
        retryable ? $"{item.DisplayName}：保存失败，可重试" : $"{item.DisplayName}：保存失败";

    /// <summary>MiB 文本 → 字节文本（Core 的 CacheSizeLimit 以字节存储）。空值保持为空（= 不限）。</summary>
    private static string MebibytesToBytes(string editValue)
    {
        if (!long.TryParse(editValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out var mebibytes))
        {
            return string.Empty;
        }

        var bytes = mebibytes * BytesPerMebibyte;
        return bytes.ToString(CultureInfo.InvariantCulture);
    }

    /// <summary>字节文本 → MiB 文本（列表/配置里读回时用）。</summary>
    private static string BytesToMebibytes(string? wireValue)
    {
        if (string.IsNullOrWhiteSpace(wireValue) ||
            !long.TryParse(wireValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out var bytes))
        {
            return string.Empty;
        }

        return (bytes / BytesPerMebibyte).ToString(CultureInfo.InvariantCulture);
    }
}
