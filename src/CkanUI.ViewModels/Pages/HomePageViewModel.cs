using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CkanUI.Localization;
using CkanUI.Services.Abstractions;
using CkanUI.Services.Abstractions.Dtos;
using CkanUI.Services.Abstractions.Home;
using CkanUI.ViewModels.Navigation;
using CkanUI.ViewModels.Pages.Home;
using CkanUI.ViewModels.Pages.Instances;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace CkanUI.ViewModels.Pages;

/// <summary>
/// 主菜单（应用启动后的落地页）：当前实例概览 + 常用功能入口 + 当前实例的游戏截图。
/// </summary>
/// <remarks>
/// <para>单页纵向布局：实例概览回答「我现在在哪个实例上」；快捷操作按主次分三层——两张大卡放核心工作流、
/// 三张普通卡放常用页面、两个胶囊放工具页，避免七张同规格卡平铺成一片；最下面是自动滚动的截图胶片带。</para>
/// <para>截图只做「枚举 + 排序 + 排版文案」：解码（缩略图）在 App 层，VM 只暴露路径，
/// 于是这一页不需要任何 Avalonia 依赖。</para>
/// <para>宿主能力（<see cref="ShellLauncher"/>、<see cref="CommandStore"/>）走可注入属性，
/// 由视图的 <c>??=</c> 注入——容器里没有「起进程」这类平台实现。</para>
/// </remarks>
public sealed partial class HomePageViewModel : PageViewModel
{
    /// <summary>截图页签一次取多少张：首屏一屏能看完，又不至于把大目录整个读进来。</summary>
    private const int ScreenshotLimit = 12;

    /// <summary>实例概览部件的布局身份。</summary>
    public const string InstanceWidgetId = "instance";

    /// <summary>快捷操作部件的布局身份。</summary>
    public const string QuickActionsWidgetId = "quick-actions";

    /// <summary>最近截图部件的布局身份。</summary>
    public const string ScreenshotsWidgetId = "screenshots";

    /// <summary>模组更新部件的布局身份。</summary>
    public const string ModUpdatesWidgetId = "mod-updates";

    /// <summary>安装历史部件的布局身份。</summary>
    public const string InstallHistoryWidgetId = "install-history";

    /// <summary>下载缓存部件的布局身份。</summary>
    public const string CacheWidgetId = "cache";

    /// <summary>存档部件的布局身份。</summary>
    public const string SavesWidgetId = "saves";

    /// <summary>后台任务部件的布局身份。</summary>
    public const string OperationsWidgetId = "operations";

    /// <summary>宿主自带的数据部件清单：添加菜单的顺序就是这里的顺序。</summary>
    private static readonly HomeWidgetSpec[] FeedWidgetSpecs =
    {
        new(ModUpdatesWidgetId, "模组更新", LocalizationKeys.HomeWidgetModUpdates, "有更新的已安装模组。", LocalizationKeys.HomeWidgetModUpdatesDesc, "Sync", "mods"),
        new(InstallHistoryWidgetId, "安装历史", LocalizationKeys.HomeWidgetInstallHistory, "最近的安装、升级与卸载。", LocalizationKeys.HomeWidgetInstallHistoryDesc, "History", "installhistory"),
        new(CacheWidgetId, "下载缓存", LocalizationKeys.HomeWidgetCache, "缓存条目、占用与上限。", LocalizationKeys.HomeWidgetCacheDesc, "Storage", "cache"),
        new(SavesWidgetId, "存档", LocalizationKeys.HomeWidgetSaves, "最近存档、备份与写前快照。", LocalizationKeys.HomeWidgetSavesDesc, "Archive", "saves"),
        new(OperationsWidgetId, "后台任务", LocalizationKeys.HomeWidgetOperations, "正在跑与排队中的操作。", LocalizationKeys.HomeWidgetOperationsDesc, "Clock", null),
    };

    private static readonly string[] DefaultWidgetLayout =
    {
        InstanceWidgetId,
        QuickActionsWidgetId,
        ScreenshotsWidgetId,
    };

    private readonly IInstanceService? _instances;
    private readonly IGameScreenshotService? _screenshots;
    private readonly INavigationService? _navigation;
    private readonly IPluginService? _plugins;
    private readonly IUiSettingsStore? _settings;
    private readonly IRepositoryService? _repository;
    private readonly IInstallHistoryService? _history;
    private readonly ICacheService? _cache;
    private readonly ISaveFileService? _saves;
    private readonly IOperationQueue? _queue;
    private readonly SynchronizationContext? _sync = SynchronizationContext.Current;
    private readonly Dictionary<string, HomeWidgetViewModel> _builtInWidgets = new(StringComparer.Ordinal);
    private readonly Dictionary<string, PluginHomeWidgetViewModel> _pluginWidgets = new(StringComparer.OrdinalIgnoreCase);

    private List<string> _layoutIds = new(DefaultWidgetLayout);
    private bool _widgetsInitialized;
    private bool _widgetsSubscribed;
    private bool _queueSubscribed;
    private int _widgetRefreshGeneration;
    private int _dataWidgetRefreshGeneration;

    private bool _subscribed;

    /// <summary>设计时（XAML 预览）用的空实现。</summary>
    public HomePageViewModel()
        : this(null, null, null)
    {
    }

