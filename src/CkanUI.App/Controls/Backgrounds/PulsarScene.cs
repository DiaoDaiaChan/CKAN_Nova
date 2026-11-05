using System;
using Avalonia;
using Avalonia.Media;

namespace CkanUI.App.Controls.Backgrounds;

/// <summary>
/// 脉冲星：右上角一颗极小的中子星，两束反向射电锥每 3.7 秒扫过，三圈计时环以 11 秒节拍明灭。
/// </summary>
/// <remarks>
/// <para>
/// 无状态：扫掠相位只由 <see cref="BackgroundSceneContext.Time"/> 推导，光标只通过
/// <see cref="BackgroundSceneContext.Influence"/> 与 <see cref="BackgroundSceneContext.InfluenceStrength"/>
/// 给相位加一个最多 12° 的扭矩。动效关闭时时间项冻结，射电锥停在固定相位，指针扭矩照常生效。
/// </para>
/// <para>
/// 亮度封顶：射电锥不超过 0.05，计时环 0.085–0.15，星核不超过 0.24，全部再乘主题强度；
/// 只画描边，不做填充。星体位于右上角 (0.905W, 0.135H)，半径不足短边的 0.5%，
/// 页面卡片中心区域只被最淡的锥面边缘掠过。
/// </para>
/// <para>
/// 低成本：不使用随机数、不查资源、不建逐帧数组；每帧约 40 次 DrawLine/DrawGeometry，
/// 与窗口尺寸无关。
/// </para>
/// </remarks>
public sealed class PulsarScene : IBackgroundSceneRenderer
{
    /// <summary>整圈弧度。</summary>
    private const double TwoPi = Math.PI * 2.0;

    /// <summary>中子星横向位置（相对宽度）：贴在右上角，把画面中心留给页面卡片。</summary>
    private const double StarXRatio = 0.905;

    /// <summary>中子星纵向位置（相对高度）。</summary>
    private const double StarYRatio = 0.135;

    /// <summary>射电锥扫掠周期（秒）：3.7 秒一圈，两束反向锥交替扫过。</summary>
    private const double SpinSeconds = 3.7;

    /// <summary>可见亮度包络周期（秒）：与 3.7 秒互质，避开机械的整拍闪烁。</summary>
    private const double PulseSeconds = 4.8;

    /// <summary>三圈计时环的明灭周期（秒）：11 秒，三圈相位各差 1/3 周期。</summary>
    private const double RingFadeSeconds = 11.0;

    /// <summary>指针能推动的扫掠相位上限：12°。</summary>
    private const double MaxPhaseNudgeRadians = 12.0 * Math.PI / 180.0;

    /// <summary>单束射电锥的半张角：2.6°，保证扫过时窄而淡。</summary>
    private const double BeamHalfAngle = 2.6 * Math.PI / 180.0;

    /// <summary>射电锥边缘的亮度上限：0.05，再乘亮度包络只会更暗。</summary>
    private const double BeamMaxAlpha = 0.05;

    /// <summary>计时环的折线段数：4K 下也看不出棱角。</summary>
    private const int RingSegments = 72;

