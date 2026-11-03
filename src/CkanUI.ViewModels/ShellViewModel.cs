using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Threading;
using System.Windows.Input;
using CkanUI.Services.Abstractions;
using CkanUI.Services.Abstractions.Dtos;
using CkanUI.ViewModels.Navigation;
using CkanUI.ViewModels.Pages.Instances;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace CkanUI.ViewModels;

/// <summary>导航栏项目记录（03 篇 §1）。</summary>
/// <param name="Key">页面唯一标识键值。</param>
/// <param name="Title">导航项目显示标题。</param>
/// <param name="Glyph">图标字形标识符。</param>
public sealed record NavItem(string Key, string Title, string Glyph);

/// <summary>
/// 侧栏一级入口：同一个对象的不同面（管理 / 内容预览 / 变更集 / 仓库）收进一个入口，
/// 组内用页签切换，而不是在侧栏里排成一串平级页签。
/// </summary>
/// <param name="Key">入口键，等于组内第一个页签的页面键（深链与历史栈仍然按页面键工作）。</param>
/// <param name="Title">侧栏显示文字；可以和组内首页的导航标题不同（例如「数据」）。</param>
/// <param name="RailItem">侧栏项本身（按引用做选中同步，不能与页面项混用）。</param>
/// <param name="Tabs">组内页面，顺序即页签顺序。</param>
public sealed record NavSection(string Key, string Title, NavItem RailItem, IReadOnlyList<NavItem> Tabs);

/// <summary>
/// 侧栏导航栏精简时的悬浮交互模式：
/// <see cref="TabPopout"/> 光标悬浮于单个 Tab 时仅弹出该 Tab 独立卡片与快捷操作（默认）；
/// <see cref="Drawer"/> 光标滑入侧栏时整体以抽屉式浮层滑开全部导航项。
/// </summary>
public enum NavHoverMode
{
    /// <summary>单Tab悬浮弹出独立卡片（默认）。</summary>
    TabPopout,

    /// <summary>完整侧边栏抽屉展开。</summary>
    Drawer
}

/// <summary>页面区顶部页签：同一个入口下的平级页面。<see cref="IsActive"/> 供样式类绑定。</summary>
public sealed partial class NavTab : ObservableObject
{
    /// <summary>按导航项建一个页签。</summary>
    /// <param name="item">页面导航项。</param>
    public NavTab(NavItem item)
    {
        Key = item.Key;
        Title = item.Title;
    }

    /// <summary>页面键。</summary>
    public string Key { get; }

    /// <summary>页签文字（沿用该页的导航标题）。</summary>
    public string Title { get; }

    /// <summary>是否为当前页。</summary>
    [ObservableProperty]
    private bool _isActive;
}

/// <summary>主窗口外壳（UI-01 骨架 / ARC-03）。管理顶层导航、全局状态栏与内容承载。</summary>
public sealed partial class ShellViewModel : ObservableObject
{
    private readonly INavigationService _navigation;
    private readonly Func<string, string> _translate;
    private readonly IInstanceService? _instances;
    private readonly IPluginService? _plugins;
    private readonly IUiSettingsStore? _settingsStore;
    private readonly SynchronizationContext? _sync;

    /// <summary>内置导航项（不含插件页签），标题在构造时按当前语言解析。</summary>
    private readonly List<NavItem> _baseNavItems;

    /// <summary>插件页签的缓存，按 key 复用同一实例，避免重建列表时丢掉 ListBox 的选中项。</summary>
    private readonly Dictionary<string, NavItem> _pluginNavItems = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>当前页签条对应的入口键；只在换入口或页签集合变化时才重建页签。</summary>
    private string? _activeSectionKey;

    /// <summary>当前页签条的键序列签名：同一入口内页签被插件增删时靠它察觉。</summary>
    private string? _activeSectionSignature;

    /// <summary>入口表快照（插件增删页签后整体替换，见 <see cref="Sections"/>）。</summary>
    private IReadOnlyList<NavSection> _sections = Array.Empty<NavSection>();

    /// <summary>
    /// 导航项列表（内置页签 + 已激活插件声明的页签）。标题在构造时按当前语言解析；
    /// 插件页签随 <see cref="IPluginService.Changed"/> 增删。
    /// </summary>
    public ObservableCollection<NavItem> NavItems { get; }

    /// <summary>
    /// 侧栏一级入口：五个入口 + 插件页。侧栏只渲染这一层，
    /// 组内页面（内容预览、变更集、仓库……）放在页面区顶部的页签里。
    /// </summary>
    public ObservableCollection<NavItem> RailItems { get; } = new();

    /// <summary>
    /// 入口表：每个入口包含哪些页面（顺序即页签顺序）。
    /// </summary>
    /// <remarks>
    /// 可变：插件激活/停用会往分节里增删页签（见 <see cref="RebuildPluginNavItems"/>），
    /// 所以这里是「当前快照」而不是构造期一次算完的常量。
    /// </remarks>
    public IReadOnlyList<NavSection> Sections => _sections;

    /// <summary>当前页所属入口的页签；只有一个页面时为空集合（页面区不出现页签条）。</summary>
    public ObservableCollection<NavTab> SectionTabs { get; } = new();

    /// <summary>页面区是否需要页签条。</summary>
    public bool HasSectionTabs => SectionTabs.Count > 1;

    /// <summary>当前页所属入口；无入口（插件页、日志、设置）时为 null。</summary>
    public NavSection? ActiveSection { get; private set; }

    [ObservableProperty]
    private string _statusText = string.Empty;

    [ObservableProperty]
    private string _title = "CKAN UI";

    [ObservableProperty]
    private object? _currentPage;

    /// <summary>当前选中的导航项，驱动左侧导航栏高亮并支持双向绑定同步。</summary>
    [ObservableProperty]
    private NavItem? _selectedNavItem;

    /// <summary>无参构造函数，供设计时与 XAML 预览使用。</summary>
    public ShellViewModel()
        : this(new EmptyNavigationService(), null)
    {
    }

