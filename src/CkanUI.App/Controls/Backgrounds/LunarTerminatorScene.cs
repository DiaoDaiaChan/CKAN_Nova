using System;
using Avalonia;
using Avalonia.Media;

namespace CkanUI.App.Controls.Backgrounds;

/// <summary>月面晨昏线：月缘、环形山等高线与地球新月。</summary>
/// <remarks>
/// <para>
/// 构图是纪念碑式的：月球半径取画布短边的 0.72，圆心钉在画布外右下角，画面里只留左侧那一段月缘；
/// 月面内部用断续等高线 + 环形山的同心弧与坡线暗示球面，晨昏线是月缘内侧一条会缓慢呼吸的亮带；
/// 左上角一枚很小的地球新月（细弧 + 极淡暗面轮廓）压住对角，整幅画因此不倒向一边。
/// </para>
/// <para>
/// 全是线，没有大面积实色：最亮的是月缘本体（0.205 × Intensity），晨昏线核心 0.10–0.22，
/// 环形山受光侧封顶 0.235，辅助等高线一律不超过 0.078。只透过页面卡片缝隙看得到，
/// 是一层灰蓝色的纸面浮雕，不会把正文压糊。
/// </para>
/// <para>
/// 无状态：天平动、晨昏线亮带、地球呼吸全部由 <c>scene.Time</c> 推导；光标提亮只读
/// <c>scene.Pointer</c> / <c>scene.InfluenceStrength</c>，不缓存任何帧间数据。动效关闭时时间钉在 0，
/// 画面退成静态构图，但光标附近环形山的提亮与测量弧照常出现——<see cref="BackgroundSceneCanvas"/>
/// 在动效关闭时直接把强度给满，这里只负责把它画出来。
/// </para>
/// <para>
/// 月缘、晨昏线、每条等高线的可见角度区间都是「圆 ∩ 画布矩形」解析求交算出来的，只画落在画布里的
/// 那一段折线：900×600 与 3840×2160 都不会画出界，也不会在 4K 上露出折线棱角。
/// </para>
/// </remarks>
public sealed class LunarTerminatorScene : IBackgroundSceneRenderer
{
    private const double TwoPi = Math.PI * 2d;

    private const double DegToRad = Math.PI / 180d;

    /// <summary>月缘半径与画布短边之比：够大才像一颗星球，而不是画了个圆。</summary>
    private const double MoonRadiusRatio = 0.72d;

    /// <summary>月心越出右边缘的距离（按半径的比例）：月心永远落在画布外。</summary>
    private const double MoonCenterRightBleed = 0.22d;

    /// <summary>月心高度（占画布高度的比例）：略低于中线，月缘压住右下角。</summary>
    private const double MoonCenterVertical = 0.56d;

    /// <summary>晨昏线所在半径（按月缘半径的比例）：与月缘之间夹出一条受光带。</summary>
    private const double TerminatorRatio = 0.815d;

    /// <summary>天平动水平振幅（DIP × Scale）。</summary>
    private const double LibrationAmplitudeX = 3.9d;

    /// <summary>天平动垂直振幅（DIP × Scale）。</summary>
    private const double LibrationAmplitudeY = 2.3d;

    /// <summary>天平动水平周期（秒），落在 40–70 秒区间。</summary>
    private const double LibrationPeriodX = 57d;

    /// <summary>天平动垂直周期（秒）：与水平错开，肉眼看不出循环。</summary>
    private const double LibrationPeriodY = 67d;

    /// <summary>晨昏线亮带沿弧线推移的周期（秒）。</summary>
    private const double TerminatorPeriod = 41d;

    /// <summary>地球新月的呼吸周期（秒）。</summary>
    private const double EarthPeriod = 46d;

    /// <summary>
    /// 月面等高线：半径比例、基准透明度、每段实线的段数与空隙段数。
    /// 受光侧（0.972 / 0.930 / 0.884）三条更密更实，夜面（0.762 / 0.702）更稀更淡——
    /// 明暗过渡靠线条密度暗示，不靠填充。
    /// </summary>
    private static readonly (double Ratio, double Alpha, int On, int Off)[] SurfaceContours =
    [
        (0.972d, 0.076d, 5, 2),
        (0.930d, 0.058d, 4, 3),
        (0.884d, 0.047d, 3, 4),
        (0.762d, 0.044d, 4, 4),
        (0.702d, 0.040d, 3, 5)
    ];

