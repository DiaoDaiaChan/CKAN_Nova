using System;
using System.Diagnostics;
using System.Runtime.CompilerServices;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.VisualTree;

namespace CkanUI.App.Animations;

/// <summary>
/// 单个图标的悬浮动效：进场起势、持续循环、离开落位。形态怎么动交给 <see cref="IconHoverChoreography"/>，
/// 这条轨道只管什么时候动、动多久、动完怎么收。
/// </summary>
/// <remarks>
/// <para>只用 RotateTransform / ScaleTransform / TranslateTransform，不碰布局尺寸；RenderTransform 只赋值一次，
/// 之后逐帧改子属性，避免样式里的过渡被每帧当成新目标重启。三种通道按形态取舍：
/// 旋转类只用角度，呼吸类只用位移与缩放，挤压类只打一发冲量。</para>
/// <para>无限循环没法用样式过渡表达，所以这条轨道自己排 <c>RequestAnimationFrame</c>：
/// 只要 <see cref="Advance"/> 还返回 true 就继续要下一帧。轨道自持一副时钟，
/// 不依赖窗口级帧泵，动效目录里的公共部件改写时也不会把图标这边带崩。</para>
/// <para>同形态的图标不同频：相位与性格两个种子都取自图标在布局里的位置，
/// 一排动作按钮不会像仪仗队那样踩着同一个拍子、摆着同一个幅度。</para>
/// </remarks>
public sealed class IconHoverTrack
{
    /// <summary>摇摆与旋转共用的进场抬起角度（度）。</summary>
    public const double WaggleEntranceTiltDegrees = 8d;

    /// <summary>摇摆与旋转共用的进场放大倍率。</summary>
    public const double WaggleEntranceScale = 1.14d;

    /// <summary>旋转形态的进场抬起角度（度）。</summary>
    public const double SpinEntranceTiltDegrees = 8d;

    /// <summary>旋转形态的进场放大倍率。</summary>
    public const double SpinEntranceScale = 1.14d;

    /// <summary>钟摆进场时先摆到一侧的角度（度）。</summary>
    public const double SwayEntranceTiltDegrees = 10d;

    /// <summary>钟摆进场时的放大倍率。</summary>
    public const double SwayEntranceScale = 1.06d;

    /// <summary>呼吸形态进场时的放大倍率。</summary>
    public const double FloatEntranceScale = 1.07d;

    /// <summary>轻推形态进场时的放大倍率。</summary>
    public const double NudgeEntranceScale = 1.06d;

    /// <summary>展开形态进场时的横向放大倍率。</summary>
    public const double UnfoldEntranceScaleX = 1.07d;

    /// <summary>展开形态进场时的纵向放大倍率（等比饱满，收敛在包围盒内，绝不上溢截断与失真）。</summary>
    public const double UnfoldEntranceScaleY = 1.07d;

    /// <summary>展开形态进场抬升高度（像素）。</summary>
    public const double UnfoldEntranceLiftPixels = 1.6d;

    /// <summary>展开形态呼吸周期（秒）。</summary>
    public const double UnfoldPeriodSeconds = 1.6d;

    /// <summary>机架抽屉形态进场放大的倍率。</summary>
    public const double RackEntranceScale = 1.06d;

    /// <summary>机架抽屉形态滑出的距离（像素，控制在微交互安全区，绝不越界截断）。</summary>
    public const double RackSlidePixels = 1.6d;

    /// <summary>机架抽屉形态沉底压实的高度（像素）。</summary>
    public const double RackLiftPixels = 0.0d;

    /// <summary>机架抽屉形态寻道周期（秒）。</summary>
    public const double RackPeriodSeconds = 1.1d;

    /// <summary>穿梭形态进场放大的倍率。</summary>
    public const double ShuttleEntranceScale = 1.06d;

    /// <summary>穿梭形态左右滑行的最大距离（像素，对流往返，绝不超界切断箭头尖端）。</summary>
    public const double ShuttleSlidePixels = 1.7d;

    /// <summary>穿梭形态周期（秒）。</summary>
    public const double ShuttlePeriodSeconds = 1.2d;

    /// <summary>雀跃轻跳形态进场放大的倍率。</summary>
    public const double BounceEntranceScale = 1.07d;