    /// <inheritdoc />
    public void Render(DrawingContext context, BackgroundSceneContext scene)
    {
        ArgumentNullException.ThrowIfNull(context);

        var width = scene.Width;
        var height = scene.Height;
        if (!double.IsFinite(width) || !double.IsFinite(height) || width < 2 || height < 2 || !(scene.Intensity > 0.001))
        {
            return;
        }

        var minSide = Math.Min(width, height);
        var starRadius = Math.Clamp(minSide * 0.0042, 1.2, 5.2);
        if (!double.IsFinite(starRadius) || starRadius < 1.0 || minSide < 8)
        {
            return;
        }

        // 线宽跟着窗口长：900×600 到 4K 保持同一视觉密度，高分屏上不会细成一根头发。
        var weight = double.IsFinite(scene.Scale) ? Math.Clamp(scene.Scale, 0.55, 1.45) : 1.0;

        // 动效关闭时把时间项冻结成零：射电锥停在固定相位，指针扭矩仍由 Influence/InfluenceStrength 推导。
        var time = scene.MotionEnabled && double.IsFinite(scene.Time) ? scene.Time : 0d;

        var strength = double.IsFinite(scene.InfluenceStrength) ? Math.Clamp(scene.InfluenceStrength, 0, 1) : 0;
        var influence = scene.Influence;
        if (!double.IsFinite(influence.X) || !double.IsFinite(influence.Y))
        {
            influence = new Point(width * 0.5, height * 0.5);
            strength = 0;
        }

        var star = new Point(width * StarXRatio, height * StarYRatio);
        var spin = time * (TwoPi / SpinSeconds);

        // 4.8 秒亮度包络：扫掠几何是 3.7 秒，但肉眼看到的一亮一暗落在 4.8 秒，两拍互质，不会像秒针。
        var pulse = 0.74 + (0.26 * Math.Sin((time * (TwoPi / PulseSeconds)) + 1.15));

        // 指针扭矩：相位偏移 = sin(指针方位 − 扫掠方位) × 12° × 强度，连续变化，没有跳变。
        var pointerAngle = Math.Atan2(influence.Y - star.Y, influence.X - star.X);
        var phaseNudge = MaxPhaseNudgeRadians * strength * Math.Sin(pointerAngle - spin);
        var azimuth = spin + phaseNudge;

        using var clip = context.PushClip(new Rect(0, 0, width, height));

        // 线宽统一夹在 0.6–2.4 DIP：高分屏不糊、小窗口不消失，也不会出现一条粗黑边。
        double Thickness(double value) => double.IsFinite(value) ? Math.Clamp(value, 0.6, 2.4) : 1.0;

        Pen TextPen(double alpha, double thickness) =>
            new(new SolidColorBrush(scene.TextAt(alpha)), Thickness(thickness));

        Pen AccentPen(double alpha, double thickness) =>
            new(new SolidColorBrush(scene.AccentAt(alpha)), Thickness(thickness));

        Point OnCircle(double radius, double angle) =>
            new(star.X + (Math.Cos(angle) * radius), star.Y + (Math.Sin(angle) * radius));

        void DrawArc(double radius, double start, double sweep, int segments, Pen pen)
        {
            var geometry = new StreamGeometry();
            using (var ctx = geometry.Open())
            {
                ctx.BeginFigure(OnCircle(radius, start), false);
                for (var i = 1; i <= segments; i++)
                {
                    ctx.LineTo(OnCircle(radius, start + (sweep * i / segments)));
                }

                ctx.EndFigure(false);
            }

            context.DrawGeometry(null, pen, geometry);
        }

        // ── 1. 射电锥：两束反向锥，各由两条 3 段折线描边，越远越淡；中轴与波前刻度再淡一档 ──
        var beamLength = Math.Sqrt((width * width) + (height * height)) * 1.05;
        if (!double.IsFinite(beamLength) || beamLength < 1.0)
        {
            beamLength = minSide;
        }

        void BeamEdge(double angle, double baseAlpha)
        {
            var cos = Math.Cos(angle);
            var sin = Math.Sin(angle);
            var previous = star;
            for (var i = 1; i <= 3; i++)
            {
                var t = i / 3.0;
                var next = new Point(star.X + (cos * beamLength * t), star.Y + (sin * beamLength * t));
                var fadeOut = 1.0 - (0.30 * (i - 1) / 2.0);
                var thickness = (1.0 - (0.15 * (i - 1))) * weight;
                context.DrawLine(AccentPen(baseAlpha * fadeOut * pulse, thickness), previous, next);
                previous = next;
            }
        }

        for (var beam = 0; beam < 2; beam++)
        {
            var axis = azimuth + (beam * Math.PI);
            BeamEdge(axis - BeamHalfAngle, BeamMaxAlpha);
            BeamEdge(axis + BeamHalfAngle, BeamMaxAlpha * 0.78);

            // 中轴：极淡，只提示扫掠方向，不参与构图。
            var midLength = beamLength * 0.55;
            context.DrawLine(
                AccentPen(0.016 * pulse, 0.8 * weight),
                star,
                new Point(star.X + (Math.Cos(axis) * midLength), star.Y + (Math.Sin(axis) * midLength)));

            // 两道波前刻度：把锥体切成“射电脉冲”，宽度由锥角决定，不会变成第二根光柱。
            var normalX = -Math.Sin(axis);
            var normalY = Math.Cos(axis);
            for (var k = 0; k < 2; k++)
            {
                var distance = minSide * (0.16 + (0.17 * k));
                var halfWidth = Math.Tan(BeamHalfAngle) * distance;
                var center = new Point(star.X + (Math.Cos(axis) * distance), star.Y + (Math.Sin(axis) * distance));
                context.DrawLine(
                    AccentPen((0.020 - (0.005 * k)) * pulse, 0.8 * weight),
                    new Point(center.X - (normalX * halfWidth), center.Y - (normalY * halfWidth)),
                    new Point(center.X + (normalX * halfWidth), center.Y + (normalY * halfWidth)));
            }
        }

        // ── 2. 计时环：三圈 11 秒明灭，相位各差 1/3 周期；半径只有短边的 3.0%–6.7% ──
        var ringBase = minSide * 0.030;
        var ringCycle = time * (TwoPi / RingFadeSeconds);
        for (var i = 0; i < 3; i++)
        {
            var radius = ringBase * (1.0 + (0.62 * i));
            var phase = ringCycle + (i * TwoPi / 3.0);
            var fade = 0.55 + (0.45 * Math.Sin(phase));
            var breath = 1.0 + (0.008 * Math.Cos(phase));
            var peak = i switch
            {
                0 => 0.150,
                1 => 0.115,
                _ => 0.085
            };
            var alpha = peak * fade;
            var start = -1.35 + (i * 0.62) + (0.04 * Math.Sin(phase * 0.5));
            var gap = 0.30 + (0.04 * i);
            var sweep = TwoPi - gap;
            var livedRadius = radius * breath;

            DrawArc(livedRadius, start, sweep, RingSegments, TextPen(alpha * 0.30, 2.6 * weight));
            DrawArc(livedRadius, start, sweep, RingSegments, TextPen(alpha, 0.95 * weight));

            // 缺口两端各一枚短刻度：像仪表游标，而不是装饰圆环。
            var tickInner = livedRadius + 1.0;
            var tickOuter = tickInner + Math.Max(2.0, starRadius * 1.2);
            var tickPen = TextPen(alpha * 0.75, 0.9 * weight);
            context.DrawLine(tickPen, OnCircle(tickInner, start), OnCircle(tickOuter, start));
            context.DrawLine(tickPen, OnCircle(tickInner, start + sweep), OnCircle(tickOuter, start + sweep));
        }

        // 指针在计时环上的落点：一小段短弧，强度为零时完全不画。
        if (strength > 0.01)
        {
            DrawArc(
                ringBase * 2.24,
                pointerAngle - 0.16,
                0.32,
                12,
                AccentPen(0.10 * strength, 1.5 * weight));
        }

        // ── 3. 中子星：全部描边，星核最亮也只有 0.24；十字刻度与沿轴闪焰提示自转 ──
        context.DrawEllipse(null, TextPen(0.045, 0.9 * weight), star, starRadius * 3.4, starRadius * 3.4);
        context.DrawEllipse(null, TextPen(0.070, 0.9 * weight), star, starRadius * 1.95, starRadius * 1.95);
        context.DrawEllipse(null, AccentPen(0.200 * pulse, 1.15 * weight), star, starRadius, starRadius);
        context.DrawEllipse(null, TextPen(0.240, 0.9 * weight), star, starRadius * 0.42, starRadius * 0.42);

        for (var i = 0; i < 4; i++)
        {
            var angle = (Math.PI / 4.0) + (i * Math.PI / 2.0) + (phaseNudge * 0.5);
            var inner = starRadius * 1.25;
            var outer = starRadius * (1.85 + (0.18 * (i % 2)));
            context.DrawLine(TextPen(0.10, 0.8 * weight), OnCircle(inner, angle), OnCircle(outer, angle));
        }

        context.DrawLine(
            AccentPen(0.180 * pulse, 1.0 * weight),
            OnCircle(starRadius * 1.1, azimuth),
            OnCircle(starRadius * 2.8, azimuth));
        context.DrawLine(
            AccentPen(0.120 * pulse, 0.9 * weight),
            OnCircle(starRadius * 1.1, azimuth + Math.PI),
            OnCircle(starRadius * 2.8, azimuth + Math.PI));
    }
}