    /// <summary>运行时构造：实例清单 + 截图枚举 + 页面导航。</summary>
    /// <param name="instances">实例清单（提供当前实例与切换通知）。</param>
    /// <param name="screenshots">截图枚举服务。</param>
    /// <param name="navigation">页面导航（快捷操作卡片与「切换实例」用）。</param>
    public HomePageViewModel(
        IInstanceService? instances,
        IGameScreenshotService? screenshots,
        INavigationService? navigation = null,
        IPluginService? plugins = null,
        IUiSettingsStore? settings = null,
        IRepositoryService? repository = null,
        IInstallHistoryService? history = null,
        ICacheService? cache = null,
        ISaveFileService? saves = null,
        IOperationQueue? queue = null)
    {
        _instances = instances;
        _screenshots = screenshots;
        _navigation = navigation;
        _plugins = plugins;
        _settings = settings;
        _repository = repository;
        _history = history;
        _cache = cache;
        _saves = saves;
        _queue = queue;
        Instance = instances?.Current;
        BuildQuickActions();
    }

    /// <inheritdoc />
    public override string Title => Text("主页", LocalizationKeys.NavRailHome);

    /// <summary>外壳动作：打开目录 / 启动游戏。由视图注入。</summary>
    public IInstanceShellLauncher? ShellLauncher { get; set; }

    /// <summary>实例启动命令的持久化（<c>GUIConfig.json</c>）。由视图注入。</summary>
    public IInstanceCommandStore? CommandStore { get; set; }

    /// <summary>核心工作流：模组库、变更集（页面上排成两张大卡）。</summary>
    public ObservableCollection<HomeQuickActionViewModel> PrimaryActions { get; } = new();

    /// <summary>常用页面：实例管理、存档、模组仓库（三张普通卡）。</summary>
    public ObservableCollection<HomeQuickActionViewModel> PageActions { get; } = new();

    /// <summary>工具页：日志、设置（两个紧凑胶囊，不与上面的卡抢视觉重量）。</summary>
    public ObservableCollection<HomeQuickActionViewModel> ToolActions { get; } = new();

    /// <summary>截图列表（按拍摄时间倒序，最多 <see cref="ScreenshotLimit"/> 张）。</summary>
    public ObservableCollection<HomeScreenshotRowViewModel> Screenshots { get; } = new();

    /// <summary>当前主页上的部件（顺序即布局顺序，只含此刻可用的部件）。</summary>
    public ObservableCollection<HomeWidgetViewModel> Widgets { get; } = new();

    /// <summary>还没放到主页上的部件（内置三项 + 已激活插件声明且尚未添加的部件）。</summary>
    public ObservableCollection<HomeWidgetOptionViewModel> AvailableWidgets { get; } = new();

    /// <summary>「添加部件」下拉当前选中项。</summary>
    [ObservableProperty]
    private HomeWidgetOptionViewModel? _selectedAvailableWidget;

    /// <summary>有没有可添加的部件。</summary>
    public bool HasAvailableWidgets => AvailableWidgets.Count > 0;

    /// <summary>主页是否一个部件都没有（用户可以把它们全部移除）。</summary>
    public bool ShowNoWidgets => Widgets.Count == 0;

    /// <summary>部件数量文案。</summary>
    public string WidgetCountText => Text("{0} 个部件", LocalizationKeys.HomeWidgetCount, Widgets.Count);

    /// <summary>是否至少有一个可用部件可以添加。</summary>
    public bool CanAddWidget => SelectedAvailableWidget is not null;

    /// <summary>当前实例；没有实例时为 null。</summary>
    [ObservableProperty]
    private InstanceInfo? _instance;

    /// <summary>错误条文案（空串表示不显示）。</summary>
    [ObservableProperty]
    private string _errorText = string.Empty;

    /// <summary>状态条文案（启动成功等一次性反馈）。</summary>
    [ObservableProperty]
    private string _statusText = string.Empty;

    /// <summary>是否正在枚举截图。</summary>
    [ObservableProperty]
    private bool _isBusy;

    /// <summary>是否有可用实例。</summary>
    public bool HasInstance => Instance is not null;

    /// <summary>没有实例时显示空态引导。</summary>
    public bool ShowNoInstance => Instance is null;

    /// <summary>是否显示截图胶片带：有实例且确实取到了图才占位，空的时候不白留一条横带。</summary>
    public bool ShowScreenshotRail => HasInstance && Screenshots.Count > 0;

    /// <summary>是否显示状态条。</summary>
    public bool HasStatus => !string.IsNullOrWhiteSpace(StatusText);

    /// <summary>是否显示错误条。</summary>
    public bool HasError => !string.IsNullOrWhiteSpace(ErrorText);

    /// <summary>实例被占用（游戏可能正在运行）时不允许再启动一次。</summary>
    public bool CanLaunch => Instance is { IsLocked: false };

    /// <summary>显示「实例被占用」的说明。</summary>
    public bool ShowLockedHint => Instance is { IsLocked: true };

    /// <summary>是否给实例名旁边挂「默认实例」徽标。</summary>
    public bool ShowDefaultBadge => Instance is { IsDefault: true };

    /// <summary>实例显示名（没有实例时给空态标题）。</summary>
    public string InstanceName => Instance?.DisplayName ?? Text("还没有可用实例", LocalizationKeys.HomeNoInstanceTitle);

    /// <summary>游戏版本（缺失时给「版本未知」）。</summary>
    public string InstanceVersionText => string.IsNullOrWhiteSpace(Instance?.GameVersion)
        ? Text("版本未知", LocalizationKeys.HomeVersionUnknown)
        : Instance!.GameVersion!;

    /// <summary>模组数文案。</summary>
    public string ModuleCountText => Instance?.ModuleCount is int count
        ? Text("{0} 个模组", LocalizationKeys.HomeModuleCount, count)
        : Text("模组数未知", LocalizationKeys.HomeModuleCountUnknown);

