using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace CkanUI.App.Converters;

/// <summary>
/// 十六进制颜色文本 → 画刷（Mod 配置 color 字段的色块预览）。
/// </summary>
/// <remarks>
/// 只做"能不能画"的判定：解析不了就返回透明画刷，行内会另有说明文字如实告诉用户
/// "仍按原文写入"，所以这里不再抛异常、也不弹提示（取色是辅助，不是拦路门禁）。
/// </remarks>
public sealed class HexColorToBrushConverter : IValueConverter
{
    /// <summary>解析失败时的占位画刷（透明，露出底下的输入框背景）。</summary>
    public static readonly IBrush Fallback = Brushes.Transparent;

    /// <inheritdoc />
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not string text || string.IsNullOrWhiteSpace(text))
        {
            return Fallback;
        }

        return Color.TryParse(text.Trim(), out var color) ? new SolidColorBrush(color) : Fallback;
    }

    /// <inheritdoc />
    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException("色块预览是单向绑定。");
}
