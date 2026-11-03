using System;
using System.Collections.Generic;

namespace CkanUI.App.Controls;

/// <summary>
/// 一颗星点的静态参数：归一化坐标（0–1），布局时才乘成像素。
/// </summary>
/// <param name="NormalizedX">水平位置，0 在左、1 在右。</param>
/// <param name="NormalizedY">垂直位置，0 在上、1 在下。</param>
/// <param name="Radius">基础半径（DIP）；星座节点比背景星点大一点。</param>
/// <param name="BaseAlpha">基础不透明度，闪烁在它上面做乘性调制。</param>
/// <param name="TwinklePhase">闪烁相位（弧度），让每个星点错开。</param>
/// <param name="TwinkleSpeed">闪烁角速度（弧度/秒）。</param>
/// <param name="Warmth">色温 0（冷白）–1（偏暖），渲染侧据此在冷星与暖星之间取色。</param>
public sealed record StarPoint(
    double NormalizedX,
    double NormalizedY,
    double Radius,
    double BaseAlpha,
    double TwinklePhase,
    double TwinkleSpeed,
    double Warmth);

/// <summary>星座里的一条连线，两端都是 <see cref="ConstellationFigure.Nodes"/> 的下标。</summary>
/// <param name="From">起点下标。</param>
/// <param name="To">终点下标。</param>
public readonly record struct ConstellationEdge(int From, int To);

/// <summary>
/// 一个星座：节点（星点下标）、连线、以及整体"呼吸"的相位与周期。
/// </summary>
/// <param name="Nodes">节点对应的星点下标。</param>
/// <param name="Edges">要画的连线。</param>
/// <param name="Phase">呼吸相位（弧度）。</param>
/// <param name="PeriodSeconds">呼吸周期（秒）。</param>
public sealed record ConstellationFigure(
    IReadOnlyList<int> Nodes,
    IReadOnlyList<ConstellationEdge> Edges,
    double Phase,
    double PeriodSeconds);

/// <summary>
/// 一颗流星在某一帧的几何与亮度：头尾坐标都是归一化 0–1（渲染侧再乘窗口尺寸），
/// 模型本身不依赖 <see cref="StarfieldModel.Layout"/> 得到的画布尺寸。
/// </summary>
/// <param name="TailX">尾端横坐标（归一化）。</param>
/// <param name="TailY">尾端纵坐标（归一化）。</param>
/// <param name="HeadX">头端横坐标（归一化）。</param>
/// <param name="HeadY">头端纵坐标（归一化）。</param>
/// <param name="HeadAlpha">头端不透明度；峰值由 schedule 的 0.30–0.55 决定。</param>
/// <param name="TailAlpha">尾端不透明度，恒为头端的 0.30。</param>
public readonly record struct MeteorState(
    double TailX,
    double TailY,
    double HeadX,
    double HeadY,
    double HeadAlpha,
    double TailAlpha);

/// <summary>
/// 星空背景的几何与亮度模型：只算数值、不碰渲染，可以在无头测试里直接验证。
/// </summary>
/// <remarks>
/// <para>
/// 三层叠加：星轨（渲染侧按时钟缓慢旋转的弧线）、星点（各带相位与角速度的闪烁）、星座（节点 + 连线，
/// 按各自周期整体呼吸）。光标只影响邻近的一小片：星点被推离光标并变亮，命中星座的节点越多，
/// 该星座的连线越亮，同时最多拉出 <see cref="PointerLinkCount"/> 条牵引线。
/// </para>
/// <para>
/// 所有公共方法都容忍非法输入（NaN、负数、零尺寸）：背景层在窗口最小化、设计时预览、
/// 主题切换的中间态里都会被问尺寸和位置，抛异常会把整个窗口带崩。
/// </para>
/// </remarks>
public sealed class StarfieldModel
{
    /// <summary>默认星点数量（含星座节点）。</summary>
    public const int DefaultStarCount = 172;

    /// <summary>默认星座数量。</summary>
    public const int DefaultConstellationCount = 7;

    /// <summary>
    /// 固定随机种子基线：测试与「想要可复现的一片星空」时显式传入，
    /// 不传则用 <see cref="LaunchSeed"/>（每次启动换一份）。
    /// </summary>
    public const int DefaultSeed = 0x5EED1A7;

    /// <summary>光标影响半径（像素）；超出这个距离的星点完全不理会光标。</summary>
    public const double InfluenceRadius = 190d;

    /// <summary>光标最多拉出几条牵引线。</summary>
    public const int PointerLinkCount = 3;

    /// <summary>每个星座的节点数。</summary>
    public const int NodesPerFigure = 5;

    /// <summary>星座数量上限（也决定了分布网格的容量）。</summary>
    public const int MaxConstellationCount = 9;

    /// <summary>星点数量下限，保证「星点 + 星座」两层都画得出来。</summary>
    public const int MinStarCount = 48;

    /// <summary>左上角延时星轨的弧数量：一圈圈同心弧才有长曝光的味道。</summary>
    public const int CornerArcCount = 9;

    /// <summary>左上角星轨峰值不透明度的天花板：再亮就压过正文文字了。</summary>
    public const double CornerArcAlphaCeiling = 0.34;

    /// <summary>左上角星轨最内侧那条的峰值不透明度：外圈按 <see cref="CornerArcFalloff"/> 递减。</summary>
    public const double CornerArcPeakAlpha = 0.26;

    /// <summary>相邻两条星轨的峰值不透明度递减比例。</summary>
    public const double CornerArcFalloff = 0.055;

    /// <summary>星轨呼吸的深度：0.18 表示峰值在 82%–100% 之间起伏，只做"轻微"呼吸。</summary>
    public const double CornerArcBreathDepth = 0.18;

    /// <summary>星轨呼吸周期（秒）：比星座的 8.5–11.5s 略快，两层不会整片一起明灭。</summary>
    public const double CornerArcBreathPeriod = 9.5;

    /// <summary>远景星轨（右上角那族）的呼吸周期（秒）：与左上角错开一个档次。</summary>
    public const double TrailBreathPeriod = 13.5;

