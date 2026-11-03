using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.VisualTree;
using CkanUI.App.Diagnostics;
using CkanUI.App.Tests.Automation;
using CkanUI.ViewModels;
using Avalonia.Threading;
using NUnit.Framework;
using System.Linq;
using System.Threading;

namespace CkanUI.App.Tests;

/// <summary>
/// 顶栏整洁布局测试：侧栏开关、导航前进后退、标题文本、日志/FPS 小标记、窗口自绘控制按钮各居其位。
/// 严格断言控制按钮位于最右侧独立列，小标记独占标题与按钮之间的一列——两者都不可重叠在标题文本（Column 3）之上。
/// </summary>
[TestFixture]
[NonParallelizable]
public sealed class TitleBarLayoutTests
{
    [SetUp]
    public void SetUp() => HeadlessAppHost.EnsureInitialized();

    [Test]
    public void CaptionButtons_ArePlacedInFarRightColumn_NotOverlappingTitle()
    {
        var vm = new ShellViewModel();
        var window = new MainWindow { DataContext = vm };
        window.Show();
        try
        {
            HeadlessAppHost.PumpJobs();

            var titleText = window.FindControl<TextBlock>("WindowTitleTextBlock");
            Assert.That(titleText, Is.Not.Null, "标题文本控件必须存在");
            Assert.That(Grid.GetColumn(titleText!), Is.EqualTo(3), "标题文本应位于第 3 列 (*)");

            var minimizeBtn = window.GetVisualDescendants()
                .OfType<Button>()
                .FirstOrDefault(b => b.Classes.Contains("CkanCaption") && b.Classes.Contains("Close") == false);
            Assert.That(minimizeBtn, Is.Not.Null, "窗口控制按钮必须存在");

            var captionPanel = minimizeBtn!.Parent as StackPanel;
            Assert.That(captionPanel, Is.Not.Null, "控制按钮容器必须是 StackPanel");
            Assert.That(Grid.GetColumn(captionPanel!), Is.EqualTo(6), "窗口控制按钮面板必须位于第 6 列 (最右侧 Auto)");
            Assert.That(captionPanel!.HorizontalAlignment, Is.EqualTo(HorizontalAlignment.Right), "控制按钮必须靠右对齐");

            var quickStatus = window.FindControl<StackPanel>("TopQuickStatusHost");
            Assert.That(quickStatus, Is.Not.Null, "顶栏必须有日志/FPS 小标记容器");
            Assert.That(Grid.GetColumn(quickStatus!), Is.EqualTo(4), "小标记必须独占标题与窗口按钮之间的一列");

            // 常驻启动按钮：贴在窗口按钮左侧的固定强调色主按钮，位置不随导航漂移。
            var launchButton = window.FindControl<Button>("TopLaunchGameButton");
            Assert.That(launchButton, Is.Not.Null, "顶栏必须有常驻「启动游戏」按钮");
            Assert.That(Grid.GetColumn(launchButton!), Is.EqualTo(5), "启动按钮必须独占小标记与窗口按钮之间的一列");
            Assert.That(launchButton!.Classes, Does.Contain("CkanPrimary"), "启动按钮是 shell 唯一的主动作，必须是强调色主按钮");
            Assert.That(launchButton.Command, Is.SameAs(vm.LaunchGameCommand), "启动按钮必须绑到外壳的启动命令，而不是自己 new 一个");
            Assert.That(VisualOrderIndex(launchButton), Is.LessThan(VisualOrderIndex(captionPanel)), "启动按钮必须在窗口按钮左侧（视觉顺序）");
        }
        finally
        {
            window.Close();
            HeadlessAppHost.PumpJobs();
        }
    }