    /// <summary>
    /// 七个环形山：极角（度）、半径比例、坑半径（占月缘半径的比例）、同心弧层数、坡线数量。
    /// 极角集中在月缘中段，越靠近晨昏线（0.815）层数与坡线越多；最里面两个落在夜面，只用两层淡线。
    /// </summary>
    private static readonly (double Theta, double Rho, double Size, int Rings, int Ticks)[] Craters =
    [
        (155d, 0.930d, 0.050d, 2, 7),
        (168d, 0.878d, 0.038d, 3, 9),
        (181d, 0.845d, 0.066d, 3, 11),
        (196d, 0.900d, 0.044d, 2, 8),
        (209d, 0.862d, 0.056d, 3, 10),
        (190d, 0.752d, 0.060d, 2, 8),
        (204d, 0.706d, 0.046d, 2, 6)
    ];

    /// <inheritdoc />
    public void Render(DrawingContext context, BackgroundSceneContext scene)
    {
        ArgumentNullException.ThrowIfNull(context);

        var width = scene.Width;
        var height = scene.Height;
        if (!(width > 64d) || !(height > 64d) || !double.IsFinite(width) || !double.IsFinite(height))
        {
            return;
        }

        var intensity = double.IsFinite(scene.Intensity) ? Math.Clamp(scene.Intensity, 0d, 1d) : 1d;
        if (intensity <= 0.01d)
        {
            return;
        }

        var scale = double.IsFinite(scene.Scale) ? Math.Clamp(scene.Scale, 0.5d, 1.9d) : 1d;

        // 动效关掉就把时间钉死在这一帧：不能再靠时间漂，否则「关闭动效」等于没关。
        var time = scene.MotionEnabled && double.IsFinite(scene.Time) ? Math.Max(0d, scene.Time) : 0d;

        var radius = Math.Min(width, height) * MoonRadiusRatio;
        if (!(radius > 8d))
        {
            return;
        }

        Pen Stroke(double alpha, double thickness) => new(new SolidColorBrush(scene.AccentAt(alpha)), thickness);

        // 天平动作用在月心上：整段月缘一起缓慢平移，月面本身不旋转。
        var moon = new Point(
            width + (radius * MoonCenterRightBleed) + (Math.Sin(time * TwoPi / LibrationPeriodX) * LibrationAmplitudeX * scale),
            (height * MoonCenterVertical) + (Math.Sin((time * TwoPi / LibrationPeriodY) + 0.9d) * LibrationAmplitudeY * scale));

        var rimStart = ArcStart(moon, radius, width, height);
        var rimEnd = ArcEnd(moon, radius, width, height);
        if (!(rimEnd - rimStart > 0.03d))
        {
            // 极端窄窗下月缘整段落在画布外，这一帧没有可画的东西。
            return;
        }

        // ---- 光标：落在月面上给满强度，离开月缘 0.28R 之外淡出 ----
        var pointer = scene.Pointer;
        var strength = double.IsFinite(scene.InfluenceStrength) ? Clamp01(scene.InfluenceStrength) : 0d;
        var hasPointer = false;
        var pointerX = 0d;
        var pointerY = 0d;
        var pointerTheta = 0d;
        var pointerRho = double.MaxValue;
        var influence = 0d;

        if (pointer is { } p && strength > 0.001d && double.IsFinite(p.X) && double.IsFinite(p.Y))
        {
            var dx = p.X - moon.X;
            var dy = p.Y - moon.Y;
            var distance = Math.Sqrt((dx * dx) + (dy * dy));
            if (distance > 0.5d)
            {
                hasPointer = true;
                pointerX = p.X;
                pointerY = p.Y;
                pointerTheta = Math.Atan2(dy, dx);
                pointerRho = distance / radius;
                influence = pointerRho <= 1d
                    ? strength
                    : strength * Clamp01(1d - ((pointerRho - 1d) / 0.28d));
            }
        }

        // ---- 晨昏线：位置随呼吸微移，亮度沿弧线推移；光标把它推开 2–6 DIP ----
        var terminatorRatio = TerminatorRatio + (Math.Sin(time * TwoPi / 47d) * 0.011d);
        var push = hasPointer && influence > 0.001d
            ? Math.Clamp(4.6d * scale, 2d, 6d) * influence * (pointerRho >= terminatorRatio ? 1d : -1d)
            : 0d;
        var terminatorRadius = (radius * terminatorRatio) + push;

        // 晨昏线圆心相对月心偏一点：它不是月缘的同心圆，才像明暗交界而不是又一圈年轮。
        var terminatorCenter = new Point(moon.X + (radius * 0.055d), moon.Y + (radius * 0.028d));

        // ---- 1. 月面等高线（最先画，最淡） ----
        foreach (var contour in SurfaceContours)
        {
            var contourRadius = radius * contour.Ratio;
            var start = ArcStart(moon, contourRadius, width, height);
            var end = ArcEnd(moon, contourRadius, width, height);
            if (!(end - start > 0.05d))
            {
                continue;
            }

            DrawBrokenArc(
                context,
                Stroke(contour.Alpha, Math.Max(0.8d, 0.95d * scale)),
                moon,
                contourRadius,
                start,
                end - start,
                60,
                contour.On,
                contour.Off);
        }

        // ---- 2. 晨昏线亮带 ----
        var terminatorStart = ArcStart(terminatorCenter, terminatorRadius, width, height);
        var terminatorEnd = ArcEnd(terminatorCenter, terminatorRadius, width, height);
        if (terminatorEnd - terminatorStart > 0.05d && terminatorRadius > 4d)
        {
            var span = terminatorEnd - terminatorStart;
            DrawArc(context, Stroke(0.048d, Math.Max(2.2d, 3d * scale)), terminatorCenter, terminatorRadius, terminatorStart, span, 72);

            // 15 段各自算透明度：亮区沿弧线慢慢跑，就是「晨昏线亮度推移」。
            const int chunks = 15;
            var chunkSweep = span / chunks;
            var phase = time * TwoPi / TerminatorPeriod;
            for (var i = 0; i < chunks; i++)
            {
                var middle = (i + 0.5d) / chunks;
                var wave = 0.5d + (0.5d * Math.Cos((middle * TwoPi * 1.25d) - phase));
                var alpha = 0.100d + (0.120d * wave);
                DrawArc(
                    context,
                    Stroke(alpha, Math.Max(1d, 1.2d * scale)),
                    terminatorCenter,
                    terminatorRadius,
                    terminatorStart + (chunkSweep * i),
                    chunkSweep,
                    6);
            }

            // 明暗两侧各一圈更淡的回声弧，把交界包成一条带而不是一条线。
            var innerEcho = terminatorRadius - (10d * scale);
            if (innerEcho > radius * 0.35d)
            {
                var innerStart = ArcStart(terminatorCenter, innerEcho, width, height);
                var innerEnd = ArcEnd(terminatorCenter, innerEcho, width, height);
                if (innerEnd - innerStart > 0.08d)
                {
                    DrawBrokenArc(context, Stroke(0.042d, Math.Max(0.8d, 0.9d * scale)), terminatorCenter, innerEcho, innerStart, innerEnd - innerStart, 54, 5, 3);
                }
            }

            var outerEcho = terminatorRadius + (9d * scale);
            if (outerEcho < radius * 0.96d)
            {
                var outerStart = ArcStart(terminatorCenter, outerEcho, width, height);
                var outerEnd = ArcEnd(terminatorCenter, outerEcho, width, height);
                if (outerEnd - outerStart > 0.08d)
                {
                    DrawBrokenArc(context, Stroke(0.040d, Math.Max(0.8d, 0.9d * scale)), terminatorCenter, outerEcho, outerStart, outerEnd - outerStart, 54, 4, 3);
                }
            }
        }

        // ---- 3. 环形山：同心弧 + 放射状坡线，靠近光标的那一个提亮 ----
        var nearestDistance = double.MaxValue;
        var nearestCenter = default(Point);
        var nearestRadius = 1d;

        foreach (var layout in Craters)
        {
            var theta = layout.Theta * DegToRad;
            var rho = layout.Rho;
            var center = Polar(moon, rho * radius, theta);
            if (!(center.X > -radius * 0.1d) || !(center.X < width + (radius * 0.1d))
                || !(center.Y > -radius * 0.1d) || !(center.Y < height + (radius * 0.1d)))
            {
                continue;
            }

            // 坑沿不许越过月缘：越靠外，可用半径越小。
            var craterRadius = Math.Min(layout.Size * radius, (0.992d - rho) * radius);
            if (!(craterRadius > 3d * scale))
            {
                continue;
            }

            // 球面透视压缩：越靠月缘越扁，但不压到看不清。
            var squash = Math.Max(0.42d, Math.Sqrt(Math.Max(0.1d, 1d - (rho * rho))));
            var tangentX = -Math.Sin(theta);
            var tangentY = Math.Cos(theta);
            var radialX = Math.Cos(theta);
            var radialY = Math.Sin(theta);
            var crater = new Crater(center, tangentX, tangentY, radialX, radialY, craterRadius, squash);

            var glow = 0d;
            if (hasPointer && influence > 0.001d)
            {
                var angleGap = Math.Abs(WrapAngle(pointerTheta - theta));
                var radiusGap = Math.Abs(pointerRho - rho);
                glow = influence * (1d - Clamp01(angleGap / 0.62d)) * (1d - Clamp01(radiusGap / 0.22d));
            }

            var lit = rho >= terminatorRatio;
            var alpha = Math.Min(0.235d, (lit ? 0.148d : 0.060d) * (1d + (glow * 1.15d)));
            var hair = Math.Max(0.9d, 1.05d * scale);

            crater.DrawRing(context, Stroke(alpha, hair), 1d, 0.3d, TwoPi - 0.24d, 16, 5, 2);
            if (layout.Rings >= 2)
            {
                crater.DrawRing(context, Stroke(alpha * 0.66d, hair), 0.64d, -0.2d, TwoPi - 0.5d, 14, 4, 2);
            }

            if (layout.Rings >= 3)
            {
                crater.DrawRing(context, Stroke(alpha * 0.42d, hair), 0.36d, 0.6d, TwoPi - 0.4d, 12, 3, 2);
            }

            // 受光侧（局部 90°，也就是朝月缘的那一侧）坑沿再压一道，环形山才立得住。
            crater.DrawRing(context, Stroke(Math.Min(0.235d, alpha * 1.08d), hair), 1.06d, (Math.PI * 0.5d) - 0.95d, 1.9d, 10, 10, 0);

            // 坡线：从坑沿向内收的短刻线（地图上的凹陷画法），不会越过月缘。
            if (layout.Ticks > 0)
            {
                var tickPen = Stroke(Math.Max(0.04d, alpha * 0.52d), Math.Max(0.8d, 0.9d * scale));
                for (var k = 0; k < layout.Ticks; k++)
                {
                    var angle = (k * TwoPi / layout.Ticks) + 0.37d;
                    context.DrawLine(tickPen, crater.At(angle, 0.985d), crater.At(angle, 0.76d));
                }
            }

            if (hasPointer)
            {
                var toPointerX = center.X - pointerX;
                var toPointerY = center.Y - pointerY;
                var distance = Math.Sqrt((toPointerX * toPointerX) + (toPointerY * toPointerY));
                if (distance < nearestDistance)
                {
                    nearestDistance = distance;
                    nearestCenter = center;
                    nearestRadius = craterRadius;
                }
            }
        }

        // ---- 4. 月缘本体：宽而淡的垫底 + 细芯，最后压在最上面 ----
        var rimSegments = (int)Math.Round(64d + (32d * Clamp01((scale - 0.52d) / 1.33d)));
        var rimSpan = rimEnd - rimStart;
        DrawArc(context, Stroke(0.052d, Math.Max(2.2d, 3.2d * scale)), moon, radius, rimStart, rimSpan, rimSegments);
        DrawArc(context, Stroke(0.205d, Math.Max(1d, 1.15d * scale)), moon, radius, rimStart, rimSpan, rimSegments);

        // 月缘内侧一条更短的受光带：只亮中段，月缘两端自然收淡。
        var lipStart = rimStart + (rimSpan * 0.22d);
        DrawArc(context, Stroke(0.075d, Math.Max(0.8d, 0.9d * scale)), moon, radius - (3.2d * scale), lipStart, rimSpan * 0.5d, 42);

        // ---- 5. 测量弧：光标附近的环形山被点亮时，周围浮出一圈很淡的八段弧 + 十字刻线 ----
        if (hasPointer && influence > 0.02d && nearestDistance < radius * 0.42d)
        {
            var reticleRadius = Math.Max(nearestRadius * 1.55d, 10d * scale);
            var reticlePen = Stroke(0.045d + (0.075d * influence), Math.Max(0.8d, 0.9d * scale));
            for (var quadrant = 0; quadrant < 8; quadrant++)
            {
                var arcStart = (quadrant * Math.PI / 4d) + 0.14d;
                DrawArc(context, reticlePen, nearestCenter, reticleRadius, arcStart, (Math.PI / 4d) - 0.28d, 6);
            }

            for (var tick = 0; tick < 4; tick++)
            {
                var angle = tick * Math.PI / 2d;
                context.DrawLine(
                    reticlePen,
                    Polar(nearestCenter, reticleRadius * 0.78d, angle),
                    Polar(nearestCenter, reticleRadius * 1.14d, angle));
            }
        }

        // ---- 6. 左上角地球新月：极慢呼吸，只有一条细弧 + 一点暗面轮廓 ----
        var earthRadius = Math.Clamp(Math.Min(width, height) * 0.046d, 16d, 92d)
            * (1d + (Math.Sin(time * TwoPi / EarthPeriod) * 0.028d));
        var earth = new Point(Math.Max(64d, width * 0.14d), Math.Max(58d, height * 0.16d));
        const double lightAngle = 0.42d;
        var earthHair = Math.Max(0.9d, 1.05d * scale);

        DrawArc(context, Stroke(0.042d, Math.Max(0.8d, 0.9d * scale)), earth, earthRadius, lightAngle + 1.16d, TwoPi - 2.32d, 28);
        DrawArc(context, Stroke(0.165d, earthHair), earth, earthRadius, lightAngle - 1.16d, 2.32d, 34);

        // 内弧（晨昏线）：两端收拢到受光边缘的同一点，中间最厚，才是新月而不是圆环。
        const int innerSegments = 30;
        var previous = default(Point);
        for (var i = 0; i <= innerSegments; i++)
        {
            var t = (i / (double)innerSegments * 2d) - 1d;
            var angle = lightAngle + (t * 1.16d);
            var innerRadius = earthRadius * (1d - (0.26d * (1d - (t * t))));
            var current = Polar(earth, innerRadius, angle);
            if (i > 0)
            {
                context.DrawLine(Stroke(0.165d, earthHair), previous, current);
            }

            previous = current;
        }
    }

