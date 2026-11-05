using System;
using Avalonia;
using Avalonia.Media;

namespace CkanUI.App.Controls.Backgrounds;

/// <summary>
/// 太空电梯：左侧行星弧从画外压进来，一根极细的碳纳米管缆绳贴着画面左四分之一升出画外；
/// 爬升器 90s 登顶，锚点挂三圈张力环，整根缆绳 53s 摆一次。
/// </summary>
/// <remarks>
/// <para>
/// 构图刻意偏置：行星弧只占左下角，缆绳压在 20% 宽度处，右侧与中央全部让给页面卡片；
/// 缆绳的累计横移在绘制前就夹进左四分之一，窗口再宽也不会伸进内容区。
/// </para>
/// <para>
/// 渲染器无状态：摆动、爬升、张力环呼吸全部由 <c>scene.Time</c> 推导；指针只读
/// <c>scene.Influence</c> / <c>scene.InfluenceStrength</c>，横向偏移把缆绳压弯最多 2.6 DIP，
/// 同时把爬升速度提到 1.4×。同一份输入永远画出同一帧。
/// </para>
/// <para>
/// 动效关闭时时间冻结在 36s（爬升四成）的定格帧，摆动停住，但指针横向压弯照旧生效——
/// 关掉动效不该连带关掉可感知的状态变化。
/// </para>
/// </remarks>
public sealed class SpaceElevatorScene : IBackgroundSceneRenderer
{
    /// <summary>2π：周期换算常量。</summary>
    private const double Tau = Math.PI * 2.0;

    /// <summary>爬升周期（秒）：从锚点爬到画外正好 90s。</summary>
    private const double ClimbPeriod = 90.0;

    /// <summary>缆绳摆动周期（秒）。</summary>
    private const double SwayPeriod = 53.0;

    /// <summary>张力环呼吸周期（秒）：取 31s，和摆动错开相位。</summary>
    private const double RingBreathPeriod = 31.0;

    /// <summary>动效关闭时的定格时刻（秒）：36s 对应爬升四成，构图里看得见爬升器。</summary>
    private const double FrozenTime = 36.0;

    /// <summary>缆绳顶端伸出画外的余量（DIP）：线头不贴边。</summary>
    private const double TetherOvershoot = 18.0;

    /// <summary>指针满强度、满横向偏移时缆绳顶端的压弯量（DIP）：规格要求 2–3 DIP。</summary>
    private const double MaxBend = 2.6;

    /// <summary>摆动在缆绳顶端的振幅（DIP）。</summary>
    private const double SwayAmplitude = 22.0;

    /// <summary>爬升器满速时的额外倍率：1 + 0.4 = 1.4×。</summary>
    private const double ClimberBoost = 0.40;

    /// <summary>缆绳弯曲的幂次：底部钉死在锚点，越往上摆幅越大。</summary>
    private const double BendPower = 1.55;

    /// <summary>缆绳所在的横向位置（画布宽度比例）：压在左四分之一里。</summary>
    private const double TetherColumn = 0.20;

    /// <summary>锚点的纵向位置（画布高度比例）：正好落在行星弧穿过缆绳的位置。</summary>
    private const double AnchorRow = 0.862;

    /// <summary>行星椭圆中心相对画布的横向位置。</summary>
    private const double LimbCenterX = 0.02;

    /// <summary>行星椭圆中心相对画布的纵向位置：压到画外，只露一圈弧。</summary>
    private const double LimbCenterY = 1.00;

    /// <summary>行星椭圆的横向半径（画布宽度比例）：右缘停在 22% 宽度处。</summary>
    private const double LimbRadiusX = 0.20;

    /// <summary>行星椭圆的纵向半径（画布高度比例）。</summary>
    private const double LimbRadiusY = 0.44;

    /// <summary>缆绳折线段数：72 段在 4K 下也看不出折角。</summary>
    private const int TetherSegments = 72;