    [Test]
    public void PageZoomSlider_LivesInTheStatusBar_AndDrivesTheShell()
    {
        var vm = new ShellViewModel();
        var window = new MainWindow { DataContext = vm };
        window.Show();
        try
        {
            HeadlessAppHost.PumpJobs();

            var slider = window.FindControl<Slider>("PageZoomSlider");
            Assert.That(slider, Is.Not.Null, "底栏必须有页面缩放滑块");
            Assert.That(slider!.Minimum, Is.EqualTo(75d));
            Assert.That(slider.Maximum, Is.EqualTo(150d));
            Assert.That(slider.TickFrequency, Is.EqualTo(5d));
            Assert.That(slider.IsSnapToTickEnabled, Is.True, "缩放按 5% 一档，滑块不该停在 103.7% 这种值上");

            var cluster = window.FindControl<StackPanel>("PageZoomCluster");
            Assert.That(cluster, Is.Not.Null, "缩放控件必须聚成一组，快捷键/读屏才有落点");
            Assert.That(Grid.GetColumn(cluster!), Is.EqualTo(1), "缩放控件放底栏中间列，不挤状态文案与任务胶囊");

            var barGrid = cluster!.Parent as Grid;
            Assert.That(barGrid, Is.Not.Null);
            Assert.That(
                barGrid!.Children.OfType<StackPanel>().Any(panel => panel.Children.OfType<Border>().Any(border => border.Name == "StatusBarTaskPillBorder")),
                Is.True,
                "缩放控件必须和状态文案/任务胶囊同处 28px 底栏那一行");

            slider.Value = 125d;
            HeadlessAppHost.PumpJobs();
            Assert.That(vm.PageZoom, Is.EqualTo(1.25d).Within(1e-9), "滑块必须双向绑到外壳比例");
            Assert.That(window.FindControl<TextBlock>("PageZoomValueText")!.Text, Is.EqualTo("125%"));

            vm.PageZoom = 0.8d;
            HeadlessAppHost.PumpJobs();
            Assert.That(slider.Value, Is.EqualTo(80d).Within(1e-6), "滚轮/快捷键改的比例必须回写到滑块，否则两个入口会各说各话");

            Assert.That(window.KeyBindings.Any(binding => ReferenceEquals(binding.Command, vm.ResetPageZoomCommand)), Is.True, "Ctrl+0 必须能复位页面缩放");
        }
        finally
        {
            window.Close();
            HeadlessAppHost.PumpJobs();
        }
    }

    [Test]
    public void PageZoom_ScalesThePageHostThroughALayoutTransform()
    {
        var vm = new ShellViewModel();
        var window = new MainWindow { DataContext = vm };
        window.Show();
        try
        {
            HeadlessAppHost.PumpJobs();

            var host = window.FindControl<LayoutTransformControl>("PageZoomHost");
            Assert.That(host, Is.Not.Null, "页面承载区必须被 LayoutTransformControl 包住，缩放才会重排而不是位图拉伸");
            Assert.That(
                host!.LayoutTransform,
                Is.Null,
                "100% 时不能挂恒等变换：那会把整页变成合成层，快速滚动时合成层来不及失效会留下残影行");

            var hostWidth = host.Bounds.Width;
            var pageHost = window.FindControl<Control>("PageHost");
            Assert.That(pageHost, Is.Not.Null);
            var pageWidthAt100 = pageHost!.Bounds.Width;
            Assert.That(pageWidthAt100, Is.EqualTo(hostWidth).Within(1d), "100% 时页面宿主应当铺满承载区");

            vm.PageZoom = 1.25d;

            // 补间走 RequestAnimationFrame：无头平台得主动推合成帧，否则回调永远不来，弹簧停在原地。
            // 帧时间戳取的是真实耗时，不 sleep 的话 120 帧只走 0.02 秒，弹簧会停在 102% 装死。
            for (var i = 0; i < 50; i++)
            {
                AvaloniaHeadlessPlatform.ForceRenderTimerTick();
                Dispatcher.UIThread.RunJobs();
                Thread.Sleep(10);
            }

            HeadlessAppHost.PumpJobs();

            var scale = (ScaleTransform)host.LayoutTransform!;
            Assert.That(scale.ScaleX, Is.EqualTo(1.25d).Within(0.002d), "补间结束必须精确落在目标比例上（静止阈值不能停在半路）");
            Assert.That(scale.ScaleY, Is.EqualTo(1.25d).Within(0.002d));

            // 真缩放判据：承载区宽度不变，页面按 1/scale 重新测量并重排——只改 RenderTransform 的话这里宽度不会变。
            Assert.That(host.Bounds.Width, Is.EqualTo(hostWidth).Within(1d), "外壳尺寸不该跟着页面缩放变化");
            Assert.That(
                pageHost.Bounds.Width,
                Is.EqualTo(pageWidthAt100 / 1.25d).Within(2d),
                "页面内容要按「视口 / scale」重新测量（重排），不是把 100% 的位图拉大");

            // 回到 100%：变换必须被摘掉，页面回到无合成层的直绘状态。
            vm.PageZoom = 1d;
            for (var i = 0; i < 50; i++)
            {
                AvaloniaHeadlessPlatform.ForceRenderTimerTick();
                Dispatcher.UIThread.RunJobs();
                Thread.Sleep(10);
            }

            Assert.That(host.LayoutTransform, Is.Null, "回到 100% 后必须摘掉变换，否则残影源还在");
            Assert.That(pageHost.Bounds.Width, Is.EqualTo(pageWidthAt100).Within(2d), "100% 时页面宿主应当重新铺满承载区");
        }
        finally
        {
            window.Close();
            HeadlessAppHost.PumpJobs();
        }
    }