    /// <summary>游戏目录（没有实例时为空串）。</summary>
    public string GameDirText => Instance?.GameDir ?? string.Empty;

    /// <summary>截图目录（没有实例或没有截图服务时为空串）。</summary>
    public string ScreenshotFolderText => Instance?.GameDir is { Length: > 0 } gameDir && _screenshots is not null
        ? _screenshots.GetScreenshotsDirectory(gameDir)
        : string.Empty;

    /// <summary>截图张数文案。</summary>
    public string ScreenshotCountText => Text("{0} 张截图", LocalizationKeys.HomeScreenshotCount, Screenshots.Count);

    /// <summary>没有截图（且不在读取中）时显示空态。</summary>
    public bool ShowEmptyScreenshots => Screenshots.Count == 0 && !IsBusy;

    /// <inheritdoc />
    protected override void OnActivated()
    {
        if (_instances is not null && !_subscribed)
        {
            _instances.CurrentChanged += OnCurrentChanged;
            _subscribed = true;
        }

        SyncInstance(_instances?.Current);
        BuildQuickActions();
        EnsureWidgetsInitialized();
        SubscribePluginWidgets();
        SubscribeQueue();
        RefreshScreenshotsCommand.Execute(null);
        _ = RefreshWidgetsAsync();
    }

    /// <inheritdoc />
    protected override void OnDeactivated()
    {
        if (_instances is not null && _subscribed)
        {
            _instances.CurrentChanged -= OnCurrentChanged;
            _subscribed = false;
        }

        if (_plugins is not null && _widgetsSubscribed)
        {
            _plugins.Changed -= OnPluginsChanged;
            _widgetsSubscribed = false;
        }

        if (_queue is not null && _queueSubscribed)
        {
            _queue.Updated -= OnQueueUpdated;
            _queueSubscribed = false;
        }
    }

    /// <summary>切换实例后：概览区与截图一起换成本实例的。</summary>
    private void OnCurrentChanged(InstanceInfo? current)
    {
        SyncInstance(current);
        RefreshScreenshotsCommand.Execute(null);
        _ = RefreshWidgetsAsync();
    }

    /// <summary>把当前实例写进可绑定字段（派生属性由 <see cref="OnInstanceChanged"/> 统一通知）。</summary>
    private void SyncInstance(InstanceInfo? current) => Instance = current;

    /// <summary>实例变化后统一通知派生属性（源生成器只通知 <see cref="Instance"/> 本身）。</summary>
    partial void OnInstanceChanged(InstanceInfo? value)
    {
        OnPropertyChanged(nameof(HasInstance));
        OnPropertyChanged(nameof(ShowNoInstance));
        OnPropertyChanged(nameof(CanLaunch));
        OnPropertyChanged(nameof(ShowLockedHint));
        OnPropertyChanged(nameof(ShowDefaultBadge));
        OnPropertyChanged(nameof(InstanceName));
        OnPropertyChanged(nameof(InstanceVersionText));
        OnPropertyChanged(nameof(ModuleCountText));
        OnPropertyChanged(nameof(GameDirText));
        OnPropertyChanged(nameof(ScreenshotFolderText));
        OnPropertyChanged(nameof(ShowScreenshotRail));
        LaunchGameCommand.NotifyCanExecuteChanged();
        OpenGameFolderCommand.NotifyCanExecuteChanged();
        OpenScreenshotsFolderCommand.NotifyCanExecuteChanged();
    }

    partial void OnStatusTextChanged(string value) => OnPropertyChanged(nameof(HasStatus));

    partial void OnErrorTextChanged(string value) => OnPropertyChanged(nameof(HasError));

    partial void OnIsBusyChanged(bool value) => OnPropertyChanged(nameof(ShowEmptyScreenshots));

    /// <summary>按实例的启动命令启动游戏（优先用实例里保存的，其次用本机推断的默认值）。</summary>
    [RelayCommand(CanExecute = nameof(CanLaunch))]
    private async Task LaunchGameAsync()
    {
        SetError(null);
        StatusText = string.Empty;
        if (Instance is not { } instance)
        {
            return;
        }

        if (ShellLauncher is null)
        {
            SetError(Text("启动通道尚未接入（未注入外壳启动器），可先用「打开游戏目录」自行启动。", LocalizationKeys.HomeErrorLaunchMissing));
            return;
        }

        var command = await ResolveLaunchCommandAsync(instance.GameDir).ConfigureAwait(true);
        if (string.IsNullOrWhiteSpace(command))
        {
            SetError(Text("该实例没有可用的启动命令，请到「实例管理」里编辑启动命令。", LocalizationKeys.HomeErrorNoLaunchCommand));
            return;
        }

        var result = await ShellLauncher.LaunchGameAsync(instance.GameDir, command!).ConfigureAwait(true);
        if (result.IsSuccess)
        {
            StatusText = Text("已启动：{0}", LocalizationKeys.HomeStatusLaunched, command);
            return;
        }

        SetError(
            InstancesErrorText.Compose(
                result.Message,
                Text("启动游戏失败", LocalizationKeys.HomeErrorLaunchFailed),
                result.Retryable),
            result.Detail);
    }

    /// <summary>
    /// 取第一条可用启动命令；没有就返回 null。
    /// 回落链只有一份实现（顶栏常驻按钮也调它），否则同一个实例会出现两种答案。
    /// </summary>
    private Task<string?> ResolveLaunchCommandAsync(string gameDir)
        => InstanceLaunchCommandResolver.ResolveAsync(CommandStore, gameDir);

