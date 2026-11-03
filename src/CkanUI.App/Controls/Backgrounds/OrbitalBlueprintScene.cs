using System;
using Avalonia;
using Avalonia.Media;

namespace CkanUI.App.Controls.Backgrounds;

/// <summary>
/// 轨道蓝图：左下角一颗只露四分之一弧线的巨行星，三条不同倾角的轨道椭圆围绕它缓慢进动，
/// 卫星标记沿轨道运行；另有稀疏刻度环、轨道节点、一条虚线基准轴和极淡的 8×8 蓝图网格。
/// </summary>
/// <remarks>
/// <para>
/// 无状态：运动只由 <see cref="BackgroundSceneContext.Time"/> 推导，光标响应只由
/// <see cref="BackgroundSceneContext.Influence"/> 与 <see cref="BackgroundSceneContext.InfluenceStrength"/> 推导，
/// 同一份输入永远画出同一帧。动效关闭时时间项整体冻结为零，构图静止，
/// 但光标对轨道倾角、相位与卫星高亮的影响照常生效。
/// </para>
/// <para>
/// 透明度按设计语言封顶：主体线 0.10–0.24（再乘主题强度与呼吸系数），辅助线 0.04–0.10，网格 ≤ 0.035；
/// 没有任何实色填充，全部靠描边（DrawLine / DrawGeometry / DrawRectangle）。
/// </para>
/// </remarks>
public sealed class OrbitalBlueprintScene : IBackgroundSceneRenderer
{
    /// <summary>轨道折线段数：48–72 段足够让大椭圆看不出棱角。</summary>
    private const int OrbitSegments = 64;

    /// <summary>行星弧折线段数。</summary>
    private const int PlanetArcSegments = 84;

    /// <summary>蓝图网格的列数/行数。</summary>
    private const int GridDivisions = 8;

    /// <summary>刻度环一圈的刻度数。</summary>
    private const int TickCount = 72;

    /// <summary>每颗卫星的牵引线最大长度系数（DIP，再乘 scene.Scale）。</summary>
    private const double TetherReach = 26.0;

    /// <summary>光标对卫星高亮的感应半径系数（DIP，再乘 scene.Scale）。</summary>
    private const double ProximityReach = 150.0;

    /// <summary>三条轨道的压扁比：倾角越小越接近正圆侧视。</summary>
    private static readonly double[] OrbitFlatten = [0.34, 0.43, 0.29];

    /// <summary>三条轨道的长半轴（相对 MinSide 的倍数）。</summary>
    private static readonly double[] OrbitRadius = [0.300, 0.375, 0.455];

    /// <summary>三条轨道的基准倾角（弧度）。</summary>
    private static readonly double[] OrbitTilt = [-0.30, 0.16, 0.40];

    /// <summary>三条轨道的进动周期（秒）：互质一些，不要同频。</summary>
    private static readonly double[] OrbitPrecessionSeconds = [86.0, 104.0, 119.0];

    /// <summary>三条轨道的主体线透明度。</summary>
    private static readonly double[] OrbitAlpha = [0.200, 0.145, 0.105];

    /// <summary>五颗卫星各自所在的轨道下标。</summary>
    private static readonly int[] SatelliteOrbit = [0, 1, 2, 1, 2];

    /// <summary>五颗卫星的运行初相。</summary>
    private static readonly double[] SatellitePhase = [0.10, 0.36, 0.58, 0.80, 0.93];

    /// <summary>五颗卫星的公转周期（秒）：各不相同，避免整片轨道同频。</summary>
    private static readonly double[] SatellitePeriod = [26.0, 33.0, 41.0, 29.0, 47.0];

    /// <summary>轨道节点所在的归一化参数。</summary>
    private static readonly double[] NodeParameters = [0.25, 0.75];

    private static readonly DashStyle AxisDash = new([7.0, 11.0], 0);
    private static readonly DashStyle ContourDash = new([3.0, 7.0], 0);
    private static readonly DashStyle TetherDash = new([3.0, 4.0], 0);

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

        var scale = double.IsFinite(scene.Scale) ? Math.Clamp(scene.Scale, 0.4, 2.0) : 1.0;
        var minSide = Math.Min(width, height);
        var planetRadius = minSide * 0.65;
        if (!double.IsFinite(planetRadius) || planetRadius < 12)
        {
            return;
        }

        // 动效关闭时把时间项冻结成零：不靠时间继续漂移，直接画基准构图。
        var time = scene.MotionEnabled && double.IsFinite(scene.Time) ? scene.Time : 0d;

        // 行星圆心刻意落在画布外的左下角，画布内只剩右上那一段弧。
        var planetCenter = new Point(-planetRadius * 0.10, height + (planetRadius * 0.09));

