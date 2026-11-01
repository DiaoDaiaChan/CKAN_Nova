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
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.Transformation;
using Avalonia.Threading;
using Avalonia.Automation;
using Avalonia.VisualTree;
using CkanUI.App.Animations;
using CkanUI.App.Branding;
using CkanUI.App.Diagnostics;
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
    /// <summary>
    /// 侧栏分组（FluentAvalonia 官方标准做法：分组标题是菜单列表里的独立元素，不是容器）。
    /// 键必须与 <see cref="ShellViewModel"/> 里的导航键一致；插件页追加在「扩展」组末尾。
    /// </summary>
    /// <summary>
    /// 插件自述页在侧栏里是独立入口，统一归在这个标题下；业务页不再分「核心 / 工具」两组——
    /// 同一对象的多个面已经收进一个入口，再用小组标题套一层只会多两级层级。
    /// </summary>
    private const string ExtensionsHeaderKey = "Ui_NavGroup_Extensions";

    /// <summary>底部固定项：日志与设置是「随时要看」的常驻入口，不适合混在业务分组里。</summary>
    private static readonly string[] NavFooterKeys = ["logs", "settings"];

    /// <summary>分组标题的中文兜底（本地化键还没进语言包时不至于显示键名）。</summary>
    private static readonly Dictionary<string, string> NavGroupFallbacks = new(StringComparer.Ordinal)
    {
        [ExtensionsHeaderKey] = "扩展",
    };

    private static readonly ILog Log = LogManager.GetLogger(typeof(MainWindow));

    /// <summary>当前处于「追赶位移」拉伸态的导航项。</summary>
    private Control? _navPulseTarget;

    /// <summary>追赶位移的代数：只有最后一次脉冲有权摘类。</summary>
    private int _navPulseGeneration;

    /// <summary>上一次激活的页面键，用来判断同组页签是向左切还是向右切。</summary>
    private string? _lastPageKey;

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
        PointerExited += OnWindowPointerExited;
        Deactivated += OnWindowDeactivated;

        // 页面切换时给一级卡片排队入场。监听 PageHost.Content 而不是 ShellViewModel：
        // 命令面板、深链、前进后退最终都落到同一个 Content 上，一处接完全都覆盖。
        if (PageHost is not null)
        {
            PageHost.PropertyChanged += OnPageHostPropertyChanged;
        }

        // 页内 TabControl（模组详情、整合包导入导出等）切换 TabItem 时同步驱动方向性弹簧过渡。
        AddHandler(SelectingItemsControl.SelectionChangedEvent, OnAnyTabControlSelectionChanged, RoutingStrategies.Bubble);

        ApplyStatusBarFacts();
        InitializePerformanceMonitor();
    }

    /// <summary>
    /// 把光标位置转给星空背景。越界（贴着窗口边缘）就当作离开，让牵引线淡出。
    /// </summary>
    private void OnWindowPointerMoved(object? sender, PointerEventArgs e)
    {
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
        var ripple = ClickRipple;
        if (ripple is null || !ripple.IsVisible || !ripple.IsMotionEnabled)
        {
            return;
        }

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

        ripple.Spawn(position);
    }

    private void OnWindowPointerExited(object? sender, PointerEventArgs e) => BackgroundStarfield?.TrackPointer(null);

    private void OnWindowDeactivated(object? sender, EventArgs e)
    {
        BackgroundStarfield?.TrackPointer(null);

        // 失焦后计时器没必要继续跑：环已经没人看，清掉省掉一路 60fps 的重绘。
        ClickRipple?.Clear();
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

        _shell.NavItems.CollectionChanged += OnNavItemsChanged;
        _shell.PropertyChanged += OnShellPropertyChanged;

        RebuildNavigation();
        ApplyPaneState(_shell.IsNavExpanded);
        SyncSelectedNavItem();
        UpdateShellStatusDot(_shell.StatusText);
        _lastPageKey = _shell.SelectedNavItem?.Key;
        Dispatcher.UIThread.Post(SyncSectionTabIndicator, DispatcherPriority.Loaded);
    }

    private void OnPageHostPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property != ContentControl.ContentProperty || PageHost is null)
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
                TriggerTabSwitchAtmosphere(PageHost, direction);
            },
            DispatcherPriority.Send);
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
        SectionTabActivePill.RenderTransform = TransformOperations.Parse(
            FormattableString.Invariant($"translate({x:0.##}px, 0px) scaleX({pillScale:0.####})"));

        SectionTabActiveBar.Opacity = 1;
        SectionTabActiveBar.RenderTransform = TransformOperations.Parse(
            FormattableString.Invariant($"translate({barX:0.##}px, 0px) scaleX({barScale:0.####})"));
    }

    private void TriggerTabSwitchAtmosphere(Control? contentTarget, int direction)
    {
        if (Application.Current?.Resources.TryGetResource(Theming.MotionHost.SpringSoftKey, null, out var durationObj) == true &&
            durationObj is TimeSpan ts && ts <= TimeSpan.Zero)
        {
            return;
        }

        if (contentTarget is not null)
        {
            contentTarget.Classes.Remove("is-revealing");
            contentTarget.Classes.Add("is-revealing");
        }

        if (TabSwitchHorizonBeam is not null)
        {
            TabSwitchHorizonBeam.RenderTransformOrigin = direction < 0
                ? new RelativePoint(1, 0.5, RelativeUnit.Relative)
                : new RelativePoint(0, 0.5, RelativeUnit.Relative);
            TabSwitchHorizonBeam.Classes.Remove("is-sweeping");
            TabSwitchHorizonBeam.Classes.Add("is-sweeping");
        }

        if (TabSwitchAuroraWash is not null)
        {
            TabSwitchAuroraWash.Classes.Remove("is-sweeping");
            TabSwitchAuroraWash.Classes.Add("is-sweeping");
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
        if (e.Source is not TabControl tabControl || e.RemovedItems.Count == 0 || e.AddedItems.Count == 0)
        {
            return;
        }

        var oldIndex = tabControl.Items.IndexOf(e.RemovedItems[0]);
        var newIndex = tabControl.Items.IndexOf(e.AddedItems[0]);
        if (oldIndex < 0 || newIndex < 0 || oldIndex == newIndex)
        {
            return;
        }

        var contentHost = tabControl
            .GetVisualDescendants()
            .OfType<ContentPresenter>()
            .FirstOrDefault(p => p.Name == "PART_SelectedContentHost");

        if (contentHost is not null)
        {
            contentHost.Classes.Remove("is-revealing");
            contentHost.Classes.Add("is-revealing");
        }
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
        if (e.PropertyName == nameof(ShellViewModel.SelectedNavItem))
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
    /// <para>结论：页面本体不做透明度动画。位移只由 <c>stagger-card</c> 承担，而且预备态不再压透明度
    /// （否则卡片自己又让页面闪一次）。<c>CkanMotionDurationPage</c> 仍供对话框淡入使用。</para>
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

    private void InitializePerformanceMonitor()
    {
        var monitor = PerformanceMonitor.Instance;
        monitor.AttachToTopLevel(this);
        Closed += (_, _) => monitor.Detach();

        var dynamicRate = DynamicRefreshRateController.Instance;
        dynamicRate.Attach(this);
        Closed += (_, _) => dynamicRate.Detach();

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

    private void UpdateFpsStatusDisplay(PerformanceMonitor monitor)
    {
        if (FpsStatusText is not null)
        {
            FpsStatusText.Text = monitor.TotalFrames == 0 ? "-- fps" : $"{monitor.CurrentFps:0.0} fps";
        }

        if (FpsStatusDot is not null)
        {
            var brushKey = monitor.FpsStatusTone switch
            {
                "Good" => "CkanSuccessBrush",
                "Warning" => "CkanWarningBrush",
                _ => "CkanDangerBrush"
            };

            if (this.TryFindResource(brushKey, out var res) && res is IBrush brush)
            {
                FpsStatusDot.Fill = brush;
            }
        }
    }
}