    /// <summary>在文件管理器里打开游戏目录。</summary>
    [RelayCommand(CanExecute = nameof(HasInstance))]
    private Task OpenGameFolderAsync()
        => OpenFolderAsync(GameDirText, "当前实例没有可打开的游戏目录。");

    /// <summary>在文件管理器里打开截图目录。</summary>
    [RelayCommand(CanExecute = nameof(HasInstance))]
    private Task OpenScreenshotsFolderAsync()
        => OpenFolderAsync(ScreenshotFolderText, "当前实例没有可打开的游戏目录。");

    private async Task OpenFolderAsync(string? path, string emptyMessage)
    {
        SetError(null);
        if (string.IsNullOrWhiteSpace(path))
        {
            SetError(emptyMessage);
            return;
        }

        if (ShellLauncher is null)
        {
            SetError(Text("打开目录的能力尚未接入（未注入外壳启动器）。", LocalizationKeys.HomeErrorLauncherMissing));
            return;
        }

        var result = await ShellLauncher.OpenFolderAsync(path!).ConfigureAwait(true);
        if (!result.IsSuccess)
        {
            SetError(
                InstancesErrorText.Compose(
                    result.Message,
                    Text("打开目录失败", LocalizationKeys.HomeErrorOpenFolderFailed),
                    result.Retryable),
                result.Detail);
        }
    }

    /// <summary>重新枚举当前实例的截图目录。</summary>
    [RelayCommand]
    private async Task RefreshScreenshotsAsync()
    {
        Screenshots.Clear();
        if (_screenshots is null || Instance?.GameDir is not { Length: > 0 } gameDir)
        {
            NotifyScreenshotState();
            return;
        }

        IsBusy = true;
        try
        {
            var result = await _screenshots.ListAsync(gameDir, ScreenshotLimit).ConfigureAwait(true);
            // 枚举期间实例被切走：这批结果属于上一个实例，直接丢弃。
            if (!string.Equals(Instance?.GameDir, gameDir, StringComparison.Ordinal))
            {
                return;
            }

            if (!result.IsSuccess)
            {
                SetError(
                    InstancesErrorText.Compose(
                        result.Message,
                        Text("读取截图失败", LocalizationKeys.HomeErrorLoadScreenshots),
                        result.Retryable),
                    result.Detail);
                return;
            }

            foreach (var item in result.Value ?? Array.Empty<GameScreenshotInfo>())
            {
                Screenshots.Add(CreateRow(item));
            }
        }
        catch (Exception exception)
        {
            SetError(InstancesErrorText.FromException(
                exception,
                Text("读取截图失败", LocalizationKeys.HomeErrorLoadScreenshots)));
        }
        finally
        {
            IsBusy = false;
            NotifyScreenshotState();
        }
    }

    private HomeScreenshotRowViewModel CreateRow(GameScreenshotInfo item)
        => new(
            item.FilePath,
            item.FileName,
            Text("拍摄于 {0}", LocalizationKeys.HomeScreenshotCapturedAt, FormatCapturedAt(item.CapturedAt)),
            Text("{0} MB", LocalizationKeys.HomeScreenshotSizeMb, FormatMegabytes(item.Bytes)));

    private static string FormatCapturedAt(DateTimeOffset stamp)
        => stamp.ToLocalTime().ToString("yyyy-MM-dd HH:mm", CultureInfo.CurrentCulture);

    private static string FormatMegabytes(long bytes)
        => (bytes / (1024d * 1024d)).ToString("0.#", CultureInfo.CurrentCulture);

    /// <summary>跳到另一页（快捷操作卡片与「切换实例」共用）。</summary>
    [RelayCommand]
    private async Task NavigateAsync(string? pageKey)
    {
        if (_navigation is null || string.IsNullOrWhiteSpace(pageKey))
        {
            return;
        }

        var navigated = await _navigation.NavigateAsync(pageKey!).ConfigureAwait(true);

        // 卡片指向的页面可能住在可停用的插件里（存档就是），插件关掉后这个键解析不出页面。
        // 与其留一个点了没反应的按钮，不如退到它的归属分节——「实例」页能把当前可用的页签摆出来。
        if (!navigated && !string.Equals(pageKey, "instances", StringComparison.Ordinal))
        {
            await _navigation.NavigateAsync("instances").ConfigureAwait(true);
        }
    }

    /// <summary>把七张常用功能卡建好（卡片内容是静态的，建一次就够）。</summary>
    private void BuildQuickActions()
    {
        if (PrimaryActions.Count > 0)
        {
            return;
        }

        PrimaryActions.Add(CreateAction(
            "模组库", LocalizationKeys.HomeActionMods,
            "搜索、安装与更新模组", LocalizationKeys.HomeActionModsDesc,
            "Apps", "mods"));
        PrimaryActions.Add(CreateAction(
            "变更集", LocalizationKeys.HomeActionChanges,
            "查看待应用的安装计划", LocalizationKeys.HomeActionChangesDesc,
            "Sync", "changes"));
        PageActions.Add(CreateAction(
            "实例管理", LocalizationKeys.HomeActionInstances,
            "切换、克隆或添加实例", LocalizationKeys.HomeActionInstancesDesc,
            "Folder", "instances"));
        PageActions.Add(CreateAction(
            "存档", LocalizationKeys.HomeActionSaves,
            "浏览与备份游戏存档", LocalizationKeys.HomeActionSavesDesc,
            "Archive", "saves"));
        PageActions.Add(CreateAction(
            "模组仓库", LocalizationKeys.HomeActionRepos,
            "管理 CKAN 仓库源", LocalizationKeys.HomeActionReposDesc,
            "Globe", "repos"));
        ToolActions.Add(CreateAction(
            "日志", LocalizationKeys.HomeActionLogs,
            "查看运行日志与错误", LocalizationKeys.HomeActionLogsDesc,
            "Bug", "logs"));
        ToolActions.Add(CreateAction(
            "设置", LocalizationKeys.HomeActionSettings,
            "语言、背景与兼容性选项", LocalizationKeys.HomeActionSettingsDesc,
            "Settings", "settings"));
    }

