using System.Globalization;

namespace CkanUI.Services.Abstractions.ModConfig;

/// <summary>
/// 比较字段值与声明的默认值。ConfigNode 的文本写法不唯一（`true`/`True`、`2.7`/`2.7f`），
/// 按字节比较会把用户没动过的项误标成「已修改」，所以判定统一走这里。
/// </summary>
public static class ModConfigValueComparer
{
    /// <summary><paramref name="actual"/> 是否与 <paramref name="expected"/> 等价。</summary>
    public static bool AreEqual(ModConfigFieldType type, string? actual, string? expected)
    {
        if (actual is null || expected is null)
        {
            return actual is null && expected is null;
        }

        var left = actual.Trim();
        var right = expected.Trim();

        switch (type)
        {
            case ModConfigFieldType.Bool:
                return string.Equals(left, right, StringComparison.OrdinalIgnoreCase);

            case ModConfigFieldType.Float when TryParseNumber(left, out var leftValue) && TryParseNumber(right, out var rightValue):
                return leftValue.Equals(rightValue);

            default:
                return string.Equals(left, right, StringComparison.Ordinal);
        }
    }

    /// <summary>解析数值原文，允许 Unity 风格的 f/F 后缀（实测 SystemHeat 的 "2.7f"）。</summary>
    public static bool TryParseNumber(string text, out double value)
    {
        var numeric = text.Trim();
        if (numeric.Length > 1 && (numeric.EndsWith('f') || numeric.EndsWith('F')))
        {
            numeric = numeric[..^1];
        }

        return double.TryParse(numeric, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
    }
}
