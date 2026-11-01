using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
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
            Assert.That(host!.LayoutTransform, Is.InstanceOf<ScaleTransform>());

            var hostWidth = host.Bounds.Width;
            var pageHost = window.FindControl<Control>("PageHost");
            Assert.That(pageHost, Is.Not.Null);
            var pageWidthAt100 = pageHost!.Bounds.Width;
            Assert.That(pageWidthAt100, Is.EqualTo(hostWidth).Within(1d), "100% 时页面宿主应当铺满承载区");

            var scale = (ScaleTransform)host.LayoutTransform!;
            Assert.That(scale.ScaleX, Is.EqualTo(1d).Within(1e-6), "接管外壳时直接落位，不该看见整页从别的比例弹过来");

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

            Assert.That(scale.ScaleX, Is.EqualTo(1.25d).Within(0.002d), "补间结束必须精确落在目标比例上（静止阈值不能停在半路）");
            Assert.That(scale.ScaleY, Is.EqualTo(1.25d).Within(0.002d));

            // 真缩放判据：承载区宽度不变，页面按 1/scale 重新测量并重排——只改 RenderTransform 的话这里宽度不会变。
            Assert.That(host.Bounds.Width, Is.EqualTo(hostWidth).Within(1d), "外壳尺寸不该跟着页面缩放变化");
            Assert.That(
                pageHost.Bounds.Width,
                Is.EqualTo(pageWidthAt100 / 1.25d).Within(2d),
                "页面内容要按「视口 / scale」重新测量（重排），不是把 100% 的位图拉大");
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
}
