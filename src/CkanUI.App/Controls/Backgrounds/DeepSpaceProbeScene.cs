using System;
using Avalonia;
using Avalonia.Media;

namespace CkanUI.App.Controls.Backgrounds;

/// <summary>
/// 深空探测器：右上角一台斜向的探测器总装线稿——透视线框总线、两片带电池片分割的细长太阳翼、
/// 抛物面天线（外沿 + 焦点馈源 + 三脚支架）、尾部钟形喷口；左下拖一条很长的虚线轨迹和三圈渐弱的下行脉冲。
/// </summary>
/// <remarks>
/// <para>构图刻意不平衡：探测器压在右上三分之一，虚线轨迹把视线带向左下角，中间大片留白让给页面卡片。</para>
/// <para>
/// 渲染器无状态：整体漂移、姿态摆动、翼面颤动、脉冲推进全部由 <c>scene.Time</c> 推导；
/// 天线转向与脉冲提速只读 <c>scene.Influence</c> / <c>scene.InfluenceStrength</c>，同一输入永远画出同一帧。
/// </para>
/// <para>
/// 动效关闭时时间按 0 处理，整幅画面停成一张静态总装图，但光标影响照旧生效（天线仍然转 4.35°）——
/// 关掉动效不该连带关掉可感知的状态变化。
/// </para>
/// </remarks>
public sealed class DeepSpaceProbeScene : IBackgroundSceneRenderer
{
    /// <summary>2π：周期换算常量，省掉每帧一次乘法。</summary>
    private const double Tau = Math.PI * 2.0;

    /// <summary>基准缩放（<c>scene.Scale == 1</c>）下机体系的线性单位（DIP）。</summary>
    private const double BaseUnit = 300.0;

    /// <summary>单位下限：窗口再小也不让线稿糊成一团。</summary>
    private const double MinUnit = 80.0;

    /// <summary>单位上限：4K 下探测器也不再继续放大，始终只占右侧一角。</summary>
    private const double MaxUnit = 620.0;

    /// <summary>姿态基准角（弧度，约 −14.9°）：右翼指向右上，左翼指向左下。</summary>
    private const double ProbeTilt = -0.26;

    /// <summary>姿态摆动幅度（弧度，约 1.7°）：压在规格的 1–2° 里。</summary>
    private const double SwayAmplitude = 0.030;

    /// <summary>姿态摆动周期（秒）。</summary>
    private const double SwayPeriod = 38.0;

    /// <summary>漂移的横向快周期（秒）。</summary>
    private const double DriftPeriodFast = 61.0;

    /// <summary>漂移的横向慢周期（秒）：两条不同周期叠加，永远走不到同一相位。</summary>
    private const double DriftPeriodSlow = 97.0;

    /// <summary>纵向漂移周期（秒）：和横向互质，叠出不会打转的游走。</summary>
    private const double DriftPeriodCross = 71.0;

    /// <summary>横向快分量的振幅（机体系单位）。</summary>
    private const double DriftAmplitudeFast = 0.024;

    /// <summary>横向慢分量的振幅（机体系单位）。</summary>
    private const double DriftAmplitudeSlow = 0.009;

    /// <summary>纵向分量的振幅（机体系单位）。</summary>
    private const double DriftAmplitudeCross = 0.018;

    /// <summary>下行脉冲周期（秒）：4.3s，落在规格的 3–5s 之间。</summary>
    private const double PulsePeriod = 4.3;

    /// <summary>光标满强度时脉冲推进的额外倍率：加速但不到频闪。</summary>
    private const double PulseBoost = 1.05;

    /// <summary>太阳翼展开颤动周期（秒）：刻意比姿态摆动快一个数量级。</summary>
    private const double FlutterPeriod = 2.1;

    /// <summary>太阳翼翼尖颤动上限（DIP）：规格要求小于 1 DIP。</summary>
    private const double FlutterLimit = 0.8;

    /// <summary>天线跟随光标的极限转角（弧度，约 4.35°）：只转天线，不转整机。</summary>
    private const double MaxAimTurn = 0.076;