    /// <summary>行星弧折线段数。</summary>
    private const int LimbSegments = 48;

    /// <summary>缆绳上的等距刻度数：只做尺度提示，不做文字标注。</summary>
    private const int TetherTicks = 9;

    /// <summary>行星弧上的经线刻度数。</summary>
    private const int LimbTicks = 3;

    /// <summary>锚点张力环数量。</summary>
    private const int TensionRings = 3;

    /// <summary>爬升器在底部淡入的行程比例。</summary>
    private const double ClimberFadeIn = 0.06;

    /// <summary>爬升器在顶部淡出的行程比例。</summary>
    private const double ClimberFadeOut = 0.07;

    /// <inheritdoc />
    public void Render(DrawingContext context, BackgroundSceneContext scene)
    {
        ArgumentNullException.ThrowIfNull(context);

        var width = scene.Width;
        var height = scene.Height;
        if (!double.IsFinite(width) || !double.IsFinite(height) || width < 72.0 || height < 72.0)
        {
            return;
        }

        if (!double.IsFinite(scene.Intensity) || scene.Intensity <= 0.002)
        {
            return;
        }

        var scale = double.IsFinite(scene.Scale) ? Math.Clamp(scene.Scale, 0.5, 1.9) : 1.0;
        var time = scene.MotionEnabled && double.IsFinite(scene.Time) ? scene.Time : FrozenTime;
        var strength = double.IsFinite(scene.InfluenceStrength)
            ? Math.Clamp(scene.InfluenceStrength, 0.0, 1.0)
            : 0.0;

        var baseX = width * TetherColumn;
        var baseY = height * AnchorRow;
        var span = baseY + TetherOvershoot;

        // 指针横向离开缆绳多远，就往哪边压弯多少：满偏 2.6 DIP，落在规格的 2–3 DIP 里。
        var horizontal = Math.Clamp((scene.Influence.X - baseX) / Math.Max(1.0, width * 0.35), -1.0, 1.0);
        var bend = horizontal * MaxBend * strength;
        var sway = Math.Sin(Tau * time / SwayPeriod) * SwayAmplitude * Math.Clamp(scale, 0.75, 1.35);

        // 摆动和压弯叠加后统一夹进左四分之一，锚点左侧也只留 6 DIP，任何窗口比例都不会越界。
        var lean = Math.Clamp(
            sway + bend,
            -Math.Max(0.0, baseX - 6.0),
            Math.Max(0.0, (width * 0.25) - 4.0 - baseX));

        var frame = new TetherFrame(baseX, baseY, span, lean);

        DrawLimb(context, scene, width, height, scale);
        DrawTether(context, scene, frame, scale);
        DrawClimber(context, scene, frame, scale, time, horizontal, strength);
        DrawAnchor(context, scene, frame, scale, time);
    }