    /// <summary>带导航服务注入的构造函数。</summary>
    /// <param name="navigation">导航服务实例。</param>
    /// <param name="translate">取译文委托（视图模型层不引用本地化层，见架构铁律 2）；为空时直接回显 key。</param>
    /// <param name="operationQueue">后台操作队列浮层；为空表示未接线。</param>
    /// <param name="instances">实例服务（标题带实例名）；为空表示未接线。</param>
    /// <param name="plugins">插件服务：已激活插件声明的页签会追加到导航栏（12 篇 F-17）；为空表示未接线。</param>
    public ShellViewModel(
        INavigationService navigation,
        Func<string, string>? translate = null,
        Operations.OperationQueueViewModel? operationQueue = null,
        IInstanceService? instances = null,
        IPluginService? plugins = null,
        IUiSettingsStore? settingsStore = null)
    {
        _navigation = navigation ?? throw new ArgumentNullException(nameof(navigation));
        OperationQueue = operationQueue;
        _instances = instances;
        _plugins = plugins;
        _settingsStore = settingsStore;
        // 页面缩放读回来时直接写字段：走属性 setter 会在构造期就把设置回写一遍文件。
        _pageZoom = SnapPageZoom(settingsStore?.Current.PageZoom ?? DefaultPageZoom);
        if (settingsStore is not null && string.Equals(settingsStore.Current.NavHoverMode, "drawer", StringComparison.OrdinalIgnoreCase))
        {
            _navHoverMode = NavHoverMode.Drawer;
        }
        else
        {
            _navHoverMode = NavHoverMode.TabPopout;
        }
        // 插件激活/卸载可能发生在后台线程（发现插件目录是异步的），改集合必须回 UI 线程。
        // 视图模型层不引用 Avalonia（架构铁律 1），所以只捕获 BCL 的同步上下文。
        _sync = SynchronizationContext.Current;
        // 没有本地化注入（设计时预览、headless 单测）时给出中文兜底文案，
        // 而不是把 Ui_* 键名直接显示给用户。
        _translate = translate ?? (key => FallbackTexts.TryGetValue(key, out var text) ? text : key);

        _baseNavItems = new List<NavItem>
        {
            new("mods", _translate("Ui_NavRail_ManageMods"), "Apps"),
            new("content", _translate("Ui_NavRail_Content"), "Document"),
            new("changes", _translate("Ui_NavRail_Changes"), "Sync"),
            new("repos", _translate("Ui_NavRail_Repos"), "Globe"),
            new("modpacks", _translate("Ui_NavRail_ModPacks"), "Exchange"),
            new("modpack-editor", _translate("Ui_NavRail_ModPackEditor"), "Edit"),
            new("instances", _translate("Ui_NavRail_Instances"), "Folder"),
            new("playtime", _translate("Ui_NavRail_PlayTime"), "Clock"),
            // 存档 / 飞船 / 旗帜 / 加载图 / KSP 游戏设置这五页已搬成首方插件（docs/design/plugins.md §11.11）：
            // 它们不再是宿主的内置页，由插件在 SectionSpecs 的同名槽位上补进来——键名不变，用户看到的顺序不变。
            new("unmanaged", _translate("Ui_NavRail_Unmanaged"), "FolderOpen"),
            new("cache", _translate("Ui_NavRail_Cache"), "Storage"),
            new("downloadstats", _translate("Ui_NavRail_DownloadStats"), "Chart"),
            new("dedup", _translate("Ui_NavRail_Dedup"), "Link"),
            new("installhistory", _translate("Ui_NavRail_InstallHistory"), "History"),
            new("plugins", _translate("Ui_NavRail_Plugins"), "Puzzle"),
                new("logs", _translate("Ui_NavRail_Logs"), "Bug"),
            new("settings", _translate("Ui_NavRail_Settings"), "Settings"),
            // Mod 配置页（M9）：追加在末尾而不是插进中间——既有测试按数组下标断言导航项，
            // 插中间会让后面每一项整体位移。
            new("modconfig", _translate("Ui_NavRail_ModConfig"), "Sliders"),
            // 主菜单：同样追加在末尾保住既有下标；它在 SectionSpecs 里排第一，
            // 于是 RailItems[0] 就是主页——启动默认页靠的就是这一条（App.axaml.cs 取 RailItems[0]）。
            new("home", _translate("Ui_NavRail_Home"), "Home")
        };
        NavItems = new ObservableCollection<NavItem>(_baseNavItems);
        _sections = BuildSections(null);
        RefreshRailItems();
        _statusText = _translate("Ui_Common_Status_Ready");

        _currentPage = _navigation.CurrentPage;
        SelectedNavItem = NavItems.FirstOrDefault(item => string.Equals(item.Key, _navigation.CurrentKey, StringComparison.OrdinalIgnoreCase));
        RefreshSection(_navigation.CurrentKey);
        _navigation.Navigated += OnNavigated;

        NavigateCommand = new AsyncRelayCommand<string>(ExecuteNavigateAsync);
        ToggleNavCollapsedCommand = new RelayCommand(ToggleNavCollapsed);
        ToggleNavHoverModeCommand = new RelayCommand(ToggleNavHoverMode);
        ToggleLogDrawerCommand = new RelayCommand(() => LogDrawer?.ToggleCommand.Execute(null), () => LogDrawer is not null);
        BackCommand = new AsyncRelayCommand(ExecuteBackAsync);
        ForwardCommand = new AsyncRelayCommand(ExecuteForwardAsync);
        _navigation.HistoryChanged += OnHistoryChanged;
        RefreshHistoryState();

        if (_instances is not null)
        {
            UpdateTitleWithInstance(_instances.Current);
            _instances.CurrentChanged += OnInstanceChanged;
        }

        if (_plugins is not null)
        {
            _plugins.Changed += OnPluginsChanged;
            RebuildPluginNavItems();
        }
    }

    /// <summary>后台操作队列浮层（UI-08 / VM-05）。为空表示未接线（设计时预览），此时浮层不显示。</summary>
    public Operations.OperationQueueViewModel? OperationQueue { get; }

    /// <summary>浮层是否接线；供视图决定要不要挂载（空引用时避免渲染空壳）。</summary>
    public bool HasOperationQueue => OperationQueue is not null;

    /// <summary>
    /// 底部日志抽屉（14 篇 §4.1 / UI-LOG-01）。由 App 在窗口建好前回填；为空时视图不渲染抽屉。
    /// </summary>
    /// <remarks>
    /// 之所以挂在外壳而不是各页面：抽屉要跨页面常驻（下载失败时长在设置页也要能看到最近错误），
    /// 页面自己实例化就会出现「换个标签页历史就没了」。抽屉只认 ILogStore，与外壳无反向依赖。
    /// </remarks>
    [ObservableProperty]
    private Pages.Logs.LogDrawerViewModel? _logDrawer;

    /// <summary>底部日志抽屉是否展开。状态栏中段的开关与 Overlay 的 <c>is-open</c> 类都读它。</summary>
    public bool IsLogDrawerOpen => LogDrawer?.IsOpen == true;

