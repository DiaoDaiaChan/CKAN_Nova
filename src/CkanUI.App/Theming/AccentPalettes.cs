namespace CkanUI.App.Theming;

/// <summary>
/// 预设强调色定义（20 篇 P-03、§6.1）。
/// 包含 12 种精心调配的预设强调色，每种提供浅色底与深色底的最佳十六进制色值，以及中英文显示名。
/// </summary>
/// <remarks>
/// 所有色值都必须与所在主题的底色（<c>CkanSurfaceBackgroundBrush</c>）达到 WCAG AA 正文阈值 4.5:1：
/// 浅色档的橙/琥珀/黄绿/绿/青碧/天蓝六个色原先只有 3.1–4.1:1，压在上面的白字读不清，已按同色系压深。
/// <c>tests/CkanUI.App.Tests/AccentContrastThemeTests.cs</c> 对整张色板做守卫，改色必须先过那条测试。
/// </remarks>
public sealed record AccentPreset(
    string Key,
    string DisplayName,
    string LightHex,
    string DarkHex);

public static class AccentPalettes
{
    public const string DefaultKey = "default";

    public static readonly IReadOnlyList<AccentPreset> Presets = new AccentPreset[]
    {
        new("blue", "经典蓝 / Blue", "#0A66C2", "#4C9AFF"),
        new("indigo", "靛青 / Indigo", "#4F46E5", "#818CF8"),
        new("purple", "星云紫 / Purple", "#7C3AED", "#A78BFA"),
        new("magenta", "品红 / Magenta", "#C026D3", "#E879F9"),
        new("red", "火箭红 / Red", "#DC2626", "#F87171"),
        new("orange", "轨道橙 / Orange", "#C2410C", "#FB923C"),
        new("amber", "琥珀金 / Amber", "#B45309", "#FBBF24"),
        new("lime", "坎星绿 / Lime", "#4D7C0F", "#A3E635"),
        new("green", "极光绿 / Green", "#15803D", "#4ADE80"),
        new("teal", "青碧 / Teal", "#0F766E", "#2DD4BF"),
        new("cyan", "天蓝 / Cyan", "#0369A1", "#38BDF8"),
        new("slate", "灰蓝 / Slate", "#475569", "#94A3B8")
    };

    public static IReadOnlyList<AccentPreset> All => Presets;

    public static AccentPreset? FindPreset(string? key)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            return null;
        }

        var trimmed = key.Trim();
        if (string.Equals(trimmed, DefaultKey, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return Presets.FirstOrDefault(p => string.Equals(p.Key, trimmed, StringComparison.OrdinalIgnoreCase));
    }

    public static bool TryGet(string? key, out AccentPreset? preset)
    {
        preset = FindPreset(key);
        return preset is not null;
    }
}
