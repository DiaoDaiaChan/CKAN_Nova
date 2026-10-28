using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using CkanUI.App.Converters;
using CkanUI.App.Theming;
using CkanUI.App.Views.Dialogs;
using CkanUI.Services.Abstractions.Commands;
using CkanUI.Services.Commands;
using CkanUI.ViewModels;
using CkanUI.ViewModels.CommandPalette;
using CkanUI.ViewModels.Dialogs;
using FluentAvalonia.UI.Controls;

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

        // 日志抽屉「上拉半屏」（14 篇 §4.1）：按窗口实际高度算 MaxHeight，不写死像素。
        SizeChanged += (_, e) => UpdateLogDrawerHeight(e.NewSize.Height);
        UpdateLogDrawerHeight(Bounds.Height);

        ApplyStatusBarFacts();

        // 页面错峰入场（20 篇 P-10）：换页后把新页面的直接子元素依次滑入。
        // 挂在 ContentControl.ContentProperty 上而不是 TransitioningContentControl 的过渡事件，
        // 是因为过渡只管透明度，入场节奏得由这里按动效 token 决定。
        if (PageHost is not null)
        {
            PageHost.PropertyChanged += OnPageHostPropertyChanged;
        }
    }

    private void OnPageHostPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property != ContentControl.ContentProperty || PageHost is null)
        {
            return;
        }

        _ = MotionPrimitives.StaggerEntranceAsync(PageHost.Content as Control, MotionPrimitives.ResolveDuration(this, PageMotionToken));
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

        // 初值也要落一次：深链或恢复的会话可能一进来就是抽屉打开、或页面已脏。
        if (_shell.LogDrawer is not null)
        {
            RevealLogDrawer();
        }

        RevealUnsavedHint();
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
        else if (e.PropertyName == nameof(ShellViewModel.LogDrawer))
        {
            RevealLogDrawer();
        }
        else if (e.PropertyName == nameof(ShellViewModel.HasUnsavedChanges))
        {
            RevealUnsavedHint();
        }
    }

    /// <summary>日志抽屉滑入：位移量取抽屉当前高度（还没测量时用半屏约值），只动 RenderTransform。</summary>
    private void RevealLogDrawer()
    {
        if (LogDrawerHost is null || _shell?.LogDrawer is null)
        {
            return;
        }

        var offset = LogDrawerHost.Bounds.Height > 0 ? LogDrawerHost.Bounds.Height : 240d;
        _ = MotionPrimitives.SlideInAsync(LogDrawerHost, offset, MotionPrimitives.ResolveDuration(this, PageMotionToken));
    }

    /// <summary>未保存提示条：脏了就上浮 12px，脏状态清掉时直接归零（不需要退场动画）。</summary>
    private void RevealUnsavedHint()
    {
        if (UnsavedHintHost is null)
        {
            return;
        }

        if (_shell?.HasUnsavedChanges != true)
        {
            UnsavedHintHost.Opacity = 0d;
            return;
        }

        _ = MotionPrimitives.SlideInAsync(UnsavedHintHost, 12d, MotionPrimitives.ResolveDuration(this, PageMotionToken));
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

    /// <summary>把日志抽屉的最大高度钉在窗口一半（下限 160px，保证至少能看到几行）。</summary>
    private void UpdateLogDrawerHeight(double windowHeight)
    {
        if (LogDrawerHost is null || double.IsNaN(windowHeight) || windowHeight <= 0)
        {
            return;
        }

        LogDrawerHost.MaxHeight = Math.Max(160d, windowHeight / 2d);
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
