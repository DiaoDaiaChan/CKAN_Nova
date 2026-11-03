using System;
using Avalonia;
using Avalonia.Media;

namespace CkanUI.App.Controls.Backgrounds;

/// <summary>
/// 引力透镜：偏心爱因斯坦环、被引力场拉弯的稀疏网格，以及网格外绕环缓慢流动的星弧。
/// </summary>
/// <remarks>
/// <para>
/// 无状态：运动只由 <see cref="BackgroundSceneContext.Time"/> 推导，光标响应只由
/// <see cref="BackgroundSceneContext.Influence"/> 与 <see cref="BackgroundSceneContext.InfluenceStrength"/> 推导，
/// 同一份输入永远画出同一帧。动效关闭时时间项冻结为零，构图静止，但光标造成的额外弯曲与环上高亮照常可见。
/// </para>
/// <para>
/// 亮度按设计语言封顶：环线 0.10–0.24（再乘主题强度 <c>scene.Intensity</c>），网格与星弧 0.04–0.10；
/// 没有任何实色填充，全部是 0.8–2.0 DIP 的细描边。网格固定 18 × 12 条线、每条 12–13 段折线，
/// 总段数封顶 390 段；环与星弧各占一次 DrawGeometry，单帧绘制调用约 45 次，不随窗口尺寸膨胀。
/// </para>
/// </remarks>
public sealed class GravitationalLensScene : IBackgroundSceneRenderer
{
    /// <summary>整圈弧度。</summary>
    private const double TwoPi = Math.PI * 2.0;

    /// <summary>环心横向位置（相对宽度）：刻意偏右，把左侧留给被拉弯的网格。</summary>
    private const double LensCenterXRatio = 0.62;

    /// <summary>环心纵向位置（相对高度）：偏上，形成非对称角落构图。</summary>
    private const double LensCenterYRatio = 0.40;

    /// <summary>环半径（相对短边）：0.18–0.24 档里的中值。</summary>
    private const double LensRadiusRatio = 0.21;

    /// <summary>竖网格线条数。</summary>
    private const int GridColumns = 18;

    /// <summary>横网格线条数。</summary>
    private const int GridRows = 12;

    /// <summary>远离环的网格线折线段数。</summary>
    private const int GridSegmentsFar = 12;

    /// <summary>靠近环的网格线折线段数：多一段就少一点棱角。</summary>
    private const int GridSegmentsNear = 13;

    /// <summary>整圈环的折线段数：4K 下也看不出棱角。</summary>
    private const int RingSegments = 64;

    /// <summary>每条透镜化星弧的折线段数。</summary>
    private const int StarArcSegments = 18;

    /// <summary>星弧数量：稀疏分布在网格之外。</summary>
    private const int StarArcCount = 8;

    /// <summary>环的呼吸周期（秒）：20–30 秒档，慢到不抢正文。</summary>
    private const double BreathSeconds = 26.0;

    /// <summary>环的椭圆化周期（秒）：与呼吸不同频，避免整幅画同拍。</summary>
    private const double OvalSeconds = 31.0;

    /// <summary>主透镜的位移强度（相对环半径）。</summary>
    private const double WarpStrengthRatio = 0.30;

    /// <summary>主透镜的最大位移（相对环半径）：夹住，别把网格拽出折痕。</summary>
    private const double WarpMaxRatio = 0.16;

    /// <summary>光标的位移强度（相对环半径，再乘 InfluenceStrength）。</summary>
    private const double PointerWarpRatio = 0.26;

    /// <summary>光标位移的最大幅度（相对环半径）。</summary>
    private const double PointerWarpMaxRatio = 0.12;

    /// <summary>光标引力井半径（相对环半径）：局部响应，不整屏拉扯。</summary>
    private const double PointerWellRatio = 0.62;

    /// <summary>环上高亮弧的感应半径（相对环半径）。</summary>
    private const double HighlightReachRatio = 1.60;

