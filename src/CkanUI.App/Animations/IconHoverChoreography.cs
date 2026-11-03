using System;

namespace CkanUI.App.Animations;

/// <summary>转动的支点：中心转是"晃"，贴着顶边转是"挂"。</summary>
internal enum IconPivot
{
    /// <summary>绕图标中心转。</summary>
    Center = 0,

    /// <summary>绕上沿转：像挂在钉子上的小牌子。</summary>
    TopHinge = 1,
}

/// <summary>一帧的目标位姿：只说"要摆成什么样"，不说弹簧怎么追过去。</summary>
internal readonly record struct IconHoverPose(
    double Tilt,
    double Scale,
    double Slide,
    double Lift,
    bool ExactTilt = false)
{
    /// <summary>原点：不转、原大、不偏。</summary>
    public static IconHoverPose Neutral => new(0d, 1d, 0d, 0d);
}

/// <summary>进场那一下：支点、目标位姿，外加可选的一发冲量（挤压与心跳靠冲量，不靠目标位）。</summary>
internal readonly record struct IconHoverEntry(
    IconPivot Pivot,
    IconHoverPose Pose,
    double ImpulseX = 0d,
    double ImpulseY = 0d);

/// <summary>一帧的状态：进场段只有幅度，持续段还要时间切片、相位与这根轨道自己的性格。</summary>
internal readonly record struct IconHoverBeat(
    double Elapsed,
    double Slice,
    double Amplitude,
    double Character,
    double Phase)
{
    /// <summary>按周期取正弦，叠上相位。</summary>
    public double Wave(double period) => Math.Sin(2d * Math.PI * ((Elapsed / period) + Phase));

    /// <summary>按档位与性格缩放一个幅度：同形态的两颗图标不该摆得一模一样。</summary>
    public double Jittered(double value) => value * Amplitude * (0.82d + (0.36d * Character));

    /// <summary>按性格微调周期：幅度之外，快慢也要各走各的。</summary>
    public double JitteredPeriod(double period) => period * (0.88d + (0.24d * Character));

    /// <summary>把缩放从原值向 1 收拢（精简档只压幅度、不改变落点）。</summary>
    public double Scaled(double scale) => 1d + ((scale - 1d) * Amplitude);
}

/// <summary>
/// 一种形态的编排：进场怎么起、持续怎么动、离开停在哪。
/// </summary>
/// <remarks>
/// <para>每种形态只回答"这一刻要摆成什么样"，不碰弹簧、不排帧、不认识 <see cref="Avalonia.Controls"/>。
/// 帧循环、引用计数、宽限期、脏区续命都留在 <see cref="IconHoverTrack"/>，
/// 所以加一种形态只需要写一个类，不用在几百行的轨道里再切一刀 <c>switch</c>。</para>
/// <para>有状态的形态（旋转要积角度）自己存字段，一次悬停一份实例；轨道换形态时按需新建。</para>
/// </remarks>
internal abstract class IconHoverChoreography
{
    /// <summary>绕哪儿转：默认绕中心，钟摆这类改绕上沿。</summary>
    public virtual IconPivot Pivot => IconPivot.Center;

    /// <summary>有没有持续段；挤压这类"进场即结束"的形态没有。</summary>
    public virtual bool Sustains => true;

    /// <summary>每次进场前清一次自己的状态。</summary>
    public virtual void Reset()
    {
    }

    /// <summary>进场目标位姿。</summary>
    /// <param name="beat">状态。</param>
    public abstract IconHoverEntry Enter(IconHoverBeat beat);

    /// <summary>持续段的一帧目标位姿。</summary>
    /// <param name="beat">状态。</param>
    public abstract IconHoverPose Sustain(IconHoverBeat beat);

    /// <summary>离开时该停在哪个角度：旋转形态顺转到整圈，其余回零。</summary>
    /// <param name="tilt">当前角度。</param>
    public virtual double FinishTilt(double tilt) => 0d;
}

