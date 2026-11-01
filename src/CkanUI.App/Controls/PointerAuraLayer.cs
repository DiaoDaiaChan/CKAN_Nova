using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Data;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CkanUI.App.Theming;

namespace CkanUI.App.Controls;

/// <summary>
/// 指针光环层：整窗覆盖、不吃鼠标事件，把「光标半径内的元素响应」画出来。
/// </summary>
/// <remarks>
/// <para>
/// 响应范围由 <see cref="PointerAuraModel.Radius"/> 决定，绘制时按元素自身边界裁剪（含圆角），
/// 所以光只落在光标下的那块控件表面上，不会漏到旁边的空白或别张卡片。
/// </para>
/// <para>
/// 三层叠加：径向光斑（等不透明度圆盘按 profile 反解，等价于一段平滑渐变）+ 确定性颗粒（磨砂纹理）
/// + 1px 强调色描边（有厚度感）。三者都乘同一个强度，淡入淡出统一。
/// </para>
/// <para>
/// 计时器只在有光时跑：模型报不活跃就停表，空闲时零开销。动效关闭时整层不画——它不承载状态。
/// </para>
/// </remarks>
public sealed class PointerAuraLayer : Control
{
    /// <summary>径向光斑的环数：24 环在 168 像素上已经看不出分层。</summary>
    private const int RingCount = 24;

    /// <summary>不透明度量化档数：复用画刷，避免每帧 new 几百个。</summary>
    private const int AlphaSteps = 32;

    /// <summary>动画帧间隔（毫秒）。</summary>
    private const double TickIntervalMs = 16;

    /// <summary>描边宽度（DIP）。</summary>
    private const double RimThickness = 1d;

    /// <summary>颗粒边长（DIP）。</summary>
    private const double SpeckSize = 1.4d;

    /// <summary>颗粒保留阈值：只有哈希值超过它的格子才画，密度约四成。</summary>
    private const double SpeckThreshold = 0.62d;

    private static readonly Color DefaultAccent = Color.Parse("#72A7FF");

    private readonly PointerAuraModel _model = new();
    private readonly DispatcherTimer _timer;
    private readonly SolidColorBrush?[] _ringBrushes = new SolidColorBrush?[AlphaSteps + 1];
    private readonly SolidColorBrush?[] _speckBrushes = new SolidColorBrush?[AlphaSteps + 1];
    private readonly Pen?[] _rimPens = new Pen?[AlphaSteps + 1];

    private Color _accent;
    private bool _paletteReady;
    private Control? _target;
    private Rect _targetBounds;
    private double _cornerRadius;

    /// <summary>构造一个空的光环层：不拦鼠标事件，也不裁剪到自身边界。</summary>
    public PointerAuraLayer()
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

    /// <summary>是否还有可见的光。</summary>
    public bool IsActive => _model.IsActive;

    /// <summary>当前强度 0..1（测试与诊断用）。</summary>
    public double Intensity => _model.Alpha;

    /// <summary>当前响应元素在层坐标里的矩形（测试与诊断用）。</summary>
    public Rect TargetBounds => _targetBounds;

    /// <summary>当前响应元素的类型名（测试与诊断用）；没有目标时为 null。</summary>
    public string? TargetKind => _target?.GetType().Name;

    /// <summary>
    /// 把光标位置与它下面的元素交给本层。元素为 null 表示光标落在空白处，开始淡出。
    /// </summary>
    /// <param name="position">光标位置，相对本层的 DIP。</param>
    /// <param name="surface">光标命中的可响应元素；没有就传 null。</param>
    public void Track(Point position, Control? surface)
    {
        if (!IsMotionEnabled)
        {
            Clear();
            return;
        }

        if (surface is null)
        {
            _target = null;
            _targetBounds = default;
            _cornerRadius = 0d;
            _model.Clear();
            UpdateTimerState();
            return;
        }

        var origin = surface.TranslatePoint(default, this);
        if (origin is null)
        {
            return;
        }

        _target = surface;
        _targetBounds = new Rect(origin.Value, surface.Bounds.Size);
        _cornerRadius = ResolveCornerRadius(surface);
        _model.Track(position.X, position.Y);
        UpdateTimerState();
    }

