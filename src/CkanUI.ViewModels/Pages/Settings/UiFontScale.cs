using System.Globalization;

namespace CkanUI.ViewModels.Pages.Settings;

/// <summary>
/// 字号缩放五档（20 篇 P-09 / THM-06）：90% / 100% / 110% / 120% / 130%。
/// </summary>
/// <remarks>
/// 与 <see cref="DensityScale"/> 同样的分层理由：<c>UiSettings.FontScale</c> 是 VM 层数据，
/// 映射本身要能 headless 断言，真正写资源字典的是 App 层 <c>FontScaleHost</c>。
/// <para>
/// 只作用于界面文案 token（<c>CkanFontSizeCaption/Body/Subtitle/Heading/Title</c>），
/// 不作用于数据文本：DataGrid 单元格与徽章用的是固定字号，认证规则见 20 篇 §3 原则 5 与风险 R-80。
/// </para>
/// </remarks>
public static class UiFontScale
{
    /// <summary>最小档 90%。</summary>
    public const string Small = "90";

    /// <summary>标准档（缺省）100%。</summary>
    public const string Standard = "100";

    /// <summary>偏大档 110%。</summary>
    public const string Large = "110";

    /// <summary>更大档 120%。</summary>
    public const string Larger = "120";

    /// <summary>最大档 130%。</summary>
    public const string Largest = "130";

    /// <summary>五档取值，顺序即界面展示顺序。</summary>
    public static readonly string[] All = { Small, Standard, Large, Larger, Largest };

    /// <summary>把任意字符串规整成五档之一：先解析百分比，取最接近的一档；解析不出则回落 100%。</summary>
    /// <param name="scale">原始取值（"110"、"1.1"、"110%" 之类都接受）。</param>
    public static string Normalize(string? scale)
    {
        if (string.IsNullOrWhiteSpace(scale))
        {
            return Standard;
        }

        var text = scale.Trim().TrimEnd('%').Trim();
        if (!double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
        {
            return Standard;
        }

        // 兼容「1.1」这种倍率写法：小于 5 的值按倍率理解。
        if (value > 0 && value < 5)
        {
            value *= 100;
        }

        var best = All[0];
        var bestDistance = double.MaxValue;
        foreach (var candidate in All)
        {
            var distance = Math.Abs(int.Parse(candidate, CultureInfo.InvariantCulture) - value);
            if (distance < bestDistance)
            {
                best = candidate;
                bestDistance = distance;
            }
        }

        return best;
    }

    /// <summary>档位对应的缩放倍率（1.0 = 100%）。</summary>
    /// <param name="scale">原始取值。</param>
    public static double Factor(string? scale) =>
        int.Parse(Normalize(scale), CultureInfo.InvariantCulture) / 100.0;

    /// <summary>把基准字号乘以档位倍率，取到 0.5 磅的整数倍，避免半像素模糊。</summary>
    /// <param name="baseSize">Tokens.axaml 里的基准字号。</param>
    /// <param name="scale">原始取值。</param>
    public static double Apply(double baseSize, string? scale)
    {
        var scaled = baseSize * Factor(scale);
        return Math.Round(scaled * 2, MidpointRounding.AwayFromZero) / 2;
    }
}
