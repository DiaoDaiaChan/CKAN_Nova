using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Rendering.Composition;
using Avalonia.VisualTree;
using CkanUI.App.Tests.Automation;
using CkanUI.ViewModels;
using FluentAvalonia.UI.Controls;
using NUnit.Framework;

namespace CkanUI.App.Tests;

/// <summary>
/// 锁定左侧导航「小蓝条」在切页时不许消失，以及它为什么曾经消失。
/// 背景：FluentAvalonia 的 FANavigationView.PlayIndicatorAnimations 用
/// <c>indicator.Bounds.Height</c> 当缩放基准算 <c>Math.Abs(to - from) / dimension</c>；
/// 一旦指示条被 IsVisible=False 折叠成 0x0，切换瞬间就会算出 Inf/NaN 的 Scale.Y，
/// 新指示条整段 600ms 渲染不出来 —— 看起来就是「切页的时候小蓝条消失一阵」。
/// 所以未选中项只能用「不影响布局」的方式隐身（这里用 OpacityMask），尺寸必须常驻。
/// </summary>
[TestFixture]
[NonParallelizable]
public sealed class NavSelectionIndicatorGeometryTests
{
    [Test]
    public void UnselectedItems_KeepIndicatorLaidOut()
    {
        var (window, nav) = CreateShell();
        try
        {
            var items = Items(nav);
            Assert.That(items.Count, Is.GreaterThan(1), "导航项数量不足以验证指示条");

            foreach (var item in items)
            {
                var indicator = Indicator(item);
                Assert.That(indicator, Is.Not.Null, $"[{item.Content}] 找不到 Border#SelectionIndicator");
                Assert.That(indicator!.IsVisible, Is.True,
                    $"[{item.Content}] 指示条被 IsVisible 折叠了——FA 会拿 0 当缩放除数");
                Assert.That(indicator.Bounds.Height, Is.GreaterThan(0d),
                    $"[{item.Content}] 指示条没有参与布局，高度为 0");
            }
        }
        finally
        {
            window.Close();
            HeadlessAppHost.PumpJobs();
        }
    }

    [Test]
    public void Switching_NeverBlanksTheIndicator()
    {
        var (window, nav) = CreateShell();
        try
        {
            var items = Items(nav);
            Assert.That(items.Count, Is.GreaterThan(4));

            // 先把两条指示条的落位量出来，切换时只在它们之间的竖带里找蓝像素。
            nav.SelectedItem = items[1];
            Pump(20);
            var firstRect = IndicatorRect(items[1], window);
            nav.SelectedItem = items[4];
            Pump(20);
            var secondRect = IndicatorRect(items[4], window);
            Assert.That(firstRect.Height, Is.GreaterThan(0d));
            Assert.That(secondRect.Height, Is.GreaterThan(0d));

            var strip = firstRect.Union(secondRect).Inflate(new Thickness(4d, 2d));

            // 真正的问题现场：切换那一刻起，FA 的 600ms 动画期间指示条还画得出来吗。
            // 判定不靠抓帧计时（抓帧本身要几十毫秒，会把被测窗口撑到失真），
            // 而是直接读两个决定性数值：
            //   1) 指示条不能是 0x0 —— FA 用 Bounds.Height 当缩放除数；
            //   2) 合成层 Scale 必须是有限值 —— 除零会算出 Inf/NaN，整段动画画不出来。
            nav.SelectedItem = items[1];
            for (var i = 0; i <= 30; i++)
            {
                foreach (var item in new[] { items[1], items[4] })
                {
                    var indicator = Indicator(item);
                    Assert.That(indicator, Is.Not.Null);
                    Assert.That(indicator!.IsVisible, Is.True,
                        $"[{item.Content}] 切换后第 {i} 帧指示条被折叠了");
                    Assert.That(indicator.Bounds.Height, Is.GreaterThan(0d),
                        $"[{item.Content}] 切换后第 {i} 帧指示条高度为 0——FA 会拿它当缩放除数");
                    AssertFiniteScale(indicator, $"[{item.Content}] 切换后第 {i} 帧");
                }

                Pump(1);
            }

            // 像素兜底：动画跑完之后，那条蓝杠必须真的画在导航栏上。
            Pump(20);
            Assert.That(CountBlueIn(window, strip), Is.GreaterThan(0),
                "切换动画结束后两条指示条所在的竖带里一个蓝像素都没有——蓝条没画出来");
        }
        finally
        {
            window.Close();
            HeadlessAppHost.PumpJobs();
        }
    }

