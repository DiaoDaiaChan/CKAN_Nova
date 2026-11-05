using System;
using Avalonia;
using Avalonia.Media;

namespace CkanUI.App.Controls.Backgrounds;

/// <summary>
/// 行星环：右下角一段行星弧线，三条极细的环线斜穿画面，环带里留一道暗缝，两颗牧羊卫星缓慢绕行。
/// </summary>
/// <remarks>
/// <para>
/// 无状态：进动、卫星方位与明暗全部由 <see cref="BackgroundSceneContext.Time"/> 推导；光标只通过
/// <see cref="BackgroundSceneContext.Influence"/> 的横向位置改变椭圆度，同一份输入永远画出同一帧。
/// 动效关闭时时间项冻结为零，构图静止，但光标造成的椭圆度变化照常生效。
/// </para>
/// <para>
/// 构图压在右下角：行星圆心在 (1.03W, 0.88H)，半径 0.34 短边，画面只看见左上象限的一条弧。
/// 环面旋转约 18° 后，只画近侧单弧——从画面右下角之外起笔，斜穿行星正面，在环面背面收笔；
/// 末段分三档渐隐，避免线头突兀，也不把环画成闭环的土星插画。
/// 三条环线位于 1.90R / 2.00R / 2.30R，中环与外环之间的宽空带就是那道暗缝；
/// 环线亮度 0.11–0.21（再乘主题强度），行星弧 0.04–0.09，全部是 0.6–2.4 DIP 的细描边，没有填充。
/// 单帧固定 3×3 段环线 + 2 圈行星弧 + 2 颗卫星，共 13 个几何体，不随窗口尺寸膨胀，也不使用 Random。
/// </para>
/// </remarks>
public sealed class RingPlaneScene : IBackgroundSceneRenderer
{
    /// <summary>整圈弧度。</summary>
    private const double TwoPi = Math.PI * 2.0;

    /// <summary>环面进动周期（秒）：90 秒一次完整的慢摆，肉眼几乎察觉不到拍点。</summary>
    private const double PrecessionSeconds = 90.0;

    /// <summary>内侧牧羊卫星公转周期（秒）。</summary>
    private const double InnerMoonSeconds = 37.0;

    /// <summary>外侧牧羊卫星公转周期（秒），与内侧互质，避免同拍。</summary>
    private const double OuterMoonSeconds = 53.0;

    /// <summary>行星圆心横向位置（相对宽度）：压到右边缘之外一点。</summary>
    private const double PlanetCenterXRatio = 1.03;

    /// <summary>行星圆心纵向位置（相对高度）：压到右下角，只露一条弧。</summary>
    private const double PlanetCenterYRatio = 0.88;

    /// <summary>行星半径（相对短边）：占短边三成四，让弧线从右边缘落到下边缘。</summary>
    private const double PlanetRadiusRatio = 0.34;

    /// <summary>环面倾角（弧度）：约 18°，让环带斜穿而不是水平切一刀。</summary>
    private const double RingTilt = 0.32;

    /// <summary>进动摆幅（弧度）：约 ±2.9°，90 秒走完一个来回。</summary>
    private const double PrecessionSwing = 0.05;

    /// <summary>椭圆度基准：光标在屏幕中央时的环面扁平程度。</summary>
    private const double EllipticityBase = 0.22;

    /// <summary>椭圆度摆幅：横向指针把 0.18–0.26 铺满。</summary>
    private const double EllipticitySwing = 0.04;

    /// <summary>内环半径（相对行星半径）。</summary>
    private const double InnerRingRatio = 1.90;

    /// <summary>中环半径：与内环靠得很近，构成一对细线。</summary>
    private const double MiddleRingRatio = 2.00;

    /// <summary>外环半径：与中环之间留出 0.30R 的宽空带，即暗缝。</summary>
    private const double OuterRingRatio = 2.30;

