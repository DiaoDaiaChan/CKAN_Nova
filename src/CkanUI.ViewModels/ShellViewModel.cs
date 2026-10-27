using System.Collections.Generic;
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

    /// <summary>导航项列表（mods / instances / settings）。标题在构造时按当前语言解析。</summary>
    public IReadOnlyList<NavItem> NavItems { get; }

    [ObservableProperty]
    private string _statusText = string.Empty;

    [ObservableProperty]
    private string _title = "CKAN UI";

    [ObservableProperty]
    private object? _currentPage;

    /// <summary>无参构造函数，供设计时与 XAML 预览使用。</summary>
    public ShellViewModel()
        : this(new EmptyNavigationService(), null)
    {
    }

    /// <summary>带导航服务注入的构造函数。</summary>
    /// <param name="navigation">导航服务实例。</param>
    /// <param name="translate">取译文委托（视图模型层不引用本地化层，见架构铁律 2）；为空时直接回显 key。</param>
    public ShellViewModel(INavigationService navigation, Func<string, string>? translate = null, Operations.OperationQueueViewModel? operationQueue = null)
    {
        _navigation = navigation ?? throw new ArgumentNullException(nameof(navigation));
        OperationQueue = operationQueue;
        // 没有本地化注入（设计时预览、headless 单测）时给出中文兜底文案，
        // 而不是把 Ui_* 键名直接显示给用户。
        _translate = translate ?? (key => FallbackTexts.TryGetValue(key, out var text) ? text : key);

        NavItems = new List<NavItem>
        {
            new("mods", _translate("Ui_NavRail_ManageMods"), "Apps"),
            new("changes", _translate("Ui_NavRail_Changes"), "Sync"),
            new("repos", _translate("Ui_NavRail_Repos"), "Globe"),
            new("modpacks", _translate("Ui_NavRail_ModPacks"), "Exchange"),
            new("instances", _translate("Ui_NavRail_Instances"), "Folder"),
            new("playtime", _translate("Ui_NavRail_PlayTime"), "Clock"),
            new("saves", _translate("Ui_NavRail_Saves"), "Archive"),
            new("unmanaged", _translate("Ui_NavRail_Unmanaged"), "FolderOpen"),
            new("cache", _translate("Ui_NavRail_Cache"), "Storage"),
            new("dedup", _translate("Ui_NavRail_Dedup"), "Link"),
            new("installhistory", _translate("Ui_NavRail_InstallHistory"), "History"),
            new("plugins", _translate("Ui_NavRail_Plugins"), "Puzzle"),
            new("settings", _translate("Ui_NavRail_Settings"), "Settings")
        };
        _statusText = _translate("Ui_Common_Status_Ready");

        _currentPage = _navigation.CurrentPage;
        _navigation.Navigated += OnNavigated;

        NavigateCommand = new AsyncRelayCommand<string>(ExecuteNavigateAsync);
        BackCommand = new AsyncRelayCommand(ExecuteBackAsync);
        ForwardCommand = new AsyncRelayCommand(ExecuteForwardAsync);
        _navigation.HistoryChanged += OnHistoryChanged;
        RefreshHistoryState();
    }

    /// <summary>后台操作队列浮层（UI-08 / VM-05）。为空表示未接线（设计时预览），此时浮层不显示。</summary>
    public Operations.OperationQueueViewModel? OperationQueue { get; }

    /// <summary>浮层是否接线；供视图决定要不要挂载（空引用时避免渲染空壳）。</summary>
    public bool HasOperationQueue => OperationQueue is not null;

    /// <summary>页面导航异步命令。</summary>
    public IAsyncRelayCommand<string> NavigateCommand { get; }

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

    private void OnNavigated(object? newPage, string key)
    {
        CurrentPage = newPage;
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
        ["Ui_NavRail_Changes"] = "变更集",
        ["Ui_NavRail_Repos"] = "仓库",
        ["Ui_NavRail_ModPacks"] = "导入导出",
        ["Ui_NavRail_Instances"] = "游戏实例",
        ["Ui_NavRail_PlayTime"] = "游戏时长",
        ["Ui_NavRail_Saves"] = "存档",
        ["Ui_NavRail_Unmanaged"] = "未管理文件",
        ["Ui_NavRail_Cache"] = "下载缓存",
        ["Ui_NavRail_Dedup"] = "跨实例去重",
        ["Ui_NavRail_InstallHistory"] = "安装历史",
        ["Ui_NavRail_Plugins"] = "插件",
        ["Ui_NavRail_Settings"] = "设置",
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
