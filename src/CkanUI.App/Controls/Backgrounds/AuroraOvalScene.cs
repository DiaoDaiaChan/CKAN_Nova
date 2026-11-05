using System;
using Avalonia;
using Avalonia.Media;

namespace CkanUI.App.Controls.Backgrounds;

/// <summary>
/// 极光椭圆：左下角的磁层弧从画布左缘切进来，沿椭圆上沿挂三条极光帘；
/// 帘边是折线，帘内是单色强调的场线纹理，整幅图只透过页面缝隙可见。
/// </summary>
/// <remarks>
/// <para>
/// 构图压向左下：椭圆中心在 (0.135W, 0.80H)，长半轴取短边的 0.42，半个椭圆落在画布外，
/// 画面里只留上沿那段弧和三条帘子；帘顶最高到画面约 35% 高度，且全部收在左半边，
/// 中间大片留白让给页面卡片。弧在右上留一个缺口，避免闭合成跑道环。
/// </para>
/// <para>
/// 无状态：帘子 23s 摆动、椭圆与场线 61s 呼吸、帘边另叠 31s 细波纹，全部由 <c>scene.Time</c> 推导；
/// 没有 Random、没有帧间缓存，同一份输入永远画出同一帧。动效关闭时时间钉在 0，画面停住，
/// 但光标位移与提亮照旧生效——关掉动效不该连带关掉可感知的状态变化。
/// </para>
/// <para>
/// 光标落在上半屏时整座椭圆朝光标平移 3–6 DIP，并提亮离光标最近的那条帘子；下半屏只提亮、不位移，
/// 避免用户读卡片时背景跟着手跑。位移量与提亮都读 <c>scene.Influence</c> / <c>scene.InfluenceStrength</c>，
/// 没有光标时强度为 0，画面照常渲染。
/// </para>
/// <para>
/// alpha 天花板 0.24，环境线 0.04–0.10；帘子只用强调色，椭圆、场线、刻度只用正文色，
/// 单色强调不串色。没有渐变填充：极光靠折线边缘与纹理密度表达，不靠色块。
/// </para>
/// </remarks>
public sealed class AuroraOvalScene : IBackgroundSceneRenderer
{
    /// <summary>2π：周期换算常量。</summary>
    private const double Tau = Math.PI * 2.0;

    /// <summary>帘子摆动周期（秒）。</summary>
    private const double SwayPeriod = 23.0;

    /// <summary>椭圆与场线的呼吸周期（秒）。</summary>
    private const double BreathPeriod = 61.0;

    /// <summary>帘边细波纹周期（秒）：与摆动错开，帘子才不会整体硬摆。</summary>
    private const double RipplePeriod = 31.0;

    /// <summary>椭圆短半轴与长半轴之比：压扁的磁层椭圆，不是正圆。</summary>
    private const double MinorRatio = 0.34;

    /// <summary>椭圆倾角（弧度）：长轴朝右上抬约 21.8°。</summary>
    private const double Tilt = -0.38;

    /// <summary>呼吸时倾角的摆动幅度（弧度）。</summary>
    private const double TiltBreath = 0.012;

    /// <summary>可见弧的起始角（π 为单位）：从画布左缘切进来。</summary>
    private const double ArcStartTurns = -0.68;

    /// <summary>可见弧的扫过角（π 为单位）：到画布下缘收口，右上留缺口。</summary>
    private const double ArcSweepTurns = 1.34;

    /// <summary>外弧折线段数。</summary>
    private const int ArcSegments = 96;

    /// <summary>内弧折线段数：内弧更淡，少一半段数也看不出折角。</summary>
    private const int InnerArcSegments = 64;

    /// <summary>内弧半径比例：椭圆带的厚度。</summary>
    private const double InnerArcRatio = 0.86;

    /// <summary>外弧外侧的回声线比例。</summary>
    private const double EchoArcRatio = 1.035;

    /// <summary>椭圆带上的刻度线数量。</summary>
    private const int BandTicks = 13;

    /// <summary>帘边的折线段数：18 段足够看出折线感，又不至于变成锯齿。</summary>
    private const int EdgeSegments = 18;

