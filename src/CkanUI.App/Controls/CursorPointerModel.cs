using System;
using Avalonia;

namespace CkanUI.App.Controls;

/// <summary>
/// 光标指针效果的数值模型：管理指针尖端坐标、元素悬浮倾斜角度、点击缩小与松开放大的弹簧缩放。
/// 纯数学模型，不碰 Avalonia 控件也不碰渲染画笔，便于无头环境 100% 单元测试。
/// 对齐 Windows 桌面原生光标手感：
/// 1. 默认无冗余倾斜偏移；
/// 2. 悬停交互微倾斜（-4°）自然优雅，避免大幅歪斜产生的视觉拖拽感；
/// 3. 点击轻度缩小（0.88）紧致利落，松开微弹放大（1.10）并迅速复原（0.18s）。
/// </summary>
public sealed class CursorPointerModel
{
    /// <summary>静止默认缩放比例。</summary>
    public const double RestScale = 1.0d;

    /// <summary>按下时收缩的目标缩放比例（紧致利落，避免过度形变带来的视差错位）。</summary>
    public const double PressedScale = 0.88d;

    /// <summary>松开时弹性冲顶的最大放大比例（轻微弹性）。</summary>
    public const double ReleasedPeakScale = 1.10d;

    /// <summary>松开动效总时长（秒，快速利落归位）。</summary>
    public const double ReleaseDurationSeconds = 0.18d;

    /// <summary>松开冲到最大峰值的时间（秒）。</summary>
    public const double ReleasePeakTimeSeconds = 0.05d;

    /// <summary>划过可交互元素时的基础微倾斜角度（-4度，自然细腻）。</summary>
    public const double InteractiveTiltAngle = -4.0d;

    /// <summary>水平滑动惯性倾斜的最大额外增益（度数，适度克制）。</summary>
    public const double MaxDynamicTilt = 4.0d;

    /// <summary>允许的最大负向倾斜（度数）。</summary>
    public const double MinTotalTilt = -8.0d;

    /// <summary>允许的最大正向倾斜（度数）。</summary>
    public const double MaxTotalTilt = 6.0d;

    /// <summary>倾斜平滑过渡速率（提高响应速度，跟手利落）。</summary>
    public const double TiltSmoothingSpeed = 35.0d;

    /// <summary>按压缩放过渡速率（提高跟手速度）。</summary>
    public const double ScaleSmoothingSpeed = 40.0d;

    /// <summary>单步最大时间步长（防止调试或掉帧时瞬移）。</summary>
    public const double MaxStepSeconds = 0.1d;

    private double _x;
    private double _y;
    private double _tilt;
    private double _targetTilt;
    private double _scale = RestScale;
    private double _opacity;
    private bool _isVisible;
    private bool _isPressed;
    private bool _isOverInteractive;
    private bool _isReleasing;
    private double _releaseTime;
    private double _releaseStartScale = RestScale;
    private bool _isIdle = true;

    /// <summary>当前指针尖端横坐标（DIP）。</summary>
    public double X => _x;

    /// <summary>当前指针尖端纵坐标（DIP）。</summary>
    public double Y => _y;

    /// <summary>当前光标倾斜角度（度数）。</summary>
    public double Tilt => _tilt;

    /// <summary>当前光标倾斜角度（度数，别名）。</summary>
    public double CurrentTiltDegrees => _tilt;

    /// <summary>目标倾斜角度（度数）。</summary>
    public double TargetTilt => _targetTilt;

    /// <summary>目标倾斜角度（度数，别名）。</summary>
    public double TargetTiltDegrees => _targetTilt;

    /// <summary>当前光标倾斜弧度。</summary>
    public double TiltRadians => _tilt * (Math.PI / 180.0d);

    /// <summary>当前缩放倍率。</summary>
    public double Scale => _scale;

    /// <summary>当前缩放倍率（别名）。</summary>
    public double CurrentScale => _scale;