    [Test]
    public void PageZoom_MatrixLandsOnTheSurfacePanel_SoTextAndIconsActuallyGrow()
    {
        var vm = new ShellViewModel();
        var window = new MainWindow { DataContext = vm };
        window.Show();
        try
        {
            HeadlessAppHost.PumpJobs();

            var surface = window.FindControl<Panel>("PageZoomSurface");
            Assert.That(
                surface,
                Is.Not.Null,
                "缩放矩阵得落在专属面板上：直接压在 PageHost 上会被页面动效（TransformSpring）顶成 identity");
            var pageHost = window.FindControl<Control>("PageHost")!;
            var host = window.FindControl<LayoutTransformControl>("PageZoomHost")!;

            vm.PageZoom = 1.5d;
            for (var i = 0; i < 50; i++)
            {
                AvaloniaHeadlessPlatform.ForceRenderTimerTick();
                Dispatcher.UIThread.RunJobs();
                Thread.Sleep(10);
            }

            HeadlessAppHost.PumpJobs();

            // 唯一能区分「真缩放」与「版面变窄、字还是原样」的判据：放大矩阵在谁身上，谁才真的被画大。
            var matrix = (surface!.RenderTransform as MatrixTransform)?.Matrix ?? Matrix.Identity;
            Assert.That(
                matrix.M11,
                Is.EqualTo(1.5d).Within(0.002d),
                "缩放矩阵没落到缩放面：页面会变成「卡片变窄、文字原尺寸」——正是用户报的那条 bug");
            Assert.That(matrix.M22, Is.EqualTo(1.5d).Within(0.002d));

            // 缩放面先按「视口 / 1.5」重新测量（1227 → 818），页面宿主铺满它，再由矩阵放大 1.5 倍画回去：
            // 「重排 + 矩阵」叠起来才是视觉上的 1.5 倍，这一步保证不是把 100% 的位图拉大。
            Assert.That(pageHost.Bounds.Width, Is.EqualTo(surface.Bounds.Width).Within(2d));
            Assert.That(surface.Bounds.Width, Is.EqualTo(host.Bounds.Width / 1.5d).Within(2d));
        }
        finally
        {
            window.Close();
            HeadlessAppHost.PumpJobs();
        }
    }

