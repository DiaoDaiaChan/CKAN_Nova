using System.Globalization;

namespace CkanUI.ViewModels.Pages.Stats;

/// <summary>下载统计页的数值格式化（12 篇 F-47）：字节用 IEC 单位，计数用千位分隔。</summary>
public static class DownloadStatsFormat
{
    /// <summary>把字节数格式化为 <c>B/KiB/MiB/GiB</c>。</summary>
    public static string Bytes(long bytes)
    {
        if (bytes < 0)
        {
            return "—";
        }

        if (bytes < 1024)
        {
            return string.Create(CultureInfo.InvariantCulture, $"{bytes} B");
        }

        var value = (double)bytes;
        var units = new[] { "KiB", "MiB", "GiB", "TiB" };
        var unit = 0;
        value /= 1024d;
        while (value >= 1024d && unit < units.Length - 1)
        {
            value /= 1024d;
            unit++;
        }

        return string.Create(CultureInfo.InvariantCulture, $"{value:0.#} {units[unit]}");
    }

    /// <summary>把计数格式化为带千位分隔的整数。</summary>
    public static string Count(long value) =>
        value.ToString("N0", CultureInfo.CurrentCulture);

    /// <summary>把百分比格式化为 <c>12.3%</c>。</summary>
    public static string Percent(double percent) =>
        string.Create(CultureInfo.InvariantCulture, $"{percent:0.#}%");
}
