using System;
using System.Linq;
using CkanUI.App.Controls;
using NUnit.Framework;

namespace CkanUI.App.Tests;

[TestFixture]
public class WindowDragStarlightModelTests
{
    [Test]
    public void InitialState_IsIdleAndEmpty()
    {
        var model = new WindowDragStarlightModel(seed: 42);
        Assert.That(model.IsIdle, Is.True);
        Assert.That(model.IsDragging, Is.False);
        Assert.That(model.ActiveCount, Is.EqualTo(0));
    }

    [Test]
    public void StartDrag_EmitsInitialParticles()
    {
        var model = new WindowDragStarlightModel(seed: 42);
        model.StartDrag(100, 200);

        Assert.That(model.IsDragging, Is.True);
        Assert.That(model.IsIdle, Is.False);
        Assert.That(model.ActiveCount, Is.GreaterThan(0));
        Assert.That(model.Particles.All(p => p.IsAlive), Is.True);
    }

    [Test]
    public void UpdateDrag_MovingTrajectoryEmitsParticles()
    {
        var model = new WindowDragStarlightModel(seed: 42);
        model.StartDrag(100, 100);
        int initialCount = model.ActiveCount;

        // Move 50px away
        model.UpdateDrag(150, 100);
        Assert.That(model.ActiveCount, Is.GreaterThan(initialCount));

        // Move another 100px away
        int nextCount = model.ActiveCount;
        model.UpdateDrag(250, 100);
        Assert.That(model.ActiveCount, Is.GreaterThan(nextCount));
    }

    [Test]
    public void Advance_ParticlesAgeAndFadeOut()
    {
        var model = new WindowDragStarlightModel(seed: 42);
        model.StartDrag(100, 100);
        model.EndDrag();

        Assert.That(model.IsDragging, Is.False);
        Assert.That(model.ActiveCount, Is.GreaterThan(0));

        // Advance past all particles' max lifetime
        model.Advance(1.5d);

        Assert.That(model.ActiveCount, Is.EqualTo(0));
        Assert.That(model.IsIdle, Is.True);
    }

    [Test]
    public void Shift_MovesParticlesAndLastEmitTogether()
    {
        var model = new WindowDragStarlightModel(seed: 42);
        model.StartDrag(100, 100);
        model.UpdateDrag(200, 100);

        var beforeX = model.Particles.Select(p => p.X).ToArray();
        var beforeY = model.Particles.Select(p => p.Y).ToArray();
        var beforeLast = model.LastPosition;

        model.Shift(-30, 15);

        Assert.That(model.LastPosition.X, Is.EqualTo(beforeLast.X - 30).Within(1e-9));
        Assert.That(model.LastPosition.Y, Is.EqualTo(beforeLast.Y + 15).Within(1e-9));
        Assert.That(model.Particles.Count, Is.EqualTo(beforeX.Length));
        for (int i = 0; i < beforeX.Length; i++)
        {
            Assert.That(model.Particles[i].X, Is.EqualTo(beforeX[i] - 30).Within(1e-9));
            Assert.That(model.Particles[i].Y, Is.EqualTo(beforeY[i] + 15).Within(1e-9));
        }
    }

    [Test]
    public void Clear_EmptiesAllParticles()
    {
        var model = new WindowDragStarlightModel(seed: 42);
        model.StartDrag(100, 100);
        model.UpdateDrag(200, 200);

        Assert.That(model.ActiveCount, Is.GreaterThan(0));
        model.Clear();

        Assert.That(model.ActiveCount, Is.EqualTo(0));
        Assert.That(model.IsIdle, Is.True);
    }
}
