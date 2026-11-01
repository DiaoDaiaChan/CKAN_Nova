using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Threading;
using System.Windows.Input;
using CkanUI.Services.Abstractions;
using CkanUI.Services.Abstractions.Dtos;
using CkanUI.ViewModels.Navigation;
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
    private readonly SynchronizationContext? _sync;

    /// <summary>内置导航项（不含插件页签），标题在构造时按当前语言解析。</summary>
    private readonly List<NavItem> _baseNavItems;

    /// <summary>插件页签的缓存，按 key 复用同一实例，避免重建列表时丢掉 ListBox 的选中项。</summary>
    private readonly Dictionary<string, NavItem> _pluginNavItems = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>当前页签条对应的入口键；只在换入口时才重建页签。</summary>
    private string? _activeSectionKey;

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

    /// <summary>入口表：每个入口包含哪些页面（顺序即页签顺序）。</summary>
    public IReadOnlyList<NavSection> Sections { get; }

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
        IPluginService? plugins = null)
    {
        _navigation = navigation ?? throw new ArgumentNullException(nameof(navigation));
        OperationQueue = operationQueue;
        _instances = instances;
        _plugins = plugins;
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
            new("saves", _translate("Ui_NavRail_Saves"), "Archive"),
            new("unmanaged", _translate("Ui_NavRail_Unmanaged"), "FolderOpen"),
            new("cache", _translate("Ui_NavRail_Cache"), "Storage"),
            new("downloadstats", _translate("Ui_NavRail_DownloadStats"), "Chart"),
            new("dedup", _translate("Ui_NavRail_Dedup"), "Link"),
            new("installhistory", _translate("Ui_NavRail_InstallHistory"), "History"),
            new("plugins", _translate("Ui_NavRail_Plugins"), "Puzzle"),
            new("modtext", _translate("Ui_NavRail_ModText"), "Translate"),
            new("logs", _translate("Ui_NavRail_Logs"), "Bug"),
            new("settings", _translate("Ui_NavRail_Settings"), "Settings"),
            // Mod 配置页（M9）：追加在末尾而不是插进中间——既有测试按数组下标断言导航项，
            // 插中间会让后面每一项整体位移。
            new("modconfig", _translate("Ui_NavRail_ModConfig"), "Sliders")
        };
        NavItems = new ObservableCollection<NavItem>(_baseNavItems);
        Sections = BuildSections();
        RefreshRailItems();
        _statusText = _translate("Ui_Common_Status_Ready");

        _currentPage = _navigation.CurrentPage;
        SelectedNavItem = NavItems.FirstOrDefault(item => string.Equals(item.Key, _navigation.CurrentKey, StringComparison.OrdinalIgnoreCase));
        RefreshSection(_navigation.CurrentKey);
        _navigation.Navigated += OnNavigated;

        NavigateCommand = new AsyncRelayCommand<string>(ExecuteNavigateAsync);
        ToggleNavCollapsedCommand = new RelayCommand(ToggleNavCollapsed);
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

    /// <summary>展开/收起底部日志抽屉（状态栏中段按钮与抽屉标题条共用同一条命令）。</summary>
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
        OnPropertyChanged(nameof(IsLogDrawerOpen));
        OnPropertyChanged(nameof(LogStatusText));
    }

    private void OnLogDrawerPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        OnPropertyChanged(nameof(IsLogDrawerOpen));
        OnPropertyChanged(nameof(LogStatusText));
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
    private bool _isNavCollapsed;

    /// <summary>侧栏是否展开；供视图做「只在展开时显示文字」的反向可见性绑定。</summary>
    public bool IsNavExpanded => !IsNavCollapsed;

    /// <summary>折叠按钮提示（收起时提示「展开侧栏」，反之亦然）。</summary>
    public string NavToggleToolTip => _translate(IsNavCollapsed ? "Ui_NavRail_Expand" : "Ui_NavRail_Collapse");

    /// <summary>折叠按钮图标文本：展开态用「‹」提示往左收，收起态用「›」提示往右放。</summary>
    public string NavToggleGlyph => IsNavCollapsed ? "›" : "‹";

    /// <summary>切换侧栏展开/收起（Ctrl+B 也走这条命令）。</summary>
    public IRelayCommand ToggleNavCollapsedCommand { get; }

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

    private void OnInstanceChanged(InstanceInfo? instance)
    {
        UpdateTitleWithInstance(instance);
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

        if (NavItems.Count == desired.Count &&
            desired.Select((item, index) => ReferenceEquals(item, NavItems[index])).All(same => same))
        {
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

        // 当前页所属插件被卸载时，页签已经不存在：退回插件管理页，避免停在一个无入口的页面。
        var currentKey = _navigation.CurrentKey;
        if (!string.IsNullOrEmpty(currentKey) && NavItems.All(item => !string.Equals(item.Key, currentKey, StringComparison.OrdinalIgnoreCase)))
        {
            _ = ExecuteNavigateAsync("plugins", CancellationToken.None);
        }
    }

    private NavItem GetOrCreatePluginNavItem(PluginPageEntry entry)
    {
        if (_pluginNavItems.TryGetValue(entry.Key, out var existing) &&
            string.Equals(existing.Title, entry.Title, StringComparison.Ordinal))
        {
            return existing;
        }

        var item = new NavItem(entry.Key, entry.Title, "Puzzle");
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
        // 模组：装什么、装进去长什么样、哪些待应用、元数据打哪来、单个 mod 怎么配、界面文案怎么汉化。
        ("mods", null, ["mods", "content", "changes", "repos", "modconfig", "modtext"]),
        // 实例：一份安装本身 + 它衍生的存档、磁盘垃圾、历史与时长。
        ("instances", null, ["instances", "saves", "unmanaged", "installhistory", "playtime"]),
        // 整合包：导入导出与直接编辑 .ckan。
        ("modpacks", null, ["modpacks", "modpack-editor"]),
        // 数据：本地缓存、下载统计、跨实例去重——都是「盘上的东西」，与模组列表无关。
        ("cache", "Ui_NavRail_Data", ["cache", "downloadstats", "dedup"]),
        // 工具：插件管理（只剩一页，页签条不会出现）。
        ("plugins", "Ui_NavRail_Tools", ["plugins"]),
    ];

    private List<NavSection> BuildSections()
    {
        var result = new List<NavSection>(SectionSpecs.Length);
        foreach (var (root, titleKey, keys) in SectionSpecs)
        {
            var tabs = new List<NavItem>(keys.Length);
            foreach (var key in keys)
            {
                if (_baseNavItems.FirstOrDefault(item => string.Equals(item.Key, key, StringComparison.Ordinal)) is { } item)
                {
                    tabs.Add(item);
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

    /// <summary>重建侧栏入口列表：五个入口在前，插件自述页追加在后（日志与设置由外壳放进底部区）。</summary>
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
            if (SectionTabs.Count == 0)
            {
                return;
            }

            SectionTabs.Clear();
            OnPropertyChanged(nameof(HasSectionTabs));
            return;
        }

        if (!string.Equals(_activeSectionKey, section.Key, StringComparison.Ordinal))
        {
            SectionTabs.Clear();
            foreach (var tab in section.Tabs)
            {
                SectionTabs.Add(new NavTab(tab));
            }

            _activeSectionKey = section.Key;
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
        ["Ui_NavRail_Data"] = "数据",
        ["Ui_NavRail_Tools"] = "工具",
        ["Ui_NavRail_Collapse"] = "收起侧栏",
        ["Ui_NavRail_Expand"] = "展开侧栏",
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