    /// <summary>远景星轨的呼吸深度。</summary>
    public const double TrailBreathDepth = 0.22;

    /// <summary>
    /// 本次进程启动用的星座种子：每次启动换一份，同一进程内的默认实例共用同一片星空
    /// （窗口重建、页面切换时背景不会突然换一张图）。
    /// </summary>
    private static readonly int LaunchSeed = CreateLaunchSeed();

    /// <summary>同时排布的流星颗数。</summary>
    public const int MeteorScheduleCount = 12;

    /// <summary>一颗流星划过全程的时长（秒）。</summary>
    public const double MeteorTravelSeconds = 1.4;

    /// <summary>虚拟极点的横向偏移（占画布宽度的比例）：略微在窗外，弧线才像绕极点扫过来。</summary>
    private const double CornerPoleOffsetX = 0.06;

    /// <summary>虚拟极点的纵向偏移（占画布高度的比例）。</summary>
    private const double CornerPoleOffsetY = 0.10;

    /// <summary>星轨起始角的基准值（弧度）：正对左上角那片看得见的区域。</summary>
    private const double CornerArcBaseAngle = 0.55;

    /// <summary>相邻两条弧错开的起始角（弧度）：否则九条弧从同一条半径上一起起步。</summary>
    private const double CornerArcStagger = 0.03;

    /// <summary>
    /// 整族星轨共同绕极点转动的角速度（弧度/秒）：0.012 约合每秒 0.7°，
    /// 转满一圈约 8.7 分钟——眼睛盯十秒才看得出位移，不会抢正文的注意力。
    /// </summary>
    public const double CornerArcRotationRate = 0.012;

    /// <summary>
    /// 相邻星轨之间的视差角速度（弧度/秒）：奇偶条反向，叠在共同转动之上，
    /// 让九条弧不是一块铁板在转。数值必须远小于 <see cref="CornerArcRotationRate"/>，否则会退化成互相打转。
    /// </summary>
    public const double CornerArcDrift = 0.0035;

    /// <summary>流星尾巴占行程长度的比例。</summary>
    private const double MeteorTailRatio = 0.38;

    /// <summary>尾端不透明度相对头端的比例。</summary>
    private const double MeteorTailAlphaRatio = 0.30;

    /// <summary>头端亮度随相位衰减的指数：小于 1 让头部更"尖"，划过的一瞬更干脆。</summary>
    private const double MeteorHeadAlphaPower = 0.8;

    /// <summary>流星的两条相位通道数。</summary>
    private const int MeteorLaneCount = 2;

    /// <summary>每条通道里的窗口槽位数。</summary>
    private const int MeteorLaneSlots = MeteorScheduleCount / MeteorLaneCount;

    /// <summary>通道内周期抖动幅度（秒）：只做小幅抖动，窗口才不会被相邻槽位追上。</summary>
    private const double MeteorPeriodJitter = 0.05;

    /// <summary>流星排布用的固定种子偏移：与星空主种子混合后单开一条随机流。</summary>
    private const int MeteorSeedSalt = 0x6B8B4567;

    /// <summary>单帧最多推进的秒数：窗口被拖动/断点停住后回来，不能让星空瞬移一大段。</summary>
    private const double MaxStepSeconds = 0.1;

    /// <summary>光标跟随的收敛速率（越大越"贴手"，越小越有拖尾）。</summary>
    private const double PointerEaseRate = 7.5;

    /// <summary>牵引线起画的门槛强度，避免收尾阶段留下一条几乎看不见的线。</summary>
    private const double LinkStrengthFloor = 0.04;

    private const int FigureGridColumns = 3;

    private const int FigureGridRows = 3;

    // 右下角测控站的保守避让区（归一化）：锅盖 + 馈源 + 支腿的视觉包围盒。
    // 只用于移动整组星座，不裁线——避免连线从盘面中间穿过，也避免出现断头线。
    private const double TrackingStationZoneMinX = 0.66;
    private const double TrackingStationZoneMinY = 0.58;
    private const double TrackingStationZoneMaxX = 1.12;
    private const double TrackingStationZoneMaxY = 1.12;
    private const double TrackingStationZoneMargin = 0.015;

    private const double FigureMargin = 0.13;

    private const double FigureSpread = 0.74;

    private const double NodeMargin = 0.05;

    private const int MinAmbientStars = 24;

    // 两条通道的基准周期：通道内窗口间距 = 周期 / 槽位数（13.2/6 ≈ 2.2s、16.2/6 = 2.7s），
    // 都大于 1.4s 的行程，所以任一条通道在同一时刻最多一颗在飞 → 同屏最多两颗。
    // 通道内只做 ±0.05s 的周期抖动：抖动再大，相对相位就会在几十秒内漂到窗口重叠。
    private static readonly double[] MeteorLanePeriods = [13.2, 16.2];

    // 通道 0 的相位让第 1 颗在 t=0 时正好飞到 1/3 处（headless 快照才有像素可验）；
    // 通道 1 错开到 1.2s，使 t=0 这一帧只有一颗在飞。
    private static readonly double[] MeteorLanePhases = [-MeteorTravelSeconds / 3, 1.2];

    /// <summary>
    /// 星座经典天文学形态骨架与连线配置：
    /// 抛弃刻板的圆周均分点，采用真实天文学中的折线流、十字架、长勺、弓矢与菱形风筝形态。
    /// </summary>
    private readonly struct ConstellationArchetype
    {
        public readonly (double X, double Y)[] LocalCoords;
        public readonly ConstellationEdge[] Edges;

        public ConstellationArchetype((double X, double Y)[] coords, ConstellationEdge[] edges)
        {
            LocalCoords = coords;
            Edges = edges;
        }
    }

