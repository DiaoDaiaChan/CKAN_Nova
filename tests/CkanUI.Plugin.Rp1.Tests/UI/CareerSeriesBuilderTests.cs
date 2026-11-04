using System;
using System.Collections.Generic;
using CkanUI.Plugin.Rp1.Career;
using CkanUI.Plugin.Rp1.UI;
using NUnit.Framework;

namespace CkanUI.Plugin.Rp1.Tests.UI;

/// <summary>
/// 生涯曲线的构建：画全部周期（不止列表里的 12 期），缺席保持断线，不补 0。
/// </summary>
[TestFixture]
public sealed class CareerSeriesBuilderTests
{
    [Test]
    public void NoPeriods_SaysWhyThereIsNoCurve()
    {
        Assert.Multiple(() =>
        {
            var empty = CareerSeriesBuilder.Build(Array.Empty<Rp1CareerPeriod>(), "funds", "资金");
            Assert.That(empty.HasData, Is.False);
            Assert.That(empty.Series, Is.Empty);
            Assert.That(empty.Summary, Does.Contain("没有预算周期"));

            var missing = CareerSeriesBuilder.Build(null, "funds", "资金");
            Assert.That(missing.HasData, Is.False);
            Assert.That(missing.Series, Is.Empty);
        });
    }

    [Test]
    public void FundsSeriesCoversEveryPeriodInOrder()
    {
        var periods = new[]
        {
            Period(0, funds: 1000d),
            Period(1, funds: 2500d),
            Period(2, funds: 1800d),
        };

        var result = CareerSeriesBuilder.Build(periods, "funds", "资金");

        Assert.Multiple(() =>
        {
            Assert.That(result.HasData, Is.True);
            Assert.That(result.Series, Has.Count.EqualTo(1));
            Assert.That(result.Series[0].Name, Is.EqualTo("资金"));
            Assert.That(result.Series[0].Values, Is.EqualTo(new double?[] { 1000d, 2500d, 1800d }));
            Assert.That(result.Series[0].Fill, Is.Not.Null, "资金有面积填充，其余指标只画线");
            Assert.That(result.Summary, Does.Contain("3 期"));
            Assert.That(result.Summary, Does.Contain("期序号"));
        });
    }

    [Test]
    public void EachMetricKeyReadsItsOwnField()
    {
        var period = new Rp1CareerPeriod(0, 0d, 1000d, "1951-01-01", 1000d, 10d, 5d, 10d, 20d, 0, 100d);

        var science = CareerSeriesBuilder.Build(new[] { period }, "science", "科技");
        var confidence = CareerSeriesBuilder.Build(new[] { period }, "confidence", "信心");
        var reputation = CareerSeriesBuilder.Build(new[] { period }, "reputation", "声望");

        Assert.Multiple(() =>
        {
            Assert.That(science.Series[0].Values[0], Is.EqualTo(10d));
            Assert.That(confidence.Series[0].Values[0], Is.EqualTo(10d));
            Assert.That(reputation.Series[0].Values[0], Is.EqualTo(20d));
            Assert.That(science.Series[0].Fill, Is.Null);
            Assert.That(confidence.Series[0].Fill, Is.Null);
            Assert.That(reputation.Series[0].Fill, Is.Null);
        });
    }

    [Test]
    public void UnknownOrEmptyKeyFallsBackToFunds()
    {
        var periods = new[] { Period(0, funds: 500d) };

        Assert.Multiple(() =>
        {
            Assert.That(CareerSeriesBuilder.Build(periods, null, "指标").Series[0].Values[0], Is.EqualTo(500d));
            Assert.That(CareerSeriesBuilder.Build(periods, "   ", "指标").Series[0].Values[0], Is.EqualTo(500d));
            Assert.That(CareerSeriesBuilder.Build(periods, "unknown", "指标").Series[0].Values[0], Is.EqualTo(500d));
        });
    }

    [Test]
    public void MissingValuesStayNullInsteadOfBecomingZero()
    {
        var periods = new[]
        {
            new Rp1CareerPeriod(0, 0d, 1000d, "1951-01-01", null, 10d, 5d, 10d, 20d, 0, 100d),
            new Rp1CareerPeriod(1, 1000d, 2000d, "1951-01-02", 1500d, 10d, 5d, 10d, 20d, 0, 100d),
        };

        var result = CareerSeriesBuilder.Build(periods, "funds", "资金");

        Assert.Multiple(() =>
        {
            Assert.That(result.Series[0].Values[0], Is.Null, "缺席的周期断线，不能画成「钱花光了」");
            Assert.That(result.Series[0].Values[1], Is.EqualTo(1500d));
            Assert.That(result.HasData, Is.True);
        });
    }

    [Test]
    public void MetricWithNoValuesAtAll_DoesNotDrawACurve()
    {
        var periods = new[]
        {
            new Rp1CareerPeriod(0, 0d, 1000d, "1951-01-01", 1000d, null, null, null, null, 0, 100d),
        };

        var result = CareerSeriesBuilder.Build(periods, "science", "科技");

        Assert.Multiple(() =>
        {
            Assert.That(result.HasData, Is.False);
            Assert.That(result.Series, Is.Empty);
            Assert.That(result.Summary, Does.Contain("没有「科技」"));
        });
    }

    [Test]
    public void MissingLabel_FallsBackToAPlaceholder()
    {
        var result = CareerSeriesBuilder.Build(new[] { Period(0, funds: 1d) }, "funds", null);

        Assert.That(result.Series[0].Name, Is.EqualTo("指标"));
    }

    private static Rp1CareerPeriod Period(int index, double? funds)
        => new(
            index,
            StartUt: index * 1000d,
            EndUt: (index + 1) * 1000d,
            DateText: "1951-01-0" + (index + 1),
            Funds: funds,
            Science: 10d,
            ScienceEarned: 5d,
            Confidence: 10d,
            Reputation: 20d,
            CrewLost: 0,
            SubsidyPaidOut: 100d);
}
