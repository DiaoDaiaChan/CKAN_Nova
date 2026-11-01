using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using CkanUI.ViewModels.Pages;
using CkanUI.ViewModels.Pages.Instances;

namespace CkanUI.App.Views;

/// <summary>
/// 主菜单页视图（F-01 落地页）：只做宿主能力接线，业务全在 <see cref="HomePageViewModel"/>。
/// </summary>
/// <remarks>
/// 起进程、打开文件管理器、读写实例内的 <c>GUIConfig.json</c> 都要碰平台 API，按 04 篇 §1 只能留在 App 层；
/// 用 <c>??=</c> 是为了让宿主（测试夹具 / 将来的 DI 注册）已经塞好的实现优先。
/// 截图胶片带的自动滚动也是纯视图效果（<see cref="ScrollViewer.Offset"/> 推进），因此留在这一层。
/// </remarks>
public partial class HomePageView : UserControl
{
    /// <summary>推进间隔：约 60 fps，和其余自绘动画层同一量级。</summary>
    private const double RailFrameMilliseconds = 16;

    /// <summary>每帧推进的像素数：60 fps 下约 50 像素/秒，卡片读得清但不至于让人等。</summary>
    private const double RailStepPixels = 0.8;

    /// <summary>滚到末尾后停留的帧数（约 1.2 秒），再回卷到开头，避免看起来像卡住。</summary>
    private const int RailHoldFrames = 75;

    private DispatcherTimer? _railTimer;
    private int _railHoldFrames;
    private bool _railPaused;

    private HomePageViewModel? _viewModel;

    // 失焦后停表：没人看的胶片带不必每秒推 60 次，回到前台立刻续上（画面在被观看时完全不变）。
    private Window? _railWindow;

    /// <summary>构造视图并接线（ViewLocator 无参构造，因此这里不能有参数）。</summary>
    public HomePageView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    /// <inheritdoc />
    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        ScreenshotRail.PointerEntered += OnRailPointerEntered;
        ScreenshotRail.PointerExited += OnRailPointerExited;
        _railTimer ??= new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(RailFrameMilliseconds) };
        _railTimer.Tick -= OnRailTick;
        _railTimer.Tick += OnRailTick;
        AttachRailWindow();
        UpdateRailTimer();
    }

    /// <inheritdoc />
    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        ScreenshotRail.PointerEntered -= OnRailPointerEntered;
        ScreenshotRail.PointerExited -= OnRailPointerExited;
        if (_railTimer is not null)
        {
            _railTimer.Stop();
            _railTimer.Tick -= OnRailTick;
        }

        if (_railWindow is not null)
        {
            _railWindow.PropertyChanged -= OnRailWindowPropertyChanged;
            _railWindow = null;
        }

        base.OnDetachedFromVisualTree(e);
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        _viewModel = DataContext as HomePageViewModel;
        if (_viewModel is null)
        {
            return;
        }

        _viewModel.ShellLauncher ??= new ShellInstanceLauncher();
        _viewModel.CommandStore ??= new LaunchCommandFile();
    }

    private void OnRailPointerEntered(object? sender, Avalonia.Input.PointerEventArgs e) => _railPaused = true;

    private void OnRailPointerExited(object? sender, Avalonia.Input.PointerEventArgs e) => _railPaused = false;

    private void AttachRailWindow()
    {
        _railWindow = TopLevel.GetTopLevel(this) as Window;
        if (_railWindow is null)
        {
            return;
        }

        _railWindow.PropertyChanged -= OnRailWindowPropertyChanged;
        _railWindow.PropertyChanged += OnRailWindowPropertyChanged;
    }

    private void OnRailWindowPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property == Window.IsActiveProperty)
        {
            UpdateRailTimer();
        }
    }

    /// <summary>只有「挂在可视树上 + 可见 + 窗口是前台」时才该推进胶片带。</summary>
    /// <remarks>抽成纯函数是为了能在无头测试里断言这条不变量，不必起真窗口（与 PerformanceMonitor.ShouldRequestFrame 同一手法）。</remarks>
    public static bool ShouldRunRail(bool attached, bool isVisible, bool? windowIsActive)
        => attached && isVisible && windowIsActive != false;

    /// <summary>按 <see cref="ShouldRunRail"/> 起停定时器。</summary>
    private void UpdateRailTimer()
    {
        if (_railTimer is null)
        {
            return;
        }

        var canRun = ShouldRunRail(VisualRoot is not null, IsEffectivelyVisible, _railWindow?.IsActive);
        if (canRun)
        {
            if (!_railTimer.IsEnabled)
            {
                _railTimer.Start();
            }

            return;
        }

        _railTimer.Stop();
    }

    /// <summary>推进胶片带：到末尾先停 <see cref="RailHoldFrames"/> 帧再回卷，卡片不足一屏时不动。</summary>
    private void OnRailTick(object? sender, EventArgs e)
    {
        if (_railPaused)
        {
            return;
        }

        // 兜底：窗口刚失焦、属性通知还没到时不必再推。
        if (_railWindow is { IsActive: false })
        {
            UpdateRailTimer();
            return;
        }

        var rail = ScreenshotRail;
        var maxOffset = rail.Extent.Width - rail.Viewport.Width;
        if (maxOffset <= 1)
        {
            _railHoldFrames = 0;
            return;
        }

        if (rail.Offset.X >= maxOffset - 0.5)
        {
            // 已经触底：先停留，再回到开头
            if (_railHoldFrames++ < RailHoldFrames)
            {
                return;
            }

            _railHoldFrames = 0;
            rail.Offset = new Vector(0, rail.Offset.Y);
            return;
        }

        rail.Offset = new Vector(System.Math.Min(rail.Offset.X + RailStepPixels, maxOffset), rail.Offset.Y);
    }
}
