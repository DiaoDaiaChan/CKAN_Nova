using System;
using Avalonia;
using Avalonia.Media;

namespace CkanUI.App.Controls.Backgrounds;

/// <summary>
/// 日冕：日全食压在右上角画外，月面几乎全黑，五条日冕流苏朝画内铺开，
/// 最靠近画面中心的那段月缘上落着一枚钻石环光点。
/// </summary>
/// <remarks>
/// <para>
/// 构图刻意偏心：月心在画布右上角之外（右出 0.16R、上出 0.12R），画面里只留左下那一段月缘，
/// 因此焦点永远在角落，不会变成居中的太阳插画。流苏只朝左上、左下两个方向伸，最远落在画面上部，
/// 中心的正文区留白；没有大面积填充，月亮本体靠「没有线」和两圈内缘暗弧暗示。
/// </para>
/// <para>
/// 无状态：呼吸（41s）、流苏摆动（53s）、钻石环闪烁（7s）全部由 <see cref="BackgroundSceneContext.Time"/> 推导，
/// 同一份输入永远画出同一帧，无随机数、无缓存、无资源查找。动效关闭时时间项钉在 0，
/// 构图静止，但指针仍按 <see cref="BackgroundSceneContext.Influence"/> /
/// <see cref="BackgroundSceneContext.InfluenceStrength"/> 让流苏内弯、钻石环提亮。
/// </para>
/// <para>
/// 亮度封顶：月缘核心 ≤0.235，流苏核心 ≤0.235，内冕带 ≤0.182，钻石环核心 ≤0.24；
/// 环境线（外冕弧、月面内缘暗弧）0.035–0.095。线宽 0.6–2.4 DIP，随 <c>scene.Scale</c> 缩放。
/// 单帧约 640 条线段（流苏 160、月缘 128、内外冕与光点弧约 350），段数固定，不随窗口尺寸膨胀。
/// </para>
/// </remarks>
public sealed class CoronaScene : IBackgroundSceneRenderer
{
    /// <summary>整圈弧度。</summary>
    private const double TwoPi = Math.PI * 2.0;

    /// <summary>角度转弧度。</summary>
    private const double DegToRad = Math.PI / 180.0;

    /// <summary>月面半径与画布短边之比：够大才有压迫感，又不至于吃掉整块画布。</summary>
    private const double DiscRadiusRatio = 0.34;

    /// <summary>月心越出右边缘的距离（按半径的比例）：月心永远落在画布之外。</summary>
    private const double DiscCenterXBleed = 0.16;

    /// <summary>月心越出上边缘的距离（按半径的比例）。</summary>
    private const double DiscCenterYBleed = 0.12;

    /// <summary>日冕整体呼吸周期（秒）：合同要求 41 秒。</summary>
    private const double BreathSeconds = 41.0;

    /// <summary>钻石环闪烁周期（秒）：合同要求 7 秒。</summary>
    private const double ShimmerSeconds = 7.0;

    /// <summary>流苏摆动的慢周期（秒）：与呼吸错频，半分钟内看不出循环。</summary>
    private const double SwaySeconds = 53.0;

    /// <summary>光标响应半径（DIP）：合同规定 180 DIP。</summary>
    private const double PointerReach = 180.0;

    /// <summary>流苏朝光标弯曲的最小幅度（DIP）。</summary>
    private const double PointerBendMin = 2.0;

    /// <summary>流苏朝光标弯曲的最大幅度（DIP）。</summary>
    private const double PointerBendMax = 4.0;

    /// <summary>每条流苏的折线段数：16 段在 4K 下也看不出棱角。</summary>
    private const int StreamerSegments = 16;

    /// <summary>可见月缘（约一个象限）的折线段数。</summary>
    private const int LimbSegments = 64;

    /// <summary>
    /// 五条流苏：极角（度，0° 向右、90° 向下）、长度（半径倍数）、基准透明度、
    /// 基准线宽（DIP × Scale）、弯曲量（半径比例，正负决定向哪侧弯）、相位。
    /// 角度集中在 102°–166°，全部朝画内（左侧/左下）伸，避开画面中心。
    /// </summary>
    private static readonly (double Angle, double Length, double Alpha, double Thickness, double Curl, double Phase)[] Streamers =
    [
        (102.0, 1.45, 0.190, 1.55, 0.16, 0.0),
        (120.0, 1.75, 0.142, 2.15, -0.13, 1.1),
        (138.0, 1.60, 0.204, 1.35, 0.19, 2.4),
        (154.0, 1.95, 0.112, 2.45, -0.17, 3.3),
        (166.0, 1.50, 0.096, 1.15, 0.11, 4.6)
    ];

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