    private static readonly ConstellationArchetype[] ConstellationArchetypes =
    [
        // 1. 仙后座 / W 形或折线流 (Cassiopeia / Zigzag stream)：起伏舒展的 5 点主干折线
        new(
            [(-1.0, 0.25), (-0.45, -0.45), (0.05, 0.35), (0.55, -0.30), (1.0, 0.40)],
            [new(0, 1), new(1, 2), new(2, 3), new(3, 4)]),

        // 2. 北斗小勺 / 斗魁与斗柄 (Little Dipper / Ladle)：斗身 4 点闭环 + 斗柄引出
        new(
            [(-0.75, -0.35), (-0.15, -0.45), (-0.05, 0.25), (-0.65, 0.30), (0.75, 0.50)],
            [new(0, 1), new(1, 2), new(2, 3), new(3, 0), new(2, 4)]),

        // 3. 天鹅十字 / 南十字座 (Northern Cross / Cygnus)：中心亮星 + 纵横舒展的十字飞翼
        new(
            [(0.0, 0.0), (0.0, -0.90), (0.0, 0.85), (-0.75, -0.15), (0.75, -0.15)],
            [new(1, 0), new(0, 2), new(3, 0), new(0, 4)]),

        // 4. 猎户弓矢 / 箭镞形态 (Orion Arrow / Chevron)：主干脊柱 + 优雅的后掠折角
        new(
            [(0.0, -0.80), (-0.65, -0.10), (0.65, -0.10), (0.0, 0.30), (0.0, 0.90)],
            [new(0, 1), new(0, 2), new(1, 3), new(2, 3), new(3, 4)]),

        // 5. 菱形风筝与星尾 (Diamond Kite with Streamer)：双三角菱形 + 尾随摆动星
        new(
            [(0.0, -0.75), (-0.55, 0.0), (0.55, 0.0), (0.0, 0.50), (0.45, 0.95)],
            [new(0, 1), new(0, 2), new(1, 3), new(2, 3), new(1, 2), new(3, 4)]),

        // 6. 皇冠月牙弧形 (Corona Arc / Crescent)：优雅的微曲抛物线星弧
        new(
            [(-0.90, 0.40), (-0.50, -0.10), (0.0, -0.35), (0.50, -0.10), (0.90, 0.40)],
            [new(0, 1), new(1, 2), new(2, 3), new(3, 4)]),
    ];

    private readonly List<StarPoint> _stars = [];
    private readonly List<ConstellationFigure> _figures = [];
    private readonly List<(double Distance, int Index)> _candidates = [];
    private readonly List<int> _pointerLinks = [];
    private readonly MeteorSpec[] _meteors;

    private double[] _pixelX;
    private double[] _pixelY;
    private double[] _influence;
    private double[] _offsetX;
    private double[] _offsetY;
    private double[] _figureInfluence;

    private double _targetPointerX;
    private double _targetPointerY;
    private double _pointerX;
    private double _pointerY;
    private bool _hasPointer;
    private double _pointerStrength;

    /// <summary>按星点数与星座数生成一份固定布局。</summary>
    /// <param name="starCount">星点总数（含星座节点），低于 <see cref="MinStarCount"/> 会被抬到下限。</param>
    /// <param name="constellationCount">星座数量，0–<see cref="MaxConstellationCount"/>。</param>
    /// <param name="seed">随机种子；同一种子得到同一份星空。传 <c>null</c> 用本次启动的 <see cref="Seed"/>。</param>
    public StarfieldModel(
        int starCount = DefaultStarCount,
        int constellationCount = DefaultConstellationCount,
        int? seed = null)
    {
        Seed = seed ?? LaunchSeed;
        var total = Math.Max(MinStarCount, starCount);
        var figureCount = Math.Clamp(constellationCount, 0, MaxConstellationCount);
        var reserved = figureCount * NodesPerFigure;
        if (total - reserved < MinAmbientStars)
        {
            figureCount = Math.Max(0, (total - MinAmbientStars) / NodesPerFigure);
            reserved = figureCount * NodesPerFigure;
        }

        _meteors = new MeteorSpec[MeteorScheduleCount];

        var rng = new Rng(Seed);
        BuildFigures(figureCount, ref rng);
        BuildAmbientStars(total - reserved, ref rng);
        BuildMeteors(Seed);

        _pixelX = new double[_stars.Count];
        _pixelY = new double[_stars.Count];
        _influence = new double[_stars.Count];
        _offsetX = new double[_stars.Count];
        _offsetY = new double[_stars.Count];
        _figureInfluence = new double[_figures.Count];
    }

    /// <summary>本次布局用的种子：默认是本次启动的随机值，显式传入时就是那个值。</summary>
    public int Seed { get; }

    /// <summary>本次进程启动的星座种子（只读，供测试与诊断查看）。</summary>
    public static int LaunchSeedValue => LaunchSeed;

    /// <summary>当前画布宽度（DIP）；还没 <see cref="Layout"/> 过时为 0。</summary>
    public double Width { get; private set; }

    /// <summary>当前画布高度（DIP）；还没 <see cref="Layout"/> 过时为 0。</summary>
    public double Height { get; private set; }

    /// <summary>动画时钟（秒）。动效关闭时渲染侧不再推进它，星空就停在当前一帧。</summary>
    public double Time { get; private set; }

    /// <summary>星点总数（含星座节点）。</summary>
    public int StarCount => _stars.Count;

    /// <summary>星座数量。</summary>
    public int FigureCount => _figures.Count;

    /// <summary>星点参数表，索引与 <see cref="StarX"/> 等取点方法一致。</summary>
    public IReadOnlyList<StarPoint> Stars => _stars;

    /// <summary>星座表。</summary>
    public IReadOnlyList<ConstellationFigure> Figures => _figures;

    /// <summary>光标是否停在窗口内（离开后牵引线淡出）。</summary>
    public bool HasPointer => _hasPointer;

    /// <summary>光标跟随强度 0–1：离开窗口后缓降到 0，牵引线与星点高亮一起消失。</summary>
    public double PointerStrength => _pointerStrength;

    /// <summary>光标当前位置（已缓动、已夹进画布）。</summary>
    public double PointerX => _pointerX;

    /// <summary>光标当前位置（已缓动、已夹进画布）。</summary>
    public double PointerY => _pointerY;