    /// <summary>太阳翼翼根到总线中线的距离（机体系单位）。</summary>
    private const double WingRoot = 0.176;

    /// <summary>单侧太阳翼长度（机体系单位）。</summary>
    private const double WingLength = 0.60;

    /// <summary>太阳翼半高：细长，不要画成机翼。</summary>
    private const double WingHalfHeight = 0.052;

    /// <summary>每侧电池片数量：8 条分割线，正好读成"电池片"而不是刻度。</summary>
    private const int PanelCells = 9;

    /// <summary>虚线轨迹的分段数：段够多，曲线才顺，虚线的间隙也不至于忽长忽短。</summary>
    private const int TrailSteps = 108;

    /// <summary>抛物面母线条数。</summary>
    private const int DishGores = 8;

    /// <summary>抛物面沿法向的压扁系数：0.52 大约相当于斜看 58°，碗沿才不会成一条线。</summary>
    private const double DishSquash = 0.52;

    /// <summary>椭圆折线段数：48 段在 4K 下也看不出折角。</summary>
    private const int EllipseSegments = 48;

    /// <summary>下行脉冲环数量：三环错相，任意时刻肉眼看到两到三环。</summary>
    private const int PulseRings = 3;

    /// <inheritdoc />
    public void Render(DrawingContext context, BackgroundSceneContext scene)
    {
        ArgumentNullException.ThrowIfNull(context);

        var width = scene.Width;
        var height = scene.Height;
        if (!double.IsFinite(width) || !double.IsFinite(height) || width < 96 || height < 96)
        {
            return;
        }

        if (!double.IsFinite(scene.Intensity) || scene.Intensity <= 0.002)
        {
            return;
        }

        // 动效关闭：时间归零，画一张静态总装图；光标影响在下面单独取 scene.InfluenceStrength，不受这里影响。
        var time = scene.MotionEnabled && double.IsFinite(scene.Time) ? scene.Time : 0.0;
        var strength = double.IsFinite(scene.InfluenceStrength)
            ? Math.Clamp(scene.InfluenceStrength, 0.0, 1.0)
            : 0.0;

        var unit = Math.Clamp(
            Math.Min(BaseUnit * scene.Scale, Math.Min(width, height) * 0.46),
            MinUnit,
            MaxUnit);

        // 极慢漂移：三个不同周期的正弦叠加，几十秒里只挪几个像素。
        var driftX = (Math.Sin(Tau * time / DriftPeriodFast) * DriftAmplitudeFast)
            + (Math.Sin(Tau * time / DriftPeriodSlow) * DriftAmplitudeSlow);
        var driftY = Math.Cos(Tau * time / DriftPeriodCross) * DriftAmplitudeCross;

        var tilt = ProbeTilt + (Math.Sin(Tau * time / SwayPeriod) * SwayAmplitude);

        // 构图锚点压在右上；reach 保证整套线稿（含天线外沿）永远不进墙。
        var reach = Math.Min(unit * 1.04, Math.Min(width, height) * 0.44);
        var centerX = Math.Clamp((width * 0.715) + (driftX * unit), reach, width - reach);
        var centerY = Math.Clamp((height * 0.345) + (driftY * unit), reach, height - reach);

        var frame = new ProbeFrame(centerX, centerY, tilt, unit);

        DrawTrail(context, scene, frame, width, height, time, strength);
        DrawWings(context, scene, frame, time);
        DrawBody(context, scene, frame);
        DrawDish(context, scene, frame, strength);
        DrawReticle(context, scene, frame, width, height);
    }

