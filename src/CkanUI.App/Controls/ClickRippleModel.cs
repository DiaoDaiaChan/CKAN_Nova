using System;
using System.Collections.Generic;

namespace CkanUI.App.Controls;

/// <summary>
/// 点击涟漪的数值模型：每个涟漪从落点向外扩出两圈细环，只算半径与不透明度，不碰渲染。
/// </summary>
/// <remarks>
/// <para>
/// 点击涟漪只吃落点不吃状态：它不承载「点到了什么」的任何语义，所以动效关闭时由渲染层
/// （<c>ClickRippleLayer</c>）在生成前直接跳过 <see cref="Spawn"/>，模型侧不需要知道动效开关。
/// </para>
/// <para>
/// 两圈错开起步（内圈晚 0.09 秒），并且各自在自己剩下的窗口内走完行程：既保住水波的错位感，
/// 又能在 <see cref="LifetimeSeconds"/> 到点时两圈同时收敛到 0，收尾不会硬切一下。
/// 半径按 ease-out cubic 推进（起手快、尾巴慢），不透明度按 1.6 次幂衰减（尾巴掉得更快）。
/// </para>
/// <para>
/// 所有公共方法都容忍非法输入（NaN、无穷、零或负的时间步）：窗口最小化、断点停住、
/// 计时器抖出负数 delta 时都会被问到，抛异常会把整个窗口带崩。
/// </para>
/// </remarks>
public sealed class ClickRippleModel
{
    /// <summary>一个涟漪从出生到被移除的秒数。</summary>
    public const double LifetimeSeconds = 0.5d;

    /// <summary>同时最多存在几个涟漪；再点就挤掉最老的那个。</summary>
    public const int MaxActiveRipples = 6;

    /// <summary>每个涟漪的圈数：内外各一圈细环，再多就糊成一片了。</summary>
    public const int RingCount = 2;

    /// <summary>单次推进最多吃掉的秒数：窗口被拖住/断点停住后回来，涟漪不能瞬移一大段。</summary>
    public const double MaxStepSeconds = 0.1d;

    /// <summary>不透明度的衰减指数：大于 1 让尾巴掉得更快，收尾才不拖泥带水。</summary>
    private const double AlphaExponent = 1.6d;

    /// <summary>内圈比外圈晚起步的秒数；错位才有「水波」感。</summary>
    private const double Ring1DelaySeconds = 0.09d;

    /// <summary>各圈的起始半径（DIP）；下标即 ring。</summary>
    private static readonly double[] RingStartRadius = [6d, 3d];

    /// <summary>各圈的终止半径（DIP）；下标即 ring。</summary>
    private static readonly double[] RingEndRadius = [44d, 26d];

    /// <summary>各圈的起始不透明度；下标即 ring。</summary>
    private static readonly double[] RingStartAlpha = [0.30d, 0.22d];

    /// <summary>各圈的起步延迟（秒）；外圈立刻起步，内圈错开 <see cref="Ring1DelaySeconds"/>。</summary>
    private static readonly double[] RingDelaySeconds = [0d, Ring1DelaySeconds];

    private readonly List<Ripple> _ripples = [];

    /// <summary>当前在飞的涟漪数量（0–<see cref="MaxActiveRipples"/>）。</summary>
    public int Count => _ripples.Count;

    /// <summary>是否已经静止：为真时渲染层可以停表，不再逐帧 <see cref="Advance"/>。</summary>
    public bool IsIdle => _ripples.Count == 0;

    /// <summary>
    /// 在指定落点放一个涟漪；超过 <see cref="MaxActiveRipples"/> 时挤掉最老的那个。
    /// </summary>
    /// <param name="x">落点横坐标（相对涟漪层，DIP）。</param>
    /// <param name="y">落点纵坐标（相对涟漪层，DIP）。</param>
    public void Spawn(double x, double y)
    {
        // 转发的坐标来自指针事件，理论上有限；NaN/无穷只会来自被算坏的布局，画出来就是废帧。
        if (!double.IsFinite(x) || !double.IsFinite(y))
        {
            return;
        }

        if (_ripples.Count >= MaxActiveRipples)
        {
            _ripples.RemoveAt(0);
        }

        _ripples.Add(new Ripple(x, y));
    }

