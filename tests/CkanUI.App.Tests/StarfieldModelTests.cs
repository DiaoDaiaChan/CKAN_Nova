using CkanUI.App.Controls;
using NUnit.Framework;

namespace CkanUI.App.Tests;

/// <summary>
/// 星空背景的数值模型：确定性布局、尺寸映射、光标影响场、时钟推进的边界。
/// </summary>
/// <remarks>
/// 模型不碰渲染也不碰计时器，所以全部可以在无头环境里直接断言；
/// 渲染侧的画笔/帧率由 <c>MainWindow</c> 实机帧验证，不在这里做像素断言。
/// </remarks>
[TestFixture]
public sealed class StarfieldModelTests
{
    [Test]
    public void Model_WithTheSameSeedProducesTheSameSky()
    {
        var left = new StarfieldModel();
        var right = new StarfieldModel();

        Assert.That(left.StarCount, Is.EqualTo(StarfieldModel.DefaultStarCount));
        Assert.That(left.FigureCount, Is.EqualTo(StarfieldModel.DefaultConstellationCount));
        Assert.That(right.StarCount, Is.EqualTo(left.StarCount));

        for (var i = 0; i < left.StarCount; i++)
        {
            Assert.That(right.Stars[i], Is.EqualTo(left.Stars[i]), $"第 {i} 颗星必须可复现");
        }
    }

    [Test]
    public void Model_DifferentSeedMovesTheSky()
    {
        var left = new StarfieldModel();
        var right = new StarfieldModel(seed: StarfieldModel.DefaultSeed + 1);

        var moved = 0;
        for (var i = 0; i < left.StarCount; i++)
        {
            if (Math.Abs(right.Stars[i].NormalizedX - left.Stars[i].NormalizedX) > 1e-9)
            {
                moved++;
            }
        }

        Assert.That(moved, Is.GreaterThan(0), "换种子必须换布局，否则种子只是摆设");
    }

    [Test]
    public void Model_RaisesStarCountToTheFloor()
    {
        var model = new StarfieldModel(starCount: 1, constellationCount: 0);

        Assert.That(model.StarCount, Is.EqualTo(StarfieldModel.MinStarCount));
        Assert.That(model.FigureCount, Is.EqualTo(0));
    }

    [Test]
    public void Model_KeepsEnoughAmbientStarsWhenFiguresWouldEatTheBudget()
    {
        var model = new StarfieldModel(starCount: StarfieldModel.MinStarCount, constellationCount: StarfieldModel.MaxConstellationCount);

        Assert.That(model.StarCount, Is.EqualTo(StarfieldModel.MinStarCount));
        // 48 颗星里留出 MinAmbientStars=24 之后，星座最多只放得下 4 组。
        Assert.That(model.FigureCount, Is.EqualTo(4));
        Assert.That(model.FigureCount * StarfieldModel.NodesPerFigure, Is.LessThan(model.StarCount));
    }

    [Test]
    public void Model_ClampsConstellationCountToTheCeiling()
    {
        var model = new StarfieldModel(constellationCount: 99);

        Assert.That(model.FigureCount, Is.EqualTo(StarfieldModel.MaxConstellationCount));
    }

    [Test]
    public void Figures_AreGroupsOfFiveNodesWithValidEdges()
    {
        var model = new StarfieldModel();

        foreach (var figure in model.Figures)
        {
            Assert.That(figure.Nodes, Has.Count.EqualTo(StarfieldModel.NodesPerFigure));
            Assert.That(figure.Edges, Has.Count.InRange(4, 7));
            Assert.That(figure.PeriodSeconds, Is.GreaterThan(0));
            foreach (var edge in figure.Edges)
            {
                Assert.That(edge.From, Is.InRange(0, StarfieldModel.NodesPerFigure - 1));
                Assert.That(edge.To, Is.InRange(0, StarfieldModel.NodesPerFigure - 1));
                Assert.That(edge.To, Is.Not.EqualTo(edge.From), "星座连线不能自环");
            }

            foreach (var node in figure.Nodes)
            {
                Assert.That(node, Is.InRange(0, model.StarCount - 1));
            }
        }
    }