    /// <summary>
    /// 左下角的虚线轨迹与下行脉冲：一条三次贝塞尔拉到左下角，远端渐隐；
    /// 脉冲环沿同一条曲线从探测器往左下推，越远越大越淡，光标靠近时整串加速提亮。
    /// </summary>
    private static void DrawTrail(
        DrawingContext context,
        BackgroundSceneContext scene,
        ProbeFrame frame,
        double width,
        double height,
        double time,
        double strength)
    {
        var unit = frame.Unit;
        var start = frame.Map(-0.46, 0.36);
        var controlA = new Point(start.X - (unit * 0.54), start.Y + (unit * 0.20));
        var controlB = new Point(width * 0.32, height * 0.72);
        var end = new Point(Math.Max(width * 0.06, 14.0), Math.Min(height * 0.93, height - 14.0));

        Point Along(double t)
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

        void DrawPulse(IPen pen, Point center, double radius)
        {
            if (!(radius > 0.6))
            {
                return;
            }

            // 只描边不填充：脉冲是波前，不是光斑。
            context.DrawEllipse(null, pen, center, radius, radius * 0.42);
        }

        // 虚线：108 段里亮 2 段、空 1 段；透明度分 4 档，避免为每段虚线新建画笔。
        var bands = new Pen[4];
        for (var band = 0; band < bands.Length; band++)
        {
            bands[band] = Stroke(scene.TextAt(0.096 - (band * 0.018)), 1.0);
        }

        var previous = Along(0);
        for (var i = 1; i <= TrailSteps; i++)
        {
            var t = (double)i / TrailSteps;
            var current = Along(t);
            if ((i % 3) != 2)
            {
                var band = Math.Min(bands.Length - 1, (int)(t * bands.Length));
                context.DrawLine(bands[band], previous, current);
            }

            previous = current;
        }

        // 三个横向刻度：读起来像航迹上的量测点，不做文字标注。
        var tick = Stroke(scene.TextAt(0.066), 1.0);
        for (var i = 0; i < 3; i++)
        {
            var t = 0.18 + (i * 0.29);
            var point = Along(t);
            var ahead = Along(Math.Min(1.0, t + 0.01));
            var dx = ahead.X - point.X;
            var dy = ahead.Y - point.Y;
            var length = Math.Sqrt((dx * dx) + (dy * dy));
            if (!(length > 0.001))
            {
                continue;
            }

            var nx = -(dy / length) * 0.030 * unit;
            var ny = (dx / length) * 0.030 * unit;
            context.DrawLine(tick, new Point(point.X - nx, point.Y - ny), new Point(point.X + nx, point.Y + ny));
        }

        // 轨迹终点：地面站标记，两个同心圆，外圈更淡。
        context.DrawEllipse(null, Stroke(scene.TextAt(0.075), 1.0), end, 4.6 * scene.Scale, 4.6 * scene.Scale);
        context.DrawEllipse(null, Stroke(scene.TextAt(0.045), 1.0), end, 9.5 * scene.Scale, 9.5 * scene.Scale);

        // 下行脉冲：三环错相 1/3，从轨迹起点往左下推。
        var travel = time * (1.0 + (PulseBoost * strength)) / PulsePeriod;
        for (var ring = 0; ring < PulseRings; ring++)
        {
            var phase = Fraction(travel + (ring / (double)PulseRings));
            var center = Along(0.10 + (0.80 * phase));
            var radius = unit * (0.030 + (0.150 * phase));
            var fade = 1.0 - phase;

            // 逐环衰减 + 光标提亮；上限 0.24 是这套线稿的亮度天花板。
            var alpha = Math.Min(0.24, 0.236 * Math.Pow(fade, 1.15) * (1.0 + (0.55 * strength)));
            if (alpha < 0.012)
            {
                continue;
            }

            DrawPulse(Stroke(scene.AccentAt(alpha), 1.2), center, radius);
        }

        // 发射点：跟着脉冲节拍呼吸的小圆 + 一对短括号，说明"信号从这里出去"。
        var emit = Fraction(travel);
        var pip = 2.4 + (1.4 * (1.0 - emit));
        var pipPen = Stroke(scene.AccentAt(0.13 + (0.07 * (1.0 - emit))), 1.1);
        context.DrawEllipse(null, pipPen, start, pip, pip);
        context.DrawEllipse(null, Stroke(scene.TextAt(0.05), 1.0), start, pip * 2.6, pip * 2.6);
    }

