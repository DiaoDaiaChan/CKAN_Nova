using CkanUI.Services.Abstractions.Input;
using CkanUI.Services.Input;
using NUnit.Framework;

namespace CkanUI.Services.Tests;

/// <summary>
/// 手柄状态机（UI-12）：按键只认按下沿、方向按住连发、扳机当翻页键。
/// 时间戳由测试给定，因此「350 ms 首延迟 / 120 ms 连发」可以精确断言，不靠 Thread.Sleep 赌调度。
/// </summary>
[TestFixture]
public sealed class GamepadNavigatorTests
{
    private static GamepadSnapshot Pad(GamepadButtons buttons = GamepadButtons.None, float x = 0f, float y = 0f, float leftTrigger = 0f, float rightTrigger = 0f)
        => new(true, buttons, x, y, leftTrigger, rightTrigger);

    [Test]
    public void ConnectedIdle_ProducesNothing()
    {
        var navigator = new GamepadNavigator();

        Assert.That(navigator.Update(Pad(), 0), Is.Empty);
    }

    [Test]
    public void ButtonPress_FiresOnceOnTheFallingEdge()
    {
        var navigator = new GamepadNavigator();

        Assert.That(navigator.Update(Pad(GamepadButtons.A), 0), Is.EqualTo(new[] { GamepadAction.Accept }));
        Assert.That(navigator.Update(Pad(GamepadButtons.A), 33), Is.Empty, "按住不放不能连发确认，否则一次按键会触发十几次。");
        Assert.That(navigator.Update(Pad(), 66), Is.Empty);
        Assert.That(navigator.Update(Pad(GamepadButtons.A), 99), Is.EqualTo(new[] { GamepadAction.Accept }));
    }

    [Test]
    public void ShoulderButtons_MapToTabSwitching()
    {
        var navigator = new GamepadNavigator();

        Assert.That(navigator.Update(Pad(GamepadButtons.RightShoulder), 0), Is.EqualTo(new[] { GamepadAction.NextTab }));
        Assert.That(navigator.Update(Pad(), 33), Is.Empty);
        Assert.That(navigator.Update(Pad(GamepadButtons.LeftShoulder), 66), Is.EqualTo(new[] { GamepadAction.PreviousTab }));
    }

    [Test]
    public void DPad_FirstPressIsImmediate_ThenRepeatsAfterTheInitialDelay()
    {
        var navigator = new GamepadNavigator();
        var down = Pad(GamepadButtons.DPadDown);

        Assert.That(navigator.Update(down, 0), Is.EqualTo(new[] { GamepadAction.FocusDown }), "第一次按下必须立刻动一格，不能让用户等 350 ms。");
        Assert.That(navigator.Update(down, 200), Is.Empty);
        Assert.That(navigator.Update(down, 349), Is.Empty, "首延迟未到之前不许连发。");
        Assert.That(navigator.Update(down, 350), Is.EqualTo(new[] { GamepadAction.FocusDown }));
        Assert.That(navigator.Update(down, 400), Is.Empty);
        Assert.That(navigator.Update(down, 470), Is.EqualTo(new[] { GamepadAction.FocusDown }));
    }

    [Test]
    public void ReleasingDirection_StopsTheRepeat()
    {
        var navigator = new GamepadNavigator();
        var down = Pad(GamepadButtons.DPadDown);

        navigator.Update(down, 0);
        navigator.Update(down, 350);
        Assert.That(navigator.Update(Pad(), 400), Is.Empty);
        Assert.That(navigator.Update(down, 800), Is.EqualTo(new[] { GamepadAction.FocusDown }), "松手再按，要按「新的一次按下」处理，立刻派发。");
    }

    [Test]
    public void ThumbStick_BelowDeadZoneIsIgnored()
    {
        var navigator = new GamepadNavigator();

        Assert.That(navigator.Update(Pad(y: GamepadNavigator.DeadZone - 0.01f), 0), Is.Empty);
    }

    [Test]
    public void ThumbStick_UsesTheDominantAxis()
    {
        var navigator = new GamepadNavigator();

        Assert.That(navigator.Update(Pad(x: 0.9f, y: 0.2f), 0), Is.EqualTo(new[] { GamepadAction.FocusRight }));
        Assert.That(navigator.Update(Pad(x: -0.9f, y: 0.2f), 33), Is.EqualTo(new[] { GamepadAction.FocusLeft }));
    }

    [Test]
    public void DPad_WinsOverThumbStick()
    {
        var navigator = new GamepadNavigator();

        Assert.That(navigator.Update(Pad(GamepadButtons.DPadUp, x: 0.9f), 0), Is.EqualTo(new[] { GamepadAction.FocusUp }));
    }

    [Test]
    public void Triggers_AreEdgeTriggeredScroll()
    {
        var navigator = new GamepadNavigator();

        Assert.That(navigator.Update(Pad(leftTrigger: GamepadNavigator.TriggerThreshold), 0), Is.EqualTo(new[] { GamepadAction.ScrollUp }));
        Assert.That(navigator.Update(Pad(leftTrigger: 1f), 33), Is.Empty, "扳机按住不放不能连续翻页。");
        Assert.That(navigator.Update(Pad(leftTrigger: 0.1f), 66), Is.Empty);
        Assert.That(navigator.Update(Pad(rightTrigger: GamepadNavigator.TriggerThreshold), 99), Is.EqualTo(new[] { GamepadAction.ScrollDown }));
    }

    [Test]
    public void Disconnect_ClearsTheHeldState()
    {
        var navigator = new GamepadNavigator();
        navigator.Update(Pad(GamepadButtons.DPadDown), 0);
        navigator.Update(Pad(GamepadButtons.DPadDown), 350);

        Assert.That(navigator.Update(GamepadSnapshot.Disconnected, 400), Is.Empty);
        Assert.That(navigator.Update(Pad(GamepadButtons.DPadDown), 433), Is.EqualTo(new[] { GamepadAction.FocusDown }), "重连后按住的方向要重新算一次「首次按下」。");
    }

    [Test]
    public void Reset_ClearsHeldButtons()
    {
        var navigator = new GamepadNavigator();
        navigator.Update(Pad(GamepadButtons.A), 0);

        navigator.Reset();

        Assert.That(navigator.Update(Pad(GamepadButtons.A), 33), Is.EqualTo(new[] { GamepadAction.Accept }));
    }
}