        var influence = scene.Influence;
        var strength = double.IsFinite(scene.InfluenceStrength) ? Math.Clamp(scene.InfluenceStrength, 0, 1) : 0;
        var pointerNx = double.IsFinite(influence.X)
            ? Math.Clamp((influence.X - (width * 0.5)) / (width * 0.5), -1, 1)
            : 0;
        var pointerNy = double.IsFinite(influence.Y)
            ? Math.Clamp((influence.Y - (height * 0.5)) / (height * 0.5), -1, 1)
            : 0;

        // 光标只改变一点点投影倾角/相位，幅度压在 0.02–0.05 rad；离开时 strength 归零自然回到基准。
        var tiltShift = 0.046 * pointerNy * strength;
        var phaseShift = 0.034 * pointerNx * strength;

        // 行星呼吸：27 秒一转，与任何轨道的周期都不同频。
        var breath = 0.84 + (0.16 * Math.Sin((time * Math.PI * 2) / 27.0));

        var orbitCenter = new Point(width * 0.34, height * 0.74);

        using var clip = context.PushClip(new Rect(0, 0, width, height));

        // ── 局部工具 ──────────────────────────────────────────────────
        Pen TextPen(double alpha, double thickness) => new(new SolidColorBrush(scene.TextAt(alpha)), thickness);

        Pen AccentPen(double alpha, double thickness) => new(new SolidColorBrush(scene.AccentAt(alpha)), thickness);

        Pen TextDashed(double alpha, double thickness, DashStyle dash) =>
            new(new SolidColorBrush(scene.TextAt(alpha)), thickness, dash);

        Point OnCircle(Point center, double radius, double angle) =>
            new(center.X + (Math.Cos(angle) * radius), center.Y + (Math.Sin(angle) * radius));

        // 旋转椭圆上的一点：t 为 0–1 的归一化参数（本文件的椭圆折线与卫星位置共用它）。
        Point RotatedEllipsePoint(Point center, double rx, double ry, double rotation, double t)
        {
            var angle = t * Math.PI * 2;
            var ex = Math.Cos(angle) * rx;
            var ey = Math.Sin(angle) * ry;
            var cos = Math.Cos(rotation);
            var sin = Math.Sin(rotation);
            return new Point(
                center.X + (ex * cos) - (ey * sin),
                center.Y + (ex * sin) + (ey * cos));
        }

        double OrbitRotation(int index)
        {
            var spin = (time / OrbitPrecessionSeconds[index]) * Math.PI * 2;
            return OrbitTilt[index] + spin + tiltShift + (phaseShift * 0.6);
        }

        double ArcStartAngle(double radius) => -Math.Acos(Math.Clamp(-planetCenter.X / radius, -1, 1));

        double ArcEndAngle(double radius) => Math.Asin(Math.Clamp((height - planetCenter.Y) / radius, -1, 1));

        // ── 1. 极淡的 8×8 蓝图网格 ───────────────────────────────────
        var gridMinor = TextPen(0.017, 1);
        var gridMajor = TextPen(0.029, 1);
        for (var i = 0; i <= GridDivisions; i++)
        {
            var gridPen = i % (GridDivisions / 2) == 0 ? gridMajor : gridMinor;
            var gx = (width * i) / GridDivisions;
            var gy = (height * i) / GridDivisions;
            context.DrawLine(gridPen, new Point(gx, 0), new Point(gx, height));
            context.DrawLine(gridPen, new Point(0, gy), new Point(width, gy));
        }

        // 右上角的制图角标，跟行星弧对角的非对称构图。
        var bracketPen = TextPen(0.062, 1);
        var bracketLength = 34 * scale;
        var bracketInset = 22 * scale;
        context.DrawLine(
            bracketPen,
            new Point(width - bracketInset - bracketLength, bracketInset),
            new Point(width - bracketInset, bracketInset));
        context.DrawLine(
            bracketPen,
            new Point(width - bracketInset, bracketInset),
            new Point(width - bracketInset, bracketInset + bracketLength));

        // ── 2. 虚线基准轴：穿过行星弧缓慢摆动，作为整张蓝图的参考线 ──
        var axisAngle = -0.62 + (0.05 * Math.Sin((time * Math.PI * 2) / 71.0)) + (tiltShift * 0.5);
        var axisStart = OnCircle(planetCenter, planetRadius, axisAngle);
        var axisPen = TextDashed(0.085, 1, AxisDash);
        var axisLength = planetRadius * 2.2;
        context.DrawLine(
            axisPen,
            axisStart,
            new Point(
                axisStart.X + (Math.Cos(axisAngle) * axisLength),
                axisStart.Y + (Math.Sin(axisAngle) * axisLength)));
        context.DrawEllipse(null, TextPen(0.075, 1), axisStart, 3.2 * scale, 3.2 * scale);

