using System;
using Avalonia;
using CkanUI.App.Animations;
using NUnit.Framework;

namespace CkanUI.App.Tests;

[TestFixture]
public sealed class ControlTiltModelTests
{
    [Test]
    public void Calculate_CenterPosition_ProducesZeroShearAndSubpixelDeflection()
    {
        var cursor = new Point(50, 20);
        var bounds = new Rect(0, 0, 100, 40);

        var tilt = ControlTiltModel.Calculate(cursor, bounds);

        Assert.That(tilt.Extent, Is.EqualTo(0d).Within(1e-5));
        // 固定平面假设：零剪切、零旋转，绝不破坏文字排版与可读性
        Assert.That(tilt.SkewX, Is.EqualTo(0d).Within(1e-5));
        Assert.That(tilt.SkewY, Is.EqualTo(0d).Within(1e-5));
        Assert.That(tilt.RotateAngle, Is.EqualTo(0d).Within(1e-5));
        Assert.That(tilt.DeflectionX, Is.EqualTo(0d).Within(1e-5));
        // 中心接触受力微下压
        Assert.That(tilt.DeflectionY, Is.GreaterThanOrEqualTo(0d));
        Assert.That(tilt.DeflectionY, Is.LessThanOrEqualTo(ControlTiltModel.MaxDeflectionPixels));
        Assert.That(tilt.DepressionScale, Is.LessThanOrEqualTo(1.0d));
        Assert.That(tilt.DepressionScale, Is.GreaterThanOrEqualTo(1.0d - ControlTiltModel.MaxDepressionScaleDelta));
    }

    [Test]
    public void Calculate_InvalidBounds_ProducesZeroTilt()
    {
        var cursor = new Point(50, 20);
        var zeroBounds = new Rect(0, 0, 0, 0);

        var tilt = ControlTiltModel.Calculate(cursor, zeroBounds);

        Assert.That(tilt, Is.EqualTo(ControlTilt.Zero));
        Assert.That(tilt.SkewX, Is.EqualTo(0d));
        Assert.That(tilt.SkewY, Is.EqualTo(0d));
        Assert.That(tilt.RotateAngle, Is.EqualTo(0d));
        Assert.That(tilt.DeflectionX, Is.EqualTo(0d));
        Assert.That(tilt.DeflectionY, Is.EqualTo(0d));
        Assert.That(tilt.DepressionScale, Is.EqualTo(1.0d));
    }

    [Test]
    public void Calculate_TopRight_ProducesZeroShearAndSmoothForceDeflection()
    {
        var cursor = new Point(100, 0); // Top-Right corner
        var bounds = new Rect(0, 0, 100, 40);

        var tilt = ControlTiltModel.Calculate(cursor, bounds);

        Assert.That(tilt.Extent, Is.GreaterThan(0.5d));
        Assert.That(tilt.Extent, Is.LessThanOrEqualTo(1d));
        // 固定平面：直角正交，绝对不产生平行四边形切变与文字倾斜
        Assert.That(tilt.SkewX, Is.EqualTo(0d));
        Assert.That(tilt.SkewY, Is.EqualTo(0d));
        Assert.That(tilt.RotateAngle, Is.EqualTo(0d));
        // 受到鼠标施加的力响应：
        Assert.That(tilt.ForceX, Is.GreaterThan(0d));
        Assert.That(tilt.ForceY, Is.LessThan(0d));
        Assert.That(tilt.DeflectionX, Is.GreaterThan(0d));
        Assert.That(tilt.DeflectionX, Is.LessThanOrEqualTo(ControlTiltModel.MaxDeflectionPixels));
        Assert.That(tilt.DeflectionY, Is.LessThanOrEqualTo(ControlTiltModel.MaxDeflectionPixels));
    }

    [Test]
    public void Calculate_OutsideBounds_ClampsGracefully()
    {
        var farCursor = new Point(500, 300);
        var bounds = new Rect(0, 0, 100, 40);

        var tilt = ControlTiltModel.Calculate(farCursor, bounds);

        Assert.That(tilt.Extent, Is.LessThanOrEqualTo(1d));
        Assert.That(tilt.SkewX, Is.EqualTo(0d));
        Assert.That(tilt.SkewY, Is.EqualTo(0d));
        Assert.That(tilt.RotateAngle, Is.EqualTo(0d));
        Assert.That(Math.Abs(tilt.DeflectionX), Is.LessThanOrEqualTo(ControlTiltModel.MaxDeflectionPixels));
        Assert.That(Math.Abs(tilt.DeflectionY), Is.LessThanOrEqualTo(ControlTiltModel.MaxDeflectionPixels));
        Assert.That(tilt.DepressionScale, Is.GreaterThanOrEqualTo(1.0d - ControlTiltModel.MaxDepressionScaleDelta));
    }
}
