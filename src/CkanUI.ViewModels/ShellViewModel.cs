using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Threading;
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

    /// <summary>
    /// 导航项列表（内置页签 + 已激活插件声明的页签）。标题在构造时按当前语言解析；
    /// 插件页签随 <see cref="IPluginService.Changed"/> 增删。
    /// </summary>
    public ObservableCollection<NavItem> NavItems { get; }

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
        _statusText = _translate("Ui_Common_Status_Ready");

        _currentPage = _navigation.CurrentPage;
        SelectedNavItem = NavItems.FirstOrDefault(item => string.Equals(item.Key, _navigation.CurrentKey, StringComparison.OrdinalIgnoreCase));
        _navigation.Navigated += OnNavigated;

        NavigateCommand = new AsyncRelayCommand<string>(ExecuteNavigateAsync);
        ToggleNavCollapsedCommand = new RelayCommand(ToggleNavCollapsed);
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
        var dirty = CurrentPage is IUnsavedChangesAware aware && aware.HasUnsavedChanges;
        if (dirty == HasUnsavedChanges)
        {
            return;
        }

        HasUnsavedChanges = dirty;
        OnPropertyChanged(nameof(HasUnsavedChanges));
    }

    private void OnNavigated(object? newPage, string key)
    {
        CurrentPage = newPage;
        SelectedNavItem = NavItems.FirstOrDefault(item => string.Equals(item.Key, key, StringComparison.OrdinalIgnoreCase));
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
