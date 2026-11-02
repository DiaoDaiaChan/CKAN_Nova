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
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.Transformation;
using Avalonia.Threading;
using Avalonia.Automation;
using Avalonia.VisualTree;
using CkanUI.App.Animations;
using CkanUI.App.Branding;
using CkanUI.App.Diagnostics;
using CkanUI.App.Controls;
using CkanUI.App.Converters;
using CkanUI.App.Views;
using CkanUI.App.Views.Dialogs;
using CkanUI.Localization;
using CkanUI.Services.Abstractions.Commands;
using CkanUI.Services.Commands;
using CkanUI.ViewModels;
using CkanUI.ViewModels.CommandPalette;
using CkanUI.ViewModels.Dialogs;
using CkanUI.ViewModels.Onboarding;
using CkanUI.ViewModels.Pages.Instances;
using FluentAvalonia.UI.Controls;
using log4net;

namespace CkanUI.App;

/// <summary>主窗口（03 篇 §1 外壳）。导航只做命令转发，状态全部在 ShellViewModel 里。</summary>
public sealed partial class MainWindow : Window
{
    /// <summary>
    /// 侧栏分组（FluentAvalonia 官方标准做法：分组标题是菜单列表里的独立元素，不是容器）。
    /// 键必须与 <see cref="ShellViewModel"/> 里的导航键一致；插件页追加在「扩展」组末尾。
    /// </summary>
    /// <summary>
    /// 插件自述页在侧栏里是独立入口，统一归在这个标题下；业务页不再分「核心 / 工具」两组——
    /// 同一对象的多个面已经收进一个入口，再用小组标题套一层只会多两级层级。
    /// </summary>
    private const string CoreHeaderKey = "Ui_NavGroup_Core";
    private const string ToolsHeaderKey = "Ui_NavGroup_Tools";
    private const string ExtensionsHeaderKey = "Ui_NavGroup_Extensions";

    /// <summary>底部固定项：日志与设置是「随时要看」的常驻入口，不适合混在业务分组里。</summary>
    private static readonly string[] NavFooterKeys = ["logs", "settings"];

    /// <summary>分组标题的中文兜底（本地化键还没进语言包时不至于显示键名）。</summary>
    private static readonly Dictionary<string, string> NavGroupFallbacks = new(StringComparer.Ordinal)
    {
        [CoreHeaderKey] = "核心",
        [ToolsHeaderKey] = "高级工具箱",
        [ExtensionsHeaderKey] = "扩展",
    };

    private static readonly ILog Log = LogManager.GetLogger(typeof(MainWindow));

    /// <summary>顶栏共享胶囊的位移 + 宽度弹簧（首帧定位后才创建）。</summary>
    private TransformSpring? _pillSpring;

    /// <summary>顶栏激光条的位移 + 宽度弹簧。</summary>
    private TransformSpring? _barSpring;

    /// <summary>指示器是否已经定过位（首次定位置不播动画）。</summary>
    private bool _indicatorPlaced;

    /// <summary>页面内容本体的入场弹簧（换页时淡入 + 上浮落位）。</summary>
    private TransformSpring? _pageReveal;

    /// <summary>上一次激活的页面键，用来判断同组页签是向左切还是向右切。</summary>
    private string? _lastPageKey;

    /// <summary>
    /// 页面视图保活表：VM → 已实例化且常驻 <see cref="PageStage"/> 的视图。
    /// 换页只切 IsVisible、不换 PageHost.Content —— 实测把视图从可视树摘下来再挂回去会整棵重新排版
    /// （设置页 2,340 个节点要 500–900 ms），而「已挂树、只切可见性」的重新显示只要 1–2 ms。
    /// 见 docs/ops/perf-baseline.md §29。
    /// </summary>
    private readonly Dictionary<object, Control> _pageViews = new(ReferenceEqualityComparer.Instance);

    /// <summary>当前可见的页面视图，也是换页动效的落点（见 <see cref="PlayPageReveal"/>）。</summary>
    private Control? _activePageView;

    /// <summary>
    /// 空闲预热：首帧之后逐页把还没访问过的页面视图挂进 <see cref="PageStage"/> 量一次再藏起来。
    /// 首次进入某页的一次性 JIT / 模板落地成本（实测日志页 800 ms、设置页 425 ms）因此挪到用户没在看的时候；
    /// 之后真正的首次点击只剩「翻可见性」的 1–2 ms（§29 的同一个机制）。
    /// 见 docs/ops/perf-baseline.md §35。
    /// </summary>
    private readonly DispatcherTimer _pageWarmTimer = new() { Interval = TimeSpan.FromMilliseconds(420) };

    /// <summary>等待预热的导航键，按首次进入成本从高到低。</summary>
    private readonly Queue<string> _warmQueue = new();
    private int _warmIdleTicks;

    /// <summary>最近一次换页的时刻：预热会让位给正在操作的用户。</summary>
    private DateTime _lastNavigationUtc = DateTime.MinValue;

    private bool _warmTimerHooked;

    /// <summary>页面缩放的变换（首次应用缩放时创建）：LayoutTransformControl 靠它把缩放折进测量。</summary>
    private ScaleTransform? _pageZoomTransform;

    /// <summary>缩放补间的弹簧通道：滑块逐格拖动时不希望整页布局硬跳，用临界阻尼弹簧收口、不过冲。</summary>
    private SpringChannel? _pageZoomSpring;

    /// <summary>上一帧的时间戳；null 表示这是一段补间的第一帧。</summary>
    private TimeSpan? _pageZoomLastStamp;

    /// <summary>是否已经排了一帧补间（避免同一帧里重复排）。</summary>
    private bool _pageZoomFramePending;

    /// <summary>Ctrl+滚轮隧道处理器是否已挂（AttachShell 可能因 DataContext 变更被多次调用）。</summary>
    private bool _pageZoomWheelHooked;

    private CommandPaletteViewModel? _palette;
    private ICommandCatalog? _commands;
    private Func<string, string>? _translate;
    private OnboardingTourViewModel? _onboarding;

    /// <summary>外壳视图模型（导航项的来源）。</summary>
    private ShellViewModel? _shell;

    /// <summary>抽屉自动收起防抖计时器（移出后 180ms 优雅关闭，防止光标掠过边缘时频繁抖动）。</summary>
    private readonly DispatcherTimer _navDrawerCloseTimer = new() { Interval = TimeSpan.FromMilliseconds(180) };

    /// <summary>单 Tab 悬浮弹出卡片关闭防抖计时器（移出后 160ms 优雅关闭）。</summary>
    private readonly DispatcherTimer _popoutCloseTimer = new() { Interval = TimeSpan.FromMilliseconds(160) };

    /// <summary>
    /// 收起动画结束后再把显示模式降回 LeftCompact 的延时器（320ms > 0.25s 宽度过渡）。
    /// 收起瞬间就切模式会让 SplitView 立刻变成 CompactOverlay：内容先弹回左侧，面板还在 256px 原位，
    /// 观感就是「收一半先盖住内容再缩」；等动画走完再切，两种模式的静止几何完全一致，看不出接缝。
    /// </summary>
    private readonly DispatcherTimer _navPaneSettleTimer = new() { Interval = TimeSpan.FromMilliseconds(320) };

    private NavItem? _currentPopoutNavItem;
    private object? _currentPopoutSender;

    /// <summary>最近一次导航项被点击的时间戳（Environment.TickCount64），用于抑制切页瞬间误触重新弹开。</summary>
    private long _lastNavInvokedTime;

    /// <summary>代码里改选中项时置真，避免 SelectionChanged 又来改一遍视图模型（会打架）。</summary>
    private bool _syncingNav;

    /// <summary>
    /// 宿主侧动作落点（UI-27 的 <c>action:</c> 命令）。返回 true 表示已处理。
    /// 外壳自己只认「打开面板 / 快捷键目录 / 前进后退」四条，其余动作的含义属于 App 层能力
    /// （托盘动作、诊断包、更新检查、主题与密度），所以由宿主注入。
    /// </summary>
    private Func<string, bool>? _paletteActionHost;

    /// <summary>
    /// 光标指针效果层：挂载于全窗 VisualLayerManager 顶层（ZIndex = int.MaxValue），
    /// 确保始终覆盖一切弹窗、右键菜单、Flyout 与浮层（解决 PopupOverlayLayer 覆盖底层内容的问题）。
    /// </summary>
    public CursorPointerLayer? CursorPointer { get; private set; }