    /// <summary>雀跃轻跳形态向上跃起的高度（像素，轻快而不冲出天花板）。</summary>
    public const double BounceLiftPixels = 1.7d;

    /// <summary>雀跃轻跳形态跳动周期（秒）。</summary>
    public const double BouncePeriodSeconds = 1.4d;

    /// <summary>进场抬起的角度（度）。</summary>
    public const double EnterTiltDegrees = WaggleEntranceTiltDegrees;

    /// <summary>进场放大的倍率。</summary>
    public const double EnterScale = WaggleEntranceScale;

    /// <summary>悬停多久后进入循环（280ms 灵敏过渡，告别漫长呆滞感）。</summary>
    public const double SustainDelaySeconds = 0.28d;

    /// <summary>旋转形态的稳态角速度（度/秒）。</summary>
    public const double SpinDegreesPerSecond = 200d;

    /// <summary>旋转从静止加速到稳态所需秒数。</summary>
    public const double SpinRampSeconds = 0.4d;

    /// <summary>摇摆形态的幅度（度）。</summary>
    public const double WaggleAmplitudeDegrees = 3.2d;

    /// <summary>摇摆形态的周期（秒）。</summary>
    public const double WagglePeriodSeconds = 1.6d;

    /// <summary>钟摆形态的幅度（度）：比摇摆大，因为它绕上沿画圆弧。</summary>
    public const double SwayAmplitudeDegrees = 7d;

    /// <summary>钟摆形态的周期（秒）：摆得慢才像被风推着的牌子。</summary>
    public const double SwayPeriodSeconds = 2.1d;

    /// <summary>钟摆进场时先摆到一侧的角度（度）。</summary>
    public const double SwayTiltDegrees = SwayEntranceTiltDegrees;

    /// <summary>呼吸形态离地的高度（像素）。</summary>
    public const double FloatLiftPixels = 3.5d;

    /// <summary>呼吸形态起伏的幅度（像素）。</summary>
    public const double FloatBobPixels = 1.4d;

    /// <summary>呼吸形态的周期（秒）。</summary>
    public const double FloatPeriodSeconds = 2.4d;

    /// <summary>轻推形态前顶的距离（像素）。</summary>
    public const double NudgePixels = 4d;

    /// <summary>轻推一次冲出去所需秒数。</summary>
    public const double NudgePushSeconds = 0.45d;

    /// <summary>轻推的完整节拍（秒）：推一下，停一拍，再推一下。</summary>
    public const double NudgePeriodSeconds = 1.3d;

    /// <summary>心跳形态单拍的缩放幅度。</summary>
    public const double PulsePeakScale = 0.085d;

    /// <summary>心跳形态的完整周期（秒，双拍）。</summary>
    public const double PulsePeriodSeconds = 1.6d;

    /// <summary>挤压形态的挤压幅度：进场靠冲量打出来，不留在稳态上。</summary>
    public const double SnapSquash = 0.13d;

    /// <summary>指针离开后宽限多久再收（秒）：弹层、提示卡抢走指针时不至于把循环打断。</summary>
    public const double LeaveGraceSeconds = 0.2d;

    private const double MinFrameSeconds = 1.0 / 240.0;
    private const double MaxFrameSeconds = 1.0 / 30.0;

    private static readonly ConditionalWeakTable<Control, IconHoverTrack> Shared = new();

    private readonly Control _target;
    private readonly TransformGroup _group = new();
    private readonly TranslateTransform _translate = new();
    private readonly RotateTransform _rotate = new();
    private readonly ScaleTransform _scale = new() { ScaleX = 1, ScaleY = 1 };
    private readonly SpringChannel _angle;
    private readonly SpringChannel _scaleX;
    private readonly SpringChannel _scaleY;
    private readonly SpringChannel _offsetX;
    private readonly SpringChannel _offsetY;

    private TopLevel? _topLevel;
    private IconHoverShape _shape = IconHoverShape.Waggle;
    private IconHoverChoreography _choreography = IconHoverMotions.Create(IconHoverShape.Waggle);
    private int _hoverCount;
    private double _sustained;
    private double _phase;
    private double _character;
    private double _leaveGrace;
    private bool _scheduled;
    private long _last;

