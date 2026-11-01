using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Avalonia.Threading;
using CkanUI.App.Diagnostics;
using CkanUI.App.Theming;

namespace CkanUI.App.Controls;

/// <summary>
/// 星空背景：星轨弧线 + 闪烁星点 + 会呼吸的星座，左上角另有一族长曝光星轨、偶有流星划过；
/// 光标靠近时星点被推开、星座提亮并拉出牵引线。
/// </summary>
/// <remarks>
/// <para>
/// 纯自绘、不吃鼠标事件（<see cref="InputElement.IsHitTestVisible"/> 恒为 false），
/// 挂在 <c>MainWindow</c> 最底层的背景面板里，页面卡片之间的空隙与侧栏外面才看得到它。
/// 因为只透过缝隙露面，所有亮度都压在很低的量级：细线最高 0.6，背景星点最高 0.62。
/// </para>
/// <para>
/// 显示与否则交给 <c>BackgroundHost</c>：系统高对比度、或用户自选了背景图/渐变时隐藏（THM-10 优先）。
/// 动效关闭（<c>CkanMotionDurationBase</c> 为零）时不再推进时钟，星空停成一帧静态图，
/// 但指针高亮仍立刻生效——「状态变化必须仍然可感知」是动效分层的既有约定。
/// </para>
/// <para>
/// 自下而上的绘制顺序固定为：右上角远景星轨 → 左上角长曝光星轨 → 星座连线 → 光标牵引线 → 流星 → 星点。
/// 流星在最上层且不参与星座的可见性判断；左上角星轨的峰值不透明度封顶 0.13，不会压过正文。
/// </para>
/// </remarks>
public sealed class StarfieldBackground : Control
{
    /// <summary>星星与连线的透明度分档数：把连续透明度量化成可复用的画笔，避免每帧 new 画刷。</summary>
    private const int AlphaSteps = 32;

    /// <summary>星轨条数。</summary>
    private const int TrailArcCount = 4;

    /// <summary>每条星轨切成多少段（分段是为了做两端淡出）。</summary>
    private const int TrailSegments = 26;

    /// <summary>左上角星轨每条切成多少段：分段是为了两端淡出。</summary>
    private const int CornerArcSegments = 22;

    /// <summary>左上角星轨最内侧那条的线宽（DIP）：越靠外的弧越细。</summary>
    private const double CornerArcMaxWidth = 2.0;

    /// <summary>相邻两条星轨的线宽递减量（DIP）。</summary>
    private const double CornerArcWidthStep = 0.05;

    /// <summary>流星尾巴分几段：头到尾三段递减，尾段最淡。</summary>
    private const int MeteorTailSegments = 3;

    /// <summary>流星头段的线宽（DIP）：细到 1.2 才不糊，粗到 1.6 才有速度感。</summary>
    private const double MeteorHeadWidth = 1.6;

    /// <summary>流星尾段的线宽（DIP）。</summary>
    private const double MeteorTailWidth = 1.2;

    /// <summary>流星头部的亮点半径（DIP），直径 2px。</summary>
    private const double MeteorHeadCoreRadius = 1.0;

    /// <summary>动画帧间隔（毫秒）：30fps 足够，星空不需要 60fps 的入场券。</summary>
    private const double TickIntervalMs = 33;

    /// <summary>空闲降频后的帧间隔（毫秒）：对齐 <c>DynamicRefreshRateController.IdleTargetFps</c> 的 10fps。</summary>
    private const double IdleTickIntervalMs = 100;

    /// <summary>星轨层位图的重建步长（毫秒）：星轨 33ms 只挪 0.23px，百毫秒重建一次肉眼看不出差别。</summary>
    private const double TrailCacheQuantumMs = 100;

    /// <summary>亮色主题下整体压暗的比例：深色背景才撑得住「星空」，亮色下要更像纸面的暗纹。</summary>
    private const double LightThemeIntensity = 0.55;