    /// <summary>当前离光标最近的几个星座节点下标，供渲染侧画牵引线。</summary>
    public IReadOnlyList<int> PointerLinks => _pointerLinks;

    /// <summary>左上角星轨族的虚拟极点横坐标：落在画布左侧之外。</summary>
    /// <param name="width">画布宽度（DIP）。</param>
    /// <returns>极点横坐标；宽度为正时恒为负。</returns>
    public double CornerPoleX(double width) => -CornerPoleOffsetX * SanitizeSize(width);

    /// <summary>左上角星轨族的虚拟极点纵坐标：落在画布上方之外。</summary>
    /// <param name="height">画布高度（DIP）。</param>
    /// <returns>极点纵坐标；高度为正时恒为负。</returns>
    public double CornerPoleY(double height) => -CornerPoleOffsetY * SanitizeSize(height);

    /// <summary>第几条左上角星轨的半径：从 0.12 递增到 0.72 个 <paramref name="unit"/>。</summary>
    /// <param name="index">星轨下标，越界会被夹进 0–(<see cref="CornerArcCount"/> − 1)。</param>
    /// <param name="unit">归一化基准，渲染侧传 min(宽, 高)。</param>
    /// <returns>半径（DIP）；<paramref name="unit"/> 非法时为 0。</returns>
    public double CornerArcRadius(int index, double unit) =>
        SanitizeSize(unit) * (0.12 + (0.075 * CornerArcIndex(index)));

    /// <summary>
    /// 第几条星轨的起始角（弧度）：整族以 <see cref="CornerArcRotationRate"/> 绕极点缓慢转动，
    /// 奇偶条再叠一份反向的 <see cref="CornerArcDrift"/> 视差，模拟长曝光的周日旋转。
    /// </summary>
    /// <param name="index">星轨下标，越界会被夹进 0–(<see cref="CornerArcCount"/> − 1)。</param>
    /// <returns>起始角；时钟推进 1 秒转 0.012 ± 0.0035 弧度。</returns>
    public double CornerArcStart(int index)
    {
        var arc = CornerArcIndex(index);
        var parallax = (arc % 2) == 0 ? CornerArcDrift : -CornerArcDrift;
        return CornerArcBaseAngle + (arc * CornerArcStagger) + (Time * (CornerArcRotationRate + parallax));
    }

    /// <summary>整族星轨转满一圈所需的秒数：供渲染/测试核对「轻微」的量级。</summary>
    public static double CornerArcRevolutionSeconds => Math.Tau / CornerArcRotationRate;

    /// <summary>第几条星轨扫过的角度（弧度）：越靠外的弧越长。</summary>
    /// <param name="index">星轨下标，越界会被夹进 0–(<see cref="CornerArcCount"/> − 1)。</param>
    /// <returns>介于 0.34–0.74 弧度。</returns>
    public double CornerArcSweep(int index) => 0.34 + (0.05 * CornerArcIndex(index));

    /// <summary>第几条星轨的峰值不透明度：越靠外越淡，且封顶 <see cref="CornerArcAlphaCeiling"/>。</summary>
    /// <param name="index">星轨下标，越界会被夹进 0–(<see cref="CornerArcCount"/> − 1)。</param>
    /// <returns>(0, <see cref="CornerArcAlphaCeiling"/>] 之间的不透明度。</returns>
    public double CornerArcAlpha(int index) =>
        Clamp(CornerArcPeakAlpha * (1 - (CornerArcFalloff * CornerArcIndex(index))), 0, CornerArcAlphaCeiling);

    /// <summary>
    /// 第几条星轨此刻的呼吸系数：在 1−<see cref="CornerArcBreathDepth"/> 到 1 之间缓慢起伏，
    /// 渲染侧乘到 <see cref="CornerArcAlpha"/> 上。只压不抬，峰值永远不会顶穿天花板。
    /// </summary>
    /// <param name="index">星轨下标，越界会被夹进 0–(<see cref="CornerArcCount"/> − 1)。</param>
    /// <returns>[1 − <see cref="CornerArcBreathDepth"/>, 1] 之间的系数。</returns>
    public double CornerArcBreath(int index) =>
        BreathFactor(Time, CornerArcBreathPeriod, CornerArcBreathDepth, CornerArcIndex(index) * 0.7);

    /// <summary>右上角远景星轨族的呼吸系数：与左上角错开周期，两层不会一起明灭。</summary>
    /// <returns>[1 − <see cref="TrailBreathDepth"/>, 1] 之间的系数。</returns>
    public double TrailBreath() => BreathFactor(Time, TrailBreathPeriod, TrailBreathDepth, 0.9);

    /// <summary>此刻有多少颗流星正在飞：遍历固定 schedule 计数，不在 Render 里新建集合。</summary>
    /// <param name="time">动画时钟（秒）；非法值按 0 颗处理。</param>
    /// <returns>0–<see cref="MeteorScheduleCount"/> 之间的颗数。</returns>
    public int CountActiveMeteors(double time)
    {
        if (!double.IsFinite(time))
        {
            return 0;
        }

        var count = 0;
        for (var index = 0; index < _meteors.Length; index++)
        {
            if (IsActiveMeteor(index, time))
            {
                count++;
            }
        }

        return count;
    }

    /// <summary>
    /// 取第几颗流星在指定时刻的状态：头尾坐标均为归一化 0–1，渲染侧再乘窗口尺寸。
    /// </summary>
    /// <param name="index">流星下标，0–(<see cref="MeteorScheduleCount"/> − 1)。</param>
    /// <param name="time">动画时钟（秒）；负时间按周期整数倍折算，NaN/±∞ 直接返回 false。</param>
    /// <param name="state">在飞时给出头尾坐标与不透明度，否则为 <c>default</c>。</param>
    /// <returns>该流星此刻是否在活跃窗口内（相位落在 [0, 1)）。</returns>
    public bool TryGetMeteor(int index, double time, out MeteorState state)
    {
        state = default;
        if (!TryReadMeteor(index, time, out var meteor, out var phase))
        {
            return false;
        }

        var travel = phase * meteor.Length;
        var headX = meteor.StartX + (meteor.DirectionX * travel);
        var headY = meteor.StartY + (meteor.DirectionY * travel);
        var tail = MeteorTailRatio * travel;
        var headAlpha = meteor.PeakAlpha * Math.Pow(Math.Sin(Math.PI * phase), MeteorHeadAlphaPower);

        state = new MeteorState(
            headX - (meteor.DirectionX * tail),
            headY - (meteor.DirectionY * tail),
            headX,
            headY,
            headAlpha,
            headAlpha * MeteorTailAlphaRatio);
        return true;
    }

