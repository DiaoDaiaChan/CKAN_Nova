using System;
using System.Diagnostics;
using System.Runtime.CompilerServices;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace CkanUI.App.Animations;

/// <summary>
/// 单个图标的悬浮动效：进场抬角放大，持续悬浮转成循环，离开后落回原位。
/// </summary>
/// <remarks>
/// <para>只用 RotateTransform 与 ScaleTransform，不碰布局尺寸；RenderTransform 只赋值一次，
/// 之后逐帧改子属性，避免样式里的过渡被每帧当成新目标重启。</para>
/// <para>无限循环没法用样式过渡表达，所以这条轨道自己排 <c>RequestAnimationFrame</c>：
/// 只要 <see cref="Advance"/> 还返回 true 就继续要下一帧。轨道自持一副时钟，
/// 不依赖窗口级帧泵，动效目录里的公共部件改写时也不会把图标这边带崩。</para>
/// </remarks>
public sealed class IconHoverTrack
{
    /// <summary>进场抬起的角度（度）。</summary>
    public const double EnterTiltDegrees = 8d;

    /// <summary>进场放大的倍率。</summary>
    public const double EnterScale = 1.14d;

    /// <summary>悬停多久后进入循环。</summary>
    public const double SustainDelaySeconds = 0.55d;

    /// <summary>旋转形态的稳态角速度（度/秒）。</summary>
    public const double SpinDegreesPerSecond = 200d;

    /// <summary>旋转从静止加速到稳态所需秒数。</summary>
    public const double SpinRampSeconds = 0.4d;

    /// <summary>摇摆形态的幅度（度）。</summary>
    public const double WaggleAmplitudeDegrees = 3.2d;

    /// <summary>摇摆形态的周期（秒）。</summary>
    public const double WagglePeriodSeconds = 1.6d;

    /// <summary>指针离开后宽限多久再收（秒）：弹层、提示卡抢走指针时不至于把循环打断。</summary>
    public const double LeaveGraceSeconds = 0.2d;

    private const double MinFrameSeconds = 1.0 / 240.0;
    private const double MaxFrameSeconds = 1.0 / 30.0;

    private static readonly ConditionalWeakTable<Control, IconHoverTrack> Shared = new();

    private readonly Control _target;
    private readonly TransformGroup _group = new();
    private readonly RotateTransform _rotate = new();
    private readonly ScaleTransform _scale = new() { ScaleX = 1, ScaleY = 1 };
    private readonly SpringChannel _angle;
    private readonly SpringChannel _scaleX;
    private readonly SpringChannel _scaleY;

    private TopLevel? _topLevel;
    private IconHoverShape _shape = IconHoverShape.Waggle;
    private int _hoverCount;
    private double _sustained;
    private double _spinRate;
    private double _spinDegrees;
    private double _wagglePhase;
    private double _leaveGrace;
    private bool _scheduled;
    private long _last;

    /// <summary>给某个图标建一根轨道，并把它的 RenderTransform 收归己有。</summary>
    public IconHoverTrack(Control target)
    {
        ArgumentNullException.ThrowIfNull(target);

        _target = target;
        _group.Children.Add(_rotate);
        _group.Children.Add(_scale);
        _target.RenderTransform = _group;

        if (_target.RenderTransformOrigin.Equals(RelativePoint.TopLeft))
        {
            _target.RenderTransformOrigin = new RelativePoint(0.5, 0.5, RelativeUnit.Relative);
        }

        _angle = new SpringChannel(0, SpringSpec.Snappy, 0.05, 2.0);
        _scaleX = new SpringChannel(1, SpringSpec.Snappy, 0.0004, 0.005);
        _scaleY = new SpringChannel(1, SpringSpec.Snappy, 0.0004, 0.005);
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

    /// <summary>是否仍有指针落在它（或它的宿主）上。</summary>
    public bool IsHovered => _hoverCount > 0;

    /// <summary>指针进入：抬角放大，并记下持续阶段的形态。</summary>
    /// <param name="shape">循环形态。</param>
    public void Enter(IconHoverShape shape)
    {
        if (MotionBudget.IsCollapsed)
        {
            return;
        }

        _hoverCount++;
        if (_hoverCount > 1)
        {
            return;
        }

        // 离开宽限期里又回来（侧栏弹层、悬浮卡片会把指针短暂抢走）：接着转，不要从头再数 0.55s。
        var resumed = _leaveGrace > 0;
        _leaveGrace = 0;

        _shape = shape == IconHoverShape.Auto ? IconHoverShape.Waggle : shape;
        if (!resumed)
        {
            _sustained = 0;
            _spinRate = 0;
            _spinDegrees = 0;
            _wagglePhase = 0;
        }

        // 上次是转着走的，值可能停在 360 的整数倍上：先归一到 (-180,180]，视觉等价，不会倒转一圈。
        _angle.SnapTo(Normalize(_angle.Value));
        _angle.Retarget(EnterTiltDegrees);
        _scaleX.Retarget(EnterScale);
        _scaleY.Retarget(EnterScale);
        Start();
    }

    /// <summary>指针离开：旋转形态顺转到下一个整圈再停，其余直接落回原位。</summary>
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

        // 精简档只是压幅度，不是关掉反馈：长悬停的循环照样要转，转得慢一点、摆得小一点。
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
                looping = true;

                if (_shape == IconHoverShape.Spin)
                {
                    var topSpeed = SpinDegreesPerSecond * amplitude;
                    _spinRate = Math.Min(topSpeed, _spinRate + (topSpeed / SpinRampSeconds * slice));
                    _spinDegrees += _spinRate * slice;
                    _angle.SnapTo(_spinDegrees);
                }
                else
                {
                    _wagglePhase += slice * 2 * Math.PI / WagglePeriodSeconds;
                    _angle.Retarget(EnterTiltDegrees + (Math.Sin(_wagglePhase) * WaggleAmplitudeDegrees * amplitude));
                }
            }
        }

        Apply();

        // 悬浮还在、动效还开着，就还有"过一会儿要进入循环"这件事要做：
        // 弹簧往往比 0.55s 的阈值先收敛，这里要是停帧，长悬停就永远进不了循环（齿轮于是只抬一下不转）。
        var waiting = (_hoverCount > 0 || _leaveGrace > 0) && MotionBudget.Mode != MotionMode.None;
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

    /// <summary>真正收尾：旋转形态顺转到下一个整圈再停，其余直接落回原位。</summary>
    private void GoHome()
    {
        _sustained = 0;
        _spinRate = 0;

        if (_shape == IconHoverShape.Spin && _spinDegrees > 0)
        {
            _angle.SnapTo(_spinDegrees);
            _angle.Retarget(Math.Ceiling(_spinDegrees / 360d) * 360d);
        }
        else
        {
            _angle.Retarget(0);
        }

        _scaleX.Retarget(1);
        _scaleY.Retarget(1);
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
        Apply();
        _topLevel = null;
        _hoverCount = 0;
        _leaveGrace = 0;
        _sustained = 0;
        _spinRate = 0;
        _spinDegrees = 0;
        _last = 0;
    }

    private void Apply()
    {
        _rotate.Angle = _angle.Value;
        _scale.ScaleX = _scaleX.Value;
        _scale.ScaleY = _scaleY.Value;
    }
}
