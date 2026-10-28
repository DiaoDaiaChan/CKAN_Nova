using System.Globalization;

namespace CkanUI.ViewModels.Pages.Settings;

/// <summary>强调色的 RGB 分量（20 篇 P-03）。VM 层不引用 Avalonia，故自带一个最小色值类型。</summary>
/// <param name="R">红通道。</param>
/// <param name="G">绿通道。</param>
/// <param name="B">蓝通道。</param>
public readonly record struct AccentRgb(byte R, byte G, byte B);

/// <summary>强调色校验结论。</summary>
/// <param name="IsValid">是否通过对比度门禁。</param>
/// <param name="Ratio">与当前主题底色的 WCAG 对比度。</param>
/// <param name="Color">解析出来的颜色（无法解析时为默认值）。</param>
/// <param name="Suggested">不合规时的建议色；合规时为 null。</param>
/// <param name="Message">给用户看的结论；合规时为空串。</param>
public sealed record AccentContrastVerdict(
    bool IsValid,
    double Ratio,
    AccentRgb Color,
    AccentRgb? Suggested,
    string Message);

/// <summary>
/// 强调色的对比度门禁（20 篇 P-03 / THM-02）：自定义 Hex 必须与当前主题的文字底色达到
/// WCAG 2.2 AA 正文阈值（4.5:1），不合规就拒绝写入并给出同色系建议色。
/// </summary>
/// <remarks>
/// <para>
/// 「文字底色」取 <c>src/CkanUI.App/Resources/Tokens.axaml</c> 里各主题的
/// <c>CkanSurfaceBackgroundBrush</c>；强调色在界面上既当填充（上面压 <c>CkanTextOnAccentBrush</c>）
/// 也当链接文字，两种用法的阈值相同，取底色一个值即可。三个常量由
/// <c>tests/CkanUI.App.Tests/AccentContrastThemeTests.cs</c> 对着 Tokens.axaml 守卫，改色必须同步。
/// </para>
/// <para>
/// 浅色与深色主题的约束方向相反（浅色要深、深色要亮），因此「跟随系统」主题只能按当前生效的
/// 那一档校验：调用方通过 <c>SettingsPageViewModel.DarkThemeProbe</c> 把界面的真实变体告诉这里。
/// </para>
/// </remarks>
public static class AccentContrast
{
    /// <summary>WCAG 2.2 AA 正文阈值。</summary>
    public const double MinimumRatio = 4.5;

    /// <summary>
    /// 浅色主题的底色：取卡片表面 #FFFFFF（第二阶段表面分层后画布压到 #F4F6F9，但强调色文字
    /// 绝大多数落在卡片上，按卡片算才是真实阅读场景）。
    /// </summary>
    public static readonly AccentRgb LightPole = new(0xFF, 0xFF, 0xFF);

    /// <summary>深色主题的底色：取卡片表面 #151921（同上，画布 #0D1015 更深、对比度只会更高）。</summary>
    public static readonly AccentRgb DarkPole = new(0x15, 0x19, 0x21);

    /// <summary>高对比度主题的底色（Tokens.axaml <c>CkanSurfaceBackgroundBrush</c> #000000）。</summary>
    public static readonly AccentRgb HighContrastPole = new(0x00, 0x00, 0x00);

    /// <summary>把主题键归一成 <c>light</c> / <c>dark</c> / <c>highcontrast</c> 三档。</summary>
    /// <param name="themeKey">设置里存的主题值（system / light / dark / highcontrast）。</param>
    /// <param name="darkActive">跟随系统时界面当前是否深色。</param>
    public static string NormalizeTheme(string? themeKey, bool darkActive)
    {
        var key = themeKey?.Trim().ToLowerInvariant();
        return key switch
        {
            "light" => "light",
            "dark" => "dark",
            "highcontrast" or "high-contrast" or "contrast" => "highcontrast",
            _ => darkActive ? "dark" : "light",
        };
    }

    /// <summary>主题底色。</summary>
    /// <param name="themeKey">已归一化的主题键。</param>
    public static AccentRgb Pole(string themeKey) => themeKey switch
    {
        "light" => LightPole,
        "highcontrast" => HighContrastPole,
        _ => DarkPole,
    };

    /// <summary>主题的中文名（拼提示文案用）。</summary>
    /// <param name="themeKey">已归一化的主题键。</param>
    public static string ThemeDisplayName(string themeKey) => themeKey switch
    {
        "light" => "浅色",
        "highcontrast" => "高对比度",
        _ => "深色",
    };