    /// <summary>暖星的色偏目标（往这个方向拉一点，星点才不是一片死白）。</summary>
    private static readonly Color WarmTint = Color.Parse("#FFD7A8");

    private static readonly Color DefaultStarColor = Color.Parse("#E8ECF2");

    private static readonly Color DefaultLineColor = Color.Parse("#72A7FF");

    /// <summary>线宽档位表：星轨与流星的线宽只落在档位最近的这一格，画笔缓存才不会爆炸。</summary>
    private static readonly double[] StrokeWidths = [1.0, 1.2, 1.4, 1.6, 2.0];

    private readonly StarfieldModel _model = new();
    private readonly DispatcherTimer? _timer;
    private readonly Color[] _starColors = new Color[AlphaSteps + 1];
    private readonly Color[] _warmColors = new Color[AlphaSteps + 1];
    private readonly Color[] _lineColors = new Color[AlphaSteps + 1];
    private readonly SolidColorBrush?[] _starCool = new SolidColorBrush?[AlphaSteps + 1];
    private readonly SolidColorBrush?[] _starWarm = new SolidColorBrush?[AlphaSteps + 1];
    private readonly SolidColorBrush?[] _lineBrushes = new SolidColorBrush?[AlphaSteps + 1];
    private readonly Pen?[] _starPens = new Pen?[AlphaSteps + 1];
    private readonly Pen?[] _linePens = new Pen?[AlphaSteps + 1];

    /// <summary>按线宽档位 × alpha 档位缓存的 accent 画笔：星轨与流星线宽不同，才需要第二维。</summary>
    private readonly Pen?[,] _strokePens = new Pen?[StrokeWidths.Length, AlphaSteps + 1];

    /// <summary>两族星轨的离屏位图：重建一次贴一帧，替掉每帧 604 条 DrawLine。</summary>
    private RenderTargetBitmap? _trailLayer;

    /// <summary>当前后端给不出离屏位图（部分无头后端没有 render target）时置位，之后一直走逐帧线绘。</summary>
    private bool _trailLayerUnavailable;

    private long _trailStamp = long.MinValue;
    private double _trailIntensity = double.NaN;
    private Color _trailStarColor;
    private Color _trailLineColor;

    private Color _starColor;
    private Color _lineColor;
    private bool _paletteReady;
    private Size _laidOutSize;
    private bool _hasLaidOut;