    [Test]
    public void Figures_DrawFromMoreThanOneTopology()
    {
        var model = new StarfieldModel(seed: StarfieldModel.DefaultSeed);

        var shapes = new HashSet<string>(StringComparer.Ordinal);
        foreach (var figure in model.Figures)
        {
            shapes.Add(string.Join(',', figure.Edges.Select(edge => edge.From + "-" + edge.To)));
        }

        Assert.That(
            shapes.Count,
            Is.GreaterThan(1),
            "七颗星座共用同一张连线模板就谈不上「每次启动样式不一样」");
    }

    [Test]
    public void DefaultModels_ShareThePerLaunchSeed()
    {
        var left = new StarfieldModel();
        var right = new StarfieldModel();

        Assert.That(left.Seed, Is.EqualTo(right.Seed), "同一进程内的默认实例必须共用启动种子，背景才不会来回换图");
        Assert.That(left.Seed, Is.EqualTo(StarfieldModel.LaunchSeedValue));
    }

    [Test]
    public void ExplicitSeed_OverridesTheLaunchSeed()
    {
        var model = new StarfieldModel(seed: 0x1234);
        var twin = new StarfieldModel(seed: 0x1234);

        Assert.That(model.Seed, Is.EqualTo(0x1234));
        for (var i = 0; i < model.StarCount; i++)
        {
            Assert.That(twin.Stars[i], Is.EqualTo(model.Stars[i]));
        }
    }

    [Test]
    public void Layout_MapsNormalizedCoordinatesToPixels()
    {
        var model = new StarfieldModel();
        model.Layout(1280, 800);

        Assert.That(model.Width, Is.EqualTo(1280));
        Assert.That(model.Height, Is.EqualTo(800));
        for (var i = 0; i < model.StarCount; i++)
        {
            Assert.That(model.StarX(i), Is.EqualTo(model.Stars[i].NormalizedX * 1280).Within(1e-6));
            Assert.That(model.StarY(i), Is.EqualTo(model.Stars[i].NormalizedY * 800).Within(1e-6));
        }
    }

    [Test]
    public void Layout_TreatsIllegalSizesAsZero()
    {
        var model = new StarfieldModel();
        model.Layout(1280, 800);

        model.Layout(double.NaN, -50);

        Assert.That(model.Width, Is.EqualTo(0));
        Assert.That(model.Height, Is.EqualTo(0));
    }

    [Test]
    public void SetPointer_IgnoresNonFiniteCoordinates()
    {
        var model = new StarfieldModel();
        model.Layout(1280, 800);

        model.SetPointer(double.NaN, 10);
        model.SetPointer(10, double.PositiveInfinity);

        Assert.That(model.HasPointer, Is.False);
    }

    [Test]
    public void RefreshPointer_HighlightsOnlyTheNeighbourhood()
    {
        var model = new StarfieldModel();
        model.Layout(1280, 800);

        model.SetPointer(model.StarX(0), model.StarY(0));
        model.RefreshPointer();

        Assert.That(model.HasPointer, Is.True);
        Assert.That(model.PointerStrength, Is.EqualTo(1).Within(1e-6));
        Assert.That(model.StarInfluence(0), Is.EqualTo(1).Within(1e-6), "光标压在星点上时影响必须拉满");

        // 找一颗离光标远超 InfluenceRadius 的星，它必须完全不受影响。
        var far = -1;
        for (var i = 0; i < model.StarCount; i++)
        {
            var dx = model.StarX(i) - model.StarX(0);
            var dy = model.StarY(i) - model.StarY(0);
            if (Math.Sqrt((dx * dx) + (dy * dy)) > StarfieldModel.InfluenceRadius * 2)
            {
                far = i;
                break;
            }
        }

        Assert.That(far, Is.GreaterThanOrEqualTo(0), "默认星空里应该找得到远处的星");
        Assert.That(model.StarInfluence(far), Is.EqualTo(0));
        Assert.That(model.StarOffsetX(far), Is.EqualTo(0));
        Assert.That(model.StarOffsetY(far), Is.EqualTo(0));
    }

