using System;

namespace CkanUI.App.Animations;

/// <summary>
/// 一根弹簧的物理参数：质量、刚度、阻尼。阻尼比由三者推出，不手写"回弹百分比"。
/// </summary>
public readonly record struct SpringSpec
{
    public SpringSpec(double mass, double stiffness, double damping)
    {
        if (mass <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(mass), mass, "质量必须为正");
        }

        if (stiffness <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(stiffness), stiffness, "刚度必须为正");
        }

        if (damping < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(damping), damping, "阻尼不能为负");
        }

        Mass = mass;
        Stiffness = stiffness;
        Damping = damping;
    }

    public double Mass { get; }

    public double Stiffness { get; }

    public double Damping { get; }

    /// <summary>无阻尼角频率 ω₀（rad/s）。</summary>
    public double AngularFrequency => Math.Sqrt(Stiffness / Mass);

    /// <summary>阻尼比 ζ：1 为临界阻尼，小于 1 会过冲。</summary>
    public double DampingRatio => Damping / (2 * Math.Sqrt(Stiffness * Mass));

    /// <summary>临界阻尼：位移不过冲，用于胶囊横向移动。</summary>
    public static SpringSpec Glide => new(1, 380, 39);

    /// <summary>欠阻尼：约 3% 过冲，用于宽度/缩放的回弹。</summary>
    public static SpringSpec Snappy => new(1, 420, 30);

    /// <summary>回弹更明显的一档。</summary>
    public static SpringSpec Bounce => new(1, 560, 26);

    /// <summary>冲量：校验抖动，峰值严格等于设计幅度。</summary>
    public static SpringSpec Impulse => new(1, 650, 14);

    /// <summary>触感：按压/悬停这类高频小位移，收敛快、不拖尾。</summary>
    public static SpringSpec Tactile => new(1, 900, 45);
}
