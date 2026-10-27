using System.ComponentModel;
using System.Globalization;
using CkanUI.Services.Abstractions;
using CkanUI.Services.Abstractions.Dtos;
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
    private readonly IInstanceService? _instances;
    private readonly AuthTokenEditorViewModel _tokenEditor;
    private readonly SettingsDiagnosticsViewModel _diagnostics;
    private readonly StabilityToleranceFile _stability = new();
    private readonly List<SettingItemViewModel> _items = new();
    private readonly List<SettingGroupViewModel> _groups = new();
    private ISettingsShellLauncher? _shellLauncher;

    private SettingItemViewModel? _autoStartItem;
    private SettingItemViewModel? _cacheDirItem;
    private SettingItemViewModel? _stabilityItem;

    /// <summary>创建设置页视图模型。</summary>
    /// <param name="settings">设置服务（Core 配置的唯一出口）。</param>
    /// <param name="uiSettings">UI 自有设置存储（外观类设置）。</param>
    /// <param name="instances">实例服务：供「自动打开的实例」下拉与实例级容差文件定位；为空时这两项退化为只读/不可用。</param>
    /// <param name="copyTextAsync">写剪贴板的宿主委托（DI 已注册 <c>Func&lt;string, Task&gt;</c>）；为空时复制按钮给出无通道提示。</param>
    public SettingsPageViewModel(
        ISettingsService settings,
        IUiSettingsStore uiSettings,
        IInstanceService? instances = null,
        Func<string, Task>? copyTextAsync = null)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(uiSettings);

        _settings = settings;
        _uiSettings = uiSettings;
        _instances = instances;
        _tokenEditor = new AuthTokenEditorViewModel(settings);
        _tokenEditor.PropertyChanged += OnTokenEditorPropertyChanged;
        _diagnostics = new SettingsDiagnosticsViewModel(copyTextAsync);
        OpenCacheFolderCommand = new AsyncRelayCommand(OpenCacheFolderAsync);
        OpenConfigFolderCommand = new AsyncRelayCommand(OpenConfigFolderAsync);

        BuildGroups();

        foreach (var item in _items)
        {
            item.PropertyChanged += OnItemPropertyChanged;
        }
    }

    /// <inheritdoc />
    public override string Title => "设置";

    /// <summary>八个设置分组（顺序即界面顺序）。</summary>
    public IReadOnlyList<SettingGroupViewModel> Groups => _groups;

    /// <summary>全部标量设置项（拍平后便于遍历；数量级很小，直接现算）。</summary>
    public IReadOnlyList<SettingItemViewModel> Items => _items;

    /// <summary>认证令牌组的编辑器。</summary>
    public AuthTokenEditorViewModel TokenEditor => _tokenEditor;

    /// <summary>高级组里的诊断区。</summary>
    public SettingsDiagnosticsViewModel Diagnostics => _diagnostics;

    /// <summary>
    /// 打开目录的宿主能力：由视图在 DataContext 就绪后注入（与 <c>InstancesPageView</c> 同一套先例），
    /// VM 因此不引用 Avalonia；未注入时按钮给出「没有通道」提示而不是崩。
    /// </summary>
    public ISettingsShellLauncher? ShellLauncher
    {
        get => _shellLauncher;
        set
        {
            if (ReferenceEquals(_shellLauncher, value))
            {
                return;
            }

            _shellLauncher = value;
            OnPropertyChanged();
        }
    }

    /// <summary>打开下载缓存目录。</summary>
    public IAsyncRelayCommand OpenCacheFolderCommand { get; }

    /// <summary>打开配置目录（<c>%LocalAppData%/CKAN</c>）。</summary>
    public IAsyncRelayCommand OpenConfigFolderCommand { get; }

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
            ApplyInstanceState(all.Value);
            await LoadStabilityToleranceAsync(ct).ConfigureAwait(true);
            _diagnostics.Update(
                _instances?.Current,
                SettingsPaths.DefaultConfigDirectory,
                _cacheDirItem?.EditValue);
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

                case SettingPersistence.InstanceFile:
                    await SaveToInstanceFileAsync(item, ct).ConfigureAwait(true);
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

        _autoStartItem = new SettingItemViewModel(
            SettingsKeyNames.AutoStartInstance,
            "自动打开的实例",
            SettingItemKind.Choice,
            SettingPersistence.Core,
            SettingGroupKey.Instance,
            description: "从已登记的实例里选一个；「不自动打开」表示启动后需要手动选实例。",
            requiresRestart: true,
            choices: new[] { new SettingChoice(string.Empty, "不自动打开") },
            saveHandler: vm => SaveItemAsync(vm));

        var instance = Group(
            SettingGroupKey.Instance,
            "实例",
            "启动该界面时自动打开的实例。",
            _autoStartItem);

        _cacheDirItem = new SettingItemViewModel(
            SettingsKeyNames.DownloadCacheDir,
            "缓存目录",
            SettingItemKind.Path,
            SettingPersistence.Core,
            SettingGroupKey.DownloadCache,
            description: "留空表示使用 Core 的默认缓存目录。",
            saveHandler: vm => SaveItemAsync(vm));
        _cacheDirItem.SetAuxiliaryAction("打开目录", OpenCacheFolderCommand);

        var cache = Group(
            SettingGroupKey.DownloadCache,
            "下载缓存",
            "模组压缩包的缓存位置与容量上限（F-50）。",
            _cacheDirItem,
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

        _stabilityItem = new SettingItemViewModel(
            SettingsKeyNames.StabilityTolerance,
            "稳定版容差",
            SettingItemKind.Choice,
            SettingPersistence.InstanceFile,
            SettingGroupKey.Compatibility,
            description: "写入当前实例的 CKAN/stability_tolerance.json，只影响该实例；选「稳定」表示只安装标记为稳定的模组。",
            choices: new[]
            {
                new SettingChoice("stable", "稳定"),
                new SettingChoice("testing", "测试"),
                new SettingChoice("development", "开发"),
            },
            saveHandler: vm => SaveItemAsync(vm));

        var compatibility = Group(
            SettingGroupKey.Compatibility,
            "兼容性",
            "实例级的稳定版容差，以及按游戏生效的全局安装过滤器（F-14/F-16）。",
            _stabilityItem,
            new SettingItemViewModel(
                SettingsKeyNames.GlobalInstallFilters("KSP"),
                "安装过滤器（KSP）",
                SettingItemKind.LineList,
                SettingPersistence.Core,
                SettingGroupKey.Compatibility,
                description: "每行一条过滤器表达式，例如 not:*.zip；留空表示不过滤。",
                saveHandler: vm => SaveItemAsync(vm)),
            new SettingItemViewModel(
                SettingsKeyNames.GlobalInstallFilters("KSP2"),
                "安装过滤器（KSP2）",
                SettingItemKind.LineList,
                SettingPersistence.Core,
                SettingGroupKey.Compatibility,
                description: "每行一条过滤器表达式，例如 not:*.zip；留空表示不过滤。",
                saveHandler: vm => SaveItemAsync(vm)));

        var advancedItems = Array.Empty<SettingItemViewModel>();
        var advanced = new SettingGroupViewModel(
            SettingGroupKey.Advanced,
            "高级与诊断",
            "诊断信息可直接贴进 issue；目录按钮在文件管理器里打开对应位置。",
            advancedItems,
            tokenEditor: null,
            diagnostics: _diagnostics);

        _groups.Clear();
        _groups.Add(general);
        _groups.Add(instance);
        _groups.Add(cache);
        _groups.Add(repository);
        _groups.Add(authToken);
        _groups.Add(appearance);
        _groups.Add(compatibility);
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

    /// <summary>
    /// 用实例列表重建「自动打开的实例」候选，并把已落盘值装回编辑空间。
    /// 已落盘但当前实例列表里不存在的值要保留成一个候选，否则一进设置页就会把用户的配置冲成空值。
    /// </summary>
    private void ApplyInstanceState(IReadOnlyDictionary<string, string?> all)
    {
        if (_autoStartItem is null)
        {
            return;
        }

        all.TryGetValue(SettingsKeyNames.AutoStartInstance, out var saved);
        var value = saved ?? string.Empty;

        var choices = new List<SettingChoice> { new(string.Empty, "不自动打开") };
        var known = value.Length == 0;
        foreach (var instance in EnumerateInstances())
        {
            choices.Add(new SettingChoice(instance.Name, instance.DisplayName));
            known |= string.Equals(instance.Name, value, StringComparison.Ordinal);
        }

        if (!known)
        {
            choices.Add(new SettingChoice(value, string.Create(CultureInfo.InvariantCulture, $"{value}（不在当前实例列表中）")));
        }

        _autoStartItem.SetChoices(choices);
        _autoStartItem.ApplyLoaded(value);
    }

    /// <summary>读取当前实例的稳定版容差；没有实例时该行置灰（它是实例级文件，离开实例无处可读）。</summary>
    private async Task LoadStabilityToleranceAsync(CancellationToken ct)
    {
        if (_stabilityItem is null)
        {
            return;
        }

        var instance = _instances?.Current;
        if (instance is null)
        {
            _stabilityItem.IsEnabled = false;
            _stabilityItem.ApplyLoaded(StabilityToleranceFile.DefaultTolerance);
            return;
        }

        _stabilityItem.IsEnabled = true;
        var path = StabilityToleranceFile.PathFor(instance.GameDir);
        var result = await _stability.ReadAsync(path, ct).ConfigureAwait(true);
        _stabilityItem.ApplyLoaded(StabilityToleranceFile.NormalizeTolerance(result.IsSuccess ? result.Value : null));
        if (!result.IsSuccess)
        {
            _stabilityItem.IsEnabled = false;
            _stabilityItem.MarkFailed(result.Message ?? "读取稳定版容差失败", result.Detail, result.Retryable);
        }
    }

    /// <summary>写实例级容差文件，并读回校验（与 Core 配置项同一套「改一项写一项 + 读回校验」）。</summary>
    private async Task SaveToInstanceFileAsync(SettingItemViewModel item, CancellationToken ct)
    {
        var instance = _instances?.Current;
        if (instance is null)
        {
            item.MarkFailed("尚未打开实例", "稳定版容差是实例级文件，需要先打开一个实例", retryable: false);
            StatusText = ComposeFailedStatus(item, retryable: false);
            return;
        }

        var path = StabilityToleranceFile.PathFor(instance.GameDir);
        var write = await _stability.WriteAsync(path, item.WireValue, ct).ConfigureAwait(true);
        if (!write.IsSuccess)
        {
            item.MarkFailed(write.Message ?? "保存稳定版容差失败", write.Detail, write.Retryable);
            StatusText = ComposeFailedStatus(item, write.Retryable);
            return;
        }

        var read = await _stability.ReadAsync(path, ct).ConfigureAwait(true);
        if (!read.IsSuccess)
        {
            item.MarkFailed(read.Message ?? "读回稳定版容差失败", read.Detail, read.Retryable);
            StatusText = ComposeFailedStatus(item, read.Retryable);
            return;
        }

        if (!string.Equals(read.Value, item.WireValue, StringComparison.Ordinal))
        {
            item.MarkFailed(
                "写回校验不一致",
                string.Create(CultureInfo.InvariantCulture, $"读出 {read.Value}，期望 {item.WireValue}"),
                retryable: true);
            StatusText = ComposeFailedStatus(item, retryable: true);
            return;
        }

        item.MarkSaved();
        StatusText = ComposeSavedStatus(item);
    }

    private Task OpenCacheFolderAsync() => OpenFolderAsync(_cacheDirItem?.EditValue, "缓存目录");

    private Task OpenConfigFolderAsync() => OpenFolderAsync(SettingsPaths.DefaultConfigDirectory, "配置目录");

    private async Task OpenFolderAsync(string? directory, string what)
    {
        if (_shellLauncher is null)
        {
            StatusText = "当前环境没有打开目录的通道";
            return;
        }

        if (string.IsNullOrWhiteSpace(directory))
        {
            StatusText = string.Create(CultureInfo.InvariantCulture, $"{what}为空：请先填写目录并保存，或使用默认目录");
            return;
        }

        var result = await _shellLauncher.OpenFolderAsync(directory.Trim()).ConfigureAwait(true);
        StatusText = result.IsSuccess
            ? string.Create(CultureInfo.InvariantCulture, $"已打开{what}")
            : string.Create(CultureInfo.InvariantCulture, $"打开{what}失败：{result.Message}");
    }

    private IReadOnlyList<InstanceInfo> EnumerateInstances()
    {
        if (_instances is null)
        {
            return Array.Empty<InstanceInfo>();
        }

        try
        {
            return _instances.Instances;
        }
        catch (Exception)
        {
            // 实例服务在启动早期可能还没读盘；候选退化成「不自动打开」，不阻断整个设置页。
            return Array.Empty<InstanceInfo>();
        }
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