    [Test]
    public void RefreshPointer_ConnectsAtMostThreeNodes()
    {
        var model = new StarfieldModel();
        model.Layout(1280, 800);

        model.SetPointer(model.StarX(0), model.StarY(0));
        model.RefreshPointer();

        Assert.That(model.PointerLinks, Has.Count.EqualTo(StarfieldModel.PointerLinkCount));
        Assert.That(model.PointerLinks, Is.Unique);
    }

    [Test]
    public void ClearPointer_DecaysHighlightsAndLinks()
    {
        var model = new StarfieldModel();
        model.Layout(1280, 800);
        model.SetPointer(model.StarX(0), model.StarY(0));
        model.RefreshPointer();
        Assert.That(model.PointerLinks, Is.Not.Empty);

        model.ClearPointer();
        for (var i = 0; i < 12; i++)
        {
            model.Advance(0.1);
        }

        Assert.That(model.HasPointer, Is.False);
        Assert.That(model.PointerStrength, Is.LessThan(0.01));
        Assert.That(model.PointerLinks, Is.Empty, "光标离开后不能再挂着牵引线");
        Assert.That(model.StarOffsetX(0), Is.EqualTo(0));
        Assert.That(model.StarInfluence(0), Is.EqualTo(0));
    }

    [Test]
    public void Advance_RejectsIllegalDeltasAndCapsTheStep()
    {
        var model = new StarfieldModel();

        model.Advance(double.NaN);
        model.Advance(-1);
        model.Advance(0);

        Assert.That(model.Time, Is.EqualTo(0));

        // 单帧最多 0.1 秒：窗口被拖住再回来，星空不能瞬移。
        model.Advance(5);
        Assert.That(model.Time, Is.EqualTo(0.1).Within(1e-9));
    }

    [Test]
    public void Advance_EasesThePointerTowardsTheTarget()
    {
        var model = new StarfieldModel();
        model.Layout(1280, 800);
        model.SetPointer(640, 400);
        model.Advance(0.1);

        Assert.That(model.PointerX, Is.GreaterThan(0));
        Assert.That(model.PointerX, Is.LessThan(640));
        Assert.That(model.PointerStrength, Is.InRange(0d, 1d));
    }

    [Test]
    public void StarAlpha_StaysWithinTheUnitInterval()
    {
        var model = new StarfieldModel();
        model.Layout(1280, 800);
        model.SetPointer(model.StarX(0), model.StarY(0));

        for (var frame = 0; frame < 40; frame++)
        {
            model.Advance(0.1);
            for (var i = 0; i < model.StarCount; i++)
            {
                Assert.That(model.StarAlpha(i), Is.InRange(0d, 1d));
                Assert.That(model.StarRadius(i), Is.GreaterThan(0));
            }
        }
    }

    [Test]
    public void FigureAlpha_NeverBreaksTheCeiling()
    {
        var model = new StarfieldModel();
        model.Layout(1280, 800);
        var node = model.Figures[0].Nodes[0];
        model.SetPointer(model.StarX(node), model.StarY(node));
        model.RefreshPointer();

        for (var frame = 0; frame < 40; frame++)
        {
            model.Advance(0.1);
            for (var f = 0; f < model.FigureCount; f++)
            {
                Assert.That(model.FigureAlpha(f), Is.InRange(0d, 0.6));
                Assert.That(model.FigureInfluence(f), Is.InRange(0d, 1d));
            }
        }
    }