        var intensity = double.IsFinite(scene.Intensity) ? Math.Clamp(scene.Intensity, 0.0, 1.0) : 1.0;
        if (intensity <= 0.004)
        {
            return;
        }

        var radius = Math.Min(width, height) * DiscRadiusRatio;
        if (!double.IsFinite(radius) || !(radius > 6.0))
        {
            return;
        }

        var scale = double.IsFinite(scene.Scale) ? Math.Clamp(scene.Scale, 0.5, 1.9) : 1.0;
        var weight = Math.Clamp(scale, 0.55, 1.45);

        // 动效关闭时把时间钉在 0：呼吸和闪烁停住，指针响应仍由 Influence/InfluenceStrength 推导。
        var time = scene.MotionEnabled && double.IsFinite(scene.Time) ? Math.Max(0.0, scene.Time) : 0.0;

        var strength = double.IsFinite(scene.InfluenceStrength) ? Math.Clamp(scene.InfluenceStrength, 0.0, 1.0) : 0.0;
        var influence = scene.Influence;
        if (!double.IsFinite(influence.X) || !double.IsFinite(influence.Y))
        {
            influence = scene.Anchor;
            strength = 0.0;
        }

        var center = new Point(width + (radius * DiscCenterXBleed), -radius * DiscCenterYBleed);
        var breath = Math.Sin(time * (TwoPi / BreathSeconds));
        var shimmer = 0.5 + (0.5 * Math.Sin(time * (TwoPi / ShimmerSeconds)));

        // 光标到月缘的间隙：180 DIP 内才响应，越近越强；光标不存在时强度为 0，这里也自然退化为 0。
        var proximity = 0.0;
        if (strength > 0.001)
        {
            var gapX = influence.X - center.X;
            var gapY = influence.Y - center.Y;
            var distance = Math.Sqrt((gapX * gapX) + (gapY * gapY));
            var gap = Math.Max(0.0, distance - radius);
            if (gap < PointerReach)
            {
                proximity = (1.0 - (gap / PointerReach)) * strength;
            }
        }

        // 位移按合同用 DIP 实数：2–4 DIP，不随 DPI scale 放大，否则 4K 下会超上限。
        var bend = (PointerBendMin + ((PointerBendMax - PointerBendMin) * proximity)) * proximity;

        // 钻石环落在「月心 → 画面中心」这段月缘上：最靠近正文的一侧，也是视觉上唯一该亮的点。
        var diamondAngle = Math.Atan2(scene.Anchor.Y - center.Y, scene.Anchor.X - center.X);
        var diamondCos = Math.Cos(diamondAngle);
        var diamondSin = Math.Sin(diamondAngle);
        var diamondPoint = new Point(
            center.X + (diamondCos * radius * 1.006),
            center.Y + (diamondSin * radius * 1.006));

        using var clip = context.PushClip(new Rect(0, 0, width, height));

        Pen AccentPen(double alpha, double thickness) =>
            new(new SolidColorBrush(scene.AccentAt(Math.Min(alpha, 0.24))), Math.Clamp(thickness, 0.6, 2.4));

        Pen TextPen(double alpha, double thickness) =>
            new(new SolidColorBrush(scene.TextAt(Math.Min(alpha, 0.095))), Math.Clamp(thickness, 0.6, 2.4));

        // ── 1. 外冕：三圈弧，呼吸时只微微胀缩，给流苏一个底 ───────────────
        for (var ring = 0; ring < 3; ring++)
        {
            var ringRatio = 1.16 + (ring * 0.16) + (0.012 * breath);
            var ringRadius = radius * ringRatio;
            // 环境线守合同下限 0.04：再淡就看不见，也容易被误当成压缩噪声。
            var ringAlpha = Math.Max(0.04, 0.060 - (ring * 0.009) + (0.008 * breath));
            var ringPen = AccentPen(ringAlpha, Math.Max(0.6, (0.85 - (ring * 0.1)) * weight));
            var offset = ring * 0.12;

            // 只画朝画内的那一段：外冕在画布外的部分没有意义，也省掉一半线条。
            DrawArc(context, ringPen, center, ringRadius, 1.30 + offset, 1.95 - offset, 56);
        }

