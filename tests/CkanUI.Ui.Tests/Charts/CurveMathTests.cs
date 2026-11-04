using System;
using System.Linq;
using Avalonia;
using CkanUI.Ui.Charts;
using NUnit.Framework;

namespace CkanUI.Ui.Tests.Charts;

/// <summary>
/// <see cref="CurveMath"/> 的行为锁：值域归并与坐标投影是曲线图里最容易算错、又最难靠肉眼发现的两段。
/// </summary>
[TestFixture]
public sealed class CurveMathTests
{
    [Test]
    public void ResolveRange_NoSeries_ReportsNoData()
    {
        var range = CurveMath.ResolveRange(null);

        Assert.Multiple(() =>
        {
            Assert.That(range.HasData, Is.False);
            Assert.That(range.Min, Is.EqualTo(0d));
            Assert.That(range.Max, Is.EqualTo(1d));
            Assert.That(range.Span, Is.EqualTo(1d));
        });
    }

    [Test]
    public void ResolveRange_EmptyAndAllNull_ReportsNoData()
    {
        var series = new[]
        {
            new ChartSeries("空", Array.Empty<double?>()),
            new ChartSeries("全空", new double?[] { null, null }),
            new ChartSeries("非数", new double?[] { double.NaN, double.PositiveInfinity }),
        };

        var range = CurveMath.ResolveRange(series, includeZero: false);

        Assert.That(range.HasData, Is.False);
        Assert.That(range.Span, Is.EqualTo(1d));
    }

    [Test]
    public void ResolveRange_IncludeZero_StretchesToTheBaseline()
    {
        var range = CurveMath.ResolveRange(new[] { new ChartSeries("资金", new double?[] { 40d, 55d, 48d }) });

        Assert.Multiple(() =>
        {
            Assert.That(range.HasData, Is.True);
            Assert.That(range.Min, Is.EqualTo(0d));
            Assert.That(range.Max, Is.EqualTo(55d));
        });
    }

    [Test]
    public void ResolveRange_WithoutZero_UsesTheDataBoundsOnly()
    {
        var range = CurveMath.ResolveRange(
            new[] { new ChartSeries("资金", new double?[] { 40d, 55d, 48d }) },
            includeZero: false);

        Assert.Multiple(() =>
        {
            Assert.That(range.Min, Is.EqualTo(40d));
            Assert.That(range.Max, Is.EqualTo(55d));
            Assert.That(range.Span, Is.EqualTo(15d));
        });
    }

    [Test]
    public void ResolveRange_MergesEverySeries()
    {
        var range = CurveMath.ResolveRange(
            new[]
            {
                new ChartSeries("帧耗时", new double?[] { 12d, 18d }),
                new ChartSeries("帧率", new double?[] { 55d, 70d }),
            },
            includeZero: false);

        Assert.Multiple(() =>
        {
            Assert.That(range.Min, Is.EqualTo(12d));
            Assert.That(range.Max, Is.EqualTo(70d));
        });
    }

    [Test]
    public void ResolveRange_ExplicitBoundsWin()
    {
        var range = CurveMath.ResolveRange(
            new[] { new ChartSeries("帧耗时", new double?[] { 12d, 18d }) },
            includeZero: false,
            min: 0d,
            max: 33d);

        Assert.Multiple(() =>
        {
            Assert.That(range.Min, Is.EqualTo(0d));
            Assert.That(range.Max, Is.EqualTo(33d));
        });
    }

    [Test]
    public void ResolveRange_FlatSeries_GetsAVisibleBand()
    {
        var range = CurveMath.ResolveRange(
            new[] { new ChartSeries("帧耗时", new double?[] { 16d, 16d, 16d }) },
            includeZero: false);

        Assert.Multiple(() =>
        {
            Assert.That(range.HasData, Is.True);
            Assert.That(range.Max - range.Min, Is.GreaterThan(0d), "全平数据必须给出一条可见的线，否则所有点贴在底边");
            Assert.That(range.Min, Is.LessThan(16d));
            Assert.That(range.Max, Is.GreaterThan(16d));
        });
    }

    [Test]
    public void ResolveRange_FlatZeroSeries_GetsUnitBand()
    {
        var range = CurveMath.ResolveRange(
            new[] { new ChartSeries("零", new double?[] { 0d, 0d }) },
            includeZero: false);

        Assert.Multiple(() =>
        {
            Assert.That(range.Min, Is.EqualTo(-1d));
            Assert.That(range.Max, Is.EqualTo(1d));
        });
    }

    [Test]
    public void Project_NullAndNonFiniteValues_StayNull()
    {
        var plot = new Rect(0d, 0d, 100d, 50d);
        var points = CurveMath.Project(new double?[] { 1d, null, double.NaN, double.PositiveInfinity, 5d }, plot, new CurveRange(0d, 5d, true));

        Assert.Multiple(() =>
        {
            Assert.That(points, Has.Count.EqualTo(5));
            Assert.That(points[0], Is.Not.Null);
            Assert.That(points[1], Is.Null, "缺席的点必须断线，不能补 0");
            Assert.That(points[2], Is.Null);
            Assert.That(points[3], Is.Null);
            Assert.That(points[4], Is.Not.Null);
        });
    }

    [Test]
    public void Project_SpreadsPointsAcrossThePlotWidth()
    {
        var plot = new Rect(10d, 20d, 80d, 40d);
        var points = CurveMath.Project(new double?[] { 0d, 5d, 10d }, plot, new CurveRange(0d, 10d, true));

        Assert.Multiple(() =>
        {
            Assert.That(points[0]!.Value.X, Is.EqualTo(10d), "首点贴左边界");
            Assert.That(points[1]!.Value.X, Is.EqualTo(50d), "中点居中");
            Assert.That(points[2]!.Value.X, Is.EqualTo(90d), "末点贴右边界");
            Assert.That(points[0]!.Value.Y, Is.EqualTo(60d), "最小值贴底边");
            Assert.That(points[2]!.Value.Y, Is.EqualTo(20d), "最大值贴顶边");
        });
    }

    [Test]
    public void Project_SingleValue_CollapsesToTheLeftEdge()
    {
        var plot = new Rect(0d, 0d, 100d, 40d);
        var points = CurveMath.Project(new double?[] { 5d }, plot, new CurveRange(0d, 10d, true));

        Assert.That(points, Has.Count.EqualTo(1));
        Assert.That(points[0]!.Value.X, Is.EqualTo(0d), "只有一个点时没有横向跨度，贴左边而不是除以零");
    }

    [Test]
    public void Project_OutOfRangeValues_AreClampedIntoThePlot()
    {
        var plot = new Rect(0d, 0d, 100d, 50d);
        var points = CurveMath.Project(new double?[] { -10d, 20d }, plot, new CurveRange(0d, 10d, true));

        Assert.Multiple(() =>
        {
            Assert.That(points[0]!.Value.Y, Is.EqualTo(50d), "低于下界的点夹在底边，不画到图外");
            Assert.That(points[1]!.Value.Y, Is.EqualTo(0d));
        });
    }

    [Test]
    public void Project_EmptyInput_ReturnsEmpty()
    {
        Assert.That(CurveMath.Project(Array.Empty<double?>(), new Rect(0d, 0d, 10d, 10d), new CurveRange(0d, 1d, true)), Is.Empty);
        Assert.That(CurveMath.Project(null, new Rect(0d, 0d, 10d, 10d), new CurveRange(0d, 1d, true)), Is.Empty);
    }
}