    /// <inheritdoc />
    public void Render(DrawingContext context, BackgroundSceneContext scene)
    {
        ArgumentNullException.ThrowIfNull(context);

        var width = scene.Width;
        var height = scene.Height;
        if (!double.IsFinite(width) || !double.IsFinite(height) || width < 24 || height < 24 || scene.Intensity <= 0.001)
        {
            return;
        }

        var minSide = Math.Min(width, height);
        var ringRadius = minSide * LensRadiusRatio;
        if (!double.IsFinite(ringRadius) || ringRadius < 10)
        {
            return;
        }

        // 线宽跟着窗口长：900×600 到 4K 都保持同一视觉密度，高分屏上不会细成一根头发。
        var weight = double.IsFinite(scene.Scale) ? Math.Clamp(scene.Scale, 0.55, 1.45) : 1.0;

        // 动效关闭时把时间项冻结成零：构图停住，但光标响应仍由 Influence/InfluenceStrength 推导，照样看得见。
        var time = scene.MotionEnabled && double.IsFinite(scene.Time) ? scene.Time : 0d;

        var strength = double.IsFinite(scene.InfluenceStrength) ? Math.Clamp(scene.InfluenceStrength, 0, 1) : 0;
        var influence = scene.Influence;
        if (!double.IsFinite(influence.X) || !double.IsFinite(influence.Y))
        {
            influence = new Point(width * 0.5, height * 0.5);
            strength = 0;
        }

        var center = new Point(width * LensCenterXRatio, height * LensCenterYRatio);

        // 环：26 秒一呼一吸 ±1.3%，31 秒一椭圆化 ±2.2%，两拍不同频，半分钟内看不出循环。
        var breath = 1.0 + (Math.Sin(time * (TwoPi / BreathSeconds)) * 0.013);
        var oval = Math.Sin((time * (TwoPi / OvalSeconds)) + 0.7) * 0.022;
        var ringX = ringRadius * breath * (1.0 + oval);
        var ringY = ringRadius * breath * (1.0 - (oval * 0.8));

        // 弯曲中心只漂 0.5–1.5 DIP：网格像被引力拖住，而不是泡在水里晃。
        var warpCenter = new Point(
            center.X + (Math.Sin(time * 0.21) * 0.9),
            center.Y + (Math.Cos(time * 0.15) * 0.65));

        using var clip = context.PushClip(new Rect(0, 0, width, height));

        Pen TextPen(double alpha, double thickness) => new(new SolidColorBrush(scene.TextAt(alpha)), thickness);

        Pen AccentPen(double alpha, double thickness) => new(new SolidColorBrush(scene.AccentAt(alpha)), thickness);

        // 引力位移：方向沿 p−center，幅度 strength / (1 + (dist/radius)²)，再夹到 maxShift。
        Point Warp(Point point, Point well, double warpStrength, double wellRadius, double maxShift)
        {
            if (warpStrength <= 0 || wellRadius <= 0 || maxShift <= 0)
            {
                return point;
            }

            var dx = point.X - well.X;
            var dy = point.Y - well.Y;
            var distance = Math.Sqrt((dx * dx) + (dy * dy));
            if (!(distance > 1e-3))
            {
                return point;
            }

            var ratio = distance / wellRadius;
            var shift = Math.Min(warpStrength / (1.0 + (ratio * ratio)), maxShift);
            var factor = shift / distance;
            return new Point(point.X + (dx * factor), point.Y + (dy * factor));
        }

        // 主透镜 + 光标：两个中心叠加；光标强度为零时严格退化成单透镜。
        Point Lensed(Point point) => Warp(
            Warp(point, warpCenter, ringRadius * WarpStrengthRatio, ringRadius, ringRadius * WarpMaxRatio),
            influence,
            ringRadius * PointerWarpRatio * strength,
            ringRadius * PointerWellRatio,
            ringRadius * PointerWarpMaxRatio);

        Point OnEllipse(Point arcCenter, double rx, double ry, double angle) =>
            new(arcCenter.X + (Math.Cos(angle) * rx), arcCenter.Y + (Math.Sin(angle) * ry));

        void DrawArc(Point arcCenter, double rx, double ry, double start, double sweep, int segments, Pen pen)
        {
            var geometry = new StreamGeometry();
            using (var ctx = geometry.Open())
            {
                ctx.BeginFigure(OnEllipse(arcCenter, rx, ry, start), false);
                for (var i = 1; i <= segments; i++)
                {
                    ctx.LineTo(OnEllipse(arcCenter, rx, ry, start + (sweep * i / segments)));
                }

                ctx.EndFigure(false);
            }

            context.DrawGeometry(null, pen, geometry);
        }

        void DrawWarpedLine(Point start, Point end, int segments, Pen pen)
        {
            var geometry = new StreamGeometry();
            using (var ctx = geometry.Open())
            {
                ctx.BeginFigure(Lensed(start), false);
                for (var i = 1; i <= segments; i++)
                {
                    var t = (double)i / segments;
                    ctx.LineTo(Lensed(new Point(start.X + ((end.X - start.X) * t), start.Y + ((end.Y - start.Y) * t))));
                }

                ctx.EndFigure(false);
            }

            context.DrawGeometry(null, pen, geometry);
        }

        // ── 1. 被拉弯的稀薄网格：18 竖 × 12 横，靠近环的线改成 13 段 ───────
        var gridNear = TextPen(0.088, 0.9 * weight);
        var gridMid = TextPen(0.062, 0.85 * weight);
        var gridFar = TextPen(0.043, 0.8 * weight);

        var marginX = width * 0.04;
        var marginY = height * 0.05;
        var stepX = (width + (marginX * 2)) / (GridColumns - 1);
        var stepY = (height + (marginY * 2)) / (GridRows - 1);

        for (var i = 0; i < GridColumns; i++)
        {
            var x = -marginX + (stepX * i);
            var distance = Math.Abs(x - warpCenter.X);
            var near = distance <= ringRadius * 0.62;
            var mid = distance <= ringRadius * 1.65;
            DrawWarpedLine(
                new Point(x, -marginY),
                new Point(x, height + marginY),
                near ? GridSegmentsNear : GridSegmentsFar,
                near ? gridNear : mid ? gridMid : gridFar);
        }

        for (var j = 0; j < GridRows; j++)
        {
            var y = -marginY + (stepY * j);
            var distance = Math.Abs(y - warpCenter.Y);
            var near = distance <= ringRadius * 0.62;
            var mid = distance <= ringRadius * 1.65;
            DrawWarpedLine(
                new Point(-marginX, y),
                new Point(width + marginX, y),
                near ? GridSegmentsNear : GridSegmentsFar,
                near ? gridNear : mid ? gridMid : gridFar);
        }

        // ── 2. 网格外的透镜化星弧：半径 1.26–1.66 R，沿环向缓慢流动 ─────────
        for (var i = 0; i < StarArcCount; i++)
        {
            var seed = i + 1;
            var factor = 1.26 + (0.40 * Hash(seed * 7));
            var span = 0.24 + (0.36 * Hash((seed * 11) + 3));
            var phase = TwoPi * Hash((seed * 13) + 5);
            var flow = 0.012 + (0.010 * Hash((seed * 17) + 7));
            var direction = Hash((seed * 23) + 9) < 0.5 ? -1.0 : 1.0;
            var alpha = (i % 3) switch
            {
                0 => 0.050,
                1 => 0.066,
                _ => 0.082
            };

            DrawArc(
                center,
                ringX * factor,
                ringY * factor,
                phase + (time * flow * direction),
                span,
                StarArcSegments,
                TextPen(alpha, 0.9 * weight));
        }

        // ── 3. 爱因斯坦环：三层不等亮度的细弧，左上亮、右下暗，留几处小缺口 ──
        DrawArc(center, ringX, ringY, 3.24, 1.86, RingSegments, AccentPen(0.215, 1.35 * weight));
        DrawArc(center, ringX, ringY, -0.04, 3.10, RingSegments, AccentPen(0.118, 1.05 * weight));
        DrawArc(center, ringX, ringY, 5.36, 0.84, RingSegments / 3, AccentPen(0.088, 0.9 * weight));
        DrawArc(center, ringX * 0.925, ringY * 0.925, 3.02, 4.72, RingSegments, AccentPen(0.106, 0.9 * weight));
        DrawArc(center, ringX * 1.072, ringY * 1.072, 3.92, 4.36, RingSegments, AccentPen(0.072, 0.8 * weight));

        // ── 4. 光标：局部额外弯曲已在 Lensed 里生效，这里补环上会滑动的高亮弧 ──
        var pointerDistance = Math.Sqrt(Square(influence.X - center.X) + Square(influence.Y - center.Y));
        var nearness = 1.0 / (1.0 + Square(pointerDistance / (ringRadius * HighlightReachRatio)));
        var pointerWeight = strength * nearness;
        if (pointerWeight > 0.004)
        {
            // 角度取自光标相对环心的方位：光标一动高亮弧就沿环滑动，离开时随 InfluenceStrength 衰减。
            var slide = Math.Atan2(influence.Y - center.Y, influence.X - center.X) + (time * 0.045);
            var span = 0.62 + (0.42 * pointerWeight);
            var start = slide - (span * 0.5);
            var highlight = 0.075 + (0.165 * pointerWeight);
            DrawArc(center, ringX * 1.004, ringY * 1.004, start, span, 26, AccentPen(highlight * 0.42, 3.2 * weight));
            DrawArc(center, ringX * 1.004, ringY * 1.004, start, span, 26, AccentPen(highlight, 1.55 * weight));
        }
    }

    /// <summary>把种子散列成稳定的 0–1 伪随机数：只依赖常量，不需要 Random，同一输入同一帧。</summary>
    private static double Hash(int seed)
    {
        var value = Math.Sin((seed * 12.9898) + 4.1414) * 43758.5453;
        return value - Math.Floor(value);
    }

    /// <summary>平方，省掉重复的乘法括号。</summary>
    private static double Square(double value) => value * value;
}
