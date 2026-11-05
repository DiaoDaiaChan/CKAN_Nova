using System.Collections.ObjectModel;
using CkanUI.Localization;
using System.ComponentModel;
using System.Globalization;
using System.Windows.Input;
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

    /// <summary>组内每帧挂载的设置行数：行少时一帧挂完，行多时拆成几帧。</summary>
    private const int GroupRowBatchSize = 4;

    /// <summary>分组是否已开始挂载（并发调用 LoadAsync 时只允许一次挂载，避免重复行）。</summary>
    private bool _groupsFillStarted;
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

    /// <summary>
    /// 首次启动指引的宿主入口：为空时按钮给出「没有通道」提示，而不是点了没反应。
    /// </summary>
    public IFirstRunWizardLauncher? FirstRunWizard { get; set; }

    /// <summary>创建设置页视图模型。</summary>
    /// <param name="settings">设置服务（Core 配置的唯一出口）。</param>
    /// <param name="uiSettings">UI 自有设置存储（外观类设置）。</param>
    /// <param name="instances">实例服务：供「自动打开的实例」下拉与实例级容差文件定位；为空时这两项退化为只读/不可用。</param>
    /// <param name="copyTextAsync">写剪贴板的宿主委托（DI 已注册 <c>Func&lt;string, Task&gt;</c>）；为空时复制按钮给出无通道提示。</param>
    /// <param name="updateCheckService">自动更新与新版本检查器。</param>
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
        OpenFirstRunWizardCommand = new AsyncRelayCommand(OpenFirstRunWizardAsync);
        StatusText = Text("尚未读取设置", LocalizationKeys.SettingsStatusNotLoaded);

        BuildGroups();
        // 构造期提供器还没注入，先按随包发布的语种给默认候选；组装根稍后再调一次 RefreshLocaleChoices。
        RefreshLocaleChoices();

        foreach (var item in _items)
        {
            // 保存委托统一在这里挂：外观组（主题/语言/密度/字号/强调色/圆角/材质/动效）当初漏了构造参数，
            // 于是自动保存与行内「保存」都不触发，改完只剩外壳的「保存更改」一条路——用户看到的就是「改了不实时生效」。
            // 逐行补参数容易再漏，改由页面在装配后一次性接线，新增设置项时默认就会自动保存。
            item.AttachSaveHandler(vm => SaveItemAsync(vm));
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
    private IReadOnlyList<SettingChoice> BuildLocaleChoices(
        IReadOnlyList<(string Code, string Display)>? fromProvider)
    {
        var choices = new List<SettingChoice> { new(string.Empty, Text("跟随系统", LocalizationKeys.SettingsChoiceLocaleFollowSystem)) };
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
    public override string Title => Text("设置", LocalizationKeys.SettingsTitle);

    /// <summary>八个设置分组（顺序即界面顺序）。</summary>
    public ObservableCollection<SettingGroupViewModel> Groups { get; } = [];

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
        ? Text(
            "系统已开启高对比度：主题、强调色与背景由系统接管，这里的改动不会生效。",
            LocalizationKeys.SettingsNoticeHighContrastLock)
        : string.Empty;

    /// <summary>是否显示接管提示。</summary>
    public bool HasAppearanceLockNotice => IsAppearanceLocked;

    /// <summary>打开下载缓存目录。</summary>
    public IAsyncRelayCommand OpenCacheFolderCommand { get; }

    /// <summary>重新打开首次启动指引。</summary>
    public IAsyncRelayCommand OpenFirstRunWizardCommand { get; }

    /// <summary>打开配置目录（<c>%LocalAppData%/CKAN</c>）。</summary>
    public IAsyncRelayCommand OpenConfigFolderCommand { get; }

    /// <summary>立即检查更新。</summary>
    public IAsyncRelayCommand CheckForUpdatesCommand { get; }

    /// <summary>是否正在读取设置。</summary>
    [ObservableProperty]
    private bool _isLoading;

    /// <summary>页面上是否还有未保存的修改（任一设置项或令牌行脏）。</summary>
    [ObservableProperty]
    private bool _isDirty;

    /// <summary>外壳底部「未保存」提示条：设置项或令牌行还有没落盘的值。</summary>
    bool IUnsavedChangesAware.HasUnsavedChanges => IsDirty;

    /// <summary>提示条「保存更改」：把当前所有脏项串行写盘（与逐项自动保存走同一条 <see cref="SaveItemAsync"/> 链路）。</summary>
    ICommand? IUnsavedChangesAware.SaveChangesCommand => SaveAllCommand;

    /// <summary>提示条「放弃修改」：所有脏项回退到上次读到的值。</summary>
    ICommand? IUnsavedChangesAware.DiscardChangesCommand => DiscardCommand;

    /// <summary>状态栏文案。</summary>
    [ObservableProperty]
    private string _statusText = string.Empty;

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

    /// <summary>要跟着高对比度一起置灰的行：主题、强调色预设、自定义强调色、强调色作用范围，以及背景那十二行。</summary>
    /// <remarks>
    /// 背景同属外观：高对比度下 <c>BackgroundHost</c> 整层不画（THM-10），写进去也看不到，
    /// 所以和主题、强调色一样置灰并拒绝保存。
    /// </remarks>
    /// <summary>强调色「跟随系统」的档位名，和 <c>AccentPalettes.DefaultKey</c> 是同一个字符串。</summary>
    private const string DefaultAccentKey = "default";

    private static readonly string[] AppearanceKeys =
    [
        SettingsKeyNames.Theme,
        SettingsKeyNames.ThemeSkin,
        SettingsKeyNames.AccentColor,
        SettingsKeyNames.AccentColorCustom,
        SettingsKeyNames.AccentScope,
        SettingsKeyNames.BackgroundScene,
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

    /// <summary>是否已经成功读过一次全量设置；决定再次进入时走全量还是增量。</summary>
    private bool _hasLoadedOnce;

    /// <summary>保存全部脏项：外壳悬浮提示条上的「保存更改」走这里（逐项复用 <see cref="SaveItemAsync"/>，失败项各自留错误文案）。</summary>
    [RelayCommand]
    private async Task SaveAllAsync(CancellationToken ct)
    {
        var pending = _items.Where(item => item.IsDirty && !item.HasError).ToArray();
        if (pending.Length == 0)
        {
            StatusText = Text("没有需要保存的修改", LocalizationKeys.SettingsStatusNoChanges);
            return;
        }

        foreach (var item in pending)
        {
            await SaveItemAsync(item, ct).ConfigureAwait(true);
        }

        UpdateDirtyState();
    }

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
        StatusText = Text("已放弃未保存的修改", LocalizationKeys.SettingsStatusReverted);
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

        StatusText = retried == 0
                ? Text("没有可重试的失败项", LocalizationKeys.SettingsStatusNoRetryable)
                : Text("已重试 {0} 项", LocalizationKeys.SettingsStatusRetried, retried);
        UpdateDirtyState();
    }

    /// <summary>从服务读取全部设置并填充各组（分组读取，06 篇 UI-07）。</summary>
    /// <param name="ct">取消标记。</param>
    public async Task LoadAsync(CancellationToken ct = default)
    {
        var timing = SettingsTiming.Start();
        IsLoading = true;
        LoadErrorText = null;
        OnPropertyChanged(nameof(HasLoadError));

        try
        {
            var all = await _settings.GetAllAsync(ct).ConfigureAwait(true);
            if (!all.IsSuccess || all.Value is null)
            {
                SetLoadError(all.Message ?? Text("读取设置失败", LocalizationKeys.SettingsErrorLoadFailed), all.Detail);
                return;
            }

            timing.Mark("getAll");
            ApplyCoreValues(all.Value);
            ApplyUiSettings();
            ApplyInstanceState(all.Value);
            timing.Mark("apply");
            await LoadStabilityToleranceAsync(ct).ConfigureAwait(true);
            _diagnostics.Update(
                _instances?.Current,
                SettingsPaths.DefaultConfigDirectory,
                _cacheDirItem?.EditValue);
            _tokenEditor.LoadFrom(all.Value);
            UpdateDirtyState();
            timing.Mark("finish");

            // 行是这次读取才建出来的，接管状态要重新贴一遍（不能指望视图的 DataContextChanged 早于 LoadAsync）。
            RefreshAppearanceLock();
            _hasLoadedOnce = true;

            if (!HasLoadError)
            {
                StatusText = Text(
                    "已读取 {0} 项设置、{1} 条令牌",
                    LocalizationKeys.SettingsStatusLoaded,
                    _items.Count,
                    _tokenEditor.Tokens.Count);
            }
        }
        catch (Exception ex)
        {
            // 服务契约不抛异常；真抛了也不能穿透到 UI 线程（01 篇 §8 异常收口）。
            SetLoadError(Text("读取设置失败", LocalizationKeys.SettingsErrorLoadFailed), ex.Message);
        }
        finally
        {
            // 先撤骨架再挂分组：骨架不能等分批挂载（读值完成就该消失），否则用户在分批期间看到的是假加载态。
            IsLoading = false;
            timing.Mark("isLoading-off");

            await FillGroupsAsync().ConfigureAwait(true);
            timing.MarkMounted(Groups.Count, Groups.Sum(group => group.Items.Count));
            timing.Flush();
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
            StatusText = Text("{0}：请先修正输入", LocalizationKeys.SettingsStatusNeedsFix, item.DisplayName);
            UpdateDirtyState();
            return;
        }

        if (IsAppearanceLocked && AppearanceKeys.Contains(item.Key, StringComparer.Ordinal))
        {
            // 高对比度期间这两行是只读的：即便命令被绕过（脚本/快捷键），也不能写出一个立刻要回滚的值。
            StatusText = Text("{0}：系统高对比度已接管外观", LocalizationKeys.SettingsStatusHighContrastOwned, item.DisplayName);
            return;
        }

        if (!item.IsDirty)
        {
            StatusText = Text("{0}：没有修改", LocalizationKeys.SettingsStatusNoChange, item.DisplayName);
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
                        item.MarkFailed(result.Message ?? Text("保存失败", LocalizationKeys.SettingsErrorSaveFailed), result.Detail, result.Retryable);
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
                    item.MarkFailed(Text("未知的保存去向", LocalizationKeys.SettingsErrorUnknownPersistence), null, retryable: false);
                    StatusText = Text("{0}：保存失败", LocalizationKeys.SettingsStatusItemSaveFailed, item.DisplayName);
                    break;
            }
        }
        catch (Exception ex)
        {
            item.MarkFailed(Text("保存失败", LocalizationKeys.SettingsErrorSaveFailed), ex.Message, retryable: true);
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

        // 首次进入读全量；之后再进来只补随实例变化的那部分。
        // 全量重读会在每次切页时重灌 36 行的编辑值并重发一轮属性通知，且 IsLoading 会让整页闪一次骨架。
        _ = _hasLoadedOnce ? RefreshInstanceStateAsync() : LoadAsync();
    }

    /// <summary>
    /// 逐组、逐行把分组挂进 <see cref="Groups"/>，每次挂载之间让一帧。
    /// </summary>
    /// <remarks>
    /// 八组共 2016 个节点，一次性挂上去会把「控件实体化 + 首次布局」全压进同一个 UI 帧（实测 maxGap 1091ms）；
    /// 只按组分帧时「外观」组单独一帧仍要 ~240ms。故组内再按行分帧：每帧挂 <see cref="GroupRowBatchSize"/> 行，
    /// 单帧只承担一小片实体化。值已先灌好，后面不再因为属性变化触发二次重排。
    /// </remarks>
    private async Task FillGroupsAsync()
    {
        if (_groupsFillStarted || _groups.Count == 0)
        {
            return;
        }

        _groupsFillStarted = true;

        foreach (var group in _groups)
        {
            Groups.Add(group);

            while (group.PendingItemCount > 0)
            {
                // Task.Delay 的续体回到 UI 线程排队，中间这一帧留给渲染/布局，用户看到的是逐组、逐行长出来。
                await Task.Delay(16).ConfigureAwait(true);
                group.RevealItems(GroupRowBatchSize);
            }

            await Task.Delay(16).ConfigureAwait(true);
        }
    }

    /// <summary>再次进入时只刷新依赖当前实例的设置项与诊断卡，不重灌 36 行编辑值。</summary>
    /// <param name="ct">取消标记。</param>
    private async Task RefreshInstanceStateAsync(CancellationToken ct = default)
    {
        try
        {
            var all = await _settings.GetAllAsync(ct).ConfigureAwait(true);
            if (!all.IsSuccess || all.Value is null)
            {
                return;
            }

            ApplyInstanceState(all.Value);
            await LoadStabilityToleranceAsync(ct).ConfigureAwait(true);
            _diagnostics.Update(
                _instances?.Current,
                SettingsPaths.DefaultConfigDirectory,
                _cacheDirItem?.EditValue);
            UpdateDirtyState();
        }
        catch (Exception ex)
        {
            // 服务契约不抛异常；真抛了也不能穿透到 UI 线程（01 篇 §8 异常收口）。
            SetLoadError(Text("读取设置失败", LocalizationKeys.SettingsErrorLoadFailed), ex.Message);
        }
    }

    private void BuildGroups()
    {
        _devBuildsItem = new SettingItemViewModel(
            SettingsKeyNames.DevBuilds,
            Text("开发版更新通道", LocalizationKeys.SettingsItemDevBuilds),
            SettingItemKind.Boolean,
            SettingPersistence.Core,
            SettingGroupKey.General,
            description: Text("开启后检查更新时包含 dev 构建；关闭则只跟踪正式版。", LocalizationKeys.SettingsItemDevBuildsDesc),
            saveHandler: vm => SaveItemAsync(vm));
        _devBuildsItem.SetAuxiliaryAction(Text("立即检查更新", LocalizationKeys.SettingsAuxiliaryCheckUpdates), CheckForUpdatesCommand);

        var general = Group(
            SettingGroupKey.General,
            Text("通用", LocalizationKeys.SettingsSectionGeneral),
            Text("与具体实例无关的行为。", LocalizationKeys.SettingsSectionGeneralDesc),
            _devBuildsItem,
            new SettingItemViewModel(
                SettingsKeyNames.MasterMuted,
                Text("全局静音", LocalizationKeys.SettingsItemMasterMuted),
                SettingItemKind.Boolean,
                SettingPersistence.UiSettings,
                SettingGroupKey.General,
                description: Text("一键静音所有音效与音乐。", LocalizationKeys.SettingsItemMasterMutedDesc),
                saveHandler: vm => SaveItemAsync(vm)),
            new SettingItemViewModel(
                SettingsKeyNames.SoundEffectsEnabled,
                Text("按键与提示音效", LocalizationKeys.SettingsItemSoundEffects),
                SettingItemKind.Boolean,
                SettingPersistence.UiSettings,
                SettingGroupKey.General,
                description: Text("开启后点击、勾选、切换与弹出提示时播放反馈音效。", LocalizationKeys.SettingsItemSoundEffectsDesc),
                saveHandler: vm => SaveItemAsync(vm)),
            new SettingItemViewModel(
                SettingsKeyNames.SoundEffectsVolume,
                Text("音效音量", LocalizationKeys.SettingsItemSoundEffectsVolume),
                SettingItemKind.Integer,
                SettingPersistence.UiSettings,
                SettingGroupKey.General,
                description: Text("提示音效音量百分比（0–100）。", LocalizationKeys.SettingsItemSoundEffectsVolumeDesc),
                min: 0,
                max: 100,
                saveHandler: vm => SaveItemAsync(vm)),
            new SettingItemViewModel(
                SettingsKeyNames.MusicEnabled,
                Text("背景音乐播放器", LocalizationKeys.SettingsItemMusicEnabled),
                SettingItemKind.Boolean,
                SettingPersistence.UiSettings,
                SettingGroupKey.General,
                description: Text("开启原声音乐播放功能。", LocalizationKeys.SettingsItemMusicEnabledDesc),
                saveHandler: vm => SaveItemAsync(vm)),
            new SettingItemViewModel(
                SettingsKeyNames.MusicVolume,
                Text("音乐音量", LocalizationKeys.SettingsItemMusicVolume),
                SettingItemKind.Integer,
                SettingPersistence.UiSettings,
                SettingGroupKey.General,
                description: Text("背景音乐音量百分比（0–100）。", LocalizationKeys.SettingsItemMusicVolumeDesc),
                min: 0,
                max: 100,
                saveHandler: vm => SaveItemAsync(vm)));

        _autoStartItem = new SettingItemViewModel(
            SettingsKeyNames.AutoStartInstance,
            Text("自动打开的实例", LocalizationKeys.SettingsItemAutoStartInstance),
            SettingItemKind.Choice,
            SettingPersistence.Core,
            SettingGroupKey.Instance,
            description: Text("从已登记的实例里选一个；「不自动打开」表示启动后需要手动选实例。", LocalizationKeys.SettingsItemAutoStartInstanceDesc),
            requiresRestart: true,
            choices: new[] { new SettingChoice(string.Empty, Text("不自动打开", LocalizationKeys.SettingsChoiceAutoStartInstanceNone)) },
            saveHandler: vm => SaveItemAsync(vm));

        var instance = Group(
            SettingGroupKey.Instance,
            Text("实例", LocalizationKeys.SettingsSectionInstance),
            Text("启动该界面时自动打开的实例。", LocalizationKeys.SettingsSectionInstanceDesc),
            _autoStartItem);

        _cacheDirItem = new SettingItemViewModel(
            SettingsKeyNames.DownloadCacheDir,
            Text("缓存目录", LocalizationKeys.SettingsItemDownloadCacheDir),
            SettingItemKind.Path,
            SettingPersistence.Core,
            SettingGroupKey.DownloadCache,
            description: Text("留空表示使用 Core 的默认缓存目录。", LocalizationKeys.SettingsItemDownloadCacheDirDesc),
            saveHandler: vm => SaveItemAsync(vm));
        _cacheDirItem.SetAuxiliaryAction(Text("打开目录", LocalizationKeys.SettingsAuxiliaryOpenFolder), OpenCacheFolderCommand);

        var cache = Group(
            SettingGroupKey.DownloadCache,
            Text("下载缓存", LocalizationKeys.SettingsSectionDownloadCache),
            Text("模组压缩包的缓存位置与容量上限。", LocalizationKeys.SettingsSectionDownloadCacheDesc),
            _cacheDirItem,
            new SettingItemViewModel(
                SettingsKeyNames.CacheSizeLimit,
                Text("缓存容量上限（MiB）", LocalizationKeys.SettingsItemCacheSizeLimit),
                SettingItemKind.Integer,
                SettingPersistence.Core,
                SettingGroupKey.DownloadCache,
                description: Text("超过上限时由缓存管理执行「清理到上限」；留空表示不限。", LocalizationKeys.SettingsItemCacheSizeLimitDesc),
                requiresRestart: true,
                min: 0,
                max: MaxCacheLimitMebibytes,
                toWire: MebibytesToBytes,
                fromWire: BytesToMebibytes,
                saveHandler: vm => SaveItemAsync(vm)));

        var repository = Group(
            SettingGroupKey.Repository,
            Text("仓库与网络", LocalizationKeys.SettingsSectionRepository),
            Text("仓库元数据的自动刷新节奏与主机优先级。", LocalizationKeys.SettingsSectionRepositoryDesc),
            new SettingItemViewModel(
                SettingsKeyNames.RefreshRate,
                Text("自动刷新间隔（分钟）", LocalizationKeys.SettingsItemRefreshRate),
                SettingItemKind.Integer,
                SettingPersistence.Core,
                SettingGroupKey.Repository,
                description: Text("0 表示不自动刷新；留空表示使用默认值。", LocalizationKeys.SettingsItemRefreshRateDesc),
                min: 0,
                max: MaxRefreshRateMinutes,
                saveHandler: vm => SaveItemAsync(vm)),
            new SettingItemViewModel(
                SettingsKeyNames.PreferredHosts,
                Text("首选主机顺序", LocalizationKeys.SettingsItemPreferredHosts),
                SettingItemKind.LineList,
                SettingPersistence.Core,
                SettingGroupKey.Repository,
                description: Text("每行一个主机名，自上而下优先；空行表示「其余主机」的位置。", LocalizationKeys.SettingsItemPreferredHostsDesc),
                saveHandler: vm => SaveItemAsync(vm)));

        var authToken = new SettingGroupViewModel(
            SettingGroupKey.AuthToken,
            Text("认证令牌", LocalizationKeys.SettingsSectionAuthToken),
            Text("按主机配置访问令牌；界面只显示脱敏预览，明文不写日志。", LocalizationKeys.SettingsSectionAuthTokenDesc),
            Array.Empty<SettingItemViewModel>(),
            _tokenEditor);

        var appearance = Group(
            SettingGroupKey.Appearance,
            Text("外观", LocalizationKeys.SettingsSectionAppearance),
            Text("界面主题、语言与密度；只写 ckan-ui.json，不进 Core 配置。", LocalizationKeys.SettingsSectionAppearanceDesc),
            new SettingItemViewModel(
                SettingsKeyNames.Theme,
                Text("主题", LocalizationKeys.SettingsItemTheme),
                SettingItemKind.Choice,
                SettingPersistence.UiSettings,
                SettingGroupKey.Appearance,
                description: Text("高对比度主题在系统开启高对比度时会被强制使用。", LocalizationKeys.SettingsItemThemeDesc),
                choices: new[]
                {
                    new SettingChoice("system", Text("跟随系统", LocalizationKeys.SettingsChoiceThemeSystem)),
                    new SettingChoice("light", Text("浅色", LocalizationKeys.SettingsChoiceThemeLight)),
                    new SettingChoice("dark", Text("深色", LocalizationKeys.SettingsChoiceThemeDark)),
                    new SettingChoice("highcontrast", Text("高对比度", LocalizationKeys.SettingsChoiceThemeHighContrast)),
                }),
            new SettingItemViewModel(
                SettingsKeyNames.ThemeSkin,
                Text("主题皮肤", LocalizationKeys.SettingsItemThemeSkin),
                SettingItemKind.Choice,
                SettingPersistence.UiSettings,
                SettingGroupKey.Appearance,
                description: Text("在深色主题下整体替换画布、表面与强调色；浅色和高对比度仍使用默认主题，保证极端对比度下的可读性。保存后立即生效。", LocalizationKeys.SettingsItemThemeSkinDesc),
                choices: new[]
                {
                    new SettingChoice(ThemeSkinKinds.Default, Text("默认（深空蓝）", LocalizationKeys.SettingsChoiceThemeSkinDefault)),
                    new SettingChoice(ThemeSkinKinds.CoronaGold, Text("日冕金", LocalizationKeys.SettingsChoiceThemeSkinCoronaGold)),
                    new SettingChoice(ThemeSkinKinds.LunarGraphite, Text("月白石墨", LocalizationKeys.SettingsChoiceThemeSkinLunarGraphite)),
                    new SettingChoice(ThemeSkinKinds.Celadon, Text("青瓷", LocalizationKeys.SettingsChoiceThemeSkinCeladon)),
                    new SettingChoice(ThemeSkinKinds.Copper, Text("铜金", LocalizationKeys.SettingsChoiceThemeSkinCopper)),
                    new SettingChoice(ThemeSkinKinds.NebulaViolet, Text("星云紫", LocalizationKeys.SettingsChoiceThemeSkinNebulaViolet)),
                    new SettingChoice(ThemeSkinKinds.MonochromeSilver, Text("单色银", LocalizationKeys.SettingsChoiceThemeSkinMonochromeSilver)),
                }),
            new SettingItemViewModel(
                SettingsKeyNames.Locale,
                Text("界面语言", LocalizationKeys.SettingsItemLocale),
                SettingItemKind.Choice,
                SettingPersistence.UiSettings,
                SettingGroupKey.Appearance,
                description: Text("语言切换需要重启界面后生效。", LocalizationKeys.SettingsItemLocaleDesc),
                requiresRestart: true,
                choices: BuildLocaleChoices(fromProvider: null)),
            new SettingItemViewModel(
                SettingsKeyNames.Density,
                Text("界面密度", LocalizationKeys.SettingsItemDensity),
                SettingItemKind.Choice,
                SettingPersistence.UiSettings,
                SettingGroupKey.Appearance,
                description: Text("紧凑/标准/舒适三档，影响行高与内边距；保存后即时生效。", LocalizationKeys.SettingsItemDensityDesc),
                choices: new[]
                {
                    new SettingChoice("compact", Text("紧凑", LocalizationKeys.SettingsChoiceDensityCompact)),
                    new SettingChoice("standard", Text("标准", LocalizationKeys.SettingsChoiceDensityStandard)),
                    new SettingChoice("comfortable", Text("舒适", LocalizationKeys.SettingsChoiceDensityComfortable)),
                }),
            new SettingItemViewModel(
                SettingsKeyNames.FontScale,
                Text("字号缩放", LocalizationKeys.SettingsItemFontScale),
                SettingItemKind.Choice,
                SettingPersistence.UiSettings,
                SettingGroupKey.Appearance,
                description: Text("90%–130% 五档，只放大界面文案与标题；模组名、版本号等数据文本保持原样。", LocalizationKeys.SettingsItemFontScaleDesc),
                choices: new[]
                {
                    new SettingChoice("90", "90%"),
                    new SettingChoice("100", Text("100%（默认）", LocalizationKeys.SettingsChoiceFontScale100)),
                    new SettingChoice("110", "110%"),
                    new SettingChoice("120", "120%"),
                    new SettingChoice("130", "130%"),
                }),
            new SettingItemViewModel(
                SettingsKeyNames.AccentColor,
                Text("强调色", LocalizationKeys.SettingsItemAccentColor),
                SettingItemKind.Choice,
                SettingPersistence.UiSettings,
                SettingGroupKey.Appearance,
                description: Text("12 预设色板与系统色彩跟随；即时生效。", LocalizationKeys.SettingsItemAccentColorDesc),
                choices: new[]
                {
                    new SettingChoice("default", Text("跟随系统 / 经典", LocalizationKeys.SettingsChoiceAccentColorDefault)),
                    new SettingChoice("blue", Text("经典蓝 / Blue", LocalizationKeys.SettingsChoiceAccentColorBlue)),
                    new SettingChoice("indigo", Text("靛青 / Indigo", LocalizationKeys.SettingsChoiceAccentColorIndigo)),
                    new SettingChoice("purple", Text("星云紫 / Purple", LocalizationKeys.SettingsChoiceAccentColorPurple)),
                    new SettingChoice("magenta", Text("品红 / Magenta", LocalizationKeys.SettingsChoiceAccentColorMagenta)),
                    new SettingChoice("red", Text("火箭红 / Red", LocalizationKeys.SettingsChoiceAccentColorRed)),
                    new SettingChoice("orange", Text("轨道橙 / Orange", LocalizationKeys.SettingsChoiceAccentColorOrange)),
                    new SettingChoice("amber", Text("琥珀金 / Amber", LocalizationKeys.SettingsChoiceAccentColorAmber)),
                    new SettingChoice("lime", Text("坎星绿 / Lime", LocalizationKeys.SettingsChoiceAccentColorLime)),
                    new SettingChoice("green", Text("极光绿 / Green", LocalizationKeys.SettingsChoiceAccentColorGreen)),
                    new SettingChoice("teal", Text("青碧 / Teal", LocalizationKeys.SettingsChoiceAccentColorTeal)),
                    new SettingChoice("cyan", Text("天蓝 / Cyan", LocalizationKeys.SettingsChoiceAccentColorCyan)),
                    new SettingChoice("slate", Text("灰蓝 / Slate", LocalizationKeys.SettingsChoiceAccentColorSlate)),
                }),
            new SettingItemViewModel(
                SettingsKeyNames.AccentColorCustom,
                Text("自定义强调色", LocalizationKeys.SettingsItemAccentColorCustom),
                SettingItemKind.Text,
                SettingPersistence.UiSettings,
                SettingGroupKey.Appearance,
                description: Text(
                    "填 #RRGGBB（例如 #1F6FEB）；保存时按 WCAG AA 校验与底色的对比度（≥4.5:1），不合规会给出建议色并拒绝写入。留空表示回到上面的预设。",
                    LocalizationKeys.SettingsItemAccentColorCustomDesc)),
            new SettingItemViewModel(
                SettingsKeyNames.AccentScope,
                Text("强调色作用范围", LocalizationKeys.SettingsItemAccentScope),
                SettingItemKind.Choice,
                SettingPersistence.UiSettings,
                SettingGroupKey.Appearance,
                description: Text("全局 = 所有实例共用一个强调色；按实例 = 为当前实例单独记一个色，切换实例时自动跟着换。", LocalizationKeys.SettingsItemAccentScopeDesc),
                choices: new[]
                {
                    new SettingChoice("global", Text("全局", LocalizationKeys.SettingsChoiceAccentScopeGlobal)),
                    new SettingChoice("instance", Text("按实例（当前实例）", LocalizationKeys.SettingsChoiceAccentScopeInstance)),
                }),
            new SettingItemViewModel(
                SettingsKeyNames.RadiusScale,
                Text("圆角尺度", LocalizationKeys.SettingsItemRadiusScale),
                SettingItemKind.Choice,
                SettingPersistence.UiSettings,
                SettingGroupKey.Appearance,
                description: Text("三档圆角比例；无圆角/标准微圆角/大饱满圆角。", LocalizationKeys.SettingsItemRadiusScaleDesc),
                choices: new[]
                {
                    new SettingChoice("none", Text("直角 (0x)", LocalizationKeys.SettingsChoiceRadiusScaleNone)),
                    new SettingChoice("standard", Text("标准微圆角 (1x)", LocalizationKeys.SettingsChoiceRadiusScaleStandard)),
                    new SettingChoice("round", Text("饱满圆角 (1.5x)", LocalizationKeys.SettingsChoiceRadiusScaleRound)),
                }),
            new SettingItemViewModel(
                SettingsKeyNames.BackdropKind,
                Text("窗口材质与透明", LocalizationKeys.SettingsItemBackdropKind),
                SettingItemKind.Choice,
                SettingPersistence.UiSettings,
                SettingGroupKey.Appearance,
                description: Text("Windows 磨砂背景材质；自动/Mica云母/Acrylic磨砂/纯色。", LocalizationKeys.SettingsItemBackdropKindDesc),
                choices: new[]
                {
                    new SettingChoice("auto", Text("自动推荐 (Mica / Acrylic)", LocalizationKeys.SettingsChoiceBackdropKindAuto)),
                    new SettingChoice("mica", Text("Mica 云母", LocalizationKeys.SettingsChoiceBackdropKindMica)),
                    new SettingChoice("acrylic", Text("Acrylic 亚克力磨砂", LocalizationKeys.SettingsChoiceBackdropKindAcrylic)),
                    new SettingChoice("none", Text("纯色无透明", LocalizationKeys.SettingsChoiceBackdropKindNone)),
                }),
            new SettingItemViewModel(
                SettingsKeyNames.MotionIntensity,
                Text("动效强度", LocalizationKeys.SettingsItemMotionIntensity),
                SettingItemKind.Choice,
                SettingPersistence.UiSettings,
                SettingGroupKey.Appearance,
                description: Text("界面动画与过渡效果强度。默认跟随系统「减少动画」设置；精简档只保留 90ms 的悬停/按下反馈。", LocalizationKeys.SettingsItemMotionIntensityDesc),
                choices: new[]
                {
                    new SettingChoice("auto", Text("跟随系统 (推荐)", LocalizationKeys.SettingsChoiceMotionIntensityAuto)),
                    new SettingChoice("full", Text("流畅动画", LocalizationKeys.SettingsChoiceMotionIntensityFull)),
                    new SettingChoice("reduced", Text("减少动画", LocalizationKeys.SettingsChoiceMotionIntensityReduced)),
                    new SettingChoice("none", Text("关闭动画", LocalizationKeys.SettingsChoiceMotionIntensityNone)),
                }),
            new SettingItemViewModel(
                SettingsKeyNames.BackgroundScene,
                Text("装饰场景", LocalizationKeys.SettingsItemBackgroundScene),
                SettingItemKind.Choice,
                SettingPersistence.UiSettings,
                SettingGroupKey.Appearance,
                description: Text("应用自带的程序化线稿背景：只在没有自定义背景、非高对比度时显示，页面卡片仍然不透明。切换后立即生效，并保留各场景自己的慢速运动与光标响应。", LocalizationKeys.SettingsItemBackgroundSceneDesc),
                choices: new[]
                {
                    new SettingChoice(BackgroundSceneKinds.Starfield, Text("星空", LocalizationKeys.SettingsChoiceBackgroundSceneStarfield)),
                    new SettingChoice(BackgroundSceneKinds.Orbital, Text("轨道蓝图", LocalizationKeys.SettingsChoiceBackgroundSceneOrbital)),
                    new SettingChoice(BackgroundSceneKinds.Lunar, Text("月面晨昏线", LocalizationKeys.SettingsChoiceBackgroundSceneLunar)),
                    new SettingChoice(BackgroundSceneKinds.Probe, Text("深空探测器", LocalizationKeys.SettingsChoiceBackgroundSceneProbe)),
                    new SettingChoice(BackgroundSceneKinds.Lens, Text("引力透镜", LocalizationKeys.SettingsChoiceBackgroundSceneLens)),
                    new SettingChoice(BackgroundSceneKinds.Corona, Text("日冕", LocalizationKeys.SettingsChoiceBackgroundSceneCorona)),
                    new SettingChoice(BackgroundSceneKinds.RingPlane, Text("行星环", LocalizationKeys.SettingsChoiceBackgroundSceneRingPlane)),
                    new SettingChoice(BackgroundSceneKinds.Aurora, Text("极光椭圆", LocalizationKeys.SettingsChoiceBackgroundSceneAurora)),
                    new SettingChoice(BackgroundSceneKinds.Pulsar, Text("脉冲星", LocalizationKeys.SettingsChoiceBackgroundScenePulsar)),
                    new SettingChoice(BackgroundSceneKinds.SolarSail, Text("太阳帆", LocalizationKeys.SettingsChoiceBackgroundSceneSolarSail)),
                    new SettingChoice(BackgroundSceneKinds.SpaceElevator, Text("太空电梯", LocalizationKeys.SettingsChoiceBackgroundSceneSpaceElevator)),
                    new SettingChoice(BackgroundSceneKinds.None, Text("无（纯主题底色）", LocalizationKeys.SettingsChoiceBackgroundSceneNone)),
                },
                saveHandler: vm => SaveItemAsync(vm)),
            new SettingItemViewModel(
                SettingsKeyNames.BackgroundKind,
                Text("背景来源", LocalizationKeys.SettingsItemBackgroundKind),
                SettingItemKind.Choice,
                SettingPersistence.UiSettings,
                SettingGroupKey.Appearance,
                description: Text("窗口空白处的背景：纯色 / 渐变 / 本机图片 / 当前实例的最新一张游戏截图；页面卡片保持不透明以保证文字清晰。系统高对比度下不画背景。", LocalizationKeys.SettingsItemBackgroundKindDesc),
                choices: new[]
                {
                    new SettingChoice("none", Text("无（跟随主题）", LocalizationKeys.SettingsChoiceBackgroundKindNone)),
                    new SettingChoice("solid", Text("纯色", LocalizationKeys.SettingsChoiceBackgroundKindSolid)),
                    new SettingChoice("gradient", Text("渐变", LocalizationKeys.SettingsChoiceBackgroundKindGradient)),
                    new SettingChoice("image", Text("本机图片", LocalizationKeys.SettingsChoiceBackgroundKindImage)),
                    new SettingChoice("screenshot", Text("游戏截图（最新一张）", LocalizationKeys.SettingsChoiceBackgroundKindScreenshot)),
                },
                saveHandler: vm => SaveItemAsync(vm)),
            new SettingItemViewModel(
                SettingsKeyNames.BackgroundColor,
                Text("背景色", LocalizationKeys.SettingsItemBackgroundColor),
                SettingItemKind.Text,
                SettingPersistence.UiSettings,
                SettingGroupKey.Appearance,
                description: Text("纯色来源的颜色，填 #RRGGBB（例如 #1B1F27）。", LocalizationKeys.SettingsItemBackgroundColorDesc),
                saveHandler: vm => SaveItemAsync(vm)),
            new SettingItemViewModel(
                SettingsKeyNames.BackgroundGradientStart,
                Text("渐变起色", LocalizationKeys.SettingsItemBackgroundGradientStart),
                SettingItemKind.Text,
                SettingPersistence.UiSettings,
                SettingGroupKey.Appearance,
                description: Text("渐变来源的起始颜色，填 #RRGGBB。", LocalizationKeys.SettingsItemBackgroundGradientStartDesc),
                saveHandler: vm => SaveItemAsync(vm)),
            new SettingItemViewModel(
                SettingsKeyNames.BackgroundGradientEnd,
                Text("渐变止色", LocalizationKeys.SettingsItemBackgroundGradientEnd),
                SettingItemKind.Text,
                SettingPersistence.UiSettings,
                SettingGroupKey.Appearance,
                description: Text("渐变来源的结束颜色，填 #RRGGBB。", LocalizationKeys.SettingsItemBackgroundGradientEndDesc),
                saveHandler: vm => SaveItemAsync(vm)),
            new SettingItemViewModel(
                SettingsKeyNames.BackgroundGradientAngle,
                Text("渐变角度", LocalizationKeys.SettingsItemBackgroundGradientAngle),
                SettingItemKind.Integer,
                SettingPersistence.UiSettings,
                SettingGroupKey.Appearance,
                description: Text("0° 向右、90° 向下、180° 向左；取值 0–359。", LocalizationKeys.SettingsItemBackgroundGradientAngleDesc),
                min: 0,
                max: 359,
                saveHandler: vm => SaveItemAsync(vm)),
            new SettingItemViewModel(
                SettingsKeyNames.BackgroundImagePath,
                Text("背景图片", LocalizationKeys.SettingsItemBackgroundImagePath),
                SettingItemKind.Path,
                SettingPersistence.UiSettings,
                SettingGroupKey.Appearance,
                description: Text("本机图片的绝对路径（PNG/JPG/BMP）；按 1600 像素宽解码，读不出来会退回无背景。", LocalizationKeys.SettingsItemBackgroundImagePathDesc),
                saveHandler: vm => SaveItemAsync(vm)),
            new SettingItemViewModel(
                SettingsKeyNames.BackgroundFill,
                Text("图片填充方式", LocalizationKeys.SettingsItemBackgroundFill),
                SettingItemKind.Choice,
                SettingPersistence.UiSettings,
                SettingGroupKey.Appearance,
                description: Text("图片超出窗口时怎么摆。", LocalizationKeys.SettingsItemBackgroundFillDesc),
                choices: new[]
                {
                    new SettingChoice("fill", Text("填充（等比铺满）", LocalizationKeys.SettingsChoiceBackgroundFillFill)),
                    new SettingChoice("fit", Text("适应（完整显示）", LocalizationKeys.SettingsChoiceBackgroundFillFit)),
                    new SettingChoice("stretch", Text("拉伸（不保比例）", LocalizationKeys.SettingsChoiceBackgroundFillStretch)),
                    new SettingChoice("tile", Text("平铺", LocalizationKeys.SettingsChoiceBackgroundFillTile)),
                    new SettingChoice("center", Text("居中（原尺寸）", LocalizationKeys.SettingsChoiceBackgroundFillCenter)),
                },
                saveHandler: vm => SaveItemAsync(vm)),
            new SettingItemViewModel(
                SettingsKeyNames.BackgroundDim,
                Text("背景暗化", LocalizationKeys.SettingsItemBackgroundDim),
                SettingItemKind.Integer,
                SettingPersistence.UiSettings,
                SettingGroupKey.Appearance,
                description: Text("在背景上压一层黑色，百分比越高文字越清楚（0–100）。", LocalizationKeys.SettingsItemBackgroundDimDesc),
                min: 0,
                max: 100,
                saveHandler: vm => SaveItemAsync(vm)),
            new SettingItemViewModel(
                SettingsKeyNames.BackgroundBlur,
                Text("背景模糊", LocalizationKeys.SettingsItemBackgroundBlur),
                SettingItemKind.Integer,
                SettingPersistence.UiSettings,
                SettingGroupKey.Appearance,
                description: Text("模糊半径（0–64 像素）；0 表示不模糊。", LocalizationKeys.SettingsItemBackgroundBlurDesc),
                min: 0,
                max: 64,
                saveHandler: vm => SaveItemAsync(vm)),
            new SettingItemViewModel(
                SettingsKeyNames.BackgroundOpacity,
                Text("背景不透明度", LocalizationKeys.SettingsItemBackgroundOpacity),
                SettingItemKind.Integer,
                SettingPersistence.UiSettings,
                SettingGroupKey.Appearance,
                description: Text("背景层整体不透明度百分比（0–100）。", LocalizationKeys.SettingsItemBackgroundOpacityDesc),
                min: 0,
                max: 100,
                saveHandler: vm => SaveItemAsync(vm)),
            new SettingItemViewModel(
                SettingsKeyNames.BackgroundScope,
                Text("背景作用范围", LocalizationKeys.SettingsItemBackgroundScope),
                SettingItemKind.Choice,
                SettingPersistence.UiSettings,
                SettingGroupKey.Appearance,
                description: Text("「仅当前实例」把整份背景记在 BackgroundByInstance 里；切回「全局」会删掉本实例的覆盖。", LocalizationKeys.SettingsItemBackgroundScopeDesc),
                choices: new[]
                {
                    new SettingChoice("global", Text("全局（所有实例）", LocalizationKeys.SettingsChoiceBackgroundScopeGlobal)),
                    new SettingChoice("instance", Text("仅当前实例", LocalizationKeys.SettingsChoiceBackgroundScopeInstance)),
                },
                saveHandler: vm => SaveItemAsync(vm)));

        _stabilityItem = new SettingItemViewModel(
            SettingsKeyNames.StabilityTolerance,
            Text("稳定版容差", LocalizationKeys.SettingsItemStabilityTolerance),
            SettingItemKind.Choice,
            SettingPersistence.InstanceFile,
            SettingGroupKey.Compatibility,
            description: Text("写入当前实例的 CKAN/stability_tolerance.json，只影响该实例；选「稳定」表示只安装标记为稳定的模组。", LocalizationKeys.SettingsItemStabilityToleranceDesc),
            choices: new[]
            {
                new SettingChoice("stable", Text("稳定", LocalizationKeys.SettingsChoiceStabilityToleranceStable)),
                new SettingChoice("testing", Text("测试", LocalizationKeys.SettingsChoiceStabilityToleranceTesting)),
                new SettingChoice("development", Text("开发", LocalizationKeys.SettingsChoiceStabilityToleranceDevelopment)),
            },
            saveHandler: vm => SaveItemAsync(vm));

        var compatibility = Group(
            SettingGroupKey.Compatibility,
            Text("兼容性", LocalizationKeys.SettingsSectionCompatibility),
            Text("实例级的稳定版容差，以及按游戏生效的全局安装过滤器。", LocalizationKeys.SettingsSectionCompatibilityDesc),
            _stabilityItem,
            new SettingItemViewModel(
                SettingsKeyNames.GlobalInstallFilters("KSP"),
                Text("安装过滤器（KSP）", LocalizationKeys.SettingsItemGlobalInstallFiltersKsp),
                SettingItemKind.LineList,
                SettingPersistence.Core,
                SettingGroupKey.Compatibility,
                description: Text("每行一条过滤器表达式，例如 not:*.zip；留空表示不过滤。", LocalizationKeys.SettingsItemGlobalInstallFiltersKspDesc),
                saveHandler: vm => SaveItemAsync(vm)),
            new SettingItemViewModel(
                SettingsKeyNames.GlobalInstallFilters("KSP2"),
                Text("安装过滤器（KSP2）", LocalizationKeys.SettingsItemGlobalInstallFiltersKsp2),
                SettingItemKind.LineList,
                SettingPersistence.Core,
                SettingGroupKey.Compatibility,
                description: Text("每行一条过滤器表达式，例如 not:*.zip；留空表示不过滤。", LocalizationKeys.SettingsItemGlobalInstallFiltersKsp2Desc),
                saveHandler: vm => SaveItemAsync(vm)));

        var advancedItems = new[]
        {
            new SettingItemViewModel(
                SettingsKeyNames.HideVersionEpoch,
                Text("隐藏版本 epoch 前缀", LocalizationKeys.SettingsItemHideVersionEpoch),
                SettingItemKind.Boolean,
                SettingPersistence.UiSettings,
                SettingGroupKey.Advanced,
                description: Text("把 1:2.0.0 显示为 2.0.0。", LocalizationKeys.SettingsItemHideVersionEpochDesc),
                saveHandler: vm => SaveItemAsync(vm)),
            new SettingItemViewModel(
                SettingsKeyNames.HideVersionVPrefix,
                Text("隐藏版本 v 前缀", LocalizationKeys.SettingsItemHideVersionVPrefix),
                SettingItemKind.Boolean,
                SettingPersistence.UiSettings,
                SettingGroupKey.Advanced,
                description: Text("把 v2.0.0 显示为 2.0.0。", LocalizationKeys.SettingsItemHideVersionVPrefixDesc),
                saveHandler: vm => SaveItemAsync(vm)),
            new SettingItemViewModel(
                SettingsKeyNames.AutoSortUpdates,
                Text("可升级项自动置顶", LocalizationKeys.SettingsItemAutoSortUpdates),
                SettingItemKind.Boolean,
                SettingPersistence.UiSettings,
                SettingGroupKey.Advanced,
                description: Text("模组列表默认把有更新的项排在前面。", LocalizationKeys.SettingsItemAutoSortUpdatesDesc),
                saveHandler: vm => SaveItemAsync(vm)),
            new SettingItemViewModel(
                SettingsKeyNames.SuppressRecommendations,
                Text("不再询问推荐模组", LocalizationKeys.SettingsItemSuppressRecommendations),
                SettingItemKind.Boolean,
                SettingPersistence.UiSettings,
                SettingGroupKey.Advanced,
                description: Text("安装时跳过推荐模组选择框；可在推荐框内随时改回来。", LocalizationKeys.SettingsItemSuppressRecommendationsDesc),
                saveHandler: vm => SaveItemAsync(vm)),
            new SettingItemViewModel(
                SettingsKeyNames.MinimizeToTray,
                Text("最小化到托盘", LocalizationKeys.SettingsItemMinimizeToTray),
                SettingItemKind.Boolean,
                SettingPersistence.UiSettings,
                SettingGroupKey.Advanced,
                description: Text("最小化时只留托盘图标，不占任务栏。", LocalizationKeys.SettingsItemMinimizeToTrayDesc),
                saveHandler: vm => SaveItemAsync(vm)),
            new SettingItemViewModel(
                SettingsKeyNames.CloseToTray,
                Text("关闭到托盘", LocalizationKeys.SettingsItemCloseToTray),
                SettingItemKind.Boolean,
                SettingPersistence.UiSettings,
                SettingGroupKey.Advanced,
                description: Text("点关闭按钮时隐藏窗口而不退出；退出请用托盘菜单的「退出」。", LocalizationKeys.SettingsItemCloseToTrayDesc),
                saveHandler: vm => SaveItemAsync(vm)),
        };
        _items.AddRange(advancedItems);

        var advanced = new SettingGroupViewModel(
            SettingGroupKey.Advanced,
            Text("高级与诊断", LocalizationKeys.SettingsSectionAdvanced),
            Text("诊断信息可直接贴进 issue；目录按钮在文件管理器里打开对应位置。", LocalizationKeys.SettingsSectionAdvancedDesc),
            advancedItems,
            tokenEditor: null,
            diagnostics: _diagnostics);

        // 兼容性保留与规范引用 (I18N-04 / docs/ops/i18n-guide.md §6.2)
        _ = LocalizationKeys.SettingsLanguageLegacyWarning;
        _ = LocalizationKeys.SettingsLanguage;
        _ = LocalizationKeys.SettingsLanguageHint;

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
            SetLoadError(Text("读取界面设置失败", LocalizationKeys.SettingsErrorUiSettingsLoadFailed), ex.Message);
            return;
        }

        ApplyChoice(SettingsKeyNames.Theme, current.Theme, "dark");
        ApplyChoice(SettingsKeyNames.ThemeSkin, current.ThemeSkin, ThemeSkinKinds.Default);
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
        ApplyFlag(SettingsKeyNames.MasterMuted, current.MasterMuted);
        ApplyFlag(SettingsKeyNames.SoundEffectsEnabled, current.SoundEffectsEnabled);
        FindItem(SettingsKeyNames.SoundEffectsVolume)?.ApplyLoaded(Math.Round(current.SoundEffectsVolume * 100.0).ToString("0", CultureInfo.InvariantCulture));
        ApplyFlag(SettingsKeyNames.MusicEnabled, current.MusicEnabled);
        FindItem(SettingsKeyNames.MusicVolume)?.ApplyLoaded(Math.Round(current.MusicVolume * 100.0).ToString("0", CultureInfo.InvariantCulture));
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
        choices.Insert(1, new SettingChoice(text, Text("自定义（{0}）", LocalizationKeys.SettingsChoiceInstanceCustom, text)));
        presetItem.SetChoices(choices);
        presetItem.ApplyLoaded(text);
        customItem?.ApplyLoaded(text);
    }

    /// <summary>
    /// 背景来源相关的十一行共用一份 <see cref="BackgroundProfile"/>（20 篇 P-06 / P-07）：整份写盘，而不是
    /// 每行各存一个字段，这样「按实例覆盖」才能一次性搬走整组设置，也不会出现半新半旧的组合。
    /// </summary>
    /// <param name="current">刚读到的界面设置。</param>
    private void ApplyBackgroundRows(UiSettings current)
    {
        var (scope, profile) = ResolveBackgroundProfile(current);
        var clamped = profile.Clamp();

        ApplyChoice(
            SettingsKeyNames.BackgroundScene,
            BackgroundSceneKinds.Normalize(current.BackgroundScene),
            BackgroundSceneKinds.Starfield);
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
    /// 解析「当前生效」的背景：本实例有覆盖就用覆盖，否则跟随全局。
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
    /// 还是 <c>BackgroundByInstance[当前实例]</c>。
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

        item.MarkFailed(
                    Text(
                        "背景色需要 #RRGGBB 形式的十六进制（例如 #1B1F27）。",
                        LocalizationKeys.SettingsErrorBackgroundColorInvalid),
                    null,
                    retryable: false);
        StatusText = Text("{0}：保存失败", LocalizationKeys.SettingsStatusItemSaveFailed, item.DisplayName);
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

        var choices = new List<SettingChoice> { new(string.Empty, Text("不自动打开", LocalizationKeys.SettingsChoiceAutoStartInstanceNone)) };
        var known = value.Length == 0;
        foreach (var instance in EnumerateInstances())
        {
            choices.Add(new SettingChoice(instance.Name, instance.DisplayName));
            known |= string.Equals(instance.Name, value, StringComparison.Ordinal);
        }

        if (!known)
        {
            choices.Add(new SettingChoice(
                value,
                Text("{0}（不在当前实例列表中）", LocalizationKeys.SettingsChoiceInstanceNotInList, value)));
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
            _stabilityItem.MarkFailed(
                result.Message ?? Text("读取稳定版容差失败", LocalizationKeys.SettingsErrorStabilityReadFailed),
                result.Detail,
                result.Retryable);
        }
    }

    /// <summary>写实例级容差文件，并读回校验（与 Core 配置项同一套「改一项写一项 + 读回校验」）。</summary>
    private async Task SaveToInstanceFileAsync(SettingItemViewModel item, CancellationToken ct)
    {
        var instance = _instances?.Current;
        if (instance is null)
        {
            item.MarkFailed(
                Text("尚未打开实例", LocalizationKeys.SettingsErrorNoInstanceForStability),
                Text(
                    "稳定版容差是实例级文件，需要先打开一个实例",
                    LocalizationKeys.SettingsErrorNoInstanceForStabilityDetail),
                retryable: false);
            StatusText = ComposeFailedStatus(item, retryable: false);
            return;
        }

        var path = StabilityToleranceFile.PathFor(instance.GameDir);
        var write = await _stability.SaveOverallAsync(instance.GameDir, item.WireValue, ct).ConfigureAwait(true);
        if (!write.IsSuccess)
        {
            item.MarkFailed(
                write.Message ?? Text("保存稳定版容差失败", LocalizationKeys.SettingsErrorStabilityWriteFailed),
                write.Detail,
                write.Retryable);
            StatusText = ComposeFailedStatus(item, write.Retryable);
            return;
        }

        var read = await _stability.ReadAsync(path, ct).ConfigureAwait(true);
        if (!read.IsSuccess)
        {
            item.MarkFailed(
                read.Message ?? Text("读回稳定版容差失败", LocalizationKeys.SettingsErrorStabilityReadbackFailed),
                read.Detail,
                read.Retryable);
            StatusText = ComposeFailedStatus(item, read.Retryable);
            return;
        }

        if (!string.Equals(read.Value, item.WireValue, StringComparison.Ordinal))
        {
            item.MarkFailed(
                Text("写回校验不一致", LocalizationKeys.SettingsErrorStabilityReadbackMismatch),
                Text(
                    "读出 {0}，期望 {1}",
                    LocalizationKeys.SettingsErrorStabilityReadbackMismatchDetail,
                    read.Value,
                    item.WireValue),
                retryable: true);
            StatusText = ComposeFailedStatus(item, retryable: true);
            return;
        }

        item.MarkSaved();
        StatusText = ComposeSavedStatus(item);
    }

    private async Task OpenFirstRunWizardAsync()
    {
        if (FirstRunWizard is not { } launcher)
        {
            StatusText = Text("当前环境没有首次启动指引的通道", LocalizationKeys.SettingsErrorNoFirstRunChannel);
            return;
        }

        try
        {
            await launcher.LaunchAsync().ConfigureAwait(true);
        }
        catch (Exception)
        {
            // 向导自己会兜住业务失败；走到这里说明窗口都没弹出来，只能给一句可读的话。
            StatusText = Text("首次启动指引未能打开", LocalizationKeys.SettingsErrorFirstRunWizardFailed);
        }
    }

    private Task OpenCacheFolderAsync() =>
        OpenFolderAsync(_cacheDirItem?.EditValue, Text("缓存目录", LocalizationKeys.SettingsFolderCache));

    private Task OpenConfigFolderAsync() =>
        OpenFolderAsync(SettingsPaths.DefaultConfigDirectory, Text("配置目录", LocalizationKeys.SettingsFolderConfig));

    private async Task OpenFolderAsync(string? directory, string what)
    {
        if (_shellLauncher is null)
        {
            StatusText = Text("当前环境没有打开目录的通道", LocalizationKeys.SettingsErrorNoFolderChannel);
            return;
        }

        if (string.IsNullOrWhiteSpace(directory))
        {
            StatusText = Text(
                "{0}为空：请先填写目录并保存，或使用默认目录",
                LocalizationKeys.SettingsErrorFolderEmpty,
                what);
            return;
        }

        var result = await _shellLauncher.OpenFolderAsync(directory.Trim()).ConfigureAwait(true);
        StatusText = result.IsSuccess
            ? Text("已打开{0}", LocalizationKeys.SettingsStatusFolderOpened, what)
            : Text("打开{0}失败：{1}", LocalizationKeys.SettingsErrorFolderOpenFailed, what, result.Message);
    }

    private async Task CheckForUpdatesAsync()
    {
        if (_updateCheckService is null)
        {
            StatusText = Text("未配置更新检查服务", LocalizationKeys.SettingsErrorNoUpdateService);
            return;
        }

        StatusText = Text("正在检查更新…", LocalizationKeys.SettingsStatusCheckingUpdates);
        var includeDevBuilds = _devBuildsItem?.BooleanValue ?? false;
        var result = await _updateCheckService.CheckForUpdatesAsync(includeDevBuilds).ConfigureAwait(true);

        if (!result.IsSuccess || result.Value is null)
        {
            StatusText = Text("检查更新失败：{0}", LocalizationKeys.SettingsErrorUpdateCheckFailed, result.Message);
            return;
        }

        var updateInfo = result.Value;
        if (updateInfo.HasUpdate)
        {
            StatusText = Text(
                "发现新版本 {0}（当前版本：{1}）",
                LocalizationKeys.SettingsStatusUpdateAvailable,
                updateInfo.LatestVersion,
                updateInfo.CurrentVersion);

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
            StatusText = Text(
                "已是最新版本（{0}）",
                LocalizationKeys.SettingsStatusUpToDate,
                updateInfo.CurrentVersion);
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

            case SettingsKeyNames.ThemeSkin:
                next = current with { ThemeSkin = ThemeSkinKinds.Normalize(item.EditValue) };
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
                    StatusText = Text("{0}：保存失败", LocalizationKeys.SettingsStatusItemSaveFailed, item.DisplayName);
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
                    item.MarkFailed(
                        Text(
                            "没有当前实例，无法按实例保存强调色；请先添加或切换实例。",
                            LocalizationKeys.SettingsErrorNoInstanceForAccent),
                        null,
                        retryable: false);
                    StatusText = Text("{0}：保存失败", LocalizationKeys.SettingsStatusItemSaveFailed, item.DisplayName);
                    return;
                }

                next = SaveAccentScope(perInstance);
                break;
            }

            case SettingsKeyNames.BackgroundScene:
                next = current with { BackgroundScene = BackgroundSceneKinds.Normalize(item.EditValue) };
                break;

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
                    item.MarkFailed(
                        Text("找不到图片文件：{0}", LocalizationKeys.SettingsErrorBackgroundImageMissing, path),
                        null,
                        retryable: true);
                    StatusText = Text("{0}：保存失败", LocalizationKeys.SettingsStatusItemSaveFailed, item.DisplayName);
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
                    item.MarkFailed(
                        Text(
                            "没有当前实例，无法按实例保存背景；请先添加或切换实例。",
                            LocalizationKeys.SettingsErrorNoInstanceForBackground),
                        null,
                        retryable: false);
                    StatusText = Text("{0}：保存失败", LocalizationKeys.SettingsStatusItemSaveFailed, item.DisplayName);
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

            case SettingsKeyNames.MasterMuted:
                next = current with { MasterMuted = item.BooleanValue };
                break;

            case SettingsKeyNames.SoundEffectsEnabled:
                next = current with { SoundEffectsEnabled = item.BooleanValue };
                break;

            case SettingsKeyNames.SoundEffectsVolume:
                next = current with { SoundEffectsVolume = Math.Clamp(ReadDouble(item.EditValue, current.SoundEffectsVolume * 100.0) / 100.0, 0.0, 1.0) };
                break;

            case SettingsKeyNames.MusicEnabled:
                next = current with { MusicEnabled = item.BooleanValue };
                break;

            case SettingsKeyNames.MusicVolume:
                next = current with { MusicVolume = Math.Clamp(ReadDouble(item.EditValue, current.MusicVolume * 100.0) / 100.0, 0.0, 1.0) };
                break;

            default:
                item.MarkFailed(
                    Text("未知的界面设置项：{0}", LocalizationKeys.SettingsErrorUnknownUiSetting, item.Key),
                    null,
                    retryable: false);
                StatusText = Text("{0}：保存失败", LocalizationKeys.SettingsStatusItemSaveFailed, item.DisplayName);
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

        item.MarkFailed(
                result.Message ?? Text("保存界面设置失败", LocalizationKeys.SettingsErrorUiSettingSaveFailed),
                result.Detail,
                result.Retryable);
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
        StatusText = Text("读取设置失败", LocalizationKeys.SettingsErrorLoadFailed);
        OnPropertyChanged(nameof(HasLoadError));
    }

    private string ComposeSavedStatus(SettingItemViewModel item) =>
        item.RequiresRestart
            ? Text("{0}：已保存，重启后生效", LocalizationKeys.SettingsStatusItemSavedRestart, item.DisplayName)
            : Text("{0}：已保存", LocalizationKeys.SettingsStatusItemSaved, item.DisplayName);

    private string ComposeFailedStatus(SettingItemViewModel item, bool retryable) =>
        retryable
            ? Text("{0}：保存失败，可重试", LocalizationKeys.SettingsStatusItemSaveFailedRetryable, item.DisplayName)
            : Text("{0}：保存失败", LocalizationKeys.SettingsStatusItemSaveFailed, item.DisplayName);

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