    /// <summary>圆上落在画布内的起始角度（弧度）：与右边、上边求交取更晚的那一个。</summary>
    private static double ArcStart(Point center, double radius, double width, double height)
    {
        if (!(radius > 0.5d) || !double.IsFinite(radius))
        {
            return Math.PI;
        }

        var start = Math.PI * 0.5d;
        var xLimit = (width - center.X) / radius;
        if (xLimit < 0d)
        {
            start = Math.Acos(Math.Clamp(xLimit, -1d, 1d));
        }

        var topLimit = (height - center.Y) / radius;
        var topAngle = Math.PI - Math.Asin(Math.Clamp(topLimit, -1d, 1d));
        return Math.Max(start, topAngle);
    }

    /// <summary>圆上落在画布内的结束角度（弧度）：与右边、下边求交取更早的那一个。</summary>
    private static double ArcEnd(Point center, double radius, double width, double height)
    {
        if (!(radius > 0.5d) || !double.IsFinite(radius))
        {
            return Math.PI;
        }

        var end = Math.PI * 1.5d;
        var xLimit = (width - center.X) / radius;
        if (xLimit < 0d)
        {
            end = TwoPi - Math.Acos(Math.Clamp(xLimit, -1d, 1d));
        }

        var bottomLimit = (0d - center.Y) / radius;
        var bottomAngle = Math.PI - Math.Asin(Math.Clamp(bottomLimit, -1d, 1d));
        return Math.Min(end, bottomAngle);
    }

