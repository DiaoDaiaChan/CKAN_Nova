using System.Globalization;

namespace CkanUI.ViewModels.Pages.ModConfig;

/// <summary>
/// 颜色字段的文本规整（Mod 配置适配层的 color 类型）。
/// </summary>
/// <remarks>
/// <para>KSP 的配置文件里颜色写法五花八门：<c>#RRGGBB</c>、<c>#AARRGGBB</c>、<c>#RGB</c>、
/// 甚至 <c>0.2,0.4,0.8</c> 这种逗号三元组。页面<b>不拦</b>任何写法——写盘时服务层只校验
/// "不能为空 / 不能带换行"（ModConfigDetector.ValidateFieldValue），这里仅做两件事：
/// 能识别就给出可预览的 <c>#RRGGBB</c>/<c>#AARRGGBB</c>，识别不了就如实说明"仍按原文写入"。</para>
/// <para>刻意不做"替用户改写"：配置可能被 mod 自己的解析器读，改成规范写法等于替用户改了语义。</para>
/// </remarks>
public static class ModConfigColorText
{
    /// <summary>把草稿文本规整成 Avalonia 能解析的十六进制写法。</summary>
    /// <param name="text">草稿文本。</param>
    /// <param name="hex">规整后的 <c>#RRGGBB</c> 或 <c>#AARRGGBB</c>。</param>
    /// <returns>是否识别成功。</returns>
    public static bool TryNormalize(string? text, out string hex)
    {
        hex = string.Empty;
        var raw = text?.Trim();
        if (string.IsNullOrEmpty(raw))
        {
            return false;
        }

        if (raw[0] == '#')
        {
            var digits = raw[1..];
            if ((digits.Length is 3 or 4 or 6 or 8) && digits.All(Uri.IsHexDigit))
            {
                hex = "#" + Expand(digits);
                return true;
            }

            return false;
        }

        // 逗号写法：0.2,0.4,0.8 / 0.8,0.2,0.4,1（KSP 的浮点约定）或 51,102,204 / 51,102,204,255（0~255）。
        // 整组都 ≤1 就按 0~1 解释（0.8 表示 80%），否则按 0~255 解释——两种约定不会自相矛盾。
        var parts = raw.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length is not (3 or 4))
        {
            return false;
        }

        var values = new double[parts.Length];
        for (var i = 0; i < parts.Length; i++)
        {
            if (!double.TryParse(parts[i], NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
                || double.IsNaN(value)
                || value < 0d)
            {
                return false;
            }

            values[i] = value;
        }

        var fractional = values.All(value => value <= 1d);
        var channels = values
            .Select(value => (int)Math.Round(
                fractional ? value * 255d : value,
                MidpointRounding.AwayFromZero))
            .ToArray();

        if (channels.Any(channel => channel is < 0 or > 255))
        {
            return false;
        }

        // r,g,b,a 的 a 在前者（#AARRGGBB），没给 a 就是 #RRGGBB。
        hex = channels.Length == 4
            ? "#" + string.Concat(
                new[] { channels[3], channels[0], channels[1], channels[2] }
                    .Select(channel => channel.ToString("X2", CultureInfo.InvariantCulture)))
            : "#" + string.Concat(channels.Select(channel => channel.ToString("X2", CultureInfo.InvariantCulture)));
        return true;
    }

    /// <summary>行内说明：空值 / 无法识别时给一句人话，识别成功时为空串（不占版面）。</summary>
    /// <param name="text">草稿文本。</param>
    public static string Describe(string? text)
    {
        var raw = text?.Trim();
        if (string.IsNullOrEmpty(raw))
        {
            return "尚未设置，可填 #RRGGBB 或 #AARRGGBB。";
        }

        return TryNormalize(raw, out _)
            ? string.Empty
            : "无法识别为颜色，仍会按原文写入（支持 #RGB / #RRGGBB / #AARRGGBB / r,g,b）。";
    }

    private static string Expand(string digits) => digits.Length switch
    {
        // #RGB/#RGBA 每个通道只有一位：补成两位（F → FF）。
        3 or 4 => string.Concat(digits.Select(c => new string(c, 2))),
        _ => digits,
    };
}
