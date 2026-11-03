using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;

using CkanUI.App.Animations;
using CkanUI.App.Tests.Automation;

using NUnit.Framework;

namespace CkanUI.App.Tests;

/// <summary>
/// 图标动效的"不一样"证明：七种形态跑出来的轨迹必须两两不同，
/// 同形态的两颗图标在画面上也不能长得一模一样（相位与性格各自独立）。
/// </summary>
/// <remarks>
/// 前一组用公开的角度/缩放/位移采样做轨迹比对，不依赖像素；
/// 后一组用 Skia 无头真渲染抓帧，比的是同一块矩形里的真实像素——
/// 数值不同但画出来一样，那种"重构"等于没做，所以这里必须有像素证据。
/// </remarks>
[TestFixture]
[NonParallelizable]
public sealed class IconHoverCharacterTests
{
    private const string Glyph = "M2 2 L14 2 L14 14 L2 14 Z";

    [SetUp]
    public void SetUp() => HeadlessAppHost.EnsureInitialized();

    [Test]
    public void EveryShape_TracesItsOwnPath()
    {
        var shapes = Enum.GetValues<IconHoverShape>()
            .Where(shape => shape != IconHoverShape.Auto)
            .ToArray();

        var traces = new Dictionary<IconHoverShape, double[]>();

        foreach (var shape in shapes)
        {
            traces[shape] = SampleTrace(shape);
        }

        foreach (var left in shapes)
        {
            foreach (var right in shapes)
            {
                if (string.CompareOrdinal(left.ToString(), right.ToString()) >= 0)
                {
                    continue;
                }

                // 四个通道各自的"看得出来"阈值不同：角度 0.25°、缩放 2%、位移 0.25px，
                // 归一之后取最大差，任何一种通道拉开就算两种形态不一样。
                var weights = new[] { 4d, 50d, 4d, 4d };
                var gap = traces[left]
                    .Zip(traces[right], (a, b) => (Sample: Math.Abs(a - b), Channel: 0))
                    .Select((pair, index) => pair.Sample * weights[index % 4])
                    .Max();
                Assert.That(gap, Is.GreaterThan(1d), $"{left} 与 {right} 不该跑出同一条轨迹");
            }
        }
    }

    [Test]
    public void HoveringIcons_DoNotRenderTheSameFrame()
    {
        var (window, icons) = MountRow();
        try
        {
            HeadlessAppHost.PumpJobs();

            var neutral = Capture(window);
            var neutralLeft = Region(neutral, window, 40);
            var neutralRight = Region(neutral, window, 180);

            Assert.That(Diff(neutralLeft, neutralRight), Is.LessThan(8), "静止时两颗同形图标应当逐像素一致，区域提取才可信");

            var left = IconHoverTrack.For(icons[0]);
            var middle = IconHoverTrack.For(icons[1]);
            var right = IconHoverTrack.For(icons[2]);

            left.Enter(IconHoverShape.Sway);
            middle.Enter(IconHoverShape.Float);
            right.Enter(IconHoverShape.Sway);

            for (var frame = 0; frame < 66; frame++)
            {
                left.Advance(1d / 60d);
                middle.Advance(1d / 60d);
                right.Advance(1d / 60d);
            }

            var hovered = Capture(window);
            var swayPixels = Region(hovered, window, 40);
            var floatPixels = Region(hovered, window, 110);
            var otherSwayPixels = Region(hovered, window, 180);

            TestContext.Out.WriteLine($"pixels: hovered-vs-neutral={Diff(neutralLeft, swayPixels)}, sway-vs-float={Diff(swayPixels, floatPixels)}, sway-vs-sway={Diff(swayPixels, otherSwayPixels)}");
            Assert.That(Diff(neutralLeft, swayPixels), Is.GreaterThan(40), "悬停必须真的改变画面");
            Assert.That(Diff(swayPixels, floatPixels), Is.GreaterThan(40), "钟摆与呼吸不能画出同一帧");
            Assert.That(Diff(swayPixels, otherSwayPixels), Is.GreaterThan(12), "同形态的两颗图标也不能踩着同一个拍子摆");
        }
        finally
        {
            window.Close();
            HeadlessAppHost.PumpJobs();
        }
    }