    [Test]
    public void FigureAlpha_BreathesAcrossThePeriod()
    {
        var model = new StarfieldModel();
        var min = double.MaxValue;
        var max = double.MinValue;

        for (var frame = 0; frame < 200; frame++)
        {
            model.Advance(0.1);
            for (var f = 0; f < model.FigureCount; f++)
            {
                var alpha = model.FigureAlpha(f);
                min = Math.Min(min, alpha);
                max = Math.Max(max, alpha);
            }
        }

        Assert.That(max - min, Is.GreaterThan(0.15), "星座必须看得出来在缓慢明灭");
        Assert.That(max, Is.LessThanOrEqualTo(0.6));
    }

    [Test]
    public void CornerArcRadius_GrowsOutwardFromTheCorner()
    {
        var model = new StarfieldModel();
        const double unit = 720;

        Assert.That(model.CornerArcRadius(0, unit), Is.EqualTo(0.12 * unit).Within(1e-9));
        Assert.That(
            model.CornerArcRadius(StarfieldModel.CornerArcCount - 1, unit),
            Is.EqualTo(0.72 * unit).Within(1e-9));

        for (var index = 1; index < StarfieldModel.CornerArcCount; index++)
        {
            Assert.That(
                model.CornerArcRadius(index, unit),
                Is.GreaterThan(model.CornerArcRadius(index - 1, unit)));
        }
    }

    [Test]
    public void CornerPole_SitsOutsideTheCanvasAndFollowsTheSize()
    {
        var model = new StarfieldModel();

        Assert.That(model.CornerPoleX(800), Is.EqualTo(-48).Within(1e-9));
        Assert.That(model.CornerPoleY(600), Is.EqualTo(-60).Within(1e-9));
        Assert.That(model.CornerPoleX(1600), Is.EqualTo(model.CornerPoleX(800) * 2).Within(1e-9));
        Assert.That(model.CornerPoleY(1200), Is.EqualTo(model.CornerPoleY(600) * 2).Within(1e-9));
        Assert.That(model.CornerPoleX(double.NaN), Is.EqualTo(0).Within(1e-9));
        Assert.That(model.CornerPoleY(-10), Is.EqualTo(0).Within(1e-9));
    }

    [Test]
    public void CornerArcAlpha_StaysUnderTheCeilingAndFadesOutward()
    {
        var model = new StarfieldModel();
        var previous = double.MaxValue;

        for (var index = 0; index < StarfieldModel.CornerArcCount; index++)
        {
            var alpha = model.CornerArcAlpha(index);
            Assert.That(alpha, Is.GreaterThan(0));
            Assert.That(alpha, Is.LessThanOrEqualTo(StarfieldModel.CornerArcAlphaCeiling));
            Assert.That(alpha, Is.LessThanOrEqualTo(previous));
            previous = alpha;
        }
    }

    [Test]
    public void CornerArcBreath_StaysSubtleAndVariesOverTime()
    {
        var model = new StarfieldModel();
        var min = double.MaxValue;
        var max = double.MinValue;

        for (var frame = 0; frame < 200; frame++)
        {
            model.Advance(0.1);
            for (var arc = 0; arc < StarfieldModel.CornerArcCount; arc++)
            {
                var factor = model.CornerArcBreath(arc);
                Assert.That(factor, Is.InRange(1 - StarfieldModel.CornerArcBreathDepth, 1));
                min = Math.Min(min, factor);
                max = Math.Max(max, factor);
            }
        }

        Assert.That(max - min, Is.GreaterThan(0.12), "起伏幅度小于 0.12 就等于没有呼吸");
    }

    [Test]
    public void CornerArcAlphaTimesBreath_NeverBreaksTheCeiling()
    {
        var model = new StarfieldModel();

        for (var frame = 0; frame < 150; frame++)
        {
            model.Advance(0.1);
            for (var arc = 0; arc < StarfieldModel.CornerArcCount; arc++)
            {
                Assert.That(
                    model.CornerArcAlpha(arc) * model.CornerArcBreath(arc),
                    Is.LessThanOrEqualTo(StarfieldModel.CornerArcAlphaCeiling));
            }
        }
    }

