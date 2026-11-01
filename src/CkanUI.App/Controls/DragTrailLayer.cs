using System;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using CkanUI.App.Theming;

namespace CkanUI.App.Controls;

/// <summary>
/// 拖动细线的渲染层：把 <see cref="DragTrailModel"/> 的采样点连成一条 1.6px 的强调色细线，
/// 并在划线轨迹上按随机分布渲染 KSP 轨道节点（Ap, Pe, An, Dn）。
/// 包含差异化小角标、弹性弹出动效及平滑淡出。
/// </summary>
public sealed class DragTrailLayer : Control
{
    private const double TickIntervalMs = 16d;
    private const double CoreThickness = 1.6d;
    private const double TailThickness = 0.8d;
    private const double PeakAlpha = 0.62d;
    private const int AlphaSteps = 32;

    private static readonly Color DefaultAccent = Color.Parse("#72A7FF");
    private static readonly Color PeColor = Color.Parse("#38BDF8");      // 近拱点：天青蓝
    private static readonly Color AnColor = Color.Parse("#22C55E");      // 升交点：翡翠绿
    private static readonly Color DnColor = Color.Parse("#F59E0B");      // 降交点：琥珀金
    private static readonly Color BadgeBg = Color.Parse("#0D1424");      // 深空半透明底色

    private static readonly Typeface NodeFont = new(FontFamily.Default, FontStyle.Normal, FontWeight.Bold);

    private readonly DragTrailModel _model = new();
    private readonly DispatcherTimer _timer;

    private readonly FormattedText?[] _textAp = new FormattedText?[AlphaSteps + 1];
    private readonly FormattedText?[] _textPe = new FormattedText?[AlphaSteps + 1];
    private readonly FormattedText?[] _textAn = new FormattedText?[AlphaSteps + 1];
    private readonly FormattedText?[] _textDn = new FormattedText?[AlphaSteps + 1];

    private readonly Pen?[] _apRingPens = new Pen?[AlphaSteps + 1];
    private readonly Pen?[] _peRingPens = new Pen?[AlphaSteps + 1];
    private readonly Pen?[] _anRingPens = new Pen?[AlphaSteps + 1];
    private readonly Pen?[] _dnRingPens = new Pen?[AlphaSteps + 1];

    private readonly SolidColorBrush?[] _apBadgeBgBrushes = new SolidColorBrush?[AlphaSteps + 1];
    private readonly SolidColorBrush?[] _peBadgeBgBrushes = new SolidColorBrush?[AlphaSteps + 1];
    private readonly SolidColorBrush?[] _anBadgeBgBrushes = new SolidColorBrush?[AlphaSteps + 1];
    private readonly SolidColorBrush?[] _dnBadgeBgBrushes = new SolidColorBrush?[AlphaSteps + 1];

    private readonly SolidColorBrush?[] _anChevronBrushes = new SolidColorBrush?[AlphaSteps + 1];
    private readonly SolidColorBrush?[] _dnChevronBrushes = new SolidColorBrush?[AlphaSteps + 1];

    private Color _accent;
    private bool _paletteReady;