    /// <summary>帘内场线纹理条数。</summary>
    private const int Streaks = 3;

    /// <summary>每条纹理的折线段数。</summary>
    private const int StreakSegments = 9;

    /// <summary>每条场线的折线段数。</summary>
    private const int FieldSegments = 22;

    /// <summary>帘子摆动幅度（DIP × Scale）。</summary>
    private const double SwayDips = 3.4;

    /// <summary>帘边波纹幅度（DIP × Scale）。</summary>
    private const double RippleDips = 2.6;

    /// <summary>光标位移基准（DIP × Scale），最终夹在 3–6 DIP。</summary>
    private const double ShiftDips = 4.6;

    /// <summary>光标提亮半径与长半轴之比。</summary>
    private const double GlowRadiusRatio = 0.30;

    /// <summary>α 天花板：任何核心线都不超过它。</summary>
    private const double CoreCeiling = 0.24;

    /// <summary>
    /// 三条帘子：极角（π 为单位）、高度（长半轴的倍数）、倾角（弧度，负值偏左）、
    /// 弯曲量（长半轴的倍数）、相位（错开摆动）。
    /// </summary>
    private static readonly (double Theta, double Height, double Lean, double Bow, double Phase)[] Curtains =
    [
        (-0.54, 0.50, -0.08, 0.012, 0.0),
        (-0.36, 0.58, -0.16, -0.018, 2.1),
        (-0.18, 0.46, -0.24, 0.010, 4.2)
    ];

    /// <summary>三条场线：极角、高度、倾角、横向漂移、相位；都朝左上张开，不进入页面中心。</summary>
    private static readonly (double Theta, double Height, double Lean, double Drift, double Phase)[] FieldLines =
    [
        (-0.62, 0.46, -0.42, -0.11, 0.0),
        (-0.44, 0.60, -0.30, 0.06, 1.9),
        (-0.08, 0.50, -0.16, 0.09, 3.7)
    ];

    /// <inheritdoc />
    public void Render(DrawingContext context, BackgroundSceneContext scene)
    {
        ArgumentNullException.ThrowIfNull(context);

        var width = scene.Width;
        var height = scene.Height;
        if (!double.IsFinite(width) || !double.IsFinite(height) || width < 96.0 || height < 96.0)
        {
            return;
        }

        if (!double.IsFinite(scene.Intensity) || scene.Intensity <= 0.002)
        {
            return;
        }

        var scale = double.IsFinite(scene.Scale) ? Math.Clamp(scene.Scale, 0.5, 1.9) : 1.0;
        var time = scene.MotionEnabled && double.IsFinite(scene.Time) ? Math.Max(0.0, scene.Time) : 0.0;
        var strength = double.IsFinite(scene.InfluenceStrength) ? Math.Clamp(scene.InfluenceStrength, 0.0, 1.0) : 0.0;
        var aim = scene.Influence;
        var aimActive = strength > 0.001 && double.IsFinite(aim.X) && double.IsFinite(aim.Y);
        var upperAim = aimActive && aim.Y < height * 0.5;

        var unit = Math.Clamp(Math.Min(width, height) * 0.42, 120.0, 560.0);
        var baseCenterX = width * 0.135;
        var baseCenterY = height * 0.80;

        // 3–6 DIP 的位移：够感知“朝手边偏了一点”，又不会让构图跳。
        var shiftX = 0.0;
        var shiftY = 0.0;
        if (upperAim)
        {
            var deltaX = aim.X - baseCenterX;
            var deltaY = aim.Y - baseCenterY;
            var length = Math.Sqrt((deltaX * deltaX) + (deltaY * deltaY));
            if (length > 0.5)
            {
                var amount = Math.Clamp(ShiftDips * scale, 3.0, 6.0) * strength;
                shiftX = deltaX / length * amount;
                shiftY = deltaY / length * amount;
            }
        }

        var tilt = Tilt + (Math.Sin((Tau * time / BreathPeriod) + 0.7) * TiltBreath);
        var frame = new AuroraFrame(new Point(baseCenterX + shiftX, baseCenterY + shiftY), unit, tilt);

        DrawFieldLines(context, scene, frame, time, scale);
        DrawOvalBand(context, scene, frame, time, scale);

        for (var i = 0; i < Curtains.Length; i++)
        {
            DrawCurtain(context, scene, frame, Curtains[i], time, aim.X, aim.Y, upperAim, strength, scale);
        }
    }