/// <summary>形态 → 编排。</summary>
internal static class IconHoverMotions
{
    /// <summary>取一种形态的编排。</summary>
    /// <param name="shape">形态。</param>
    public static IconHoverChoreography Create(IconHoverShape shape) => shape switch
    {
        IconHoverShape.Spin => new SpinChoreography(),
        IconHoverShape.Sway => new SwayChoreography(),
        IconHoverShape.Float => new FloatChoreography(),
        IconHoverShape.Nudge => new NudgeChoreography(),
        IconHoverShape.Pulse => new PulseChoreography(),
        IconHoverShape.Snap => new SnapChoreography(),
        _ => new WaggleChoreography(),
    };
}

/// <summary>通用摇摆：抬起一点角度，然后小幅摆；周期与幅度带各自性格。</summary>
internal sealed class WaggleChoreography : IconHoverChoreography
{
    /// <inheritdoc/>
    public override IconHoverEntry Enter(IconHoverBeat beat) => new(
        IconPivot.Center,
        new IconHoverPose(
            beat.Jittered(IconHoverTrack.WaggleEntranceTiltDegrees),
            beat.Scaled(IconHoverTrack.WaggleEntranceScale),
            0d,
            0d));

    /// <inheritdoc/>
    public override IconHoverPose Sustain(IconHoverBeat beat)
    {
        var wave = beat.Wave(beat.JitteredPeriod(IconHoverTrack.WagglePeriodSeconds));
        return new IconHoverPose(
            IconHoverTrack.WaggleEntranceTiltDegrees + (wave * beat.Jittered(IconHoverTrack.WaggleAmplitudeDegrees)),
            beat.Scaled(IconHoverTrack.WaggleEntranceScale),
            0d,
            0d);
    }
}

/// <summary>旋转：持续段按角速度积分，离开时顺转到最近的整圈，不倒着退回去。</summary>
internal sealed class SpinChoreography : IconHoverChoreography
{
    private double _rate;
    private double _turns;

    /// <inheritdoc/>
    public override void Reset()
    {
        _rate = 0d;
        _turns = 0d;
    }

    /// <inheritdoc/>
    public override IconHoverEntry Enter(IconHoverBeat beat) => new(
        IconPivot.Center,
        new IconHoverPose(
            IconHoverTrack.SpinEntranceTiltDegrees,
            beat.Scaled(IconHoverTrack.SpinEntranceScale),
            0d,
            0d));

    /// <inheritdoc/>
    public override IconHoverPose Sustain(IconHoverBeat beat)
    {
        var topSpeed = IconHoverTrack.SpinDegreesPerSecond * beat.Amplitude;
        _rate = Math.Min(topSpeed, _rate + (topSpeed / IconHoverTrack.SpinRampSeconds * beat.Slice));
        _turns += _rate * beat.Slice;
        return new IconHoverPose(_turns, beat.Scaled(IconHoverTrack.SpinEntranceScale), 0d, 0d, ExactTilt: true);
    }

    /// <inheritdoc/>
    public override double FinishTilt(double tilt) => tilt > 0d ? Math.Ceiling(tilt / 360d) * 360d : 0d;
}

/// <summary>钟摆：支点挪到上沿，先摆到一侧再绕回来。</summary>
internal sealed class SwayChoreography : IconHoverChoreography
{
    /// <inheritdoc/>
    public override IconPivot Pivot => IconPivot.TopHinge;

    /// <inheritdoc/>
    public override IconHoverEntry Enter(IconHoverBeat beat) => new(
        IconPivot.TopHinge,
        new IconHoverPose(
            beat.Jittered(IconHoverTrack.SwayEntranceTiltDegrees),
            beat.Scaled(IconHoverTrack.SwayEntranceScale),
            0d,
            0d));

    /// <inheritdoc/>
    public override IconHoverPose Sustain(IconHoverBeat beat)
    {
        var wave = beat.Wave(beat.JitteredPeriod(IconHoverTrack.SwayPeriodSeconds));
        return new IconHoverPose(
            (IconHoverTrack.SwayEntranceTiltDegrees * 0.3d) + (wave * beat.Jittered(IconHoverTrack.SwayAmplitudeDegrees)),
            beat.Scaled(IconHoverTrack.SwayEntranceScale),
            0d,
            0d);
    }
}

