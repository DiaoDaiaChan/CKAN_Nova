using CkanUI.Services.Abstractions.Input;

namespace CkanUI.Services.Input;

/// <summary>
/// 手柄状态机：把逐帧的 <see cref="GamepadSnapshot"/> 翻译成带「长短按」手感的 <see cref="GamepadAction"/>。
/// </summary>
/// <remarks>
/// <para>纯状态机、无定时器、无 UI 依赖：时间由调用方按毫秒传入，测试用伪造时钟即可
/// 精确断言「按一下只动一格、按住 350 ms 后才开始连发、连发间隔 120 ms」。</para>
/// <para>分工：按键（A/B/X/Y/LB/RB/Back）只在**按下沿**派发一次；方向（十字键与左摇杆）按住才连发。
/// 扳机当翻页键用，同样只认按下沿——按住扳机连续翻页会滑过头。</para>
/// </remarks>
public sealed class GamepadNavigator
{
    /// <summary>左摇杆死区：小于它视为回中（0.5 约等于手柄的 50% 行程，误触少、推到底才走）。</summary>
    public const float DeadZone = 0.5f;

    /// <summary>方向键按住后第一次连发前的延迟（毫秒）：跨越式长列表里要能一格一格点。</summary>
    public const int InitialRepeatDelayMs = 350;

    /// <summary>方向键连发间隔（毫秒）。</summary>
    public const int RepeatIntervalMs = 120;

    /// <summary>扳机按下的判定阈值（0–1）。</summary>
    public const float TriggerThreshold = 0.6f;

    /// <summary>按键 → 动作表（只在按下沿派发）。</summary>
    private static readonly (GamepadButtons Mask, GamepadAction Action)[] ButtonMap =
    [
        (GamepadButtons.A, GamepadAction.Accept),
        (GamepadButtons.B, GamepadAction.Cancel),
        (GamepadButtons.X, GamepadAction.TogglePalette),
        (GamepadButtons.Y, GamepadAction.ShowShortcuts),
        (GamepadButtons.Start, GamepadAction.TogglePalette),
        (GamepadButtons.Back, GamepadAction.ToggleSidebar),
        (GamepadButtons.LeftShoulder, GamepadAction.PreviousTab),
        (GamepadButtons.RightShoulder, GamepadAction.NextTab),
    ];

    private GamepadButtons _held;
    private GamepadAction _repeatDirection;
    private long _nextRepeatAt;
    private bool _leftTriggerHeld;
    private bool _rightTriggerHeld;

    /// <summary>喂一帧状态，取回这一帧应该派发的动作（通常 0 个，最多 2 个：一个按键 + 一个方向）。</summary>
    /// <param name="snapshot">这一帧的手柄状态。</param>
    /// <param name="timestampMs">单调递增的毫秒时间戳（调用方给，测试可伪造）。</param>
    /// <returns>按发生顺序排列的动作；没有动作时返回空集合。</returns>
    public IReadOnlyList<GamepadAction> Update(GamepadSnapshot snapshot, long timestampMs)
    {
        if (!snapshot.IsConnected)
        {
            // 断连不清状态会留下「按住」的幽灵：重新连上时 A 键仍是按下沿，会凭空激活一次。
            Reset();
            return Array.Empty<GamepadAction>();
        }

        List<GamepadAction>? actions = null;

        var pressed = snapshot.Buttons & ~_held;
        foreach (var (mask, action) in ButtonMap)
        {
            if ((pressed & mask) != 0)
            {
                (actions ??= new List<GamepadAction>(2)).Add(action);
            }
        }

        if (snapshot.LeftTrigger >= TriggerThreshold && !_leftTriggerHeld)
        {
            _leftTriggerHeld = true;
            (actions ??= new List<GamepadAction>(2)).Add(GamepadAction.ScrollUp);
        }
        else if (snapshot.LeftTrigger < TriggerThreshold)
        {
            _leftTriggerHeld = false;
        }

        if (snapshot.RightTrigger >= TriggerThreshold && !_rightTriggerHeld)
        {
            _rightTriggerHeld = true;
            (actions ??= new List<GamepadAction>(2)).Add(GamepadAction.ScrollDown);
        }
        else if (snapshot.RightTrigger < TriggerThreshold)
        {
            _rightTriggerHeld = false;
        }

        var direction = ResolveDirection(snapshot);
        if (direction != _repeatDirection)
        {
            _repeatDirection = direction;
            _nextRepeatAt = timestampMs + InitialRepeatDelayMs;
            if (direction != GamepadAction.None)
            {
                (actions ??= new List<GamepadAction>(2)).Add(direction);
            }
        }
        else if (direction != GamepadAction.None && timestampMs >= _nextRepeatAt)
        {
            // 连发按「计划表」推进而不是「上次时间+间隔」：低帧率下不会因为一帧晚了就整体漂移。
            _nextRepeatAt = timestampMs + RepeatIntervalMs;
            (actions ??= new List<GamepadAction>(2)).Add(direction);
        }

        _held = snapshot.Buttons;
        return (IReadOnlyList<GamepadAction>?)actions ?? Array.Empty<GamepadAction>();
    }

    /// <summary>清空按住状态（断连、窗口失焦、手柄被换掉时调用）。</summary>
    public void Reset()
    {
        _held = GamepadButtons.None;
        _repeatDirection = GamepadAction.None;
        _nextRepeatAt = 0;
        _leftTriggerHeld = false;
        _rightTriggerHeld = false;
    }

    /// <summary>十字键优先于摇杆；摇杆按主导轴判定方向，避免斜推时来回抖。</summary>
    private static GamepadAction ResolveDirection(GamepadSnapshot snapshot)
    {
        if ((snapshot.Buttons & GamepadButtons.DPadUp) != 0)
        {
            return GamepadAction.FocusUp;
        }

        if ((snapshot.Buttons & GamepadButtons.DPadDown) != 0)
        {
            return GamepadAction.FocusDown;
        }

        if ((snapshot.Buttons & GamepadButtons.DPadLeft) != 0)
        {
            return GamepadAction.FocusLeft;
        }

        if ((snapshot.Buttons & GamepadButtons.DPadRight) != 0)
        {
            return GamepadAction.FocusRight;
        }

        var x = snapshot.LeftThumbX;
        var y = snapshot.LeftThumbY;
        var absX = Math.Abs(x);
        var absY = Math.Abs(y);
        if (absX < DeadZone && absY < DeadZone)
        {
            return GamepadAction.None;
        }

        if (absY >= absX)
        {
            return y > 0 ? GamepadAction.FocusUp : GamepadAction.FocusDown;
        }

        return x > 0 ? GamepadAction.FocusRight : GamepadAction.FocusLeft;
    }
}