    [Test]
    public void PageZoom_DraggingUpFromIdentity_KeepsScalingInsteadOfSilentlyDroppingTheTransform()
    {
        var vm = new ShellViewModel();
        var window = new MainWindow { DataContext = vm };
        window.Show();
        try
        {
            HeadlessAppHost.PumpJobs();
            var host = window.FindControl<LayoutTransformControl>("PageZoomHost")!;
            Assert.That(host.LayoutTransform, Is.Null);

            // 滑块一档 5%：逐档往上拖，每档之间把帧推到位——用户真实轨迹。
            for (var step = 1; step <= 10; step++)
            {
                vm.PageZoom = 1d + (step * ShellViewModel.PageZoomStep);
                for (var i = 0; i < 6; i++)
                {
                    AvaloniaHeadlessPlatform.ForceRenderTimerTick();
                    Dispatcher.UIThread.RunJobs();
                    Thread.Sleep(10);
                }

                Assert.That(
                    host.LayoutTransform,
                    Is.Not.Null,
                    $"拖到 {vm.PageZoomPercentText} 时变换被提前摘掉了：滑块读数会涨、页面却停在 100%");
            }

            for (var i = 0; i < 50; i++)
            {
                AvaloniaHeadlessPlatform.ForceRenderTimerTick();
                Dispatcher.UIThread.RunJobs();
                Thread.Sleep(10);
            }

            var scale = (ScaleTransform)host.LayoutTransform!;
            Assert.That(scale.ScaleX, Is.EqualTo(1.5d).Within(0.002d), "拖到 150% 必须真的放大到 1.5 倍");
        }
        finally
        {
            window.Close();
            HeadlessAppHost.PumpJobs();
        }
    }

    /// <summary>同一父容器下的兄弟顺序下标：用来断言「启动按钮在窗口按钮左边」而不依赖像素坐标。</summary>
    private static int VisualOrderIndex(Control control)
    {
        var parent = control.Parent as Panel;
        Assert.That(parent, Is.Not.Null, "控件必须挂在顶栏的 Grid 里");
        return parent!.Children.IndexOf(control);
    }

    [Test]
    public void FpsMarker_KeepsItsWidthWhenTheReadingChangesDigits()
    {
        var window = new MainWindow { DataContext = new ShellViewModel() };
        window.Show();
        try
        {
            HeadlessAppHost.PumpJobs();

            var text = window.FindControl<TextBlock>("TopFpsStatusText");
            Assert.That(text, Is.Not.Null, "FPS 标记必须有读数文本");

            // 9.1 → 261.1 位数一多，胶囊若跟着变宽，顶栏右侧整簇（日志标记、标题可用宽度）都会左右抖。
            static double WidthOf(TextBlock block)
            {
                block.Measure(Size.Infinity);
                return block.DesiredSize.Width;
            }

            double? baseline = null;
            foreach (var reading in new[] { "-- fps", "idle", "9.1 fps", "26.1 fps", "126.6 fps", "261.1 fps", "360.0 fps" })
            {
                text!.Text = reading;
                var width = WidthOf(text);
                baseline ??= width;
                Assert.That(width, Is.EqualTo(baseline), $"读数 {reading} 不该撑宽胶囊，否则顶栏会抖");
            }

            // 等宽数字（tnum）兜住同一长度内单个数字宽度的差异，个位数跳变也不抖。
            Assert.That(text!.FontFeatures, Is.Not.Null, "读数必须启用等宽数字特性");
        }
        finally
        {
            window.Close();
            HeadlessAppHost.PumpJobs();
        }
    }

