using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Styling;
using CkanUI.Services.Abstractions.Dtos;

namespace CkanUI.App.Controls;

/// <summary>
/// 环形图（12 篇 F-47）：只画环，图例由视图用 ItemsControl 渲染，避免在 Render 里排文本。
/// 颜色一律取语义 token（Ckan*），段数 > 8 时聚合层已合并为「其它」，此处最多呈现 9 段（前 8 段 + 其它）。
/// </summary>
public sealed class DonutChart : Control
{
    /// <summary>环最多分几段；超过的段不再单独绘制（前 8 段 + 其它，共 9 段）。</summary>
    public const int MaxSegments = 9;

    /// <summary>环宽（像素）。</summary>
    public static readonly StyledProperty<double> RingThicknessProperty =
        AvaloniaProperty.Register<DonutChart, double>(nameof(RingThickness), 22d);

    /// <summary>数据点。</summary>
    public static readonly StyledProperty<IReadOnlyList<DownloadStatsSlice>?> SlicesProperty =
        AvaloniaProperty.Register<DonutChart, IReadOnlyList<DownloadStatsSlice>?>(nameof(Slices));

    /// <summary>中心显示的合计文本（为空则不画）。</summary>
    public static readonly StyledProperty<string?> CenterTextProperty =
        AvaloniaProperty.Register<DonutChart, string?>(nameof(CenterText));

    /// <summary>中心文本下面的说明。</summary>
    public static readonly StyledProperty<string?> CenterCaptionProperty =
        AvaloniaProperty.Register<DonutChart, string?>(nameof(CenterCaption));

    private static readonly string[] Palette =
    [
        "CkanAccentBrush",
        "CkanSuccessBrush",
        "CkanWarningBrush",
        "CkanDangerBrush",
        "CkanTextSecondaryBrush",
    ];

    static DonutChart()
    {
        AffectsRender<DonutChart>(SlicesProperty, RingThicknessProperty, CenterTextProperty, CenterCaptionProperty);
    }

    /// <summary>环宽（像素）。</summary>
    public double RingThickness
    {
        get => GetValue(RingThicknessProperty);
        set => SetValue(RingThicknessProperty, value);
    }

    /// <summary>数据点。</summary>
    public IReadOnlyList<DownloadStatsSlice>? Slices
    {
        get => GetValue(SlicesProperty);
        set => SetValue(SlicesProperty, value);
    }

    /// <summary>中心文本。</summary>
    public string? CenterText
    {
        get => GetValue(CenterTextProperty);
        set => SetValue(CenterTextProperty, value);
    }

    /// <summary>中心说明。</summary>
    public string? CenterCaption
    {
        get => GetValue(CenterCaptionProperty);
        set => SetValue(CenterCaptionProperty, value);
    }

    /// <inheritdoc />
    public override void Render(DrawingContext context)
    {
        base.Render(context);

        var size = Math.Min(Bounds.Width, Bounds.Height);
        if (size <= 0)
        {
            return;
        }

        var center = new Point(Bounds.Width / 2, Bounds.Height / 2);
        var thickness = Math.Max(2d, Math.Min(RingThickness, size / 4));
        var radius = (size - thickness) / 2;
        var segments = Slices?.Take(MaxSegments).Where(slice => slice.Count > 0).ToArray()
                       ?? Array.Empty<DownloadStatsSlice>();

        var total = segments.Sum(slice => (double)slice.Count);
        if (total <= 0)
        {
            var empty = ResolveThemeBrush("CkanBorderBrush");
            context.DrawEllipse(null, new Pen(empty, thickness), center, radius, radius);
            DrawCenterText(context, center);
            return;
        }

        var startAngle = -Math.PI / 2;
        const int StepsPerSegment = 24;
        for (var index = 0; index < segments.Length; index++)
        {
            var brush = ResolveThemeBrush(Palette[index % Palette.Length]);
            var sweep = segments[index].Count * Math.PI * 2 / total;
            var pen = new Pen(brush, thickness) { LineCap = PenLineCap.Flat };
            for (var step = 0; step < StepsPerSegment; step++)
            {
                var from = startAngle + sweep * step / StepsPerSegment;
                var to = startAngle + sweep * (step + 1) / StepsPerSegment;
                context.DrawLine(
                    pen,
                    new Point(center.X + Math.Cos(from) * radius, center.Y + Math.Sin(from) * radius),
                    new Point(center.X + Math.Cos(to) * radius, center.Y + Math.Sin(to) * radius));
            }

            startAngle += sweep;
        }

        DrawCenterText(context, center);
    }

    private void DrawCenterText(DrawingContext context, Point center)
    {
        if (string.IsNullOrEmpty(CenterText) && string.IsNullOrEmpty(CenterCaption))
        {
            return;
        }

        var primary = ResolveThemeBrush("CkanTextPrimaryBrush");
        var secondary = ResolveThemeBrush("CkanTextSecondaryBrush");

        if (!string.IsNullOrEmpty(CenterText))
        {
            var text = new FormattedText(
                CenterText,
                CultureInfo.CurrentCulture,
                FlowDirection.LeftToRight,
                Typeface.Default,
                16,
                primary);
            context.DrawText(text, new Point(center.X - text.Width / 2, center.Y - text.Height + 2));
        }

        if (!string.IsNullOrEmpty(CenterCaption))
        {
            var caption = new FormattedText(
                CenterCaption,
                CultureInfo.CurrentCulture,
                FlowDirection.LeftToRight,
                Typeface.Default,
                11,
                secondary);
            context.DrawText(caption, new Point(center.X - caption.Width / 2, center.Y + 4));
        }
    }

    private IBrush ResolveThemeBrush(string key)
    {
        var theme = ActualThemeVariant ?? Application.Current?.ActualThemeVariant ?? ThemeVariant.Default;
        if (this.TryFindResource(key, theme, out var resource) && resource is IBrush brush)
        {
            return brush;
        }

        if (this.TryFindResource(key, out var resourceNoTheme) && resourceNoTheme is IBrush brushNoTheme)
        {
            return brushNoTheme;
        }

        if (Application.Current is not null)
        {
            if (Application.Current.TryFindResource(key, theme, out var appRes) && appRes is IBrush appBrush)
            {
                return appBrush;
            }

            if (Application.Current.TryFindResource(key, out var appResNoTheme) && appResNoTheme is IBrush appBrushNoTheme)
            {
                return appBrushNoTheme;
            }
        }

        // 回退至基础语义 token，绝不硬编码灰/黑，若全局均未初始化则退回透明
        if (key != "CkanTextSecondaryBrush")
        {
            var fallback = ResolveThemeBrush("CkanTextSecondaryBrush");
            if (!ReferenceEquals(fallback, Brushes.Transparent))
            {
                return fallback;
            }
        }

        return Brushes.Transparent;
    }
}
