using System;
using System.Runtime.CompilerServices;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace CkanUI.App.Animations;

/// <summary>
/// 把一个控件的 RenderTransform 变成四通道弹簧（位移 X/Y、缩放 X/Y，可选不透明度）。
/// </summary>
/// <remarks>
/// <para>RenderTransform 本身只赋值一次，之后逐帧只改 TranslateTransform 与 ScaleTransform 的子属性：
/// 子属性变更不触发样式里的 TransformOperationsTransition，否则每帧都会被当成新目标重启。</para>
/// </remarks>
public sealed class TransformSpring : IMotionTicker
{
    private static readonly ConditionalWeakTable<Control, TransformSpring> Shared = new();

    private readonly Control _target;
    private readonly TransformGroup _group = new();
    private readonly TranslateTransform _translate = new();
    private readonly ScaleTransform _scale = new() { ScaleX = 1, ScaleY = 1 };
    private readonly SkewTransform _skew = new();
    private readonly RotateTransform _rotate = new();
    private readonly SpringChannel _x;
    private readonly SpringChannel _y;
    private readonly SpringChannel _scaleX;
    private readonly SpringChannel _scaleY;

    private SpringChannel? _opacity;
    private SpringChannel? _skewX;
    private SpringChannel? _skewY;
    private SpringChannel? _rotateAngle;
    private TopLevel? _topLevel;
    private bool _waitingForTree;

    public TransformSpring(Control target, SpringSpec translateSpec, SpringSpec scaleSpec)
    {
        ArgumentNullException.ThrowIfNull(target);

        _target = target;
        _group.Children.Add(_scale);
        _group.Children.Add(_translate);
        _group.Children.Add(_skew);
        _group.Children.Add(_rotate);
        _target.RenderTransform = _group;

        if (_target.RenderTransformOrigin.Equals(RelativePoint.TopLeft))
        {
            _target.RenderTransformOrigin = new RelativePoint(0.5, 0.5, RelativeUnit.Relative);
        }

        _x = new SpringChannel(0, translateSpec);
        _y = new SpringChannel(0, translateSpec);
        _scaleX = new SpringChannel(1, scaleSpec, 0.0004, 0.005);
        _scaleY = new SpringChannel(1, scaleSpec, 0.0004, 0.005);
    }

    /// <summary>取某个控件共享的弹簧；没有就建一根。</summary>
    public static TransformSpring For(Control target, SpringSpec spec)
    {
        ArgumentNullException.ThrowIfNull(target);
        return Shared.GetValue(target, control => new TransformSpring(control, spec, spec));
    }

    /// <summary>把指定通道送向新目标；没传的通道保持原目标不动。</summary>
    public void MoveTo(
        double? x = null,
        double? y = null,
        double? scaleX = null,
        double? scaleY = null,
        double? opacity = null,
        double? skewX = null,
        double? skewY = null,
        double? rotateAngle = null,
        double hold = 0,
        bool snap = false)
    {
        ReclaimRenderTransform();
        var instant = snap || MotionBudget.IsCollapsed;

        if (x.HasValue)
        {
            Retarget(_x, x.Value, instant, hold);
        }

        if (y.HasValue)
        {
            Retarget(_y, y.Value, instant, hold);
        }

        if (scaleX.HasValue)
        {
            Retarget(_scaleX, scaleX.Value, instant, hold);
        }

        if (scaleY.HasValue)
        {
            Retarget(_scaleY, scaleY.Value, instant, hold);
        }

        if (opacity.HasValue)
        {
            _opacity ??= new SpringChannel(1, SpringSpec.Glide, 0.004, 0.02);
            Retarget(_opacity, Math.Clamp(opacity.Value, 0, 1), instant, hold);
        }

        if (skewX.HasValue)
        {
            _skewX ??= new SpringChannel(0, SpringSpec.Snappy, 0.005, 0.02);
            Retarget(_skewX, skewX.Value, instant, hold);
        }

        if (skewY.HasValue)
        {
            _skewY ??= new SpringChannel(0, SpringSpec.Snappy, 0.005, 0.02);
            Retarget(_skewY, skewY.Value, instant, hold);
        }

        if (rotateAngle.HasValue)
        {
            _rotateAngle ??= new SpringChannel(0, SpringSpec.Snappy, 0.005, 0.02);
            Retarget(_rotateAngle, rotateAngle.Value, instant, hold);
        }

        Apply();

        if (instant)
        {
            SnapAll();
            return;
        }

        if (IsSettled)
        {
            // 目标正好落在当前姿态上：这一枪不会跑，别把刚收回来的变换又挂成恒等层。
            ReleaseIdentityTransform();
            return;
        }

        Start();
    }

    /// <summary>在当前状态上叠加一次冲量（校验抖动、导航追赶）。</summary>
    public void Kick(double horizontal, double vertical = 0)
    {
        if (MotionBudget.IsCollapsed)
        {
            return;
        }

        ReclaimRenderTransform();
        _x.Retarget(0);
        _x.Kick(horizontal);
        _y.Retarget(0);
        _y.Kick(vertical);
        Start();
    }

    /// <summary>在当前缩放上叠加一次冲量（开关选中、徽标跳动）。</summary>
    public void KickScale(double amount)
    {
        if (MotionBudget.IsCollapsed)
        {
            return;
        }

        ReclaimRenderTransform();
        _scaleX.Retarget(1d);
        _scaleX.Kick(amount);
        _scaleY.Retarget(1d);
        _scaleY.Kick(amount);
        Start();
    }

    bool IMotionTicker.Advance(double seconds) => Advance(seconds);

