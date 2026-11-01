using System;
using System.Globalization;
using Avalonia.Data.Converters;

namespace CkanUI.App.Converters;

/// <summary>
/// CKAN 元数据里的稳定标识（stable / metapackage…）不该直接露给用户。
/// 这里只做显示层翻译，绑定值仍然保留原始字符串，写回文件时不受影响。
/// </summary>
public sealed class ModPackTermConverter : IValueConverter
{
    public static readonly ModPackTermConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => value switch
    {
        "stable" => "稳定版",
        "testing" => "测试版",
        "development" => "开发版",
        "metapackage" => "元包",
        "package" => "内容包",
        "dlc" => "官方 DLC",
        _ => value,
    };

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException("显示层转换不支持反向写回。");
}