        // ── 2. 流苏：双层描边（淡宽晕 + 细芯），朝光标内弯 2–4 DIP ─────────
        for (var index = 0; index < Streamers.Length; index++)
        {
            var streamer = Streamers[index];
            var breathePhase = (time * (TwoPi / BreathSeconds)) + streamer.Phase;
            var lengthScale = 1.0 + (0.075 * Math.Sin(breathePhase));
            var alphaScale = 1.0 + (0.14 * Math.Sin(breathePhase + 0.9));
            var sway = Math.Sin((time * (TwoPi / SwaySeconds)) + streamer.Phase) * 0.022;

            var angle = (streamer.Angle * DegToRad) + sway;
            var cos = Math.Cos(angle);
            var sin = Math.Sin(angle);
            // 视觉半径与「中心留白」取小：高窄窗口里流苏会正对画面中心，必须在那之前收住，
            // 否则线尖扎进正文区，背景就变成前景了。安全圈半径按短边取，随窗口比例自适应。
            var safeRadius = Math.Min(width, height) * (0.37 + (index * 0.008));
            var desiredReach = radius * streamer.Length * lengthScale;
            var safeReach = SafeReach(center, cos, sin, scene.Anchor, safeRadius) - (5.0 * scale) - radius;
            var reach = Math.Max(0.0, Math.Min(desiredReach, safeReach));
            if (reach < radius * 0.12)
            {
                continue;
            }

            var curl = radius * streamer.Curl;

            var coreAlpha = Math.Min(0.235, Math.Max(0.0, streamer.Alpha * alphaScale));
            var coreThickness = Math.Clamp(streamer.Thickness * weight, 0.6, 2.4);
            var haloThickness = Math.Min(2.4, Math.Max(0.6, coreThickness * 1.85));
            var haloPen = AccentPen(Math.Clamp(coreAlpha * 0.30, 0.04, 0.09), haloThickness);
            var corePen = AccentPen(coreAlpha, coreThickness);

            var previous = new Point(
                center.X + (cos * radius),
                center.Y + (sin * radius));

            for (var step = 1; step <= StreamerSegments; step++)
            {
                var t = step / (double)StreamerSegments;
                var radial = radius + (reach * t);

                // 侧向弯曲：sin(πt) 保证根部和尖端都回到月缘法线上，只在中段鼓出去。
                var tangent = curl * Math.Sin(Math.PI * t);
                var current = new Point(
                    center.X + (cos * radial) - (sin * tangent),
                    center.Y + (sin * radial) + (cos * tangent));

                if (bend > 0.001)
                {
                    // 越靠尖端弯得越多，根部钉在月缘上；方向指向光标。
                    var towardX = influence.X - current.X;
                    var towardY = influence.Y - current.Y;
                    var towardLength = Math.Sqrt((towardX * towardX) + (towardY * towardY));
                    if (towardLength > 1e-3)
                    {
                        var factor = bend * t * t / towardLength;
                        current = new Point(
                            current.X + (towardX * factor),
                            current.Y + (towardY * factor));
                    }
                }

                context.DrawLine(corePen, previous, current);
                context.DrawLine(haloPen, previous, current);
                previous = current;
            }
        }

        // ── 3. 内冕：贴着月缘的一圈亮带，钻石环方位最亮，其余随角度衰减 ─────
        const int coronaChunks = 20;
        var chunkSweep = TwoPi / coronaChunks;
        for (var chunk = 0; chunk < coronaChunks; chunk++)
        {
            var middle = (chunk + 0.5) * chunkSweep;
            if (middle < 1.30 || middle > 3.42)
            {
                continue;
            }

            var facing = 0.5 + (0.5 * Math.Cos(middle - diamondAngle));
            var chunkAlpha = 0.085 + (0.085 * facing) + (0.012 * breath);
            if (chunkAlpha <= 0.006)
            {
                continue;
            }

            DrawArc(
                context,
                AccentPen(chunkAlpha, Math.Max(0.6, (1.15 + (0.45 * facing)) * weight)),
                center,
                radius * (1.014 + (0.004 * breath)),
                middle - (chunkSweep * 0.42),
                chunkSweep * 0.84,
                6);
        }

        // ── 4. 月缘本体：宽而淡的垫底 + 细芯；月面只靠内缘两道暗弧收边 ─────
        var limbAlpha = Math.Min(0.235, 0.205 + (0.018 * breath));
        DrawArc(context, AccentPen(0.058 + (0.010 * breath), Math.Max(0.6, 3.0 * weight)), center, radius, 1.42, 1.80, LimbSegments);
        DrawArc(context, AccentPen(limbAlpha, Math.Max(0.8, 1.35 * weight)), center, radius, 1.42, 1.80, LimbSegments);

        // 内缘暗弧：把「几乎全黑」的月面与亮月缘隔开一层，不需要实色填充。
        DrawArc(context, TextPen(0.075, Math.Max(0.6, 0.9 * weight)), center, radius * 0.968, 1.38, 1.86, 34);
        DrawArc(context, TextPen(0.045, Math.Max(0.6, 0.8 * weight)), center, radius * 0.918, 1.46, 1.72, 30);

