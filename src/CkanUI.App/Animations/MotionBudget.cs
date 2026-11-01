using System;

using Avalonia;

using CkanUI.App.Theming;

namespace CkanUI.App.Animations;

/// <summary>动效强度档位。</summary>
public enum MotionMode
{
    /// <summary>关闭：一律不动。</summary>
    None = 0,

    /// <summary>精简：只压幅度，不排队、不做整页编排。</summary>
    Reduced = 1,

    /// <summary>完整：错峰、编排、全幅度。</summary>
    Full = 2,
}

/// <summary>
/// 从时长 token 反推动效强度，并把"设计幅度"按档位压缩。
/// </summary>
/// <remarks>
/// 档位不在代码里存第二份开关：<see cref="MotionHost"/> 写进资源字典的 Fast/Page 时长就是唯一事实。
/// 另存一份布尔量必然与设置页脱节，那类漂移最后都变成"我明明关了动效它还在动"。
/// </remarks>
public static class MotionBudget
{
    /// <summary>精简档保留的幅度比例。</summary>
    public const double ReducedAmplitudeScale = 0.45d;

    /// <summary>当前档位。</summary>
    public static MotionMode Mode
    {
        get
        {
            if (!TryRead(MotionHost.PageKey, out var page) || !TryRead(MotionHost.FastKey, out var fast))
            {
                // 资源还没装好（设计期/启动早期）：按完整档走，宁可见到动效也不要误判成"关闭"。
                return MotionMode.Full;
            }

            if (page > TimeSpan.Zero)
            {
                return MotionMode.Full;
            }

            return fast > TimeSpan.Zero ? MotionMode.Reduced : MotionMode.None;
        }
    }

    /// <summary>是否完全关闭。</summary>
    public static bool IsCollapsed => Mode == MotionMode.None;

    /// <summary>整页编排只在完整档执行。</summary>
    public static bool AllowsPageChoreography => Mode == MotionMode.Full;

    /// <summary>幅度倍率。</summary>
    public static double AmplitudeScale => Mode switch
    {
        MotionMode.Full => 1d,
        MotionMode.Reduced => ReducedAmplitudeScale,
        _ => 0d,
    };

    /// <summary>按档位缩放一个位移幅度。</summary>
    public static double Amplitude(double value) => value * AmplitudeScale;

    /// <summary>按档位把缩放从原值向 1 收拢；<paramref name="collapse"/> 是关闭档的落点。</summary>
    public static double Scaled(double scale, double collapse = 1d) =>
        collapse + ((scale - collapse) * AmplitudeScale);

    private static bool TryRead(string key, out TimeSpan value)
    {
        value = default;
        return Application.Current?.Resources.TryGetResource(key, null, out var raw) == true
            && raw is TimeSpan span
            && (value = span) >= TimeSpan.Zero;
    }
}
