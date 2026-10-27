using System.Globalization;

namespace CkanUI.ViewModels.Pages.Mods;

/// <summary>列表与状态栏的数字/体积格式化（03 篇 §11：格式化集中一处，XAML 里不写格式串）。</summary>
public static class ModsFormatter
{
    private static readonly string[] Units = { "B", "KB", "MB", "GB", "TB" };

    /// <summary>千分位计数。</summary>
    public static string Count(long value) => value.ToString("N0", CultureInfo.CurrentCulture);

    /// <summary>体积格式化（1024 进制，一位小数）；null 显示为"—"。</summary>
    public static string Bytes(long? bytes)
    {
        if (bytes is not { } value || value <= 0)
        {
            return "—";
        }

        double size = value;
        var unit = 0;
        while (size >= 1024 && unit < Units.Length - 1)
        {
            size /= 1024;
            unit++;
        }

        return unit == 0
            ? string.Create(CultureInfo.CurrentCulture, $"{value} {Units[unit]}")
            : string.Create(CultureInfo.CurrentCulture, $"{size:0.0} {Units[unit]}");
    }

    /// <summary>已装/最新版本并排展示。</summary>
    public static string Versions(string? installed, string? latest)
    {
        var installedText = string.IsNullOrWhiteSpace(installed) ? "未安装" : installed;
        var latestText = string.IsNullOrWhiteSpace(latest) ? "—" : latest;
        return $"已装 {installedText} · 最新 {latestText}";
    }

    /// <summary>状态栏摘要：显示数 / 总数 · 已选 · 变更。</summary>
    public static string StatusLine(int shown, int total, int selected, int installs, int removals) =>
        $"显示 {Count(shown)} / {Count(total)} 个模组 · 已选 {selected} · 变更 +{installs}/-{removals}";
}
