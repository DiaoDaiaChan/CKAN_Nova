using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.Styling;
using CkanUI.App.Controls;
using CkanUI.App.Tests.Automation;
using CkanUI.Services.Abstractions.Dtos;
using NUnit.Framework;

namespace CkanUI.App.Tests;

/// <summary>
/// 环形统计图控件测试（12 篇 F-47 / 06 篇 UI-25）：
/// 验证主题色解析在有头/无头各主题变体下的可用性，以及空态/多切片下的渲染健壮性。
/// </summary>
[TestFixture]
public sealed class DonutChartTests
{
    [SetUp]
    public void Setup()
    {
        HeadlessAppHost.EnsureInitialized();
    }

    [Test]
    public void ThemeBrushes_ResolveFromActualThemeVariant_WhenAttachedToWindow()
    {
        var chart = new DonutChart();
        var window = new Window { Content = chart };
        var app = Application.Current!;

        // 验证浅色主题下画刷解析
        app.RequestedThemeVariant = ThemeVariant.Light;
        var foundLight = chart.TryFindResource("CkanAccentBrush", chart.ActualThemeVariant, out var lightBrush);
        Assert.That(foundLight, Is.True, "浅色主题下必须能解析 CkanAccentBrush");
        Assert.That(lightBrush, Is.InstanceOf<IBrush>());

        // 验证深色主题下画刷解析
        app.RequestedThemeVariant = ThemeVariant.Dark;
        var foundDark = chart.TryFindResource("CkanAccentBrush", chart.ActualThemeVariant, out var darkBrush);
        Assert.That(foundDark, Is.True, "深色主题下必须能解析 CkanAccentBrush");
        Assert.That(darkBrush, Is.InstanceOf<IBrush>());
    }

    [Test]
    public void DonutChart_MeasureAndArrange_WithSlices_Succeeds()
    {
        var chart = new DonutChart
        {
            Width = 180,
            Height = 180,
            RingThickness = 22,
            Slices = new[]
            {
                new DownloadStatsSlice("MechJeb2", 2675641, 1048576, 75.0),
                new DownloadStatsSlice("ModuleManager", 890000, 524288, 25.0),
            },
            CenterText = "2",
            CenterCaption = "个模组",
        };

        var window = new Window { Content = chart };
        chart.Measure(new Size(180, 180));
        chart.Arrange(new Rect(0, 0, 180, 180));

        Assert.That(chart.Bounds.Width, Is.EqualTo(180));
        Assert.That(chart.Bounds.Height, Is.EqualTo(180));
    }

    [Test]
    public void DonutChart_MeasureAndArrange_EmptySlices_Succeeds()
    {
        var chart = new DonutChart
        {
            Width = 180,
            Height = 180,
            RingThickness = 22,
            Slices = Array.Empty<DownloadStatsSlice>(),
            CenterText = "0",
            CenterCaption = "个模组",
        };

        var window = new Window { Content = chart };
        chart.Measure(new Size(180, 180));
        chart.Arrange(new Rect(0, 0, 180, 180));

        Assert.That(chart.Bounds.Width, Is.EqualTo(180));
        Assert.That(chart.Bounds.Height, Is.EqualTo(180));
    }

    [Test]
    public void DonutChart_RenderFrame_ProducesValidBitmapFrame()
    {
        var chart = new DonutChart
        {
            Width = 180,
            Height = 180,
            RingThickness = 22,
            Slices = new[]
            {
                new DownloadStatsSlice("MechJeb2", 2675641, 1048576, 75.0),
                new DownloadStatsSlice("ModuleManager", 890000, 524288, 25.0),
            },
            CenterText = "2",
            CenterCaption = "个模组",
        };

        var window = new Window
        {
            Width = 300,
            Height = 300,
            Content = chart
        };
        window.Show();
        HeadlessAppHost.PumpJobs();

        var frame = window.CaptureRenderedFrame();
        Assert.That(frame, Is.Not.Null, "无头环境抓帧必须成功");
        Assert.That(frame!.PixelSize.Width, Is.GreaterThan(0));
        Assert.That(frame.PixelSize.Height, Is.GreaterThan(0));
    }
}
