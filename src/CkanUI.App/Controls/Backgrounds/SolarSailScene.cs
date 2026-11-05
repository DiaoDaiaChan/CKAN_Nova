using System;
using Avalonia;
using Avalonia.Media;

namespace CkanUI.App.Controls.Backgrounds;

/// <summary>
/// 太阳帆：左上角一面斜掠出画布的巨大半透明帆面。X 形帆桁、八条径向缝、八个系索环，
/// 一根主系绳从左下角垂出画面；没有航天器本体。
/// </summary>
/// <remarks>
/// <para>
/// 构图刻意压在左上：帆面中心落在画面约 25% / 21% 处，两个角直接越出上边缘，
/// 页面卡片所在的中部与右下角保持留白；右上角完全让给已有的深空探测器场景，
/// 两套背景不会抢同一个焦点。
/// </para>
/// <para>
/// 无状态：帆面呼吸（17s）、系绳扭转（43s）只由 <c>scene.Time</c> 推导；指针响应只读
/// <c>scene.Influence</c> / <c>scene.InfluenceStrength</c>。同一份输入永远画出同一帧。
/// </para>
/// <para>
/// 动效关闭时时间项冻结为零，画面停在基准构图；但指针落在帆面投影内时，最近控制点的
/// 4 DIP 起伏照常生效——关掉动效不该连带关掉可感知的交互。
/// </para>
/// <para>
/// 全描边、无填充；主体线透明度 ≤ 0.20，辅助线 0.04–0.10，线宽随缩放夹在 0.6–2.4 DIP。
/// </para>
/// </remarks>
public sealed class SolarSailScene : IBackgroundSceneRenderer
{
    /// <summary>2π：周期换算常量。</summary>
    private const double Tau = Math.PI * 2.0;

    /// <summary>径向缝数量：4 个角 + 4 个边中点，正好把帆面分成八瓣。</summary>
    private const int SeamCount = 8;

    /// <summary>系绳折线段数：40 段足够让扭转曲线看不出折角。</summary>
    private const int TetherSteps = 40;

    /// <summary>帆面呼吸周期（秒）。</summary>
    private const double BreathPeriod = 17.0;

    /// <summary>主系绳扭转周期（秒）。</summary>
    private const double TetherTwistPeriod = 43.0;

    /// <summary>呼吸时控制点沿径向的最大位移（DIP）：规格上限 2 DIP。</summary>
    private const double BreathDepth = 1.8;

    /// <summary>指针涟漪在最近控制点的最大位移（DIP）。</summary>
    private const double RippleLimit = 4.0;

    /// <summary>相邻控制点分到的涟漪比例：单点起伏读成一圈软波，而不是折角。</summary>
    private const double RippleNeighborShare = 0.34;

    /// <summary>帆面半宽（相对 MinSide）：横向长轴。</summary>
    private const double SailHalfU = 0.36;

    /// <summary>帆面半高（相对 MinSide）：透视压扁后的短轴。</summary>
    private const double SailHalfV = 0.28;

    /// <summary>横向帆桁的屏幕倾角（弧度）：约 −17°，指向右上。</summary>
    private const double BoomAngleU = -0.30;

    /// <summary>纵向帆桁的屏幕倾角（弧度）：约 68°，指向右下。</summary>
    private const double BoomAngleV = 1.18;

    /// <summary>系绳扭转的横向摆幅（DIP）。</summary>
    private const double TetherTwistDepth = 1.7;

    /// <summary>系绳挂点在画布上的横向比例：压在左侧导航区，避开页面卡片。</summary>
    private const double TetherAnchorX = 0.10;

    /// <summary>系绳挂点在画布上的纵向比例。</summary>
    private const double TetherAnchorY = 0.58;

    /// <summary>八个控制点沿帆面边界的归一化坐标，按周长顺序排列：角、边中点交替。</summary>
    private static readonly double[] SeamU = [1.0, 0.0, -1.0, -1.0, -1.0, 0.0, 1.0, 1.0];

    /// <summary>与 <see cref="SeamU"/> 对应的纵向归一化坐标，顺序一致。</summary>
    private static readonly double[] SeamV = [1.0, 1.0, 1.0, 0.0, -1.0, -1.0, -1.0, 0.0];

