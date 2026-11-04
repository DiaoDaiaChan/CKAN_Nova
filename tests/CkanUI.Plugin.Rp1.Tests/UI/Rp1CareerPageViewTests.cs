using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.VisualTree;
using CkanUI.Plugin.Rp1.Career;
using CkanUI.Plugin.Rp1.Tests.Support;
using CkanUI.Plugin.Rp1.UI;
using CkanUI.Services.Abstractions.Dtos;
using CkanUI.Ui.Charts;
using CkanUI.Ui.Icons;
using NUnit.Framework;

namespace CkanUI.Plugin.Rp1.Tests.UI;

/// <summary>
/// RP-1 生涯页的视图冒烟：真的把 <c>Rp1CareerPageView</c> 装进无头窗口，确认曲线与指标切换一路走到控件上。
/// </summary>
/// <remarks>
/// 纯函数用例（<c>CareerSeriesBuilderTests</c>）保证「算什么」，这里保证「画的是不是那个」：
/// 绑定名写错、控件被塞进不可见的页签、宽度塌成 0——这些在纯函数里一条都测不出来。
/// 预算周期页签是懒加载的，要先选中它才能拿到可视树里的曲线。
/// </remarks>
[TestFixture]
public sealed class Rp1CareerPageViewTests
{
    [Test]
    public void CareerCurveAndMetricTabsAreWiredToTheViewModel()
    {
        UiTestHost.EnsureInitialized();
        if (!UiTestHost.CanCreateWindows)
        {
            Assert.Ignore("无头壳由全量套件里另一个测试程序集在别的线程建好，本线程没有窗口亲和性；单跑本程序集即可验证本条。");
        }

        using var game = new TempGame();
        var career = new FakeCareerService { Timeline = TimelineWithPeriods(15, game.Root) };
        career.Saves.Add(new Rp1SaveSummary(
            "Test",
            Path.Combine(game.Root, "saves", "Test"),
            Path.Combine(game.Root, "saves", "Test", "persistent.sfs"),
            DateTime.UtcNow,
            4096));
        var instances = new FakeInstanceService
        {
            Current = new InstanceInfo("Test", game.Root, "1.12.5", false, false, null),
        };

        using var context = UiTestHost.UseDispatcherSynchronizationContext();
        using var viewModel = Rp1CareerPageViewModel.CreateForTests(career, instances);

        var window = new Window
        {
            Width = 1400,
            Height = 1000,
            Content = new Rp1CareerPageView { DataContext = viewModel },
        };

        try
        {
            window.Show();
            Assert.That(UiTestHost.WaitUntil(() => viewModel.ShowContent && viewModel.HasCareerSeries), Is.True,
                "视图挂上树就会 Start 并读到存档，之后应画出生涯曲线。");

            var tabs = window.GetVisualDescendants().OfType<TabControl>().FirstOrDefault();
            Assert.That(tabs, Is.Not.Null, "预算周期页签是懒加载的，先要拿到 TabControl。");
            tabs!.SelectedIndex = 1;

            UiTestHost.PumpJobs();
            window.Measure(new Size(1400, 1000));
            window.Arrange(new Rect(0, 0, 1400, 1000));
            window.UpdateLayout();
            UiTestHost.PumpJobs();
            window.UpdateLayout();

            var charts = window.GetVisualDescendants().OfType<CurveChart>().ToArray();
            var metricTabs = window.GetVisualDescendants().OfType<Button>()
                .Where(button => button.Classes.Contains("rp1-metric-tab"))
                .ToArray();
            var glyphs = window.GetVisualDescendants().OfType<GlyphIcon>().ToArray();

            Assert.Multiple(() =>
            {
                Assert.That(viewModel.Periods, Has.Count.EqualTo(12), "列表只显示最近 12 期。");
                Assert.That(charts, Has.Length.EqualTo(1), "预算周期页只有一个量纲的曲线。");
                Assert.That(charts[0].Series, Is.Not.Null);
                Assert.That(charts[0].Series![0].Name, Is.EqualTo("资金"));
                Assert.That(charts[0].Series![0].Values, Has.Count.EqualTo(15),
                    "曲线画的是全部周期，不是列表里的 12 期。");
                Assert.That(charts[0].Bounds.Width, Is.GreaterThan(0), "曲线应被真正布局，宽度大于 0。");
                Assert.That(charts[0].Bounds.Height, Is.GreaterThan(0));
                Assert.That(charts[0].ValueSuffix, Is.EqualTo(" 资金"));
                Assert.That(metricTabs.Select(button => button.CommandParameter as string),
                    Is.EquivalentTo(new[] { "funds", "science", "confidence", "reputation" }));
                Assert.That(glyphs, Is.Not.Empty, "统计卡与指标页签都要带图标。");
                Assert.That(glyphs.All(glyph => glyph.HasGlyph), Is.True,
                    "页内字形名必须都能解析：猜错名字的图标宁可在这里炸，也不要在界面上留空。");
            });

            viewModel.SelectCareerMetricCommand.Execute("science");
            UiTestHost.PumpJobs();
            window.UpdateLayout();

            Assert.Multiple(() =>
            {
                Assert.That(viewModel.CareerSeries[0].Name, Is.EqualTo("科技"));
                Assert.That(charts[0].ValueSuffix, Is.EqualTo(" 点"), "切到科技后 Y 轴单位要跟着换。");
            });
        }
        finally
        {
            window.Close();
        }
    }

    private static Rp1CareerTimeline TimelineWithPeriods(int count, string gameDir)
    {
        var periods = new List<Rp1CareerPeriod>(count);
        for (var i = 0; i < count; i++)
        {
            periods.Add(new Rp1CareerPeriod(
                Index: i,
                StartUt: 1000d * i,
                EndUt: (1000d * i) + 1000d,
                DateText: new DateTime(1951, 1, 1).AddDays(31 * i).ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture),
                Funds: 100000d + (2500d * i),
                Science: 10d + i,
                ScienceEarned: 1d + (0.5d * i),
                Confidence: 40d + i,
                Reputation: 5d + (0.5d * i),
                CrewLost: 0,
                SubsidyPaidOut: 1000d));
        }

        return new Rp1CareerTimeline(
            SaveName: "Test",
            SavePath: Path.Combine(gameDir, "saves", "Test", "persistent.sfs"),
            GameDir: gameDir,
            Rp1Version: "1.12.0",
            GameVersion: "1.12.5",
            CurrentUt: 15000d,
            CurrentDateText: "1951-02-01 00:16",
            Rp1Installed: true,
            Events: Array.Empty<Rp1CareerEvent>(),
            Periods: periods,
            ContractCount: 3,
            MilestoneCount: 2,
            ProgramCount: 1,
            CrewRetiredCount: 4,
            CrewLostCount: 1,
            CurrentFunds: 135000d,
            CurrentScience: 24d,
            CurrentConfidence: 54d,
            CurrentReputation: 12d,
            Diagnostics: Array.Empty<string>());
    }
}
