using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using CkanUI.App.Theming;

namespace CkanUI.App.Controls;

/// <summary>
/// 光标指针效果的交互渲染层：在全窗顶层（ZIndex 16）渲染一个跟随光标的高精度指针效果与拖拽星光轨迹。
/// 划过可交互元素时具有平滑倾斜效果，点击时缩小，松开时放大回弹。拖拽窗口时留下星光粒子轨迹。
/// 自身 IsHitTestVisible 恒为 false，绝不拦截任何鼠标或键盘输入。
/// </summary>
public sealed class CursorPointerLayer : Control
{
    private const double TickIntervalMs = 16d;
    private const int AlphaSteps = 32;

    private static readonly Color DefaultAccent = Color.Parse("#72A7FF");
    private static readonly Color DarkContourColor = Color.Parse("#0D1117");
    private static readonly Color WhiteAccentColor = Color.Parse("#FFFFFF");
    private static readonly Color WarmStarTint = Color.Parse("#FFD7A8");

    /// <summary>
    /// 高精度流线型光标矢量几何（尖端精准锚定在 0, 0）。
    /// </summary>
    private static readonly StreamGeometry PointerGeometry =
        StreamGeometry.Parse("M 0 0 L 0 16.5 L 4.2 12.8 L 7.8 19.8 L 10.6 18.4 L 7.0 11.6 L 12.4 11.6 Z");

    private readonly CursorPointerModel _model = new();
    private readonly WindowDragStarlightModel _dragStarlight = new();
    private readonly DispatcherTimer _timer;

    // 窗口拖动期间光标相对窗口客户区的锚点。原生拖动时局部坐标基本不变，
    // 不能拿 LastPosition 累加屏幕像素，否则星光会一路跑到光标前面。
    private double _windowDragAnchorX;
    private double _windowDragAnchorY;

    private readonly SolidColorBrush?[] _bodyFillBrushes = new SolidColorBrush?[AlphaSteps + 1];
    private readonly SolidColorBrush?[] _haloBrushes = new SolidColorBrush?[AlphaSteps + 1];
    private readonly SolidColorBrush?[] _pipBrushes = new SolidColorBrush?[AlphaSteps + 1];
    private readonly Pen?[] _outerPens = new Pen?[AlphaSteps + 1];
    private readonly Pen?[] _innerPens = new Pen?[AlphaSteps + 1];

    private readonly SolidColorBrush?[] _coolStarBrushes = new SolidColorBrush?[AlphaSteps + 1];
    private readonly SolidColorBrush?[] _warmStarBrushes = new SolidColorBrush?[AlphaSteps + 1];
    private readonly Pen?[] _sparkleCrossPens = new Pen?[AlphaSteps + 1];

    private Color _accent;
    private bool _paletteReady;