        // ── 3. 行星弧：只露四分之一，外圈垫一层很宽的淡光当呼吸 ─────
        void DrawPlanetArc(double radius, Pen core, Pen? halo, bool dashed)
        {
            var start = ArcStartAngle(radius);
            var end = ArcEndAngle(radius);
            if (!(end > start))
            {
                return;
            }

            var geometry = new StreamGeometry();
            using (var ctx = geometry.Open())
            {
                ctx.BeginFigure(OnCircle(planetCenter, radius, start), false);
                for (var i = 1; i <= PlanetArcSegments; i++)
                {
                    var angle = start + ((end - start) * i / PlanetArcSegments);
                    ctx.LineTo(OnCircle(planetCenter, radius, angle));
                }

                ctx.EndFigure(false);
            }

            if (halo is not null && !dashed)
            {
                context.DrawGeometry(null, halo, geometry);
            }

            context.DrawGeometry(null, core, geometry);
        }

        DrawPlanetArc(planetRadius, TextPen(0.225 * breath, 1.35), TextPen(0.055 * breath, 4.5), false);
        DrawPlanetArc(planetRadius * 0.955, TextPen(0.070 * breath, 1.0), null, false);
        DrawPlanetArc(planetRadius * 0.905, TextDashed(0.048 * breath, 1.0, ContourDash), null, true);

        // 行星内部的五根放射状制图线，只到弧内侧一点，读出“球体”的体积感。
        var spokePen = TextPen(0.045, 1);
        var spokeStart = ArcStartAngle(planetRadius);
        var spokeEnd = ArcEndAngle(planetRadius);
        if (spokeEnd > spokeStart)
        {
            for (var i = 0; i < 5; i++)
            {
                var angle = spokeStart + ((spokeEnd - spokeStart) * (i + 0.5) / 5.0);
                context.DrawLine(
                    spokePen,
                    OnCircle(planetCenter, planetRadius * 0.78, angle),
                    OnCircle(planetCenter, planetRadius * 0.998, angle));
            }
        }

        // ── 4. 稀疏刻度环：每 6° 一格，每 30° 加长并落一个节点小圆 ────
        var tickRadius = planetRadius * 1.045;
        var tickMinor = TextPen(0.052, 1);
        var tickMajor = TextPen(0.078, 1.15);
        var tickNode = TextPen(0.072, 1);
        for (var i = 0; i < TickCount; i++)
        {
            var angle = i * Math.PI * 2 / TickCount;
            var outer = OnCircle(planetCenter, tickRadius, angle);
            if (outer.X < -12 || outer.X > width + 12 || outer.Y < -12 || outer.Y > height + 12)
            {
                continue;
            }

            var major = i % 6 == 0;
            var inner = OnCircle(planetCenter, tickRadius - ((major ? 8.0 : 3.6) * scale), angle);
            context.DrawLine(major ? tickMajor : tickMinor, inner, outer);

            if (i % 18 == 0)
            {
                var node = OnCircle(planetCenter, tickRadius - (15.0 * scale), angle);
                context.DrawEllipse(null, tickNode, node, 2.4 * scale, 2.4 * scale);
            }
        }

        // ── 5. 三条轨道椭圆：整圈压暗、近侧半圈提亮，全部用折线画 ────
        void DrawEllipseArc(
            Point center,
            double rx,
            double ry,
            double rotation,
            double fromT,
            double toT,
            int segments,
            Pen pen)
        {
            var geometry = new StreamGeometry();
            using (var ctx = geometry.Open())
            {
                ctx.BeginFigure(RotatedEllipsePoint(center, rx, ry, rotation, fromT), false);
                for (var i = 1; i <= segments; i++)
                {
                    ctx.LineTo(RotatedEllipsePoint(center, rx, ry, rotation, fromT + ((toT - fromT) * i / segments)));
                }

                ctx.EndFigure(false);
            }

            context.DrawGeometry(null, pen, geometry);
        }

