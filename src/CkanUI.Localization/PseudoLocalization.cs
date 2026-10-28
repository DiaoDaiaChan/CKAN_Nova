using System.Text;

namespace CkanUI.Localization;

/// <summary>
/// 伪本地化（16 篇 §7.3）：把界面文案变成 [!! Šéttíngš·!!] 形状并加长约 30%。
/// </summary>
/// <remarks>
/// 为什么需要它：中文用户看不出"某条文案其实没翻译"，因为回退链会把英文原文递出来；
/// 伪本地化把每条真正命中的译文都包起来，未命中的 key 保持原样（Ui_NavRail_X 一眼可见），
/// 于是"漏译""文案被截断""硬编码在视图里的字符串"三类问题在同一张截图里全部暴露。
/// 只影响显示文本（Get 结果与显示快照），覆盖率与缺译计数不因伪本地化改变——
/// 覆盖率统计的是语言包里的键，与变形无关。
/// 加壳用 ASCII 方括号而不是全角符号——方括号在任何字体下都有字形，不会自己制造豆腐块。
/// </remarks>
public static class PseudoLocalization
{
    /// <summary>伪本地化语言标识（与 ICU 惯例一致）；仅用于让开关可被识别，不进语言下拉。</summary>
    public const string PseudoLocale = "qps-ploc";

    private const string Open = "[!! ";
    private const string Close = " !!]";
    private const char Pad = '·';
    private const double ExpansionRatio = 1.3d;

    // 只重写 ASCII 字母：CJK/西里尔等字形保持不变，正好让"这一条是中文还是没翻译"一眼可辨。
    private const string LowerSource = "abcdefghijklmnopqrstuvwxyz";
    private const string LowerTarget = "áƀçðéƒĝĥíĵķļɱñóþǫŕšţúṽŵẋýž";

    /// <summary>语言标识是否等于伪本地化标识。</summary>
    /// <param name="locale">语言标识。</param>
    public static bool IsPseudoLocale(string? locale) =>
        string.Equals(locale?.Trim(), PseudoLocale, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// 把一条译文变成伪本地化文本：字母加重音、外包 [!! … !!]、按 30% 比例补填充符号。
    /// 空值返回空串（不制造假文案）。
    /// </summary>
    /// <param name="value">原文。</param>
    public static string Transform(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        var body = Accent(value);
        var target = (int)Math.Ceiling(value.Length * ExpansionRatio);
        var builder = new StringBuilder(Open.Length + target + Close.Length + 8);
        builder.Append(Open).Append(body);
        // 只补到 30% 膨胀量为止：补过头会让本来不溢出的布局也溢出，失去"只找真问题"的意义。
        for (var i = body.Length; i < target; i++)
        {
            builder.Append(Pad);
        }

        builder.Append(Close);
        return builder.ToString();
    }

    private static string Accent(string value)
    {
        var builder = new StringBuilder(value.Length);
        var depth = 0;

        foreach (var ch in value)
        {
            // 花括号里是占位符（如 {0:N0}），一旦被改写就会让 string.Format 抛 FormatException，
            // 所以逐字符跟踪深度：括号内的内容原样保留，这也是伪本地化能跑真实格式化路径的前提。
            if (ch == '{')
            {
                depth++;
            }
            else if (ch == '}' && depth > 0)
            {
                depth--;
            }

            if (depth > 0 || !char.IsAsciiLetter(ch))
            {
                builder.Append(ch);
                continue;
            }

            var lower = char.ToLowerInvariant(ch);
            var index = LowerSource.IndexOf(lower, StringComparison.Ordinal);
            if (index < 0)
            {
                builder.Append(ch);
                continue;
            }

            var mapped = LowerTarget[index];
            builder.Append(char.IsUpper(ch) ? char.ToUpperInvariant(mapped) : mapped);
        }

        return builder.ToString();
    }
}
