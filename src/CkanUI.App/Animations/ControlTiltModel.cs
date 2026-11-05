using System;

namespace CkanUI.App.Animations;

/// <summary>
/// 控件基于固定平面受力假设的力学交互模型（纯数学，无 UI 依赖）。
/// 假设整个平面是固定住的（直角正交、绝对零剪切、零旋转，杜绝文字变形与阅读干扰），
/// 鼠标移动到控件上时施加接触力，控件随鼠标受力位置与程度产生平滑的亚像素微位移与弹性下凹。
/// </summary>
public static class ControlTiltModel
{
    /// <summary>固定平面最大顺应微位移（像素）：亚像素级，文字绝对清晰正交。</summary>
    public const double MaxDeflectionPixels = 0.45d;

    /// <summary>受力最大弹性下凹微缩放差值（1.0 - 0.003 = 0.997）。</summary>
    public const double MaxDepressionScaleDelta = 0.003d;

    /// <summary>历史倾斜度数常量（固定平面假设下归零以保护文字阅读）。</summary>
    public const double DefaultMaxTiltDegrees = 0d;
    public const double MaxSkewAngleDegrees = 0d;
    public const double MaxRotateAngleDegrees = 0d;

    public static TiltResult Calculate(Avalonia.Point cursor, Avalonia.Rect bounds, double maxDeflection = MaxDeflectionPixels)
        => Calculate(bounds.Width, bounds.Height, cursor.X - bounds.X, cursor.Y - bounds.Y, maxDeflection);

    /// <summary>
    /// 计算光标对固定平面的施加力、受力程度、受力角度及亚像素弹性响应。
    /// </summary>
    /// <param name="width">控件宽度（DIP）。</param>
    /// <param name="height">控件高度（DIP）。</param>
    /// <param name="localX">光标相对控件左上角的 X（DIP）。</param>
    /// <param name="localY">光标相对控件左上角的 Y（DIP）。</param>
    /// <param name="maxDeflection">最大亚像素位移（DIP）。</param>
    /// <returns>受力计算结果结构体。</returns>
    public static TiltResult Calculate(
        double width,
        double height,
        double localX,
        double localY,
        double maxDeflection = MaxDeflectionPixels)
    {
        if (width <= 0d || height <= 0d || !double.IsFinite(localX) || !double.IsFinite(localY))
        {
            return new TiltResult(0d, 0d, 0d, 0d, 0d, 0d, 0d, 0d, 0d, 1.0d);
        }

        var cx = width * 0.5d;
        var cy = height * 0.5d;

        // 归一化受力点坐标 [-1, 1]
        var nx = Math.Clamp((localX - cx) / cx, -1.0d, 1.0d);
        var ny = Math.Clamp((localY - cy) / cy, -1.0d, 1.0d);

        var extent = Math.Clamp(Math.Sqrt(nx * nx + ny * ny), 0.0d, 1.0d);
        var angle = Math.Atan2(ny, nx);

        // 平面是固定住的物理受力模型：
        // 鼠标移入控件即施加接触力，控件随受力点位置产生顺应下压：
        // 接触力基础强度（0.6） + 边缘平滑缓动，杜绝突兀跳变
        var contactForce = 0.55d + 0.45d * Math.Clamp(1.0d - extent * 0.4d, 0d, 1.0d);
        var forceX = nx * contactForce;
        var forceY = ny * contactForce;

        // 亚像素受力顺应位移（微量，文字绝对清晰，杜绝平行四边形切变畸变）：
        var defX = forceX * (maxDeflection * 0.65d);
        var defY = Math.Abs(forceY) * (maxDeflection * 0.85d); // 受力向下按压为主
        var depression = 1.0d - (contactForce * MaxDepressionScaleDelta);

        // 固定平面：Skew 与 Rotate 严格为 0，彻底根除文字扭曲
        return new TiltResult(
            extent,
            angle,
            0d,
            0d,
            0d,
            forceX,
            forceY,
            defX,
            defY,
            depression);
    }
}

/// <summary>控件受力与微倾斜计算结果。</summary>
public readonly record struct TiltResult(
    double Extent,
    double Angle,
    double SkewX,
    double SkewY,
    double Rotate,
    double ForceX,
    double ForceY,
    double DeflectionX,
    double DeflectionY,
    double DepressionScale)
{
    public TiltResult(double extent, double angle, double skewX, double skewY, double rotate)
        : this(extent, angle, skewX, skewY, rotate, 0d, 0d, 0d, 0d, 1.0d)
    {
    }

    public double RotateAngle => Rotate;
}

public static class ControlTilt
{
    public static readonly TiltResult Zero = new(0d, 0d, 0d, 0d, 0d, 0d, 0d, 0d, 0d, 1.0d);
}
