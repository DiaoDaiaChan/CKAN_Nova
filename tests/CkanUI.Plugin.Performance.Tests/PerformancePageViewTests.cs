using System.Text.Json.Nodes;
using Avalonia;
using Avalonia.Controls;
using Avalonia.VisualTree;
using CkanUI.Services.Abstractions.Dtos;
using CkanUI.Ui.Charts;
using CkanUI.Ui.Icons;
using NUnit.Framework;

namespace CkanUI.Plugin.Performance.Tests;

/// <summary>
/// 性能页的视图冒烟：真的把 <c>PerformancePageView</c> 装进无头窗口，确认数据一路走到控件上。
/// </summary>
/// <remarks>
/// <para>纯函数用例（<c>FrameSeriesBuilderTests</c>）保证「算什么」，这里保证「画的是不是那个」：
/// 绑定名写错、控件被塞进不可见的容器、宽度塌成 0——这些在纯函数里一条都测不出来。</para>
/// <para>只断言结构与数量，不断言像素：具体形状由 <c>CurveChart</c> 自己的用例负责。</para>
/// </remarks>
[TestFixture]
public sealed class PerformancePageViewTests
{
    [Test]
    public void FrameCurvesAreBoundToBothSeriesAndLaidOut()
    {
        UiTestHost.EnsureInitialized();
        RequireWindowAffinity();

        using var game = new TempGameDirectory();
        game.WriteTelemetry(TelemetryFixtures.LatestAgedBy(120), TelemetryFixtures.StatusWithWriterAlive(true));

        using var context = UiTestHost.UseDispatcherSynchronizationContext();
        using var viewModel = new PerformancePageViewModel(CreateInstances(game.Root));
        viewModel.StopPolling();

        // RefreshCommand 里同步跑一轮 Poll；更新通过刚装上的同步上下文投递回来，由 PumpJobs 落地。
        viewModel.RefreshCommand.Execute(null);
        Assert.That(UiTestHost.WaitUntil(() => viewModel.HasFrameSeries), Is.True,
            "latest.json 里有 3 帧，轮询一轮后就应该有帧曲线数据。");

        var window = Show(viewModel);
        try
        {
            var charts = window.GetVisualDescendants().OfType<CurveChart>().ToArray();
            var glyphs = window.GetVisualDescendants().OfType<GlyphIcon>().ToArray();

            Assert.Multiple(() =>
            {
                Assert.That(charts, Has.Length.EqualTo(2), "帧耗时与帧率各一张图（量纲不同，不共用 Y 轴）。");
                Assert.That(charts.Select(chart => chart.Series![0].Name), Is.EqualTo(new[] { "帧耗时", "帧率" }));
                Assert.That(charts.All(chart => chart.HasData), Is.True, "两张图都应该有可绘制的点。");

                // 布局真的发生过：绑定失败或被折叠的控件宽高会是 0。
                Assert.That(charts.All(chart => chart.Bounds.Width > 0 && chart.Bounds.Height > 0), Is.True,
                    "两张曲线都应被真正布局（宽高大于 0）。");
                Assert.That(charts[0].ValueSuffix, Is.EqualTo("ms"));
                Assert.That(charts[1].ValueSuffix, Is.EqualTo("fps"));

                Assert.That(glyphs, Is.Not.Empty, "指标卡与分解行都要带图标。");
                Assert.That(glyphs.All(glyph => glyph.HasGlyph), Is.True,
                    "页内字形名必须都能解析：猜错名字的图标宁可在这里炸，也不要在界面上留空。");
            });
        }
        finally
        {
            window.Close();
        }
    }

    [Test]
    public void MissingTimelineShowsExplanationInsteadOfCharts()
    {
        UiTestHost.EnsureInitialized();
        RequireWindowAffinity();

        using var game = new TempGameDirectory();
        game.WriteTelemetry(WithoutTimeline(TelemetryFixtures.LatestAgedBy(120)), TelemetryFixtures.StatusWithWriterAlive(true));

        using var context = UiTestHost.UseDispatcherSynchronizationContext();
        using var viewModel = new PerformancePageViewModel(CreateInstances(game.Root));
        viewModel.StopPolling();

        viewModel.RefreshCommand.Execute(null);
        Assert.That(UiTestHost.WaitUntil(() => viewModel.ShowContent), Is.True, "有快照就应该显示内容区。");
        Assert.That(viewModel.HasFrameSeries, Is.False, "没有 timeline.frames 就不该有曲线数据。");

        var window = Show(viewModel);
        try
        {
            var visibleCharts = window.GetVisualDescendants().OfType<CurveChart>().Where(chart => chart.IsEffectivelyVisible).ToArray();
            var explanation = window.GetVisualDescendants().OfType<TextBlock>()
                .FirstOrDefault(text => text.IsEffectivelyVisible && text.Text?.Contains("不补 0", StringComparison.Ordinal) == true);

            Assert.Multiple(() =>
            {
                Assert.That(visibleCharts, Is.Empty, "没有点就不该出现曲线。");
                Assert.That(explanation, Is.Not.Null, "要给出「为什么不画」，而不是一片空白。");
            });
        }
        finally
        {
            window.Close();
        }
    }

    private static void RequireWindowAffinity()
    {
        if (!UiTestHost.CanCreateWindows)
        {
            Assert.Ignore("无头壳由全量套件里另一个测试程序集在别的线程建好，本线程没有窗口亲和性；单跑本程序集即可验证本条。");
        }
    }

    private static FakeInstanceService CreateInstances(string gameDir)
        => new() { Current = new InstanceInfo("Test", gameDir, "1.12.5", false, false, null) };

    private static Window Show(PerformancePageViewModel viewModel)
    {
        var window = new Window
        {
            Width = 1200,
            Height = 900,
            Content = new PerformancePageView { DataContext = viewModel },
        };

        window.Show();
        UiTestHost.PumpJobs();
        window.Measure(new Size(1200, 900));
        window.Arrange(new Rect(0, 0, 1200, 900));
        UiTestHost.PumpJobs();
        window.UpdateLayout();
        UiTestHost.PumpJobs();
        return window;
    }

    private static string WithoutTimeline(string latestJson)
    {
        var node = JsonNode.Parse(latestJson)!.AsObject();
        node.Remove("timeline");
        return node.ToJsonString();
    }

    /// <summary>临时游戏目录：只建遥测目录，够页面算出一个游戏目录即可。</summary>
    private sealed class TempGameDirectory : IDisposable
    {
        public TempGameDirectory()
        {
            Root = Path.Combine(Path.GetTempPath(), "kpp-view-" + Guid.NewGuid().ToString("n"));
            TelemetryDirectory = Path.Combine(Root, "GameData", "KSPPerformanceProfiler", "Telemetry");
            Directory.CreateDirectory(TelemetryDirectory);
        }

        public string Root { get; }

        public string TelemetryDirectory { get; }

        public void WriteTelemetry(string latestJson, string statusJson)
        {
            File.WriteAllText(Path.Combine(TelemetryDirectory, "latest.json"), latestJson);
            File.WriteAllText(Path.Combine(TelemetryDirectory, "status.json"), statusJson);
        }

        public void Dispose()
        {
            try
            {
                if (Directory.Exists(Root))
                {
                    Directory.Delete(Root, recursive: true);
                }
            }
            catch (IOException)
            {
                // 临时目录删不掉不影响用例结论。
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }
}