    /// <summary>构造一个星空背景。默认隐藏——是否露面由 <c>BackgroundHost</c> 按设置与主题决定。</summary>
    public StarfieldBackground()
    {
        IsHitTestVisible = false;
        ClipToBounds = true;
        IsVisible = false;

        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(TickIntervalMs) };
        _timer.Tick += OnTick;
    }

    /// <summary>当前是否开着动效（<c>CkanMotionDurationBase</c> 不为零，或 token 还没注入）。</summary>
    public bool IsMotionEnabled =>
        !this.TryFindResource(MotionHost.BaseKey, out var value)
        || value is not TimeSpan duration
        || duration > TimeSpan.Zero;

    /// <summary>
    /// 把光标位置交给星空（相对本控件的坐标，DIP）。传 <c>null</c> 表示光标离开了窗口。
    /// </summary>
    /// <param name="position">光标位置；越界值由模型自己夹进画布。</param>
    public void TrackPointer(Point? position)
    {
        if (position is { } point)
        {
            _model.SetPointer(point.X, point.Y);
        }
        else
        {
            _model.ClearPointer();
        }

        // 动效关闭时没有逐帧缓动可依赖，高亮必须当场到位。
        if (!IsMotionEnabled)
        {
            _model.RefreshPointer();
        }

        InvalidateVisual();
    }

    /// <inheritdoc />
    public override void Render(DrawingContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var size = Bounds.Size;
        if (size.Width < 8 || size.Height < 8)
        {
            return;
        }

        if (!_hasLaidOut || _laidOutSize != size)
        {
            _model.Layout(size.Width, size.Height);
            _laidOutSize = size;
            _hasLaidOut = true;
        }

        var intensity = ActualThemeVariant == ThemeVariant.Light ? LightThemeIntensity : 1d;
        EnsurePalette(
            ResolveColor("CkanTextPrimaryBrush", DefaultStarColor),
            ResolveColor("CkanAccentBrush", DefaultLineColor));

        DrawStarTrails(context, size, intensity);
        DrawFigures(context, intensity);
        DrawPointerLinks(context, intensity);
        DrawMeteors(context, size, intensity);
        DrawStars(context, intensity);
    }

    /// <inheritdoc />
    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        // 空闲降频由全局输入控制器广播：挂上就订阅，离树就退订，别在看不见的地方继续烧 CPU。
        DynamicRefreshRateController.Instance.IdleStateChanged -= OnIdleStateChanged;
        DynamicRefreshRateController.Instance.IdleStateChanged += OnIdleStateChanged;
        UpdateTimerState();
    }

    /// <inheritdoc />
    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        DynamicRefreshRateController.Instance.IdleStateChanged -= OnIdleStateChanged;
        ReleaseTrailLayer();
        base.OnDetachedFromVisualTree(e);
        UpdateTimerState();
    }

    /// <inheritdoc />
    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        ArgumentNullException.ThrowIfNull(change);
        base.OnPropertyChanged(change);

        if (change.Property == IsVisibleProperty)
        {
            UpdateTimerState();
        }
    }

    private void UpdateTimerState()
    {
        if (_timer is null)
        {
            return;
        }

        // 隐藏时停表：高对比度或用户自选背景期间不该继续占 30fps 的 CPU。
        _timer.IsEnabled = VisualRoot is not null && IsVisible;
        ApplyTickInterval();
    }

    /// <summary>
    /// 按空闲状态调整帧间隔：活跃期 30fps，无输入 2.5 秒后降到 10fps。
    /// 星轨每秒只走十几像素，降频后肉眼分不出，但空转 CPU 差三倍。
    /// </summary>
    private void ApplyTickInterval()
    {
        if (_timer is null)
        {
            return;
        }

        var interval = DynamicRefreshRateController.Instance.IsIdle ? IdleTickIntervalMs : TickIntervalMs;
        if (Math.Abs(_timer.Interval.TotalMilliseconds - interval) > 0.5)
        {
            _timer.Interval = TimeSpan.FromMilliseconds(interval);
        }
    }

    private void OnIdleStateChanged(bool isIdle) => ApplyTickInterval();

    private void OnTick(object? sender, EventArgs e)
    {
        if (!IsMotionEnabled || TopLevel.GetTopLevel(this) is Window { IsActive: false })
        {
            return;
        }

        // 推进真实经过的时长而不是固定 33ms：降频只该减少帧数，不该让星空整体变慢。
        _model.Advance((_timer?.Interval.TotalMilliseconds ?? TickIntervalMs) / 1000d);
        InvalidateVisual();
    }

    /// <summary>
    /// 两族星轨先烘进一张离屏位图，这一帧只贴图。
    /// 星轨角速度约 0.014 rad/s，一帧只挪 0.23px，却占了每帧 604 条 DrawLine ——
    /// 拿全窗重绘换看不见的位移，是纯粹的 CPU 白烧。缓存键 = 尺寸 × 缩放 × 主题强度 × 调色板 × 量化到
    /// <see cref="TrailCacheQuantumMs"/> 的模型时间；任何一项变了才重建。
    /// </summary>
    /// <param name="context">当前帧的绘制上下文。</param>
    /// <param name="size">控件尺寸（DIP）。</param>
    /// <param name="intensity">主题强度系数。</param>
    private void DrawStarTrails(DrawingContext context, Size size, double intensity)
    {
        if (_trailLayerUnavailable)
        {
            DrawTrails(context, size, intensity);
            DrawCornerTrails(context, size, intensity);
            return;
        }

        var stamp = (long)(_model.Time * 1000d / TrailCacheQuantumMs);
        if (_trailLayer is null
            || stamp != _trailStamp
            || _trailIntensity != intensity
            || _trailStarColor != _starColor
            || _trailLineColor != _lineColor)
        {
            if (!TryRenderTrailLayer(size, intensity))
            {
                DrawTrails(context, size, intensity);
                DrawCornerTrails(context, size, intensity);
                return;
            }

            _trailStamp = stamp;
            _trailIntensity = intensity;
            _trailStarColor = _starColor;
            _trailLineColor = _lineColor;
        }

        if (_trailLayer is { } layer)
        {
            context.DrawImage(layer, new Rect(size));
        }
    }

    /// <summary>
    /// 把星轨画进离屏层。
    /// </summary>
    /// <param name="size">控件尺寸（DIP）。</param>
    /// <param name="intensity">主题强度系数。</param>
    /// <returns>离屏层可用并已画好时为 <c>true</c>；后端给不出位图时返回 <c>false</c>，由调用方退回逐帧线绘。</returns>
    private bool TryRenderTrailLayer(Size size, double intensity)
    {
        var scaling = Math.Max(1d, TopLevel.GetTopLevel(this)?.RenderScaling ?? 1d);
        var pixels = new PixelSize(
            Math.Max(1, (int)Math.Ceiling(size.Width * scaling)),
            Math.Max(1, (int)Math.Ceiling(size.Height * scaling)));

        try
        {
            if (_trailLayer is null || _trailLayer.PixelSize != pixels)
            {
                ReleaseTrailLayer();
                _trailLayer = new RenderTargetBitmap(pixels);
            }

            // CreateDrawingContext 会先把整层清成透明，重建即全量，不用手动擦。
            using var layerContext = _trailLayer.CreateDrawingContext();
            if (scaling > 1d)
            {
                // 位图按物理像素开、绘制坐标仍是 DIP：这一层缩放把两者对齐，高 DPI 下细线才不会糊。
                using var scale = layerContext.PushTransform(Matrix.CreateScale(scaling, scaling));
                DrawTrails(layerContext, size, intensity);
                DrawCornerTrails(layerContext, size, intensity);
            }
            else
            {
                DrawTrails(layerContext, size, intensity);
                DrawCornerTrails(layerContext, size, intensity);
            }

            return true;
        }
        catch (InvalidOperationException)
        {
            _trailLayerUnavailable = true;
        }
        catch (NotSupportedException)
        {
            _trailLayerUnavailable = true;
        }
        catch (ArgumentException)
        {
            _trailLayerUnavailable = true;
        }

        ReleaseTrailLayer();
        return false;
    }

    private void ReleaseTrailLayer()
    {
        _trailLayer?.Dispose();
        _trailLayer = null;
        _trailStamp = long.MinValue;
    }

    private void DrawTrails(DrawingContext context, Size size, double intensity)
    {
        // 虚拟极点放在画布右上角外：所有星轨都是绕它的同心弧，扫过来的那一截才落在窗内。
        var poleX = size.Width * 1.06;
        var poleY = -size.Height * 0.22;
        var unit = Math.Min(size.Width, size.Height);

        for (var arc = 0; arc < TrailArcCount; arc++)
        {
            var radius = unit * (1.05 - (arc * 0.18));
            var direction = (arc % 2) == 0 ? 1 : -1;
            var start = 2.18 + (_model.Time * 0.014 * direction) + (arc * 0.06);
            var sweep = 0.42 + (arc * 0.05);
            var peak = 0.125 * (1 - (arc * 0.16)) * intensity * _model.TrailBreath();

            for (var segment = 0; segment < TrailSegments; segment++)
            {
                var t0 = (double)segment / TrailSegments;
                var t1 = (double)(segment + 1) / TrailSegments;
                var fade = Math.Sin(Math.PI * (t0 + t1) * 0.5);
                var alpha = peak * Math.Max(0, fade);
                if (alpha < 0.002)
                {
                    continue;
                }

                var pen = LinePen(AlphaIndex(alpha));
                var from = PointOnCircle(poleX, poleY, radius, start + (sweep * t0));
                var to = PointOnCircle(poleX, poleY, radius, start + (sweep * t1));
                context.DrawLine(StrokePen(StrokeLevel(2.4), AlphaIndex(alpha * 0.28)), from, to);
                context.DrawLine(pen, from, to);
            }
        }
    }

    /// <summary>
    /// 左上角那族延时星轨：极点略微落在窗外左上，九条同心弧从极点方向扫出来。
    /// 与右上角的 <see cref="DrawTrails"/> 互不影响——那族是远景，这族才是长曝光的主角。
    /// </summary>
    private void DrawCornerTrails(DrawingContext context, Size size, double intensity)
    {
        if (intensity <= 0)
        {
            return;
        }

        var poleX = _model.CornerPoleX(size.Width);
        var poleY = _model.CornerPoleY(size.Height);
        var unit = Math.Min(size.Width, size.Height);
        for (var arc = 0; arc < StarfieldModel.CornerArcCount; arc++)
        {
            var radius = _model.CornerArcRadius(arc, unit);
            var start = _model.CornerArcStart(arc);
            var sweep = _model.CornerArcSweep(arc);
            // 呼吸系数只压不抬：峰值永远不会顶穿 CornerArcAlphaCeiling。
            var peak = _model.CornerArcAlpha(arc) * _model.CornerArcBreath(arc) * intensity;
            if (peak < 0.002)
            {
                continue;
            }

            // 线宽随 index 递减，但只落在 StrokeWidths 的档位上：画笔缓存是有界的。
            var level = StrokeLevel(CornerArcMaxWidth - (arc * CornerArcWidthStep));
            var haloLevel = Math.Min(level + 1, StrokeWidths.Length - 1);
            for (var segment = 0; segment < CornerArcSegments; segment++)
            {
                var t0 = (double)segment / CornerArcSegments;
                var t1 = (double)(segment + 1) / CornerArcSegments;
                var fade = Math.Sin(Math.PI * (t0 + t1) * 0.5);
                var alpha = peak * Math.Max(0, fade);
                if (alpha < 0.002)
                {
                    continue;
                }

                var from = PointOnCircle(poleX, poleY, radius, start + (sweep * t0));
                var to = PointOnCircle(poleX, poleY, radius, start + (sweep * t1));

                // 宽而淡的一层垫底：单像素细线在 retina 上会被抗锯齿吃掉一半，加这层才像长曝光。
                context.DrawLine(StrokePen(haloLevel, AlphaIndex(alpha * 0.30)), from, to);
                context.DrawLine(StrokePen(level, AlphaIndex(alpha)), from, to);
            }
        }
    }

    /// <summary>
    /// 流星：遍历固定 schedule，头到尾分三段递减不透明度，头部再点一个亮点。
    /// 画在最上层装饰，不参与星座的可见性判断——流星划过时本来就该盖住底下的东西。
    /// </summary>
    private void DrawMeteors(DrawingContext context, Size size, double intensity)
    {
        if (intensity <= 0)
        {
            return;
        }

        var canvasWidth = size.Width;
        var canvasHeight = size.Height;
        for (var index = 0; index < StarfieldModel.MeteorScheduleCount; index++)
        {
            if (!_model.TryGetMeteor(index, _model.Time, out var meteor))
            {
                continue;
            }

            var head = new Point(meteor.HeadX * canvasWidth, meteor.HeadY * canvasHeight);
            var tail = new Point(meteor.TailX * canvasWidth, meteor.TailY * canvasHeight);
            var headAlpha = meteor.HeadAlpha * intensity;
            var tailAlpha = meteor.TailAlpha * intensity;
            if (headAlpha < 0.002)
            {
                continue;
            }

            for (var segment = 0; segment < MeteorTailSegments; segment++)
            {
                var from = (double)segment / MeteorTailSegments;
                var to = (double)(segment + 1) / MeteorTailSegments;
                var middle = (from + to) * 0.5;
                context.DrawLine(
                    StrokePen(StrokeLevel(MeteorHeadWidth + ((MeteorTailWidth - MeteorHeadWidth) * middle)),
                        AlphaIndex(headAlpha + ((tailAlpha - headAlpha) * middle))),
                    Between(head, tail, from),
                    Between(head, tail, to));
            }

            // 头部亮点：用 accent 画笔当填充，直径 2px，只点一下不铺光晕。
            context.DrawEllipse(
                LineBrush(AlphaIndex(headAlpha)),
                null,
                head,
                MeteorHeadCoreRadius,
                MeteorHeadCoreRadius);
        }
    }

    private void DrawFigures(DrawingContext context, double intensity)
    {
        for (var f = 0; f < _model.FigureCount; f++)
        {
            var alpha = _model.FigureAlpha(f) * intensity;
            if (alpha < 0.01)
            {
                continue;
            }

            var figure = _model.Figures[f];
            var core = StrokePen(StrokeLevel(1.4), AlphaIndex(alpha));
            var halo = StrokePen(StrokeLevel(2.4), AlphaIndex(alpha * 0.26));
            for (var e = 0; e < figure.Edges.Count; e++)
            {
                var edge = figure.Edges[e];
                var from = NodePoint(figure.Nodes[edge.From]);
                var to = NodePoint(figure.Nodes[edge.To]);

                // 与星轨同一套做法：先铺一层宽而淡的，再压一条细芯，线条才不至于"看不见"。
                context.DrawLine(halo, from, to);
                context.DrawLine(core, from, to);
            }
        }
    }

    private void DrawPointerLinks(DrawingContext context, double intensity)
    {
        if (!_model.HasPointer || _model.PointerStrength < 0.05)
        {
            return;
        }

        var origin = new Point(_model.PointerX, _model.PointerY);
        var strength = _model.PointerStrength;
        var pen = LinePen(AlphaIndex(0.2 * strength * intensity));
        foreach (var index in _model.PointerLinks)
        {
            context.DrawLine(pen, origin, NodePoint(index));
        }

        context.DrawEllipse(
            null,
            LinePen(AlphaIndex(0.24 * strength * intensity)),
            origin,
            5,
            5);
    }

    private void DrawStars(DrawingContext context, double intensity)
    {
        for (var i = 0; i < _model.StarCount; i++)
        {
            var alpha = _model.StarAlpha(i) * intensity;
            if (alpha < 0.012)
            {
                continue;
            }

            var radius = _model.StarRadius(i);
            var center = new Point(
                _model.StarX(i) + _model.StarOffsetX(i),
                _model.StarY(i) + _model.StarOffsetY(i));
            var index = AlphaIndex(alpha);
            context.DrawEllipse(StarBrush(index, _model.Stars[i].Warmth > 0.55), null, center, radius, radius);

            // 星芒只给「本来就亮」和「正被指着」的星点，否则整片星空会糊成一层噪点。
            if (radius < 1.5 && _model.StarInfluence(i) <= 0.4)
            {
                continue;
            }

            var pen = StarPen(AlphaIndex(alpha * 0.7));
            var arm = radius * 3.2;
            context.DrawLine(pen, new Point(center.X - arm, center.Y), new Point(center.X + arm, center.Y));
            context.DrawLine(pen, new Point(center.X, center.Y - arm), new Point(center.X, center.Y + arm));
        }
    }

    private Point NodePoint(int index) => new(
        _model.StarX(index) + _model.StarOffsetX(index),
        _model.StarY(index) + _model.StarOffsetY(index));

    private static Point PointOnCircle(double centerX, double centerY, double radius, double angle) =>
        new(centerX + (Math.Cos(angle) * radius), centerY + (Math.Sin(angle) * radius));

    private static int AlphaIndex(double alpha) =>
        (int)Math.Clamp(Math.Round(alpha * AlphaSteps, MidpointRounding.AwayFromZero), 0, AlphaSteps);

    private Color ResolveColor(string key, Color fallback) =>
        this.TryFindResource(key, out var value) && value is ISolidColorBrush brush ? brush.Color : fallback;

    private void EnsurePalette(Color star, Color line)
    {
        if (_paletteReady && _starColor == star && _lineColor == line)
        {
            return;
        }

        _starColor = star;
        _lineColor = line;

        for (var i = 0; i <= AlphaSteps; i++)
        {
            var byteAlpha = (byte)Math.Round(i * 255d / AlphaSteps);
            _starColors[i] = Color.FromArgb(byteAlpha, star.R, star.G, star.B);
            _warmColors[i] = Color.FromArgb(
                byteAlpha,
                Blend(star.R, WarmTint.R),
                Blend(star.G, WarmTint.G),
                Blend(star.B, WarmTint.B));
            _lineColors[i] = Color.FromArgb(byteAlpha, line.R, line.G, line.B);
        }

        // 只改已有画笔的颜色，不重建对象：画笔一旦交给渲染线程，替换实例会拿到已经用完的那个。
        for (var i = 0; i <= AlphaSteps; i++)
        {
            if (_starCool[i] is { } cool)
            {
                cool.Color = _starColors[i];
            }

            if (_starWarm[i] is { } warm)
            {
                warm.Color = _warmColors[i];
            }

            if (_lineBrushes[i] is { } lineBrush)
            {
                lineBrush.Color = _lineColors[i];
            }
        }

        _paletteReady = true;
    }

    private static byte Blend(byte from, byte to) => (byte)(from + ((to - from) * 0.45));

    private SolidColorBrush StarBrush(int index, bool warm)
    {
        var cache = warm ? _starWarm : _starCool;
        return cache[index] ??= new SolidColorBrush((warm ? _warmColors : _starColors)[index]);
    }

    private SolidColorBrush LineBrush(int index) => _lineBrushes[index] ??= new SolidColorBrush(_lineColors[index]);

    private Pen StarPen(int index) => _starPens[index] ??= new Pen(StarBrush(index, false), 1);

    private Pen LinePen(int index) => _linePens[index] ??= new Pen(LineBrush(index), 1);

    /// <summary>把任意线宽夹到 <see cref="StrokeWidths"/> 的最近档位。</summary>
    /// <param name="width">期望线宽（DIP）。</param>
    /// <returns>档位下标。</returns>
    private static int StrokeLevel(double width) =>
        (int)Math.Clamp(
            Math.Round((width - StrokeWidths[0]) / (StrokeWidths[1] - StrokeWidths[0])),
            0,
            StrokeWidths.Length - 1);

    /// <summary>取线宽档位 × alpha 档位的 accent 画笔：命中缓存后一帧也不新建对象。</summary>
    /// <param name="level"><see cref="StrokeLevel"/> 得到的档位下标。</param>
    /// <param name="index"><see cref="AlphaIndex"/> 得到的 alpha 档位。</param>
    /// <returns>可复用的画笔。</returns>
    private Pen StrokePen(int level, int index) =>
        _strokePens[level, index] ??= new Pen(LineBrush(index), StrokeWidths[level]);

    /// <summary>在两点之间按归一化进度插值，流星分段用。</summary>
    /// <param name="from">起点。</param>
    /// <param name="to">终点。</param>
    /// <param name="progress">0–1 的进度。</param>
    /// <returns>插值后的点。</returns>
    private static Point Between(Point from, Point to, double progress) =>
        new(
            from.X + ((to.X - from.X) * progress),
            from.Y + ((to.Y - from.Y) * progress));
}