    /// <summary>给某个图标建一根轨道，并把它的 RenderTransform 收归己有。</summary>
    public IconHoverTrack(Control target)
    {
        ArgumentNullException.ThrowIfNull(target);

        _target = target;
        _target.ClipToBounds = false;
        UnclipAncestors(_target);

        _group.Children.Add(_translate);
        _group.Children.Add(_rotate);
        _group.Children.Add(_scale);
        _target.RenderTransform = _group;

        if (_target.RenderTransformOrigin.Equals(RelativePoint.TopLeft))
        {
            _target.RenderTransformOrigin = Center;
        }

        _angle = new SpringChannel(0, SpringSpec.Snappy, 0.05, 2.0);
        _scaleX = new SpringChannel(1, SpringSpec.Snappy, 0.0004, 0.005);
        _scaleY = new SpringChannel(1, SpringSpec.Snappy, 0.0004, 0.005);
        _offsetX = new SpringChannel(0, SpringSpec.Glide, 0.01, 0.5);
        _offsetY = new SpringChannel(0, SpringSpec.Glide, 0.01, 0.5);
    }

    private static void UnclipAncestors(Control target)
    {
        Visual? current = target.GetVisualParent();
        var depth = 0;
        while (current is not null && depth < 4)
        {
            if (current is Control ctrl)
            {
                ctrl.ClipToBounds = false;
            }
            current = current.GetVisualParent();
            depth++;
        }
    }

    /// <summary>取某个图标共享的轨道；没有就建一根。</summary>
    /// <param name="icon">图标控件。</param>
    public static IconHoverTrack For(Control icon)
    {
        ArgumentNullException.ThrowIfNull(icon);
        return Shared.GetValue(icon, static control => new IconHoverTrack(control));
    }

    /// <summary>当前旋转角度（度）。</summary>
    public double Angle => _angle.Value;

    /// <summary>当前缩放倍率。</summary>
    public double Scale => _scaleY.Value;

    /// <summary>当前水平位移（像素）。</summary>
    public double OffsetX => _offsetX.Value;

    /// <summary>当前竖直位移（像素，向上为负）。</summary>
    public double OffsetY => _offsetY.Value;

    /// <summary>是否仍有指针落在它（或它的宿主）上。</summary>
    public bool IsHovered => _hoverCount > 0;

    private static RelativePoint Center => new(0.5, 0.5, RelativeUnit.Relative);

    /// <summary>钟摆绕上沿摆：支点挪到贴着顶边的地方，才摆得出"挂着"的弧度。</summary>
    private static RelativePoint TopHinge => new(0.5, 0.04, RelativeUnit.Relative);

    /// <summary>展开形态绕底边转：支点挪到贴着底边的地方，才掀得出文件夹翻盖的纵向开合。</summary>
    private static RelativePoint BottomHinge => new(0.5, 0.96, RelativeUnit.Relative);

    /// <summary>指针进入：按形态起势，并记下持续阶段的形态。</summary>
    /// <param name="shape">形态。</param>
    public void Enter(IconHoverShape shape)
    {
        if (MotionBudget.IsCollapsed)
        {
            return;
        }

        _target.ClipToBounds = false;
        UnclipAncestors(_target);

        _hoverCount++;
        if (_hoverCount > 1)
        {
            return;
        }

        // 离开宽限期里又回来（侧栏弹层、悬浮卡片会把指针短暂抢走）：接着动，不要从头再数 0.55s。
        var resumed = _leaveGrace > 0;
        _leaveGrace = 0;

        var resolved = shape == IconHoverShape.Auto ? IconHoverShape.Waggle : shape;
        if (resolved != _shape)
        {
            _shape = resolved;
            _choreography = IconHoverMotions.Create(resolved);
        }

        if (!resumed)
        {
            _sustained = 0;
            _phase = PhaseSeed();
            _character = CharacterSeed();
            _choreography.Reset();
        }

        // 上次是转着走的，值可能停在 360 的整数倍上：先归一到 (-180,180]，视觉等价，不会倒转一圈。
        _angle.SnapTo(Normalize(_angle.Value));
        BeginEntry();
        Start();
    }