    /// <summary>把弧线拆成折线画出来；<paramref name="segments"/> 决定弯曲度，段数不够会露出棱角。</summary>
    private static void DrawArc(DrawingContext context, IPen pen, Point center, double radius, double start, double sweep, int segments)
    {
        if (segments < 1 || !(radius > 0.1d) || !(Math.Abs(sweep) > 1e-4d))
        {
            return;
        }

        var step = sweep / segments;
        var previous = Polar(center, radius, start);
        for (var i = 1; i <= segments; i++)
        {
            var current = Polar(center, radius, start + (step * i));
            context.DrawLine(pen, previous, current);
            previous = current;
        }
    }

    /// <summary>断续弧：每 <paramref name="onSegments"/> 段实线接 <paramref name="offSegments"/> 段空白，等高线才不像年轮。</summary>
    private static void DrawBrokenArc(
        DrawingContext context,
        IPen pen,
        Point center,
        double radius,
        double start,
        double sweep,
        int segments,
        int onSegments,
        int offSegments)
    {
        if (segments < 2 || !(radius > 0.1d) || !(Math.Abs(sweep) > 1e-4d))
        {
            return;
        }

        var cycle = Math.Max(1, onSegments + offSegments);
        var step = sweep / segments;
        var previous = Polar(center, radius, start);
        for (var i = 1; i <= segments; i++)
        {
            var current = Polar(center, radius, start + (step * i));
            if (((i - 1) % cycle) < onSegments)
            {
                context.DrawLine(pen, previous, current);
            }

            previous = current;
        }
    }