    /// <inheritdoc />
    public void Render(DrawingContext context, BackgroundSceneContext scene)
    {
        ArgumentNullException.ThrowIfNull(context);

        var width = scene.Width;
        var height = scene.Height;
        if (!double.IsFinite(width) || !double.IsFinite(height) || width < 2.0 || height < 2.0)
        {
            return;
        }

        if (!double.IsFinite(scene.Intensity) || scene.Intensity <= 0.002)
        {
            return;
        }

        var minSide = Math.Min(width, height);
        if (!(minSide > 1.0))
        {
            return;
        }

        var scale = double.IsFinite(scene.Scale) ? Math.Clamp(scene.Scale, 0.4, 2.0) : 1.0;
        var time = scene.MotionEnabled && double.IsFinite(scene.Time) ? scene.Time : 0.0;
        var strength = double.IsFinite(scene.InfluenceStrength)
            ? Math.Clamp(scene.InfluenceStrength, 0.0, 1.0)
            : 0.0;

        double Weight(double value) => Math.Clamp(value * scale, 0.6, 2.4);

        // 帆面基向量：U 是长轴（右上），V 是透视压扁后的短轴（右下）。
        // Q 是始终留在画布内的左下角，由它反推中心，保证系绳挂点不会被窗口比例变化挤出画面。
        var halfU = minSide * SailHalfU;
        var halfV = minSide * SailHalfV;
        var ux = Math.Cos(BoomAngleU) * halfU;
        var uy = Math.Sin(BoomAngleU) * halfU;
        var vx = Math.Cos(BoomAngleV) * halfV;
        var vy = Math.Sin(BoomAngleV) * halfV;

        var anchor = new Point(width * TetherAnchorX, height * TetherAnchorY);
        var center = new Point(anchor.X + ux - vx, anchor.Y + uy - vy);

        var breathPhase = Tau * time / BreathPeriod;
        var alphaBreath = 0.97 + (0.05 * Math.Sin(breathPhase));

        Span<Point> control = stackalloc Point[SeamCount];
        for (var i = 0; i < SeamCount; i++)
        {
            var x = center.X + (ux * SeamU[i]) + (vx * SeamV[i]);
            var y = center.Y + (uy * SeamU[i]) + (vy * SeamV[i]);

            var dx = x - center.X;
            var dy = y - center.Y;
            var length = Math.Sqrt((dx * dx) + (dy * dy));
            var breath = BreathDepth * Math.Sin(breathPhase + (i * 0.85));
            if (length > 0.001 && double.IsFinite(breath))
            {
                x += dx / length * breath;
                y += dy / length * breath;
            }

            control[i] = new Point(x, y);
        }

        // 指针是否落在帆面投影内：把画布坐标反解回帆面局部坐标，|u|、|v| 同时 ≤ 1 才算命中。
        var pointerInside = false;
        var nearest = -1;
        var ripple = 0.0;
        var determinant = (ux * vy) - (uy * vx);
        if (strength > 0.001 && Math.Abs(determinant) > 1e-6)
        {
            var influence = scene.Influence;
            var px = influence.X - center.X;
            var py = influence.Y - center.Y;
            var localU = ((px * vy) - (py * vx)) / determinant;
            var localV = ((ux * py) - (uy * px)) / determinant;

            if (double.IsFinite(localU) && double.IsFinite(localV)
                && Math.Abs(localU) <= 1.0 && Math.Abs(localV) <= 1.0)
            {
                pointerInside = true;
                var bestDistanceSquared = double.MaxValue;
                for (var i = 0; i < SeamCount; i++)
                {
                    var dx = control[i].X - influence.X;
                    var dy = control[i].Y - influence.Y;
                    var distanceSquared = (dx * dx) + (dy * dy);
                    if (distanceSquared < bestDistanceSquared)
                    {
                        bestDistanceSquared = distanceSquared;
                        nearest = i;
                    }
                }

                var proximity = 1.0 - Math.Clamp(
                    Math.Sqrt(bestDistanceSquared) / (0.45 * minSide),
                    0.0,
                    1.0);
                ripple = RippleLimit * strength * (0.55 + (0.45 * proximity));
            }
        }

        if (pointerInside && nearest >= 0)
        {
            for (var i = 0; i < SeamCount; i++)
            {
                var step = Math.Abs(i - nearest);
                step = Math.Min(step, SeamCount - step);
                var share = step switch
                {
                    0 => 1.0,
                    1 => RippleNeighborShare,
                    _ => 0.0,
                };

                if (share <= 0.0)
                {
                    continue;
                }

                var dx = control[i].X - center.X;
                var dy = control[i].Y - center.Y;
                var length = Math.Sqrt((dx * dx) + (dy * dy));
                if (!(length > 0.001))
                {
                    continue;
                }

                var offset = ripple * share;
                control[i] = new Point(
                    control[i].X + (dx / length * offset),
                    control[i].Y + (dy / length * offset));
            }
        }

        using var clip = context.PushClip(new Rect(0, 0, width, height));

        // 系绳先画：它从帆的左下角垂出画面，压在帆面之后，不抢帆桁的层级。
        DrawTether(context, scene, control[2], width, height, minSide, scale, time);

        var edgePen = new Pen(new SolidColorBrush(scene.AccentAt(0.175 * alphaBreath)), Weight(1.25));
        var hemPen = new Pen(new SolidColorBrush(scene.TextAt(0.055 * alphaBreath)), Weight(1.0));
        var seamPen = new Pen(new SolidColorBrush(scene.TextAt(0.075 * alphaBreath)), Weight(1.0));
        var cornerSeamPen = new Pen(new SolidColorBrush(scene.TextAt(0.045 * alphaBreath)), Weight(1.0));
        var grommetPen = new Pen(new SolidColorBrush(scene.TextAt(0.085 * alphaBreath)), Weight(1.0));

        // 八边形轮廓：每条边一根主边 + 一根内收 3 DIP 的缝边，读成绷紧的帆布卷边。
        for (var i = 0; i < SeamCount; i++)
        {
            var next = (i + 1) % SeamCount;
            context.DrawLine(edgePen, control[i], control[next]);
            context.DrawLine(
                hemPen,
                Inset(control[i], center, 3.0 * scale),
                Inset(control[next], center, 3.0 * scale));
        }

        // 八条径向缝：边中点四瓣用主线，四角四瓣更淡，避免和 X 帆桁叠成一条亮线。
        for (var i = 0; i < SeamCount; i++)
        {
            context.DrawLine((i % 2 == 1) ? seamPen : cornerSeamPen, center, control[i]);
        }

        // X 形帆桁：两条对角线各画一根主梁 + 两根 1.9 DIP 偏移的细边，读成有壁厚的方管。
        DrawBoom(context, scene, control[0], control[4], scale, alphaBreath);
        DrawBoom(context, scene, control[2], control[6], scale, alphaBreath);

        // 八个系索环：每个控制点一个小圆；指针命中时最近的一个加一圈高亮。
        for (var i = 0; i < SeamCount; i++)
        {
            var point = control[i];
            if (point.X < -24 || point.X > width + 24 || point.Y < -24 || point.Y > height + 24)
            {
                continue;
            }

            var radius = 2.2 * scale;
            context.DrawEllipse(null, grommetPen, point, radius, radius);
            if (pointerInside && i == nearest)
            {
                var ringPen = new Pen(
                    new SolidColorBrush(scene.AccentAt(0.135 + (0.055 * strength))),
                    Weight(1.1));
                context.DrawEllipse(null, ringPen, point, 4.4 * scale, 4.4 * scale);
            }
        }
    }