    /// <summary>四通道都停住时触发（每次从"在动"变成"静止"发一次）。</summary>
    public event EventHandler? Settled;

    /// <summary>各通道是否都停住了；正在等 hold 排队的也算没停。</summary>
    public bool IsSettled =>
        !_x.IsMoving && !_y.IsMoving && !_scaleX.IsMoving && !_scaleY.IsMoving &&
        _opacity?.IsMoving != true &&
        _skewX?.IsMoving != true && _skewY?.IsMoving != true && _rotateAngle?.IsMoving != true;

    /// <summary>手动推进一帧（帧泵与测试共用；正常情况下由 <see cref="MotionFramePump"/> 驱动）。</summary>
    /// <param name="seconds">帧间隔（秒）。</param>
    /// <returns>是否还在运动。</returns>
    public bool AdvanceFrame(double seconds) => Advance(seconds);

    internal bool Advance(double seconds)
    {
        var top = _topLevel;
        if (top is null || !ReferenceEquals(TopLevel.GetTopLevel(_target), top))
        {
            SnapAll();
            return false;
        }

        // 飞行中样式可能把 RenderTransform 换掉（:pointerover 的 translateY / none），
        // 那样弹簧写的是脱离可视树的旧组，看起来"卡住不动"。只在动画期间收回，停住后不反复抢样式。
        if (!ReferenceEquals(_target.RenderTransform, _group))
        {
            _target.RenderTransform = _group;
        }

        var moving = _x.Advance(seconds);
        moving |= _y.Advance(seconds);
        moving |= _scaleX.Advance(seconds);
        moving |= _scaleY.Advance(seconds);

        if (_opacity is not null)
        {
            moving |= _opacity.Advance(seconds);
        }

        if (_skewX is not null)
        {
            moving |= _skewX.Advance(seconds);
        }

        if (_skewY is not null)
        {
            moving |= _skewY.Advance(seconds);
        }

        if (_rotateAngle is not null)
        {
            moving |= _rotateAngle.Advance(seconds);
        }

        Apply();

        if (!moving)
        {
            // 停在恒等姿态还挂着 RenderTransform，控件会被合成器单独开一层；
            // 快速拖动滚动条时这些层来不及失效，屏幕上就留下一片空白。
            ReleaseIdentityTransform();
            _topLevel = null;
            Settled?.Invoke(this, EventArgs.Empty);
        }

        return moving;
    }

    private static void Retarget(SpringChannel channel, double target, bool instant, double hold)
    {
        if (instant)
        {
            channel.SnapTo(target);
        }
        else
        {
            channel.Retarget(target, hold);
        }
    }

    private void Start()
    {
        var top = TopLevel.GetTopLevel(_target);
        if (top is null)
        {
            // 换页那一刻新内容可能还没挂进可视树：直接 SnapAll 会把入场动画压成一帧瞬移，
            // 所以保留起点姿态，等挂树事件到了再起跑。
            if (!_waitingForTree)
            {
                _waitingForTree = true;
                _target.AttachedToVisualTree += OnAttachedToVisualTree;
            }

            return;
        }

        _topLevel = top;
        MotionFramePump.Subscribe(top, this);
    }

    private void OnAttachedToVisualTree(object? sender, VisualTreeAttachmentEventArgs e)
    {
        _target.AttachedToVisualTree -= OnAttachedToVisualTree;
        _waitingForTree = false;
        Start();
    }

    private void SnapAll()
    {
        if (_waitingForTree)
        {
            _target.AttachedToVisualTree -= OnAttachedToVisualTree;
            _waitingForTree = false;
        }

        _x.SnapTo(_x.Target);
        _y.SnapTo(_y.Target);
        _scaleX.SnapTo(_scaleX.Target);
        _scaleY.SnapTo(_scaleY.Target);
        _opacity?.SnapTo(_opacity.Target);
        _skewX?.SnapTo(_skewX.Target);
        _skewY?.SnapTo(_skewY.Target);
        _rotateAngle?.SnapTo(_rotateAngle.Target);
        Apply();
        _topLevel = null;
    }

    /// <summary>弹簧停在恒等姿态时把 RenderTransform 摘掉，别让控件白背一层合成层。</summary>
    private void ReleaseIdentityTransform()
    {
        if (!ReferenceEquals(_target.RenderTransform, _group))
        {
            return;
        }

        if (_x.Value != 0d || _y.Value != 0d
            || _scaleX.Value != 1d || _scaleY.Value != 1d
            || (_skewX?.Value ?? 0d) != 0d || (_skewY?.Value ?? 0d) != 0d
            || (_rotateAngle?.Value ?? 0d) != 0d)
        {
            return;
        }

        _target.RenderTransform = null;
    }

    private void Apply()
    {
        _translate.X = _x.Value;
        _translate.Y = _y.Value;
        _scale.ScaleX = _scaleX.Value;
        _scale.ScaleY = _scaleY.Value;

        if (_skewX is not null || _skewY is not null)
        {
            _skew.AngleX = _skewX?.Value ?? 0d;
            _skew.AngleY = _skewY?.Value ?? 0d;
        }

        if (_rotateAngle is not null)
        {
            _rotate.Angle = _rotateAngle.Value;
        }

        if (_opacity is not null)
        {
            _target.Opacity = Math.Clamp(_opacity.Value, 0, 1);
        }
    }

    /// <summary>样式抢走 RenderTransform 后，下一次显式触发必须把它收回来。</summary>
    private void ReclaimRenderTransform()
    {
        if (!ReferenceEquals(_target.RenderTransform, _group))
        {
            _target.RenderTransform = _group;
        }
    }
}