    /// <summary>两片太阳翼：翼面、电池片分割线、根部短梁；翼尖颤动小于 1 DIP。</summary>
    private static void DrawWings(DrawingContext context, BackgroundSceneContext scene, ProbeFrame frame, double time)
    {
        var flutter = Math.Sin(Tau * time / FlutterPeriod);
        var bend = Math.Min(FlutterLimit, frame.Unit * 0.0024) / frame.Unit;

        DrawPanel(context, scene, frame, WingRoot, 1.0, bend * flutter);
        DrawPanel(context, scene, frame, -WingRoot, -1.0, -bend * flutter);
    }

    /// <summary>
    /// 单片太阳翼：沿机体系 ±X 铺开的细长四边形，内部 8 条分割线把翼面切成电池片，
    /// 两条内收的长桁让翼面有厚度；翼根用一截短梁接回总线。
    /// </summary>
    private static void DrawPanel(
        DrawingContext context,
        BackgroundSceneContext scene,
        ProbeFrame frame,
        double rootX,
        double direction,
        double bend)
    {
        var tipX = rootX + (direction * WingLength);

        Point Corner(double progress, double side) => frame.Map(
            rootX + ((tipX - rootX) * progress),
            (side * WingHalfHeight) + (bend * progress * progress));

        var edge = Stroke(scene.AccentAt(0.20), 1.2);
        var topRoot = Corner(0, 1);
        var topTip = Corner(1, 1);
        var bottomTip = Corner(1, -1);
        var bottomRoot = Corner(0, -1);
        context.DrawLine(edge, topRoot, topTip);
        context.DrawLine(edge, topTip, bottomTip);
        context.DrawLine(edge, bottomTip, bottomRoot);
        context.DrawLine(edge, bottomRoot, topRoot);

        var spar = Stroke(scene.AccentAt(0.085), 1.0);
        context.DrawLine(spar, Corner(0, 0.56), Corner(1, 0.56));
        context.DrawLine(spar, Corner(0, -0.56), Corner(1, -0.56));

        var cell = Stroke(scene.TextAt(0.10), 1.0);
        for (var i = 1; i < PanelCells; i++)
        {
            var progress = (double)i / PanelCells;
            context.DrawLine(cell, Corner(progress, -1), Corner(progress, 1));
        }

        var boom = Stroke(scene.TextAt(0.13), 1.0);
        var baseX = rootX - (direction * 0.054);
        context.DrawLine(boom, frame.Map(baseX, 0.026), frame.Map(rootX, 0.026));
        context.DrawLine(boom, frame.Map(baseX, -0.026), frame.Map(rootX, -0.026));
        context.DrawLine(boom, frame.Map(baseX, 0.026), frame.Map(baseX, -0.026));
    }