    /// <summary>指针离开：旋转形态顺转到下一个整圈再停，其余顺回原位。</summary>
    public void Exit()
    {
        if (_hoverCount == 0)
        {
            return;
        }

        _hoverCount--;
        if (_hoverCount > 0)
        {
            return;
        }

        // 先记一个宽限期，等 <see cref="Advance"/> 里确认真的没人再碰它，才落回原位。
        _leaveGrace = LeaveGraceSeconds;
        Start();
    }

    /// <summary>推进一帧；返回是否仍需继续。</summary>
    /// <param name="seconds">距离上一帧的秒数。</param>
    public bool Advance(double seconds)
    {
        var top = _topLevel;
        if (top is null || !ReferenceEquals(TopLevel.GetTopLevel(_target), top))
        {
            SnapHome();
            return false;
        }

        if (MotionBudget.Mode == MotionMode.None)
        {
            SnapHome();
            return false;
        }

        var moving = _angle.Advance(seconds);
        moving |= _scaleX.Advance(seconds);
        moving |= _scaleY.Advance(seconds);
        moving |= _offsetX.Advance(seconds);
        moving |= _offsetY.Advance(seconds);

        // 精简档只是压幅度，不是关掉反馈：长悬停的循环照样要动，动得慢一点、幅度小一点。
        var amplitude = MotionBudget.AmplitudeScale;
        var slice = Math.Min(seconds, 0.05);
        var looping = false;

        // 宽限期当作"还贴着"：指针被弹层短暂抢走时，0.55s 的持续窗口照样往前走。
        if (_hoverCount > 0)
        {
            _leaveGrace = LeaveGraceSeconds;
        }
        else if (_leaveGrace > 0)
        {
            _leaveGrace = Math.Max(0, _leaveGrace - slice);
            if (_leaveGrace == 0)
            {
                GoHome();
                moving = true;
            }
        }

        if ((_hoverCount > 0 || _leaveGrace > 0) && MotionBudget.Mode != MotionMode.None)
        {
            _sustained += slice;

            if (_sustained >= SustainDelaySeconds)
            {
                var beat = new IconHoverBeat(_sustained - SustainDelaySeconds, slice, amplitude, _character, _phase);
                ApplyPose(_choreography.Sustain(beat));
                looping = _choreography.Sustains;
            }
        }

        Apply();

        // 悬浮还在、动效还开着，就还有"过一会儿要进入循环"这件事要做：
        // 弹簧往往比 0.55s 的阈值先收敛，这里要是停帧，长悬停就永远进不了循环（齿轮于是只抬一下不转）。
        // 挤压形态没有循环，起势那一发冲量落稳就该停帧，不能空转着每秒 60 次失效重绘。
        var waiting = (_hoverCount > 0 || _leaveGrace > 0)
            && MotionBudget.Mode != MotionMode.None
            && _choreography.Sustains;
        var keepGoing = moving || looping || waiting;
        if (keepGoing)
        {
            // 弹簧收敛后画面没有脏区，合成器就不出新帧，rAF 链条会断在等待期、收尾段或循环起点：
            // 每帧主动失效一次把它续上，否则长悬停只会抬一下（齿轮不转、摇摆不动），离开也回不了家。
            _target.InvalidateVisual();
        }
        else
        {
            _topLevel = null;
            _last = 0;
        }

        return keepGoing;
    }

    /// <summary>按形态起势：不同形态动的是不同通道，别让所有图标都从同一个"抬 8° 放大 1.14"开始。</summary>
    private void BeginEntry()
    {
        var beat = new IconHoverBeat(0, 0, MotionBudget.AmplitudeScale, _character, _phase);
        var entry = _choreography.Enter(beat);

        _target.RenderTransformOrigin = entry.Pivot switch
        {
            IconPivot.TopHinge => TopHinge,
            IconPivot.BottomHinge => BottomHinge,
            _ => Center,
        };

        if (entry.ImpulseX != 0 || entry.ImpulseY != 0)
        {
            _scaleX.SnapTo(1);
            _scaleY.SnapTo(1);
        }

        ApplyPose(entry.Pose);

        if (entry.ImpulseX != 0)
        {
            _scaleX.Kick(entry.ImpulseX);
        }

        if (entry.ImpulseY != 0)
        {
            _scaleY.Kick(entry.ImpulseY);
        }
    }

