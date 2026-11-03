using System;
using System.Linq;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.VisualTree;
using CkanUI.App.Tests.Automation;
using CkanUI.ViewModels;
using NUnit.Framework;

namespace CkanUI.App.Tests;

/// <summary>
/// 弹层不得盖住自绘光标：ComboBox 下拉列表走的是窗口内 PopupOverlayLayer，
/// 光标层挂在同一棵 VisualLayerManager 里，谁在上面由 ZIndex 裁决。
/// 这里用真实渲染帧 + 清空对照，防止「看起来修好了」这种自嗨。
/// </summary>
[TestFixture]
[NonParallelizable]
public sealed class CursorAbovePopupTests
{
    private const int FrameWidth = 1280;
    private const int FrameHeight = 800;
    private static readonly Point Tip = new(640, 400);

    [SetUp]
    public void SetUp() => HeadlessAppHost.EnsureInitialized();

    [Test]
    public void ComboBoxDropDown_IsHostedInWindowOverlayLayerNotASeparateWindow()
    {
        var (window, combo) = OpenDropDown();
        try
        {
            var popup = combo.GetVisualDescendants().OfType<Popup>().First();
            var host = popup.Child!;

            Assert.That(TopLevel.GetTopLevel(host), Is.SameAs((TopLevel)window),
                "下拉列表必须留在本窗口内，另开顶层窗口的话任何窗口内图层都盖不住它");

            var layerNames = host.GetSelfAndVisualAncestors().Select(v => v.GetType().Name).ToList();
            Assert.That(layerNames, Does.Contain("PopupOverlayLayer"),
                "下拉列表应挂在 VisualLayerManager 的弹层里");
        }
        finally
        {
            Close(window);
        }
    }

    [Test]
    public void CursorLayer_StaysAboveEveryOtherWindowLayer()
    {
        var (window, combo) = OpenDropDown();
        try
        {
            var cursorCandidate = window.CursorPointer;
            Assert.That(cursorCandidate, Is.Not.Null);
            var cursor = cursorCandidate!;

            var vlm = cursor.GetVisualParent() as VisualLayerManager;
            Assert.That(vlm, Is.Not.Null, "光标层应挂在 VisualLayerManager 上");

            var highestOther = vlm!.GetVisualChildren()
                .OfType<Visual>()
                .Where(v => !ReferenceEquals(v, cursor))
                .Select(v => v.ZIndex)
                .DefaultIfEmpty(0)
                .Max();

            Assert.That(cursor.ZIndex, Is.GreaterThan(highestOther),
                $"光标层 ZIndex({cursor.ZIndex}) 必须高于同窗口内最高的其它图层({highestOther})");
            Assert.That(combo.IsDropDownOpen, Is.True);
        }
        finally
        {
            Close(window);
        }
    }

    [Test]
    public void CustomCursor_IsStillDrawnOnTopOfAnOpenDropDown()
    {
        var (window, _) = OpenDropDown();
        try
        {
            window.MouseMove(Tip);
            HeadlessAppHost.PumpJobs();

            var withCursor = CountAccentNear(Capture(window), Tip, 24);
            Assert.That(withCursor, Is.GreaterThan(30),
                "光标尖端位置必须画出强调色像素，否则就是被下拉列表盖住了");

            // 对照：清掉光标后同一块区域不该再有同样的强调色像素，
            // 否则数到的其实是下拉列表自身的强调色高亮。
            window.CursorPointer!.Clear();
            HeadlessAppHost.PumpJobs();
            var withoutCursor = CountAccentNear(Capture(window), Tip, 24);

            Assert.That(withoutCursor, Is.LessThan(withCursor / 2),
                $"清空光标后强调色像素应显著减少（有 {withCursor}，清空后 {withoutCursor}）");
        }
        finally
        {
            Close(window);
        }
    }

    private static (MainWindow Window, ComboBox Combo) OpenDropDown()
    {
        var window = new MainWindow { DataContext = new ShellViewModel() };
        window.Show();
        HeadlessAppHost.PumpJobs();

        var combo = new ComboBox
        {
            ItemsSource = new[] { "Fastest", "Fast", "Simple", "Good", "Beautiful", "Fantastic" },
            SelectedIndex = 4,
            Width = 260
        };
        Canvas.SetLeft(combo, 540);
        Canvas.SetTop(combo, 360);

        var layer = new Canvas
        {
            Width = window.Bounds.Width,
            Height = window.Bounds.Height,
            Children = { combo }
        };

        var root = (Panel)window.Content!;
        root.Children.Add(layer);
        HeadlessAppHost.PumpJobs();

        combo.IsDropDownOpen = true;
        HeadlessAppHost.PumpJobs();
        HeadlessAppHost.PumpJobs();
        return (window, combo);
    }

    private static void Close(Window window)
    {
        window.Close();
        HeadlessAppHost.PumpJobs();
    }

    private static byte[] Capture(Window window)
    {
        HeadlessAppHost.PumpJobs();
        var frame = window.CaptureRenderedFrame();
        Assert.That(frame, Is.Not.Null, "无头 Skia 渲染必须能抓到帧");

        var size = frame!.PixelSize;
        var stride = size.Width * 4;
        var buffer = new byte[stride * size.Height];
        var handle = Marshal.AllocHGlobal(buffer.Length);
        try
        {
            frame.CopyPixels(new PixelRect(0, 0, size.Width, size.Height), handle, buffer.Length, stride);
            Marshal.Copy(handle, buffer, 0, buffer.Length);
        }
        finally
        {
            Marshal.FreeHGlobal(handle);
        }

        return buffer;
    }

    private static int CountAccentNear(byte[] pixels, Point center, int radius)
    {
        var count = 0;
        for (var y = (int)center.Y - radius; y <= (int)center.Y + radius; y++)
        {
            for (var x = (int)center.X - radius; x <= (int)center.X + radius; x++)
            {
                if (x < 0 || y < 0 || x >= FrameWidth || y >= FrameHeight)
                {
                    continue;
                }

                var i = (y * FrameWidth + x) * 4;
                var r = pixels[i];
                var g = pixels[i + 1];
                var b = pixels[i + 2];
                if (b > 150 && b - r > 30)
                {
                    count++;
                }
            }
        }

        return count;
    }
}