    /// <summary>总线：前面板 + 上右偏移的背面 + 四条连线，读成立方体而不是两个矩形；尾部挂喷口。</summary>
    private static void DrawBody(DrawingContext context, BackgroundSceneContext scene, ProbeFrame frame)
    {
        const double halfLength = 0.128;
        const double halfHeight = 0.098;
        const double depthX = 0.074;
        const double depthY = -0.056;

        Point Front(double x, double y) => frame.Map(x, y);
        Point Back(double x, double y) => frame.Map(x + depthX, y + depthY);

        var core = Stroke(scene.AccentAt(0.24), 1.4);
        var front0 = Front(-halfLength, -halfHeight);
        var front1 = Front(halfLength, -halfHeight);
        var front2 = Front(halfLength, halfHeight);
        var front3 = Front(-halfLength, halfHeight);
        context.DrawLine(core, front0, front1);
        context.DrawLine(core, front1, front2);
        context.DrawLine(core, front2, front3);
        context.DrawLine(core, front3, front0);

        var back = Stroke(scene.AccentAt(0.12), 1.0);
        var back0 = Back(-halfLength, -halfHeight);
        var back1 = Back(halfLength, -halfHeight);
        var back2 = Back(halfLength, halfHeight);
        var back3 = Back(-halfLength, halfHeight);
        context.DrawLine(back, back0, back1);
        context.DrawLine(back, back1, back2);
        context.DrawLine(back, back2, back3);
        context.DrawLine(back, back3, back0);

        var link = Stroke(scene.AccentAt(0.15), 1.0);
        context.DrawLine(link, front0, back0);
        context.DrawLine(link, front1, back1);
        context.DrawLine(link, front2, back2);
        context.DrawLine(link, front3, back3);

        // 前面板细节：两道隔板 + 一个传感器窗口，避免方块中间空得发光。
        var detail = Stroke(scene.TextAt(0.11), 1.0);
        context.DrawLine(detail, Front(-0.092, -0.042), Front(0.062, -0.042));
        context.DrawLine(detail, Front(-0.092, 0.028), Front(0.016, 0.028));
        context.DrawEllipse(null, Stroke(scene.AccentAt(0.17), 1.1), Front(0.074, 0.060), 0.015 * frame.Unit, 0.015 * frame.Unit);

        // 尾部钟形喷口：张口朝机腹（与天线相反的一侧），只画外轮廓与内锥。
        var bell = Stroke(scene.TextAt(0.17), 1.1);
        var throatLeft = Front(0.069, 0.150);
        var throatRight = Front(0.141, 0.150);
        var mouthLeft = Front(0.045, 0.240);
        var mouthRight = Front(0.165, 0.240);
        context.DrawLine(bell, throatLeft, throatRight);
        context.DrawLine(bell, throatLeft, mouthLeft);
        context.DrawLine(bell, throatRight, mouthRight);
        context.DrawLine(bell, mouthLeft, mouthRight);

        var inner = Stroke(scene.TextAt(0.095), 1.0);
        var throatCenter = Front(0.105, 0.152);
        context.DrawLine(inner, throatCenter, mouthLeft);
        context.DrawLine(inner, throatCenter, mouthRight);

        var collar = Stroke(scene.TextAt(0.12), 1.0);
        context.DrawLine(collar, Front(0.062, 0.132), Front(0.148, 0.132));

        // 排气：三道极淡的短虚线，暗示推力方向，不画火焰。
        var exhaust = Stroke(scene.TextAt(0.05), 1.0);
        context.DrawLine(exhaust, Front(0.105, 0.254), Front(0.105, 0.292));
        context.DrawLine(exhaust, Front(0.066, 0.258), Front(0.066, 0.284));
        context.DrawLine(exhaust, Front(0.144, 0.258), Front(0.144, 0.284));
    }