    /// <summary>行星弧：左下角一圈很淡的椭圆边 + 外沿大气细线 + 三条经线刻度，只描边不填充。</summary>
    private static void DrawLimb(
        DrawingContext context,
        BackgroundSceneContext scene,
        double width,
        double height,
        double scale)
    {
        var cx = width * LimbCenterX;
        var cy = height * LimbCenterY;
        var rx = width * LimbRadiusX;
        var ry = height * LimbRadiusY;
        if (!(rx > 1.0) || !(ry > 1.0))
        {
            return;
        }

        var start = -Math.Acos(Math.Clamp((0.0 - cx) / rx, -1.0, 1.0));
        var end = Math.Asin(Math.Clamp((height - cy) / ry, -1.0, 1.0));
        if (!double.IsFinite(start) || !double.IsFinite(end) || !(end > start))
        {
            return;
        }

        var limb = Stroke(scene.AccentAt(0.185), LineWidth(scale, 1.15));
        var previous = PointOnEllipse(cx, cy, rx, ry, start);
        for (var i = 1; i <= LimbSegments; i++)
        {
            var angle = start + ((end - start) * i / LimbSegments);
            var current = PointOnEllipse(cx, cy, rx, ry, angle);
            context.DrawLine(limb, previous, current);
            previous = current;
        }

        // 大气外沿：比本体再淡一档、外扩 5 DIP，贴着弧走一圈，不做渐变填充。
        var haloRx = rx + (5.0 * scale);
        var haloRy = ry + (5.0 * scale);
        var haloStart = -Math.Acos(Math.Clamp((0.0 - cx) / haloRx, -1.0, 1.0));
        var haloEnd = Math.Asin(Math.Clamp((height - cy) / haloRy, -1.0, 1.0));
        if (double.IsFinite(haloStart) && double.IsFinite(haloEnd) && haloEnd > haloStart)
        {
            var halo = Stroke(scene.TextAt(0.045), LineWidth(scale, 0.85));
            var haloPrevious = PointOnEllipse(cx, cy, haloRx, haloRy, haloStart);
            for (var i = 1; i <= LimbSegments; i++)
            {
                var angle = haloStart + ((haloEnd - haloStart) * i / LimbSegments);
                var current = PointOnEllipse(cx, cy, haloRx, haloRy, angle);
                context.DrawLine(halo, haloPrevious, current);
                haloPrevious = current;
            }
        }

        // 经线刻度：从弧面往行星内部扎 7 DIP，读起来像测绘线而不是装饰。
        var tick = Stroke(scene.TextAt(0.06), LineWidth(scale, 0.9));
        for (var i = 0; i < LimbTicks; i++)
        {
            var angle = start + ((end - start) * (0.26 + (i * 0.20)));
            var point = PointOnEllipse(cx, cy, rx, ry, angle);
            var tangentX = -rx * Math.Sin(angle);
            var tangentY = ry * Math.Cos(angle);
            var length = Math.Sqrt((tangentX * tangentX) + (tangentY * tangentY));
            if (!(length > 0.0001))
            {
                continue;
            }

            var normalX = tangentY / length;
            var normalY = -tangentX / length;
            var depth = 7.0 * scale;
            context.DrawLine(
                tick,
                point,
                new Point(point.X - (normalX * depth), point.Y - (normalY * depth)));
        }
    }

    /// <summary>缆绳：两股极细的平行发丝 + 等距刻度，越靠近画外越淡，不让竖线硬切版面。</summary>
    private static void DrawTether(
        DrawingContext context,
        BackgroundSceneContext scene,
        TetherFrame frame,
        double scale)
    {
        var corePen = Stroke(scene.AccentAt(0.215), LineWidth(scale, 1.05));
        var midPen = Stroke(scene.AccentAt(0.185), LineWidth(scale, 1.0));
        var topPen = Stroke(scene.AccentAt(0.150), LineWidth(scale, 0.95));
        var edgePen = Stroke(scene.TextAt(0.075), LineWidth(scale, 0.8));

        // 第二股发丝向右偏 1.7 DIP：极细的缆带才有厚度，而不是一根铅笔线。
        var edgeOffset = 1.7 * scale;

        var previous = frame.PointAt(0.0);
        var previousEdge = new Point(previous.X + edgeOffset, previous.Y);
        for (var i = 1; i <= TetherSegments; i++)
        {
            var u = (double)i / TetherSegments;
            var current = frame.PointAt(u);
            var currentEdge = new Point(current.X + edgeOffset, current.Y);
            var pen = u < 0.34 ? corePen : u < 0.67 ? midPen : topPen;
            context.DrawLine(pen, previous, current);
            context.DrawLine(edgePen, previousEdge, currentEdge);
            previous = current;
            previousEdge = currentEdge;
        }

        var tickPen = Stroke(scene.TextAt(0.055), LineWidth(scale, 0.9));
        for (var i = 1; i <= TetherTicks; i++)
        {
            var u = (double)i / (TetherTicks + 1);
            var point = frame.PointAt(u);
            var half = 2.4 * scale;
            context.DrawLine(tickPen, new Point(point.X - half, point.Y), new Point(point.X + half, point.Y));
        }

        // 一条高程引出线：向右拉 10 DIP 再折一道短竖线，像总装图上的高度标注。
        var markPen = Stroke(scene.TextAt(0.045), LineWidth(scale, 0.9));
        var mark = frame.PointAt(0.58);
        var markEnd = new Point(mark.X + (10.0 * scale), mark.Y);
        context.DrawLine(markPen, mark, markEnd);
        context.DrawLine(markPen, markEnd, new Point(markEnd.X, markEnd.Y - (3.4 * scale)));
    }