    public MainWindow()
    {
        InitializeComponent();

        // 窗口/任务栏图标与 exe 内嵌图标同一份 7 档 ICO（M6 / PKG-02）；文件缺失时保持平台默认外观。
        if (AppIcon.TryLoad() is { } appIcon)
        {
            Icon = appIcon;
        }

        // 动效强度变化会重写这个 token；CrossFade.Duration 是普通属性，只能在代码里跟着重建。
        ResourcesChanged += (_, _) => ApplyPageTransition();
        ApplyPageTransition();

        // 星空背景整块 IsHitTestVisible=false，拿不到自己的指针事件，只能由窗口转交。
        // 走 Tunnel + handledEventsToo：子控件（列表、表格）吃掉 Move 也不影响背景跟着亮。
        AddHandler(PointerMovedEvent, OnWindowPointerMoved, RoutingStrategies.Tunnel, handledEventsToo: true);

        // 点击涟漪同理由窗口转交：覆盖层不参与命中测试，收不到自己的按下事件。
        // 同样 handledEventsToo: 按钮、列表行内部的按下已标记 Handled，这里仍要放环。
        AddHandler(PointerPressedEvent, OnWindowPointerPressed, RoutingStrategies.Tunnel, handledEventsToo: true);
        AddHandler(PointerReleasedEvent, OnWindowPointerReleased, RoutingStrategies.Tunnel, handledEventsToo: true);
        PointerExited += OnWindowPointerExited;
        Deactivated += OnWindowDeactivated;

        // 页面切换时给一级卡片排队入场：见 ShowPage —— 页面视图常驻 PageStage，换页只切可见性，
        // 所以 PageHost.Content 从此恒定是 PageStage，不再有 Content 变化可监听。

        // 页内 TabControl（模组详情、整合包导入导出等）切换 TabItem 时同步驱动方向性弹簧过渡。
        AddHandler(SelectingItemsControl.SelectionChangedEvent, OnAnyTabControlSelectionChanged, RoutingStrategies.Bubble);

        _navDrawerCloseTimer.Tick += (_, _) =>
        {
            _navDrawerCloseTimer.Stop();
            if (NavView is null || _shell is null)
            {
                return;
            }

            if (_shell.IsNavCollapsed && NavView.IsPaneOpen)
            {
                NavView.IsPaneOpen = false;
            }
        };

        _popoutCloseTimer.Tick += (_, _) =>
        {
            _popoutCloseTimer.Stop();
            HideTabPopout(immediate: false);
        };

        _navPaneSettleTimer.Tick += (_, _) =>
        {
            _navPaneSettleTimer.Stop();
            if (NavView is null || _shell is null)
            {
                return;
            }

            // 收起动画还没走完、或者用户/抽屉又把面板打开了：继续等，别在中间切模式。
            if (!_shell.IsNavCollapsed || NavView.IsPaneOpen)
            {
                _navPaneSettleTimer.Start();
                return;
            }

            NavView.PaneDisplayMode = FANavigationViewPaneDisplayMode.LeftCompact;
        };

        if (AboutCompactButton is not null)
        {
            AboutCompactButton.PointerEntered += OnAboutPointerEntered;
            AboutCompactButton.PointerExited += OnAboutPointerExited;
        }

        if (NavView is not null)
        {
            NavView.PropertyChanged += OnNavViewPropertyChanged;
        }

        ApplyStatusBarFacts();
        InitializePerformanceMonitor();
    }

