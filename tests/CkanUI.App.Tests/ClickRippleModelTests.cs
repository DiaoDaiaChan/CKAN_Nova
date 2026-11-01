using CkanUI.App.Controls;
using NUnit.Framework;

namespace CkanUI.App.Tests;

/// <summary>
/// 点击涟漪的数值模型：落点、上限、时间步的边界，以及两圈半径/不透明度随时间的行为。
/// </summary>
/// <remarks>
/// 模型不碰 Avalonia 也不碰计时器，全部可以在无头环境里断言；
/// 画笔缓存与帧率由 <c>ClickRippleLayer</c> 在实机帧里验证，这里不做像素断言。
/// </remarks>
[TestFixture]
public sealed class ClickRippleModelTests
{
    [Test]
    public void Spawn_AddsOneRippleAtThePointer()
    {
        var model = new ClickRippleModel();
        model.Spawn(320, 180);

        Assert.That(model.Count, Is.EqualTo(1));
        Assert.That(model.IsIdle, Is.False);
        Assert.That(model.RippleX(0), Is.EqualTo(320));
        Assert.That(model.RippleY(0), Is.EqualTo(180));
    }

    [Test]
    public void Spawn_IgnoresNonFiniteCoordinates()
    {
        var model = new ClickRippleModel();

        model.Spawn(double.NaN, 10);
        model.Spawn(10, double.PositiveInfinity);
        model.Spawn(double.NegativeInfinity, double.NaN);

        Assert.That(model.Count, Is.EqualTo(0));
        Assert.That(model.IsIdle, Is.True);
    }

    [Test]
    public void Spawn_KeepsTheNewestRipplesAndDropsTheOldest()
    {
        var model = new ClickRippleModel();
        for (var i = 0; i < ClickRippleModel.MaxActiveRipples + 2; i++)
        {
            model.Spawn(i, 0);
        }

        Assert.That(model.Count, Is.EqualTo(ClickRippleModel.MaxActiveRipples));
        Assert.That(model.RippleX(0), Is.EqualTo(2), "最早的两个必须被挤掉，剩下的从第 2 个落点开始");
        Assert.That(model.RippleX(ClickRippleModel.MaxActiveRipples - 1), Is.EqualTo(7), "最新的一定还在");
    }

    [Test]
    public void Advance_IgnoresNonPositiveAndNonFiniteSteps()
    {
        var model = new ClickRippleModel();
        model.Spawn(100, 100);
        var radius = model.RingRadius(0, 0);

        model.Advance(double.NaN);
        model.Advance(double.PositiveInfinity);
        model.Advance(0);
        model.Advance(-4);

        Assert.That(model.RingRadius(0, 0), Is.EqualTo(radius), "非法时间步一律不推进");
        Assert.That(model.Count, Is.EqualTo(1));
    }

    [Test]
    public void Advance_CapsASingleStepAtTheMaximum()
    {
        var longStep = new ClickRippleModel();
        longStep.Spawn(0, 0);
        longStep.Advance(5);

        var capped = new ClickRippleModel();
        capped.Spawn(0, 0);
        capped.Advance(ClickRippleModel.MaxStepSeconds);

        Assert.That(longStep.RingRadius(0, 0), Is.EqualTo(capped.RingRadius(0, 0)), "单帧最多推进 0.1 秒");
        Assert.That(longStep.RingAlpha(0, 0), Is.EqualTo(capped.RingAlpha(0, 0)));
    }

    [Test]
    public void RingRadius_GrowsMonotonicallyAndStaysInBounds()
    {
        var model = new ClickRippleModel();
        model.Spawn(200, 120);

        var previous = new double[ClickRippleModel.RingCount];
        for (var frame = 0; frame < 40; frame++)
        {
            for (var ring = 0; ring < ClickRippleModel.RingCount; ring++)
            {
                var radius = model.RingRadius(0, ring);

                Assert.That(radius, Is.InRange(0d, 44d));
                Assert.That(radius, Is.GreaterThanOrEqualTo(previous[ring]), "半径只能往外扩");
                previous[ring] = radius;
            }

            model.Advance(0.01);
        }

        Assert.That(previous[0], Is.GreaterThan(40), "外圈 0.4 秒时应该扩到接近 44");
        Assert.That(previous[1], Is.LessThanOrEqualTo(26), "内圈封顶 26");
    }

