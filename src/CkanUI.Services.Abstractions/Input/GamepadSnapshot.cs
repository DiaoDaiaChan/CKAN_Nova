namespace CkanUI.Services.Abstractions.Input;

/// <summary>
/// 一帧手柄状态。<see cref="LeftThumbX"/> 正右、<see cref="LeftThumbY"/> 正上，
/// 摇杆与扳机都已归一到 0–1（摇杆 -1–1），状态机因此不需要知道原生量纲。
/// </summary>
/// <param name="IsConnected">这一帧是否有手柄在线；<c>false</c> 时其余字段全部为 0。</param>
/// <param name="Buttons">按下的按键位。</param>
/// <param name="LeftThumbX">左摇杆水平轴，-1 左、+1 右。</param>
/// <param name="LeftThumbY">左摇杆垂直轴，-1 下、+1 上。</param>
/// <param name="LeftTrigger">左扳机行程，0–1。</param>
/// <param name="RightTrigger">右扳机行程，0–1。</param>
public readonly record struct GamepadSnapshot(
    bool IsConnected,
    GamepadButtons Buttons,
    float LeftThumbX,
    float LeftThumbY,
    float LeftTrigger,
    float RightTrigger)
{
    /// <summary>没有任何手柄在线的一帧（默认值即是断连态）。</summary>
    public static GamepadSnapshot Disconnected => default;
}