    /// <summary>
    /// 抛物面天线：外沿椭圆 + 内圈盆底 + 8 条母线 + 焦点馈源与三脚支架。
    /// 朝向由 <c>scene.Influence</c> 给出的方向决定，但转角夹在 ±4.35° 内，而且只转天线、不转整机。
    /// </summary>
    private static void DrawDish(
        DrawingContext context,
        BackgroundSceneContext scene,
        ProbeFrame frame,
        double strength)
    {
        var unit = frame.Unit;
        var hub = frame.Map(-0.255, -0.255);
        var rim = 0.185 * unit;

        var baseFacing = frame.Tilt + Math.PI + 0.13;
        var toInfluence = scene.Influence - hub;
        var delta = WrapAngle(Math.Atan2(toInfluence.Y, toInfluence.X) - baseFacing);
        var facing = baseFacing + (Math.Clamp(delta, -MaxAimTurn, MaxAimTurn) * strength);

        var normal = new Vector(Math.Cos(facing), Math.Sin(facing));
        var lateral = new Vector(-normal.Y, normal.X);
        var throat = hub - (normal * (rim * 0.42));

        // 外沿：核心线 20%–24%，光标越近越亮（仍然不超过 24%）。
        context.DrawGeometry(
            null,
            Stroke(scene.AccentAt(0.20 + (0.04 * strength)), 1.4),
            BuildEllipse(hub, lateral, rim, normal, rim * DishSquash));

        // 盆底内圈：小一圈、深一点，碗的深度就出来了。
        context.DrawGeometry(
            null,
            Stroke(scene.AccentAt(0.115), 1.0),
            BuildEllipse(throat, lateral, rim * 0.56, normal, rim * 0.56 * DishSquash));

        var gore = Stroke(scene.TextAt(0.095), 1.0);
        for (var i = 0; i < DishGores; i++)
        {
            var angle = Tau * i / DishGores;
            context.DrawLine(gore, DishRimPoint(hub, normal, lateral, rim, angle), throat);
        }

        // 焦点馈源：三根支腿从碗沿收到馈源，馈源再朝法向伸一小截喇叭。
        var feed = hub + (normal * (rim * 1.06));
        var strut = Stroke(scene.TextAt(0.13), 1.0);
        for (var i = 0; i < 3; i++)
        {
            var angle = (Tau * i / 3.0) + 0.85;
            context.DrawLine(strut, DishRimPoint(hub, normal, lateral, rim * 0.93, angle), feed);
        }

        var feedPen = Stroke(scene.AccentAt(0.22), 1.2);
        var hornTip = feed + (normal * (0.040 * unit));
        var hornLeft = feed + (lateral * (0.017 * unit));
        var hornRight = feed - (lateral * (0.017 * unit));
        context.DrawLine(feedPen, hornLeft, hornTip);
        context.DrawLine(feedPen, hornRight, hornTip);
        context.DrawLine(feedPen, hornLeft, hornRight);
        context.DrawEllipse(null, feedPen, feed, 0.010 * unit, 0.010 * unit);

        // 支架：总线顶面一根主柱 + 一根斜撑 + 万向节小圆。
        var mountBase = frame.Map(-0.118, -0.128);
        var mountTop = hub - (normal * (rim * 0.34));
        var mount = Stroke(scene.TextAt(0.17), 1.2);
        context.DrawLine(mount, mountBase, mountTop);
        context.DrawEllipse(null, Stroke(scene.TextAt(0.14), 1.0), mountTop, 0.015 * unit, 0.015 * unit);

        var brace = Stroke(scene.TextAt(0.10), 1.0);
        context.DrawLine(
            brace,
            frame.Map(-0.058, -0.102),
            new Point((mountBase.X + mountTop.X) * 0.5, (mountBase.Y + mountTop.Y) * 0.5));

        // 光标靠近才出现的瞄准括号：给"天线在跟手"一个直接证据。
        if (strength > 0.02)
        {
            var lockPen = Stroke(scene.AccentAt(0.055 + (0.125 * strength)), 1.0);
            var pip = feed + (normal * (rim * 0.62));
            var arm = 0.034 * unit;
            var inner = arm * 0.34;
            context.DrawLine(lockPen, pip + (lateral * arm), pip + (lateral * inner));
            context.DrawLine(lockPen, pip - (lateral * arm), pip - (lateral * inner));
            context.DrawLine(lockPen, pip + (normal * (arm * 0.55)), pip + (normal * (arm * 1.25)));
            context.DrawEllipse(null, lockPen, pip, 0.008 * unit, 0.008 * unit);
        }
    }

    /// <summary>右下角非闭合角标 + 左翼尖引出线：总装图的制图感，不写字，保持"只透过缝隙可见"的低调。</summary>
    private static void DrawReticle(
        DrawingContext context,
        BackgroundSceneContext scene,
        ProbeFrame frame,
        double width,
        double height)
    {
        var scale = scene.Scale;
        var cornerPen = Stroke(scene.TextAt(0.055), 1.0);
        var cornerX = width * 0.925;
        var cornerY = height * 0.855;
        var arm = 26.0 * scale;
        context.DrawLine(cornerPen, new Point(cornerX - arm, cornerY), new Point(cornerX, cornerY));
        context.DrawLine(cornerPen, new Point(cornerX, cornerY), new Point(cornerX, cornerY + arm));
        context.DrawLine(
            cornerPen,
            new Point(cornerX - (arm * 0.58), cornerY + (arm * 0.58)),
            new Point(cornerX - (arm * 0.32), cornerY + (arm * 0.32)));

        var tip = frame.Map(-0.782, 0.045);
        var elbow = new Point(Math.Max(tip.X - (52.0 * scale), 16.0), tip.Y + (30.0 * scale));
        var tail = new Point(Math.Max(elbow.X - (40.0 * scale), 8.0), elbow.Y);
        var leaderPen = Stroke(scene.TextAt(0.07), 1.0);
        context.DrawLine(leaderPen, tip, elbow);
        context.DrawLine(leaderPen, elbow, tail);
        context.DrawLine(leaderPen, new Point(tail.X, tail.Y - (5.0 * scale)), new Point(tail.X, tail.Y + (5.0 * scale)));
    }

