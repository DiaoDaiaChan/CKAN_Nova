using System.ComponentModel;
using System.Globalization;
using CkanUI.Services.Abstractions;
using CkanUI.Services.Abstractions.Dtos;
using CkanUI.Services.Abstractions.Update;
using CkanUI.ViewModels.Dialogs;
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
public sealed partial class SettingsPageViewModel : PageViewModel, IUnsavedChangesAware
{
    private const long BytesPerMebibyte = 1024L * 1024L;

    /// <summary>缓存上限的界面输入上限（MiB）：够大又挡住误输入的夸张值。</summary>
    private const long MaxCacheLimitMebibytes = 1024L * 1024L;

    /// <summary>自动刷新间隔的界面输入上限（分钟）= 一整天。</summary>
    private const long MaxRefreshRateMinutes = 1440L;

    private readonly ISettingsService _settings;
    private readonly IUiSettingsStore _uiSettings;
    private readonly IInstanceService? _instances;
    private readonly IUpdateCheckService? _updateCheckService;
    private readonly IUpdateInstaller? _updateInstaller;
    private readonly AuthTokenEditorViewModel _tokenEditor;
    private readonly SettingsDiagnosticsViewModel _diagnostics;
    private readonly StabilityToleranceFile _stability = new();
    private readonly List<SettingItemViewModel> _items = new();
    private readonly List<SettingGroupViewModel> _groups = new();
    private ISettingsShellLauncher? _shellLauncher;

    private SettingItemViewModel? _devBuildsItem;
    private SettingItemViewModel? _autoStartItem;
    private SettingItemViewModel? _cacheDirItem;
    private SettingItemViewModel? _stabilityItem;

    /// <summary>弹窗呈现委托，解耦视图层。</summary>
    public Func<UpdateAvailableDialogViewModel, Task>? ShowUpdateDialogAsync { get; set; }

    /// <summary>打开外部链接的宿主委托（更新弹窗里的「打开发布页」）；为空时弹窗只提示手动访问。</summary>
    public Func<string, Task<bool>>? OpenExternalUrlAsync { get; set; }

    /// <summary>
    /// 界面语言候选的提供器（组装根按「实际发过包」的语种注入）。
    /// 为什么走委托：候选来自本地化层，而 VM 不引用本地化层；
    /// 若这里硬编码一堆没发过包的语种，用户选完重启界面不会有任何变化，属于「设置了却没效果」的假 UI。
    /// </summary>
    public Func<IReadOnlyList<(string Code, string Display)>>? LocaleChoicesProvider { get; set; }

    /// <summary>创建设置页视图模型。</summary>
    /// <param name="settings">设置服务（Core 配置的唯一出口）。</param>
    /// <param name="uiSettings">UI 自有设置存储（外观类设置）。</param>
    /// <param name="instances">实例服务：供「自动打开的实例」下拉与实例级容差文件定位；为空时这两项退化为只读/不可用。</param>
    /// <param name="copyTextAsync">写剪贴板的宿主委托（DI 已注册 <c>Func&lt;string, Task&gt;</c>）；为空时复制按钮给出无通道提示。</param>
    /// <param name="updateCheckService">自动更新与新版本检查器（F-52）。</param>
    /// <param name="updateInstaller">更新包安装器（F-52 下载 → 校验 → 备份 → 替换）；为空时更新弹窗只能打开发布页。</param>
    public SettingsPageViewModel(
        ISettingsService settings,
        IUiSettingsStore uiSettings,
        IInstanceService? instances = null,
        Func<string, Task>? copyTextAsync = null,
        IUpdateCheckService? updateCheckService = null,
        IUpdateInstaller? updateInstaller = null)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(uiSettings);

        _settings = settings;
        _uiSettings = uiSettings;
        _instances = instances;
        _updateCheckService = updateCheckService;
        _updateInstaller = updateInstaller;
        _tokenEditor = new AuthTokenEditorViewModel(settings);
        _tokenEditor.PropertyChanged += OnTokenEditorPropertyChanged;
        _diagnostics = new SettingsDiagnosticsViewModel(copyTextAsync);
        OpenCacheFolderCommand = new AsyncRelayCommand(OpenCacheFolderAsync);
        OpenConfigFolderCommand = new AsyncRelayCommand(OpenConfigFolderAsync);
        CheckForUpdatesCommand = new AsyncRelayCommand(CheckForUpdatesAsync);

        BuildGroups();
        // 构造期提供器还没注入，先按随包发布的语种给默认候选；组装根稍后再调一次 RefreshLocaleChoices。
        RefreshLocaleChoices();