    /// <summary>
    /// 状态栏中段的日志开关文案：抽屉标题（含最近行数）+ 收起时的未读徽标；
    /// 抽屉未接线时退回导航项里的「日志」字样。
    /// </summary>
    public string LogStatusText => LogDrawer is { } drawer
        ? drawer.IsBadgeVisible
            ? string.Concat(drawer.Title, " · ", drawer.BadgeText)
            : drawer.Title
        : _translate("Ui_NavRail_Logs");

    /// <summary>
    /// 顶栏日志小标记的未读计数（错误 + 警告）：只有抽屉收起且确有新日志时才非零。
    /// 展开抽屉等于已读，计数由抽屉自己清零，外壳不另存一份真相。
    /// </summary>
    public int LogUnreadCount => LogDrawer is { HasBadge: true } drawer
        ? drawer.NewErrorCount + drawer.NewWarningCount
        : 0;

    /// <summary>未读里是否含错误：顶栏徽标据此从警告色切到危险色。</summary>
    public bool LogHasUnreadError => LogDrawer is { HasBadge: true, NewErrorCount: > 0 };

    /// <summary>顶栏日志小标记是否点亮未读徽标。</summary>
    public bool HasLogBadge => LogDrawer?.HasBadge == true;

    /// <summary>展开/收起底部日志抽屉（顶栏小标记与抽屉标题条共用同一条命令）。</summary>
    public IRelayCommand ToggleLogDrawerCommand { get; }

    /// <summary>正在监听属性变化的抽屉实例（换实例时退订，避免旧抽屉继续推动外壳状态）。</summary>
    private object? _logDrawerWatched;

    /// <summary>
    /// 抽屉实例变化：重新挂监听并刷新派生属性。抽屉的展开态是它自己的状态，
    /// 外壳只做镜像——这样「点状态栏」「点抽屉标题条」「深链要求打开日志」三条路径不会各存一份真相。
    /// </summary>
    partial void OnLogDrawerChanged(Pages.Logs.LogDrawerViewModel? value)
    {
        if (_logDrawerWatched is INotifyPropertyChanged previous)
        {
            previous.PropertyChanged -= OnLogDrawerPropertyChanged;
        }

        _logDrawerWatched = value;

        if (value is INotifyPropertyChanged current)
        {
            current.PropertyChanged += OnLogDrawerPropertyChanged;
        }

        ToggleLogDrawerCommand.NotifyCanExecuteChanged();
        RaiseLogMirrorChanged();
    }

    private void OnLogDrawerPropertyChanged(object? sender, PropertyChangedEventArgs e) => RaiseLogMirrorChanged();

    /// <summary>抽屉状态是唯一真相，外壳这侧只把镜像属性一并推给绑定。</summary>
    private void RaiseLogMirrorChanged()
    {
        OnPropertyChanged(nameof(IsLogDrawerOpen));
        OnPropertyChanged(nameof(LogStatusText));
        OnPropertyChanged(nameof(LogUnreadCount));
        OnPropertyChanged(nameof(LogHasUnreadError));
        OnPropertyChanged(nameof(HasLogBadge));
    }

    /// <summary>页面导航异步命令。</summary>
    public IAsyncRelayCommand<string> NavigateCommand { get; }