    private static Point Polar(Point center, double radius, double angle) =>
        new(center.X + (Math.Cos(angle) * radius), center.Y + (Math.Sin(angle) * radius));

    private static double Clamp01(double value) => value <= 0d ? 0d : value >= 1d ? 1d : value;

    /// <summary>把任意角差折进 (-π, π]，光标与环形山的夹角落差才不会被 2π 周期骗到。</summary>
    private static double WrapAngle(double angle)
    {
        var wrapped = Math.IEEERemainder(angle, TwoPi);
        return double.IsFinite(wrapped) ? wrapped : 0d;
    }

    /// <summary>
    /// 环形山在画面上的局部坐标系：切线轴 × 半径（水平）、法线轴 × 半径 × 透视压缩（垂直）。
    /// 局部角 0° 指向切线方向，90° 指向月缘——受光侧的高光就落在 90° 附近。
    /// </summary>
    private readonly struct Crater
    {
        private readonly Point _center;
        private readonly double _tangentX;
        private readonly double _tangentY;
        private readonly double _radialX;
        private readonly double _radialY;
        private readonly double _radius;
        private readonly double _squash;

        public Crater(Point center, double tangentX, double tangentY, double radialX, double radialY, double radius, double squash)
        {
            _center = center;
            _tangentX = tangentX;
            _tangentY = tangentY;
            _radialX = radialX;
            _radialY = radialY;
            _radius = radius;
            _squash = squash;
        }

        /// <summary>取局部角度上的点，<paramref name="scale"/> 是相对坑沿半径的倍数。</summary>
        public Point At(double angle, double scale)
        {
            var cosine = Math.Cos(angle) * scale * _radius;
            var sine = Math.Sin(angle) * scale * _radius * _squash;
            return new Point(
                _center.X + (_tangentX * cosine) + (_radialX * sine),
                _center.Y + (_tangentY * cosine) + (_radialY * sine));
        }

        /// <summary>画一圈（或一段）坑沿弧；<paramref name="offSegments"/> 为 0 时是实线。</summary>
        public void DrawRing(
            DrawingContext context,
            IPen pen,
            double scale,
            double start,
            double sweep,
            int segments,
            int onSegments,
            int offSegments)
        {
            if (segments < 2 || !(_radius > 0.1d) || !(Math.Abs(sweep) > 1e-4d))
            {
                return;
            }

            var cycle = Math.Max(1, onSegments + offSegments);
            var step = sweep / segments;
            var previous = At(start, scale);
            for (var i = 1; i <= segments; i++)
            {
                var current = At(start + (step * i), scale);
                if (((i - 1) % cycle) < onSegments)
                {
                    context.DrawLine(pen, previous, current);
                }

                previous = current;
            }
        }
    }
}