    /// <summary>椭圆带：外弧 + 外侧回声 + 内弧 + 刻度；呼吸只改内弧厚度与亮度，不改构图。</summary>
    private static void DrawOvalBand(
        DrawingContext context,
        BackgroundSceneContext scene,
        AuroraFrame frame,
        double time,
        double scale)
    {
        var breath = Math.Sin((Tau * time / BreathPeriod) + 1.3);
        var innerRatio = InnerArcRatio + (breath * 0.02);
        var thin = Math.Clamp(0.85 * scale, 0.6, 1.4);
        var hair = Math.Clamp(1.2 * scale, 0.9, 2.0);

        DrawArc(context, frame, EchoArcRatio, Stroke(scene.TextAt(0.040), thin), ArcSegments);
        DrawArc(context, frame, 1.0, Stroke(scene.TextAt(0.165 + (breath * 0.008)), hair), ArcSegments);
        DrawArc(context, frame, innerRatio, Stroke(scene.TextAt(0.072 - (breath * 0.006)), thin), InnerArcSegments);

        var tickPen = Stroke(scene.TextAt(0.050 + (breath * 0.004)), thin);
        for (var i = 0; i <= BandTicks; i++)
        {
            var theta = (ArcStartTurns + (ArcSweepTurns * i / BandTicks)) * Math.PI;
            var outer = i % 3 == 0 ? EchoArcRatio : 1.0;
            context.DrawLine(tickPen, frame.Oval(theta, innerRatio), frame.Oval(theta, outer));
        }
    }

    /// <summary>场线：从椭圆带向外张开的淡弧，61s 呼吸只改长度与亮度；根部一个小点标出磁力线脚。</summary>
    private static void DrawFieldLines(
        DrawingContext context,
        BackgroundSceneContext scene,
        AuroraFrame frame,
        double time,
        double scale)
    {
        var breath = Math.Sin(Tau * time / BreathPeriod);
        var hair = Math.Clamp(0.9 * scale, 0.6, 1.3);
        var nearPen = Stroke(scene.TextAt(0.072 + (breath * 0.008)), hair);
        var farPen = Stroke(scene.TextAt(0.044 + (breath * 0.006)), hair);
        var rootPen = Stroke(scene.TextAt(0.050), hair);

        for (var i = 0; i < FieldLines.Length; i++)
        {
            var line = FieldLines[i];
            var root = frame.Oval(line.Theta * Math.PI, 1.0);
            var height = frame.Unit * line.Height
                * (1.0 + (Math.Sin((Tau * time / BreathPeriod) + line.Phase) * 0.05));
            var angle = (-Math.PI * 0.5) + line.Lean;
            var dirX = Math.Cos(angle);
            var dirY = Math.Sin(angle);
            var latX = -dirY;
            var latY = dirX;

            var controlA = new Point(
                root.X + (dirX * height * 0.36) + (latX * height * line.Drift * 0.45),
                root.Y + (dirY * height * 0.36) + (latY * height * line.Drift * 0.45));
            var controlB = new Point(
                root.X + (dirX * height * 0.76) + (latX * height * line.Drift * 1.15),
                root.Y + (dirY * height * 0.76) + (latY * height * line.Drift * 1.15));
            var end = new Point(
                root.X + (dirX * height) + (latX * height * line.Drift * 1.55),
                root.Y + (dirY * height) + (latY * height * line.Drift * 1.55));

            var previous = root;
            for (var step = 1; step <= FieldSegments; step++)
            {
                var t = (double)step / FieldSegments;
                var current = Cubic(root, controlA, controlB, end, t);
                context.DrawLine(t < 0.55 ? nearPen : farPen, previous, current);
                previous = current;
            }

            context.DrawEllipse(null, rootPen, root, 1.7 * scale, 1.7 * scale);
        }
    }