    /// <summary>目标缩放倍率。</summary>
    public double TargetScale => _isReleasing
        ? (_releaseTime < ReleasePeakTimeSeconds ? ReleasedPeakScale : RestScale)
        : (_isPressed ? PressedScale : RestScale);

    /// <summary>当前光标不透明度 0..1。</summary>
    public double Opacity => _opacity;

    /// <summary>当前光标不透明度（别名）。</summary>
    public double CurrentOpacity => _opacity;

    /// <summary>目标不透明度。</summary>
    public double TargetOpacity => _isVisible ? 1.0d : 0.0d;

    /// <summary>当前是否处于按住状态。</summary>
    public bool IsPressed => _isPressed;

    /// <summary>当前是否悬停在可响应元素上。</summary>
    public bool IsOverInteractiveElement => _isOverInteractive;

    /// <summary>是否应该显示（在窗口范围内）。</summary>
    public bool IsVisible => _isVisible;

    /// <summary>当前是否处于松开回弹放大状态。</summary>
    public bool IsReleasing => _isReleasing;

    /// <summary>是否完全静止。为 true 时渲染层可以停表，空闲零开销。</summary>
    public bool IsIdle => _isIdle;

    /// <summary>
    /// 更新指针位置与悬停目标状态。
    /// </summary>
    public void Track(Point position, bool isOverInteractive) => Track(position.X, position.Y, isOverInteractive);

    /// <summary>
    /// 更新指针位置与悬停目标状态。
    /// </summary>
    /// <param name="x">指针横坐标。</param>
    /// <param name="y">指针纵坐标。</param>
    /// <param name="isOverInteractive">是否划过可交互元素（按钮、列表行、卡片等）。</param>
    public void Track(double x, double y, bool isOverInteractive)
    {
        if (!double.IsFinite(x) || !double.IsFinite(y))
        {
            return;
        }

        double dx = _isVisible ? x - _x : 0d;
        _x = x;
        _y = y;
        _isOverInteractive = isOverInteractive;
        _isVisible = true;

        double baseTilt = isOverInteractive ? InteractiveTiltAngle : 0.0d;
        double dynamicTilt = Math.Clamp(dx * 0.15d, -MaxDynamicTilt, MaxDynamicTilt);
        _targetTilt = Math.Clamp(baseTilt + dynamicTilt, MinTotalTilt, MaxTotalTilt);
        _isIdle = false;
    }

    /// <summary>按下指针（Point 重载）。</summary>
    public void Press(Point position) => Press(position.X, position.Y);

    /// <summary>按下指针（默认使用当前坐标）。</summary>
    public void Press() => Press(_x, _y);

    /// <summary>
    /// 按下指针：触发缩小动效。
    /// </summary>
    /// <param name="x">按下坐标 X。</param>
    /// <param name="y">按下坐标 Y。</param>
    public void Press(double x, double y)
    {
        if (double.IsFinite(x) && double.IsFinite(y))
        {
            _x = x;
            _y = y;
        }

        _isPressed = true;
        _isReleasing = false;
        _isVisible = true;
        _isIdle = false;
    }

    /// <summary>松开指针（Point 重载）。</summary>
    public void Release(Point position) => Release(position.X, position.Y);

    /// <summary>松开指针（默认使用当前坐标）。</summary>
    public void Release() => Release(_x, _y);

    /// <summary>
    /// 松开指针：触发放大回弹冲顶动效，随后平滑归位至默认缩放。
    /// </summary>
    /// <param name="x">松开坐标 X。</param>
    /// <param name="y">松开坐标 Y。</param>
    public void Release(double x, double y)
    {
        if (double.IsFinite(x) && double.IsFinite(y))
        {
            _x = x;
            _y = y;
        }

        _isPressed = false;
        _isReleasing = true;
        _releaseTime = 0d;
        _releaseStartScale = _scale;
        _isIdle = false;
    }

    /// <summary>
    /// 隐藏指针（移出窗口等）。
    /// </summary>
    public void Hide()
    {
        _isVisible = false;
        _isPressed = false;
        _isOverInteractive = false;
        _isIdle = false;
    }