    /// <summary>主系绳：一条三次贝塞尔贴着左缘垂下，两根股线按 43s 周期互相绕行。</summary>
    private static void DrawTether(
        DrawingContext context,
        BackgroundSceneContext scene,
        Point anchor,
        double width,
        double height,
        double minSide,
        double scale,
        double time)
    {
        var end = new Point(width * 0.045, height * 1.06);
        var controlA = new Point(anchor.X - (minSide * 0.115), anchor.Y + (minSide * 0.20));
        var controlB = new Point(width * 0.012, height * 0.82);

        Point Along(double t)
        {
            var rest = 1.0 - t;
            var a = rest * rest * rest;
            var b = 3.0 * rest * rest * t;
            var c = 3.0 * rest * t * t;
            var d = t * t * t;
            return new Point(
                (a * anchor.X) + (b * controlA.X) + (c * controlB.X) + (d * end.X),
                (a * anchor.Y) + (b * controlA.Y) + (c * controlB.Y) + (d * end.Y));
        }

        var corePen = new Pen(new SolidColorBrush(scene.AccentAt(0.045)), Math.Clamp(1.0 * scale, 0.6, 2.4));
        var strandAPen = new Pen(new SolidColorBrush(scene.AccentAt(0.115)), Math.Clamp(1.15 * scale, 0.6, 2.4));
        var strandBPen = new Pen(new SolidColorBrush(scene.AccentAt(0.062)), Math.Clamp(0.95 * scale, 0.6, 2.4));

        var twistPhase = Tau * time / TetherTwistPeriod;
        var previousCore = Along(0);
        var previousA = previousCore;
        var previousB = previousCore;

        for (var i = 1; i <= TetherSteps; i++)
        {
            var t = (double)i / TetherSteps;
            var point = Along(t);
            var ahead = Along(Math.Min(1.0, t + 0.004));
            var tangentX = ahead.X - point.X;
            var tangentY = ahead.Y - point.Y;
            var tangentLength = Math.Sqrt((tangentX * tangentX) + (tangentY * tangentY));

            var twist = 0.0;
            var nx = 0.0;
            var ny = 0.0;
            if (tangentLength > 0.001)
            {
                nx = -tangentY / tangentLength;
                ny = tangentX / tangentLength;

                // 两端收口：扭转幅度用 sin(πt) 包络，挂点和末端都严格落在中线上。
                var envelope = Math.Sin(Math.PI * t);
                twist = Math.Sin(twistPhase + (t * Tau * 2.4)) * TetherTwistDepth * envelope * scale;
            }

            var strandA = new Point(point.X + (nx * twist), point.Y + (ny * twist));
            var strandB = new Point(point.X - (nx * twist), point.Y - (ny * twist));

            context.DrawLine(corePen, previousCore, point);
            context.DrawLine(strandAPen, previousA, strandA);
            context.DrawLine(strandBPen, previousB, strandB);

            previousCore = point;
            previousA = strandA;
            previousB = strandB;
        }

        // 挂点：一个扁环，读成系索眼环，不是航天器本体。
        var anchorPen = new Pen(new SolidColorBrush(scene.AccentAt(0.15)), Math.Clamp(1.2 * scale, 0.6, 2.4));
        context.DrawEllipse(null, anchorPen, anchor, 3.0 * scale, 1.8 * scale);
    }

