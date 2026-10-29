using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CkanUI.App.Animations;
using CkanUI.App.Converters;
using CkanUI.App.Views.Dialogs;
using CkanUI.Services.Abstractions.Commands;
using CkanUI.Services.Commands;
using CkanUI.ViewModels;
using CkanUI.ViewModels.CommandPalette;
using CkanUI.ViewModels.Dialogs;
using FluentAvalonia.UI.Controls;
using log4net;

namespace CkanUI.App;

/// <summary>主窗口（03 篇 §1 外壳）。导航只做命令转发，状态全部在 ShellViewModel 里。</summary>
public sealed partial class MainWindow : Window
{
    /// <summary>页面切换动画的时长 token（20 篇 P-10 由 MotionHost 写入）。</summary>
    private const string PageMotionToken = "CkanMotionDurationPage";

    /// <summary>
    /// 侧栏分组（FluentAvalonia 官方标准做法：分组标题是菜单列表里的独立元素，不是容器）。
    /// 键必须与 <see cref="ShellViewModel"/> 里的导航键一致；插件页追加在「扩展」组末尾。
    /// </summary>
    private static readonly (string HeaderKey, string[] Keys)[] NavGroups =
    [
        ("Ui_NavGroup_Core", ["mods", "content", "changes", "repos", "modpacks", "modpack-editor", "instances"]),
        ("Ui_NavGroup_Tools", ["playtime", "saves", "unmanaged", "cache", "downloadstats", "dedup", "installhistory", "modtext", "modconfig"]),
        ("Ui_NavGroup_Extensions", ["plugins"]),
    ];

    /// <summary>底部固定项：日志与设置是「随时要看」的常驻入口，不适合混在业务分组里。</summary>
    private static readonly string[] NavFooterKeys = ["logs", "settings"];

    /// <summary>分组标题的中文兜底（本地化键还没进语言包时不至于显示键名）。</summary>
    private static readonly Dictionary<string, string> NavGroupFallbacks = new(StringComparer.Ordinal)
    {
        ["Ui_NavGroup_Core"] = "核心",
        ["Ui_NavGroup_Tools"] = "工具与数据",
        ["Ui_NavGroup_Extensions"] = "扩展",
    };

    private static readonly ILog Log = LogManager.GetLogger(typeof(MainWindow));

    /// <summary>当前正在播放的页面错峰入场；切页时取消上一次（未轮到的卡片会被复位）。</summary>
    private CancellationTokenSource? _pageCascadeCts;

    /// <summary>当前处于「追赶位移」拉伸态的导航项。</summary>
    private Control? _navPulseTarget;

    /// <summary>追赶位移的代数：只有最后一次脉冲有权摘类。</summary>
    private int _navPulseGeneration;

    private CommandPaletteViewModel? _palette;
    private ICommandCatalog? _commands;
    private Func<string, string>? _translate;

    /// <summary>外壳视图模型（导航项的来源）。</summary>
    private ShellViewModel? _shell;

    /// <summary>代码里改选中项时置真，避免 SelectionChanged 又来改一遍视图模型（会打架）。</summary>
    private bool _syncingNav;

    /// <summary>
    /// 宿主侧动作落点（UI-27 的 <c>action:</c> 命令）。返回 true 表示已处理。
    /// 外壳自己只认「打开面板 / 快捷键目录 / 前进后退」四条，其余动作的含义属于 App 层能力
    /// （托盘动作、诊断包、更新检查、主题与密度），所以由宿主注入。
    /// </summary>
    private Func<string, bool>? _paletteActionHost;

    public MainWindow()
    {
        InitializeComponent();

        // 动效强度变化会重写这个 token；CrossFade.Duration 是普通属性，只能在代码里跟着重建。
        ResourcesChanged += (_, _) => ApplyPageTransition();
        ApplyPageTransition();

        // 页面切换时给一级卡片排队入场。监听 PageHost.Content 而不是 ShellViewModel：
        // 命令面板、深链、前进后退最终都落到同一个 Content 上，一处接完全都覆盖。
        if (PageHost is not null)
        {
            PageHost.PropertyChanged += OnPageHostPropertyChanged;
        }

        ApplyStatusBarFacts();
    }