    /// <summary>椭圆的极坐标点：<paramref name="lateral"/> 是长轴，<paramref name="normal"/> 是压扁后的短轴。</summary>
    private static Point DishRimPoint(Point center, Vector normal, Vector lateral, double radius, double angle) =>
        center
        + (lateral * (radius * Math.Cos(angle)))
        + (normal * (radius * DishSquash * Math.Sin(angle)));

    /// <summary>用折线拼一个（可任意取向的）椭圆；只描边，不填充。</summary>
    private static StreamGeometry BuildEllipse(
        Point center,
        Vector majorAxis,
        double majorRadius,
        Vector minorAxis,
        double minorRadius)
    {
        var geometry = new StreamGeometry();
        using (var geometryContext = geometry.Open())
        {
            geometryContext.BeginFigure(center + (majorAxis * majorRadius), false);
            for (var i = 1; i <= EllipseSegments; i++)
            {
                var angle = Tau * i / EllipseSegments;
                geometryContext.LineTo(
                    center
                    + (majorAxis * (majorRadius * Math.Cos(angle)))
                    + (minorAxis * (minorRadius * Math.Sin(angle))),
                    true);
            }

            geometryContext.EndFigure(true);
        }

        return geometry;
    }

    /// <summary>把角度折进 (−π, π]：光标绕到天线背后时，夹角才不会突然翻边。NaN 一律归零。</summary>
    private static double WrapAngle(double angle) =>
        double.IsFinite(angle) ? Math.IEEERemainder(angle, Tau) : 0.0;

    /// <summary>取小数部分：脉冲相位用，负数也能落在 0–1。</summary>
    private static double Fraction(double value)
    {
        if (!double.IsFinite(value))
        {
            return 0.0;
        }

        var fraction = value - Math.Floor(value);
        return fraction >= 0.0 ? fraction : 0.0;
    }

    /// <summary>一支细线画笔：透明度已经由 <see cref="BackgroundSceneContext.AccentAt"/> 之类折进颜色里。</summary>
    private static Pen Stroke(Color color, double thickness) =>
        new(new SolidColorBrush(color, 1.0), thickness);

    /// <summary>
    /// 机体系：局部坐标以 <see cref="Unit"/> 为单位，+X 沿右翼、+Y 指向机腹（屏幕上偏右下）。
    /// 姿态角只影响映射，不留在任何字段里——渲染器仍然是无状态的。
    /// </summary>
    /// <param name="CenterX">总线中心的画布 X（DIP）。</param>
    /// <param name="CenterY">总线中心的画布 Y（DIP）。</param>
    /// <param name="Tilt">当前姿态角（弧度，已含摆动）。</param>
    /// <param name="Unit">机体系单位长度（DIP）。</param>
    private readonly record struct ProbeFrame(double CenterX, double CenterY, double Tilt, double Unit)
    {
        /// <summary>把机体系局部坐标映射到画布坐标。</summary>
        /// <param name="localX">沿右翼方向的局部坐标。</param>
        /// <param name="localY">沿机腹方向的局部坐标。</param>
        /// <returns>画布上的点（DIP）。</returns>
        public Point Map(double localX, double localY)
        {
            var cos = Math.Cos(Tilt);
            var sin = Math.Sin(Tilt);
            return new Point(
                CenterX + (((localX * cos) - (localY * sin)) * Unit),
                CenterY + (((localX * sin) + (localY * cos)) * Unit));
        }
    }
}