    /// <summary>
    /// 更新画布尺寸：把归一化坐标乘成像素，并重算光标影响。
    /// </summary>
    /// <param name="width">宽度（DIP），非法值按 0 处理。</param>
    /// <param name="height">高度（DIP），非法值按 0 处理。</param>
    public void Layout(double width, double height)
    {
        Width = SanitizeSize(width);
        Height = SanitizeSize(height);

        for (var i = 0; i < _stars.Count; i++)
        {
            _pixelX[i] = _stars[i].NormalizedX * Width;
            _pixelY[i] = _stars[i].NormalizedY * Height;
        }

        UpdateInfluence();
    }

    /// <summary>
    /// 把光标放到指定位置（像素）。位置会被夹进画布，非法值忽略。
    /// </summary>
    /// <param name="x">水平像素。</param>
    /// <param name="y">垂直像素。</param>
    public void SetPointer(double x, double y)
    {
        if (!double.IsFinite(x) || !double.IsFinite(y))
        {
            return;
        }

        _targetPointerX = Clamp(x, 0, Math.Max(0, Width));
        _targetPointerY = Clamp(y, 0, Math.Max(0, Height));
        _hasPointer = true;
    }

    /// <summary>光标离开窗口：牵引线与高亮开始淡出。</summary>
    public void ClearPointer() => _hasPointer = false;

    /// <summary>
    /// 把缓动直接收敛到目标值。动效关闭时用：星轨与闪烁停住，
    /// 但「指着哪里就亮哪里」这个状态必须立刻可见（动效分层的既有约定）。
    /// </summary>
    public void RefreshPointer()
    {
        _pointerX = _targetPointerX;
        _pointerY = _targetPointerY;
        _pointerStrength = _hasPointer ? 1 : 0;
        UpdateInfluence();
    }

    /// <summary>
    /// 推进一帧：时钟、光标缓动、影响场一起更新。
    /// </summary>
    /// <param name="deltaSeconds">距上一帧的秒数；非正值与 NaN 都当作不推进。</param>
    public void Advance(double deltaSeconds)
    {
        if (!double.IsFinite(deltaSeconds) || deltaSeconds <= 0)
        {
            return;
        }

        var step = Math.Min(deltaSeconds, MaxStepSeconds);
        Time += step;

        var ease = Math.Min(1d, step * PointerEaseRate);
        _pointerX += (_targetPointerX - _pointerX) * ease;
        _pointerY += (_targetPointerY - _pointerY) * ease;
        _pointerStrength += ((_hasPointer ? 1d : 0d) - _pointerStrength) * ease;
        UpdateInfluence();
    }

    /// <summary>星点的像素横坐标（不含光标偏移）。</summary>
    /// <param name="index">星点下标。</param>
    /// <returns>画布内的横坐标。</returns>
    public double StarX(int index) => _pixelX[index];

    /// <summary>星点的像素纵坐标（不含光标偏移）。</summary>
    /// <param name="index">星点下标。</param>
    /// <returns>画布内的纵坐标。</returns>
    public double StarY(int index) => _pixelY[index];

    /// <summary>星点当前半径（DIP）：基础半径 + 光标带来的少量放大。</summary>
    /// <param name="index">星点下标。</param>
    /// <returns>半径，始终为正。</returns>
    public double StarRadius(int index) => _stars[index].Radius + (_influence[index] * 0.6);

    /// <summary>星点当前不透明度：基础值 × 闪烁 × 光标高亮。</summary>
    /// <param name="index">星点下标。</param>
    /// <returns>0–1 之间的不透明度。</returns>
    public double StarAlpha(int index)
    {
        var star = _stars[index];
        var twinkle = 0.62 + (0.38 * Math.Sin((Time * star.TwinkleSpeed) + star.TwinklePhase));
        return Clamp(star.BaseAlpha * twinkle * (1 + (1.25 * _influence[index])), 0, 1);
    }

    /// <summary>星点被光标推开的横向偏移（像素）。</summary>
    /// <param name="index">星点下标。</param>
    /// <returns>带符号的偏移量，0 表示不受光标影响。</returns>
    public double StarOffsetX(int index) => _offsetX[index];

    /// <summary>星点被光标推开的纵向偏移（像素）。</summary>
    /// <param name="index">星点下标。</param>
    /// <returns>带符号的偏移量，0 表示不受光标影响。</returns>
    public double StarOffsetY(int index) => _offsetY[index];

    /// <summary>星点受光标影响的程度 0–1，渲染侧据此决定要不要加星芒。</summary>
    /// <param name="index">星点下标。</param>
    /// <returns>影响强度。</returns>
    public double StarInfluence(int index) => _influence[index];

    /// <summary>星座连线的当前不透明度：基础呼吸 + 光标命中节点带来的提亮。</summary>
    /// <param name="index">星座下标。</param>
    /// <returns>0–0.6 之间的不透明度，永不超过上限（否则细线会抢正文的注意力）。</returns>
    public double FigureAlpha(int index)
    {
        var figure = _figures[index];
        var breath = 0.5 + (0.5 * Math.Sin(((Time / figure.PeriodSeconds) * Math.Tau) + figure.Phase));
        var alpha = (0.14 + (0.26 * breath)) * (1 + (2 * _figureInfluence[index]));
        return Clamp(alpha, 0, 0.6);
    }

