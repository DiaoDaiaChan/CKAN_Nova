using System;
using Avalonia;
using CkanUI.App.Controls;
using NUnit.Framework;

namespace CkanUI.App.Tests;

[TestFixture]
public sealed class CursorPointerModelTests
{
    [Test]
    public void InitialState_IsAtRestAndIdle()
    {
        var model = new CursorPointerModel();
        Assert.That(model.CurrentScale, Is.EqualTo(CursorPointerModel.RestScale));
        Assert.That(model.CurrentTiltDegrees, Is.EqualTo(0.0d));
        Assert.That(model.IsIdle, Is.True);
        Assert.That(model.IsPressed, Is.False);
    }

    [Test]
    public void HoverInteractive_TiltsCursor()
    {
        var model = new CursorPointerModel();
        model.Track(new Point(100, 100), isOverInteractive: true);

        Assert.That(model.TargetTiltDegrees, Is.LessThan(0), "Target tilt should be negative when over interactive element");
        Assert.That(model.IsIdle, Is.False, "Model should be active when target tilt changes");

        // Advance time by 0.1s to allow smoothing
        model.Update(0.1d);
        Assert.That(model.CurrentTiltDegrees, Is.LessThan(-1.0d), $"Current tilt should move towards target, got {model.CurrentTiltDegrees}");
    }

    [Test]
    public void Press_ShrinksScale()
    {
        var model = new CursorPointerModel();
        model.Press(new Point(200, 200));

        Assert.That(model.IsPressed, Is.True);
        Assert.That(model.TargetScale, Is.EqualTo(CursorPointerModel.PressedScale));

        // Advance time
        model.Update(0.15d);
        Assert.That(model.CurrentScale, Is.LessThan(0.92d), $"Scale should shrink on press, got {model.CurrentScale}");
    }

    [Test]
    public void Release_ExpandsBeforeSettlingAtRest()
    {
        var model = new CursorPointerModel();
        model.Press(new Point(200, 200));
        model.Update(0.2d); // Pressed state settled

        model.Release(new Point(200, 200));
        Assert.That(model.IsPressed, Is.False);

        // At peak time (0.05s), scale should exceed RestScale (1.0)
        model.Update(CursorPointerModel.ReleasePeakTimeSeconds);
        Assert.That(model.CurrentScale, Is.GreaterThan(1.02d), $"Scale should expand beyond 1.0 at release peak, got {model.CurrentScale}");

        // After release duration (0.25s total), should settle back to RestScale (1.0)
        model.Update(0.25d);
        Assert.That(model.CurrentScale, Is.InRange(0.99d, 1.01d));
        Assert.That(model.IsIdle, Is.True);
    }

    [Test]
    public void Motion_AddsDynamicVelocityTilt()
    {
        var model = new CursorPointerModel();
        model.Track(new Point(100, 100), isOverInteractive: false);
        model.Track(new Point(160, 100), isOverInteractive: false); // Fast move to the right

        Assert.That(model.TargetTiltDegrees, Is.GreaterThan(0), "Moving right should induce positive tilt");
    }

    [Test]
    public void HideAndClear_ResetsOrFades()
    {
        var model = new CursorPointerModel();
        model.Track(new Point(100, 100), isOverInteractive: true);
        model.Hide();

        Assert.That(model.TargetOpacity, Is.EqualTo(0.0d));
        model.Update(0.1d);
        Assert.That(model.CurrentOpacity, Is.LessThan(0.5d));

        model.Clear();
        Assert.That(model.CurrentOpacity, Is.EqualTo(0.0d));
        Assert.That(model.IsIdle, Is.True);
    }
}