    /// <summary>内侧卫星所在半径（暗缝中央）。</summary>
    private const double InnerMoonRatio = 2.15;

    /// <summary>外侧卫星所在半径（紧贴外环之外）。</summary>
    private const double OuterMoonRatio = 2.36;

    /// <summary>内侧卫星初始方位（弧度），常数即可，不需要随机。</summary>
    private const double InnerMoonPhase = 2.15;

    /// <summary>外侧卫星初始方位（弧度）。</summary>
    private const double OuterMoonPhase = 2.40;

    /// <summary>近侧弧的起点：略低于 0，确保线头落在画面右下角之外。</summary>
    private const double ArcStart = -0.22;

    /// <summary>渐隐第一档的起点：离背面收笔约 0.36 弧度。</summary>
    private const double TaperStart = 2.78;

    /// <summary>渐隐第二档的起点：最后 0.18 弧度只留一道余痕。</summary>
    private const double TaperMid = 2.96;

    /// <summary>主体弧的折线段数：96 段在 4K 下也看不出棱角。</summary>
    private const int ArcSegments = 96;

    /// <inheritdoc />
    public void Render(DrawingContext context, BackgroundSceneContext scene)
    {
        ArgumentNullException.ThrowIfNull(context);

        var width = scene.Width;
        var height = scene.Height;
        if (!double.IsFinite(width) || !double.IsFinite(height) || width < 2 || height < 2 || scene.Intensity <= 0.001)
        {
            return;
        }

        var minSide = Math.Min(width, height);
        var planetRadius = minSide * PlanetRadiusRatio;
        if (!double.IsFinite(planetRadius) || planetRadius < 12)
        {
            return;
        }

        // 线宽跟着窗口长：小窗不粗、高分屏不细，最终夹在 0.6–2.4 DIP。
        var weight = double.IsFinite(scene.Scale) ? Math.Clamp(scene.Scale, 0.55, 1.45) : 1.0;
        double Dip(double value) => Math.Clamp(value * weight, 0.6, 2.4);

        // 动效关闭时冻结时间项：进动和卫星停住，但下面的椭圆度仍读取光标，指针响应不丢。
        var time = scene.MotionEnabled && double.IsFinite(scene.Time) && scene.Time > 0 ? scene.Time : 0d;

        var strength = double.IsFinite(scene.InfluenceStrength) ? Math.Clamp(scene.InfluenceStrength, 0, 1) : 0;
        var influence = scene.Influence;
        if (!double.IsFinite(influence.X) || !double.IsFinite(influence.Y))
        {
            influence = new Point(width * 0.5, height * 0.5);
            strength = 0;
        }

        // 光标横向位置映射到 0.18–0.26：左端最扁、右端最鼓；没有光标时回到 0.22 的静态基准。
        var pointerX = Math.Clamp(influence.X / width, 0, 1);
        var ellipticity = EllipticityBase + ((pointerX - 0.5) * 2.0 * EllipticitySwing * strength);

        // 环面进动：90 秒一个来回，只改变倾角，不改半径，避免构图整体晃动。
        var precession = Math.Sin(time * (TwoPi / PrecessionSeconds)) * PrecessionSwing;
        var rotation = RingTilt + precession;

        var center = new Point(width * PlanetCenterXRatio, height * PlanetCenterYRatio);
        if (!double.IsFinite(center.X) || !double.IsFinite(center.Y))
        {
            return;
        }

        using var clip = context.PushClip(new Rect(0, 0, width, height));

        Pen TextPen(double alpha, double thickness) => new(new SolidColorBrush(scene.TextAt(alpha)), thickness);
        Pen AccentPen(double alpha, double thickness) => new(new SolidColorBrush(scene.AccentAt(alpha)), thickness);

        Point OnEllipse(double rx, double ry, double angle)
        {
            var cos = Math.Cos(angle);
            var sin = Math.Sin(angle);
            var cosRotation = Math.Cos(rotation);
            var sinRotation = Math.Sin(rotation);
            var localX = rx * cos;
            var localY = ry * sin;
            return new Point(
                center.X + (localX * cosRotation) - (localY * sinRotation),
                center.Y + (localX * sinRotation) + (localY * cosRotation));
        }

        // 一段弧的折线；端点都在画布外或已渐隐，因此不需要遮挡裁剪。
        void DrawArc(double rx, double ry, double start, double end, int segments, Pen pen)
        {
            var geometry = new StreamGeometry();
            using (var ctx = geometry.Open())
            {
                ctx.BeginFigure(OnEllipse(rx, ry, start), false);
                for (var i = 1; i <= segments; i++)
                {
                    ctx.LineTo(OnEllipse(rx, ry, start + ((end - start) * i / segments)));
                }

                ctx.EndFigure(false);
            }

            context.DrawGeometry(null, pen, geometry);
        }

        // ── 1. 行星弧：外缘 + 一圈很淡的内缘，只留线稿，不做实心圆面 ───────────
        context.DrawEllipse(null, TextPen(0.086, Dip(1.05)), center, planetRadius, planetRadius);
        context.DrawEllipse(null, TextPen(0.042, Dip(0.75)), center, planetRadius * 0.982, planetRadius * 0.982);

        // ── 2. 三条环线：只画近侧单弧，末段三档渐隐 ─────────────────────────
        // 第 2、3 条之间 0.30R 的空白就是暗缝，宽度明显大于第 1、2 条之间的 0.10R。
        DrawRing(InnerRingRatio, 0.158, Dip(0.95));
        DrawRing(MiddleRingRatio, 0.118, Dip(0.85));
        DrawRing(OuterRingRatio, 0.205, Dip(1.15));

        // ── 3. 两颗牧羊卫星：各自贴着环带公转，转到行星背面时压暗 ─────────────
        DrawMoon(InnerMoonRatio, InnerMoonSeconds, InnerMoonPhase);
        DrawMoon(OuterMoonRatio, OuterMoonSeconds, OuterMoonPhase);

        void DrawRing(double ratio, double alpha, double thickness)
        {
            var rx = planetRadius * ratio;
            var ry = rx * ellipticity;

            // 近侧单弧：从画面右下角之外起笔，到环面背面收笔。
            DrawArc(rx, ry, ArcStart, TaperStart, ArcSegments, AccentPen(alpha, thickness));

            // 背面末梢分两档压暗，让环线像没入行星的阴影，而不是被剪刀剪断。
            DrawArc(rx, ry, TaperStart, TaperMid, 10, AccentPen(alpha * 0.55, thickness));
            DrawArc(rx, ry, TaperMid, Math.PI, 10, AccentPen(alpha * 0.22, thickness));
        }

        void DrawMoon(double ratio, double period, double phase)
        {
            var rx = planetRadius * ratio;
            var ry = rx * ellipticity;
            var angle = phase + (time * (TwoPi / period));
            var point = OnEllipse(rx, ry, angle);

            // 行星背面（localY < 0）的卫星压暗到一半；落在圆面里的直接不画。
            var behind = Math.Sin(angle) < 0;
            if (behind && Square(point.X - center.X) + Square(point.Y - center.Y) < Square(planetRadius * 0.985))
            {
                return;
            }

            var dim = behind ? 0.5 : 1.0;
            var radius = Math.Clamp(2.0 * weight, 1.3, 2.9);

            // 只描边，不做实心点；外面再套一圈几乎看不见的晕，让卫星在细线里能被认出来。
            context.DrawEllipse(null, AccentPen(0.22 * dim, Dip(0.8)), point, radius, radius);
            context.DrawEllipse(null, AccentPen(0.05 * dim, Dip(0.6)), point, radius * 2.4, radius * 2.4);
        }
    }

    /// <summary>平方，省掉重复的乘法括号。</summary>
    private static double Square(double value) => value * value;
}