    /// <summary>星座整体受光标影响的程度 0–1（节点影响的均值）。</summary>
    /// <param name="index">星座下标。</param>
    /// <returns>影响强度。</returns>
    public double FigureInfluence(int index) => _figureInfluence[index];

    private static double SanitizeSize(double value) => double.IsFinite(value) && value > 0 ? value : 0;

    private static double Clamp(double value, double min, double max) =>
        double.IsNaN(value) ? min : Math.Clamp(value, min, max);

    /// <summary>在 [1 − depth, 1] 之间做正弦呼吸：只压不抬，亮度峰值不会被呼吸顶破天花板。</summary>
    /// <param name="time">动画时钟（秒）。</param>
    /// <param name="periodSeconds">呼吸周期（秒）；非正数按 1 秒处理，避免除零。</param>
    /// <param name="depth">起伏深度 0–1。</param>
    /// <param name="phase">相位（弧度），让每条弧/每个星座错开。</param>
    /// <returns>[1 − depth, 1] 之间的系数；时间非法时返回 1（静止在峰值）。</returns>
    private static double BreathFactor(double time, double periodSeconds, double depth, double phase)
    {
        if (!double.IsFinite(time) || !double.IsFinite(periodSeconds) || periodSeconds <= 0)
        {
            return 1;
        }

        var wave = 0.5 + (0.5 * Math.Sin((time / periodSeconds * Math.Tau) + phase));
        return 1 - (depth * (1 - wave));
    }

    /// <summary>给本次进程挑一个种子：同一进程内固定，换一次启动换一份星座。</summary>
    /// <returns>非零的 32 位种子。</returns>
    private static int CreateLaunchSeed()
    {
        var ticks = Environment.TickCount64;
        var mixed = unchecked((int)(ticks ^ (ticks >> 32)) ^ Random.Shared.Next());
        return mixed == 0 ? DefaultSeed ^ 0x2F1B3C5D : mixed;
    }

    private void BuildFigures(int figureCount, ref Rng rng)
    {
        // 允许星座部分超出屏幕边界，让星空在边缘更有延伸感与深邃感（-0.08 到 1.08）
        const double boundMin = -0.08;
        const double boundMax = 1.08;

        // 候选网格点生成，扩大分布空间并跳过左上角星轨强遮盖区
        var candidateCenters = new List<(double X, double Y)>();
        const int gridCols = 5;
        const int gridRows = 4;
        var stepX = (boundMax - boundMin) / gridCols;
        var stepY = (boundMax - boundMin) / gridRows;

        for (var c = 0; c < gridCols; c++)
        {
            for (var r = 0; r < gridRows; r++)
            {
                var cellCenterX = boundMin + ((c + 0.5) * stepX);
                var cellCenterY = boundMin + ((r + 0.5) * stepY);
                var distToCornerOrigin = Math.Sqrt(Math.Pow(cellCenterX + CornerPoleOffsetX, 2) + Math.Pow(cellCenterY + CornerPoleOffsetY, 2));

                // 彻底避让左上角星轨覆盖区：星轨最外层半径达 0.72，留足 0.82 安全距离，且扇区 (x < 0.55 且 y < 0.55) 均排除
                var inCornerTrailFan = cellCenterX < 0.55 && cellCenterY < 0.55 && distToCornerOrigin < 0.82;
                var inTrackingStationZone = cellCenterX > TrackingStationZoneMinX && cellCenterY > TrackingStationZoneMinY;

                if (!inCornerTrailFan && !inTrackingStationZone)
                {
                    candidateCenters.Add((cellCenterX, cellCenterY));
                }
            }
        }

        // 洗牌候选格子以保证离散且随机分布
        for (var i = candidateCenters.Count - 1; i > 0; i--)
        {
            var j = (int)(rng.NextDouble() * (i + 1)) % (i + 1);
            (candidateCenters[i], candidateCenters[j]) = (candidateCenters[j], candidateCenters[i]);
        }

        var chosenCount = Math.Min(figureCount, candidateCenters.Count);
        for (var f = 0; f < figureCount; f++)
        {
            var baseCenter = candidateCenters[f % candidateCenters.Count];
            // 在格内施加 ±0.35 步长的有机扰动，避免机械对齐感
            var jitterX = (rng.NextDouble() - 0.5) * stepX * 0.7;
            var jitterY = (rng.NextDouble() - 0.5) * stepY * 0.7;
            var centerX = baseCenter.X + jitterX;
            var centerY = baseCenter.Y + jitterY;

            var rotAngle = rng.NextDouble() * Math.Tau;
            var cosR = Math.Cos(rotAngle);
            var sinR = Math.Sin(rotAngle);
            var scaleX = 0.050 + (0.040 * rng.NextDouble());
            var scaleY = scaleX * (0.80 + (0.45 * rng.NextDouble()));

            var archetype = ConstellationArchetypes[(int)(rng.NextDouble() * ConstellationArchetypes.Length) % ConstellationArchetypes.Length];
            var nodes = new List<int>(NodesPerFigure);
            var nodeStart = _stars.Count;

            for (var i = 0; i < NodesPerFigure; i++)
            {
                var (lx, ly) = archetype.LocalCoords[i];
                // 适度微抖动，使同种形态每次生成依然具备独特性
                var jx = lx + ((rng.NextDouble() - 0.5) * 0.14);
                var jy = ly + ((rng.NextDouble() - 0.5) * 0.14);

                var px = (jx * scaleX * cosR) - (jy * scaleY * sinR);
                var py = (jx * scaleX * sinR) + (jy * scaleY * cosR);

                var x = Clamp(centerX + px, -0.12, 1.12);
                var y = Clamp(centerY + py, -0.12, 1.12);

                nodes.Add(_stars.Count);
                _stars.Add(new StarPoint(
                    x,
                    y,
                    1.10 + (0.55 * rng.NextDouble()),
                    0.52 + (0.25 * rng.NextDouble()),
                    rng.NextDouble() * Math.Tau,
                    0.5 + (0.7 * rng.NextDouble()),
                    rng.NextDouble() * 0.45));
            }

            // 整组星座平移出测控站避让区：连线引用不变，形态不会被裁断。
            KeepFigureClearOfTrackingStation(nodeStart);

            // 星座最低节点留出 10% 底部安全线：状态栏是半透明的，星座折线贴到窗口底边会像穿帮。
            // 只平移这颗星座自己的节点，连线引用不变，形态不会被拉坏。
            var lowestY = -1.0;
            for (var i = nodeStart; i < _stars.Count; i++)
            {
                lowestY = Math.Max(lowestY, _stars[i].NormalizedY);
            }

            var bottomOverflow = lowestY - 0.90;
            if (bottomOverflow > 0)
            {
                for (var i = nodeStart; i < _stars.Count; i++)
                {
                    var node = _stars[i];
                    _stars[i] = node with { NormalizedY = Math.Max(-0.12, node.NormalizedY - bottomOverflow) };
                }
            }

            _figures.Add(new ConstellationFigure(
                nodes,
                archetype.Edges,
                rng.NextDouble() * Math.Tau,
                8.5 + (3 * rng.NextDouble())));
        }
    }