    [Test]
    public void FpsMarker_ShowsIdleInsteadOfAReadingWhileTheScreenIsNotRefreshing()
    {
        var window = new MainWindow { DataContext = new ShellViewModel() };
        window.Show();
        try
        {
            HeadlessAppHost.PumpJobs();

            var text = window.FindControl<TextBlock>("TopFpsStatusText");
            Assert.That(text, Is.Not.Null, "FPS 标记必须有读数文本");

            DynamicRefreshRateController.Instance.NotifyUserActivity();
            PerformanceMonitor.Instance.RefreshSnapshot();
            HeadlessAppHost.PumpJobs();
            Assert.That(text!.Text, Is.Not.EqualTo("idle"), "活跃态要报 fps，不能挂 idle");

            // 画面停刷（空闲降频）时报一个 fps 数字是假的读数：屏幕一帧没动，数字却还在，看着像卡死了。
            DynamicRefreshRateController.Instance.ForceIdle();
            PerformanceMonitor.Instance.RefreshSnapshot();
            HeadlessAppHost.PumpJobs();
            Assert.That(text.Text, Is.EqualTo("idle"), "空闲降频时必须显示 idle 表明当前状态");
        }
        finally
        {
            DynamicRefreshRateController.Instance.NotifyUserActivity();
            window.Close();
            HeadlessAppHost.PumpJobs();
        }
    }

    /// <summary>
    /// 状态栏缩放控件必须共用一条水平中线：−/滑块/+/读数任何一方掉队，看起来就像控件碎了一地。
    /// 同时 thumb 必须完整落在 28px 状态栏内——FluentAvalonia 的 Slider 模板默认 32px 高（上下各留 14px 刻度行），
    /// 直接压进状态栏会把轨道挤到底、thumb 被窗口底边裁掉，所以这里把它当回归判据钉死。
    /// </summary>
    [Test]
    public void PageZoomCluster_SharesOneCentreLine_AndKeepsTheThumbInsideTheStatusBar()
    {
        var window = new MainWindow { DataContext = new ShellViewModel() };
        window.Show();
        try
        {
            HeadlessAppHost.PumpJobs();

            var bar = window.FindControl<Border>("StatusBarSurface");
            var minus = window.FindControl<Button>("PageZoomOutButton");
            var slider = window.FindControl<Slider>("PageZoomSlider");
            var plus = window.FindControl<Button>("PageZoomInButton");
            var value = window.FindControl<TextBlock>("PageZoomValueText");
            Assert.That(bar, Is.Not.Null, "状态栏容器必须可定位");
            Assert.That(minus, Is.Not.Null, "缩小按钮必须存在");
            Assert.That(slider, Is.Not.Null, "缩放滑块必须存在");
            Assert.That(plus, Is.Not.Null, "放大按钮必须存在");
            Assert.That(value, Is.Not.Null, "缩放读数必须存在");

            static double CentreY(Visual visual, Visual root) =>
                visual.TranslatePoint(new Point(0, visual.Bounds.Height / 2), root)!.Value.Y;
            static double LeftX(Visual visual, Visual root) =>
                visual.TranslatePoint(new Point(0, 0), root)!.Value.X;
            static double RightX(Visual visual, Visual root) =>
                visual.TranslatePoint(new Point(visual.Bounds.Width, 0), root)!.Value.X;

            var sliderCentre = CentreY(slider!, window);
            foreach (var (name, control) in new (string, Control)[]
                     {
                         ("缩小按钮", minus!), ("放大按钮", plus!), ("缩放读数", value!)
                     })
            {
                Assert.That(
                    CentreY(control, window),
                    Is.EqualTo(sliderCentre).Within(1d),
                    $"{name}必须与滑轨同一条水平中线，否则这条控件看着就是歪的");
            }

            // thumb 的几何体被模板包在 Track 里，只能从视觉树取；它完整落在状态栏内才算没被裁。
            var thumb = slider!.GetVisualDescendants().OfType<Thumb>().Single();
            Assert.That(thumb.Bounds.Height, Is.LessThanOrEqualTo(14d), "thumb 必须收小，否则 28px 状态栏装不下");

            var barTop = bar!.TranslatePoint(new Point(0, 0), window)!.Value.Y;
            var barBottom = barTop + bar!.Bounds.Height;
            var thumbTop = thumb.TranslatePoint(new Point(0, 0), window)!.Value.Y;
            Assert.That(thumbTop, Is.GreaterThanOrEqualTo(barTop - 0.5d), "thumb 顶部不能被状态栏裁掉");
            Assert.That(
                thumbTop + thumb.Bounds.Height,
                Is.LessThanOrEqualTo(barBottom + 0.5d),
                "thumb 底部不能被窗口底边裁掉——旧模板 18px thumb 就是这么缺一角的");
            Assert.That(
                CentreY(thumb, window),
                Is.EqualTo(barTop + bar!.Bounds.Height / 2d).Within(1.5d),
                "thumb 必须落在状态栏竖直中线上");

            // 左右留白对称：− 到滑轨的间距要和滑轨到 + 的一致，否则控件组看着左重右轻。
            var gapBeforeSlider = LeftX(slider!, window) - RightX(minus!, window);
            var gapAfterSlider = LeftX(plus!, window) - RightX(slider!, window);
            Assert.That(gapBeforeSlider, Is.EqualTo(gapAfterSlider).Within(0.5d), "滑轨两侧间距必须对称");

            Assert.That(slider!.Bounds.Height, Is.EqualTo(12d).Within(0.5d), "状态栏滑块要保持 12px 紧凑高度");
        }
        finally
        {
            window.Close();
            HeadlessAppHost.PumpJobs();
        }
    }

