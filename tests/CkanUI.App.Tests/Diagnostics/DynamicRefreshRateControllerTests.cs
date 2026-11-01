using System;
using CkanUI.App.Diagnostics;
using NUnit.Framework;

namespace CkanUI.App.Tests.Diagnostics;

[TestFixture]
public sealed class DynamicRefreshRateControllerTests
{
    [Test]
    public void Controller_InitialState_IsNotIdle_And_TargetFpsIs60()
    {
        var controller = new DynamicRefreshRateController();
        Assert.That(controller.IsIdle, Is.False);
        Assert.That(controller.ActiveTargetFps, Is.EqualTo(60));
        Assert.That(controller.IdleTargetFps, Is.EqualTo(10));
        Assert.That(controller.CurrentTargetFps, Is.EqualTo(60));
        Assert.That(controller.StatusDescription, Does.Contain("60 FPS"));
    }

    [Test]
    public void Controller_ForceIdle_TransitionsToIdle()
    {
        var controller = new DynamicRefreshRateController();
        bool changedFired = false;
        controller.IdleStateChanged += idle => changedFired = idle;

        controller.ForceIdle();

        Assert.That(controller.IsIdle, Is.True);
        Assert.That(changedFired, Is.True);
        Assert.That(controller.CurrentTargetFps, Is.EqualTo(10));
        Assert.That(controller.StatusDescription, Does.Contain("10 FPS"));
    }

    [Test]
    public void Controller_NotifyUserActivity_RestoresActiveFromIdle()
    {
        var controller = new DynamicRefreshRateController();
        controller.ForceIdle();
        Assert.That(controller.IsIdle, Is.True);

        bool activeFired = false;
        controller.IdleStateChanged += idle =>
        {
            if (!idle) activeFired = true;
        };

        controller.NotifyUserActivity();

        Assert.That(controller.IsIdle, Is.False);
        Assert.That(activeFired, Is.True);
        Assert.That(controller.CurrentTargetFps, Is.EqualTo(60));
    }

    [Test]
    public void Controller_CustomTargetFps_ClampsProperly()
    {
        var controller = new DynamicRefreshRateController
        {
            ActiveTargetFps = 120,
            IdleTargetFps = 5
        };

        Assert.That(controller.ActiveTargetFps, Is.EqualTo(120));
        Assert.That(controller.IdleTargetFps, Is.EqualTo(5));

        controller.ForceIdle();
        Assert.That(controller.CurrentTargetFps, Is.EqualTo(5));
    }
}
