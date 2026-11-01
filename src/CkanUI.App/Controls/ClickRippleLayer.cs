using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using CkanUI.App.Theming;

namespace CkanUI.App.Controls;

/// <summary>
/// 覆盖在全窗最上层的点击涟漪层，不吃鼠标事件；落点由 <c>MainWindow</c> 的隧道
/// <c>PointerPressed</c> 事件转发进来。
/// </summary>
/// <remarks>
/// <para>
/// 纯自绘、纯装饰：<see cref="InputElement.IsHitTestVisible"/> 恒为 false，所以它盖在整棵界面之上
/// 也不会拦住任何点击。坐标一律是相对本控件的 DIP，落点在转发时就已经换算好，
/// 因此 <see cref="Visual.Bounds"/> 的尺寸变化不影响已发出的涟漪。
/// </para>
/// <para>
/// 计时器只在「有涟漪」时跑：模型报 <see cref="ClickRippleModel.IsIdle"/> 就立刻停表，空闲时一帧都不占。
/// 动效关闭时不生成涟漪——涟漪不承载状态，关掉动效就该整层消失，而不是留一圈冻住的环。
/// </para>
/// </remarks>
public sealed class ClickRippleLayer : Control
{
    /// <summary>不透明度的分档数：把连续透明度量化成可复用的画笔，避免每帧 new 画刷。</summary>
    private const int AlphaSteps = 32;

    /// <summary>动画帧间隔（毫秒）：60fps 左右，涟漪只有半秒，帧率低了会看成跳帧。</summary>
    private const double TickIntervalMs = 16;

    /// <summary>外圈线宽（DIP）。</summary>
    private const double OuterRingThickness = 1.2;

    /// <summary>内圈线宽（DIP）；比外圈细一点，两圈才不会同时抢眼。</summary>
    private const double InnerRingThickness = 0.9;

    /// <summary>低于这个不透明度的圈不画：扩到 44 像素后，尾部只剩一圈几乎看不见的噪点。</summary>
    private const double MinDrawAlpha = 0.004;

    private static readonly Color DefaultAccent = Color.Parse("#72A7FF");

    private readonly ClickRippleModel _model = new();
    private readonly DispatcherTimer _timer;
    private readonly Color[] _colors = new Color[AlphaSteps + 1];
    private readonly SolidColorBrush?[] _brushes = new SolidColorBrush?[AlphaSteps + 1];
    private readonly Pen?[] _outerPens = new Pen?[AlphaSteps + 1];
    private readonly Pen?[] _innerPens = new Pen?[AlphaSteps + 1];

    private Color _accent;
    private bool _paletteReady;

    /// <summary>构造一个空的涟漪层：不拦鼠标事件，也不裁剪到自身边界。</summary>
    public ClickRippleLayer()
    {
        IsHitTestVisible = false;
        ClipToBounds = false;

        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(TickIntervalMs) };
        _timer.Tick += OnTick;
    }

    /// <summary>当前是否开着动效（<c>CkanMotionDurationBase</c> 不为零，或 token 还没注入）。</summary>
    public bool IsMotionEnabled =>
        !this.TryFindResource(MotionHost.BaseKey, out var value)
        || value is not TimeSpan duration
        || duration > TimeSpan.Zero;

    /// <summary>
    /// 在指定落点放一个涟漪并开始逐帧推进；动效关闭时什么也不做。
    /// </summary>
    /// <param name="position">落点，相对本控件的 DIP 坐标。</param>
    public void Spawn(Point position)
    {
        // 涟漪是纯装饰、不承载任何状态：关掉动效就直接不生成，
        // 而不是生成后画成一帧静止的环——那反而像「点中了」的提示。
        if (!IsMotionEnabled)
        {
            return;
        }

        _model.Spawn(position.X, position.Y);

        // 还没挂进可视树（构造后、模板套上之前）先不跑表，Attach 时由 UpdateTimerState 接上，
        // 否则计时器会在没有渲染目标时空转。
        if (VisualRoot is not null)
        {
            _timer.Start();
        }

        InvalidateVisual();
    }

    /// <summary>清掉所有在飞的涟漪并停表。</summary>
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

        var count = _model.Count;
        if (count == 0)
        {
            return;
        }

        EnsurePalette(ResolveAccent());

        for (var i = 0; i < count; i++)
        {
            var center = new Point(_model.RippleX(i), _model.RippleY(i));
            for (var ring = 0; ring < ClickRippleModel.RingCount; ring++)
            {
                var alpha = _model.RingAlpha(i, ring);
                if (alpha < MinDrawAlpha)
                {
                    continue;
                }

                var radius = _model.RingRadius(i, ring);
                if (radius <= 0)
                {
                    continue;
                }

                context.DrawEllipse(null, RingPen(AlphaIndex(alpha), ring), center, radius, radius);
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

    /// <summary>按「在树里、且还有涟漪在飞」决定要不要跑表。</summary>
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

        // 最后一帧也要 Invalidate：涟漪被判死之后得有一次重绘把画面擦干净。
        if (_model.IsIdle)
        {
            _timer.Stop();
        }
    }

    private Color ResolveAccent() =>
        this.TryFindResource("CkanAccentBrush", out var value) && value is ISolidColorBrush brush
            ? brush.Color
            : DefaultAccent;

    private void EnsurePalette(Color accent)
    {
        if (_paletteReady && _accent == accent)
        {
            return;
        }

        _accent = accent;

        for (var i = 0; i <= AlphaSteps; i++)
        {
            var byteAlpha = (byte)Math.Round(i * 255d / AlphaSteps);
            _colors[i] = Color.FromArgb(byteAlpha, accent.R, accent.G, accent.B);
        }

        // 只改已有画笔的颜色，不重建对象：画笔一旦交给渲染线程，替换实例会拿到已经用完的那个。
        for (var i = 0; i <= AlphaSteps; i++)
        {
            if (_brushes[i] is { } brush)
            {
                brush.Color = _colors[i];
            }
        }

        _paletteReady = true;
    }

    private static int AlphaIndex(double alpha) =>
        (int)Math.Clamp(Math.Round(alpha * AlphaSteps, MidpointRounding.AwayFromZero), 0, AlphaSteps);

    private SolidColorBrush RingBrush(int index) => _brushes[index] ??= new SolidColorBrush(_colors[index]);

    private Pen RingPen(int index, int ring)
    {
        if (ring != 0)
        {
            return _innerPens[index] ??= new Pen(RingBrush(index), InnerRingThickness);
        }

        return _outerPens[index] ??= new Pen(RingBrush(index), OuterRingThickness);
    }
}
