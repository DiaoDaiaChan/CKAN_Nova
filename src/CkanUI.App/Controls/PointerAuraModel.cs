using System;

namespace CkanUI.App.Controls;

/// <summary>
/// 指针光环的纯数学模型：只记光标位置与一个 0..1 的强度，不认识控件、也不碰渲染。
/// </summary>
/// <remarks>
/// <para>
/// 交互语义是「光标半径内元素才响应」：<see cref="Radius"/> 是作用半径，半径外的元素完全不受影响。
/// 半径内按 <see cref="Falloff"/> 做平滑衰减，越靠近光标越亮，边缘归零——所以看起来是打在元素表面的一团光，
/// 而不是给整块控件统一调亮度。
/// </para>
/// <para>
/// 强度分进出两段：进入元素 <see cref="FadeInSeconds"/> 淡入、离开 <see cref="FadeOutSeconds"/> 淡出，
/// 淡出比淡入慢，手快划过去不会闪。单帧步长夹在 <see cref="MaxStepSeconds"/> 内，
/// 掉帧时不会一步跳到底（与 <see cref="ClickRippleModel"/> 同一套口径）。
/// </para>
/// </remarks>
public sealed class PointerAuraModel
{
    /// <summary>作用半径（DIP）：半径外的像素一律不受影响。</summary>
    public const double Radius = 168d;

    /// <summary>淡入时长（秒）。</summary>
    public const double FadeInSeconds = 0.09d;

    /// <summary>淡出时长（秒）；比淡入慢，快速划过时不闪。</summary>
    public const double FadeOutSeconds = 0.22d;

    /// <summary>单帧最大推进步长（秒）。</summary>
    public const double MaxStepSeconds = 0.1d;

    /// <summary>光环峰值不透明度：压得很低，只作为表面反光，不盖住文字。</summary>
    public const double PeakAlpha = 0.18d;

    /// <summary>边缘描边的不透明度系数（相对 <see cref="PeakAlpha"/>）。</summary>
    public const double RimAlpha = 0.55d;

    /// <summary>颗粒纹理的不透明度（已乘强度）。</summary>
    public const double TextureAlpha = 0.05d;

    /// <summary>颗粒栅格间距（DIP）：7 像素一格，看起来像磨砂面而不是噪点。</summary>
    public const double TextureSpacing = 7d;

    /// <summary>低于这个强度视为熄灭，避免永远残着一点几乎看不见的光。</summary>
    public const double MinVisibleAlpha = 0.002d;

    /// <summary>光标 X（相对宿主层的 DIP）。</summary>
    public double X { get; private set; }

    /// <summary>光标 Y（相对宿主层的 DIP）。</summary>
    public double Y { get; private set; }

    /// <summary>当前强度 0..1。</summary>
    public double Alpha { get; private set; }

    /// <summary>光标当前是否落在可响应的元素上。</summary>
    public bool HasTarget { get; private set; }

    /// <summary>是否还有可见内容；为 false 时渲染层可以整层跳过。</summary>
    public bool IsActive => Alpha > MinVisibleAlpha;

    /// <summary>把光标放到 (x, y)，并声明它落在可响应元素上（开始淡入）。</summary>
    /// <param name="x">相对宿主层的 X。</param>
    /// <param name="y">相对宿主层的 Y。</param>
    public void Track(double x, double y)
    {
        if (!double.IsFinite(x) || !double.IsFinite(y))
        {
            return;
        }

        X = x;
        Y = y;
        HasTarget = true;
    }

    /// <summary>光标离开可响应元素（开始淡出）；位置保留，让最后那团光在原地收掉。</summary>
    public void Clear() => HasTarget = false;

    /// <summary>推进一帧：进入时线性淡入、离开时线性淡出，步长夹在 <see cref="MaxStepSeconds"/> 内。</summary>
    /// <param name="deltaSeconds">距上一帧的秒数；非正数、NaN、Infinity 都当作没推进。</param>
    public void Advance(double deltaSeconds)
    {
        if (!double.IsFinite(deltaSeconds) || deltaSeconds <= 0d)
        {
            return;
        }

        var step = Math.Min(deltaSeconds, MaxStepSeconds);
        Alpha = HasTarget
            ? Math.Min(1d, Alpha + step / FadeInSeconds)
            : Math.Max(0d, Alpha - step / FadeOutSeconds);

        if (!HasTarget && Alpha <= MinVisibleAlpha)
        {
            Alpha = 0d;
        }
    }

    /// <summary>
    /// 径向衰减：距离 0 为 1、距离等于半径为 0，中间走 smoothstep，斜率在两端收敛，不会看到硬边。
    /// </summary>
    /// <param name="distance">到光标的距离（DIP）。</param>
    /// <param name="radius">作用半径（DIP）。</param>
    /// <returns>0..1 的权重；非法输入一律 0。</returns>
    public static double Falloff(double distance, double radius)
    {
        if (!double.IsFinite(distance) || !double.IsFinite(radius) || radius <= 0d || distance < 0d)
        {
            return 0d;
        }

        if (distance >= radius)
        {
            return 0d;
        }

        var t = 1d - distance / radius;
        return t * t * (3d - 2d * t);
    }

    /// <summary>确定性颗粒采样：同一格永远同一个值，帧间不闪，也不需要随机数状态。</summary>
    /// <param name="column">栅格列号。</param>
    /// <param name="row">栅格行号。</param>
    /// <returns>0..1 的权重。</returns>
    public static double TextureSample(int column, int row)
    {
        var hash = unchecked((uint)(column * 73856093) ^ (uint)(row * 19349663));
        hash ^= hash >> 13;
        hash *= 0x5BD1E995u;
        hash ^= hash >> 15;
        return (hash & 0xFFFF) / 65535d;
    }
}