    /// <summary>立刻熄灭（失焦、窗口关闭）；位置与目标一起清掉。</summary>
    public void Clear()
    {
        _target = null;
        _targetBounds = default;
        _cornerRadius = 0d;
        _model.Clear();
        for (var i = 0; i < AlphaSteps; i++)
        {
            _model.Advance(0.1d);
        }

        UpdateTimerState();
        InvalidateVisual();
    }

    /// <summary>
    /// 从命中的视觉元素往上找最近的可响应表面：按钮族、列表/表格行、页签、菜单项、以及卡片容器。
    /// </summary>
    /// <param name="source">指针事件的 Source。</param>
    /// <param name="root">向上查找的边界（通常是窗口）。</param>
    /// <returns>可响应元素；没有则返回 null。</returns>
    public static Control? FindSurface(Visual? source, Visual? root)
    {
        for (var visual = source; visual is not null && visual != root; visual = visual.GetVisualParent())
        {
            // ToggleButton 覆盖 CheckBox / RadioButton；Button 覆盖 RepeatButton 与各类派生按钮。
            if (visual is MenuItem or TabItem or TreeViewItem or ListBoxItem or DataGridRow
                or Button or ToggleButton)
            {
                return visual as Control;
            }

            if (visual is Border border
                && border.Classes.Contains("CkanCardInteractive"))
            {
                return border;
            }
        }

        return null;
    }

    /// <inheritdoc />
    public override void Render(DrawingContext context)
    {
        if (!_model.IsActive || _target is null || _target.Bounds.Width < 1d || _target.Bounds.Height < 1d)
        {
            return;
        }

        var matrix = _target.TransformToVisual(this);
        if (matrix is null)
        {
            return;
        }

        var localBounds = new Rect(0, 0, _target.Bounds.Width, _target.Bounds.Height);
        EnsurePalette();

        var center = new Point(_model.X, _model.Y);
        var localCenter = this.TranslatePoint(center, _target) ?? center;
        var intensity = _model.Alpha;

        using (context.PushTransform(matrix.Value))
        using (context.PushClip(new RoundedRect(localBounds, new CornerRadius(_cornerRadius))))
        {
            DrawSpotlight(context, localCenter, intensity);
            DrawTexture(context, localCenter, intensity);

            var rimAlpha = PointerAuraModel.PeakAlpha * PointerAuraModel.RimAlpha * intensity;
            context.DrawRectangle(
                null,
                _rimPens[Quantize(rimAlpha)],
                new RoundedRect(localBounds.Deflate(0.5d), new CornerRadius(_cornerRadius)));
        }
    }
    private void DrawSpotlight(DrawingContext context, Point center, double intensity)
    {
        var peak = PointerAuraModel.PeakAlpha * intensity;
        var radius = PointerAuraModel.Radius;

        // T(i) 是第 i 圈外沿处的目标不透明度；从外向内画，第 i 个盘的不透明度由相邻两圈的覆盖率反解。
        var outerCoverage = 0d;
        for (var i = RingCount; i >= 1; i--)
        {
            var outerRadius = radius * i / RingCount;
            var innerRadius = radius * (i - 1) / RingCount;
            var bandCenter = (outerRadius + innerRadius) * 0.5d;
            var coverage = peak * PointerAuraModel.Falloff(bandCenter, radius);

            var alpha = outerCoverage >= 1d ? 0d : 1d - (1d - coverage) / (1d - outerCoverage);
            outerCoverage = coverage;

            if (alpha <= 0d)
            {
                continue;
            }

            var brush = _ringBrushes[Quantize(alpha)];
            if (brush is not null)
            {
                context.DrawEllipse(brush, null, center, outerRadius, outerRadius);
            }
        }
    }

