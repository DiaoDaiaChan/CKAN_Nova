using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace CkanUI.Plugin.Performance;

/// <summary>
/// 占比条（分解条与 GPU 段共用）。
/// </summary>
/// <remarks>
/// 宽度按 0–1 的 <see cref="Fraction"/> 画，而不是让视图去算像素：占比的计算与格式化都在视图模型，
/// 视图只负责把它画出来，避免 XAML 里出现一屏幕转换器。缺失值（<see cref="Fraction"/> 为 0 且
/// <see cref="IsAvailable"/> 为 false）时只画轨道、不画填充——不拿零宽度冒充"0 毫秒"。
/// </remarks>
public sealed class PerfBar : Control
{
    /// <summary>填充比例 0–1。</summary>
    public static readonly StyledProperty<double> FractionProperty =
        AvaloniaProperty.Register<PerfBar, double>(nameof(Fraction));

    /// <summary>是否可用（不满足时只画轨道）。</summary>
    public static readonly StyledProperty<bool> IsAvailableProperty =
        AvaloniaProperty.Register<PerfBar, bool>(nameof(IsAvailable), true);

    /// <summary>填充色。</summary>
    public static readonly StyledProperty<IBrush?> FillProperty =
        AvaloniaProperty.Register<PerfBar, IBrush?>(nameof(Fill));

    /// <summary>轨道色。</summary>
    public static readonly StyledProperty<IBrush?> TrackProperty =
        AvaloniaProperty.Register<PerfBar, IBrush?>(nameof(Track));

    static PerfBar()
    {
        AffectsRender<PerfBar>(FractionProperty, IsAvailableProperty, FillProperty, TrackProperty);
    }

    /// <summary>填充比例 0–1。</summary>
    public double Fraction
    {
        get => GetValue(FractionProperty);
        set => SetValue(FractionProperty, value);
    }

    /// <summary>是否可用。</summary>
    public bool IsAvailable
    {
        get => GetValue(IsAvailableProperty);
        set => SetValue(IsAvailableProperty, value);
    }

    /// <summary>填充色。</summary>
    public IBrush? Fill
    {
        get => GetValue(FillProperty);
        set => SetValue(FillProperty, value);
    }

    /// <summary>轨道色。</summary>
    public IBrush? Track
    {
        get => GetValue(TrackProperty);
        set => SetValue(TrackProperty, value);
    }

    /// <inheritdoc />
    public override void Render(DrawingContext context)
    {
        base.Render(context);

        var width = Bounds.Width;
        var height = Bounds.Height;
        if (width <= 0 || height <= 0)
        {
            return;
        }

        var radius = Math.Min(height / 2d, 4d);
        var track = Track;
        if (track is not null)
        {
            context.DrawRectangle(track, null, new RoundedRect(new Rect(0, 0, width, height), radius));
        }

        var fill = Fill;
        if (fill is null || !IsAvailable)
        {
            return;
        }

        var fraction = Math.Clamp(Fraction, 0d, 1d);
        var filled = width * fraction;
        if (filled <= 0.5)
        {
            return;
        }

        context.DrawRectangle(fill, null, new RoundedRect(new Rect(0, 0, filled, height), radius));
    }
}