    /// <summary>
    /// 拖动滑块必须整段拖动只落盘一次：一格一次 Save 会把 UI 线程按在 fsync 上（每次 3–8ms），
    /// 写失败时还会一格一条「保存 UI 设置失败」把日志抽屉刷满——用户看到的就是「滑块调节有 bug」。
    /// </summary>
    [Test]
    public void PageZoomSlider_DraggingWritesSettingsOnceOnRelease()
    {
        var store = new RecordingSettingsStore();
        var shell = new ShellViewModel(
            new CkanUI.ViewModels.Navigation.NavigationService(_ => null),
            settingsStore: store);
        var window = new MainWindow { DataContext = shell, Width = 1280, Height = 800 };
        window.Show();
        try
        {
            HeadlessAppHost.PumpJobs();

            var slider = window.FindControl<Slider>("PageZoomSlider")!;
            var origin = slider.TranslatePoint(new Point(0, 0), window)!.Value;
            var y = origin.Y + (slider.Bounds.Height / 2d);
            var x = origin.X + (slider.Bounds.Width / 2d);

            window.MouseDown(new Point(x, y), MouseButton.Left);
            for (var step = 1; step <= 6; step++)
            {
                window.MouseMove(new Point(x + (step * 4), y));
                Dispatcher.UIThread.RunJobs();
            }

            Assert.That(store.Saved, Is.Empty, "拖动过程中不该写盘");
            window.MouseUp(new Point(x + 24, y), MouseButton.Left);
            Dispatcher.UIThread.RunJobs();

            Assert.That(shell.PageZoom, Is.GreaterThan(1d), "拖动应当改变缩放比例");
            Assert.That(store.Saved, Has.Count.EqualTo(1), "整段拖动只落盘一次");
            Assert.That(store.Saved[0].PageZoom, Is.EqualTo(shell.PageZoom).Within(1e-9));
        }
        finally
        {
            window.Close();
            HeadlessAppHost.PumpJobs();
        }
    }

    private sealed class RecordingSettingsStore : CkanUI.Services.Abstractions.IUiSettingsStore
    {
        public List<CkanUI.Services.Abstractions.UiSettings> Saved { get; } = [];

        public CkanUI.Services.Abstractions.UiSettings Current { get; set; } = new();

        public CkanUI.Services.Abstractions.UiSettings Load() => Current;

        public event Action<CkanUI.Services.Abstractions.UiSettings>? Changed
        {
            add { }
            remove { }
        }

        public CkanUI.Services.Abstractions.OperationResult<bool> Save(
            CkanUI.Services.Abstractions.UiSettings settings)
        {
            Current = settings;
            Saved.Add(settings);
            return CkanUI.Services.Abstractions.OperationResult<bool>.Ok(true);
        }
    }
}