    /// <summary>
    /// 爬升器：一只夹住缆绳的小车厢，从锚点爬到画外，90s 一个循环；
    /// 指针横向偏移越远推进越快，满偏 1.4×。首尾各留一段淡入淡出，换圈不闪断。
    /// </summary>
    private static void DrawClimber(
        DrawingContext context,
        BackgroundSceneContext scene,
        TetherFrame frame,
        double scale,
        double time,
        double horizontal,
        double strength)
    {
        var deflection = Math.Abs(horizontal) * strength;
        var speed = scene.MotionEnabled ? 1.0 + (ClimberBoost * deflection) : 1.0;
        var travel = Fraction(time * speed / ClimbPeriod);
        var fade = Math.Clamp(
            Math.Min(travel / ClimberFadeIn, (1.0 - travel) / ClimberFadeOut),
            0.0,
            1.0);
        if (fade < 0.002)
        {
            return;
        }

        var center = frame.PointAt(travel);
        var ahead = frame.PointAt(Math.Min(1.0, travel + 0.01));
        var behind = frame.PointAt(Math.Max(0.0, travel - 0.01));
        var dx = ahead.X - behind.X;
        var dy = ahead.Y - behind.Y;
        var length = Math.Sqrt((dx * dx) + (dy * dy));
        if (!(length > 0.0001))
        {
            return;
        }

        var upX = dx / length;
        var upY = dy / length;
        var rightX = -upY;
        var rightY = upX;

        Point Corner(double along, double across) => new(
            center.X + (upX * along) + (rightX * across),
            center.Y + (upY * along) + (rightY * across));

        var halfWidth = 6.2 * scale;
        var halfHeight = 6.8 * scale;

        var body = Stroke(scene.AccentAt(0.222 * fade), LineWidth(scale, 1.15));
        context.DrawLine(body, Corner(halfHeight, -halfWidth), Corner(halfHeight, halfWidth));
        context.DrawLine(body, Corner(halfHeight, halfWidth), Corner(-halfHeight, halfWidth));
        context.DrawLine(body, Corner(-halfHeight, halfWidth), Corner(-halfHeight, -halfWidth));
        context.DrawLine(body, Corner(-halfHeight, -halfWidth), Corner(halfHeight, -halfWidth));

        var detail = Stroke(scene.TextAt(0.12 * fade), LineWidth(scale, 0.9));
        context.DrawLine(detail, Corner(1.8 * scale, -halfWidth * 0.8), Corner(1.8 * scale, halfWidth * 0.8));
        context.DrawLine(
            detail,
            Corner(-halfHeight, -halfWidth * 0.50),
            Corner(-halfHeight - (4.5 * scale), -halfWidth * 1.15));
        context.DrawLine(
            detail,
            Corner(-halfHeight, halfWidth * 0.50),
            Corner(-halfHeight - (4.5 * scale), halfWidth * 1.15));

        // 两只夹持滚轮：贴着缆绳两侧，直径 3.2 DIP，给"夹着爬"一个直接证据。
        var roller = Stroke(scene.AccentAt(0.17 * fade), LineWidth(scale, 0.9));
        var rollerRadius = 1.6 * scale;
        context.DrawEllipse(null, roller, Corner(0.0, halfWidth + rollerRadius), rollerRadius, rollerRadius);
        context.DrawEllipse(null, roller, Corner(0.0, -(halfWidth + rollerRadius)), rollerRadius, rollerRadius);
    }