    /// <summary>
    /// 把一组星座整体平移出右下角测控站的避让区。只移动这颗星座自己的节点，
    /// 连线仍引用原来的下标，所以星座形态不会被裁断或拉坏。
    /// </summary>
    /// <param name="nodeStart">该星座第一个节点在 <see cref="_stars"/> 中的下标。</param>
    private void KeepFigureClearOfTrackingStation(int nodeStart)
    {
        var minX = double.PositiveInfinity;
        var minY = double.PositiveInfinity;
        var maxX = double.NegativeInfinity;
        var maxY = double.NegativeInfinity;

        for (var i = nodeStart; i < _stars.Count; i++)
        {
            var node = _stars[i];
            minX = Math.Min(minX, node.NormalizedX);
            minY = Math.Min(minY, node.NormalizedY);
            maxX = Math.Max(maxX, node.NormalizedX);
            maxY = Math.Max(maxY, node.NormalizedY);
        }

        // 包围盒不碰避让区时，任何一条连线都不可能穿进去。
        if (maxX <= TrackingStationZoneMinX || minX >= TrackingStationZoneMaxX ||
            maxY <= TrackingStationZoneMinY || minY >= TrackingStationZoneMaxY)
        {
            return;
        }

        // 左移或上移取位移更小的一侧：尽量不把星座推离原本的构图位置。
        var shiftLeft = (maxX - TrackingStationZoneMinX) + TrackingStationZoneMargin;
        var shiftUp = (maxY - TrackingStationZoneMinY) + TrackingStationZoneMargin;

        if (shiftLeft <= shiftUp)
        {
            TranslateFigure(nodeStart, -shiftLeft, 0);
        }
        else
        {
            TranslateFigure(nodeStart, 0, -shiftUp);
        }
    }

    /// <summary>整组平移星座节点，并把越界坐标夹回允许的绘制范围。</summary>
    /// <param name="nodeStart">该星座第一个节点的下标。</param>
    /// <param name="dx">水平位移（归一化）。</param>
    /// <param name="dy">垂直位移（归一化）。</param>
    private void TranslateFigure(int nodeStart, double dx, double dy)
    {
        for (var i = nodeStart; i < _stars.Count; i++)
        {
            var node = _stars[i];
            _stars[i] = node with
            {
                NormalizedX = Clamp(node.NormalizedX + dx, -0.12, 1.12),
                NormalizedY = Clamp(node.NormalizedY + dy, -0.12, 1.12),
            };
        }
    }

    private void BuildAmbientStars(int count, ref Rng rng)
    {
        for (var i = 0; i < count; i++)
        {
            _stars.Add(new StarPoint(
                rng.NextDouble(),
                rng.NextDouble(),
                0.55 + (0.95 * rng.NextDouble()),
                0.16 + (0.46 * rng.NextDouble()),
                rng.NextDouble() * Math.Tau,
                0.25 + (1.2 * rng.NextDouble()),
                rng.NextDouble()));
        }
    }

    /// <summary>把星轨下标夹进 0–(<see cref="CornerArcCount"/> − 1)：公共方法容忍越界。</summary>
    /// <param name="index">原始下标。</param>
    /// <returns>0–8 之间的下标。</returns>
    private static int CornerArcIndex(int index) => Math.Clamp(index, 0, CornerArcCount - 1);

    /// <summary>第几颗流星此刻是否在飞：只判相位，不展开几何。</summary>
    /// <param name="index">流星下标。</param>
    /// <param name="time">动画时钟（秒）。</param>
    /// <returns>相位落在活跃窗口内时为 true。</returns>
    private bool IsActiveMeteor(int index, double time) => TryReadMeteor(index, time, out _, out _);

    /// <summary>解析第几颗流星此刻的相位：越界、时间非法、不在活跃窗口都返回 false。</summary>
    /// <param name="index">流星下标。</param>
    /// <param name="time">动画时钟（秒）。</param>
    /// <param name="meteor">schedule 里的静态参数，仅在返回 true 时有效。</param>
    /// <param name="phase">在飞进度 0–1（含 0、不含 1）。</param>
    /// <returns>是否在活跃窗口内。</returns>
    private bool TryReadMeteor(int index, double time, out MeteorSpec meteor, out double phase)
    {
        meteor = default;
        phase = 0;
        if ((uint)index >= (uint)_meteors.Length || !double.IsFinite(time))
        {
            return false;
        }

        var spec = _meteors[index];
        var progress = MeteorCyclePhase(time, spec.Period, spec.Offset) / MeteorTravelSeconds;
        if (progress >= 1)
        {
            return false;
        }

        meteor = spec;
        phase = progress;
        return true;
    }