/// <summary>设置页首进时序探针：只在 <c>CKANUI_SETTINGS_TIMING</c> 指向文件时写盘，生产路径零开销。</summary>
/// <remarks>
/// 与 NavigationProbe 的 <c>[nav]</c> 行对齐：<c>getAll</c> 是服务层读全部设置，
/// <c>apply</c> 是把值灌进 36 行（属性通知），<c>finish</c> 是令牌/诊断/脏标记走完，
/// <c>isLoading-off</c> 是骨架撤掉、视图开始实体化内容的时刻——此后到 <c>[nav]</c> 报的
/// <c>layout</c> 之间就是「控件实体化 + 首次布局」的成本。视图模型层不引用 Avalonia，故只测到这里。
/// </remarks>
internal sealed class SettingsTiming
{
    private static readonly string? SinkPath = Environment.GetEnvironmentVariable("CKANUI_SETTINGS_TIMING");

    private readonly System.Diagnostics.Stopwatch _watch = System.Diagnostics.Stopwatch.StartNew();
    private readonly List<string> _stages = [];

    private SettingsTiming()
    {
    }

    /// <summary>是否开启（未设置环境变量时所有方法立即返回）。</summary>
    public static bool Enabled => !string.IsNullOrWhiteSpace(SinkPath);

    /// <summary>开始一段计时。</summary>
    public static SettingsTiming Start() => new();