/// <summary>上浮呼吸：离地一点，再轻轻起伏；只走位移与缩放。</summary>
internal sealed class FloatChoreography : IconHoverChoreography
{
    /// <inheritdoc/>
    public override IconHoverEntry Enter(IconHoverBeat beat) => new(
        IconPivot.Center,
        new IconHoverPose(0d, beat.Scaled(IconHoverTrack.FloatEntranceScale), 0d, -beat.Jittered(IconHoverTrack.FloatLiftPixels)));

    /// <inheritdoc/>
    public override IconHoverPose Sustain(IconHoverBeat beat)
    {
        var wave = beat.Wave(beat.JitteredPeriod(IconHoverTrack.FloatPeriodSeconds));
        return new IconHoverPose(
            0d,
            beat.Scaled(IconHoverTrack.FloatEntranceScale) + (wave * 0.015d * beat.Amplitude),
            0d,
            -(beat.Jittered(IconHoverTrack.FloatLiftPixels) + (wave * IconHoverTrack.FloatBobPixels * beat.Amplitude)));
    }
}

/// <summary>轻推：沿水平轴顶一下再回弹，提示"这块能点"。</summary>
internal sealed class NudgeChoreography : IconHoverChoreography
{
    /// <inheritdoc/>
    public override IconHoverEntry Enter(IconHoverBeat beat) => new(
        IconPivot.Center,
        new IconHoverPose(0d, beat.Scaled(IconHoverTrack.NudgeEntranceScale), beat.Jittered(IconHoverTrack.NudgePixels), 0d));

    /// <inheritdoc/>
    public override IconHoverPose Sustain(IconHoverBeat beat)
    {
        var cycle = beat.Elapsed % beat.JitteredPeriod(IconHoverTrack.NudgePeriodSeconds);
        var push = cycle < IconHoverTrack.NudgePushSeconds
            ? Math.Sin(Math.PI * cycle / IconHoverTrack.NudgePushSeconds)
            : 0d;

        return new IconHoverPose(0d, beat.Scaled(IconHoverTrack.NudgeEntranceScale), beat.Jittered(push * IconHoverTrack.NudgePixels), 0d);
    }
}

/// <summary>心跳：一拍重的、一拍轻的，然后停半拍；只缩放，不转。</summary>
internal sealed class PulseChoreography : IconHoverChoreography
{
    private const double FirstBeatSeconds = 0.42d;
    private const double SecondBeatStart = 0.52d;
    private const double SecondBeatSeconds = 0.34d;
    private const double SecondBeatScale = 0.55d;

    /// <inheritdoc/>
    public override IconHoverEntry Enter(IconHoverBeat beat)
    {
        var peak = IconHoverTrack.PulsePeakScale * beat.Amplitude;
        return new IconHoverEntry(IconPivot.Center, new IconHoverPose(0d, 1d, 0d, 0d), peak, peak);
    }

    /// <inheritdoc/>
    public override IconHoverPose Sustain(IconHoverBeat beat)
    {
        var scale = 1d + (IconHoverTrack.PulsePeakScale * Beat(beat.Elapsed % beat.JitteredPeriod(IconHoverTrack.PulsePeriodSeconds)) * beat.Amplitude);
        return new IconHoverPose(0d, scale, 0d, 0d);
    }

    private static double Beat(double cycle) => cycle switch
    {
        < FirstBeatSeconds => Math.Sin(Math.PI * cycle / FirstBeatSeconds),
        > SecondBeatStart and < (SecondBeatStart + SecondBeatSeconds) =>
            SecondBeatScale * Math.Sin(Math.PI * (cycle - SecondBeatStart) / SecondBeatSeconds),
        _ => 0d,
    };
}

/// <summary>挤压：进场横向拉宽、纵向压扁，弹回来就结束——要脆不要黏。</summary>
internal sealed class SnapChoreography : IconHoverChoreography
{
    /// <inheritdoc/>
    public override bool Sustains => false;

    /// <inheritdoc/>
    public override IconHoverEntry Enter(IconHoverBeat beat)
    {
        var squash = IconHoverTrack.SnapSquash * beat.Amplitude;
        return new IconHoverEntry(IconPivot.Center, new IconHoverPose(0d, 1d, 0d, 0d), squash, -squash);
    }

    /// <inheritdoc/>
    public override IconHoverPose Sustain(IconHoverBeat beat) => IconHoverPose.Neutral;
}