    public DragTrailLayer()
    {
        IsHitTestVisible = false;
        ClipToBounds = false;

        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(TickIntervalMs) };
        _timer.Tick += OnTick;
    }

    public bool IsMotionEnabled =>
        !this.TryFindResource(MotionHost.BaseKey, out var value)
        || value is not TimeSpan duration
        || duration > TimeSpan.Zero;

    public bool IsDragging => _model.IsDragging;
    public double Intensity => _model.Intensity;
    public int SampleCount => _model.Count;
    public System.Collections.Generic.IReadOnlyList<OrbitalNodeMarker> Nodes => _model.Nodes;

    public void Begin(Point position)
    {
        if (!IsMotionEnabled)
        {
            return;
        }

        _model.Begin(position.X, position.Y);
        UpdateTimerState();
        InvalidateVisual();
    }

    public void Move(Point position)
    {
        _model.Move(position.X, position.Y);
        UpdateTimerState();
        InvalidateVisual();
    }

    public void End()
    {
        _model.End();
        UpdateTimerState();
        InvalidateVisual();
    }

    public void Clear()
    {
        _model.Clear();
        UpdateTimerState();
        InvalidateVisual();
    }

    public override void Render(DrawingContext context)
    {
        var count = _model.Count;
        if (count < 2)
        {
            return;
        }

        var accent = ResolveAccent();
        var intensity = _model.Intensity;
        if (intensity <= 0d)
        {
            return;
        }

        EnsurePalette(accent);

        var last = count - 1;
        for (var i = 0; i < last; i++)
        {
            var t = (i + 1d) / last;
            var width = TailThickness + (CoreThickness - TailThickness) * t;
            var alpha = PeakAlpha * intensity * (0.35d + 0.65d * t);
            var pen = new Pen(new SolidColorBrush(accent, alpha), width, lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round);
            context.DrawLine(pen, new Point(_model.PointX(i), _model.PointY(i)), new Point(_model.PointX(i + 1), _model.PointY(i + 1)));
        }

        RenderOrbitalNodes(context, intensity);
    }

    private void RenderOrbitalNodes(DrawingContext context, double globalIntensity)
    {
        var nodes = _model.Nodes;
        if (nodes.Count == 0)
        {
            return;
        }

        for (var i = 0; i < nodes.Count; i++)
        {
            var node = nodes[i];
            var scale = node.PopScale;
            var alpha = Math.Clamp(globalIntensity * node.PopAlpha, 0d, 1d);
            var alphaIndex = AlphaIndex(alpha);

            if (alphaIndex <= 0 || scale <= 0.01d)
            {
                continue;
            }

            var center = new Point(node.X, node.Y);

            // 围绕节点中心做弹性弹出缩放 (0..1.15..1.0)
            using (context.PushTransform(
                Matrix.CreateTranslation(-center.X, -center.Y) *
                Matrix.CreateScale(scale, scale) *
                Matrix.CreateTranslation(center.X, center.Y)))
            {
                switch (node.Type)
                {
                    case OrbitalNodeType.Ap:
                        DrawApsidalNode(context, center, "Ap", _apRingPens[alphaIndex], _apBadgeBgBrushes[alphaIndex], _textAp[alphaIndex], isApoapsis: true);
                        break;
                    case OrbitalNodeType.Pe:
                        DrawApsidalNode(context, center, "Pe", _peRingPens[alphaIndex], _peBadgeBgBrushes[alphaIndex], _textPe[alphaIndex], isApoapsis: false);
                        break;
                    case OrbitalNodeType.An:
                        DrawNodalNode(context, center, "An", _anRingPens[alphaIndex], _anBadgeBgBrushes[alphaIndex], _textAn[alphaIndex], _anChevronBrushes[alphaIndex], isAscending: true);
                        break;
                    case OrbitalNodeType.Dn:
                        DrawNodalNode(context, center, "Dn", _dnRingPens[alphaIndex], _dnBadgeBgBrushes[alphaIndex], _textDn[alphaIndex], _dnChevronBrushes[alphaIndex], isAscending: false);
                        break;
                }
            }
        }
    }

    /// <summary>
    /// 渲染远/近拱点（Ap / Pe）：经典圆形轨道准星标靶 + 引线 + 胶囊状文字小角标。
    /// </summary>
    private static void DrawApsidalNode(
        DrawingContext context,
        Point center,
        string label,
        Pen? pen,
        SolidColorBrush? bgBrush,
        FormattedText? text,
        bool isApoapsis)
    {
        if (pen is null) return;

        // 1. 中心标靶：圆形小靶标
        context.DrawEllipse(null, pen, center, 3.2d, 3.2d);
        context.DrawEllipse(pen.Brush, null, center, 1.0d, 1.0d);

        // 远拱点 Ap 在顶部加一个微小指示弧，近拱点 Pe 在底部加一个微小指示弧
        var tickY = isApoapsis ? center.Y - 5.5d : center.Y + 5.5d;
        context.DrawLine(pen, new Point(center.X - 2.5d, tickY), new Point(center.X + 2.5d, tickY));

        // 2. 引线：连接准星与右上角角标
        var badgeOrigin = new Point(center.X + 7.0d, center.Y - 17.0d);
        context.DrawLine(pen, new Point(center.X + 2.5d, center.Y - 2.5d), new Point(badgeOrigin.X, badgeOrigin.Y + 7.0d));

        // 3. 胶囊小角标 (Badge)
        var badgeRect = new Rect(badgeOrigin.X, badgeOrigin.Y, 22.0d, 13.0d);
        if (bgBrush is not null)
        {
            context.DrawRectangle(bgBrush, pen, badgeRect, 3.0d, 3.0d);
        }

        // 4. 文字
        if (text is not null)
        {
            context.DrawText(text, new Point(badgeOrigin.X + 3.5d, badgeOrigin.Y + 0.5d));
        }
    }

    /// <summary>
    /// 渲染交点（An / Dn）：菱形交点标靶 + 几何三角箭头小角标 (▲/▼)，形成与拱点鲜明的差异化。
    /// </summary>
    private static void DrawNodalNode(
        DrawingContext context,
        Point center,
        string label,
        Pen? pen,
        SolidColorBrush? bgBrush,
        FormattedText? text,
        SolidColorBrush? chevronBrush,
        bool isAscending)
    {
        if (pen is null) return;

        // 1. 菱形靶标
        const double d = 3.6d;
        var pTop = new Point(center.X, center.Y - d);
        var pRight = new Point(center.X + d, center.Y);
        var pBottom = new Point(center.X, center.Y + d);
        var pLeft = new Point(center.X - d, center.Y);

        context.DrawLine(pen, pTop, pRight);
        context.DrawLine(pen, pRight, pBottom);
        context.DrawLine(pen, pBottom, pLeft);
        context.DrawLine(pen, pLeft, pTop);
        context.DrawEllipse(pen.Brush, null, center, 1.0d, 1.0d);

        // 2. 引线
        var badgeOrigin = new Point(center.X + 8.0d, center.Y - 17.0d);
        context.DrawLine(pen, new Point(center.X + 3.0d, center.Y - 3.0d), new Point(badgeOrigin.X, badgeOrigin.Y + 7.0d));

        // 3. 带三角切角的小角标 (Badge)
        var badgeRect = new Rect(badgeOrigin.X, badgeOrigin.Y, 26.0d, 13.0d);
        if (bgBrush is not null)
        {
            context.DrawRectangle(bgBrush, pen, badgeRect, 2.0d, 2.0d);
        }

        // 4. 小角标三角箭头符号 (An ▲ 升交 / Dn ▼ 降交)
        if (chevronBrush is not null)
        {
            var iconX = badgeOrigin.X + 18.0d;
            var iconY = badgeOrigin.Y + 6.5d;
            if (isAscending)
            {
                // ▲ 升交箭头
                context.DrawLine(pen, new Point(iconX - 2.5d, iconY + 2.0d), new Point(iconX, iconY - 2.5d));
                context.DrawLine(pen, new Point(iconX, iconY - 2.5d), new Point(iconX + 2.5d, iconY + 2.0d));
            }
            else
            {
                // ▼ 降交箭头
                context.DrawLine(pen, new Point(iconX - 2.5d, iconY - 2.5d), new Point(iconX, iconY + 2.0d));
                context.DrawLine(pen, new Point(iconX, iconY + 2.0d), new Point(iconX + 2.5d, iconY - 2.5d));
            }
        }

        // 5. 文字
        if (text is not null)
        {
            context.DrawText(text, new Point(badgeOrigin.X + 3.0d, badgeOrigin.Y + 0.5d));
        }
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        _timer.Stop();
    }

    private void UpdateTimerState()
    {
        // 若有正在进行的动效（弹出动画或淡出动画），保持 Timer 运转驱动平滑重绘
        if (_model.HasActiveAnimations)
        {
            if (VisualRoot is not null && !_timer.IsEnabled)
            {
                _timer.Start();
            }
        }
        else
        {
            _timer.Stop();
        }
    }

    private void OnTick(object? sender, EventArgs e)
    {
        _model.Advance(TickIntervalMs / 1000d);
        UpdateTimerState();
        InvalidateVisual();
    }

    private Color ResolveAccent() =>
        this.TryFindResource("CkanAccentBrush", out var value) && value is ISolidColorBrush brush
            ? brush.Color
            : DefaultAccent;

    private static int AlphaIndex(double alpha) =>
        (int)Math.Clamp(Math.Round(alpha * AlphaSteps, MidpointRounding.AwayFromZero), 0, AlphaSteps);

    private void EnsurePalette(Color accent)
    {
        if (_paletteReady && _accent == accent)
        {
            return;
        }

        _accent = accent;

        for (var i = 0; i <= AlphaSteps; i++)
        {
            double stepAlpha = (double)i / AlphaSteps;
            byte a = (byte)Math.Clamp(Math.Round(stepAlpha * 255d), 0, 255);
            byte bgA = (byte)Math.Clamp(Math.Round(stepAlpha * 210d), 0, 255);

            // 拱点配色：Ap (Accent), Pe (Sky Blue)
            var cAp = Color.FromArgb(a, accent.R, accent.G, accent.B);
            var cPe = Color.FromArgb(a, PeColor.R, PeColor.G, PeColor.B);

            // 节点配色：An (Emerald Green), Dn (Amber Gold)
            var cAn = Color.FromArgb(a, AnColor.R, AnColor.G, AnColor.B);
            var cDn = Color.FromArgb(a, DnColor.R, DnColor.G, DnColor.B);

            _textAp[i] = new FormattedText("Ap", CultureInfo.InvariantCulture, FlowDirection.LeftToRight, NodeFont, 9.5d, new SolidColorBrush(cAp));
            _textPe[i] = new FormattedText("Pe", CultureInfo.InvariantCulture, FlowDirection.LeftToRight, NodeFont, 9.5d, new SolidColorBrush(cPe));
            _textAn[i] = new FormattedText("An", CultureInfo.InvariantCulture, FlowDirection.LeftToRight, NodeFont, 9.5d, new SolidColorBrush(cAn));
            _textDn[i] = new FormattedText("Dn", CultureInfo.InvariantCulture, FlowDirection.LeftToRight, NodeFont, 9.5d, new SolidColorBrush(cDn));

            _apRingPens[i] = new Pen(new SolidColorBrush(cAp), 1.1d);
            _peRingPens[i] = new Pen(new SolidColorBrush(cPe), 1.1d);
            _anRingPens[i] = new Pen(new SolidColorBrush(cAn), 1.1d);
            _dnRingPens[i] = new Pen(new SolidColorBrush(cDn), 1.1d);

            var bgBrushAp = new SolidColorBrush(Color.FromArgb(bgA, BadgeBg.R, BadgeBg.G, BadgeBg.B));
            _apBadgeBgBrushes[i] = bgBrushAp;
            _peBadgeBgBrushes[i] = bgBrushAp;
            _anBadgeBgBrushes[i] = bgBrushAp;
            _dnBadgeBgBrushes[i] = bgBrushAp;

            _anChevronBrushes[i] = new SolidColorBrush(cAn);
            _dnChevronBrushes[i] = new SolidColorBrush(cDn);
        }

        _paletteReady = true;
    }
}