        var orbitRx = new double[OrbitRadius.Length];
        var orbitRy = new double[OrbitRadius.Length];
        var orbitRotation = new double[OrbitRadius.Length];
        for (var o = 0; o < OrbitRadius.Length; o++)
        {
            orbitRx[o] = minSide * OrbitRadius[o];
            orbitRy[o] = orbitRx[o] * OrbitFlatten[o];
            orbitRotation[o] = OrbitRotation(o);

            var farPen = AccentPen(OrbitAlpha[o] * 0.45, 1.0);
            var nearPen = AccentPen(OrbitAlpha[o], 1.15);
            DrawEllipseArc(orbitCenter, orbitRx[o], orbitRy[o], orbitRotation[o], 0, 1, OrbitSegments, farPen);
            DrawEllipseArc(orbitCenter, orbitRx[o], orbitRy[o], orbitRotation[o], 0, 0.5, OrbitSegments / 2, nearPen);

            // 轨道节点：两个固定参数处的小圆 + 十字准星。
            var nodePen = TextPen(0.080, 1);
            foreach (var nodeT in NodeParameters)
            {
                var node = RotatedEllipsePoint(orbitCenter, orbitRx[o], orbitRy[o], orbitRotation[o], nodeT);
                context.DrawEllipse(null, nodePen, node, 2.6 * scale, 2.6 * scale);
                var arm = 5.0 * scale;
                context.DrawLine(nodePen, new Point(node.X - arm, node.Y), new Point(node.X + arm, node.Y));
                context.DrawLine(nodePen, new Point(node.X, node.Y - arm), new Point(node.X, node.Y + arm));
            }
        }

        // ── 6. 卫星标记：小方块 + 两片太阳翼 + 一根天线，靠近光标提亮并拉牵引线 ──
        for (var s = 0; s < SatelliteOrbit.Length; s++)
        {
            var o = SatelliteOrbit[s];
            var travel = (time / SatellitePeriod[s]) + SatellitePhase[s];

            // 轻微变速：参数上叠一点低频抖动，视觉上是“快慢不一”，不是忽快忽慢。
            var t = travel + (0.008 * Math.Sin((time * Math.PI * 2) / SatellitePeriod[s]));
            var position = RotatedEllipsePoint(orbitCenter, orbitRx[o], orbitRy[o], orbitRotation[o], t);

            var margin = 24 * scale;
            if (position.X < -margin || position.X > width + margin
                || position.Y < -margin || position.Y > height + margin)
            {
                continue;
            }

            var dx = position.X - influence.X;
            var dy = position.Y - influence.Y;
            var distance = Math.Sqrt((dx * dx) + (dy * dy));
            var proximity = 0d;
            if (strength > 0 && distance < ProximityReach * scale)
            {
                var raw = 1.0 - (distance / (ProximityReach * scale));
                proximity = raw * raw * strength;
            }

            var half = Math.Max(3.2, 4.6 * scale);
            var bodyPen = AccentPen(0.135 + (0.105 * proximity), 1.1 + (0.35 * proximity));
            var wingPen = AccentPen(0.095 + (0.065 * proximity), 1.0);
            var antennaPen = AccentPen(0.085 + (0.055 * proximity), 1.0);

            context.DrawRectangle(
                null,
                bodyPen,
                new Rect(position.X - half, position.Y - half, half * 2, half * 2),
                0,
                0);

            var wingLength = 9.5 * scale;
            var wingHalf = 2.0 * scale;
            context.DrawRectangle(
                null,
                wingPen,
                new Rect(position.X - half - wingLength, position.Y - wingHalf, wingLength, wingHalf * 2),
                0,
                0);
            context.DrawRectangle(
                null,
                wingPen,
                new Rect(position.X + half, position.Y - wingHalf, wingLength, wingHalf * 2),
                0,
                0);

            var mastTop = new Point(position.X, position.Y - half - (7.5 * scale));
            context.DrawLine(antennaPen, new Point(position.X, position.Y - half), mastTop);
            context.DrawEllipse(
                new SolidColorBrush(scene.AccentAt(0.10 + (0.09 * proximity))),
                null,
                mastTop,
                1.2 * scale,
                1.2 * scale);

            if (proximity <= 0.02)
            {
                continue;
            }

            // 高亮外环 + 短牵引线：只指向光标方向，长度随接近度增长。
            context.DrawEllipse(
                null,
                AccentPen(0.055 + (0.075 * proximity), 1.0),
                position,
                half + (5.5 * scale * proximity),
                half + (5.5 * scale * proximity));

            var length = distance;
            if (length > 0.001)
            {
                var dirX = dx / length;
                var dirY = dy / length;
                var tetherPen = new Pen(
                    new SolidColorBrush(scene.AccentAt(0.090 + (0.130 * proximity))),
                    1.0,
                    TetherDash);
                var tetherStart = new Point(
                    position.X - (dirX * (half + (3.0 * scale))),
                    position.Y - (dirY * (half + (3.0 * scale))));
                var tetherEnd = new Point(
                    tetherStart.X - (dirX * TetherReach * scale * proximity),
                    tetherStart.Y - (dirY * TetherReach * scale * proximity));
                context.DrawLine(tetherPen, tetherStart, tetherEnd);
                context.DrawEllipse(null, tetherPen, tetherEnd, 1.5 * scale, 1.5 * scale);
            }
        }
    }
}