    /// <summary>
    /// 侧栏是否收起为「图标栏」。只影响呈现（250px ↔ 68px），不影响导航语义：
    /// 收起后仍可点图标切页，选中的那一项照样高亮。
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsNavExpanded))]
    [NotifyPropertyChangedFor(nameof(NavToggleToolTip))]
    [NotifyPropertyChangedFor(nameof(NavToggleGlyph))]
    private bool _isNavCollapsed = true;

    /// <summary>侧栏是否展开；供视图做「只在展开时显示文字」的反向可见性绑定。</summary>
    public bool IsNavExpanded => !IsNavCollapsed;

    /// <summary>折叠按钮提示（收起时提示「展开侧栏」，反之亦然）。</summary>
    public string NavToggleToolTip => _translate(IsNavCollapsed ? "Ui_NavRail_Expand" : "Ui_NavRail_Collapse");

    /// <summary>折叠按钮图标文本：展开态用「‹」提示往左收，收起态用「›」提示往右放。</summary>
    public string NavToggleGlyph => IsNavCollapsed ? "›" : "‹";

    /// <summary>切换侧栏展开/收起（Ctrl+B 也走这条命令）。</summary>
    public IRelayCommand ToggleNavCollapsedCommand { get; }

    /// <summary>
    /// 导航栏精简时的悬浮交互模式：
    /// <see cref="NavHoverMode.TabPopout"/> 光标悬浮于单个 Tab 时仅弹出该 Tab；
    /// <see cref="NavHoverMode.Drawer"/> 光标滑入侧栏时整体以抽屉式浮层展开全部。
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsTabPopoutHoverMode))]
    [NotifyPropertyChangedFor(nameof(NavHoverModeTitle))]
    [NotifyPropertyChangedFor(nameof(NavHoverModeToolTip))]
    [NotifyPropertyChangedFor(nameof(NavHoverModeGlyph))]
    [NotifyPropertyChangedFor(nameof(NavHoverModePathData))]
    private NavHoverMode _navHoverMode = NavHoverMode.TabPopout;

    /// <summary>是否为单Tab悬浮弹出模式。</summary>
    public bool IsTabPopoutHoverMode => NavHoverMode == NavHoverMode.TabPopout;

    /// <summary>当前悬停模式显示文本。</summary>
    public string NavHoverModeTitle => NavHoverMode == NavHoverMode.TabPopout
        ? _translate("Ui_NavRail_HoverMode_TabPopout")
        : _translate("Ui_NavRail_HoverMode_Drawer");

    /// <summary>模式切换按钮提示说明。</summary>
    public string NavHoverModeToolTip => NavHoverMode == NavHoverMode.TabPopout
        ? "悬停交互：单Tab弹出（点击切换为完整侧栏抽屉）"
        : "悬停交互：侧栏抽屉（点击切换为单Tab弹出）";

    /// <summary>模式切换按钮图标字形。</summary>
    public string NavHoverModeGlyph => NavHoverMode == NavHoverMode.TabPopout ? "📑" : "🗂️";

    /// <summary>模式切换按钮矢量路径数据（统一 Fluent 风格，杜绝彩色 Emoji）。</summary>
    public string NavHoverModePathData => NavHoverMode == NavHoverMode.TabPopout
        ? "M19 19H5V5h7V3H5c-1.11 0-2 .9-2 2v14c0 1.1.89 2 2 2h14c1.1 0 2-.9 2-2v-7h-2v7zM14 3v2h3.59l-9.83 9.83 1.41 1.41L19 6.41V10h2V3h-7z"
        : "M4 4h16v16H4V4zm2 2v12h4V6H6zm6 0v12h6V6h-6z";

    /// <summary>在单Tab弹出与侧栏抽屉之间切换的命令。</summary>
    public IRelayCommand ToggleNavHoverModeCommand { get; }

    /// <summary>回退命令（F-25）。</summary>
    public IAsyncRelayCommand BackCommand { get; }

    /// <summary>前进命令（F-25）。</summary>
    public IAsyncRelayCommand ForwardCommand { get; }

    /// <summary>是否可回退；由导航服务的历史状态驱动。</summary>
    [ObservableProperty]
    private bool _canGoBack;

    /// <summary>是否可前进；由导航服务的历史状态驱动。</summary>
    [ObservableProperty]
    private bool _canGoForward;

    /// <summary>回退按钮提示（设计时无本地化注入时给中文兜底）。</summary>
    public string BackToolTip => _translate("Ui_Common_Nav_Back");

    /// <summary>前进按钮提示。</summary>
    public string ForwardToolTip => _translate("Ui_Common_Nav_Forward");

    partial void OnSelectedNavItemChanged(NavItem? value)
    {
        if (value != null && !string.Equals(value.Key, _navigation.CurrentKey, StringComparison.OrdinalIgnoreCase))
        {
            _ = ExecuteNavigateAsync(value.Key, CancellationToken.None);
        }
    }

    /// <summary>翻转侧栏展开状态。</summary>
    private void ToggleNavCollapsed()
    {
        IsNavCollapsed = !IsNavCollapsed;
    }

    /// <summary>在「单Tab悬浮弹出」与「侧栏整体抽屉」之间切换并持久化配置。</summary>
    public void ToggleNavHoverMode()
    {
        SetNavHoverMode(NavHoverMode == NavHoverMode.TabPopout ? NavHoverMode.Drawer : NavHoverMode.TabPopout);
    }

    /// <summary>显式设定导航悬停模式并同步落盘。</summary>
    public void SetNavHoverMode(NavHoverMode mode)
    {
        if (NavHoverMode == mode)
        {
            return;
        }

        NavHoverMode = mode;
        if (_settingsStore is not null)
        {
            _settingsStore.Save(_settingsStore.Current with
            {
                NavHoverMode = mode == NavHoverMode.TabPopout ? "tabPopout" : "drawer"
            });
        }
    }

    /// <summary>页面缩放下限（75%）：再往下字已经糊、布局开始挤。</summary>
    public const double MinPageZoom = 0.75d;

    /// <summary>页面缩放上限（150%）：再往上单屏可见内容太少，滚动成本高于收益。</summary>
    public const double MaxPageZoom = 1.5d;

    /// <summary>默认页面缩放（100%）。</summary>
    public const double DefaultPageZoom = 1.0d;

    /// <summary>一档缩放步长（5%）：滑块吸附、Ctrl+滚轮、加/减命令共用同一档。</summary>
    public const double PageZoomStep = 0.05d;

    /// <summary>
    /// 页面缩放比例（1.0 = 100%）。只缩放页面承载区：标题栏、侧栏、状态栏保持原尺寸，
    /// 否则拖动滑块时滑块自己也在变大变小，用户抓不住目标。
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PageZoomPercent))]
    [NotifyPropertyChangedFor(nameof(PageZoomPercentText))]
    [NotifyPropertyChangedFor(nameof(CanZoomIn))]
    [NotifyPropertyChangedFor(nameof(CanZoomOut))]
    private double _pageZoom = DefaultPageZoom;

    // 拖动滑块是连续动作：手势期间不落盘，结束后补一次。
    private bool _pageZoomGestureActive;
    private bool _pageZoomPersistPending;

    /// <summary>滑块绑定的百分比数值（75–150），与 <see cref="PageZoom"/> 同源、双向。</summary>
    public double PageZoomPercent
    {
        get => PageZoom * 100d;
        set
        {
            if (double.IsFinite(value))
            {
                PageZoom = SnapPageZoom(value / 100d);
            }
        }
    }

    /// <summary>状态栏读数，如 "110%"。</summary>
    public string PageZoomPercentText => $"{Math.Round(PageZoom * 100d):0}%";

    /// <summary>还能不能往上放大（到顶时禁用加号命令）。</summary>
    public bool CanZoomIn => PageZoom < MaxPageZoom - 1e-9;

    /// <summary>还能不能往下缩小（到底时禁用减号命令）。</summary>
    public bool CanZoomOut => PageZoom > MinPageZoom + 1e-9;

    /// <summary>放大一档。</summary>
    [RelayCommand(CanExecute = nameof(CanZoomIn))]
    private void ZoomIn() => PageZoom = SnapPageZoom(PageZoom + PageZoomStep);

    /// <summary>缩小一档。</summary>
    [RelayCommand(CanExecute = nameof(CanZoomOut))]
    private void ZoomOut() => PageZoom = SnapPageZoom(PageZoom - PageZoomStep);

    /// <summary>复位到 100%（Ctrl+0）。</summary>
    [RelayCommand]
    private void ResetPageZoom() => PageZoom = DefaultPageZoom;

    /// <summary>
    /// 进入「按住不放」的连续缩放：拖动滑块期间每格都只改内存，不落盘。
    /// 一次拖动 15 格 = 15 次 fsync + 15 次 Changed 广播，界面直接卡成幻灯片，
    /// 写失败时还会一格一条错误日志刷满抽屉。拖动结束由 <see cref="EndPageZoomGesture"/> 合并写一次。
    /// </summary>
    public void BeginPageZoomGesture() => _pageZoomGestureActive = true;

    /// <summary>结束连续缩放：把手势期间攒下的最后一次值落盘（没有变动就不写）。</summary>
    public void EndPageZoomGesture()
    {
        if (!_pageZoomGestureActive)
        {
            return;
        }

        _pageZoomGestureActive = false;
        if (_pageZoomPersistPending)
        {
            _pageZoomPersistPending = false;
            PersistPageZoom(PageZoom);
        }
    }

    /// <summary>Ctrl+滚轮入口：<paramref name="steps"/> 为正放大、为负缩小，一次一格。</summary>
    public void NudgePageZoom(int steps)
    {
        if (steps != 0)
        {
            PageZoom = SnapPageZoom(PageZoom + (steps * PageZoomStep));
        }
    }

    partial void OnPageZoomChanged(double value)
    {
        var normalized = SnapPageZoom(value);
        if (!normalized.Equals(value))
        {
            // 越界或非吸附值：先归一，setter 会带着合法值再进来一次。
            PageZoom = normalized;
            return;
        }

        ZoomInCommand.NotifyCanExecuteChanged();
        ZoomOutCommand.NotifyCanExecuteChanged();

        if (_pageZoomGestureActive)
        {
            // 拖动中：只记账，等手势结束再写一次（见 BeginPageZoomGesture）。
            _pageZoomPersistPending = true;
            return;
        }

        PersistPageZoom(value);
    }

    private void PersistPageZoom(double value)
    {
        // 与 NavHoverMode 同一套持久化口径：单次改动落一次，设置文件小、写盘原子。
        // 唯一的例外是滑块拖动——那是连续改动，由 Begin/EndPageZoomGesture 合并成一次。
        _settingsStore?.Save(_settingsStore.Current with { PageZoom = value });
    }

    /// <summary>把任意输入折到 5% 网格上并夹进 [75%, 150%]；非有限值退回默认。</summary>
    private static double SnapPageZoom(double value)
    {
        if (!double.IsFinite(value))
        {
            return DefaultPageZoom;
        }

        var snapped = Math.Round(value / PageZoomStep, MidpointRounding.AwayFromZero) * PageZoomStep;
        return Math.Clamp(Math.Round(snapped, 2), MinPageZoom, MaxPageZoom);
    }

    /// <summary>外壳动作：按启动命令启动当前实例的游戏。由视图注入（与主页/实例页同一套实现）。</summary>
    public IInstanceShellLauncher? ShellLauncher { get; set; }

    /// <summary>实例启动命令的持久化（<c>GUIConfig.json</c>）。由视图注入。</summary>
    public IInstanceCommandStore? CommandStore { get; set; }

    /// <summary>
    /// 顶栏常驻启动按钮的可执行条件：有当前实例，且实例没被占用（游戏可能已经在跑）。
    /// 顶栏是全页面常驻的唯一入口，条件必须与实例页保持一致，否则会出现「主页灰、顶栏亮」。
    /// </summary>
    public bool CanLaunchGame => _instances?.Current is { IsLocked: false };

    /// <summary>
    /// 顶栏常驻「启动游戏」：解析当前实例的启动命令并交给外壳拉起进程。
    /// 顶栏没有错误条，一次性结果统一落 <see cref="StatusText"/>（与底部状态栏同一出口）。
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanLaunchGame))]
    private async Task LaunchGameAsync()
    {
        if (_instances?.Current is not { } instance)
        {
            return;
        }

        if (ShellLauncher is not { } launcher)
        {
            StatusText = _translate("Ui_Home_ErrorLaunchMissing");
            return;
        }

        var command = await InstanceLaunchCommandResolver.ResolveAsync(CommandStore, instance.GameDir).ConfigureAwait(true);
        if (string.IsNullOrWhiteSpace(command))
        {
            StatusText = _translate("Ui_Home_ErrorNoLaunchCommand");
            return;
        }

        var result = await launcher.LaunchGameAsync(instance.GameDir, command!).ConfigureAwait(true);
        StatusText = result.IsSuccess
            ? string.Format(_translate("Ui_Home_StatusLaunched"), command)
            : result.Message ?? _translate("Ui_Home_ErrorLaunchFailed");
    }

    private void OnInstanceChanged(InstanceInfo? instance)
    {
        UpdateTitleWithInstance(instance);
        LaunchGameCommand.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(CanLaunchGame));
    }

    private void UpdateTitleWithInstance(InstanceInfo? instance)
    {
        Title = instance is not null
            ? $"CKAN UI - {instance.Name} ({instance.GameVersion})"
            : "CKAN UI";
    }

    private void OnPluginsChanged(object? sender, EventArgs e)
    {
        // 发现/激活/卸载都可能发生在后台线程：有同步上下文就封送，没有（headless 单测）就直接改。
        if (_sync is null)
        {
            RebuildPluginNavItems();
            return;
        }

        _sync.Post(_ => RebuildPluginNavItems(), null);
    }

    /// <summary>
    /// 按当前激活的插件重建导航栏：内置项保持不变，插件页签紧跟在「插件」之后。
    /// </summary>
    /// <remarks>
    /// 逐项差分而不是整体替换：整体换集合会把 ListBox 的 SelectedItem 变成悬空引用，
    /// 用户正在看的页签会突然不高亮了。键序列没变化时直接返回，连事件都不发。
    /// </remarks>
    private void RebuildPluginNavItems()
    {
        var entries = PluginPageCatalog.Collect(_plugins?.Active, _baseNavItems.Select(item => item.Key));

        // 重建前先记住当前页原本属于哪个分节：插件被停用后要退回该分节的首页（「实例」），
        // 而不是把用户丢到插件管理页——他关掉的是「存档」，不是「实例」。
        var currentKey = _navigation.CurrentKey;
        var previousSection = SectionRootForPage(currentKey);

        var desired = new List<NavItem>(_baseNavItems.Count + entries.Count);
        var desiredKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < _baseNavItems.Count; i++)
        {
            var baseItem = _baseNavItems[i];
            desired.Add(baseItem);
            desiredKeys.Add(baseItem.Key);

            // 「插件」页之后是插件自述页签的位置：紧挨管理入口，不打断内置页签的分组顺序。
            if (!string.Equals(baseItem.Key, "plugins", StringComparison.Ordinal))
            {
                continue;
            }

            foreach (var entry in entries)
            {
                desired.Add(GetOrCreatePluginNavItem(entry));
                desiredKeys.Add(entry.Key);
            }
        }

        // 清掉已经消失的插件页签缓存，避免插件重载后拿到过期标题。
        foreach (var stale in _pluginNavItems.Keys.Where(key => !desiredKeys.Contains(key)).ToList())
        {
            _pluginNavItems.Remove(stale);
        }

        // 分节页签随插件增删变化；先重算入口表，RefreshRailItems 才会用新的 Tabs 判重
        // （分节内的插件页也进 NavItems：选中态与 RailItemFor 都按 NavItems 找自己）。
        var sections = BuildSections(entries);
        var sectionsChanged = !SectionsEquivalent(_sections, sections);
        if (sectionsChanged)
        {
            _sections = sections;
            OnPropertyChanged(nameof(Sections));
        }

        var navChanged = NavItems.Count != desired.Count ||
                         desired.Select((item, index) => !ReferenceEquals(item, NavItems[index])).Any(different => different);
        if (!navChanged)
        {
            if (sectionsChanged)
            {
                RefreshRailItems();
                RefreshSection(currentKey);
            }

            return;
        }

        for (var i = NavItems.Count - 1; i >= 0; i--)
        {
            if (!desiredKeys.Contains(NavItems[i].Key))
            {
                NavItems.RemoveAt(i);
            }
        }

        for (var i = 0; i < desired.Count; i++)
        {
            if (i >= NavItems.Count)
            {
                NavItems.Add(desired[i]);
            }
            else if (!ReferenceEquals(NavItems[i], desired[i]))
            {
                NavItems.Insert(i, desired[i]);
                // 插入后旧项被推到 i+1，下一轮比较会把它识别为多余项。
            }
        }

        while (NavItems.Count > desired.Count)
        {
            NavItems.RemoveAt(NavItems.Count - 1);
        }

        // 插件页在侧栏里是独立入口（不进任何组的页签条），列表变了要跟着重排。
        RefreshRailItems();
        RefreshSection(currentKey);

        // 当前页所属插件被停用时，页签已经不存在：退回它原本所在分节的首页，没有分节才退回插件管理页。
        if (!string.IsNullOrEmpty(currentKey) &&
            NavItems.All(item => !string.Equals(item.Key, currentKey, StringComparison.OrdinalIgnoreCase)))
        {
            _ = ExecuteNavigateAsync(previousSection ?? "plugins", CancellationToken.None);
        }
    }

    private NavItem GetOrCreatePluginNavItem(PluginPageEntry entry)
    {
        // 有 TitleKey 的插件（首方内置）走语言包，没有的用自述标题；键写错时 _translate 会原样回键名，
        // 所以这里再兜一层：译文等于键名就退回自述标题，页签至少是可读的。
        var title = entry.TitleKey is { Length: > 0 } key ? _translate(key) : entry.Title;
        if (string.Equals(title, entry.TitleKey, StringComparison.Ordinal))
        {
            title = entry.Title;
        }

        // 图标由插件自述（Glyph）：不填或填了宿主不认的名字才落到通用插件图标。
        var glyph = entry.Glyph is { Length: > 0 } declared ? declared : "Puzzle";

        if (_pluginNavItems.TryGetValue(entry.Key, out var existing) &&
            string.Equals(existing.Title, title, StringComparison.Ordinal) &&
            string.Equals(existing.Glyph, glyph, StringComparison.Ordinal))
        {
            return existing;
        }

        var item = new NavItem(entry.Key, title, glyph);
        _pluginNavItems[entry.Key] = item;
        return item;
    }

    /// <summary>
    /// 当前页是否还有未落盘的改动（外壳底部提示条据此显隐）。
    /// 判定交给页面自己（见 <see cref="IUnsavedChangesAware"/>），外壳只负责跟着页面的通知重算。
    /// </summary>
    public bool HasUnsavedChanges { get; private set; }

    /// <summary>提示条「保存更改」：命令归属由当前页决定（见 <see cref="IUnsavedChangesAware"/>），外壳只做转发。</summary>
    public ICommand? UnsavedSaveCommand => (CurrentPage as IUnsavedChangesAware)?.SaveChangesCommand;

    /// <summary>提示条「放弃修改」：同上，页面没提供命令时为 null。</summary>
    public ICommand? UnsavedDiscardCommand => (CurrentPage as IUnsavedChangesAware)?.DiscardChangesCommand;

    /// <summary>提示条是否显示「保存更改」按钮。</summary>
    public bool HasUnsavedSaveAction => UnsavedSaveCommand is not null;

    /// <summary>提示条是否显示「放弃修改」按钮。</summary>
    public bool HasUnsavedDiscardAction => UnsavedDiscardCommand is not null;

    /// <summary>正在监听属性变化的那一页：换页时必须退订，否则旧页面会继续推动外壳状态。</summary>
    private object? _dirtyWatchedPage;

    /// <summary>换页后重新挂监听并立刻重算一次，避免提示条停留在上一页的状态。</summary>
    partial void OnCurrentPageChanged(object? value)
    {
        if (_dirtyWatchedPage is INotifyPropertyChanged previous)
        {
            previous.PropertyChanged -= OnCurrentPagePropertyChanged;
        }

        _dirtyWatchedPage = value;

        if (value is INotifyPropertyChanged current)
        {
            current.PropertyChanged += OnCurrentPagePropertyChanged;
        }

        RefreshUnsavedChanges();
    }

    private void OnCurrentPagePropertyChanged(object? sender, PropertyChangedEventArgs e) => RefreshUnsavedChanges();

    private void RefreshUnsavedChanges()
    {
        var aware = CurrentPage as IUnsavedChangesAware;
        var dirty = aware?.HasUnsavedChanges == true;

        // 命令归属可能变（同一页在「有命令」与「无命令」之间不会切换，但换页会），
        // 所以派生属性每次都推一遍而不是只在脏状态翻转时推。
        OnPropertyChanged(nameof(UnsavedSaveCommand));
        OnPropertyChanged(nameof(UnsavedDiscardCommand));
        OnPropertyChanged(nameof(HasUnsavedSaveAction));
        OnPropertyChanged(nameof(HasUnsavedDiscardAction));

        if (dirty == HasUnsavedChanges)
        {
            return;
        }

        HasUnsavedChanges = dirty;
    }

    private void OnNavigated(object? newPage, string key)
    {
        CurrentPage = newPage;
        SelectedNavItem = NavItems.FirstOrDefault(item => string.Equals(item.Key, key, StringComparison.OrdinalIgnoreCase));
        RefreshSection(key);
        RefreshHistoryState();
    }

    private void OnHistoryChanged()
    {
        RefreshHistoryState();
    }

    private void RefreshHistoryState()
    {
        CanGoBack = _navigation.CanGoBack;
        CanGoForward = _navigation.CanGoForward;
    }

    private async Task ExecuteBackAsync()
    {
        await _navigation.GoBackAsync().ConfigureAwait(false);
        RefreshHistoryState();
    }

    private async Task ExecuteForwardAsync()
    {
        await _navigation.GoForwardAsync().ConfigureAwait(false);
        RefreshHistoryState();
    }

    private Task ExecuteNavigateAsync(string? key, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(key))
        {
            return Task.CompletedTask;
        }

        return _navigation.NavigateAsync(key, null, ct);
    }

    /// <summary>
    /// 入口表：把「同一对象的多个面」收进一个侧栏入口。
    /// 键必须与 <see cref="_baseNavItems"/> 一致；标题键为空表示直接沿用该组首页的导航标题。
    /// </summary>
    private static readonly (string Root, string? TitleKey, string[] Keys)[] SectionSpecs =
    [
        // 主菜单：应用启动后的落地页。排在第一位，RailItems[0] 因此恒为主页。
        ("home", null, ["home"]),
        // 模组：装什么、装进去长什么样、哪些待应用、元数据打哪来、单个 mod 怎么配。
        ("mods", null, ["mods", "content", "changes", "repos", "modconfig"]),
        // 模组翻译已解耦为内置插件（com.ckanui.modtext），作为独立侧栏入口落在「扩展」分组里：
        // 它不再是宿主的页签，分组也就不能写死在这里。
        // 实例：一份安装本身 + 它衍生的存档、磁盘垃圾、历史与时长 + KSP 原生设置。
        // 存档 / 飞船 / 旗帜 / 加载图 / KSP 游戏设置这五页已搬成插件（docs/design/plugins.md §11.11），
        // 但仍然占着这里的槽位——槽位决定它们在分节里的位置，插件按 key 补位，所以装了没装都不改变既有顺序。
        ("instances", null, ["instances", "saves", "ships", "flags", "loadingimages", "kspconfig", "unmanaged", "installhistory", "playtime"]),
        // 整合包：导入导出与直接编辑 .ckan。
        ("modpacks", null, ["modpacks", "modpack-editor"]),
        // 数据：本地缓存、下载统计、跨实例去重——都是「盘上的东西」，与模组列表无关。
        ("cache", "Ui_NavRail_Data", ["cache", "downloadstats", "dedup"]),
        // 工具：插件管理（只剩一页，页签条不会出现）。
        ("plugins", "Ui_NavRail_Tools", ["plugins"]),
    ];

    /// <summary>
    /// 按槽位表组入口：每个槽位先找内置页，找不到再问插件（「存档」这类页解耦成插件后仍占原来的位置）。
    /// 插件声明了分节但不在任何槽位里的页，按 Order→标题追加在同一分节末尾。
    /// </summary>
    /// <param name="pluginEntries">已激活插件声明的页签；构造期为 null。</param>
    private List<NavSection> BuildSections(IReadOnlyList<PluginPageEntry>? pluginEntries)
    {
        // 宿主不认的分节名不进这里，调用方（RebuildPluginNavItems）把它们留在侧栏。
        var embedded = new Dictionary<string, List<PluginPageEntry>>(StringComparer.OrdinalIgnoreCase);
        if (pluginEntries is { Count: > 0 })
        {
            foreach (var entry in pluginEntries)
            {
                if (SectionRoot(entry.Section) is not { } root)
                {
                    continue;
                }

                if (!embedded.TryGetValue(root, out var list))
                {
                    embedded[root] = list = new List<PluginPageEntry>();
                }

                list.Add(entry);
            }
        }

        var result = new List<NavSection>(SectionSpecs.Length);
        foreach (var (root, titleKey, keys) in SectionSpecs)
        {
            var pending = embedded.TryGetValue(root, out var declared) ? new List<PluginPageEntry>(declared) : null;
            var tabs = new List<NavItem>(keys.Length + (pending?.Count ?? 0));
            foreach (var key in keys)
            {
                if (_baseNavItems.FirstOrDefault(item => string.Equals(item.Key, key, StringComparison.Ordinal)) is { } item)
                {
                    tabs.Add(item);
                    continue;
                }

                var index = pending?.FindIndex(candidate => string.Equals(candidate.Key, key, StringComparison.OrdinalIgnoreCase)) ?? -1;
                if (index >= 0)
                {
                    tabs.Add(GetOrCreatePluginNavItem(pending![index]));
                    pending!.RemoveAt(index);
                }
            }

            if (pending is not null)
            {
                foreach (var entry in pending
                             .OrderBy(candidate => candidate.Order)
                             .ThenBy(candidate => candidate.Title, StringComparer.CurrentCulture))
                {
                    tabs.Add(GetOrCreatePluginNavItem(entry));
                }
            }

            if (tabs.Count == 0)
            {
                continue;
            }

            var title = titleKey is null ? tabs[0].Title : _translate(titleKey);
            result.Add(new NavSection(root, title, new NavItem(root, title, tabs[0].Glyph), tabs));
        }

        return result;
    }

    /// <summary>
    /// 把插件声明的分节名映射回宿主的分节根键；宿主不认的返回 null（调用方退回侧栏入口）。
    /// 「分节」是宿主的词汇表，插件写错名字只能是放错位置，不能是页签消失。
    /// </summary>
    private static string? SectionRoot(string? section) =>
        string.IsNullOrWhiteSpace(section)
            ? null
            : SectionSpecs.FirstOrDefault(spec => string.Equals(spec.Root, section.Trim(), StringComparison.OrdinalIgnoreCase)).Root;

    /// <summary>某个页面键当前落在哪个分节里；不在任何分节返回 null。</summary>
    private string? SectionRootForPage(string? pageKey) =>
        string.IsNullOrEmpty(pageKey)
            ? null
            : Sections.FirstOrDefault(section => section.Tabs.Any(tab => string.Equals(tab.Key, pageKey, StringComparison.OrdinalIgnoreCase)))?.Key;

    /// <summary>入口表是否等价（根键与每个分节的页签键序列都相同），等价就不必惊动侧栏与页签条。</summary>
    private static bool SectionsEquivalent(IReadOnlyList<NavSection> left, IReadOnlyList<NavSection> right)
    {
        if (left.Count != right.Count)
        {
            return false;
        }

        for (var i = 0; i < left.Count; i++)
        {
            if (!string.Equals(left[i].Key, right[i].Key, StringComparison.Ordinal) ||
                left[i].Tabs.Count != right[i].Tabs.Count)
            {
                return false;
            }

            for (var j = 0; j < left[i].Tabs.Count; j++)
            {
                if (!string.Equals(left[i].Tabs[j].Key, right[i].Tabs[j].Key, StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }
            }
        }

        return true;
    }

    /// <summary>重建侧栏入口列表：六个入口在前，插件自述页追加在后（日志与设置由外壳放进底部区）。</summary>
    private void RefreshRailItems()
    {
        RailItems.Clear();
        foreach (var section in Sections)
        {
            RailItems.Add(section.RailItem);
        }

        foreach (var item in NavItems)
        {
            if (string.Equals(item.Key, "logs", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(item.Key, "settings", StringComparison.OrdinalIgnoreCase) ||
                Sections.Any(section => section.Tabs.Any(tab => string.Equals(tab.Key, item.Key, StringComparison.OrdinalIgnoreCase))))
            {
                continue;
            }

            RailItems.Add(item);
        }
    }

    /// <summary>
    /// 按当前页刷新页面区顶部的页签条。切到别的入口才重建，同入口内只翻 IsActive，
    /// 避免每次切页都重造一遍按钮（丢掉悬停与焦点）。
    /// </summary>
    /// <param name="pageKey">当前页的页面键；为空表示还没有导航过。</param>
    public void RefreshSection(string? pageKey)
    {
        var section = Sections.FirstOrDefault(candidate =>
            candidate.Tabs.Any(tab => string.Equals(tab.Key, pageKey, StringComparison.OrdinalIgnoreCase)));

        if (section is null)
        {
            ActiveSection = null;
            _activeSectionKey = null;
            _activeSectionSignature = null;
            if (SectionTabs.Count == 0)
            {
                return;
            }

            SectionTabs.Clear();
            OnPropertyChanged(nameof(HasSectionTabs));
            return;
        }

        // 换入口要重建；同一入口内页签被插件增删（比如停用「存档」插件）也要重建，
        // 否则页签条会停在上一份快照上，用户看着一个已经不存在页签。
        var signature = string.Join('\u001f', section.Tabs.Select(tab => tab.Key));
        if (!string.Equals(_activeSectionKey, section.Key, StringComparison.Ordinal) ||
            !string.Equals(_activeSectionSignature, signature, StringComparison.Ordinal))
        {
            SectionTabs.Clear();
            foreach (var tab in section.Tabs)
            {
                SectionTabs.Add(new NavTab(tab));
            }

            _activeSectionKey = section.Key;
            _activeSectionSignature = signature;
            ActiveSection = section;
            OnPropertyChanged(nameof(HasSectionTabs));
        }

        foreach (var tab in SectionTabs)
        {
            tab.IsActive = string.Equals(tab.Key, pageKey, StringComparison.OrdinalIgnoreCase);
        }
    }

    /// <summary>取某一页所属的侧栏入口项；插件页、日志、设置这类独立页返回它自己的导航项。</summary>
    /// <param name="pageKey">页面键。</param>
    public NavItem? RailItemFor(string? pageKey)
    {
        if (string.IsNullOrEmpty(pageKey))
        {
            return null;
        }

        var section = Sections.FirstOrDefault(candidate =>
            candidate.Tabs.Any(tab => string.Equals(tab.Key, pageKey, StringComparison.OrdinalIgnoreCase)));

        return section is not null
            ? section.RailItem
            : NavItems.FirstOrDefault(item => string.Equals(item.Key, pageKey, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>无本地化层时的中文兜底文案（键名与 LocalizationKeys 保持一致）。</summary>
    private static readonly Dictionary<string, string> FallbackTexts = new(StringComparer.Ordinal)
    {
        ["Ui_NavRail_ManageMods"] = "管理模组",
        ["Ui_NavRail_Content"] = "内容预览",
        ["Ui_NavRail_Changes"] = "变更集",
        ["Ui_NavRail_Repos"] = "仓库",
        ["Ui_NavRail_ModPacks"] = "导入导出",
        ["Ui_NavRail_ModPackEditor"] = "modpack 编辑器",
        ["Ui_NavRail_Instances"] = "游戏实例",
        ["Ui_NavRail_PlayTime"] = "游戏时长",
        ["Ui_NavRail_Saves"] = "存档",
        ["Ui_NavRail_Ships"] = "飞船",
        ["Ui_NavRail_Flags"] = "旗帜",
        ["Ui_NavRail_LoadingScreens"] = "加载图",
        ["Ui_NavRail_Unmanaged"] = "未管理文件",
        ["Ui_NavRail_Cache"] = "下载缓存",
        ["Ui_NavRail_DownloadStats"] = "下载统计",
        ["Ui_NavRail_Dedup"] = "跨实例去重",
        ["Ui_NavRail_InstallHistory"] = "安装历史",
        ["Ui_NavRail_Plugins"] = "插件",
        ["Ui_NavRail_ModText"] = "模组翻译",
        ["Ui_NavRail_Logs"] = "日志",
        ["Ui_NavRail_Settings"] = "设置",
        ["Ui_NavRail_ModConfig"] = "Mod 配置",
        ["Ui_NavRail_Home"] = "主页",
        ["Ui_NavRail_KspConfig"] = "KSP 游戏设置",
        ["Ui_NavRail_Data"] = "数据",
        ["Ui_NavRail_Tools"] = "工具",
        ["Ui_NavRail_Collapse"] = "收起侧栏",
        ["Ui_NavRail_Expand"] = "展开侧栏",
        ["Ui_NavRail_HoverMode_TabPopout"] = "单Tab悬停弹出",
        ["Ui_NavRail_HoverMode_Drawer"] = "侧边栏抽屉展开",
        ["Ui_MainWindow_ExpandFullSidebar"] = "展开完整侧栏",
        ["Ui_QueueOverlay_Title"] = "后台任务",
        ["Ui_QueueOverlay_CancelAll"] = "全部取消",
        ["Ui_QueueOverlay_ClearCompleted"] = "清除已完成",
        ["Ui_QueueOverlay_CompactTip"] = "点击展开后台任务详细进度",
        ["Ui_QueueOverlay_CollapseTip"] = "收起为紧凑小胶囊",
        ["Ui_QueueOverlay_EmptyTip"] = "暂无后台任务",
        ["Ui_QueueOverlay_EmptyDetail"] = "下载、安装或更新模组时，任务进度将在此实时呈现",
        ["Ui_Common_Nav_Back"] = "后退",
        ["Ui_Common_Nav_Forward"] = "前进",
        ["Ui_Common_Status_Ready"] = "就绪",
    };

    private sealed class EmptyNavigationService : INavigationService
    {
        public object? CurrentPage => null;
        public string CurrentKey => string.Empty;
        public event Action<object?, string>? Navigated
        {
            add { }
            remove { }
        }

        public Task<bool> NavigateAsync(string pageKey, object? parameter = null, CancellationToken ct = default)
        {
            return Task.FromResult(false);
        }
    }
}