    [Test]
    public void TrailBreath_StaysSubtleAndVariesOverTime()
    {
        var model = new StarfieldModel();
        var min = double.MaxValue;
        var max = double.MinValue;

        for (var frame = 0; frame < 200; frame++)
        {
            model.Advance(0.1);
            var factor = model.TrailBreath();
            Assert.That(factor, Is.InRange(1 - StarfieldModel.TrailBreathDepth, 1));
            min = Math.Min(min, factor);
            max = Math.Max(max, factor);
        }

        Assert.That(max - min, Is.GreaterThan(0.15));
    }

    [Test]
    public void BreathFactors_StopMovingWhenTimeStops()
    {
        var model = new StarfieldModel();
        model.Advance(3.7);
        var arc = model.CornerArcBreath(2);
        var trail = model.TrailBreath();
        var figure = model.FigureAlpha(0);

        Assert.That(model.CornerArcBreath(2), Is.EqualTo(arc));
        Assert.That(model.TrailBreath(), Is.EqualTo(trail));
        Assert.That(model.FigureAlpha(0), Is.EqualTo(figure));
    }

    [Test]
    public void CornerArcSweep_StaysInsideTheVisibleArc()
    {
        var model = new StarfieldModel();

        for (var index = 0; index < StarfieldModel.CornerArcCount; index++)
        {
            Assert.That(model.CornerArcSweep(index), Is.InRange(0.3, 0.8));
        }
    }

    [Test]
    public void CornerArcs_RevolveTogetherWithASmallParallax()
    {
        var model = new StarfieldModel();
        var evenBefore = model.CornerArcStart(0);
        var oddBefore = model.CornerArcStart(1);
        var gapBefore = evenBefore - oddBefore;

        // Advance 单帧最多推进 0.1s，这里推进 100 帧拿到 10s 的转动量。
        for (var frame = 0; frame < 100; frame++)
        {
            model.Advance(0.1);
        }

        var evenTurned = model.CornerArcStart(0) - evenBefore;
        var oddTurned = model.CornerArcStart(1) - oddBefore;

        // 整族同向转动：两条都往正方向走，量级在 0.012 ± 0.0035 弧度/秒。
        Assert.That(evenTurned, Is.EqualTo((StarfieldModel.CornerArcRotationRate + StarfieldModel.CornerArcDrift) * 10).Within(1e-6));
        Assert.That(oddTurned, Is.EqualTo((StarfieldModel.CornerArcRotationRate - StarfieldModel.CornerArcDrift) * 10).Within(1e-6));
        Assert.That(evenTurned, Is.GreaterThan(0));
        Assert.That(oddTurned, Is.GreaterThan(0));

        // 视差只让间距随秒数缓慢拉开 2×Drift×t，绝不允许出现某条反向倒转。
        var gapGrowth = (model.CornerArcStart(0) - model.CornerArcStart(1)) - gapBefore;
        Assert.That(gapGrowth, Is.EqualTo(2 * StarfieldModel.CornerArcDrift * 10).Within(1e-6));
    }

    [Test]
    public void CornerArcRevolution_StaysSlowEnoughToReadAsDrift()
    {
        // 一整圈 8–9 分钟：比这更快就成了转盘，比这更慢十秒内看不出动。
        Assert.That(StarfieldModel.CornerArcRevolutionSeconds, Is.InRange(420, 600));
    }

    [Test]
    public void MeteorSchedule_PutsOneMeteorInFlightAtTimeZero()
    {
        var model = new StarfieldModel();

        Assert.That(model.CountActiveMeteors(0), Is.EqualTo(1));
        Assert.That(model.TryGetMeteor(0, 0, out var meteor), Is.True);
        Assert.That(meteor.HeadAlpha, Is.GreaterThan(0));
    }

