using System.Linq;
using CkanUI.App.Controls;
using NUnit.Framework;

namespace CkanUI.App.Tests;

/// <summary>拖动细线的采样、轨道特征节点与淡出规则。</summary>
[TestFixture]
public sealed class DragTrailModelTests
{
    private static DragTrailModel Drawn(params (double X, double Y)[] points)
    {
        var model = new DragTrailModel();
        model.Begin(points[0].X, points[0].Y);
        for (var i = 1; i < points.Length; i++)
        {
            model.Move(points[i].X, points[i].Y);
        }

        return model;
    }

    [Test]
    public void Begin_StartsDraggingWithASinglePoint()
    {
        var model = new DragTrailModel();
        model.Begin(10, 20);

        Assert.That(model.IsDragging, Is.True);
        Assert.That(model.Count, Is.EqualTo(1));
        Assert.That(model.Intensity, Is.EqualTo(1d));
        Assert.That(model.PointX(0), Is.EqualTo(10d));
        Assert.That(model.PointY(0), Is.EqualTo(20d));
    }

    [Test]
    public void Move_RecordsPointsThatAreFarEnoughApart()
    {
        var model = Drawn((0, 0), (1, 0), (5, 0), (5.4, 0), (9, 0));

        // 1.0 与 0.4 的位移都小于 MinSampleDistance(1.2)，被丢掉。
        Assert.That(model.Count, Is.EqualTo(3));
        Assert.That(model.PointX(2), Is.EqualTo(9d));
    }

    [Test]
    public void Move_IsIgnoredBeforeBeginAndAfterEnd()
    {
        var model = new DragTrailModel();
        model.Move(1, 1);
        Assert.That(model.Count, Is.Zero);

        model.Begin(0, 0);
        model.Move(10, 10);
        model.End();
        var afterRelease = model.Count;
        model.Move(30, 30);
        Assert.That(model.Count, Is.EqualTo(afterRelease));
    }

    [Test]
    public void End_StartsFadeAndKeepsTheLine()
    {
        var model = Drawn((0, 0), (20, 0));
        model.End();

        Assert.That(model.IsDragging, Is.False);
        Assert.That(model.Count, Is.EqualTo(2));
        Assert.That(model.Intensity, Is.EqualTo(1d));
    }

    [Test]
    public void Advance_FadesOnlyAfterRelease()
    {
        var model = Drawn((0, 0), (20, 0));
        model.Advance(0.2);
        Assert.That(model.Intensity, Is.EqualTo(1d), "拖动中不该淡出");

        model.End();
        model.Advance(DragTrailModel.FadeSeconds * 0.5d);
        Assert.That(model.Intensity, Is.InRange(0.4d, 0.6d));
        Assert.That(model.IsActive, Is.True);
    }

    [Test]
    public void Advance_ClearsTheLineAfterTheFadeWindow()
    {
        var model = Drawn((0, 0), (20, 0));
        model.End();
        for (var i = 0; i < 10; i++)
        {
            model.Advance(DragTrailModel.MaxStepSeconds);
        }

        Assert.That(model.IsActive, Is.False);
        Assert.That(model.Count, Is.Zero);
    }

    [Test]
    public void Advance_ClampsLargeSteps()
    {
        var model = Drawn((0, 0), (20, 0));
        model.End();
        model.Advance(10d);

        Assert.That(model.Intensity, Is.EqualTo(1d - DragTrailModel.MaxStepSeconds / DragTrailModel.FadeSeconds).Within(1e-9));
    }

    [Test]
    public void Begin_ReplacesThePreviousTrail()
    {
        var model = Drawn((0, 0), (10, 0), (20, 0));
        model.Begin(100, 100);

        Assert.That(model.Count, Is.EqualTo(1));
        Assert.That(model.PointX(0), Is.EqualTo(100d));
    }

    [Test]
    public void Clear_DropsEverything()
    {
        var model = Drawn((0, 0), (10, 0));
        model.Clear();

        Assert.That(model.IsActive, Is.False);
        Assert.That(model.IsDragging, Is.False);
        Assert.That(model.Count, Is.Zero);
    }

    [Test]
    public void NonFiniteInput_IsIgnored()
    {
        var model = new DragTrailModel();
        model.Begin(double.NaN, 0);
        Assert.That(model.IsDragging, Is.False);

        model.Begin(0, 0);
        model.Move(double.PositiveInfinity, 2);
        Assert.That(model.Count, Is.EqualTo(1));

        model.Advance(double.NaN);
        Assert.That(model.Intensity, Is.EqualTo(1d));
    }

    [Test]
    public void Move_SpawnsAtMostFourNodesAndNeverRepeatsType()
    {
        var model = new DragTrailModel(seed: 42);
        model.Begin(0, 0);

        // 超长划线 2000 DIP，确认最多且只会生成 4 个节点（Ap, Pe, An, Dn 各一次）
        for (var x = 10; x <= 2000; x += 10)
        {
            model.Move(x, 0);
        }

        Assert.That(model.Nodes.Count, Is.EqualTo(4), "4个节点应各且仅出现一次");

        var distinctTypes = model.Nodes.Select(n => n.Type).Distinct().ToList();
        Assert.That(distinctTypes.Count, Is.EqualTo(4), "所有节点类型应互不相同");
        Assert.That(distinctTypes, Does.Contain(OrbitalNodeType.Ap));
        Assert.That(distinctTypes, Does.Contain(OrbitalNodeType.Pe));
        Assert.That(distinctTypes, Does.Contain(OrbitalNodeType.An));
        Assert.That(distinctTypes, Does.Contain(OrbitalNodeType.Dn));
    }

    [Test]
    public void Nodes_PopInAnimationAdvancesWithTime()
    {
        var model = new DragTrailModel(seed: 123);
        model.Begin(0, 0);

        for (var x = 10; x <= 300; x += 10)
        {
            model.Move(x, 0);
        }

        Assert.That(model.Nodes.Count, Is.GreaterThanOrEqualTo(1));
        var firstNode = model.Nodes[0];
        Assert.That(firstNode.AgeSeconds, Is.Zero);
        Assert.That(firstNode.PopScale, Is.LessThan(0.5d));

        // 推进 0.22 秒，弹出动画应稳定到 1.0
        model.Advance(0.25d);
        Assert.That(firstNode.AgeSeconds, Is.GreaterThanOrEqualTo(0.25d));
        Assert.That(firstNode.PopScale, Is.EqualTo(1.0d).Within(1e-6));
        Assert.That(firstNode.PopAlpha, Is.EqualTo(1.0d));
    }

    [Test]
    public void Nodes_ClearWhenTrailIsClearedOrRestarted()
    {
        var model = new DragTrailModel(seed: 42);
        model.Begin(0, 0);
        for (var x = 10; x <= 600; x += 10)
        {
            model.Move(x, 0);
        }
        Assert.That(model.Nodes.Count, Is.GreaterThan(0));

        model.Clear();
        Assert.That(model.Nodes.Count, Is.Zero);

        model.Begin(100, 100);
        Assert.That(model.Nodes.Count, Is.Zero);
    }
}
