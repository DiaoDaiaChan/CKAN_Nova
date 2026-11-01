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
public sealed class TransformSpring
{
    private static readonly ConditionalWeakTable<Control, TransformSpring> Shared = new();

    private readonly Control _target;
    private readonly TransformGroup _group = new();
    private readonly TranslateTransform _translate = new();
    private readonly ScaleTransform _scale = new() { ScaleX = 1, ScaleY = 1 };
    private readonly SpringChannel _x;
    private readonly SpringChannel _y;
    private readonly SpringChannel _scaleX;
    private readonly SpringChannel _scaleY;

    private SpringChannel? _opacity;
    private TopLevel? _topLevel;

    public TransformSpring(Control target, SpringSpec translateSpec, SpringSpec scaleSpec)
    {
        ArgumentNullException.ThrowIfNull(target);

        _target = target;
        _group.Children.Add(_scale);
        _group.Children.Add(_translate);
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

        Apply();

        if (instant)
        {
            SnapAll();
        }
        else
        {
            Start();
        }
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

    internal bool Advance(double seconds)
    {
        var top = _topLevel;
        if (top is null || !ReferenceEquals(TopLevel.GetTopLevel(_target), top))
        {
            SnapAll();
            return false;
        }

        var moving = _x.Advance(seconds);
        moving |= _y.Advance(seconds);
        moving |= _scaleX.Advance(seconds);
        moving |= _scaleY.Advance(seconds);

        if (_opacity is not null)
        {
            moving |= _opacity.Advance(seconds);
        }

        Apply();

        if (!moving)
        {
            _topLevel = null;
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
            SnapAll();
            return;
        }

        _topLevel = top;
        MotionFramePump.Subscribe(top, this);
    }

    private void SnapAll()
    {
        _x.SnapTo(_x.Target);
        _y.SnapTo(_y.Target);
        _scaleX.SnapTo(_scaleX.Target);
        _scaleY.SnapTo(_scaleY.Target);
        _opacity?.SnapTo(_opacity.Target);
        Apply();
        _topLevel = null;
    }

    private void Apply()
    {
        _translate.X = _x.Value;
        _translate.Y = _y.Value;
        _scale.ScaleX = _scaleX.Value;
        _scale.ScaleY = _scaleY.Value;

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
