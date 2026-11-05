namespace CkanUI.Services.Abstractions.Input;

/// <summary>
/// 手柄按键位。取值与 XInput 的 <c>XINPUT_GAMEPAD_*</c> 常量一致，
/// 平台层拿到 <c>wButtons</c> 之后直接强转即可，不需要再查表翻译。
/// </summary>
[Flags]
public enum GamepadButtons : ushort
{
    /// <summary>没有按键按下。</summary>
    None = 0x0000,

    /// <summary>十字键上。</summary>
    DPadUp = 0x0001,

    /// <summary>十字键下。</summary>
    DPadDown = 0x0002,

    /// <summary>十字键左。</summary>
    DPadLeft = 0x0004,

    /// <summary>十字键右。</summary>
    DPadRight = 0x0008,

    /// <summary>菜单键（Start）。</summary>
    Start = 0x0010,

    /// <summary>视图键（Back）。</summary>
    Back = 0x0020,

    /// <summary>左摇杆按下。</summary>
    LeftThumb = 0x0040,

    /// <summary>右摇杆按下。</summary>
    RightThumb = 0x0080,

    /// <summary>左肩键（LB）。</summary>
    LeftShoulder = 0x0100,

    /// <summary>右肩键（RB）。</summary>
    RightShoulder = 0x0200,

    /// <summary>下键（A）。</summary>
    A = 0x1000,

    /// <summary>右键（B）。</summary>
    B = 0x2000,

    /// <summary>左键（X）。</summary>
    X = 0x4000,

    /// <summary>上键（Y）。</summary>
    Y = 0x8000,
}