    /// <summary>确定性格点：同一格永远同一个值，帧间不闪；按到光标的距离衰减。</summary>
    private void DrawTexture(DrawingContext context, Point center, double intensity)
    {
        var radius = PointerAuraModel.Radius;
        var spacing = PointerAuraModel.TextureSpacing;
        var left = Math.Max(_targetBounds.Left, center.X - radius);
        var right = Math.Min(_targetBounds.Right, center.X + radius);
        var top = Math.Max(_targetBounds.Top, center.Y - radius);
        var bottom = Math.Min(_targetBounds.Bottom, center.Y + radius);

        var startColumn = (int)Math.Floor(left / spacing);
        var endColumn = (int)Math.Ceiling(right / spacing);
        var startRow = (int)Math.Floor(top / spacing);
        var endRow = (int)Math.Ceiling(bottom / spacing);

        var brush = _speckBrushes[Quantize(PointerAuraModel.TextureAlpha * intensity)];
        if (brush is null)
        {
            return;
        }

        for (var row = startRow; row <= endRow; row++)
        {
            for (var column = startColumn; column <= endColumn; column++)
            {
                var x = (column + 0.5d) * spacing;
                var y = (row + 0.5d) * spacing;
                var dx = x - center.X;
                var dy = y - center.Y;
                var weight = PointerAuraModel.Falloff(Math.Sqrt((dx * dx) + (dy * dy)), radius);
                if (weight <= 0d || PointerAuraModel.TextureSample(column, row) < SpeckThreshold)
                {
                    continue;
                }

                context.FillRectangle(brush, new Rect(x - (SpeckSize * 0.5d), y - (SpeckSize * 0.5d), SpeckSize, SpeckSize));
            }
        }
    }

    /// <summary>把连续不透明度量化到可复用的画刷档位。</summary>
    private static int Quantize(double alpha)
    {
        var steps = (int)Math.Round(Math.Clamp(alpha, 0d, 1d) * AlphaSteps);
        return Math.Clamp(steps, 0, AlphaSteps);
    }

    /// <summary>按当前强调色建一次画刷表；换了主题或强调色再重建。</summary>
    private void EnsurePalette()
    {
        var accent = ResolveAccent();
        if (_paletteReady && accent == _accent)
        {
            return;
        }

        _accent = accent;
        _paletteReady = true;
        for (var i = 0; i <= AlphaSteps; i++)
        {
            var alpha = (byte)Math.Round(255d * i / AlphaSteps);
            var color = Color.FromArgb(alpha, accent.R, accent.G, accent.B);
            _ringBrushes[i] = new SolidColorBrush(color);
            _speckBrushes[i] = new SolidColorBrush(Color.FromArgb((byte)Math.Round(alpha * 0.7d), 255, 255, 255));
            _rimPens[i] = new Pen(new SolidColorBrush(color), RimThickness);
        }
    }

    private Color ResolveAccent()
    {
        if (this.TryFindResource("CkanAccentBrush", out var value) && value is ISolidColorBrush brush)
        {
            return brush.Color;
        }

        return DefaultAccent;
    }

    /// <summary>元素圆角：优先取控件的 CornerRadius，菜单项这类没有该属性的回落到 6。</summary>
    private static double ResolveCornerRadius(Control control) => control switch
    {
        Border border => border.CornerRadius.TopLeft,
        TemplatedControl templated => templated.CornerRadius.TopLeft,
        _ => 6d,
    };

    private void UpdateTimerState()
    {
        // 刚 Track 的那一帧 Alpha 还是 0，只有 HasTarget 为真；如果这里只看 IsActive，
        // 计时器永远不会启动，淡入也就永远不会发生——指针光环曾经就是这么「看不见」的。
        if (!_model.IsActive && !_model.HasTarget)
        {
            _timer.Stop();
            return;
        }

        if (VisualRoot is not null && !_timer.IsEnabled)
        {
            _timer.Start();
        }
    }

    private void OnTick(object? sender, EventArgs e)
    {
        _model.Advance(TickIntervalMs / 1000d);

        if (!_model.IsActive)
        {
            _timer.Stop();
        }

        InvalidateVisual();
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
        _timer.Stop();
    }
}