    /// <summary>把一帧的目标位姿交给弹簧：位姿只说"要摆成什么样"，追得快慢是通道的事。</summary>
    private void ApplyPose(IconHoverPose pose)
    {
        if (pose.ExactTilt)
        {
            _angle.SnapTo(pose.Tilt);
        }
        else
        {
            _angle.Retarget(pose.Tilt);
        }

        _scaleX.Retarget(pose.Scale);
        _scaleY.Retarget(pose.ScaleY > 0d ? pose.ScaleY : pose.Scale);
        _offsetX.Retarget(pose.Slide);
        _offsetY.Retarget(pose.Lift);
    }

    /// <summary>相位种子：同一排图标位置不同，起摆的时机就不同。</summary>
    private double PhaseSeed() => Seed(1d, 0d, 1d);

    /// <summary>性格种子：相位之外的第二个小数，决定摆多大幅度、走多快。</summary>
    private double CharacterSeed() => Seed(3.7d, 0.31d, 1d);

    /// <summary>按图标在布局里的位置算一个 [0,1) 的种子；位置拿不到（NaN/无穷）就退化成 0。</summary>
    private double Seed(double scale, double offset, double spread)
    {
        var bounds = _target.Bounds;
        var raw = (bounds.X * 0.37d) + (bounds.Y * 0.61d) + (bounds.Width * 0.19d);
        if (double.IsNaN(raw) || double.IsInfinity(raw))
        {
            return 0;
        }

        var mixed = (raw * scale) + offset;
        var fraction = (mixed * spread) - Math.Floor(mixed * spread);
        return fraction;
    }

    /// <summary>真正收尾：旋转形态顺转到下一个整圈再停，其余顺回原位。</summary>
    private void GoHome()
    {
        _sustained = 0;
        _angle.Retarget(_choreography.FinishTilt(_angle.Value));
        _offsetX.Retarget(0);
        _offsetY.Retarget(0);
        SetScale(1);
    }

    private static double Normalize(double degrees)
    {
        var value = degrees % 360d;
        return value switch
        {
            <= -180d => value + 360d,
            > 180d => value - 360d,
            _ => value,
        };
    }

    private void SetScale(double scale)
    {
        _scaleX.Retarget(scale);
        _scaleY.Retarget(scale);
    }

    private void Start()
    {
        var top = TopLevel.GetTopLevel(_target);
        if (top is null)
        {
            SnapHome();
            return;
        }

        _topLevel = top;

        // 静止画面里没有脏区，合成器就不会出新帧，rAF 也就不会来：先标脏一次把链条点燃。
        _target.InvalidateVisual();
        Schedule();
    }

    private void Schedule()
    {
        var top = _topLevel;
        if (top is null || _scheduled)
        {
            return;
        }

        _scheduled = true;
        top.RequestAnimationFrame(_ =>
        {
            _scheduled = false;
            Tick();
        });
    }

    private void Tick()
    {
        if (_topLevel is null)
        {
            return;
        }

        var now = Stopwatch.GetTimestamp();
        var seconds = _last == 0 ? 1.0 / 60.0 : (now - _last) / (double)Stopwatch.Frequency;
        _last = now;

        if (Advance(Math.Clamp(seconds, MinFrameSeconds, MaxFrameSeconds)))
        {
            Schedule();
        }
    }

    private void SnapHome()
    {
        _angle.SnapTo(0);
        _scaleX.SnapTo(1);
        _scaleY.SnapTo(1);
        _offsetX.SnapTo(0);
        _offsetY.SnapTo(0);
        Apply();
        _topLevel = null;
        _hoverCount = 0;
        _leaveGrace = 0;
        _sustained = 0;
        _choreography.Reset();
        _last = 0;
    }

    private void Apply()
    {
        _rotate.Angle = _angle.Value;
        _scale.ScaleX = _scaleX.Value;
        _scale.ScaleY = _scaleY.Value;
        _translate.X = _offsetX.Value;
        _translate.Y = _offsetY.Value;
    }
}