    private HomeQuickActionViewModel CreateAction(
        string titleFallback,
        string titleKey,
        string descriptionFallback,
        string descriptionKey,
        string glyph,
        string pageKey)
        => new(
            Text(titleFallback, titleKey),
            Text(descriptionFallback, descriptionKey),
            glyph,
            new AsyncRelayCommand(() => NavigateAsync(pageKey)));

    /// <summary>首次进入主页时装载布局；之后每次进入只重建当前可用的部件。</summary>
    private void EnsureWidgetsInitialized()
    {
        if (_widgetsInitialized)
        {
            RebuildWidgets();
            return;
        }

        _widgetsInitialized = true;
        IReadOnlyList<string>? stored = null;
        try
        {
            stored = _settings?.Load().HomeWidgets;
        }
        catch (Exception)
        {
            // 设置文件读不出来不是主页不能用的理由：退回默认布局，下一次保存会覆盖坏值。
            stored = null;
        }

        _layoutIds = stored is null
            ? new List<string>(DefaultWidgetLayout)
            : SanitizeLayout(stored);
        RebuildWidgets();
    }

    /// <summary>去空、去重、丢掉不认识的内置键；插件键保留（插件停用期间也要保住它的位置）。</summary>
    private static List<string> SanitizeLayout(IEnumerable<string> stored)
    {
        var result = new List<string>();
        foreach (var raw in stored)
        {
            var id = raw?.Trim();
            if (string.IsNullOrEmpty(id))
            {
                continue;
            }

            var isKnownBuiltIn = IsFeedWidgetId(id) || id is InstanceWidgetId or QuickActionsWidgetId or ScreenshotsWidgetId;
            var isPlugin = id.StartsWith("plugin:", StringComparison.OrdinalIgnoreCase)
                && id.Split(':').Length == 3;
            if (!isKnownBuiltIn && !isPlugin)
            {
                continue;
            }

            if (result.Any(existing => string.Equals(existing, id, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            result.Add(id);
        }

        return result;
    }

    /// <summary>按布局身份重建可见部件；插件部件对象尽量复用，避免每次插件状态变化都闪一下。</summary>
    private void RebuildWidgets()
    {
        var bindings = new Dictionary<string, PluginHomeWidgetBinding>(StringComparer.OrdinalIgnoreCase);
        if (_plugins is not null)
        {
            foreach (var binding in _plugins.ActiveHomeWidgets)
            {
                if (binding?.Widget is null || string.IsNullOrWhiteSpace(binding.PluginId))
                {
                    continue;
                }

                bindings[PluginWidgetId(binding.PluginId, binding.Widget.Key)] = binding;
            }
        }

        Widgets.Clear();
        foreach (var id in _layoutIds)
        {
            switch (id)
            {
                case InstanceWidgetId:
                    Widgets.Add(GetBuiltInWidget(id, () => new InstanceHomeWidgetViewModel(this, Text("实例概览", LocalizationKeys.HomeWidgetInstance))));
                    break;
                case QuickActionsWidgetId:
                    Widgets.Add(GetBuiltInWidget(id, () => new QuickActionsHomeWidgetViewModel(this, Text("快捷操作", LocalizationKeys.HomeTabQuickActions))));
                    break;
                case ScreenshotsWidgetId:
                    Widgets.Add(GetBuiltInWidget(id, () => new ScreenshotsHomeWidgetViewModel(this, Text("游戏截图", LocalizationKeys.HomeTabScreenshots))));
                    break;
                case ModUpdatesWidgetId:
                case InstallHistoryWidgetId:
                case CacheWidgetId:
                case SavesWidgetId:
                case OperationsWidgetId:
                    Widgets.Add(GetFeedWidget(id));
                    break;
                default:
                    if (bindings.TryGetValue(id, out var binding))
                    {
                        if (!_pluginWidgets.TryGetValue(id, out var widget))
                        {
                            widget = new PluginHomeWidgetViewModel(this, binding.PluginId, binding.PluginName, binding.Widget);
                            _pluginWidgets[id] = widget;
                        }

                        if (!Widgets.Contains(widget))
                        {
                            Widgets.Add(widget);
                        }
                    }

                    break;
            }
        }

        // 插件被卸载后它的对象没有复用价值；只保留仍在布局里的。
        foreach (var stale in _pluginWidgets.Keys
                     .Where(key => !_layoutIds.Contains(key, StringComparer.OrdinalIgnoreCase))
                     .ToArray())
        {
            _pluginWidgets.Remove(stale);
        }

        RefreshAvailableWidgets();
        OnPropertyChanged(nameof(ShowNoWidgets));
        OnPropertyChanged(nameof(WidgetCountText));
    }

    private HomeWidgetViewModel GetBuiltInWidget(string id, Func<HomeWidgetViewModel> factory)
    {
        if (!_builtInWidgets.TryGetValue(id, out var widget))
        {
            widget = factory();
            _builtInWidgets[id] = widget;
        }

        return widget;
    }

    private void RefreshAvailableWidgets()
    {
        var selectedId = SelectedAvailableWidget?.Id;
        AvailableWidgets.Clear();

        if (!_layoutIds.Contains(InstanceWidgetId, StringComparer.OrdinalIgnoreCase))
        {
            AvailableWidgets.Add(new HomeWidgetOptionViewModel(
                InstanceWidgetId,
                Text("实例概览", LocalizationKeys.HomeWidgetInstance),
                Text("当前实例、启动游戏与打开目录。", LocalizationKeys.HomeWidgetInstanceDesc),
                "Folder",
                IsPlugin: false));
        }

        if (!_layoutIds.Contains(QuickActionsWidgetId, StringComparer.OrdinalIgnoreCase))
        {
            AvailableWidgets.Add(new HomeWidgetOptionViewModel(
                QuickActionsWidgetId,
                Text("快捷操作", LocalizationKeys.HomeTabQuickActions),
                Text("模组库、变更集与常用页面入口。", LocalizationKeys.HomeWidgetQuickActionsDesc),
                "Apps",
                IsPlugin: false));
        }

        if (!_layoutIds.Contains(ScreenshotsWidgetId, StringComparer.OrdinalIgnoreCase))
        {
            AvailableWidgets.Add(new HomeWidgetOptionViewModel(
                ScreenshotsWidgetId,
                Text("游戏截图", LocalizationKeys.HomeTabScreenshots),
                Text("当前实例最近 12 张游戏截图。", LocalizationKeys.HomeWidgetScreenshotsDesc),
                "Image",
                IsPlugin: false));
        }

        foreach (var spec in FeedWidgetSpecs)
        {
            if (_layoutIds.Contains(spec.Id, StringComparer.OrdinalIgnoreCase))
            {
                continue;
            }

            AvailableWidgets.Add(new HomeWidgetOptionViewModel(
                spec.Id,
                Text(spec.TitleFallback, spec.TitleKey),
                Text(spec.DescriptionFallback, spec.DescriptionKey),
                spec.Glyph,
                IsPlugin: false));
        }

        if (_plugins is not null)
        {
            foreach (var binding in _plugins.ActiveHomeWidgets)
            {
                if (binding?.Widget is null || string.IsNullOrWhiteSpace(binding.PluginId))
                {
                    continue;
                }

                var id = PluginWidgetId(binding.PluginId, binding.Widget.Key);
                if (_layoutIds.Contains(id, StringComparer.OrdinalIgnoreCase))
                {
                    continue;
                }

                AvailableWidgets.Add(new HomeWidgetOptionViewModel(
                    id,
                    string.IsNullOrWhiteSpace(binding.Widget.Title) ? binding.PluginName : binding.Widget.Title,
                    string.IsNullOrWhiteSpace(binding.Widget.Description)
                        ? Text("由插件「{0}」提供。", LocalizationKeys.HomeWidgetPluginDesc, binding.PluginName)
                        : binding.Widget.Description!,
                    string.IsNullOrWhiteSpace(binding.Widget.Glyph) ? "Puzzle" : binding.Widget.Glyph!,
                    IsPlugin: true));
            }
        }

        SelectedAvailableWidget = selectedId is null
            ? AvailableWidgets.FirstOrDefault()
            : AvailableWidgets.FirstOrDefault(option => string.Equals(option.Id, selectedId, StringComparison.OrdinalIgnoreCase))
              ?? AvailableWidgets.FirstOrDefault();
        OnPropertyChanged(nameof(HasAvailableWidgets));
        OnPropertyChanged(nameof(CanAddWidget));
        AddSelectedWidgetCommand.NotifyCanExecuteChanged();
    }

    /// <summary>把「添加部件」下拉里选中的那一块追加到布局末尾。</summary>
    [RelayCommand(CanExecute = nameof(CanAddWidget))]
    private void AddSelectedWidget()
    {
        if (SelectedAvailableWidget is not { } option)
        {
            return;
        }

        AddWidgetById(option.Id);
    }

    /// <summary>按布局身份添加（供拖拽/测试直接调用）。</summary>
    public void AddWidgetById(string id)
    {
        if (string.IsNullOrWhiteSpace(id) || _layoutIds.Contains(id, StringComparer.OrdinalIgnoreCase))
        {
            return;
        }

        _layoutIds.Add(id);
        SaveLayout();
        RebuildWidgets();
        _ = RefreshWidgetsAsync();
    }

    /// <summary>移除一块部件。允许移除到空——主页是用户的，不强迫保留任何一块。</summary>
    [RelayCommand]
    private void RemoveWidget(string? id)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            return;
        }

        var removed = _layoutIds.RemoveAll(existing =>
            string.Equals(existing, id, StringComparison.OrdinalIgnoreCase));
        if (removed == 0)
        {
            return;
        }

        SaveLayout();
        RebuildWidgets();
    }

    /// <summary>把一块部件上移一位（只与相邻的可见部件交换，跳过当前不可用的插件槽位）。</summary>
    [RelayCommand]
    private void MoveWidgetUp(string? id) => MoveWidget(id, -1);

    /// <summary>把一块部件下移一位。</summary>
    [RelayCommand]
    private void MoveWidgetDown(string? id) => MoveWidget(id, 1);

    /// <summary>拖拽落点：把 <paramref name="id"/> 放到 <paramref name="targetId"/> 之前。</summary>
    public void MoveWidgetBefore(string id, string targetId)
    {
        if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(targetId)
            || string.Equals(id, targetId, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var from = IndexOfLayoutId(id);
        var target = IndexOfLayoutId(targetId);
        if (from < 0 || target < 0)
        {
            return;
        }

        var moving = _layoutIds[from];
        _layoutIds.RemoveAt(from);
        target = IndexOfLayoutId(targetId);
        if (target < 0)
        {
            target = _layoutIds.Count;
        }

        _layoutIds.Insert(target, moving);
        SaveLayout();
        RebuildWidgets();
    }

    /// <summary>拖拽落点：把 <paramref name="id"/> 放到 <paramref name="targetId"/> 之后。</summary>
    public void MoveWidgetAfter(string id, string targetId)
    {
        if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(targetId)
            || string.Equals(id, targetId, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var from = IndexOfLayoutId(id);
        if (from < 0 || IndexOfLayoutId(targetId) < 0)
        {
            return;
        }

        var moving = _layoutIds[from];
        _layoutIds.RemoveAt(from);
        var target = IndexOfLayoutId(targetId);
        _layoutIds.Insert(target < 0 ? _layoutIds.Count : target + 1, moving);
        SaveLayout();
        RebuildWidgets();
    }

    /// <summary>重新拉一遍所有部件的数据。</summary>
    [RelayCommand]
    private Task RefreshWidgets() => RefreshWidgetsAsync();

    /// <summary>恢复默认三块（不会动插件部件曾经的位置记录之外的东西）。</summary>
    [RelayCommand]
    private void ResetWidgets()
    {
        _layoutIds = new List<string>(DefaultWidgetLayout);
        SaveLayout();
        RebuildWidgets();
        _ = RefreshWidgetsAsync();
    }

    private void MoveWidget(string? id, int delta)
    {
        if (string.IsNullOrWhiteSpace(id) || delta == 0)
        {
            return;
        }

        var visible = Widgets.Select(widget => widget.Id).ToList();
        var index = visible.FindIndex(candidate => string.Equals(candidate, id, StringComparison.OrdinalIgnoreCase));
        var targetIndex = index + delta;
        if (index < 0 || targetIndex < 0 || targetIndex >= visible.Count)
        {
            return;
        }

        var targetId = visible[targetIndex];
        var from = IndexOfLayoutId(id);
        var to = IndexOfLayoutId(targetId);
        if (from < 0 || to < 0)
        {
            return;
        }

        var moving = _layoutIds[from];
        _layoutIds.RemoveAt(from);
        to = IndexOfLayoutId(targetId);
        if (delta < 0)
        {
            _layoutIds.Insert(to, moving);
        }
        else
        {
            _layoutIds.Insert(to + 1, moving);
        }

        SaveLayout();
        RebuildWidgets();
    }

    private int IndexOfLayoutId(string id)
        => _layoutIds.FindIndex(candidate => string.Equals(candidate, id, StringComparison.OrdinalIgnoreCase));

    /// <summary>把布局写回设置；写失败只提示，不回滚内存里的布局。</summary>
    private void SaveLayout()
    {
        if (_settings is null)
        {
            return;
        }

        try
        {
            var current = _settings.Load();
            var result = _settings.Save(current with { HomeWidgets = _layoutIds.ToArray() });
            if (!result.IsSuccess)
            {
                StatusText = Text("主页布局已生效，但没能保存：{0}", LocalizationKeys.HomeWidgetSaveFailed, result.Message);
            }
        }
        catch (Exception exception)
        {
            StatusText = Text("主页布局已生效，但没能保存：{0}", LocalizationKeys.HomeWidgetSaveFailed, exception.Message);
        }
    }

    /// <summary>插件启停会改变可用部件：重建「添加部件」菜单，已放上去的插件部件该消失就消失。</summary>
    private void OnPluginsChanged(object? sender, EventArgs e)
    {
        void ApplyChange()
        {
            RebuildWidgets();
            _ = RefreshPluginWidgetsAsync();
        }

        if (_sync is null || ReferenceEquals(SynchronizationContext.Current, _sync))
        {
            ApplyChange();
            return;
        }

        _sync.Post(_ => ApplyChange(), null);
    }

    private void SubscribePluginWidgets()
    {
        if (_plugins is null || _widgetsSubscribed)
        {
            return;
        }

        _plugins.Changed += OnPluginsChanged;
        _widgetsSubscribed = true;
    }

    private HomeWidgetViewModel GetFeedWidget(string id)
    {
        if (_builtInWidgets.TryGetValue(id, out var existing))
        {
            return existing;
        }

        var spec = FeedWidgetSpecs.First(item => string.Equals(item.Id, id, StringComparison.Ordinal));
        var widget = new FeedHomeWidgetViewModel(
            this,
            spec.Id,
            Text(spec.TitleFallback, spec.TitleKey),
            spec.Glyph,
            spec.PageKey,
            cancellationToken => LoadFeedAsync(spec.Id, cancellationToken));
        _builtInWidgets[id] = widget;
        return widget;
    }

    private async Task<HomeWidgetFeed> LoadFeedAsync(string id, CancellationToken cancellationToken)
    {
        switch (id)
        {
            case ModUpdatesWidgetId:
                return HomeWidgetFeeds.ModUpdates(_repository?.Current);

            case InstallHistoryWidgetId:
            {
                if (_history is null)
                {
                    return new HomeWidgetFeed(Footer: "安装历史服务不可用。");
                }

                var result = await _history.LoadAsync(5, cancellationToken).ConfigureAwait(true);
                if (!result.IsSuccess)
                {
                    throw new InvalidOperationException(result.Message ?? "读取安装历史失败。");
                }

                return HomeWidgetFeeds.InstallHistory(result.Value);
            }

            case CacheWidgetId:
            {
                if (_cache is null)
                {
                    return new HomeWidgetFeed(Footer: "下载缓存服务不可用。");
                }

                var result = await _cache.GetSnapshotAsync(cancellationToken).ConfigureAwait(true);
                if (!result.IsSuccess)
                {
                    throw new InvalidOperationException(result.Message ?? "读取下载缓存失败。");
                }

                return HomeWidgetFeeds.Cache(result.Value);
            }

            case SavesWidgetId:
            {
                if (_saves is null || Instance?.GameDir is not { Length: > 0 } gameDir)
                {
                    return new HomeWidgetFeed(Footer: "先选择一个实例；有实例后这里会列出最近存档。");
                }

                var result = await _saves.ListSavesAsync(gameDir, cancellationToken).ConfigureAwait(true);
                if (!result.IsSuccess)
                {
                    throw new InvalidOperationException(result.Message ?? "读取存档失败。");
                }

                return HomeWidgetFeeds.Saves(result.Value);
            }

            case OperationsWidgetId:
                return HomeWidgetFeeds.Operations(_queue?.Operations);

            default:
                return new HomeWidgetFeed(Footer: "未知的主页部件。");
        }
    }

    /// <summary>拉一遍所有部件：宿主数据部件与插件部件并行，互不阻塞。</summary>
    private Task RefreshWidgetsAsync() => Task.WhenAll(RefreshDataWidgetsAsync(), RefreshPluginWidgetsAsync());

    /// <summary>按当前实例拉取宿主数据部件。</summary>
    private async Task RefreshDataWidgetsAsync()
    {
        var generation = ++_dataWidgetRefreshGeneration;
        var instance = Instance;
        var widgets = Widgets.OfType<FeedHomeWidgetViewModel>().ToArray();
        if (widgets.Length == 0)
        {
            return;
        }

        foreach (var widget in widgets)
        {
            if (generation != _dataWidgetRefreshGeneration || !ReferenceEquals(instance, Instance))
            {
                return;
            }

            await widget.LoadAsync().ConfigureAwait(true);
        }
    }

    /// <summary>底栏任务队列变化时只重画「后台任务」这一块，不动其他部件。</summary>
    private void OnQueueUpdated(IReadOnlyList<OperationProgress> operations)
    {
        var widget = Widgets.OfType<FeedHomeWidgetViewModel>()
            .FirstOrDefault(item => string.Equals(item.Id, OperationsWidgetId, StringComparison.Ordinal));
        widget?.Apply(HomeWidgetFeeds.Operations(operations));
    }

    private void SubscribeQueue()
    {
        if (_queue is null || _queueSubscribed)
        {
            return;
        }

        _queue.Updated += OnQueueUpdated;
        _queueSubscribed = true;
    }

    private static bool IsFeedWidgetId(string id)
        => FeedWidgetSpecs.Any(spec => string.Equals(spec.Id, id, StringComparison.Ordinal));

    private sealed record HomeWidgetSpec(
        string Id,
        string TitleFallback,
        string TitleKey,
        string DescriptionFallback,
        string DescriptionKey,
        string Glyph,
        string? PageKey);

    /// <summary>按当前实例拉取所有插件部件的数据；切实例/换插件时旧结果直接丢弃。</summary>
    private async Task RefreshPluginWidgetsAsync()
    {
        if (_plugins is null)
        {
            return;
        }

        var generation = ++_widgetRefreshGeneration;
        var instance = Instance;
        var context = new PluginHomeWidgetContext(instance?.DisplayName, instance?.GameDir, instance?.GameVersion);
        var widgets = Widgets.OfType<PluginHomeWidgetViewModel>().ToArray();
        if (widgets.Length == 0)
        {
            return;
        }

        foreach (var widget in widgets)
        {
            widget.BeginLoad();
        }

        foreach (var widget in widgets)
        {
            try
            {
                var result = await _plugins.GetHomeWidgetAsync(widget.PluginId, widget.Descriptor.Key, context)
                    .ConfigureAwait(true);
                if (generation != _widgetRefreshGeneration || !ReferenceEquals(instance, Instance))
                {
                    return;
                }

                if (!result.IsSuccess || result.Value is null)
                {
                    widget.Fail(result.Message ?? Text("读取插件部件失败。", LocalizationKeys.HomeWidgetLoadFailed));
                    continue;
                }

                widget.Apply(result.Value);
                if (!widget.HasMetrics && !widget.HasLines && !widget.HasError)
                {
                    widget.Empty(string.IsNullOrWhiteSpace(result.Value.Subtitle)
                        ? Text("暂时没有可显示的数据。", LocalizationKeys.HomeWidgetNoData)
                        : result.Value.Subtitle!);
                }
            }
            catch (Exception exception)
            {
                if (generation == _widgetRefreshGeneration)
                {
                    widget.Fail(exception.Message);
                }
            }
        }
    }

    /// <summary>插件部件的布局身份。</summary>
    public static string PluginWidgetId(string pluginId, string widgetKey)
        => "plugin:" + pluginId.Trim() + ":" + widgetKey.Trim();

    partial void OnSelectedAvailableWidgetChanged(HomeWidgetOptionViewModel? value)
    {
        OnPropertyChanged(nameof(CanAddWidget));
        AddSelectedWidgetCommand.NotifyCanExecuteChanged();
    }

    private void NotifyScreenshotState()
    {
        OnPropertyChanged(nameof(ScreenshotCountText));
        OnPropertyChanged(nameof(ShowEmptyScreenshots));
        OnPropertyChanged(nameof(ShowScreenshotRail));
    }

    /// <summary>写错误条（<paramref name="message"/> 为空表示清空）；技术细节跟在第二行。</summary>
    private void SetError(string? message, string? detail = null)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            ErrorText = string.Empty;
            return;
        }

        ErrorText = string.IsNullOrWhiteSpace(detail) ? message!.Trim() : message!.Trim() + Environment.NewLine + detail!.Trim();
    }
}