    /// <summary>记一个阶段。</summary>
    public void Mark(string stage)
    {
        if (!Enabled)
        {
            return;
        }

        _stages.Add(string.Create(CultureInfo.InvariantCulture, $"{stage}={_watch.Elapsed.TotalMilliseconds:F1}ms"));
    }

    /// <summary>记下最终挂载的分组数与行数（验证没有漏挂/重复挂）。</summary>
    /// <param name="groups">已挂载的分组数。</param>
    /// <param name="rows">已挂载的设置行总数。</param>
    public void MarkMounted(int groups, int rows)
    {
        if (!Enabled)
        {
            return;
        }

        _stages.Add(string.Create(CultureInfo.InvariantCulture, $"groups={groups} rows={rows}"));
    }

    /// <summary>把整行写盘（未开启时立即返回）。</summary>
    public void Flush()
    {
        if (!Enabled)
        {
            return;
        }

        _stages.Add(string.Create(CultureInfo.InvariantCulture, $"total={_watch.Elapsed.TotalMilliseconds:F1}ms"));

        try
        {
            File.AppendAllText(SinkPath!, string.Create(CultureInfo.InvariantCulture, $"[settings] {string.Join(' ', _stages)}{Environment.NewLine}"));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // 探针写盘失败不能影响页面本身。
        }
    }
}
