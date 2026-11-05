namespace CkanUI.Services.Abstractions.Input;

/// <summary>
/// 手柄状态来源（平台层实现；无手柄或不支持的平台上 <see cref="IsAvailable"/> 为 <c>false</c>）。
/// </summary>
/// <remarks>
/// 只负责「读一帧」，不做去抖、不做长短按——那些是 <c>GamepadNavigator</c> 状态机的事，
/// 拆分后状态机可以用伪造时钟在无头测试里跑完整套手感参数。
/// </remarks>
public interface IGamepadSource
{
    /// <summary>当前平台 / 当前机器上是否有可用的手柄读取通道。</summary>
    bool IsAvailable { get; }

    /// <summary>读一帧当前状态；没有手柄在线时返回 <see cref="GamepadSnapshot.Disconnected"/>。</summary>
    GamepadSnapshot Poll();
}