        foreach (var item in _items)
        {
            item.PropertyChanged += OnItemPropertyChanged;
        }
    }

    /// <summary>
    /// 重建「界面语言」候选。组装根拿到已发布的语言包后调用；重复调用是幂等的。
    /// </summary>
    public void RefreshLocaleChoices()
    {
        var item = _items.FirstOrDefault(candidate =>
            string.Equals(candidate.Key, SettingsKeyNames.Locale, StringComparison.Ordinal));
        item?.SetChoices(BuildLocaleChoices(LocaleChoicesProvider?.Invoke()));
    }

    /// <summary>随包实际发布的语种：没有语言包就不列，避免出现选了没反应的选项。</summary>
    private static readonly (string Code, string Display)[] ShippedLocales =
    {
        ("zh-CN", "简体中文"),
        ("en-US", "English"),
    };

    /// <summary>构造语言候选：第 0 项固定「跟随系统」（空 code 表示不写 Locale，交给系统区域兜底）。</summary>
    private static IReadOnlyList<SettingChoice> BuildLocaleChoices(
        IReadOnlyList<(string Code, string Display)>? fromProvider)
    {
        var choices = new List<SettingChoice> { new(string.Empty, "跟随系统") };
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { string.Empty };

        // 提供器可能给出空列表或重复项：退回到随包语种，并逐个去重、跳过空 code。
        var source = fromProvider is { Count: > 0 } ? fromProvider : ShippedLocales;
        foreach (var (code, display) in source)
        {
            if (string.IsNullOrWhiteSpace(code) || !seen.Add(code))
            {
                continue;
            }

            choices.Add(new SettingChoice(code, string.IsNullOrWhiteSpace(display) ? code : display));
        }

        return choices;
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

    /// <summary>
    /// 界面当前是否深色：强调色对比度门禁需要知道「跟随系统」时到底落在哪一档（20 篇 P-03）。
    /// 由视图注入（<c>SettingsPageView</c> 读 <c>ActualThemeVariant</c>），VM 因此不引用 Avalonia；
    /// 未注入时按深色处理，与 <see cref="UiSettings.Theme"/> 的默认值一致。
    /// </summary>
    public Func<bool>? DarkThemeProbe { get; set; }

    /// <summary>
    /// 系统是否开启高对比度（20 篇 P-02 / THM-10）。开启时外观组由系统接管：
    /// 主题与强调色行置灰，保存会被拒绝——因为 <c>ThemeHost</c> 会强制盖回高对比度变体，写了也不生效。
    /// 由视图注入，VM 因此不引用 Avalonia；未注入视为未开启。
    /// </summary>
    public Func<bool>? HighContrastProbe { get; set; }

    /// <summary>外观是否被系统高对比度接管（置灰 + 提示文案的开关）。</summary>
    [ObservableProperty]
    private bool _isAppearanceLocked;

    /// <summary>接管提示文案；未被接管时为空。</summary>
    public string AppearanceLockNotice => IsAppearanceLocked
        ? "系统已开启高对比度：主题、强调色与背景由系统接管，这里的改动不会生效。"
        : string.Empty;

    /// <summary>是否显示接管提示。</summary>
    public bool HasAppearanceLockNotice => IsAppearanceLocked;

    /// <summary>打开下载缓存目录。</summary>
    public IAsyncRelayCommand OpenCacheFolderCommand { get; }

    /// <summary>打开配置目录（<c>%LocalAppData%/CKAN</c>）。</summary>
    public IAsyncRelayCommand OpenConfigFolderCommand { get; }

    /// <summary>立即检查更新（F-52）。</summary>
    public IAsyncRelayCommand CheckForUpdatesCommand { get; }

    /// <summary>是否正在读取设置。</summary>
    [ObservableProperty]
    private bool _isLoading;

    /// <summary>页面上是否还有未保存的修改（任一设置项或令牌行脏）。</summary>
    [ObservableProperty]
    private bool _isDirty;

    /// <summary>外壳底部「未保存」提示条：设置项或令牌行还有没落盘的值。</summary>
    bool IUnsavedChangesAware.HasUnsavedChanges => IsDirty;

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

    /// <summary>
    /// 重新评估「系统高对比度是否接管外观」，并把主题与强调色两行连同行内提示一起更新。
    /// 视图在装载与系统配色变化时各调一次（<c>SettingsPageView</c> 订阅 <c>ColorValuesChanged</c>）。
    /// </summary>
    public void RefreshAppearanceLock()
    {
        var locked = HighContrastProbe?.Invoke() ?? false;
        IsAppearanceLocked = locked;
        foreach (var key in AppearanceKeys)
        {
            var item = FindItem(key);
            if (item is not null)
            {
                item.IsEnabled = !locked;
            }
        }

        OnPropertyChanged(nameof(AppearanceLockNotice));
        OnPropertyChanged(nameof(HasAppearanceLockNotice));
    }

    /// <summary>要跟着高对比度一起置灰的行：主题、强调色预设、自定义强调色、强调色作用范围，以及背景那十一行。</summary>
    /// <remarks>
    /// 背景同属外观：高对比度下 <c>BackgroundHost</c> 整层不画（THM-10），写进去也看不到，
    /// 所以和主题、强调色一样置灰并拒绝保存。
    /// </remarks>
    /// <summary>强调色「跟随系统」的档位名，和 <c>AccentPalettes.DefaultKey</c> 是同一个字符串。</summary>
    private const string DefaultAccentKey = "default";

    private static readonly string[] AppearanceKeys =
    [
        SettingsKeyNames.Theme,
        SettingsKeyNames.AccentColor,
        SettingsKeyNames.AccentColorCustom,
        SettingsKeyNames.AccentScope,
        SettingsKeyNames.BackgroundKind,
        SettingsKeyNames.BackgroundColor,
        SettingsKeyNames.BackgroundGradientStart,
        SettingsKeyNames.BackgroundGradientEnd,
        SettingsKeyNames.BackgroundGradientAngle,
        SettingsKeyNames.BackgroundImagePath,
        SettingsKeyNames.BackgroundFill,
        SettingsKeyNames.BackgroundDim,
        SettingsKeyNames.BackgroundBlur,
        SettingsKeyNames.BackgroundOpacity,
        SettingsKeyNames.BackgroundScope,
    ];

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

            // 行是这次读取才建出来的，接管状态要重新贴一遍（不能指望视图的 DataContextChanged 早于 LoadAsync）。
            RefreshAppearanceLock();

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

        if (IsAppearanceLocked && AppearanceKeys.Contains(item.Key, StringComparer.Ordinal))
        {
            // 高对比度期间这两行是只读的：即便命令被绕过（脚本/快捷键），也不能写出一个立刻要回滚的值。
            StatusText = $"{item.DisplayName}：系统高对比度已接管外观";
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
        _devBuildsItem = new SettingItemViewModel(
            SettingsKeyNames.DevBuilds,
            "开发版更新通道",
            SettingItemKind.Boolean,
            SettingPersistence.Core,
            SettingGroupKey.General,
            description: "开启后检查更新时包含 dev 构建（F-52）；关闭则只跟踪正式版。",
            saveHandler: vm => SaveItemAsync(vm));
        _devBuildsItem.SetAuxiliaryAction("立即检查更新", CheckForUpdatesCommand);

        var general = Group(
            SettingGroupKey.General,
            "通用",
            "与具体实例无关的行为。",
            _devBuildsItem);

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
                choices: BuildLocaleChoices(fromProvider: null)),
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
                }),
            new SettingItemViewModel(
                SettingsKeyNames.FontScale,
                "字号缩放",
                SettingItemKind.Choice,
                SettingPersistence.UiSettings,
                SettingGroupKey.Appearance,
                description: "90%–130% 五档，只放大界面文案与标题；模组名、版本号等数据文本保持原样（P-09）。",
                choices: new[]
                {
                    new SettingChoice("90", "90%"),
                    new SettingChoice("100", "100%（默认）"),
                    new SettingChoice("110", "110%"),
                    new SettingChoice("120", "120%"),
                    new SettingChoice("130", "130%"),
                }),
            new SettingItemViewModel(
                SettingsKeyNames.AccentColor,
                "强调色",
                SettingItemKind.Choice,
                SettingPersistence.UiSettings,
                SettingGroupKey.Appearance,
                description: "12 预设色板与系统色彩跟随（P-03）；即时生效。",
                choices: new[]
                {
                    new SettingChoice("default", "跟随系统 / 经典"),
                    new SettingChoice("blue", "经典蓝 / Blue"),
                    new SettingChoice("indigo", "靛青 / Indigo"),
                    new SettingChoice("purple", "星云紫 / Purple"),
                    new SettingChoice("magenta", "品红 / Magenta"),
                    new SettingChoice("red", "火箭红 / Red"),
                    new SettingChoice("orange", "轨道橙 / Orange"),
                    new SettingChoice("amber", "琥珀金 / Amber"),
                    new SettingChoice("lime", "坎星绿 / Lime"),
                    new SettingChoice("green", "极光绿 / Green"),
                    new SettingChoice("teal", "青碧 / Teal"),
                    new SettingChoice("cyan", "天蓝 / Cyan"),
                    new SettingChoice("slate", "灰蓝 / Slate"),
                }),
            new SettingItemViewModel(
                SettingsKeyNames.AccentColorCustom,
                "自定义强调色",
                SettingItemKind.Text,
                SettingPersistence.UiSettings,
                SettingGroupKey.Appearance,
                description: "填 #RRGGBB（例如 #1F6FEB）；保存时按 WCAG AA 校验与底色的对比度（≥4.5:1），不合规会给出建议色并拒绝写入（P-03）。留空表示回到上面的预设。"),
            new SettingItemViewModel(
                SettingsKeyNames.AccentScope,
                "强调色作用范围",
                SettingItemKind.Choice,
                SettingPersistence.UiSettings,
                SettingGroupKey.Appearance,
                description: "全局 = 所有实例共用一个强调色；按实例 = 为当前实例单独记一个色，切换实例时自动跟着换（P-07）。",
                choices: new[]
                {
                    new SettingChoice("global", "全局"),
                    new SettingChoice("instance", "按实例（当前实例）"),
                }),
            new SettingItemViewModel(
                SettingsKeyNames.RadiusScale,
                "圆角尺度",
                SettingItemKind.Choice,
                SettingPersistence.UiSettings,
                SettingGroupKey.Appearance,
                description: "三档圆角比例（P-05）；无圆角/标准微圆角/大饱满圆角。",
                choices: new[]
                {
                    new SettingChoice("none", "直角 (0x)"),
                    new SettingChoice("standard", "标准微圆角 (1x)"),
                    new SettingChoice("round", "饱满圆角 (1.5x)"),
                }),
            new SettingItemViewModel(
                SettingsKeyNames.BackdropKind,
                "窗口材质与透明",
                SettingItemKind.Choice,
                SettingPersistence.UiSettings,
                SettingGroupKey.Appearance,
                description: "Windows 磨砂背景材质（P-04）；自动/Mica云母/Acrylic磨砂/纯色。",
                choices: new[]
                {
                    new SettingChoice("auto", "自动推荐 (Mica / Acrylic)"),
                    new SettingChoice("mica", "Mica 云母"),
                    new SettingChoice("acrylic", "Acrylic 亚克力磨砂"),
                    new SettingChoice("none", "纯色无透明"),
                }),
            new SettingItemViewModel(
                SettingsKeyNames.MotionIntensity,
                "动效强度",
                SettingItemKind.Choice,
                SettingPersistence.UiSettings,
                SettingGroupKey.Appearance,
                description: "界面动画与过渡效果强度（P-10）。默认跟随系统「减少动画」设置；精简档只保留 90ms 的悬停/按下反馈。",
                choices: new[]
                {
                    new SettingChoice("auto", "跟随系统 (推荐)"),
                    new SettingChoice("full", "流畅动画"),
                    new SettingChoice("reduced", "减少动画"),
                    new SettingChoice("none", "关闭动画"),
                }),
            new SettingItemViewModel(
                SettingsKeyNames.BackgroundKind,
                "背景来源",
                SettingItemKind.Choice,
                SettingPersistence.UiSettings,
                SettingGroupKey.Appearance,
                description: "窗口空白处的背景（P-06）：纯色 / 渐变 / 本机图片；页面卡片保持不透明以保证文字清晰。系统高对比度下不画背景。",
                choices: new[]
                {
                    new SettingChoice("none", "无（跟随主题）"),
                    new SettingChoice("solid", "纯色"),
                    new SettingChoice("gradient", "渐变"),
                    new SettingChoice("image", "本机图片"),
                },
                saveHandler: vm => SaveItemAsync(vm)),
            new SettingItemViewModel(
                SettingsKeyNames.BackgroundColor,
                "背景色",
                SettingItemKind.Text,
                SettingPersistence.UiSettings,
                SettingGroupKey.Appearance,
                description: "纯色来源的颜色，填 #RRGGBB（例如 #1B1F27）。",
                saveHandler: vm => SaveItemAsync(vm)),
            new SettingItemViewModel(
                SettingsKeyNames.BackgroundGradientStart,
                "渐变起色",
                SettingItemKind.Text,
                SettingPersistence.UiSettings,
                SettingGroupKey.Appearance,
                description: "渐变来源的起始颜色，填 #RRGGBB。",
                saveHandler: vm => SaveItemAsync(vm)),
            new SettingItemViewModel(
                SettingsKeyNames.BackgroundGradientEnd,
                "渐变止色",
                SettingItemKind.Text,
                SettingPersistence.UiSettings,
                SettingGroupKey.Appearance,
                description: "渐变来源的结束颜色，填 #RRGGBB。",
                saveHandler: vm => SaveItemAsync(vm)),
            new SettingItemViewModel(
                SettingsKeyNames.BackgroundGradientAngle,
                "渐变角度",
                SettingItemKind.Integer,
                SettingPersistence.UiSettings,
                SettingGroupKey.Appearance,
                description: "0° 向右、90° 向下、180° 向左；取值 0–359（P-06）。",
                min: 0,
                max: 359,
                saveHandler: vm => SaveItemAsync(vm)),
            new SettingItemViewModel(
                SettingsKeyNames.BackgroundImagePath,
                "背景图片",
                SettingItemKind.Path,
                SettingPersistence.UiSettings,
                SettingGroupKey.Appearance,
                description: "本机图片的绝对路径（PNG/JPG/BMP）；按 1600 像素宽解码，读不出来会退回无背景。",
                saveHandler: vm => SaveItemAsync(vm)),
            new SettingItemViewModel(
                SettingsKeyNames.BackgroundFill,
                "图片填充方式",
                SettingItemKind.Choice,
                SettingPersistence.UiSettings,
                SettingGroupKey.Appearance,
                description: "图片超出窗口时怎么摆（P-06）。",
                choices: new[]
                {
                    new SettingChoice("fill", "填充（等比铺满）"),
                    new SettingChoice("fit", "适应（完整显示）"),
                    new SettingChoice("stretch", "拉伸（不保比例）"),
                    new SettingChoice("tile", "平铺"),
                    new SettingChoice("center", "居中（原尺寸）"),
                },
                saveHandler: vm => SaveItemAsync(vm)),
            new SettingItemViewModel(
                SettingsKeyNames.BackgroundDim,
                "背景暗化",
                SettingItemKind.Integer,
                SettingPersistence.UiSettings,
                SettingGroupKey.Appearance,
                description: "在背景上压一层黑色，百分比越高文字越清楚（0–100）。",
                min: 0,
                max: 100,
                saveHandler: vm => SaveItemAsync(vm)),
            new SettingItemViewModel(
                SettingsKeyNames.BackgroundBlur,
                "背景模糊",
                SettingItemKind.Integer,
                SettingPersistence.UiSettings,
                SettingGroupKey.Appearance,
                description: "模糊半径（0–64 像素）；0 表示不模糊。",
                min: 0,
                max: 64,
                saveHandler: vm => SaveItemAsync(vm)),
            new SettingItemViewModel(
                SettingsKeyNames.BackgroundOpacity,
                "背景不透明度",
                SettingItemKind.Integer,
                SettingPersistence.UiSettings,
                SettingGroupKey.Appearance,
                description: "背景层整体不透明度百分比（0–100）。",
                min: 0,
                max: 100,
                saveHandler: vm => SaveItemAsync(vm)),
            new SettingItemViewModel(
                SettingsKeyNames.BackgroundScope,
                "背景作用范围",
                SettingItemKind.Choice,
                SettingPersistence.UiSettings,
                SettingGroupKey.Appearance,
                description: "「仅当前实例」把整份背景记在 BackgroundByInstance 里（P-07）；切回「全局」会删掉本实例的覆盖。",
                choices: new[]
                {
                    new SettingChoice("global", "全局（所有实例）"),
                    new SettingChoice("instance", "仅当前实例"),
                },
                saveHandler: vm => SaveItemAsync(vm)));

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

        var advancedItems = new[]
        {
            new SettingItemViewModel(
                SettingsKeyNames.HideVersionEpoch,
                "隐藏版本 epoch 前缀",
                SettingItemKind.Boolean,
                SettingPersistence.UiSettings,
                SettingGroupKey.Advanced,
                description: "把 1:2.0.0 显示为 2.0.0（F-54）。",
                saveHandler: vm => SaveItemAsync(vm)),
            new SettingItemViewModel(
                SettingsKeyNames.HideVersionVPrefix,
                "隐藏版本 v 前缀",
                SettingItemKind.Boolean,
                SettingPersistence.UiSettings,
                SettingGroupKey.Advanced,
                description: "把 v2.0.0 显示为 2.0.0（F-54）。",
                saveHandler: vm => SaveItemAsync(vm)),
            new SettingItemViewModel(
                SettingsKeyNames.AutoSortUpdates,
                "可升级项自动置顶",
                SettingItemKind.Boolean,
                SettingPersistence.UiSettings,
                SettingGroupKey.Advanced,
                description: "模组列表默认把有更新的项排在前面（F-54）。",
                saveHandler: vm => SaveItemAsync(vm)),
            new SettingItemViewModel(
                SettingsKeyNames.SuppressRecommendations,
                "不再询问推荐模组",
                SettingItemKind.Boolean,
                SettingPersistence.UiSettings,
                SettingGroupKey.Advanced,
                description: "安装时跳过推荐模组选择框（F-35）；可在推荐框内随时改回来。",
                saveHandler: vm => SaveItemAsync(vm)),
            new SettingItemViewModel(
                SettingsKeyNames.MinimizeToTray,
                "最小化到托盘",
                SettingItemKind.Boolean,
                SettingPersistence.UiSettings,
                SettingGroupKey.Advanced,
                description: "最小化时只留托盘图标，不占任务栏（F-51）。",
                saveHandler: vm => SaveItemAsync(vm)),
            new SettingItemViewModel(
                SettingsKeyNames.CloseToTray,
                "关闭到托盘",
                SettingItemKind.Boolean,
                SettingPersistence.UiSettings,
                SettingGroupKey.Advanced,
                description: "点关闭按钮时隐藏窗口而不退出（F-51）；退出请用托盘菜单的「退出」。",
                saveHandler: vm => SaveItemAsync(vm)),
        };
        _items.AddRange(advancedItems);

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
        ApplyChoice(SettingsKeyNames.FontScale, current.FontScale, "100");
        ApplyAccentColor(current);
        ApplyChoice(SettingsKeyNames.RadiusScale, current.RadiusScale, "standard");
        ApplyChoice(SettingsKeyNames.BackdropKind, current.BackdropKind, "auto");
        ApplyChoice(SettingsKeyNames.MotionIntensity, current.MotionIntensity, "auto");
        ApplyBackgroundRows(current);

        ApplyFlag(SettingsKeyNames.HideVersionEpoch, current.HideVersionEpoch);
        ApplyFlag(SettingsKeyNames.HideVersionVPrefix, current.HideVersionVPrefix);
        ApplyFlag(SettingsKeyNames.AutoSortUpdates, current.AutoSortUpdates);
        ApplyFlag(SettingsKeyNames.SuppressRecommendations, current.SuppressRecommendations);
        ApplyFlag(SettingsKeyNames.MinimizeToTray, current.MinimizeToTray);
        ApplyFlag(SettingsKeyNames.CloseToTray, current.CloseToTray);
    }

    /// <summary>
    /// 强调色的两行共用一个落盘字段：预设行（下拉）与自定义行（#RRGGBB 输入）必须始终指向同一个值。
    /// 历史配置里存的自定义 Hex 需要在下拉里临时合成一个候选，否则一进设置页就会被回填成「跟随系统」。
    /// </summary>
    /// <param name="current">刚读到的界面设置。</param>
    private void ApplyAccentColor(UiSettings current)
    {
        var presetItem = FindItem(SettingsKeyNames.AccentColor);
        if (presetItem is null)
        {
            return;
        }

        // 20 篇 P-07：实例覆盖优先，下拉与自定义框都跟着「当前生效值」走，
        // 否则切了实例还会看见全局色，用户会以为覆盖没写进去。
        var (scope, effective) = ResolveAccent(current);
        ApplyChoice(SettingsKeyNames.AccentScope, scope, "global");

        var customItem = FindItem(SettingsKeyNames.AccentColorCustom);
        var text = effective?.Trim() ?? string.Empty;
        var isPreset = text.Length == 0;
        foreach (var choice in presetItem.Choices)
        {
            if (!choice.Value.StartsWith('#') && string.Equals(choice.Value, text, StringComparison.Ordinal))
            {
                isPreset = true;
                break;
            }
        }

        if (isPreset)
        {
            // 预设档位与自定义 Hex 是同一个字段的两副面孔：回落到预设时要把合成的「自定义（#…）」候选收掉，
            // 否则下拉里会留着一个已经不再生效的旧色（下一次进设置页还会被当成「当前值」）。
            if (presetItem.Choices.Any(choice => choice.Value.StartsWith('#')))
            {
                presetItem.SetChoices(presetItem.Choices.Where(choice => !choice.Value.StartsWith('#')).ToList());
            }

            presetItem.ApplyLoaded(text.Length == 0 ? "default" : text);
            customItem?.ApplyLoaded(string.Empty);
            return;
        }

        var choices = new List<SettingChoice>();
        foreach (var choice in presetItem.Choices)
        {
            if (!choice.Value.StartsWith('#'))
            {
                choices.Add(choice);
            }
        }

        // 插在「跟随系统」后面：这是用户自己定的色，位置固定一次，不要在多次保存之间跳来跳去。
        choices.Insert(1, new SettingChoice(text, $"自定义（{text}）"));
        presetItem.SetChoices(choices);
        presetItem.ApplyLoaded(text);
        customItem?.ApplyLoaded(text);
    }

    /// <summary>
    /// 背景的十一行共用一份 <see cref="BackgroundProfile"/>（20 篇 P-06 / P-07）：整份写盘，而不是
    /// 每行各存一个字段，这样「按实例覆盖」才能一次性搬走整组设置，也不会出现半新半旧的组合。
    /// </summary>
    /// <param name="current">刚读到的界面设置。</param>
    private void ApplyBackgroundRows(UiSettings current)
    {
        var (scope, profile) = ResolveBackgroundProfile(current);
        var clamped = profile.Clamp();

        ApplyChoice(SettingsKeyNames.BackgroundKind, clamped.Kind, BackgroundProfile.KindNone);
        FindItem(SettingsKeyNames.BackgroundColor)?.ApplyLoaded(clamped.Color ?? string.Empty);
        FindItem(SettingsKeyNames.BackgroundGradientStart)?.ApplyLoaded(clamped.GradientStart ?? string.Empty);
        FindItem(SettingsKeyNames.BackgroundGradientEnd)?.ApplyLoaded(clamped.GradientEnd ?? string.Empty);
        FindItem(SettingsKeyNames.BackgroundGradientAngle)?.ApplyLoaded(
            Math.Round(clamped.GradientAngle).ToString("0", CultureInfo.InvariantCulture));
        FindItem(SettingsKeyNames.BackgroundImagePath)?.ApplyLoaded(clamped.ImagePath ?? string.Empty);
        ApplyChoice(SettingsKeyNames.BackgroundFill, clamped.Fill, BackgroundProfile.FillFill);
        FindItem(SettingsKeyNames.BackgroundDim)?.ApplyLoaded(
            clamped.DimPercent.ToString(CultureInfo.InvariantCulture));
        FindItem(SettingsKeyNames.BackgroundBlur)?.ApplyLoaded(
            Math.Round(clamped.BlurRadius).ToString("0", CultureInfo.InvariantCulture));
        FindItem(SettingsKeyNames.BackgroundOpacity)?.ApplyLoaded(
            clamped.OpacityPercent.ToString(CultureInfo.InvariantCulture));
        ApplyChoice(SettingsKeyNames.BackgroundScope, scope, "global");
    }

    /// <summary>
    /// 解析「当前生效」的背景：本实例有覆盖就用覆盖，否则跟随全局（P-07）。
    /// 返回值里的 Scope 直接喂给「作用范围」下拉，保证选项永远与生效值一致。
    /// </summary>
    private (string Scope, BackgroundProfile Profile) ResolveBackgroundProfile(UiSettings current)
    {
        var name = CurrentInstanceName();
        if (name is not null
            && current.BackgroundByInstance.TryGetValue(name, out var perInstance)
            && perInstance is not null)
        {
            return ("instance", perInstance);
        }

        return ("global", current.Background ?? BackgroundProfile.Default);
    }

    /// <summary>当前实例名；实例服务还没读盘时返回 <c>null</c>（背景与强调色都退回全局）。</summary>
    private string? CurrentInstanceName()
    {
        try
        {
            var name = _instances?.Current?.Name;
            return string.IsNullOrWhiteSpace(name) ? null : name;
        }
        catch (Exception)
        {
            return null;
        }
    }

    private bool IsBackgroundInstanceScoped() =>
        string.Equals(FindItem(SettingsKeyNames.BackgroundScope)?.EditValue, "instance", StringComparison.Ordinal);

    /// <summary>
    /// 在「当前生效的整份背景」上改一个字段再写回：作用范围决定写到 <c>Background</c>（全局）
    /// 还是 <c>BackgroundByInstance[当前实例]</c>（P-07）。
    /// </summary>
    private UiSettings SaveBackground(Func<BackgroundProfile, BackgroundProfile> mutate, bool? perInstance = null)
    {
        var current = _uiSettings.Current;
        var (_, profile) = ResolveBackgroundProfile(current);
        var next = mutate(profile.Clamp()).Clamp();

        var name = CurrentInstanceName();
        if ((perInstance ?? IsBackgroundInstanceScoped()) && name is not null)
        {
            var map = new Dictionary<string, BackgroundProfile>(current.BackgroundByInstance, StringComparer.Ordinal)
            {
                [name] = next,
            };
            return current with { BackgroundByInstance = map };
        }

        return current with { Background = next };
    }

    /// <summary>切换作用范围：切到全局会删掉本实例的覆盖（不删的话切回去还是旧值，看上去像没生效）。</summary>
    private UiSettings SaveBackgroundScope(bool perInstance)
    {
        var current = _uiSettings.Current;
        var (_, profile) = ResolveBackgroundProfile(current);
        var name = CurrentInstanceName();

        if (!perInstance)
        {
            var map = new Dictionary<string, BackgroundProfile>(current.BackgroundByInstance, StringComparer.Ordinal);
            if (name is not null)
            {
                map.Remove(name);
            }

            return current with { Background = profile.Clamp(), BackgroundByInstance = map };
        }

        if (name is null)
        {
            return current;
        }

        var scoped = new Dictionary<string, BackgroundProfile>(current.BackgroundByInstance, StringComparer.Ordinal)
        {
            [name] = profile.Clamp(),
        };
        return current with { BackgroundByInstance = scoped };
    }

    /// <summary>
    /// 强调色的生效值（20 篇 P-07）：实例名在 <see cref="UiSettings.AccentByInstance"/> 里有键就用它，
    /// 否则退回全局 <see cref="UiSettings.AccentColor"/>。返回值里的 Scope 直接喂给「作用范围」下拉。
    /// </summary>
    private (string Scope, string? Value) ResolveAccent(UiSettings current)
    {
        var name = CurrentInstanceName();
        if (name is not null
            && current.AccentByInstance.TryGetValue(name, out var perInstance)
            && !string.IsNullOrWhiteSpace(perInstance))
        {
            return ("instance", perInstance);
        }

        return ("global", current.AccentColor);
    }

    private bool IsAccentInstanceScoped() =>
        string.Equals(FindItem(SettingsKeyNames.AccentScope)?.EditValue, "instance", StringComparison.Ordinal);

    /// <summary>把强调色写回当前作用范围：实例作用域写 <c>AccentByInstance[当前实例]</c>，否则写全局。</summary>
    private UiSettings SaveAccent(string? value)
    {
        var current = _uiSettings.Current;
        var name = CurrentInstanceName();
        if (IsAccentInstanceScoped() && name is not null)
        {
            // 空值在全局是「跟随系统」；落到实例键上必须有个名字，否则这条覆盖等于不存在。
            var map = new Dictionary<string, string>(current.AccentByInstance, StringComparer.Ordinal)
            {
                [name] = string.IsNullOrWhiteSpace(value) ? DefaultAccentKey : value,
            };
            return current with { AccentByInstance = map };
        }

        return current with { AccentColor = value };
    }

    /// <summary>切换强调色作用范围：切回全局会删掉本实例的覆盖，并把切走时生效的值搬成新的全局值。</summary>
    private UiSettings SaveAccentScope(bool perInstance)
    {
        var current = _uiSettings.Current;
        var (_, value) = ResolveAccent(current);
        var name = CurrentInstanceName();

        if (!perInstance)
        {
            var map = new Dictionary<string, string>(current.AccentByInstance, StringComparer.Ordinal);
            if (name is not null)
            {
                map.Remove(name);
            }

            return current with { AccentColor = value, AccentByInstance = map };
        }

        if (name is null)
        {
            return current;
        }

        var scoped = new Dictionary<string, string>(current.AccentByInstance, StringComparer.Ordinal)
        {
            [name] = string.IsNullOrWhiteSpace(value) ? DefaultAccentKey : value,
        };
        return current with { AccentByInstance = scoped };
    }

    /// <summary>颜色行校验：复用强调色的 <c>#RRGGBB</c> 解析，不合规的行内给格式提示。</summary>
    private bool TryParseBackgroundColor(SettingItemViewModel item, out string hex)
    {
        if (AccentContrast.TryParse(item.EditValue, out var rgb))
        {
            hex = AccentContrast.ToHex(rgb);
            return true;
        }

        item.MarkFailed("背景色需要 #RRGGBB 形式的十六进制（例如 #1B1F27）。", null, retryable: false);
        StatusText = string.Create(CultureInfo.InvariantCulture, $"{item.DisplayName}：保存失败");
        hex = string.Empty;
        return false;
    }

    private static int ReadInt(string? text, int fallback) =>
        int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) ? value : fallback;

    private static double ReadDouble(string? text, double fallback) =>
        double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) && double.IsFinite(value)
            ? value
            : fallback;

    /// <summary>当前选择（或当前生效）的主题键，用来决定强调色对比度比对哪一档底色。</summary>
    private string? ThemeKeyForContrast()
    {
        var theme = FindItem(SettingsKeyNames.Theme)?.EditValue;
        return string.IsNullOrWhiteSpace(theme) ? null : theme;
    }

    private void ApplyFlag(string key, bool value) =>
        FindItem(key)?.ApplyLoaded(value ? "true" : "false");

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
        var write = await _stability.SaveOverallAsync(instance.GameDir, item.WireValue, ct).ConfigureAwait(true);
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

    private async Task CheckForUpdatesAsync()
    {
        if (_updateCheckService is null)
        {
            StatusText = "未配置更新检查服务";
            return;
        }

        StatusText = "正在检查更新…";
        var includeDevBuilds = _devBuildsItem?.BooleanValue ?? false;
        var result = await _updateCheckService.CheckForUpdatesAsync(includeDevBuilds).ConfigureAwait(true);

        if (!result.IsSuccess || result.Value is null)
        {
            StatusText = string.Create(CultureInfo.InvariantCulture, $"检查更新失败：{result.Message}");
            return;
        }

        var updateInfo = result.Value;
        if (updateInfo.HasUpdate)
        {
            StatusText = string.Create(
                CultureInfo.InvariantCulture,
                $"发现新版本 {updateInfo.LatestVersion}（当前版本：{updateInfo.CurrentVersion}）");

            if (ShowUpdateDialogAsync != null)
            {
                // 安装计划由 UpdateCheckResult 里挑出来的资产推出（见 UpdateCheckService.PickUpdateAsset）。
                var dialogVm = new UpdateAvailableDialogViewModel(updateInfo, OpenExternalUrlAsync);
                // 安装器与安装计划两者都在才会亮「立即安装」；缺任一项弹窗自动降级成「打开发布页」。
                dialogVm.Installer = _updateInstaller;

                await ShowUpdateDialogAsync(dialogVm).ConfigureAwait(true);
            }
        }
        else
        {
            StatusText = string.Create(
                CultureInfo.InvariantCulture,
                $"已是最新版本（{updateInfo.CurrentVersion}）");
        }
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

            case SettingsKeyNames.FontScale:
                next = current with { FontScale = item.EditValue };
                break;

            case SettingsKeyNames.AccentColor:
                next = SaveAccent(item.EditValue);
                break;

            case SettingsKeyNames.AccentColorCustom:
            {
                var text = item.EditValue.Trim();
                if (text.Length == 0)
                {
                    // 留空 = 放弃自定义色。预设行如果还指着一个合成的自定义候选，就没有可回落的档位了，
                    // 这时回到「跟随系统」，而不是把刚被清掉的那个 Hex 又写回去。
                    var preset = FindItem(SettingsKeyNames.AccentColor)?.EditValue;
                    var fallback = string.IsNullOrWhiteSpace(preset) || preset.StartsWith('#') ? "default" : preset;
                    next = SaveAccent(fallback);
                    break;
                }

                var verdict = AccentContrast.Validate(text, ThemeKeyForContrast(), DarkThemeProbe?.Invoke() ?? true);
                if (!verdict.IsValid)
                {
                    item.MarkFailed(verdict.Message, null, retryable: false);
                    StatusText = $"{item.DisplayName}：保存失败";
                    return;
                }

                next = SaveAccent(AccentContrast.ToHex(verdict.Color));
                break;
            }

            case SettingsKeyNames.AccentScope:
            {
                var perInstance = string.Equals(item.EditValue, "instance", StringComparison.Ordinal);
                if (perInstance && CurrentInstanceName() is null)
                {
                    // 没有当前实例就没有可写的键：与其静默写坏一份空覆盖，不如当场说清楚。
                    item.MarkFailed("没有当前实例，无法按实例保存强调色；请先添加或切换实例。", null, retryable: false);
                    StatusText = string.Create(CultureInfo.InvariantCulture, $"{item.DisplayName}：保存失败");
                    return;
                }

                next = SaveAccentScope(perInstance);
                break;
            }

            case SettingsKeyNames.BackgroundKind:
                next = SaveBackground(p => p with { Kind = item.EditValue });
                break;

            case SettingsKeyNames.BackgroundColor:
            {
                if (!TryParseBackgroundColor(item, out var hex))
                {
                    return;
                }

                next = SaveBackground(p => p with { Color = hex });
                break;
            }

            case SettingsKeyNames.BackgroundGradientStart:
            {
                if (!TryParseBackgroundColor(item, out var startHex))
                {
                    return;
                }

                next = SaveBackground(p => p with { GradientStart = startHex });
                break;
            }

            case SettingsKeyNames.BackgroundGradientEnd:
            {
                if (!TryParseBackgroundColor(item, out var endHex))
                {
                    return;
                }

                next = SaveBackground(p => p with { GradientEnd = endHex });
                break;
            }

            case SettingsKeyNames.BackgroundGradientAngle:
                next = SaveBackground(p => p with { GradientAngle = ReadDouble(item.EditValue, p.GradientAngle) });
                break;

            case SettingsKeyNames.BackgroundImagePath:
            {
                var path = item.EditValue.Trim();
                if (path.Length > 0 && !File.Exists(path))
                {
                    // 路径写错时当场拦住：让宿主静默退回无背景，用户只会以为「背景坏了」。
                    item.MarkFailed(string.Create(CultureInfo.InvariantCulture, $"找不到图片文件：{path}"), null, retryable: true);
                    StatusText = string.Create(CultureInfo.InvariantCulture, $"{item.DisplayName}：保存失败");
                    return;
                }

                next = SaveBackground(p => p with { ImagePath = path.Length == 0 ? null : path });
                break;
            }

            case SettingsKeyNames.BackgroundFill:
                next = SaveBackground(p => p with { Fill = item.EditValue });
                break;

            case SettingsKeyNames.BackgroundDim:
                next = SaveBackground(p => p with { DimPercent = ReadInt(item.EditValue, p.DimPercent) });
                break;

            case SettingsKeyNames.BackgroundBlur:
                next = SaveBackground(p => p with { BlurRadius = ReadDouble(item.EditValue, p.BlurRadius) });
                break;

            case SettingsKeyNames.BackgroundOpacity:
                next = SaveBackground(p => p with { OpacityPercent = ReadInt(item.EditValue, p.OpacityPercent) });
                break;

            case SettingsKeyNames.BackgroundScope:
            {
                var perInstance = string.Equals(item.EditValue, "instance", StringComparison.Ordinal);
                if (perInstance && CurrentInstanceName() is null)
                {
                    item.MarkFailed("没有当前实例，无法按实例保存背景；请先添加或切换实例。", null, retryable: false);
                    StatusText = string.Create(CultureInfo.InvariantCulture, $"{item.DisplayName}：保存失败");
                    return;
                }

                next = SaveBackgroundScope(perInstance);
                break;
            }

            case SettingsKeyNames.RadiusScale:
                next = current with { RadiusScale = item.EditValue };
                break;

            case SettingsKeyNames.BackdropKind:
                next = current with { BackdropKind = item.EditValue };
                break;

            case SettingsKeyNames.MotionIntensity:
                next = current with { MotionIntensity = item.EditValue };
                break;

            case SettingsKeyNames.HideVersionEpoch:
                next = current with { HideVersionEpoch = item.BooleanValue };
                break;

            case SettingsKeyNames.HideVersionVPrefix:
                next = current with { HideVersionVPrefix = item.BooleanValue };
                break;

            case SettingsKeyNames.AutoSortUpdates:
                next = current with { AutoSortUpdates = item.BooleanValue };
                break;

            case SettingsKeyNames.SuppressRecommendations:
                next = current with { SuppressRecommendations = item.BooleanValue };
                break;

            case SettingsKeyNames.MinimizeToTray:
                next = current with { MinimizeToTray = item.BooleanValue };
                break;

            case SettingsKeyNames.CloseToTray:
                next = current with { CloseToTray = item.BooleanValue };
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

            // 强调色的两行（预设 / 自定义 Hex）指向同一个字段，写完要让另一行跟着对齐，
            // 否则会出现「预设选着蓝、自定义框里还留着上个 Hex」的自相矛盾状态。
            if (string.Equals(item.Key, SettingsKeyNames.AccentColor, StringComparison.Ordinal) ||
                string.Equals(item.Key, SettingsKeyNames.AccentColorCustom, StringComparison.Ordinal) ||
                string.Equals(item.Key, SettingsKeyNames.AccentScope, StringComparison.Ordinal))
            {
                ApplyAccentColor(next);
            }

            // 背景的十一行是同一份 profile 的不同字段：保存任意一行后都要把整组刷一遍，
            // 否则「作用范围」切到实例后，其它行还显示全局的旧值。
            if (item.Key.StartsWith("Background", StringComparison.Ordinal))
            {
                ApplyBackgroundRows(next);
            }

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