    [Test]
    public void RingAlpha_StaysWithinTheUnitIntervalAndNeverGrows()
    {
        var model = new ClickRippleModel();
        model.Spawn(200, 120);

        Assert.That(model.RingAlpha(0, 0), Is.EqualTo(0.30).Within(1e-9), "外圈从 0.30 起跳");

        var previous = model.RingAlpha(0, 0);
        for (var frame = 0; frame < 40; frame++)
        {
            model.Advance(0.01);

            var alpha = model.RingAlpha(0, 0);
            Assert.That(alpha, Is.InRange(0d, 1d));
            Assert.That(alpha, Is.LessThanOrEqualTo(previous), "不透明度只能往下掉");
            previous = alpha;
        }

        Assert.That(previous, Is.LessThan(0.2), "0.4 秒后外圈该淡掉了");
    }

    [Test]
    public void SecondRing_WaitsForItsDelay()
    {
        var model = new ClickRippleModel();
        model.Spawn(100, 100);
        model.Advance(0.08);

        Assert.That(model.RingRadius(0, 1), Is.EqualTo(0), "内圈还没起步，不该有半径");
        Assert.That(model.RingAlpha(0, 1), Is.EqualTo(0));
        Assert.That(model.RingRadius(0, 0), Is.GreaterThan(0), "外圈必须已经在跑");
        Assert.That(model.RingAlpha(0, 0), Is.GreaterThan(0));

        model.Advance(0.02);

        Assert.That(model.RingRadius(0, 1), Is.GreaterThan(0), "过了 0.09 秒内圈才现身");
        Assert.That(model.RingAlpha(0, 1), Is.GreaterThan(0));
    }

    [Test]
    public void Ripple_DisappearsAfterItsLifetime()
    {
        var model = new ClickRippleModel();
        model.Spawn(100, 100);

        for (var frame = 0; frame < 4; frame++)
        {
            model.Advance(0.1);
        }

        Assert.That(model.IsIdle, Is.False, "0.4 秒还活着，别提前清场");

        for (var frame = 0; frame < 2; frame++)
        {
            model.Advance(0.1);
        }

        Assert.That(model.Count, Is.EqualTo(0));
        Assert.That(model.IsIdle, Is.True);
    }

    [Test]
    public void Clear_DropsEverythingImmediately()
    {
        var model = new ClickRippleModel();
        model.Spawn(1, 2);
        model.Spawn(3, 4);

        model.Clear();

        Assert.That(model.Count, Is.EqualTo(0));
        Assert.That(model.IsIdle, Is.True);
    }

    [Test]
    public void SameTimeSequenceProducesTheSameFrame()
    {
        var left = new ClickRippleModel();
        var right = new ClickRippleModel();
        foreach (var model in new[] { left, right })
        {
            model.Spawn(120, 90);
            model.Spawn(300, 260);
        }

        for (var frame = 0; frame < 12; frame++)
        {
            left.Advance(0.03);
            right.Advance(0.03);
        }

        Assert.That(right.Count, Is.EqualTo(left.Count));
        for (var i = 0; i < left.Count; i++)
        {
            Assert.That(right.RippleX(i), Is.EqualTo(left.RippleX(i)));
            Assert.That(right.RippleY(i), Is.EqualTo(left.RippleY(i)));
            for (var ring = 0; ring < ClickRippleModel.RingCount; ring++)
            {
                Assert.That(right.RingRadius(i, ring), Is.EqualTo(left.RingRadius(i, ring)));
                Assert.That(right.RingAlpha(i, ring), Is.EqualTo(left.RingAlpha(i, ring)));
            }
        }
    }
}