    [Test]
    public void MeteorSchedule_NeverRunsMoreThanTwoAtOnce()
    {
        var model = new StarfieldModel();

        for (var step = 0; step <= 1200; step++)
        {
            Assert.That(model.CountActiveMeteors(step * 0.05), Is.LessThanOrEqualTo(2));
        }
    }

    [Test]
    public void TryGetMeteor_IsDeterministicForTheSameInputs()
    {
        var model = new StarfieldModel();

        Assert.That(model.TryGetMeteor(0, 0.9, out var first), Is.True);
        Assert.That(model.TryGetMeteor(0, 0.9, out var second), Is.True);
        Assert.Multiple(() =>
        {
            Assert.That(second.TailX, Is.EqualTo(first.TailX));
            Assert.That(second.TailY, Is.EqualTo(first.TailY));
            Assert.That(second.HeadX, Is.EqualTo(first.HeadX));
            Assert.That(second.HeadY, Is.EqualTo(first.HeadY));
            Assert.That(second.HeadAlpha, Is.EqualTo(first.HeadAlpha));
            Assert.That(second.TailAlpha, Is.EqualTo(first.TailAlpha));
        });
        Assert.That(second, Is.EqualTo(first));
    }

    [Test]
    public void TryGetMeteor_RejectsNonFiniteTimeAndOutOfRangeIndex()
    {
        var model = new StarfieldModel();

        Assert.That(model.TryGetMeteor(0, double.NaN, out var nanState), Is.False);
        Assert.That(nanState, Is.EqualTo(default(MeteorState)));
        Assert.That(model.TryGetMeteor(0, double.PositiveInfinity, out _), Is.False);
        Assert.That(model.TryGetMeteor(0, double.NegativeInfinity, out _), Is.False);
        Assert.That(model.TryGetMeteor(-1, 0, out _), Is.False);
        Assert.That(model.TryGetMeteor(StarfieldModel.MeteorScheduleCount, 0, out _), Is.False);
    }

    [Test]
    public void TryGetMeteor_KeepsHeadBrightAndTailBehindIt()
    {
        var model = new StarfieldModel();

        Assert.That(model.TryGetMeteor(0, 0, out var meteor), Is.True);
        var tailToHead = Math.Sqrt(
            ((meteor.HeadX - meteor.TailX) * (meteor.HeadX - meteor.TailX))
            + ((meteor.HeadY - meteor.TailY) * (meteor.HeadY - meteor.TailY)));

        Assert.That(meteor.HeadAlpha, Is.GreaterThan(0));
        Assert.That(meteor.HeadAlpha, Is.LessThanOrEqualTo(0.55));
        Assert.That(meteor.TailAlpha, Is.EqualTo(meteor.HeadAlpha * 0.3).Within(1e-9));
        Assert.That(tailToHead, Is.GreaterThan(0));
    }

    [Test]
    public void TryGetMeteor_ReturnsFalseOnceTheWindowHasPassed()
    {
        var model = new StarfieldModel();

        Assert.That(model.TryGetMeteor(0, 0.9, out _), Is.True);
        Assert.That(model.TryGetMeteor(0, 0.94, out var expired), Is.False);
        Assert.That(expired, Is.EqualTo(default(MeteorState)));
    }

    [Test]
    public void MeteorSchedule_SurvivesNegativeAndAstronomicalTimes()
    {
        var model = new StarfieldModel();

        Assert.That(model.CountActiveMeteors(-1.25), Is.EqualTo(1));
        Assert.That(model.CountActiveMeteors(1e7), Is.InRange(0, StarfieldModel.MeteorScheduleCount));
        Assert.That(model.CountActiveMeteors(double.NaN), Is.EqualTo(0));
        Assert.That(model.CountActiveMeteors(double.PositiveInfinity), Is.EqualTo(0));
    }
}