    /// <summary>一条极光帘：折线双边 + 中脊 + 三条纹理 + 根部足迹；光标靠近时整条提亮。</summary>
    /// <param name="layout">极角、高度、倾角、弯曲量、相位。</param>
    /// <param name="aimActive">光标是否在上半屏且强度可用；只有这时才提亮局部。</param>
    private static void DrawCurtain(
        DrawingContext context,
        BackgroundSceneContext scene,
        AuroraFrame frame,
        (double Theta, double Height, double Lean, double Bow, double Phase) layout,
        double time,
        double aimX,
        double aimY,
        bool aimActive,
        double strength,
        double scale)
    {
        var root = frame.Oval(layout.Theta * Math.PI, 1.0);
        var height = frame.Unit * layout.Height
            * (1.0 + (Math.Sin((Tau * time / BreathPeriod) + (layout.Phase * 1.7)) * 0.035));
        var angle = (-Math.PI * 0.5) + layout.Lean;
        var dirX = Math.Cos(angle);
        var dirY = Math.Sin(angle);
        var latX = -dirY;
        var latY = dirX;

        var sway = Math.Sin((Tau * time / SwayPeriod) + layout.Phase) * SwayDips * scale;
        var bow = layout.Bow * frame.Unit;

        Point Spine(double v)
        {
            var drift = (bow * Math.Sin(Math.PI * v)) + (sway * Math.Pow(v, 1.35));
            return new Point(
                root.X + (dirX * height * v) + (latX * drift),
                root.Y + (dirY * height * v) + (latY * drift));
        }

        double HalfWidth(double v) => frame.Unit * (0.030 + (0.056 * Math.Pow(v, 1.15)));

        Point Edge(double v, double side)
        {
            var wave = Math.Sin(
                    (Tau * v * 1.7)
                    - (Tau * time / RipplePeriod)
                    + layout.Phase
                    + (side * 0.9))
                * RippleDips * scale * Math.Pow(v, 1.3);
            var offset = side * (HalfWidth(v) + wave);
            var spine = Spine(v);
            return new Point(spine.X + (latX * offset), spine.Y + (latY * offset));
        }

        Point Streak(double v, double fraction)
        {
            var spine = Spine(v);
            var offset = fraction * HalfWidth(v) * 1.5;
            return new Point(spine.X + (latX * offset), spine.Y + (latY * offset));
        }

        var glow = 0.0;
        if (aimActive)
        {
            var radius = Math.Max(30.0, frame.Unit * GlowRadiusRatio);
            var nearest = Math.Min(
                Distance(aimX, aimY, root),
                Math.Min(
                    Distance(aimX, aimY, Spine(0.55)),
                    Distance(aimX, aimY, Spine(0.90))));
            var proximity = Math.Clamp(1.0 - (nearest / radius), 0.0, 1.0);
            glow = strength * proximity * proximity;
        }

        var hair = Math.Clamp(1.05 * scale, 0.8, 1.8);
        var nudge = glow * 0.05;
        var edgeNear = Stroke(scene.AccentAt(Math.Min(CoreCeiling, 0.180 + nudge)), hair);
        var edgeMid = Stroke(scene.AccentAt(0.148 + (nudge * 0.8)), hair);
        var edgeFar = Stroke(scene.AccentAt(0.100 + (nudge * 0.6)), hair);
        var keel = Stroke(scene.AccentAt(Math.Min(CoreCeiling, 0.190 + nudge)), Math.Clamp(1.15 * scale, 0.8, 2.0));
        var streakNear = Stroke(scene.AccentAt(0.062 + (glow * 0.028)), Math.Clamp(0.8 * scale, 0.6, 1.2));
        var streakFar = Stroke(scene.AccentAt(0.040 + (glow * 0.018)), Math.Clamp(0.8 * scale, 0.6, 1.2));
        var foot = Stroke(scene.AccentAt(0.105 + (glow * 0.04)), hair);

        IPen EdgePen(double v) => v < 0.32 ? edgeNear : v < 0.68 ? edgeMid : edgeFar;

        for (var side = -1; side <= 1; side += 2)
        {
            var previous = Edge(0.0, side);
            for (var i = 1; i <= EdgeSegments; i++)
            {
                var v = (double)i / EdgeSegments;
                var current = Edge(v, side);
                context.DrawLine(EdgePen(v), previous, current);
                previous = current;
            }
        }

        var keelPrevious = Spine(0.0);
        for (var i = 1; i <= 12; i++)
        {
            var v = (double)i / 12.0;
            var current = Spine(v);
            context.DrawLine(v < 0.62 ? keel : edgeMid, keelPrevious, current);
            keelPrevious = current;
        }

        for (var k = 0; k < Streaks; k++)
        {
            var fraction = -0.45 + (k * 0.45);
            var previous = Streak(0.06, fraction);
            for (var i = 1; i <= StreakSegments; i++)
            {
                var v = 0.06 + (0.86 * i / StreakSegments);
                var current = Streak(v, fraction);
                context.DrawLine(v < 0.52 ? streakNear : streakFar, previous, current);
                previous = current;
            }
        }

        context.DrawEllipse(null, foot, root, 2.2 * scale, 1.1 * scale);
    }