    /// <summary>
    /// 推进一帧：所有涟漪的年龄一起长大，活满 <see cref="LifetimeSeconds"/> 的被移除。
    /// </summary>
    /// <param name="deltaSeconds">距上一帧的秒数；NaN、无穷、0 与负值都不推进。</param>
    public void Advance(double deltaSeconds)
    {
        if (!double.IsFinite(deltaSeconds) || deltaSeconds <= 0)
        {
            return;
        }

        var step = Math.Min(deltaSeconds, MaxStepSeconds);

        // 倒着走：RemoveAt 不会打乱还没访问到的下标，也就不用额外压缩数组。
        for (var i = _ripples.Count - 1; i >= 0; i--)
        {
            var ripple = _ripples[i];
            ripple.Age += step;

            if (ripple.Age >= LifetimeSeconds)
            {
                _ripples.RemoveAt(i);
                continue;
            }

            _ripples[i] = ripple;
        }
    }

    /// <summary>立刻清空所有涟漪（窗口隐藏、切页、退出前调用）。</summary>
    public void Clear() => _ripples.Clear();

    /// <summary>涟漪的落点横坐标（相对涟漪层，DIP）。</summary>
    /// <param name="index">涟漪下标，0 是最老的那个。</param>
    /// <returns>落点横坐标。</returns>
    public double RippleX(int index) => _ripples[index].X;

    /// <summary>涟漪的落点纵坐标（相对涟漪层，DIP）。</summary>
    /// <param name="index">涟漪下标，0 是最老的那个。</param>
    /// <returns>落点纵坐标。</returns>
    public double RippleY(int index) => _ripples[index].Y;

    /// <summary>第 <paramref name="ring"/> 圈当前半径（DIP）。</summary>
    /// <param name="index">涟漪下标。</param>
    /// <param name="ring">圈下标：0 外圈、1 内圈；越界值按边界圈处理。</param>
    /// <returns>半径；这一圈还没起步时返回 0。</returns>
    public double RingRadius(int index, int ring)
    {
        var slot = NormalizeRing(ring);
        var age = _ripples[index].Age;
        if (age < RingDelaySeconds[slot])
        {
            return 0;
        }

        var eased = EaseOutCubic(RingProgress(age, slot));
        return RingStartRadius[slot] + ((RingEndRadius[slot] - RingStartRadius[slot]) * eased);
    }

    /// <summary>第 <paramref name="ring"/> 圈当前不透明度。</summary>
    /// <param name="index">涟漪下标。</param>
    /// <param name="ring">圈下标：0 外圈、1 内圈；越界值按边界圈处理。</param>
    /// <returns>0–1 之间的不透明度；还没起步或已经散尽时返回 0。</returns>
    public double RingAlpha(int index, int ring)
    {
        var slot = NormalizeRing(ring);
        var age = _ripples[index].Age;
        if (age < RingDelaySeconds[slot])
        {
            return 0;
        }

        var progress = RingProgress(age, slot);
        var alpha = RingStartAlpha[slot] * Math.Pow(1 - progress, AlphaExponent);
        return Math.Clamp(alpha, 0, 1);
    }

    private static int NormalizeRing(int ring) => Math.Clamp(ring, 0, RingCount - 1);

    /// <summary>这一圈在自己的窗口内走到哪了（0–1）；内圈要把起步延迟从分母里扣掉。</summary>
    private static double RingProgress(double age, int slot)
    {
        var delay = RingDelaySeconds[slot];
        var window = LifetimeSeconds - delay;
        return Math.Clamp((age - delay) / window, 0, 1);
    }

    private static double EaseOutCubic(double t) => 1 - Math.Pow(1 - t, 3);

    /// <summary>一个涟漪的落点与已存活秒数；用结构体让 <see cref="Spawn"/> 不产生堆分配。</summary>
    private struct Ripple
    {
        /// <summary>落点横坐标（DIP）。</summary>
        public double X;

        /// <summary>落点纵坐标（DIP）。</summary>
        public double Y;

        /// <summary>已存活秒数。</summary>
        public double Age;

        public Ripple(double x, double y)
        {
            X = x;
            Y = y;
            Age = 0;
        }
    }
}
