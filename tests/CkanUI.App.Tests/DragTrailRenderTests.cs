using System;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.Platform;
using CkanUI.App.Controls;
using CkanUI.App.Tests.Automation;
using NUnit.Framework;

namespace CkanUI.App.Tests;

/// <summary>
/// 拖动细线的像素级验证：用 Skia 无头渲染抓真实帧，证明「画出来的是一条细线」而不是光斑。
/// 不开窗口、不控制物理鼠标。
/// </summary>
[TestFixture]
public sealed class DragTrailRenderTests
{
    [Test]
    public void Drag_DrawsAThinAccentLine()
    {
        var (window, trail) = Mount();

        trail.Begin(new Point(60, 100));
        for (var x = 70; x <= 240; x += 10)
        {
            trail.Move(new Point(x, 100));
        }

        var pixels = Capture(window);
        var blue = CountAccent(pixels);

        Assert.That(blue, Is.GreaterThan(200), "拖动轨迹应当被真正画出来");
        Assert.That(ThicknessAt(pixels, 150), Is.LessThanOrEqualTo(4), "线必须是细线：竖直截面不超过 4px");
    }

    [Test]
    public void Clear_RemovesTheLineFromTheFrame()
    {
        var (window, trail) = Mount();
        trail.Begin(new Point(60, 100));
        trail.Move(new Point(240, 100));
        Assert.That(CountAccent(Capture(window)), Is.GreaterThan(0));

        trail.Clear();
        HeadlessAppHost.PumpJobs();

        Assert.That(CountAccent(Capture(window)), Is.Zero, "清空后不该再留像素");
    }

    private static (Window Window, DragTrailLayer Trail) Mount()
    {
        var trail = new DragTrailLayer { Width = 320, Height = 200 };
        var root = new Panel
        {
            Background = new SolidColorBrush(Color.Parse("#0F1218")),
            Children = { trail }
        };

        var window = HeadlessAppHost.CreateTestWindow(root, 320, 200);
        return (window, trail);
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

    private static int CountAccent(byte[] pixels)
    {
        var count = 0;
        for (var i = 0; i + 3 < pixels.Length; i += 4)
        {
            var r = pixels[i];
            var g = pixels[i + 1];
            var b = pixels[i + 2];
            if (b - r > 20 && b > g && g >= r)
            {
                count++;
            }
        }

        return count;
    }

    private static int ThicknessAt(byte[] pixels, int x)
    {
        var rows = 0;
        const int width = 320;
        for (var y = 0; y < 200; y++)
        {
            var i = (y * width + x) * 4;
            var r = pixels[i];
            var g = pixels[i + 1];
            var b = pixels[i + 2];
            if (b - r > 20 && b > g && g >= r)
            {
                rows++;
            }
        }

        return rows;
    }

    [Test]
    public void Drag_DrawsOrbitalNodesAlongCurve()
    {
        var (window, trail) = Mount();

        // 绘制一个环形轨迹（模拟轨道划线）
        const double centerX = 160;
        const double centerY = 100;
        const double radius = 60;
        trail.Begin(new Point(centerX + radius, centerY));
        for (var angle = 0.1d; angle <= Math.PI * 2 + 0.1d; angle += 0.15d)
        {
            var px = centerX + Math.Cos(angle) * radius;
            var py = centerY + Math.Sin(angle) * radius;
            trail.Move(new Point(px, py));
        }

        Assert.That(trail.Nodes.Count, Is.GreaterThanOrEqualTo(2), "环形划线应生成轨道节点");

        var pixels = Capture(window);
        var blue = CountAccent(pixels);
        Assert.That(blue, Is.GreaterThan(200), "环形轨迹与节点均应正常渲染");
    }
}