    /// <summary>
    /// 顶部 34px 一体化标题条：窗口用 <c>ExtendClientAreaToDecorationsHint</c> 把客户区铺到标题栏上，
    /// 系统不再替我们拖窗口（拖拽与双击最大化都得自己实现，否则整窗无法移动）。
    /// </summary>
    private void OnTitleBarPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        // 点在「前进 / 后退 / 关于」这类按钮上：那是一次命令，不是拖窗口。
        if (e.Source is Visual source && source.FindAncestorOfType<Button>(includeSelf: true) is not null)
        {
            return;
        }

        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            return;
        }

        if (e.ClickCount == 2)
        {
            WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
            e.Handled = true;
            return;
        }

        // 最大化状态下拖标题条：按 Windows 习惯先还原再接着拖。
        if (WindowState == WindowState.Maximized)
        {
            WindowState = WindowState.Normal;
        }

        BeginMoveDrag(e);
        e.Handled = true;
    }

    /// <summary>数据上下文就位后接管导航栏（DI 容器把 ShellViewModel 塞进来的时刻）。</summary>
    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        AttachShell();
    }

    private void AttachShell()
    {
        if (_shell is not null)
        {
            _shell.NavItems.CollectionChanged -= OnNavItemsChanged;
            _shell.PropertyChanged -= OnShellPropertyChanged;
        }

        _shell = DataContext as ShellViewModel;

        if (_shell is null)
        {
            return;
        }

        _shell.NavItems.CollectionChanged += OnNavItemsChanged;
        _shell.PropertyChanged += OnShellPropertyChanged;

        RebuildNavigation();
        ApplyPaneState(_shell.IsNavExpanded);
        SyncSelectedNavItem();
    }

    private void OnNavItemsChanged(object? sender, NotifyCollectionChangedEventArgs e) => RebuildNavigation();

    private void OnShellPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ShellViewModel.SelectedNavItem))
        {
            SyncSelectedNavItem();
        }
        else if (e.PropertyName == nameof(ShellViewModel.IsNavCollapsed) && _shell is not null)
        {
            ApplyPaneState(_shell.IsNavExpanded);
        }

        // 日志抽屉与未保存提示条的显隐完全由 XAML 绑定（Classes.is-open + is-active）驱动，
        // 代码侧不再插手：第二阶段只留骨骼，第三阶段的过渡写在样式里，不需要事件。
    }

    /// <summary>
    /// 按分组重建菜单项：壳里只有一条扁平列表，分组是外壳的呈现决策，所以映射放在这里，
    /// 视图模型不用知道「设置该放底部」这种事。插件页（未知键）归入「扩展」组。
    /// </summary>
    private void RebuildNavigation()
    {
        if (NavView is null || _shell is null)
        {
            return;
        }

        var known = new HashSet<string>(NavGroups.SelectMany(group => group.Keys), StringComparer.OrdinalIgnoreCase);
        var pluginItems = _shell.NavItems.Where(item => !known.Contains(item.Key) && !NavFooterKeys.Contains(item.Key, StringComparer.OrdinalIgnoreCase)).ToList();

        var menu = new List<object>();
        foreach (var (headerKey, keys) in NavGroups)
        {
            var entries = keys.Select(LookupNavItem).OfType<NavItem>().Select(CreateNavItem).Cast<object>().ToList();
            if (headerKey == "Ui_NavGroup_Extensions")
            {
                entries.AddRange(pluginItems.Select(CreateNavItem).Cast<object>());
            }

            if (entries.Count == 0)
            {
                continue;
            }

            menu.Add(new FANavigationViewItemHeader { Content = ResolveGroupTitle(headerKey) });
            menu.AddRange(entries);
        }

        NavView.MenuItems.Clear();
        foreach (var item in menu)
        {
            NavView.MenuItems.Add(item);
        }

        NavView.FooterMenuItems.Clear();
        foreach (var item in NavFooterKeys.Select(LookupNavItem).OfType<NavItem>())
        {
            NavView.FooterMenuItems.Add(CreateNavItem(item));
        }

        SyncSelectedNavItem();
    }

    private NavItem? LookupNavItem(string key) =>
        _shell?.NavItems.FirstOrDefault(item => string.Equals(item.Key, key, StringComparison.OrdinalIgnoreCase));

    private FANavigationViewItem CreateNavItem(NavItem nav)
    {
        var item = new FANavigationViewItem
        {
            Content = nav.Title,
            Tag = nav,
        };

        if (NavGlyphToGeometryConverter.Instance.Convert(nav.Glyph, typeof(Geometry), null, CultureInfo.InvariantCulture) is Geometry geometry)
        {
            item.IconSource = new FAPathIconSource { Data = geometry };
        }

        return item;
    }

    /// <summary>分组标题优先取语言包，取不到用自己的中文兜底（语言包加载失败也不至于露键名）。</summary>
    private string ResolveGroupTitle(string key) =>
        this.TryFindResource(key, out var value) && value is string text && text.Length > 0
            ? text
            : NavGroupFallbacks.TryGetValue(key, out var fallback) ? fallback : key;

    /// <summary>把视图模型的选中项落到导航栏上（前进后退、深链、命令面板都会从别的路径改它）。</summary>
    private void SyncSelectedNavItem()
    {
        if (NavView is null || _shell is null)
        {
            return;
        }

        var target = NavView.MenuItems.Concat(NavView.FooterMenuItems)
            .OfType<FANavigationViewItem>()
            .FirstOrDefault(item => ReferenceEquals(item.Tag, _shell.SelectedNavItem));

        _syncingNav = true;
        try
        {
            NavView.SelectedItem = target;
        }
        finally
        {
            _syncingNav = false;
        }
    }

    /// <summary>左侧导航点击 → 写回视图模型的选中项（视图模型自己负责导航与历史栈）。</summary>
    private void OnNavSelectionChanged(object? sender, FANavigationViewSelectionChangedEventArgs args)
    {
        if (_syncingNav || _shell is null)
        {
            return;
        }

        if (args.SelectedItemContainer?.Tag is NavItem nav)
        {
            _shell.SelectedNavItem = nav;
        }

        // 指示器滑动由 FluentAvalonia 自己播（600ms 组合动画，不可配）；这里补的是点击瞬间的追赶位移。
        _ = PulseNavItemAsync(args.SelectedItemContainer);
    }

    /// <summary>给刚被点中的导航项挂 110ms 的追赶位移类（连续点击时后一次接管）。</summary>
    private async Task PulseNavItemAsync(object? container)
    {
        if (container is not Control control)
        {
            return;
        }

        // 前一项若还在拉伸态，这里先摘掉：否则它到点时会回来摘「当前这一项」的类，追赶位移就丢了。
        if (_navPulseTarget is { } previous && !ReferenceEquals(previous, control))
        {
            previous.Classes.Remove(MotionFeedback.NavCatchUpClass);
        }

        _navPulseTarget = control;
        var generation = ++_navPulseGeneration;
        control.Classes.Add(MotionFeedback.NavCatchUpClass);

        await Task.Delay(MotionFeedback.NavCatchUpHold).ConfigureAwait(true);

        // 连点同一项时早到的那次不能提前掐断新一次，所以只有最新一代有权摘类。
        if (generation == _navPulseGeneration)
        {
            control.Classes.Remove(MotionFeedback.NavCatchUpClass);
        }
    }

    /// <summary>页面内容变化：新页面视图建好后让一级卡片错峰入场。</summary>
    private void OnPageHostPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property != ContentControl.ContentProperty)
        {
            return;
        }

        _ = PlayPageCascadeAsync();
    }

    /// <summary>
    /// 播放一次错峰入场。<see cref="TransitioningContentControl"/> 的 Content 刚设的这一刻，
    /// ViewLocator 还没把视图建出来，所以要等一轮 Loaded 优先级的布局再遍历卡片。
    /// </summary>
    private async Task PlayPageCascadeAsync()
    {
        _pageCascadeCts?.Cancel();
        var cts = new CancellationTokenSource();
        _pageCascadeCts = cts;

        try
        {
            await Dispatcher.UIThread.InvokeAsync(static () => { }, DispatcherPriority.Loaded);
            if (cts.IsCancellationRequested)
            {
                return;
            }

            await StaggerAnimator.PlayAsync(PageHost, cts.Token).ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
            // 用户连点导航：后一次入场接管，这里安静退出。
        }
        catch (Exception ex)
        {
            // 动效属增强：任何异常只记日志，绝不影响页面本身已经显示出来这件事。
            Log.Warn("页面错峰入场失败。", ex);
        }
        finally
        {
            if (ReferenceEquals(_pageCascadeCts, cts))
            {
                _pageCascadeCts = null;
            }

            cts.Dispose();
        }
    }

    /// <summary>
    /// 展开走 Left（常驻宽面板），收起走 LeftCompact（只剩图标栏）。
    /// 官方标准里这两种是显示模式而非宽度，所以不能像以前那样直接过渡 Width。
    /// </summary>
    private void ApplyPaneState(bool expanded)
    {
        if (NavView is null)
        {
            return;
        }

        NavView.PaneDisplayMode = expanded ? FANavigationViewPaneDisplayMode.Left : FANavigationViewPaneDisplayMode.LeftCompact;
        NavView.Classes.Set("Collapsed", !expanded);
    }

    /// <summary>按当前动效强度装配页面切换动画；时长归零时干脆不装（关闭档要的是「瞬时」）。</summary>
    private void ApplyPageTransition()
    {
        if (PageHost is null)
        {
            return;
        }

        if (this.TryFindResource(PageMotionToken, out var value) && value is TimeSpan duration && duration > TimeSpan.Zero)
        {
            PageHost.PageTransition = new Avalonia.Animation.CrossFade { Duration = duration };
        }
        else
        {
            PageHost.PageTransition = null;
        }
    }

    /// <summary>
    /// 状态栏右下角常驻版本信息（报问题时截图就带上版本号，不必再问）。
    /// 只在窗口构造时采一次：发布后程序集版本不会变，重复采集反而给每帧加负担。
    /// </summary>
    private void ApplyStatusBarFacts()
    {
        if (StatusVersionText is null)
        {
            return;
        }

        var info = AboutInfoFactory.Create();
        StatusVersionText.Text = info.AppVersion;
        ToolTip.SetTip(StatusVersionText, $"CKAN 核心 {info.CoreVersion} · {info.RuntimeVersion}");
    }

    /// <summary>打开关于框（12 篇 F-55）；外部链接交给 Avalonia 的 Launcher，失败由 VM 显示提示。</summary>
    private async void OnAboutClick(object? sender, RoutedEventArgs e)
    {
        var top = TopLevel.GetTopLevel(this);
        Func<string, Task<bool>> openExternal = async url =>
        {
            if (top is null || !Uri.TryCreate(url, UriKind.Absolute, out var uri))
            {
                return false;
            }

            return await top.Launcher.LaunchUriAsync(uri);
        };

        var viewModel = new AboutDialogViewModel(AboutInfoFactory.Create(), openExternal);
        var window = new AboutDialogView { DataContext = viewModel };

        if (IsVisible)
        {
            await window.ShowDialog(this);
        }
        else
        {
            window.Show();
        }
    }

    /// <summary>
    /// 挂命令面板（UI-27）：VM 与命令目录由 DI 给，外壳只管键位与「命令 Id → 动作」的映射。
    /// </summary>
    /// <param name="palette">命令面板 VM（DI 单例，状态下沉在它自己的属性里）。</param>
    /// <param name="commands">命令目录（快捷键目录要读它）。</param>
    /// <param name="translate">取译文委托（视图模型层不引用本地化层）。</param>
    /// <param name="actionHost">宿主侧动作落点，返回 true 表示已处理；未接线的动作在目录里已置为禁用。</param>
    public void AttachCommandPalette(
        CommandPaletteViewModel palette,
        ICommandCatalog commands,
        Func<string, string> translate,
        Func<string, bool>? actionHost = null)
    {
        ArgumentNullException.ThrowIfNull(palette);
        ArgumentNullException.ThrowIfNull(commands);
        ArgumentNullException.ThrowIfNull(translate);

        _palette = palette;
        _commands = commands;
        _translate = translate;
        _paletteActionHost = actionHost;
        CommandPaletteHost.DataContext = palette;
        palette.CommandInvoked += OnCommandInvoked;

        // 隧道阶段抢先处理：页面里的文本框会在冒泡阶段先吃掉 Ctrl+K / Ctrl+/，那时外壳已经轮不上了。
        AddHandler(KeyDownEvent, OnGlobalKeyDown, RoutingStrategies.Tunnel);
    }

    /// <summary>全局键位：Ctrl+K 开关命令面板，Ctrl+/ 打开快捷键目录。</summary>
    private void OnGlobalKeyDown(object? sender, KeyEventArgs e)
    {
        if (_palette is null || !e.KeyModifiers.HasFlag(KeyModifiers.Control))
        {
            return;
        }

        switch (e.Key)
        {
            case Key.K:
                _palette.ToggleCommand.Execute(null);
                e.Handled = true;
                break;

            // 主键盘的 / 在 Avalonia 里叫 OemQuestion；小键盘的 / 是另一个枚举值，两个都收。
            case Key.OemQuestion:
            case Key.Divide:
                _ = ShowShortcutsAsync();
                e.Handled = true;
                break;
        }
    }

    /// <summary>命令 Id → 动作：先试导航键，再试外壳内置四条，最后交给宿主。</summary>
    private void OnCommandInvoked(string id)
    {
        if (DataContext is not ShellViewModel shell)
        {
            return;
        }

        if (DefaultCommands.TryGetNavigationPageKey(id, out var pageKey))
        {
            shell.NavigateCommand.Execute(pageKey);
            return;
        }

        if (id == DefaultCommands.ActionId("show-palette"))
        {
            _palette?.OpenCommand.Execute(null);
            return;
        }

        if (id == DefaultCommands.ActionId("show-shortcuts"))
        {
            _ = ShowShortcutsAsync();
            return;
        }

        if (id == DefaultCommands.ActionId("go-back"))
        {
            shell.BackCommand.Execute(null);
            return;
        }

        if (id == DefaultCommands.ActionId("go-forward"))
        {
            shell.ForwardCommand.Execute(null);
            return;
        }

        _paletteActionHost?.Invoke(id);
    }

    /// <summary>打开快捷键目录：每次新建一个（VM 继承 DialogViewModelBase，复用会带上一次关闭态）。</summary>
    private async Task ShowShortcutsAsync()
    {
        if (_commands is null)
        {
            return;
        }

        var viewModel = new ShortcutsCatalogViewModel(_commands, _translate);
        var window = new ShortcutsDialogView { DataContext = viewModel };
        if (IsVisible)
        {
            await window.ShowDialog(this);
        }
        else
        {
            window.Show();
        }
    }
}
