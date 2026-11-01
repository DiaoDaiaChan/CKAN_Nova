using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using CkanUI.App.Theming;

namespace CkanUI.App.Controls;

/// <summary>
/// 光标指针效果的交互渲染层：在全窗顶层（ZIndex 16）渲染一个跟随光标的高精度指针效果。
/// 划过可交互元素时具有平滑倾斜效果，点击时缩小，松开时放大回弹。
/// 自身 IsHitTestVisible 恒为 false，绝不拦截任何鼠标或键盘输入。
/// </summary>
public sealed class CursorPointerLayer : Control
{
    private const double TickIntervalMs = 16d;
    private const int AlphaSteps = 32;

    private static readonly Color DefaultAccent = Color.Parse("#72A7FF");
    private static readonly Color DarkContourColor = Color.Parse("#0D1117");
    private static readonly Color WhiteAccentColor = Color.Parse("#FFFFFF");

    /// <summary>
    /// 高精度流线型光标矢量几何（尖端精准锚定在 0, 0）。
    /// </summary>
    private static readonly StreamGeometry PointerGeometry =
        StreamGeometry.Parse("M 0 0 L 0 16.5 L 4.2 12.8 L 7.8 19.8 L 10.6 18.4 L 7.0 11.6 L 12.4 11.6 Z");

    private readonly CursorPointerModel _model = new();
    private readonly DispatcherTimer _timer;

    private readonly SolidColorBrush?[] _bodyFillBrushes = new SolidColorBrush?[AlphaSteps + 1];
    private readonly SolidColorBrush?[] _haloBrushes = new SolidColorBrush?[AlphaSteps + 1];
    private readonly SolidColorBrush?[] _pipBrushes = new SolidColorBrush?[AlphaSteps + 1];
    private readonly Pen?[] _outerPens = new Pen?[AlphaSteps + 1];
    private readonly Pen?[] _innerPens = new Pen?[AlphaSteps + 1];

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
    public bool IsActive => _model.IsVisible && _model.Opacity > 0.005d;

    /// <summary>是否完全静止（用于诊断与测试）。</summary>
    public bool IsIdle => _model.IsIdle;

    /// <summary>
    /// 跟随光标移动与悬停状态。
    /// </summary>
    /// <param name="position">光标在当前层坐标系下的位置。</param>
    /// <param name="isOverInteractive">是否悬停在可响应元素（Button, Card, ListBoxItem 等）上。</param>
    public void Track(Point position, bool isOverInteractive)
    {
        if (!IsMotionEnabled)
        {
            Clear();
            return;
        }

        _model.Track(position.X, position.Y, isOverInteractive);
        UpdateTimerState();
        InvalidateVisual();
    }

    /// <summary>
    /// 按下指针：触发缩小手感。
    /// </summary>
    /// <param name="position">按下坐标。</param>
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
    /// <param name="position">松开坐标。</param>
    public void Release(Point position)
    {
        if (!IsMotionEnabled)
        {
            return;
        }

        _model.Release(position.X, position.Y);
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

        int alphaIndex = AlphaIndex(_model.Opacity);
        if (alphaIndex <= 0)
        {
            return;
        }

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
        if (VisualRoot is not null && !_model.IsIdle)
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
        _model.Advance(TickIntervalMs / 1000d);
        InvalidateVisual();

        if (_model.IsIdle)
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
                pip.Color = pipColor;
            }
            else
            {
                _pipBrushes[i] = new SolidColorBrush(pipColor);
            }
        }

        _paletteReady = true;
    }
}