    private static double[] SampleTrace(IconHoverShape shape)
    {
        var icon = CreateIcon();
        IconHoverMotion.SetShape(icon, shape);
        var window = ShowInCanvas(icon, 20, 20);

        try
        {
            HeadlessAppHost.PumpJobs();
            var track = IconHoverTrack.For(icon);

            window.MouseMove(CenterOf(icon, window));
            HeadlessAppHost.PumpJobs();
            Assert.That(track.IsHovered, Is.True);

            var samples = new List<double>();
            for (var round = 0; round < 10; round++)
            {
                Tick(6);
                samples.Add(track.Angle);
                samples.Add(track.Scale);
                samples.Add(track.OffsetX);
                samples.Add(track.OffsetY);
            }

            return samples.ToArray();
        }
        finally
        {
            window.Close();
            HeadlessAppHost.PumpJobs();
        }
    }

    private static (Window Window, Control[] Icons) MountRow()
    {
        var canvas = new Canvas
        {
            Width = 240,
            Height = 140,
            Background = new SolidColorBrush(Color.Parse("#0F1218")),
        };

        var icons = new Control[3];
        var positions = new[] { 40d, 110d, 180d };
        for (var i = 0; i < icons.Length; i++)
        {
            icons[i] = CreateIcon();
            Canvas.SetLeft(icons[i], positions[i]);
            Canvas.SetTop(icons[i], 60);
            canvas.Children.Add(icons[i]);
        }

        return (HeadlessAppHost.CreateTestWindow(canvas, 240, 140), icons);
    }

    private static Control CreateIcon() => new PathIcon
    {
        Data = Geometry.Parse(Glyph),
        Width = 16,
        Height = 16,
        Foreground = new SolidColorBrush(Colors.White),
    };

    private static Window ShowInCanvas(Control icon, double left, double top)
    {
        var canvas = new Canvas
        {
            Width = 320,
            Height = 200,
            Background = new SolidColorBrush(Color.Parse("#0F1218")),
        };
        Canvas.SetLeft(icon, left);
        Canvas.SetTop(icon, top);
        canvas.Children.Add(icon);
        return HeadlessAppHost.CreateTestWindow(canvas, 320, 200);
    }

    private static Point CenterOf(Control icon, Window window)
    {
        var origin = icon.TranslatePoint(new Point(icon.Bounds.Width / 2, icon.Bounds.Height / 2), window);
        Assert.That(origin, Is.Not.Null);
        return origin!.Value;
    }

    private static void Tick(int frames)
    {
        for (var frame = 0; frame < frames; frame++)
        {
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            HeadlessAppHost.PumpJobs();
            Thread.Sleep(16);
        }
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

    /// <summary>取某个图标周围 48×48 的像素，坐标按窗口缩放折算到设备像素。</summary>
    private static byte[] Region(byte[] frame, Window window, double centerX)
    {
        var scale = (int)(frame.Length / 4 / ((int)window.Height) / window.Width);
        var frameWidth = (int)window.Width * scale;
        var left = (int)Math.Round((centerX - 16) * scale);
        var top = (int)Math.Round((60d - 16d) * scale);
        var side = 48 * scale;

        var region = new byte[side * side * 4];
        for (var y = 0; y < side; y++)
        {
            var source = (((top + y) * frameWidth) + left) * 4;
            Buffer.BlockCopy(frame, source, region, y * side * 4, side * 4);
        }

        return region;
    }

    private static int Diff(byte[] left, byte[] right)
    {
        var count = 0;
        for (var i = 0; i + 3 < left.Length && i + 3 < right.Length; i += 4)
        {
            var delta = Math.Abs(left[i] - right[i]) + Math.Abs(left[i + 1] - right[i + 1]) + Math.Abs(left[i + 2] - right[i + 2]);
            if (delta > 12)
            {
                count++;
            }
        }

        return count;
    }
}