    /// <summary>锚点：三圈透视张力环（31s 错相呼吸）、中心毂、三根往左下扎的地锚索。</summary>
    private static void DrawAnchor(
        DrawingContext context,
        BackgroundSceneContext scene,
        TetherFrame frame,
        double scale,
        double time)
    {
        var center = new Point(frame.BaseX, frame.BaseY);
        for (var i = 0; i < TensionRings; i++)
        {
            var phase = (Tau * time / RingBreathPeriod) + (i * 0.85);
            var breathe = 1.0 + (0.035 * Math.Sin(phase));
            var radius = (14.0 + (i * 12.5)) * scale * breathe;
            context.DrawEllipse(
                null,
                Stroke(scene.AccentAt(0.19 - (i * 0.040)), LineWidth(scale, 1.0)),
                center,
                radius,
                radius * 0.30);
        }

        var hub = Stroke(scene.AccentAt(0.20), LineWidth(scale, 1.0));
        var hubRadius = 2.6 * scale;
        context.DrawEllipse(null, hub, center, hubRadius, hubRadius);

        // 三根地锚索：从环沿往左下扎进行星弧，越远越淡。
        var guy = Stroke(scene.TextAt(0.065), LineWidth(scale, 0.9));
        for (var i = 0; i < 3; i++)
        {
            var angle = Math.PI * (0.60 + (i * 0.12));
            var inner = new Point(
                center.X + (Math.Cos(angle) * 11.0 * scale),
                center.Y + (Math.Sin(angle) * 11.0 * scale * 0.30));
            var outer = new Point(
                center.X + (Math.Cos(angle) * 25.0 * scale),
                center.Y + (Math.Sin(angle) * 25.0 * scale * 0.30) + (10.0 * scale));
            context.DrawLine(guy, inner, outer);
        }
    }

    /// <summary>椭圆参数角上的点。</summary>
    private static Point PointOnEllipse(double cx, double cy, double rx, double ry, double angle) =>
        new(cx + (rx * Math.Cos(angle)), cy + (ry * Math.Sin(angle)));

    /// <summary>取小数部分：爬升相位用，负数与极大值都能落回 0–1。</summary>
    private static double Fraction(double value)
    {
        if (!double.IsFinite(value))
        {
            return 0.0;
        }

        var fraction = value - Math.Floor(value);
        return fraction >= 0.0 ? fraction : 0.0;
    }

    /// <summary>线宽随缩放走，夹在 0.6–2.4 DIP：4K 下不糊，小窗里也不消失。</summary>
    private static double LineWidth(double scale, double dip) => Math.Clamp(dip * scale, 0.6, 2.4);

    /// <summary>一支细线画笔：透明度已经由 <see cref="BackgroundSceneContext.AccentAt"/> 之类折进颜色里。</summary>
    private static Pen Stroke(Color color, double thickness) => new(new SolidColorBrush(color, 1.0), thickness);

    /// <summary>
    /// 缆绳的局部坐标：以锚点为原点、向上为 +u，<see cref="Lean"/> 是顶端累计横移（DIP）。
    /// 它只是每帧算出来的只读快照，不留在任何字段里——渲染器仍然无状态。
    /// </summary>
    private readonly record struct TetherFrame(double BaseX, double BaseY, double Span, double Lean)
    {
        /// <summary>沿缆绳取点：0 = 锚点，1 = 画外顶端。</summary>
        public Point PointAt(double u)
        {
            var along = Math.Clamp(u, 0.0, 1.0);
            return new Point(
                BaseX + (Lean * Math.Pow(along, BendPower)),
                BaseY - (Span * along));
        }
    }
}
