using System.Globalization;

namespace CkanUI.ViewModels.Dialogs;

/// <summary>详情面板的数字/体积格式化（03 篇 §11：格式化集中一处，不散落到 XAML 的 StringFormat 里）。</summary>
internal static class ModDetailsFormatter
{
    /// <summary>体积格式化：未知/0 显示"未知"，否则按 1024 进制升单位，保留一位小数。</summary>
    internal static string FormatSize(long? bytes)
    {
        if (bytes is null || bytes.Value <= 0)
        {
            return "未知";
        }

        long value = bytes.Value;
        if (value < 1024)
        {
            return value.ToString(CultureInfo.CurrentCulture) + " B";
        }

        string[] units = ["KB", "MB", "GB", "TB"];
        double size = value;
        var unit = -1;
        while (size >= 1024 && unit < units.Length - 1)
        {
            size /= 1024;
            unit++;
        }

        return size.ToString("0.#", CultureInfo.CurrentCulture) + " " + units[unit];
    }

    /// <summary>版本号格式化：空值显示破折号，避免详情面板出现空行。</summary>
    internal static string FormatVersion(string? version) =>
        string.IsNullOrWhiteSpace(version) ? "—" : version;
}