    /// <summary>解析 <c>#RRGGBB</c> 或 <c>RRGGBB</c>（大小写不敏感）。</summary>
    /// <param name="text">用户输入。</param>
    /// <param name="rgb">解析结果。</param>
    public static bool TryParse(string? text, out AccentRgb rgb)
    {
        rgb = default;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var value = text.Trim();
        if (value.StartsWith('#'))
        {
            value = value[1..];
        }

        if (value.Length != 6 || !int.TryParse(value, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var packed))
        {
            return false;
        }

        rgb = new AccentRgb((byte)((packed >> 16) & 0xFF), (byte)((packed >> 8) & 0xFF), (byte)(packed & 0xFF));
        return true;
    }

    /// <summary>格式化成 <c>#RRGGBB</c>（大写，便于落盘后比较）。</summary>
    /// <param name="rgb">颜色。</param>
    public static string ToHex(AccentRgb rgb) =>
        string.Create(CultureInfo.InvariantCulture, $"#{rgb.R:X2}{rgb.G:X2}{rgb.B:X2}");

    /// <summary>WCAG 相对亮度。</summary>
    /// <param name="color">颜色。</param>
    public static double RelativeLuminance(AccentRgb color) =>
        (0.2126 * Linearize(color.R)) + (0.7152 * Linearize(color.G)) + (0.0722 * Linearize(color.B));

    /// <summary>WCAG 对比度（1:1 到 21:1）。</summary>
    /// <param name="left">颜色一。</param>
    /// <param name="right">颜色二。</param>
    public static double Ratio(AccentRgb left, AccentRgb right)
    {
        var a = RelativeLuminance(left);
        var b = RelativeLuminance(right);
        var lighter = Math.Max(a, b);
        var darker = Math.Min(a, b);
        return (lighter + 0.05) / (darker + 0.05);
    }

    /// <summary>
    /// 找同色系里合规的替代色：保持色相，只沿亮度往底色反方向推（深色主题往白推、浅色主题往黑推），
    /// 取第一个达标的点，避免「建议色」和用户选的颜色毫无关系。
    /// </summary>
    /// <param name="desired">用户想要的颜色。</param>
    /// <param name="pole">主题底色。</param>
    public static AccentRgb Suggest(AccentRgb desired, AccentRgb pole)
    {
        var towardWhite = RelativeLuminance(pole) < 0.5;
        var target = towardWhite ? new AccentRgb(0xFF, 0xFF, 0xFF) : new AccentRgb(0x00, 0x00, 0x00);

        for (var step = 1; step <= 100; step++)
        {
            var candidate = Blend(desired, target, step / 100.0);
            if (Ratio(candidate, pole) >= MinimumRatio)
            {
                return candidate;
            }
        }

        return target;
    }

    /// <summary>校验一个十六进制强调色。</summary>
    /// <param name="text">用户输入（#RRGGBB / RRGGBB）。</param>
    /// <param name="themeKey">设置里存的主题值。</param>
    /// <param name="darkActive">跟随系统时界面当前是否深色。</param>
    public static AccentContrastVerdict Validate(string? text, string? themeKey, bool darkActive)
    {
        if (!TryParse(text, out var rgb))
        {
            return new AccentContrastVerdict(
                false,
                0,
                default,
                null,
                $"强调色需要 #RRGGBB 形式的十六进制（例如 #1F6FEB），当前是「{text?.Trim() ?? string.Empty}」。");
        }

        var key = NormalizeTheme(themeKey, darkActive);
        var pole = Pole(key);
        var ratio = Ratio(rgb, pole);
        if (ratio >= MinimumRatio)
        {
            return new AccentContrastVerdict(true, ratio, rgb, null, string.Empty);
        }

        var suggested = Suggest(rgb, pole);
        var message = string.Create(
            CultureInfo.InvariantCulture,
            $"与{ThemeDisplayName(key)}主题底色的对比度只有 {ratio:0.00}:1，低于 AA 正文阈值 {MinimumRatio:0.0}:1；建议改用 {ToHex(suggested)}。");
        return new AccentContrastVerdict(false, ratio, rgb, suggested, message);
    }

    /// <summary>一个十六进制颜色是否合规（不做建议色计算）。</summary>
    /// <param name="text">用户输入。</param>
    /// <param name="themeKey">设置里存的主题值。</param>
    /// <param name="darkActive">跟随系统时界面当前是否深色。</param>
    public static bool IsCompliant(string? text, string? themeKey, bool darkActive) =>
        TryParse(text, out var rgb) && Ratio(rgb, Pole(NormalizeTheme(themeKey, darkActive))) >= MinimumRatio;

    private static double Linearize(byte channel)
    {
        var value = channel / 255.0;
        return value <= 0.03928 ? value / 12.92 : Math.Pow((value + 0.055) / 1.055, 2.4);
    }

    private static AccentRgb Blend(AccentRgb from, AccentRgb to, double amount) => new(
        (byte)Math.Round(from.R + ((to.R - from.R) * amount)),
        (byte)Math.Round(from.G + ((to.G - from.G) * amount)),
        (byte)Math.Round(from.B + ((to.B - from.B) * amount)));
}