    /// <summary>
    /// 立即清空状态（窗口失焦或重置）。
    /// </summary>
    public void Clear()
    {
        _isVisible = false;
        _isPressed = false;
        _isOverInteractive = false;
        _isReleasing = false;
        _scale = RestScale;
        _tilt = 0d;
        _targetTilt = 0d;
        _opacity = 0d;
        _isIdle = true;
    }

    /// <summary>推进物理与过渡模拟（别名）。</summary>
    public void Update(double deltaSeconds) => Advance(deltaSeconds);

    /// <summary>
    /// 推进物理与过渡模拟，支持跨帧超长时间步长自动子步推进。
    /// </summary>
    /// <param name="deltaSeconds">时间步长（秒）。</param>
    public void Advance(double deltaSeconds)
    {
        if (!double.IsFinite(deltaSeconds) || deltaSeconds <= 0d)
        {
            return;
        }

        while (deltaSeconds > 0d)
        {
            double step = Math.Min(deltaSeconds, MaxStepSeconds);
            AdvanceStep(step);
            deltaSeconds -= step;
            if (_isIdle && !_isReleasing && !_isPressed)
            {
                break;
            }
        }
    }

    private void AdvanceStep(double dt)
    {
        // 1. 缩放逻辑：松开回弹放大 vs 按下缩小 vs 静止归位
        if (_isReleasing)
        {
            _releaseTime += dt;
            if (_releaseTime < ReleasePeakTimeSeconds)
            {
                double p = _releaseTime / ReleasePeakTimeSeconds;
                double ease = 1.0d - (1.0d - p) * (1.0d - p);
                _scale = _releaseStartScale + (ReleasedPeakScale - _releaseStartScale) * ease;
            }
            else if (_releaseTime < ReleaseDurationSeconds)
            {
                double p = (_releaseTime - ReleasePeakTimeSeconds) / (ReleaseDurationSeconds - ReleasePeakTimeSeconds);
                double ease = 1.0d - Math.Pow(1.0d - p, 3);
                _scale = ReleasedPeakScale + (RestScale - ReleasedPeakScale) * ease;
            }
            else
            {
                _scale = RestScale;
                _isReleasing = false;
            }
        }
        else if (_isPressed)
        {
            double scaleFactor = 1.0d - Math.Exp(-ScaleSmoothingSpeed * dt);
            _scale += (PressedScale - _scale) * scaleFactor;
        }
        else
        {
            double scaleFactor = 1.0d - Math.Exp(-ScaleSmoothingSpeed * dt);
            _scale += (RestScale - _scale) * scaleFactor;
        }

        // 2. 倾斜平滑与动态惯性衰减
        double baseTilt = _isOverInteractive ? InteractiveTiltAngle : 0.0d;
        _targetTilt += (baseTilt - _targetTilt) * (1.0d - Math.Exp(-20.0d * dt));
        double tiltFactor = 1.0d - Math.Exp(-TiltSmoothingSpeed * dt);
        _tilt += (_targetTilt - _tilt) * tiltFactor;

        // 3. 不透明度平滑过渡
        if (_isVisible)
        {
            _opacity = Math.Min(1.0d, _opacity + dt * 25.0d);
        }
        else
        {
            _opacity = Math.Max(0.0d, _opacity - dt * 15.0d);
        }

        // 4. 判断是否进入完全静止空闲（Zero CPU）
        if (!_isReleasing && !_isPressed
            && Math.Abs(_scale - RestScale) < 0.003d
            && Math.Abs(_tilt - _targetTilt) < 0.03d
            && (_isVisible ? _opacity >= 0.999d : _opacity <= 0.001d))
        {
            _scale = RestScale;
            _tilt = _targetTilt;
            if (_isVisible)
            {
                _opacity = 1.0d;
            }
            _isIdle = true;
        }
        else
        {
            _isIdle = false;
        }
    }
}
