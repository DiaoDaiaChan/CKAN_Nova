namespace CkanUI.App.Animations;

/// <summary>逐帧推进的任务：返回 false 表示已收敛，帧泵把它摘掉。</summary>
internal interface IMotionTicker
{
    /// <summary>推进 <paramref name="seconds"/> 秒；静止后返回 false。</summary>
    bool Advance(double seconds);
}