    [Test]
    public void SharedStyles_HideIndicatorWithoutCollapsingIt()
    {
        var path = System.IO.Path.Combine(ResolveRepositoryRoot(), "src", "CkanUI.App", "Resources", "FluentStyles.axaml");
        var text = System.IO.File.ReadAllText(path);
        var index = text.IndexOf("Border#SelectionIndicator", StringComparison.Ordinal);
        Assert.That(index, Is.GreaterThan(0), "FluentStyles.axaml 里找不到导航指示条样式");

        // 只取第一条 Selector 起、到它自己的 </Style> 为止，别把后面无关样式卷进断言。
        var start = text.LastIndexOf("<Style ", index, StringComparison.Ordinal);
        Assert.That(start, Is.GreaterThan(0));
        var end = text.IndexOf("</Style>", index, StringComparison.Ordinal);
        var block = text.Substring(start, end - start);
        Assert.That(block, Does.Not.Contain("Property=\"IsVisible\""),
            "导航指示条又改回 IsVisible 隐藏了——折叠会让 FA 的缩放基准变成 0");
        Assert.That(block, Does.Contain("OpacityMask"));
    }

    /// <summary>指示条在窗口坐标系里的矩形，用来把像素取证收窄到它可能出现的竖带。</summary>
    private static Rect IndicatorRect(FANavigationViewItem item, Window window)
    {
        var indicator = Indicator(item);
        Assert.That(indicator, Is.Not.Null, $"[{item.Content}] 找不到指示条");
        var origin = indicator!.TranslatePoint(new Point(0, 0), window);
        Assert.That(origin, Is.Not.Null, $"[{item.Content}] 指示条不在窗口视觉树里");
        return new Rect(origin!.Value, indicator!.Bounds.Size);
    }

    /// <summary>
    /// 只数指定矩形里的强调蓝像素。判定与用户截图取证一致：B 明显高于 R/G 且足够亮。
    /// 无头帧是 Rgba8888，通道顺序按 locked.Format 判断，别写死 BGRA。
    /// </summary>
    private static int CountBlueIn(Window window, Rect area)
    {
        using var frame = window.CaptureRenderedFrame();
        Assert.That(frame, Is.Not.Null, "无头渲染必须产出帧，否则像素证据无效");
        using var locked = frame!.Lock();
        var width = locked.Size.Width;
        var height = locked.Size.Height;
        var stride = locked.RowBytes;
        var bytes = new byte[stride * height];
        System.Runtime.InteropServices.Marshal.Copy(locked.Address, bytes, 0, bytes.Length);
        var rgba = locked.Format == PixelFormat.Rgba8888;

        var x0 = Math.Max(0, (int)Math.Floor(area.X));
        var y0 = Math.Max(0, (int)Math.Floor(area.Y));
        var x1 = Math.Min(width, (int)Math.Ceiling(area.Right));
        var y1 = Math.Min(height, (int)Math.Ceiling(area.Bottom));

        var count = 0;
        for (var y = y0; y < y1; y++)
        {
            var row = y * stride;
            for (var x = x0; x < x1; x++)
            {
                var i = row + (x * 4);
                int r = rgba ? bytes[i] : bytes[i + 2];
                int g = bytes[i + 1];
                int b = rgba ? bytes[i + 2] : bytes[i];
                if (b > 90 && b - Math.Max(r, g) > 25)
                {
                    count++;
                }
            }
        }

        return count;
    }

    /// <summary>
    /// FA 用 Scale.Y 把指示条压在轨道里，除数为 0 时会算出 Inf/NaN。
    /// 直接读合成层的 Scale，把「渲染不出来」的根因钉死在数值上。
    /// </summary>
    private static void AssertFiniteScale(Border indicator, string where)
    {
        var visual = ElementComposition.GetElementVisual(indicator);
        if (visual is not CompositionVisual cv)
        {
            return;
        }

        Assert.That(double.IsFinite(cv.Scale.X), Is.True, $"{where} Scale.X 非有限值：{cv.Scale.X}");
        Assert.That(double.IsFinite(cv.Scale.Y), Is.True, $"{where} Scale.Y 非有限值：{cv.Scale.Y}");
    }

    private static (Window Window, FANavigationView Nav) CreateShell()
    {
        HeadlessAppHost.EnsureInitialized();
        var shell = new ShellViewModel();
        var window = new MainWindow { DataContext = shell };
        window.Width = 1280;
        window.Height = 800;
        window.Show();
        Pump(20);
        return (window, window.FindControl<FANavigationView>("NavView")!);
    }

    private static List<FANavigationViewItem> Items(FANavigationView nav)
        => nav.MenuItems.Concat(nav.FooterMenuItems).OfType<FANavigationViewItem>().ToList();

    private static Border? Indicator(FANavigationViewItem? item)
        => item?.GetVisualDescendants().OfType<Border>().FirstOrDefault(b => b.Name == "SelectionIndicator");

    private static void Pump(int frames)
    {
        for (var i = 0; i < frames; i++)
        {
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            HeadlessAppHost.PumpJobs();
            System.Threading.Thread.Sleep(8);
        }
    }

    private static string ResolveRepositoryRoot()
    {
        var dir = new System.IO.DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !System.IO.File.Exists(System.IO.Path.Combine(dir.FullName, "ckan-ui.sln")))
        {
            dir = dir.Parent;
        }

        return dir?.FullName ?? throw new InvalidOperationException("找不到仓库根目录");
    }
}
