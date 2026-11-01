using System;
using System.Globalization;
using Avalonia.Data.Converters;

namespace CkanUI.App.Converters;

/// <summary>
/// 判断绑定值是否等于 ConverterParameter 指定的枚举名，用于把「当前维度 / 当前排序列」这类单选状态
/// 映射到 x:Classes 上（例如 Button.CkanChip.Active）。
/// </summary>
public sealed class EnumEqualsConverter : IValueConverter
{
    public static readonly EnumEqualsConverter Instance = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is not null
           && parameter is not null
           && string.Equals(value.ToString(), parameter.ToString(), StringComparison.OrdinalIgnoreCase);

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
