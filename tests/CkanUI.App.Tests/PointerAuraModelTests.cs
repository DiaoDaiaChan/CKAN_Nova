using System;

using CkanUI.App.Controls;

using NUnit.Framework;

namespace CkanUI.App.Tests;

/// <summary>
/// 指针光环的物理量：半径、淡入淡出、径向衰减、颗粒确定性。
/// 渲染层在无头环境里拍不出「光标悬停」这一帧，所以把可验证的部分全部压到模型上。
/// </summary>
[TestFixture]
public sealed class PointerAuraModelTests
{
    [Test]
    public void Track_RaisesTheTargetAndStartsFadingIn()
    {
        var model = new PointerAuraModel();
        Assert.That(model.IsActive, Is.False);

        model.Track(120d, 80d);
        Assert.That(model.HasTarget, Is.True);
        Assert.That(model.X, Is.EqualTo(120d));
        Assert.That(model.Y, Is.EqualTo(80d));

        model.Advance(PointerAuraModel.FadeInSeconds / 2d);
        Assert.That(model.Alpha, Is.InRange(0.4d, 0.6d));
    }

    [Test]
    public void Advance_ReachesFullIntensityWithinTheFadeInWindow()
    {
        var model = new PointerAuraModel();
        model.Track(0d, 0d);
        for (var i = 0; i < 20; i++)
        {
            model.Advance(0.02d);
        }

        Assert.That(model.Alpha, Is.EqualTo(1d).Within(0.001d));
    }

    [Test]
    public void Clear_FadesOutAndGoesInactive()
    {
        var model = new PointerAuraModel();
        model.Track(0d, 0d);
        model.Advance(1d);
        Assert.That(model.Alpha, Is.EqualTo(1d));

        model.Clear();
        Assert.That(model.HasTarget, Is.False);
        for (var i = 0; i < 40; i++)
        {
            model.Advance(0.02d);
        }

        Assert.That(model.Alpha, Is.EqualTo(0d));
        Assert.That(model.IsActive, Is.False);
    }

    [Test]
    public void Advance_ClampsHugeDeltasSoTheAuraDoesNotJump()
    {
        var model = new PointerAuraModel();
        model.Track(0d, 0d);
        model.Advance(30d);

        Assert.That(model.Alpha, Is.LessThanOrEqualTo(1d));
        Assert.That(model.Alpha, Is.GreaterThan(0d));
    }

    [Test]
    public void Advance_IgnoresNonPositiveAndNonFiniteDeltas()
    {
        var model = new PointerAuraModel();
        model.Track(0d, 0d);
        model.Advance(0d);
        model.Advance(-1d);
        model.Advance(double.NaN);

        Assert.That(model.Alpha, Is.EqualTo(0d));
    }

    [Test]
    public void Clear_KeepsTheLastPositionSoTheGlowDiesInPlace()
    {
        var model = new PointerAuraModel();
        model.Track(10d, 24d);
        model.Advance(1d);
        model.Clear();

        Assert.That(model.X, Is.EqualTo(10d));
        Assert.That(model.Y, Is.EqualTo(24d));
        Assert.That(model.HasTarget, Is.False);
    }

    [Test]
    public void Track_IgnoresNonFiniteCoordinates()
    {
        var model = new PointerAuraModel();
        model.Track(double.NaN, 5d);
        model.Track(5d, double.PositiveInfinity);

        Assert.That(model.HasTarget, Is.False);
        Assert.That(model.X, Is.EqualTo(0d));
        Assert.That(model.Y, Is.EqualTo(0d));
    }

    [Test]
    public void Falloff_IsOneAtTheCentreAndZeroAtTheRim()
    {
        Assert.That(PointerAuraModel.Falloff(0d, 100d), Is.EqualTo(1d));
        Assert.That(PointerAuraModel.Falloff(100d, 100d), Is.EqualTo(0d));
        Assert.That(PointerAuraModel.Falloff(200d, 100d), Is.EqualTo(0d));
    }

    [Test]
    public void Falloff_DecreasesMonotonically()
    {
        var previous = double.MaxValue;
        for (var distance = 0d; distance <= 168d; distance += 4d)
        {
            var value = PointerAuraModel.Falloff(distance, PointerAuraModel.Radius);
            Assert.That(value, Is.LessThanOrEqualTo(previous + 1e-9));
            previous = value;
        }
    }

    [Test]
    public void Falloff_SmoothstepHasNoKinkAtTheEdge()
    {
        // 边界附近应该平滑收尾：0.98r 处还留一点点亮，1.02r 处已经完全为 0。
        var nearEdge = PointerAuraModel.Falloff(PointerAuraModel.Radius * 0.98d, PointerAuraModel.Radius);
        var pastEdge = PointerAuraModel.Falloff(PointerAuraModel.Radius * 1.02d, PointerAuraModel.Radius);

        Assert.That(nearEdge, Is.GreaterThan(0d));
        Assert.That(nearEdge, Is.LessThan(0.01d));
        Assert.That(pastEdge, Is.EqualTo(0d));
    }

    [Test]
    public void Falloff_RejectsDegenerateRadius()
    {
        Assert.That(PointerAuraModel.Falloff(10d, 0d), Is.EqualTo(0d));
        Assert.That(PointerAuraModel.Falloff(double.NaN, 100d), Is.EqualTo(0d));
    }

    [Test]
    public void TextureSample_IsDeterministic()
    {
        for (var column = -20; column < 20; column++)
        {
            for (var row = -20; row < 20; row++)
            {
                Assert.That(
                    PointerAuraModel.TextureSample(column, row),
                    Is.EqualTo(PointerAuraModel.TextureSample(column, row)));
            }
        }
    }

    [Test]
    public void TextureSample_StaysInRangeAndActuallyVaries()
    {
        var distinct = new System.Collections.Generic.HashSet<int>();
        for (var column = 0; column < 40; column++)
        {
            for (var row = 0; row < 40; row++)
            {
                var value = PointerAuraModel.TextureSample(column, row);
                Assert.That(value, Is.InRange(0d, 1d));
                distinct.Add((int)Math.Round(value * 100d));
            }
        }

        Assert.That(distinct.Count, Is.GreaterThan(20));
    }

    [Test]
    public void DeclaredRadiusAndFadesMatchTheDesignEvery()
    {
        Assert.That(PointerAuraModel.Radius, Is.EqualTo(168d));
        Assert.That(PointerAuraModel.FadeInSeconds, Is.LessThan(PointerAuraModel.FadeOutSeconds));
        Assert.That(PointerAuraModel.PeakAlpha, Is.InRange(0.05d, 0.35d));
    }
}
