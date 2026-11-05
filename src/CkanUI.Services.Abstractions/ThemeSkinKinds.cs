namespace CkanUI.Services.Abstractions;

/// <summary>
/// 主题皮肤的可选值。皮肤是深色主题上的整体色板替换：画布、表面、文字与强调色一起换，
/// 浅色/高对比度仍使用基础主题，避免在两种极端对比度下做六套重复校验。
/// </summary>
public static class ThemeSkinKinds
{
    public const string Default = "default";
    public const string CoronaGold = "corona-gold";
    public const string LunarGraphite = "lunar-graphite";
    public const string Celadon = "celadon";
    public const string Copper = "copper";
    public const string NebulaViolet = "nebula-violet";
    public const string MonochromeSilver = "monochrome-silver";

    /// <summary>设置页下拉框顺序；第一项是默认深空蓝。</summary>
    public static readonly string[] All =
    [
        Default,
        CoronaGold,
        LunarGraphite,
        Celadon,
        Copper,
        NebulaViolet,
        MonochromeSilver
    ];

    /// <summary>把外部输入夹进已知值：空值/未知值回到默认皮肤，避免旧配置把主题画成空白。</summary>
    public static string Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return Default;
        }

        foreach (var candidate in All)
        {
            if (string.Equals(candidate, value, StringComparison.OrdinalIgnoreCase))
            {
                return candidate;
            }
        }

        return Default;
    }

    public static bool IsDefault(string? value) =>
        string.Equals(Normalize(value), Default, StringComparison.Ordinal);
}