        // 月面内部三道极淡的同心弧：只在中段露出一点，像被日冕照亮的球面轮廓。
        DrawArc(context, TextPen(0.042, Math.Max(0.6, 0.75 * weight)), center, radius * 0.82, 1.55, 1.10, 20);
        DrawArc(context, TextPen(0.040, Math.Max(0.6, 0.7 * weight)), center, radius * 0.66, 1.70, 0.78, 14);

        // ── 5. 钻石环：7 秒闪烁 + 指针提亮，十字星芒，不填充 ──────────────
        var gleam = 0.115 + (0.075 * shimmer) + (0.105 * proximity);
        var gleamAlpha = Math.Min(0.24, gleam);
        var spike = Math.Max(6.0, 9.5 * scale) * (0.86 + (0.14 * shimmer) + (0.22 * proximity));
        var spikePen = AccentPen(gleamAlpha, Math.Max(0.8, 1.5 * weight));
        var shortPen = AccentPen(Math.Min(0.10, gleamAlpha * 0.45), Math.Max(0.6, 0.85 * weight));

        context.DrawLine(spikePen, new Point(diamondPoint.X - spike, diamondPoint.Y), new Point(diamondPoint.X + spike, diamondPoint.Y));
        context.DrawLine(spikePen, new Point(diamondPoint.X, diamondPoint.Y - spike), new Point(diamondPoint.X, diamondPoint.Y + spike));

        var diagonal = spike * 0.52;
        context.DrawLine(shortPen, new Point(diamondPoint.X - diagonal, diamondPoint.Y - diagonal), new Point(diamondPoint.X + diagonal, diamondPoint.Y + diagonal));
        context.DrawLine(shortPen, new Point(diamondPoint.X + diagonal, diamondPoint.Y - diagonal), new Point(diamondPoint.X - diagonal, diamondPoint.Y + diagonal));

        // 光点外两圈短弧：闪烁时像一圈被照亮的环带，而不是一个实心圆点。
        DrawArc(context, AccentPen(Math.Min(0.10, gleamAlpha * 0.42), Math.Max(0.6, 0.8 * weight)), diamondPoint, spike * 0.42, 0.0, TwoPi, 18);
        DrawArc(context, AccentPen(Math.Min(0.09, 0.05 + (0.04 * shimmer)), Math.Max(0.6, 0.7 * weight)), diamondPoint, spike * 0.66, 0.7, Math.PI * 1.3, 16);

        // 钻石环所在的那一小段月缘单独提亮：光点才有「从月缘长出来」的根。
        DrawArc(context, AccentPen(Math.Min(0.24, 0.19 + (0.05 * proximity)), Math.Max(0.9, 1.55 * weight)), center, radius, diamondAngle - 0.07, 0.14, 6);
    }

    /// <summary>
    /// 从月心沿给定方向能走多远才会闯进「画面中心安全圈」：返回射线与安全圈的交点距离。
    /// 射线不朝安全圈去时返回 <see cref="double.MaxValue"/>，调用方可以直接用视觉长度。
    /// </summary>
    private static double SafeReach(Point origin, double cos, double sin, Point anchor, double safeRadius)
    {
        var offsetX = origin.X - anchor.X;
        var offsetY = origin.Y - anchor.Y;
        var b = (offsetX * cos) + (offsetY * sin);
        var c = (offsetX * offsetX) + (offsetY * offsetY) - (safeRadius * safeRadius);
        var discriminant = (b * b) - c;
        if (!(discriminant > 0.0))
        {
            return double.MaxValue;
        }

        var enter = -b - Math.Sqrt(discriminant);
        return enter > 0.0 ? enter : double.MaxValue;
    }

    /// <summary>把圆弧拆成折线画出来；画布裁剪会自然去掉画外部分。</summary>
    private static void DrawArc(
        DrawingContext context,
        IPen pen,
        Point center,
        double radius,
        double start,
        double sweep,
        int segments)
    {
        if (segments < 1 || !(radius > 0.1) || !(Math.Abs(sweep) > 1e-4))
        {
            return;
        }

        var step = sweep / segments;
        var previous = new Point(
            center.X + (Math.Cos(start) * radius),
            center.Y + (Math.Sin(start) * radius));

        for (var i = 1; i <= segments; i++)
        {
            var angle = start + (step * i);
            var current = new Point(
                center.X + (Math.Cos(angle) * radius),
                center.Y + (Math.Sin(angle) * radius));

            context.DrawLine(pen, previous, current);
            previous = current;
        }
    }
}