    /// <summary>按固定种子排 12 颗流星的 schedule：两条相位通道 × 六槽位，窗口彼此不追尾。</summary>
    /// <param name="seed">星空主种子；与 <see cref="MeteorSeedSalt"/> 混合后单开一条随机流。</param>
    private void BuildMeteors(int seed)
    {
        // 独立 Rng：绝不动主随机流，否则星点与星座的布局会随流星一起漂移。
        var rng = new Rng(seed ^ MeteorSeedSalt);
        for (var index = 0; index < MeteorScheduleCount; index++)
        {
            var lane = index % MeteorLaneCount;
            var slot = index / MeteorLaneCount;
            var period = MeteorLanePeriods[lane] + (((rng.NextDouble() * 2) - 1) * MeteorPeriodJitter);
            var startX = -0.15 + rng.NextDouble();
            var startY = -0.20 + (0.55 * rng.NextDouble());
            var direction = 0.45 + (0.50 * rng.NextDouble());
            var length = 0.25 + (0.20 * rng.NextDouble());
            var peak = 0.30 + (0.25 * rng.NextDouble());
            var offset = index == 0 ? MeteorTravelSeconds / 3 : MeteorOffset(lane, slot, period);

            _meteors[index] = new MeteorSpec(
                startX,
                startY,
                Math.Cos(direction),
                Math.Sin(direction),
                length,
                peak,
                period,
                offset);
        }
    }

    /// <summary>把「这一槽位的首次出现时间」换算成相位偏移。</summary>
    /// <param name="lane">通道下标 0–(<see cref="MeteorLaneCount"/> − 1)。</param>
    /// <param name="slot">通道内槽位下标。</param>
    /// <param name="period">该颗流星的周期（秒）。</param>
    /// <returns>[0, period) 内的相位偏移。</returns>
    private static double MeteorOffset(int lane, int slot, double period)
    {
        var slotStart = MeteorLanePhases[lane] + (slot * period / MeteorLaneSlots);
        var cycle = slotStart % period;
        return cycle < 0 ? -cycle : period - cycle;
    }

    /// <summary>把时间折算进 [0, period)：负时间与超大时间都先归一化成非负。</summary>
    /// <param name="time">动画时钟（秒），调用方已保证有限。</param>
    /// <param name="period">流星周期（秒），恒为正。</param>
    /// <param name="offset">相位偏移（秒）。</param>
    /// <returns>[0, period) 内的周期位置。</returns>
    private static double MeteorCyclePhase(double time, double period, double offset)
    {
        var cycle = (time + offset) % period;
        return cycle < 0 ? cycle + period : cycle;
    }

    /// <summary>重算「谁受光标影响、影响多少、往哪躲、连哪几条牵引线」。</summary>
    private void UpdateInfluence()
    {
        Array.Clear(_influence);
        Array.Clear(_offsetX);
        Array.Clear(_offsetY);
        Array.Clear(_figureInfluence);
        _pointerLinks.Clear();

        if (_pointerStrength <= 0.01 || _stars.Count == 0)
        {
            return;
        }

        var radiusSquared = InfluenceRadius * InfluenceRadius;
        for (var i = 0; i < _stars.Count; i++)
        {
            var dx = _pixelX[i] - _pointerX;
            var dy = _pixelY[i] - _pointerY;
            var distanceSquared = (dx * dx) + (dy * dy);
            if (distanceSquared >= radiusSquared)
            {
                continue;
            }

            var distance = Math.Sqrt(distanceSquared);
            var falloff = 1 - (distance / InfluenceRadius);
            var influence = falloff * falloff * _pointerStrength;
            _influence[i] = influence;

            if (distance > 0.001)
            {
                var push = influence * influence * 10;
                _offsetX[i] = dx / distance * push;
                _offsetY[i] = dy / distance * push;
            }
        }

        _candidates.Clear();
        for (var f = 0; f < _figures.Count; f++)
        {
            var nodes = _figures[f].Nodes;
            var sum = 0d;
            for (var n = 0; n < nodes.Count; n++)
            {
                sum += _influence[nodes[n]];
            }

            _figureInfluence[f] = sum / Math.Max(1, nodes.Count);

            for (var n = 0; n < nodes.Count; n++)
            {
                var index = nodes[n];
                var dx = _pixelX[index] - _pointerX;
                var dy = _pixelY[index] - _pointerY;
                _candidates.Add((Math.Sqrt((dx * dx) + (dy * dy)), index));
            }
        }

        if (_pointerStrength <= LinkStrengthFloor)
        {
            return;
        }

        _candidates.Sort(static (left, right) => left.Distance.CompareTo(right.Distance));
        var take = Math.Min(PointerLinkCount, _candidates.Count);
        for (var i = 0; i < take; i++)
        {
            _pointerLinks.Add(_candidates[i].Index);
        }
    }

    /// <summary>
    /// 一颗流星排布完成后就不变的静态参数：起点、方向、行程长度、亮度峰值、周期与相位偏移。
    /// 逐帧只按 <paramref name="phase"/> 插值出 <see cref="MeteorState"/>，这里不存任何时间量。
    /// </summary>
    /// <param name="StartX">起点横坐标（归一化，允许落在画布外）。</param>
    /// <param name="StartY">起点纵坐标（归一化，允许落在画布外）。</param>
    /// <param name="DirectionX">方向单位向量的横向分量。</param>
    /// <param name="DirectionY">方向单位向量的纵向分量（向下为正）。</param>
    /// <param name="Length">整段行程长度（归一化单位，按画布对角线归一）。</param>
    /// <param name="PeakAlpha">头端不透明度峰值。</param>
    /// <param name="Period">绕行周期（秒）。</param>
    /// <param name="Offset">相位偏移（秒）。</param>
    private readonly record struct MeteorSpec(
        double StartX,
        double StartY,
        double DirectionX,
        double DirectionY,
        double Length,
        double PeakAlpha,
        double Period,
        double Offset);

    /// <summary>确定性 xorshift32：不依赖 <see cref="Random"/>，同一 seed 在任何平台上结果一致。</summary>
    private struct Rng(int seed)
    {
        private uint _state = seed == 0 ? 0x9E3779B9u : unchecked((uint)seed);

        public double NextDouble()
        {
            _state ^= _state << 13;
            _state ^= _state >> 17;
            _state ^= _state << 5;
            return _state / 4294967296d;
        }
    }
}