    /// <summary>一根帆桁：主梁 + 两侧偏移细边，形成方管壁厚。</summary>
    private static void DrawBoom(
        DrawingContext context,
        BackgroundSceneContext scene,
        Point start,
        Point end,
        double scale,
        double alphaBreath)
    {
        var dx = end.X - start.X;
        var dy = end.Y - start.Y;
        var length = Math.Sqrt((dx * dx) + (dy * dy));
        if (!(length > 0.001))
        {
            return;
        }

        var nx = -dy / length;
        var ny = dx / length;
        var offset = 1.9 * scale;

        var core = new Pen(
            new SolidColorBrush(scene.AccentAt(0.19 * alphaBreath)),
            Math.Clamp(1.4 * scale, 0.6, 2.4));
        var rail = new Pen(
            new SolidColorBrush(scene.TextAt(0.055 * alphaBreath)),
            Math.Clamp(1.0 * scale, 0.6, 2.4));

        context.DrawLine(core, start, end);
        context.DrawLine(
            rail,
            new Point(start.X + (nx * offset), start.Y + (ny * offset)),
            new Point(end.X + (nx * offset), end.Y + (ny * offset)));
        context.DrawLine(
            rail,
            new Point(start.X - (nx * offset), start.Y - (ny * offset)),
            new Point(end.X - (nx * offset), end.Y - (ny * offset)));
    }

    /// <summary>把点朝中心收进 <paramref name="amount"/> DIP；距离不够时最多收到一半，避免翻边。</summary>
    private static Point Inset(Point point, Point center, double amount)
    {
        var dx = center.X - point.X;
        var dy = center.Y - point.Y;
        var length = Math.Sqrt((dx * dx) + (dy * dy));
        if (!(length > 0.001))
        {
            return point;
        }

        var step = Math.Min(amount, length * 0.5);
        return new Point(
            point.X + (dx / length * step),
            point.Y + (dy / length * step));
    }
}
