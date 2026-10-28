using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;

using CkanUI.Services.Abstractions;
using CkanUI.ViewModels.Pages.Settings;

namespace CkanUI.App.Theming;

/// <summary>
/// 背景设置的渲染映射（20 篇 P-06 / THM-05）：把 <see cref="BackgroundProfile"/> 翻译成
/// Avalonia 的画笔、拉伸方式、暗化色与模糊效果。本类不碰控件，纯函数式，方便 headless 单测。
/// </summary>
public static class BackgroundBrushFactory
{
    /// <summary>
    /// 「表面洗白」的不透明度：背景之上再盖一层主题底色，压到 74% 让底图隐约透出来。
    /// 取 0.74 而不是更低，是因为正文小字要在任何背景下都保持可读；这一层与暗化叠加后
    /// 实际对比度远高于 4.5:1（THM-02 的门禁只管强调色，这里是背景自带的兜底）。
    /// </summary>
    public const double SurfaceWashAlpha = 0.74;

    /// <summary>解析 <c>#RRGGBB</c>（复用强调色的解析器，两处不各写一份十六进制规则）。</summary>
    public static bool TryParseColor(string? text, out Color color)
    {
        if (AccentContrast.TryParse(text, out var rgb))
        {
            color = Color.FromRgb(rgb.R, rgb.G, rgb.B);
            return true;
        }

        color = default;
        return false;
    }

    /// <summary>
    /// 纯色/渐变来源的层画笔；图片来源与 <see cref="BackgroundProfile.KindNone"/> 返回 <c>null</c>
    /// （图片走 <see cref="CreateImageBrush"/> 或 <c>Image</c> 控件）。
    /// </summary>
    public static IBrush? CreateLayerBrush(BackgroundProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);

        if (string.Equals(profile.Kind, BackgroundProfile.KindSolid, StringComparison.OrdinalIgnoreCase))
        {
            return TryParseColor(profile.Color, out var solid) ? new SolidColorBrush(solid) : null;
        }

        if (!string.Equals(profile.Kind, BackgroundProfile.KindGradient, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var startOk = TryParseColor(profile.GradientStart, out var start);
        var endOk = TryParseColor(profile.GradientEnd, out var end);
        if (!startOk || !endOk)
        {
            return null;
        }

        return CreateGradient(start, end, profile.GradientAngle);
    }

    /// <summary>
    /// 按角度生成线性渐变：0° 向右、90° 向下（与 CSS <c>linear-gradient</c> 的直觉一致）。
    /// 做法是把角度换成单位向量，再以画布中心为原点取对称的两点。
    /// </summary>
    public static LinearGradientBrush CreateGradient(Color start, Color end, double angle)
    {
        var radians = angle * Math.PI / 180d;
        var dx = Math.Cos(radians);
        var dy = Math.Sin(radians);

        return new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0.5 - (dx * 0.5), 0.5 - (dy * 0.5), RelativeUnit.Relative),
            EndPoint = new RelativePoint(0.5 + (dx * 0.5), 0.5 + (dy * 0.5), RelativeUnit.Relative),
            GradientStops = new GradientStops
            {
                new GradientStop(start, 0),
                new GradientStop(end, 1),
            },
        };
    }

    /// <summary>平铺来源要按原图尺寸重复，其余来源交给 <c>Image</c> 控件的 <see cref="Stretch"/>。</summary>
    public static bool IsTiled(BackgroundProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        return string.Equals(profile.Fill, BackgroundProfile.FillTile, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>平铺画笔：用原图像素尺寸做 <c>DestinationRect</c>，TileMode.Tile 才能按原尺寸铺。</summary>
    public static IBrush? CreateTileBrush(Bitmap? bitmap)
    {
        if (bitmap is null)
        {
            return null;
        }

        var size = bitmap.PixelSize;
        if (size.Width <= 0 || size.Height <= 0)
        {
            return null;
        }

        return new ImageBrush(bitmap)
        {
            TileMode = TileMode.Tile,
            Stretch = Stretch.Fill,
            SourceRect = new RelativeRect(0, 0, 1, 1, RelativeUnit.Relative),
            DestinationRect = new RelativeRect(0, 0, size.Width, size.Height, RelativeUnit.Absolute),
        };
    }

    /// <summary>填充方式 → <c>Image</c> 拉伸方式；平铺另有画笔，这里给 <see cref="Stretch.None"/> 兜底。</summary>
    public static Stretch ToStretch(string? fill) => fill?.ToLowerInvariant() switch
    {
        BackgroundProfile.FillFit => Stretch.Uniform,
        BackgroundProfile.FillStretch => Stretch.Fill,
        BackgroundProfile.FillCenter => Stretch.None,
        BackgroundProfile.FillTile => Stretch.None,
        _ => Stretch.UniformToFill,
    };

    /// <summary>暗化层颜色：黑色按百分比取 alpha（0 就是完全透明）。</summary>
    public static Color DimColor(BackgroundProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        var percent = Math.Clamp(profile.DimPercent, 0, 100);
        return Color.FromArgb((byte)Math.Round(percent * 255d / 100d), 0, 0, 0);
    }

    /// <summary>背景层整体不透明度（0–1）。</summary>
    public static double ToOpacity(BackgroundProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        return Math.Clamp(profile.OpacityPercent, 0, 100) / 100d;
    }

    /// <summary>模糊效果；半径 0 返回 <c>null</c>（给了 <c>BlurEffect</c> 会强制开一个渲染通道）。</summary>
    public static IEffect? CreateBlur(BackgroundProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        var radius = double.IsFinite(profile.BlurRadius) ? Math.Clamp(profile.BlurRadius, 0, 64) : 0;
        return radius > 0 ? new BlurEffect { Radius = radius } : null;
    }
}