    private void OnNavViewPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property == FANavigationView.IsPaneOpenProperty)
        {
            UpdateNavPaneClasses();
        }
    }

    /// <summary>
    /// 把光标位置转给星空背景。越界（贴着窗口边缘）就当作离开，让牵引线淡出。
    /// </summary>
    private void OnWindowPointerMoved(object? sender, PointerEventArgs e)
    {
        TrackNavDrawerHover(e);

        // 细线：按住左键时每个移动事件补一个采样点；键已松开但没收到 Released（例如指针移出窗口）就收尾。
        if (DragTrail is { IsVisible: true } trail && trail.IsDragging)
        {
            if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            {
                trail.Move(e.GetPosition(trail));
            }
            else
            {
                trail.End();
            }
        }

        // 光标指针与光环：划过可交互元素（按钮/卡片/列表项等）时指针平滑微倾斜，光环亮起
        var surface = PointerAuraLayer.FindSurface(e.Source as Visual, this);
        var aura = PointerAura;
        if (aura is not null && aura.IsVisible)
        {
            aura.Track(e.GetPosition(aura), surface);
        }

        if (CursorPointer is { IsVisible: true } cursor)
        {
            cursor.Track(e.GetPosition(cursor), isOverInteractive: surface is not null);
        }

        var starfield = BackgroundStarfield;
        if (starfield is null || !starfield.IsVisible)
        {
            return;
        }

        var position = e.GetPosition(starfield);
        if (position.X < 0
            || position.Y < 0
            || position.X > starfield.Bounds.Width
            || position.Y > starfield.Bounds.Height)
        {
            starfield.TrackPointer(null);
            return;
        }

        starfield.TrackPointer(position);
    }

    /// <summary>
    /// 把按下点交给涟漪层。只认主按钮（左键 / 触摸），落点必须在窗口内。
    /// </summary>
    private void OnWindowPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        var point = e.GetCurrentPoint(this);
        if (!point.Properties.IsLeftButtonPressed)
        {
            return;
        }

        var position = point.Position;
        if (position.X < 0
            || position.Y < 0
            || position.X > Bounds.Width
            || position.Y > Bounds.Height)
        {
            return;
        }

        // 光标指针按下缩小动效
        if (CursorPointer is { IsVisible: true } cursor)
        {
            cursor.Press(e.GetPosition(cursor));
        }

        // 按住左键拖动 → 一条跟着指针走的细线。和涟漪彼此独立：涟漪关掉也照样有线。
        if (DragTrail is { IsVisible: true } trail && trail.IsMotionEnabled)
        {
            var origin = e.GetPosition(trail);
            if (origin.X >= 0 && origin.Y >= 0 && origin.X <= trail.Bounds.Width && origin.Y <= trail.Bounds.Height)
            {
                trail.Begin(origin);
            }
        }

        if (NavTabPopoutCard?.IsVisible == true)
        {
            var popoutPos = e.GetPosition(NavTabPopoutCard);
            bool insidePopout = popoutPos.X >= 0 && popoutPos.X <= NavTabPopoutCard.Bounds.Width &&
                                popoutPos.Y >= 0 && popoutPos.Y <= NavTabPopoutCard.Bounds.Height;
            if (!insidePopout)
            {
                HideTabPopout(immediate: false);
            }
        }

        var ripple = ClickRipple;
        if (ripple is { IsVisible: true, IsMotionEnabled: true })
        {
            ripple.Spawn(position);
        }

        // 按下的那一帧控件往中心塌一点，松手由弹簧弹回（与涟漪、光标指针互不影响）。
        InteractionMotion.Press(e, this);
    }

    private void OnWindowPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (e.InitialPressMouseButton == MouseButton.Left)
        {
            DragTrail?.End();

            if (CursorPointer is { IsVisible: true } cursor)
            {
                cursor.Release(e.GetPosition(cursor));
            }

            InteractionMotion.Release();
        }
    }

    private void OnWindowPointerExited(object? sender, PointerEventArgs e)
    {
        BackgroundStarfield?.TrackPointer(null);
        PointerAura?.Clear();
        CursorPointer?.Hide();
        DragTrail?.End();
        InteractionMotion.Release();

        HideTabPopout(immediate: true);

        if (_shell?.IsNavCollapsed == true && NavView?.IsPaneOpen == true)
        {
            _navDrawerCloseTimer.Stop();
            NavView.IsPaneOpen = false;
        }
    }

    private void OnWindowDeactivated(object? sender, EventArgs e)
    {
        BackgroundStarfield?.TrackPointer(null);

        // 失焦后计时器没必要继续跑：环已经没人看，清掉省掉一路 60fps 的重绘。
        ClickRipple?.Clear();
        PointerAura?.Clear();
        CursorPointer?.Clear();

        HideTabPopout(immediate: true);

        if (_shell?.IsNavCollapsed == true && NavView?.IsPaneOpen == true)
        {
            _navDrawerCloseTimer.Stop();
            NavView.IsPaneOpen = false;
        }
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

    /// <summary>自绘窗口按钮：最小化。</summary>
    private void OnMinimizeClick(object? sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    /// <summary>自绘窗口按钮：最大化 / 还原。</summary>
    private void OnMaximizeRestoreClick(object? sender, RoutedEventArgs e) =>
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

    /// <summary>自绘窗口按钮：关闭。</summary>
    private void OnCloseClick(object? sender, RoutedEventArgs e) => Close();

    /// <inheritdoc />
    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        base.OnApplyTemplate(e);

        if (CursorPointer is null)
        {
            var vlm = e.NameScope.Find<VisualLayerManager>("PART_VisualLayerManager");
            if (vlm is not null)
            {
                CursorPointer = new CursorPointerLayer();
                var addLayerMethod = typeof(VisualLayerManager).GetMethod(
                    "AddLayer",
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                addLayerMethod?.Invoke(vlm, new object[] { CursorPointer, int.MaxValue });
            }
        }
    }

    /// <summary>窗口状态变化时换图标与提示：最大化后显示「还原」的双层方块。</summary>
    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == WindowStateProperty)
        {
            UpdateMaximizeGlyph();
        }
    }

    private void UpdateMaximizeGlyph()
    {
        if (MaximizeGlyph is null)
        {
            return;
        }

        var maximized = WindowState == WindowState.Maximized;
        MaximizeGlyph.Data = Geometry.Parse(
            maximized
                ? "M6 4h9v9H6z M4 7h1.5v9h9V17.5H4z"
                : "M4.5 4.5h11v11h-11z M6 6v8h8V6z");

        if (MaximizeButton is not null)
        {
            ToolTip.SetTip(MaximizeButton, maximized ? "还原" : "最大化");
            AutomationProperties.SetName(MaximizeButton, maximized ? "还原" : "最大化");
        }
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

        // 顶栏常驻启动按钮复用主页/实例页那套外壳实现：视图层注入，VM 层不引用 App 层。
        _shell.ShellLauncher ??= new ShellInstanceLauncher();
        _shell.CommandStore ??= new LaunchCommandFile();

        _shell.NavItems.CollectionChanged += OnNavItemsChanged;
        _shell.PropertyChanged += OnShellPropertyChanged;

        RebuildNavigation();
        ApplyPaneState(_shell.IsNavExpanded);
        ApplyPageZoom(_shell.PageZoom, animate: false);
        HookPageZoomWheel();
        SyncSelectedNavItem();
        UpdateShellStatusDot(_shell.StatusText);
        _lastPageKey = _shell.SelectedNavItem?.Key;

        StartPageWarmup();

        // 窗口构建时 CurrentPage 可能已经有值（ShellViewModel 构造期就导航到首页），
        // 此时不会再有 PropertyChanged，必须主动把第一页切到可见。
        ShowPage(_shell.CurrentPage);
        Dispatcher.UIThread.Post(SyncSectionTabIndicator, DispatcherPriority.Loaded);
    }

    /// <summary>
    /// 把 <paramref name="page"/> 对应的视图切成可见。视图只建一次、之后常驻 <see cref="PageStage"/>，
    /// 再次进入只切 IsVisible —— 这样切页不再触发整棵可视树重新测量。见 docs/ops/perf-baseline.md §29。
    /// </summary>
    /// <summary>重页面是否该在这一刻预热：窗口在后台，或前台静置超过 <see cref="ExpensiveWarmIdle"/>。</summary>
    /// <param name="windowActive">窗口当前是否处于激活状态。</param>
    /// <param name="sinceNavigation">距上次换页的时间。</param>
    public static bool ShouldWarmExpensive(bool windowActive, TimeSpan sinceNavigation) =>
        !windowActive || sinceNavigation > ExpensiveWarmIdle;

    /// <summary>首帧后开始逐页预热，每 tick 只热一页，避免连排几帧。</summary>
    private void StartPageWarmup()
    {
        if (_shell is null || _warmQueue.Count > 0)
        {
            return;
        }

        foreach (var key in WarmupOrder)
        {
            if (_shell.NavItems.Any(item => string.Equals(item.Key, key, StringComparison.OrdinalIgnoreCase)))
            {
                _warmQueue.Enqueue(key);
            }
        }

        if (_warmQueue.Count == 0)
        {
            return;
        }

        _warmIdleTicks = 0;
        if (!_warmTimerHooked)
        {
            _pageWarmTimer.Tick += OnPageWarmTick;
            PropertyChanged += OnWarmWindowPropertyChanged;
            _warmTimerHooked = true;
        }

        _pageWarmTimer.Start();
    }

    private void OnPageWarmTick(object? sender, EventArgs e)
    {
        if (_shell is null || PageStage is null)
        {
            _pageWarmTimer.Stop();
            return;
        }

        // 用户正在点：这一轮让位，稍后再热。
        if (DateTime.UtcNow - _lastNavigationUtc < TimeSpan.FromMilliseconds(1200))
        {
            return;
        }

        // 重页面预热会一次性占住 UI 线程（设置页约 830 ms，实测见 §35.5）。窗口在前台时
        // 那是一次肉眼可见的冻结，所以留到窗口失焦、或前台静置很久之后再热。
        var sinceNavigation = DateTime.UtcNow - _lastNavigationUtc;
        if (++_warmIdleTicks > WarmIdleTickLimit)
        {
            _pageWarmTimer.Stop();
            return;
        }

        while (_warmQueue.Count > 0)
        {
            if (ExpensiveWarmups.Contains(_warmQueue.Peek()) && !ShouldWarmExpensive(IsActive, sinceNavigation))
            {
                return;
            }

            if (WarmPage(_warmQueue.Dequeue()))
            {
                return;
            }
        }

        _pageWarmTimer.Stop();
    }

    /// <summary>窗口切到后台时把预热定时器叫醒：重页面就是等这一刻才热的。</summary>
    private void OnWarmWindowPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property != IsActiveProperty || IsActive || _warmQueue.Count == 0)
        {
            return;
        }

        _warmIdleTicks = 0;
        _pageWarmTimer.Start();
    }

    /// <summary>把一个页面视图挂进舞台、透明地跑一次完整排版，然后藏起来。</summary>
    private bool WarmPage(string key)
    {
        if (_shell is null || PageStage is null || _shell.PeekPage(key) is not { } page)
        {
            return false;
        }

        if (_pageViews.TryGetValue(page, out var existing))
        {
            existing.IsVisible = false;
            return false;
        }

        if (new ViewLocator().Build(page) is not { } view)
        {
            return false;
        }

        _pageViews[page] = view;
        PageStage.Children.Add(view);
        view.Opacity = 0;
        view.IsHitTestVisible = false;
        view.IsVisible = true;
        var clock = System.Diagnostics.Stopwatch.StartNew();
        Dispatcher.UIThread.Post(() =>
        {
            FinishWarm(view);
            if (Environment.GetEnvironmentVariable("CKANUI_NAV_TRACE") is { Length: > 0 } trace)
            {
                try { File.AppendAllText(trace, $"[warm] {key} attach={clock.ElapsedMilliseconds}ms\n"); } catch (IOException) { }
            }
        }, DispatcherPriority.ContextIdle);
        return true;
    }

    private static void FinishWarm(Control view)
    {
        view.IsVisible = false;
        view.Opacity = 1;
        view.IsHitTestVisible = true;
    }

    /// <summary>一次性吃满 UI 线程的重页面：只在窗口后台或长时间静置时预热。</summary>
    private static readonly HashSet<string> ExpensiveWarmups = new(StringComparer.Ordinal) { "settings" };

    /// <summary>前台静置超过这么久，才认为用户不在看（此时预热重页面不会看见冻结）。</summary>
    private static readonly TimeSpan ExpensiveWarmIdle = TimeSpan.FromSeconds(45);

    /// <summary>预热定时器最多空转的 tick 数，超过就停表，避免为一个页面永远轮询。</summary>
    private const int WarmIdleTickLimit = 150;

    /// <summary>预热顺序：按实测首次进入成本从高到低（§21.2 / §27.2）。</summary>
    private static readonly string[] WarmupOrder =
    [
        "logs", "settings", "unmanaged", "modpack-editor", "mods",
        "content", "downloadstats", "modpacks", "ships", "saves"
    ];

    private void ShowPage(object? page)
    {
        if (PageStage is null)
        {
            return;
        }

        if (page is null)
        {
            foreach (var child in PageStage.Children)
            {
                child.IsVisible = false;
            }

            _activePageView = null;
            return;
        }

        if (!_pageViews.TryGetValue(page, out var view))
        {
            if (new ViewLocator().Build(page) is not { } built)
            {
                return;
            }

            view = built;
            _pageViews[page] = view;
            PageStage.Children.Add(view);
        }

        if (ReferenceEquals(_activePageView, view))
        {
            return;
        }

        _lastNavigationUtc = DateTime.UtcNow;

        foreach (var child in PageStage.Children)
        {
            child.IsVisible = ReferenceEquals(child, view);
        }

        _activePageView = view;
        SchedulePageReveal();
    }

    private void SchedulePageReveal()
    {
        if (PageHost is null)
        {
            return;
        }

        var oldKey = _lastPageKey;

        // 紧跟在 ShellViewModel.OnNavigated 同步更新完 SelectedNavItem 与 SectionTabs 之后执行：
        // 页面内容本体保持 (0, 0) 零位移（杜绝一切布局与边框抖动），
        // 依靠顶栏共享胶囊/激光条跨页签滑行 + 顶部极光天际线光束扫掠完成丝滑切换。
        Dispatcher.UIThread.Post(
            () =>
            {
                if (PageHost is null)
                {
                    return;
                }

                var newKey = _shell?.SelectedNavItem?.Key;
                var direction = ComputeTabSwitchDirection(oldKey, newKey);
                _lastPageKey = newKey;

                SyncSectionTabIndicator();
                TriggerTabSwitchAtmosphere(direction);
                PlayPageReveal(direction);
            },
            DispatcherPriority.Send);
    }

    /// <summary>
    /// 换页入场：内容本体「按页签方向落到偏下偏一侧的起点，再交给弹簧浮回原位」，一级元素随后错峰补位。
    /// </summary>
    /// <remarks>
    /// <para>与 <see cref="ApplyPageTransition"/> 里被否掉的三种整页过渡不是一回事：那三种都让**两页同时在屏幕上**，
    /// 所以才有双影与亮度凹陷。这里换页仍是瞬时替换，只有新内容参与入场，屏幕上永远只有一页。</para>
    /// <para>起点与第一次目标切换在同一个调度回合里完成，渲染前就已经落到起点，不会闪一帧原位。</para>
    /// </remarks>
    private void PlayPageReveal(int direction)
    {
        // 抽屉/浮条是窗口级浮层，与换页无关；挂在窗口上，只在首次调用时接管（ConditionalWeakTable 去重）。
        SurfaceMotion.Attach(this);

        // 标题栏/状态条/工具条不在 PageHost 里，页面级挂载扫不到；整窗挂一次，导航树由 IconHoverRole 负责，这里跳过。
        HoverLiftMotion.AttachShell(this);

        // 窗口背景图也在 PageHost 外面：只接管一次，源不变就不会重播。
        ImageLoadMotion.Attach(this);

        if (PageHost is null || MotionBudget.IsCollapsed)
        {
            return;
        }

        // 动效落点是**当前可见的页面视图**，不是 PageHost：其它页面视图也常驻在 PageStage 里（只是不可见），
        // 拿 PageHost 当根会把所有页面一起扫、一起编排。
        var stage = _activePageView ?? (Control)PageHost;

        // 提示条按 IsVisible 显隐，样式的 Opacity 过渡没有中间帧；这里挂上"出现即上浮淡入"。
        BannerMotion.Attach(stage);

        // 空态 ↔ 数据态这类同格叠放层互斥切换，样式过渡会从 t=0 重来；接管成交互可打断的弹簧。
        ViewLayerMotion.Attach(stage);

        // 卡片悬停抬升改由弹簧帧驱动：鼠标扫过一排卡片时位移之间带速度，而不是每张都从零起步。
        HoverLiftMotion.Attach(stage);

        // 缩略图/截图解码完就是一张位图，渲染出来是"啪"地出现；源变了就淡一次。
        ImageLoadMotion.Attach(stage);

        // 页内页签：下划线弹簧冲量 + 内容舞台按切换方向滑进来（原来样式给了过渡却没人改 RenderTransform）。
        TabStripMotion.Attach(stage);

        // 展开即淡入：Expander 与 TreeViewItem 展开时正文一帧硬切出现，补一段上浮淡入。
        ExpandMotion.Attach(stage);

        // 选中行与键盘焦点原来只有静态描边/背景，补一次弹簧冲量；整窗挂一次，标题栏按钮的键盘焦点也在内。
        SelectionMotion.Attach(this);
        SelectionMotion.Attach(stage);

        // 一页放不下的信息流：滚动时刚跨进底边的元素各自补一次入场，所有页面自动生效。
        ScrollRevealMotion.Attach(stage);

        // 列表数据晚于换页到达：行按 10 行一批错峰浮上来，而不是整块"啪"地出现。
        FeedArrivalMotion.Attach(stage);

        // 键盘方向键换行时，滚动条不再瞬跳：目标位置照旧计算，弹簧只负责滑过去，下一次按键可打断。
        SmoothScrollMotion.Attach(stage);

        if (!MotionBudget.AllowsPageChoreography)
        {
            // 精简档不做整页与块级编排，只保留文字的轻量淡入（幅度由 TextRevealChoreographer 自己按档位收）。
            TextRevealChoreographer.Play(stage);
            return;
        }

        // 整页姿态：同一个弹簧实例接管 PageHost，被下一次换页打断时从当前位置继续，不会回零重来。
        _pageReveal ??= new TransformSpring(PageHost, SpringSpec.Glide, SpringSpec.Glide);

        // 位移带着页签方向：向右切页时内容从右侧补进来，向左切则相反，同组首次只上浮。
        // 只有上一段入场已经落位时才重新摆姿态；还在飞的时候再摆一次 = 把页面拉回起始位移，
        // 连点页签看到的就是"跳一下再走"。飞在半路就让当前这一段带着速度直接收尾。
        if (_pageReveal.IsSettled)
        {
            var pose = PageRevealSpec.For(direction);
            _pageReveal.MoveTo(
                x: pose.OffsetX,
                y: pose.OffsetY,
                scaleX: pose.StartScale,
                scaleY: pose.StartScale,
                opacity: pose.StartOpacity,
                snap: true);
        }

        _pageReveal.MoveTo(x: 0d, y: 0d, scaleX: 1d, scaleY: 1d, opacity: 1d);

        // 一级元素只做错峰上浮，不再压一次透明度：整页已经淡入，两层一起淡会看着更闷。
        // 同步调用而不是排到 Loaded：可视树在 Content 赋值时就已建好，先落起点才能保证首帧不闪。
        if (EntranceChoreographer.Play(stage, includeOpacity: false) == 0)
        {
            // 模板还没套上（例如首次显示）时退一步，等排版完成再补一次。
            Dispatcher.UIThread.Post(
                () =>
                {
                    if (PageHost is not null)
                    {
                        EntranceChoreographer.Play(stage, includeOpacity: false);
                    }
                },
                DispatcherPriority.Loaded);
        }

        // 文字逐行上屏：块级编排之外再给"正在加载内容"一个可读的节奏。
        // 模板可能晚一拍才实例化出 TextBlock，返回 0 时在 Loaded 补一次，避免整页文字静默出现。
        if (TextRevealChoreographer.Play(stage) == 0)
        {
            Dispatcher.UIThread.Post(
                () =>
                {
                    if (PageHost is not null)
                    {
                        TextRevealChoreographer.Play(stage);
                    }
                },
                DispatcherPriority.Loaded);
        }
    }

    private void SyncSectionTabIndicator()
    {
        if (SectionTabsControl is null || SectionTabActivePill is null || SectionTabActiveBar is null)
        {
            return;
        }

        if (_shell?.HasSectionTabs != true)
        {
            SectionTabActivePill.Opacity = 0;
            SectionTabActiveBar.Opacity = 0;
            return;
        }

        var activeTabBorder = SectionTabsControl
            .GetVisualDescendants()
            .OfType<Border>()
            .FirstOrDefault(b => b.Classes.Contains("CkanTabUnderline") && b.Classes.Contains("Active"));

        if (activeTabBorder is null || activeTabBorder.Bounds.Width <= 0)
        {
            // 刚切到新分组、页签条尚未完成首次排版时，等 Loaded 排版完成后补齐位置。
            Dispatcher.UIThread.Post(
                () =>
                {
                    var deferred = SectionTabsControl?
                        .GetVisualDescendants()
                        .OfType<Border>()
                        .FirstOrDefault(b => b.Classes.Contains("CkanTabUnderline") && b.Classes.Contains("Active"));
                    if (deferred is not null && deferred.Bounds.Width > 0)
                    {
                        ApplySectionTabIndicatorGeometry(deferred);
                    }
                },
                DispatcherPriority.Loaded);
            return;
        }

        ApplySectionTabIndicatorGeometry(activeTabBorder);
    }

    private void ApplySectionTabIndicatorGeometry(Border activeTabBorder)
    {
        if (SectionTabsControl is null || SectionTabActivePill is null || SectionTabActiveBar is null)
        {
            return;
        }

        var origin = activeTabBorder.TranslatePoint(new Point(0, 0), SectionTabsControl);
        var x = origin?.X ?? 0;
        var w = activeTabBorder.Bounds.Width;
        if (w <= 0)
        {
            return;
        }

        var pillScale = Math.Max(0.1, w / 100.0);
        var barWidth = Math.Max(14.0, w - 16.0);
        var barScale = barWidth / 100.0;
        var barX = x + (w - barWidth) * 0.5;

        SectionTabActivePill.Opacity = 1;
        SectionTabActiveBar.Opacity = 1;

        // 位移走临界阻尼（胶囊过冲会啃到相邻页签的文字），宽度走欠阻尼（回弹感只留给形变）。
        // 两个控件各自一根弹簧：跨页签连点时目标换向，速度留着 —— 胶囊是被"接住拐弯"，不是急停再起步。
        _pillSpring ??= new TransformSpring(SectionTabActivePill, SpringSpec.Glide, SpringSpec.Snappy);
        _barSpring ??= new TransformSpring(SectionTabActiveBar, SpringSpec.Glide, SpringSpec.Snappy);

        // 首次定位直接落位：否则冷启动时胶囊会从 0 处横穿整条页签栏飞过来。
        var snap = !_indicatorPlaced;
        _pillSpring.MoveTo(x: x, scaleX: pillScale, snap: snap);
        _barSpring.MoveTo(x: barX, scaleX: barScale, snap: snap);
        _indicatorPlaced = true;
    }

    private void TriggerTabSwitchAtmosphere(int direction)
    {
        if (Application.Current?.Resources.TryGetResource(Theming.MotionHost.SpringSoftKey, null, out var durationObj) == true &&
            durationObj is TimeSpan ts && ts <= TimeSpan.Zero)
        {
            return;
        }

        if (TabSwitchHorizonBeam is not null && direction != 0)
        {
            TabSwitchHorizonBeam.RenderTransformOrigin = direction < 0
                ? new RelativePoint(1, 0.5, RelativeUnit.Relative)
                : new RelativePoint(0, 0.5, RelativeUnit.Relative);
            TabSwitchHorizonBeam.Classes.Remove("is-sweeping");
            TabSwitchHorizonBeam.Classes.Add("is-sweeping");
        }
    }

    private int ComputeTabSwitchDirection(string? oldKey, string? newKey)
    {
        if (_shell is null || string.IsNullOrEmpty(oldKey) || string.IsNullOrEmpty(newKey) ||
            string.Equals(oldKey, newKey, StringComparison.OrdinalIgnoreCase))
        {
            return 0;
        }

        foreach (var section in _shell.Sections)
        {
            var oldIndex = -1;
            var newIndex = -1;
            for (var i = 0; i < section.Tabs.Count; i++)
            {
                if (string.Equals(section.Tabs[i].Key, oldKey, StringComparison.OrdinalIgnoreCase))
                {
                    oldIndex = i;
                }

                if (string.Equals(section.Tabs[i].Key, newKey, StringComparison.OrdinalIgnoreCase))
                {
                    newIndex = i;
                }
            }

            if (oldIndex >= 0 && newIndex >= 0 && oldIndex != newIndex)
            {
                return newIndex > oldIndex ? 1 : -1;
            }
        }

        return 0;
    }

    private void OnAnyTabControlSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        // 页内 TabControl 的切换动画完全由 TabItem 模板内的 PART_TabUnderline 弹簧展开驱动，
        // 内容承载区保持恒定不透明度与零位移，避免闪烁与抖动。
    }

    private void UpdateShellStatusDot(string? status)
    {
        if (ShellStatusDot is null)
        {
            return;
        }

        var text = status ?? string.Empty;
        var brushKey = text.Contains("错误", StringComparison.Ordinal) || text.Contains("失败", StringComparison.Ordinal)
            ? "CkanWarningBrush"
            : "CkanSuccessBrush";

        if (this.TryFindResource(brushKey, out var res) && res is IBrush brush)
        {
            ShellStatusDot.Fill = brush;
        }
    }

    private void OnNavItemsChanged(object? sender, NotifyCollectionChangedEventArgs e) => RebuildNavigation();

    private void OnShellPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ShellViewModel.CurrentPage))
        {
            ShowPage(_shell?.CurrentPage);
        }
        else if (e.PropertyName == nameof(ShellViewModel.SelectedNavItem))
        {
            SyncSelectedNavItem();
        }
        else if (e.PropertyName == nameof(ShellViewModel.IsNavCollapsed) && _shell is not null)
        {
            ApplyPaneState(_shell.IsNavExpanded);
        }
        else if (e.PropertyName == nameof(ShellViewModel.StatusText) && _shell is not null)
        {
            UpdateShellStatusDot(_shell.StatusText);
        }
        else if (e.PropertyName == nameof(ShellViewModel.PageZoom))
        {
            ApplyPageZoom(_shell?.PageZoom ?? ShellViewModel.DefaultPageZoom, animate: true);
        }
        else if (e.PropertyName == nameof(ShellViewModel.NavHoverMode))
        {
            if (_shell?.NavHoverMode == NavHoverMode.Drawer)
            {
                HideTabPopout(immediate: true);
            }
            else if (NavView?.IsPaneOpen == true)
            {
                NavView.IsPaneOpen = false;
            }
        }

        // 日志抽屉与未保存提示条的显隐完全由 XAML 绑定（Classes.is-open + is-active）驱动，
        // 代码侧不再插手：第二阶段只留骨骼，第三阶段的过渡写在样式里，不需要事件。
    }

    /// <summary>Ctrl+滚轮接管页面缩放：隧道阶段装处理器，页面里的 ScrollViewer 抢不到。</summary>
    private void HookPageZoomWheel()
    {
        if (PageZoomHost is null || _pageZoomWheelHooked)
        {
            return;
        }

        _pageZoomWheelHooked = true;
        PageZoomHost.AddHandler(PointerWheelChangedEvent, OnPageZoomWheel, RoutingStrategies.Tunnel, handledEventsToo: true);
    }

    private void OnPageZoomWheel(object? sender, PointerWheelEventArgs e)
    {
        if (_shell is null || !e.KeyModifiers.HasFlag(KeyModifiers.Control) || Math.Abs(e.Delta.Y) < 0.01)
        {
            return;
        }

        _shell.NudgePageZoom(e.Delta.Y > 0 ? 1 : -1);
        e.Handled = true;
    }

    /// <summary>
    /// 把缩放比例落到页面承载区。<paramref name="animate"/> 为 false 时直接落位——
    /// 接管外壳时不该看见整页从 100% 弹过来。
    /// </summary>
    private void ApplyPageZoom(double zoom, bool animate)
    {
        if (PageZoomHost is null)
        {
            return;
        }

        _pageZoomTransform ??= AttachPageZoomTransform();

        var target = Math.Clamp(zoom, ShellViewModel.MinPageZoom, ShellViewModel.MaxPageZoom);
        if (!animate || MotionBudget.IsCollapsed)
        {
            // 动效关闭：不排帧，缩放直接落位（减少动效档位下布局也不该逐帧重排）。
            _pageZoomSpring?.SnapTo(target);
            SetPageZoomScale(target);
            return;
        }

        // 缩放专用弹簧：临界阻尼（ζ=1，不过冲——过一次冲整页会跟着抖两下），ω≈30 rad/s 约 200ms 收口；
        // 静止阈值压到 0.05% 量级，否则默认的 0.25 会让缩放停在离目标 20% 的地方。
        _pageZoomSpring ??= new SpringChannel(
            _pageZoomTransform.ScaleX,
            new SpringSpec(1d, 900d, 60d),
            restPosition: 0.0005d,
            restVelocity: 0.01d);
        _pageZoomSpring.Retarget(target);
        RequestPageZoomFrame();
    }

    private ScaleTransform AttachPageZoomTransform()
    {
        var transform = new ScaleTransform(1d, 1d);
        PageZoomHost!.LayoutTransform = transform;
        return transform;
    }

    private void SetPageZoomScale(double scale)
    {
        if (_pageZoomTransform is null)
        {
            return;
        }

        _pageZoomTransform.ScaleX = scale;
        _pageZoomTransform.ScaleY = scale;
    }

    private void RequestPageZoomFrame()
    {
        if (_pageZoomFramePending)
        {
            return;
        }

        if (TopLevel.GetTopLevel(this) is not { } topLevel)
        {
            // 还没挂进可视树：没有 VSync 可等，直接把弹簧推到目标，别让页面停在中间比例。
            var settled = _pageZoomSpring?.Target ?? _pageZoomTransform?.ScaleX ?? ShellViewModel.DefaultPageZoom;
            _pageZoomSpring?.SnapTo(settled);
            SetPageZoomScale(settled);
            return;
        }

        _pageZoomFramePending = true;
        _pageZoomLastStamp = null;
        topLevel.RequestAnimationFrame(OnPageZoomFrame);
    }

    private void OnPageZoomFrame(TimeSpan timestamp)
    {
        _pageZoomFramePending = false;

        if (_pageZoomSpring is null)
        {
            return;
        }

        // 与 MotionFramePump 同一口径：首帧按 60Hz 起步，掉帧时封顶 1/30s，
        // 否则一次 200ms 停顿会把弹簧按真实时间整段推过去，看起来像瞬移。
        var delta = _pageZoomLastStamp is { } last ? (timestamp - last).TotalSeconds : 1.0 / 60.0;
        _pageZoomLastStamp = timestamp;
        if (!(delta > 0))
        {
            delta = 1.0 / 60.0;
        }

        delta = Math.Min(delta, 1.0 / 30.0);

        var moving = _pageZoomSpring.Advance(delta);
        SetPageZoomScale(_pageZoomSpring.Value);
        if (moving)
        {
            RequestPageZoomFrame();
        }
        else
        {
            _pageZoomLastStamp = null;
        }
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

        var menu = new List<object>(_shell.RailItems.Count + 1);
        var extensionsHeaderAdded = false;
        foreach (var item in _shell.RailItems)
        {
            // 入口表之外的项就是插件自述页：第一次遇到时补一个「扩展」标题，后面跟在一起。
            if (!extensionsHeaderAdded && !_shell.Sections.Any(section => ReferenceEquals(section.RailItem, item)))
            {
                menu.Add(new FANavigationViewItemHeader { Content = ResolveGroupTitle(ExtensionsHeaderKey) });
                extensionsHeaderAdded = true;
            }

            menu.Add(CreateNavItem(item));
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

    /// <summary>本来就"会转"的图标：长悬浮时绕中心连续旋转，其余图标改成轻微摇摆。</summary>
    private static readonly HashSet<string> SpinningGlyphs = new(StringComparer.OrdinalIgnoreCase)
    {
        "Settings", "Sync", "History", "Clock", "Globe", "Exchange", "Storage", "Archive",
    };

    private FANavigationViewItem CreateNavItem(NavItem nav)
    {
        var item = new FANavigationViewItem
        {
            Content = nav.Title,
            Tag = nav,
        };

        // 仅在侧边栏抽屉模式且展开受阻时使用系统原生 ToolTip；
        // 在单 Tab 悬停弹出卡片模式下，由精美的悬浮亚克力卡片呈现标题与子项，禁用原生 ToolTip 避免两个浮层相互叠压冲突。
        if (_shell is not null && _shell.NavHoverMode == NavHoverMode.Drawer)
        {
            ToolTip.SetTip(item, nav.Title);
            ToolTip.SetShowDelay(item, 300);
        }

        if (NavGlyphToGeometryConverter.Instance.Convert(nav.Glyph, typeof(Geometry), null, CultureInfo.InvariantCulture) is Geometry geometry)
        {
            item.IconSource = new FAPathIconSource { Data = geometry };
        }

        if (SpinningGlyphs.Contains(nav.Glyph))
        {
            IconHoverMotion.SetShape(item, IconHoverShape.Spin);
        }

        item.PointerEntered += OnNavItemPointerEntered;
        item.PointerExited += OnNavItemPointerExited;

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

        // 页面项 → 它所属的入口项：停在「存档」时高亮的是侧栏「实例」。
        var anchor = _shell.RailItemFor(_shell.SelectedNavItem?.Key);
        var target = anchor is null
            ? null
            : NavView.MenuItems.Concat(NavView.FooterMenuItems)
                .OfType<FANavigationViewItem>()
                .FirstOrDefault(item => ReferenceEquals(item.Tag, anchor));

        _syncingNav = true;
        try
        {
            NavView.SelectedItem = target;
        }
        finally
        {
            _syncingNav = false;
        }

        ApplyNavSelectionVisual(target);
    }

    /// <summary>
    /// FluentAvalonia 的旧项指示条会用 600ms 淡出，切页瞬间新旧两条并排；
    /// 这里只让当前项保留指示条，其余项立即隐藏，保证导航永远是单选观感。
    /// </summary>
    private void ApplyNavSelectionVisual(object? selectedContainer)
    {
        if (NavView is null)
        {
            return;
        }

        foreach (var item in NavView.MenuItems.Concat(NavView.FooterMenuItems).OfType<FANavigationViewItem>())
        {
            item.Classes.Set("nav-selected", ReferenceEquals(item, selectedContainer));
        }
    }

    /// <summary>左侧导航点击 → 写回视图模型的选中项（视图模型自己负责导航与历史栈）。</summary>
    private void OnNavSelectionChanged(object? sender, FANavigationViewSelectionChangedEventArgs args)
    {
        _lastNavInvokedTime = Environment.TickCount64;
        HideTabPopout(immediate: true);

        if (_syncingNav || _shell is null)
        {
            return;
        }

        ApplyNavSelectionVisual(args.SelectedItemContainer);

        if (args.SelectedItemContainer?.Tag is NavItem nav)
        {
            _shell.SelectedNavItem = nav;
        }

        // 指示器滑动由 FluentAvalonia 自己播（600ms 组合动画，不可配）；这里补的是点击瞬间的追赶位移。
        // 冲量交给弹簧：连点时在当前位置叠加，不会像「挂 110ms 类再摘」那样把上一次的运动瞬间掐断。
        MotionFeedback.PulseNav(args.SelectedItemContainer as Control);
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

        _navDrawerCloseTimer.Stop();
        _navPaneSettleTimer.Stop();

        // 展开必须用 Left（inline）：内容整体右移、被"推开"；LeftCompact 的底层是 CompactOverlay，
        // 展开只是浮在页面上盖住内容 —— 那就是"变成覆盖了"。
        // 收起时先留在 Left 把宽度动画走完（内容与面板同步回缩），动画结束后再由 _navPaneSettleTimer
        // 降回 LeftCompact（保留精简轨 + 悬浮抽屉两种态）。
        if (expanded)
        {
            HideTabPopout(immediate: true);
            NavView.PaneDisplayMode = FANavigationViewPaneDisplayMode.Left;
        }

        NavView.IsPaneOpen = expanded;

        if (!expanded && NavView.PaneDisplayMode != FANavigationViewPaneDisplayMode.LeftCompact)
        {
            _navPaneSettleTimer.Start();
        }

        // 钉住常驻展开模式（用户通过按钮或快捷键展开）：
        // 给 NavView 打上 PanePinned 类，使得 CSS 样式中把 LightDismissLayer 的命中和可见性禁用，
        // 达到与原生 Left 模式一致的主界面操作体验，且彻底保留丝滑的收起折叠动画！
        NavView.Classes.Set("PanePinned", expanded);
        UpdateNavPaneClasses();
    }

    private void UpdateNavPaneClasses()
    {
        if (NavView is null)
        {
            return;
        }

        bool isOpen = NavView.IsPaneOpen;
        NavView.Classes.Set("PaneOpen", isOpen);
        NavView.Classes.Set("Collapsed", !isOpen);
    }

    /// <summary>
    /// 光标进入左侧精简轨（<=52px）自动以抽屉浮层（CompactOverlay）滑出展开；
    /// 光标离开展开区域（>256px）经 180ms 优雅防抖后自动收起。
    /// </summary>
    private void TrackNavDrawerHover(PointerEventArgs e)
    {
        if (NavView is null || _shell is null)
        {
            return;
        }

        // 常驻展开模式（Left）由用户固定钉住，不响应悬停抽屉逻辑
        if (!_shell.IsNavCollapsed)
        {
            _navDrawerCloseTimer.Stop();
            return;
        }

        // 模式分支：单Tab悬浮弹出模式下不展开完整侧边栏抽屉
        if (_shell.NavHoverMode != NavHoverMode.Drawer)
        {
            if (NavView.IsPaneOpen)
            {
                NavView.IsPaneOpen = false;
            }
            return;
        }

        // 刚点击导航项后提供 350ms 冷却时间，避免点击完成关上后光标微动又立即弹开
        if (Environment.TickCount64 - _lastNavInvokedTime < 350)
        {
            return;
        }

        var pos = e.GetPosition(NavView);
        var height = NavView.Bounds.Height;
        if (height <= 0)
        {
            return;
        }

        double activeWidth = NavView.IsPaneOpen ? NavView.OpenPaneLength : NavView.CompactPaneLength;
        bool isInside = pos.X >= 0 && pos.X <= activeWidth && pos.Y >= 0 && pos.Y <= height;

        if (isInside)
        {
            _navDrawerCloseTimer.Stop();
            if (!NavView.IsPaneOpen)
            {
                // 抽屉是浮层语义：万一收起收尾的延时还没跑（dispatcher 被占住），这里补一刀回到 LeftCompact，
                // 免得悬停时把内容整体推开。模式切换只在真正要浮出前发生，用户看不到跳变。
                if (NavView.PaneDisplayMode != FANavigationViewPaneDisplayMode.LeftCompact)
                {
                    NavView.PaneDisplayMode = FANavigationViewPaneDisplayMode.LeftCompact;
                }

                NavView.IsPaneOpen = true;
            }
        }
        else if (NavView.IsPaneOpen)
        {
            if (!_navDrawerCloseTimer.IsEnabled)
            {
                _navDrawerCloseTimer.Start();
            }
        }
    }

    private void OnNavItemPointerEntered(object? sender, PointerEventArgs e)
    {
        if (_shell?.IsNavCollapsed != true || _shell?.NavHoverMode != NavHoverMode.TabPopout)
        {
            return;
        }

        if (sender is FANavigationViewItem item && item.Tag is NavItem nav)
        {
            ShowTabPopout(item, nav);
        }
    }

    private void OnNavItemPointerExited(object? sender, PointerEventArgs e)
    {
        if (_shell?.IsNavCollapsed != true || _shell?.NavHoverMode != NavHoverMode.TabPopout)
        {
            return;
        }

        _popoutCloseTimer.Stop();
        _popoutCloseTimer.Start();
    }

    private void OnAboutPointerEntered(object? sender, PointerEventArgs e)
    {
        if (_shell?.IsNavCollapsed != true || _shell?.NavHoverMode != NavHoverMode.TabPopout)
        {
            return;
        }

        if (sender is Control control)
        {
            ShowAboutPopout(control);
        }
    }

    private void OnAboutPointerExited(object? sender, PointerEventArgs e)
    {
        if (_shell?.IsNavCollapsed != true || _shell?.NavHoverMode != NavHoverMode.TabPopout)
        {
            return;
        }

        _popoutCloseTimer.Stop();
        _popoutCloseTimer.Start();
    }

    private void OnNavPopoutCardPointerEntered(object? sender, PointerEventArgs e)
    {
        _popoutCloseTimer.Stop();
    }

    private void OnNavPopoutCardPointerExited(object? sender, PointerEventArgs e)
    {
        _popoutCloseTimer.Stop();
        _popoutCloseTimer.Start();
    }

    private void ShowTabPopout(Control anchor, NavItem nav)
    {
        if (NavTabPopoutCard is null || NavPopoutCanvas is null || _shell is null)
        {
            return;
        }

        _popoutCloseTimer.Stop();
        _currentPopoutNavItem = nav;
        _currentPopoutSender = anchor;

        var pt = anchor.TranslatePoint(new Point(0, 0), NavPopoutCanvas);
        double top = pt?.Y ?? 0;
        double maxTop = Math.Max(10, Bounds.Height - 340);
        top = Math.Clamp(top, 10, maxTop);
        Canvas.SetTop(NavTabPopoutCard, top);

        if (NavGlyphToGeometryConverter.Instance.Convert(nav.Glyph, typeof(Geometry), null, CultureInfo.InvariantCulture) is Geometry geom)
        {
            NavPopoutIcon.Data = geom;
        }

        NavPopoutTitle.Text = nav.Title;
        var section = _shell.Sections.FirstOrDefault(s => ReferenceEquals(s.RailItem, nav) || s.Tabs.Any(t => string.Equals(t.Key, nav.Key, StringComparison.OrdinalIgnoreCase)));

        var activeRail = _shell.RailItemFor(_shell.SelectedNavItem?.Key);
        bool isActive = ReferenceEquals(activeRail, nav) || string.Equals(_shell.SelectedNavItem?.Key, nav.Key, StringComparison.OrdinalIgnoreCase);
        NavPopoutActiveBadge.IsVisible = isActive;

        if (section is not null && section.Tabs.Count > 1)
        {
            NavPopoutSubtitle.Text = $"{section.Tabs.Count} 个页面选项";
            NavPopoutSubTabsHost.Children.Clear();
            NavPopoutSubTabsHost.IsVisible = true;

            foreach (var tab in section.Tabs)
            {
                var isSubActive = string.Equals(_shell.SelectedNavItem?.Key, tab.Key, StringComparison.OrdinalIgnoreCase);
                var btn = new Button
                {
                    Classes = { "CkanGhost" },
                    HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch,
                    HorizontalContentAlignment = Avalonia.Layout.HorizontalAlignment.Left,
                    Padding = new Thickness(8, 5),
                    CornerRadius = new CornerRadius(6),
                    Tag = tab.Key,
                };

                if (isSubActive)
                {
                    if (this.TryFindResource("CkanAccentSubtleBrush", out var subBg) && subBg is IBrush subBgBrush)
                    {
                        btn.Background = subBgBrush;
                    }
                    if (this.TryFindResource("CkanAccentBrush", out var accBorder) && accBorder is IBrush accBorderBrush)
                    {
                        btn.BorderBrush = accBorderBrush;
                    }
                    btn.BorderThickness = new Thickness(1);
                }

                var stack = new StackPanel
                {
                    Orientation = Avalonia.Layout.Orientation.Horizontal,
                    Spacing = 8,
                    VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
                };

                if (NavGlyphToGeometryConverter.Instance.Convert(tab.Glyph, typeof(Geometry), null, CultureInfo.InvariantCulture) is Geometry subGeom)
                {
                    var icon = new PathIcon
                    {
                        Data = subGeom,
                        Width = 12,
                        Height = 12,
                    };
                    if (this.TryFindResource(isSubActive ? "CkanAccentBrush" : "CkanTextSecondaryBrush", out var icBrush) && icBrush is IBrush icb)
                    {
                        icon.Foreground = icb;
                    }
                    stack.Children.Add(icon);
                }

                var text = new TextBlock
                {
                    Text = tab.Title,
                    FontSize = 12,
                    FontWeight = isSubActive ? FontWeight.SemiBold : FontWeight.Normal,
                    VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
                };
                if (this.TryFindResource(isSubActive ? "CkanTextPrimaryBrush" : "CkanTextSecondaryBrush", out var txtBrush) && txtBrush is IBrush tb)
                {
                    text.Foreground = tb;
                }
                stack.Children.Add(text);

                btn.Content = stack;
                btn.Click += (s, e) =>
                {
                    HideTabPopout(immediate: true);
                    if (_shell is not null)
                    {
                        _ = _shell.NavigateCommand.ExecuteAsync(tab.Key);
                    }
                };

                NavPopoutSubTabsHost.Children.Add(btn);
            }
        }
        else
        {
            NavPopoutSubtitle.Text = "点击切换页面";
            NavPopoutSubTabsHost.Children.Clear();
            NavPopoutSubTabsHost.IsVisible = false;
        }

        NavTabPopoutCard.IsVisible = true;
        NavTabPopoutCard.IsHitTestVisible = true;
        NavTabPopoutCard.Opacity = 1;
        NavTabPopoutCard.RenderTransform = TransformOperations.Parse("translate(0px, 0px)");
    }

    private void ShowAboutPopout(Control anchor)
    {
        if (NavTabPopoutCard is null || NavPopoutCanvas is null)
        {
            return;
        }

        _popoutCloseTimer.Stop();
        _currentPopoutNavItem = null;
        _currentPopoutSender = anchor;

        var pt = anchor.TranslatePoint(new Point(0, 0), NavPopoutCanvas);
        double top = pt?.Y ?? 0;
        double maxTop = Math.Max(10, Bounds.Height - 160);
        top = Math.Clamp(top, 10, maxTop);
        Canvas.SetTop(NavTabPopoutCard, top);

        if (NavGlyphToGeometryConverter.Instance.Convert("Info", typeof(Geometry), null, CultureInfo.InvariantCulture) is Geometry geom)
        {
            NavPopoutIcon.Data = geom;
        }

        NavPopoutTitle.Text = ResolveGroupTitle("Ui_MainWindow_About");
        NavPopoutSubtitle.Text = "CKAN Mod Manager 信息与版本";
        NavPopoutActiveBadge.IsVisible = false;
        NavPopoutSubTabsHost.Children.Clear();
        NavPopoutSubTabsHost.IsVisible = false;

        NavTabPopoutCard.IsVisible = true;
        NavTabPopoutCard.IsHitTestVisible = true;
        NavTabPopoutCard.Opacity = 1;
        NavTabPopoutCard.RenderTransform = TransformOperations.Parse("translate(0px, 0px)");
    }

    private void HideTabPopout(bool immediate)
    {
        if (NavTabPopoutCard is null)
        {
            return;
        }

        _currentPopoutNavItem = null;
        _currentPopoutSender = null;

        if (immediate)
        {
            NavTabPopoutCard.Opacity = 0;
            NavTabPopoutCard.IsVisible = false;
            NavTabPopoutCard.IsHitTestVisible = false;
        }
        else
        {
            // 淡出期间卡片还在视觉树上：若继续吃指针，鼠标刚从侧栏移到页面就点一下，
            // 点击会落在一张正在消失的透明卡片上 —— 用户感知就是“悬浮动画把点击吞了”。
            NavTabPopoutCard.IsHitTestVisible = false;
            NavTabPopoutCard.Opacity = 0;
            NavTabPopoutCard.RenderTransform = TransformOperations.Parse("translate(-6px, 0px)");
            DispatcherTimer.RunOnce(() =>
            {
                if (NavTabPopoutCard.Opacity <= 0.05)
                {
                    NavTabPopoutCard.IsVisible = false;
                }
            }, TimeSpan.FromMilliseconds(150));
        }
    }

    private void OnNavPopoutHeaderClicked(object? sender, RoutedEventArgs e)
    {
        var nav = _currentPopoutNavItem;
        HideTabPopout(immediate: true);

        if (nav is not null && _shell is not null)
        {
            _ = _shell.NavigateCommand.ExecuteAsync(nav.Key);
        }
        else
        {
            OnAboutClick(sender, e);
        }
    }

    /// <summary>页面切换不做任何整页过渡：换页瞬时替换，入场动效全部交给逐卡片位移。</summary>
    /// <remarks>
    /// <para>整页过渡试了三种，都有肉眼可见的毛病（1296x809 实测内容区平均亮度）：</para>
    /// <list type="number">
    ///   <item><description><c>CrossFade</c>＋<c>PageSlide</c>：两套版式同时半透明并纵向平移，
    ///   文字互相叠成双影，长页面还像被重新滚了一遍。</description></item>
    ///   <item><description>接力式「旧页先淡出、新页再淡入」：亮度 38.3 → 28.6 → 34.7，
    ///   中间约 120ms 的凹陷看上去就是切换时闪了一下。</description></item>
    ///   <item><description>叠化（旧页不动、新页在上面淡入）：亮度稳住了，但两页文字在淡入过程中叠印，
    ///   标题与副标题都出现重影。</description></item>
    /// </list>
    /// <para>结论：这三种做法的共同前提是"两页同时在场且都不透明"，一旦叠在一起就必然出问题。</para>
    /// <para>也试过幕布过渡（<see cref="SpringPageSlide"/>：两页始终不透明、只有接缝扫过）：
    /// 单次换页干净，但**连续快速切换时打断手感丢了**——旧过渡被取消的那一帧要先归位、
    /// 新过渡再从零起步，看见的是"停一下再走"。所以又回到"瞬时替换 + 单页方向化入场"：
    /// 入场由 <see cref="PlayPageReveal"/> 用弹簧做，同一个弹簧被下一次换页打断时保位置与速度，
    /// 连点页签时的观感才是连着的。</para>
    /// <para>因此这里恒定返回 <c>null</c>；性能与观感都由入场编排负责，屏幕上也永远只有一页。</para>
    /// </remarks>
    private void ApplyPageTransition()
    {
        if (PageHost is null)
        {
            return;
        }

        PageHost.PageTransition = null;
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

    /// <summary>全局键位：Ctrl+K 开关命令面板，Ctrl+/ 打开快捷键目录，F1 打开新手高亮导览。</summary>
    private void OnGlobalKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.F1 && !e.KeyModifiers.HasFlag(KeyModifiers.Control) && !e.KeyModifiers.HasFlag(KeyModifiers.Alt))
        {
            ShowOnboardingGuide();
            e.Handled = true;
            return;
        }

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

        if (id == DefaultCommands.ActionId("open-onboarding-guide"))
        {
            ShowOnboardingGuide();
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

    public void AttachOnboardingTour(OnboardingTourViewModel onboarding)
    {
        ArgumentNullException.ThrowIfNull(onboarding);
        _onboarding = onboarding;
        OnboardingOverlay.DataContext = onboarding;
    }

    /// <summary>界面导览是否正在播放（启动序列据此避让，避免和首次启动指引抢同一块屏幕）。</summary>
    public bool IsOnboardingActive => _onboarding is { IsActive: true };

    public void ShowOnboardingGuide()
    {
        // 连按 F1 不重播：StartTour 会把步骤打回第一步，看起来像整层闪了一下。
        if (_onboarding is null || _onboarding.IsActive)
        {
            return;
        }

        _onboarding.StartTour();
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

    private void InitializePerformanceMonitor()
    {
        var monitor = PerformanceMonitor.Instance;
        monitor.AttachToTopLevel(this);
        Closed += (_, _) => monitor.Detach();

        var dynamicRate = DynamicRefreshRateController.Instance;
        dynamicRate.Attach(this);
        dynamicRate.PropertyChanged += OnDynamicRefreshRateChanged;
        Closed += (_, _) =>
        {
            dynamicRate.PropertyChanged -= OnDynamicRefreshRateChanged;
            dynamicRate.Detach();
        };

        if (PerformanceHud is not null)
        {
            PerformanceHud.DataContext = monitor;
            PerformanceHud.CloseRequested += (_, _) => TogglePerformanceMonitor(false);
        }

        monitor.PropertyChanged += OnPerformanceMonitorPropertyChanged;
        UpdateFpsStatusDisplay(monitor);

        AddHandler(KeyDownEvent, OnWindowKeyDownTunnel, RoutingStrategies.Tunnel, handledEventsToo: true);
    }

    private void OnWindowKeyDownTunnel(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.F12)
        {
            TogglePerformanceMonitor();
            e.Handled = true;
        }
    }

    private void OnTogglePerformanceMonitorClick(object? sender, RoutedEventArgs e)
    {
        TogglePerformanceMonitor();
    }

    public void TogglePerformanceMonitor(bool? forceOpen = null)
    {
        var monitor = PerformanceMonitor.Instance;
        var next = forceOpen ?? !monitor.IsOpen;
        monitor.IsOpen = next;
        if (PerformanceMonitorHost is not null)
        {
            PerformanceMonitorHost.IsVisible = next;
        }
    }

    private void OnPerformanceMonitorPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (sender is PerformanceMonitor monitor &&
            (string.IsNullOrEmpty(e.PropertyName) ||
             e.PropertyName == nameof(PerformanceMonitor.CurrentFps) ||
             e.PropertyName == nameof(PerformanceMonitor.FpsStatusTone) ||
             e.PropertyName == nameof(PerformanceMonitor.TotalFrames)))
        {
            UpdateFpsStatusDisplay(monitor);
        }
    }

    private void OnDynamicRefreshRateChanged(object? sender, PropertyChangedEventArgs e)
    {
        // 一进一出空闲降频，顶栏就要在「fps 数字」和「idle」之间切换；只靠采样事件会漏掉这次切换。
        if (string.IsNullOrEmpty(e.PropertyName) || e.PropertyName == nameof(DynamicRefreshRateController.IsIdle))
        {
            UpdateFpsStatusDisplay(PerformanceMonitor.Instance);
        }
    }

    /// <summary>空闲态标签走本地化资源；资源管道没装好时退回字面量，别把外壳绑死在资源管道上。</summary>
    private string ResolveIdleLabel()
        => this.TryFindResource(LocalizationKeys.MainWindowPerfMonitorIdle, out var resource)
           && resource is string { Length: > 0 } label
            ? label
            : "idle";

    /// <summary>
    /// 顶栏 FPS 小标记：读数随采样刷新，圆点按色调点亮（空闲时退成三级文字色）。
    /// 底栏那份读数已下线，顶栏是唯一落点；控件缺席时静默跳过，不让外壳为一块面板买单。
    /// </summary>
    private void UpdateFpsStatusDisplay(PerformanceMonitor monitor)
    {
        var fpsText = this.FindControl<TextBlock>("TopFpsStatusText");
        var fpsDot = this.FindControl<Avalonia.Controls.Shapes.Ellipse>("TopFpsStatusDot");

        if (fpsText is null && fpsDot is null)
        {
            return;
        }

        // 画面停刷（空闲降频）时报 fps 是假的：屏幕一帧没动，读数还挂着 10 之类的数字，看着像卡了。
        var text = DynamicRefreshRateController.Instance.IsIdle
            ? ResolveIdleLabel()
            : monitor.TotalFrames == 0 ? "-- fps" : $"{monitor.CurrentFps:0.0} fps";
        if (fpsText is not null)
        {
            fpsText.Text = text;
        }

        var brushKey = DynamicRefreshRateController.Instance.IsIdle
            ? "CkanTextTertiaryBrush"
            : monitor.FpsStatusTone switch
            {
                "Good" => "CkanSuccessBrush",
                "Warning" => "CkanWarningBrush",
                _ => "CkanDangerBrush"
            };

        if (fpsDot is not null && this.TryFindResource(brushKey, out var res) && res is IBrush brush)
        {
            fpsDot.Fill = brush;
        }
    }
}