    /// <summary>把椭圆弧拆成折线放进一条 <see cref="StreamGeometry"/>：一条路径一次提交，不在循环里逐段 DrawLine。</summary>
    private static void DrawArc(DrawingContext context, AuroraFrame frame, double radiusRatio, IPen pen, int segments)
    {
        var start = ArcStartTurns * Math.PI;
        var sweep = ArcSweepTurns * Math.PI;
        var geometry = new StreamGeometry();
        using (var stream = geometry.Open())
        {
            stream.BeginFigure(frame.Oval(start, radiusRatio), false);
            for (var i = 1; i <= segments; i++)
            {
                stream.LineTo(frame.Oval(start + (sweep * i / segments), radiusRatio), true);
            }

            stream.EndFigure(false);
        }

        context.DrawGeometry(null, pen, geometry);
    }

    /// <summary>三次贝塞尔取点：场线的弯曲只靠控制点，不引入新的状态。</summary>
    private static Point Cubic(Point start, Point controlA, Point controlB, Point end, double t)
    {
        var rest = 1.0 - t;
        var a = rest * rest * rest;
        var b = 3.0 * rest * rest * t;
        var c = 3.0 * rest * t * t;
        var d = t * t * t;
        return new Point(
            (a * start.X) + (b * controlA.X) + (c * controlB.X) + (d * end.X),
            (a * start.Y) + (b * controlA.Y) + (c * controlB.Y) + (d * end.Y));
    }

    /// <summary>点到点的距离：光标提亮用。</summary>
    private static double Distance(double x, double y, Point point)
    {
        var dx = x - point.X;
        var dy = y - point.Y;
        return Math.Sqrt((dx * dx) + (dy * dy));
    }

    /// <summary>一支细线画笔：透明度已经由 <see cref="BackgroundSceneContext.AccentAt"/> 之类折进颜色里。</summary>
    private static Pen Stroke(Color color, double thickness) =>
        new(new SolidColorBrush(color, 1.0), thickness);

    /// <summary>椭圆局部坐标系：中心、长半轴、倾角；帘子与场线都从这里映射到画布。</summary>
    /// <param name="Center">椭圆中心（已含光标位移）。</param>
    /// <param name="Unit">长半轴（DIP）。</param>
    /// <param name="Tilt">倾角（弧度，已含呼吸）。</param>
    private readonly record struct AuroraFrame(Point Center, double Unit, double Tilt)
    {
        /// <summary>按极角与半径比例取椭圆上的点；<paramref name="radiusRatio"/> 1.0 是外弧。</summary>
        public Point Oval(double theta, double radiusRatio)
        {
            var (sin, cos) = Math.SinCos(theta);
            var (tiltSin, tiltCos) = Math.SinCos(Tilt);
            var major = Unit * radiusRatio;
            var minor = major * MinorRatio;
            return new Point(
                Center.X + (cos * major * tiltCos) - (sin * minor * tiltSin),
                Center.Y + (cos * major * tiltSin) + (sin * minor * tiltCos));
        }
    }
}