    /// <summary>构造一个空的光标指针渲染层。</summary>
    public CursorPointerLayer()
    {
        IsHitTestVisible = false;
        ClipToBounds = false;

        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(TickIntervalMs) };
        _timer.Tick += OnTick;
    }

    /// <summary>当前是否开启全局动效。</summary>
    public bool IsMotionEnabled =>
        !this.TryFindResource(MotionHost.BaseKey, out var value)
        || value is not TimeSpan duration
        || duration > TimeSpan.Zero;

    /// <summary>当前光标尖端位置 X（DIP）。</summary>
    public double PointerX => _model.X;

    /// <summary>当前光标尖端位置 Y（DIP）。</summary>
    public double PointerY => _model.Y;

    /// <summary>当前光标倾斜角度（度数）。</summary>
    public double Tilt => _model.Tilt;

    /// <summary>当前光标缩放比例。</summary>
    public double Scale => _model.Scale;

    /// <summary>当前光标不透明度。</summary>
    public double CurrentOpacity => _model.Opacity;

    /// <summary>是否处于活跃绘制状态。</summary>
    public bool IsActive => (_model.IsVisible && _model.Opacity > 0.005d) || !_dragStarlight.IsIdle;

    /// <summary>拖拽星光模型实例（方便单元测试与诊断）。</summary>
    public WindowDragStarlightModel DragStarlight => _dragStarlight;

    /// <summary>是否完全静止（用于诊断与测试）。</summary>
    public bool IsIdle => _model.IsIdle && _dragStarlight.IsIdle;

    /// <summary>
    /// 开始窗口拖拽轨迹撒星。
    /// </summary>
    public void StartWindowDrag(Point position)
    {
        if (!IsMotionEnabled) return;
        _windowDragAnchorX = position.X;
        _windowDragAnchorY = position.Y;
        _dragStarlight.StartDrag(position.X, position.Y);
        UpdateTimerState();
        InvalidateVisual();
    }

    /// <summary>
    /// 推进窗口拖拽轨迹撒星。
    /// </summary>
    public void UpdateWindowDrag(Point position)
    {
        if (!IsMotionEnabled) return;
        _windowDragAnchorX = position.X;
        _windowDragAnchorY = position.Y;
        _dragStarlight.UpdateDrag(position.X, position.Y);
        UpdateTimerState();
        InvalidateVisual();
    }

    /// <summary>
    /// 结束窗口拖拽轨迹撒星。
    /// </summary>
    public void EndWindowDrag()
    {
        _dragStarlight.EndDrag();
        UpdateTimerState();
        InvalidateVisual();
    }

    /// <summary>
    /// 窗口在屏幕上位移时推进拖拽星光撒点（基于原生窗口拖动像素位移）。
    /// </summary>
    public void OnWindowMoved(double deltaScreenX, double deltaScreenY)
    {
        if (!IsMotionEnabled || !_dragStarlight.IsDragging)
        {
            return;
        }

        // PositionChanged 给的是物理屏幕像素，粒子模型用的是 DIP。
        double scaling = TopLevel.GetTopLevel(this)?.RenderScaling ?? 1.0;
        if (!double.IsFinite(scaling) || scaling <= 0.0)
        {
            scaling = 1.0;
        }

        double dx = deltaScreenX / scaling;
        double dy = deltaScreenY / scaling;

        // 窗口整体平移时，已经生成的粒子要朝相反方向补偿，才能在屏幕上钉住不动；
        // 随后从真实光标锚点补发新粒子，轨迹才会落在光标后面而不是前面。
        _dragStarlight.Shift(-dx, -dy);
        _dragStarlight.UpdateDrag(_windowDragAnchorX, _windowDragAnchorY);
        UpdateTimerState();
        InvalidateVisual();
    }

    /// <summary>
    /// 跟随光标移动与悬停状态。
    /// </summary>
    public void Track(Point position, bool isOverInteractive)
    {
        if (!IsMotionEnabled)
        {
            Clear();
            return;
        }

        _model.Track(position.X, position.Y, isOverInteractive);
        if (_dragStarlight.IsDragging)
        {
            _windowDragAnchorX = position.X;
            _windowDragAnchorY = position.Y;
            _dragStarlight.UpdateDrag(position.X, position.Y);
        }

        UpdateTimerState();
        InvalidateVisual();
    }

    /// <summary>
    /// 按下指针：触发缩小手感。
    /// </summary>
    public void Press(Point position)
    {
        if (!IsMotionEnabled)
        {
            return;
        }

        _model.Press(position.X, position.Y);
        UpdateTimerState();
        InvalidateVisual();
    }

    /// <summary>
    /// 松开指针：触发放大冲顶与回弹手感。
    /// </summary>
    public void Release(Point position)
    {
        if (!IsMotionEnabled)
        {
            return;
        }

        _model.Release(position.X, position.Y);
        if (_dragStarlight.IsDragging)
        {
            _dragStarlight.EndDrag();
        }

        UpdateTimerState();
        InvalidateVisual();
    }

    /// <summary>
    /// 光标离开窗口时淡出。
    /// </summary>
    public void Hide()
    {
        _model.Hide();
        UpdateTimerState();
        InvalidateVisual();
    }

    /// <summary>
    /// 窗口失焦或重置时立即熄灭。
    /// </summary>
    public void Clear()
    {
        _model.Clear();
        _dragStarlight.Clear();
        _timer.Stop();
        InvalidateVisual();
    }

    /// <inheritdoc />
    public override void Render(DrawingContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (!IsActive)
        {
            return;
        }

        EnsurePalette(ResolveAccent());

        // 1. 先绘制拖拽星光粒子（处于底层）
        RenderDragStarlight(context);

        // 2. 绘制光标本体
        RenderCursorPointer(context);
    }

    private void RenderDragStarlight(DrawingContext context)
    {
        if (_dragStarlight.ActiveCount == 0) return;

        foreach (var p in _dragStarlight.Particles)
        {
            if (!p.IsAlive) continue;

            double lifeFraction = p.Age / p.MaxLife;
            // 粒子渐隐曲线：前期微亮，中后期平滑淡出
            double alpha = Math.Clamp(1.0 - lifeFraction * lifeFraction, 0.0, 1.0);
            double twinkle = 0.8 + 0.2 * Math.Sin(p.TwinklePhase + p.Age * p.TwinkleSpeed);
            alpha *= twinkle;

            int alphaIdx = AlphaIndex(alpha);
            if (alphaIdx <= 0) continue;

            var brush = p.Warmth > 0.5 ? _warmStarBrushes[alphaIdx] : _coolStarBrushes[alphaIdx];
            if (brush is null) continue;

            double radius = p.Radius * (0.8 + 0.2 * (1.0 - lifeFraction));
            context.DrawEllipse(brush, null, new Point(p.X, p.Y), radius, radius);

            if (p.IsSparkleCross && alpha > 0.35)
            {
                var pen = _sparkleCrossPens[alphaIdx];
                if (pen is not null)
                {
                    double arm = radius * 2.2;
                    context.DrawLine(pen, new Point(p.X - arm, p.Y), new Point(p.X + arm, p.Y));
                    context.DrawLine(pen, new Point(p.X, p.Y - arm), new Point(p.X, p.Y + arm));
                }
            }
        }
    }

    private void RenderCursorPointer(DrawingContext context)
    {
        if (!_model.IsVisible || _model.Opacity <= 0.005d) return;

        int alphaIndex = AlphaIndex(_model.Opacity);
        if (alphaIndex <= 0) return;

        // 以尖端 (0, 0) 为中心进行旋转与缩放，再平移到鼠标位置 (X, Y)
        var matrix = Matrix.CreateScale(_model.Scale, _model.Scale)
                   * Matrix.CreateRotation(_model.TiltRadians)
                   * Matrix.CreateTranslation(_model.X, _model.Y);

        using (context.PushTransform(matrix))
        {
            // 1. 光标外围高科技辉光
            var haloBrush = _haloBrushes[alphaIndex];
            if (haloBrush is not null)
            {
                context.DrawEllipse(haloBrush, null, new Point(4.5d, 7.0d), 9.5d, 9.5d);
            }

            // 2. 指针本体：填充与暗色外轮廓
            var fillBrush = _bodyFillBrushes[alphaIndex];
            var outerPen = _outerPens[alphaIndex];
            if (fillBrush is not null && outerPen is not null)
            {
                context.DrawGeometry(fillBrush, outerPen, PointerGeometry);
            }

            // 3. 强调色内描边
            var innerPen = _innerPens[alphaIndex];
            if (innerPen is not null)
            {
                context.DrawGeometry(null, innerPen, PointerGeometry);
            }

            // 4. 尖端 HUD 准星微亮点
            var pipBrush = _pipBrushes[alphaIndex];
            if (pipBrush is not null)
            {
                context.DrawEllipse(pipBrush, null, new Point(3.5d, 5.0d), 1.2d, 1.2d);
            }
        }
    }

    /// <inheritdoc />
    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        UpdateTimerState();
    }

    /// <inheritdoc />
    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        UpdateTimerState();
    }

    private void UpdateTimerState()
    {
        if (VisualRoot is not null && (! _model.IsIdle || !_dragStarlight.IsIdle))
        {
            _timer.Start();
        }
        else
        {
            _timer.Stop();
        }
    }

    private void OnTick(object? sender, EventArgs e)
    {
        double dt = TickIntervalMs / 1000d;
        _model.Advance(dt);
        _dragStarlight.Advance(dt);
        InvalidateVisual();

        if (IsIdle)
        {
            _timer.Stop();
        }
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

            // 1. 本体填充（带有半透明质感的浅蓝/白色混合）
            byte fillA = (byte)Math.Clamp(Math.Round(stepAlpha * 230d), 0, 255);
            var fillColor = Color.FromArgb(fillA, (byte)((accent.R + 255) / 2), (byte)((accent.G + 255) / 2), (byte)((accent.B + 255) / 2));
            if (_bodyFillBrushes[i] is { } fillBrush)
            {
                fillBrush.Color = fillColor;
            }
            else
            {
                _bodyFillBrushes[i] = new SolidColorBrush(fillColor);
            }

            // 2. 外部深色对比轮廓
            byte outerA = (byte)Math.Clamp(Math.Round(stepAlpha * 220d), 0, 255);
            var outerColor = Color.FromArgb(outerA, DarkContourColor.R, DarkContourColor.G, DarkContourColor.B);
            var outerBrush = new SolidColorBrush(outerColor);
            _outerPens[i] = new Pen(outerBrush, 1.6d, lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round);

            // 3. 强调色内描边
            byte innerA = (byte)Math.Clamp(Math.Round(stepAlpha * 200d), 0, 255);
            var innerColor = Color.FromArgb(innerA, accent.R, accent.G, accent.B);
            var innerBrush = new SolidColorBrush(innerColor);
            _innerPens[i] = new Pen(innerBrush, 0.9d, lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round);

            // 4. 外围辉光（低不透明度扩散）
            byte haloA = (byte)Math.Clamp(Math.Round(stepAlpha * 48d), 0, 255);
            var haloColor = Color.FromArgb(haloA, accent.R, accent.G, accent.B);
            if (_haloBrushes[i] is { } halo)
            {
                halo.Color = haloColor;
            }
            else
            {
                _haloBrushes[i] = new SolidColorBrush(haloColor);
            }

            // 5. 准星微亮点
            byte pipA = (byte)Math.Clamp(Math.Round(stepAlpha * 240d), 0, 255);
            var pipColor = Color.FromArgb(pipA, WhiteAccentColor.R, WhiteAccentColor.G, WhiteAccentColor.B);
            if (_pipBrushes[i] is { } pip)
            {
                pipColor = Color.FromArgb(pipA, WhiteAccentColor.R, WhiteAccentColor.G, WhiteAccentColor.B);
                pip.Color = pipColor;
            }
            else
            {
                _pipBrushes[i] = new SolidColorBrush(pipColor);
            }

            // 6. 星光冷白/偏蓝粒子画刷
            byte coolA = (byte)Math.Clamp(Math.Round(stepAlpha * 210d), 0, 255);
            var coolColor = Color.FromArgb(coolA, (byte)((accent.R + 255) / 2), (byte)((accent.G + 255) / 2), 255);
            _coolStarBrushes[i] = new SolidColorBrush(coolColor);

            // 7. 星光暖星粒子画刷
            byte warmA = (byte)Math.Clamp(Math.Round(stepAlpha * 210d), 0, 255);
            var warmColor = Color.FromArgb(warmA, WarmStarTint.R, WarmStarTint.G, WarmStarTint.B);
            _warmStarBrushes[i] = new SolidColorBrush(warmColor);

            // 8. 芒星十字线画笔
            byte crossA = (byte)Math.Clamp(Math.Round(stepAlpha * 180d), 0, 255);
            var crossColor = Color.FromArgb(crossA, 255, 255, 255);
            _sparkleCrossPens[i] = new Pen(new SolidColorBrush(crossColor), 0.8d, lineCap: PenLineCap.Round);
        }

        _paletteReady = true;
    }
}
