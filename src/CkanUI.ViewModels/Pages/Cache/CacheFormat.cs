using System.Globalization;

namespace CkanUI.ViewModels.Pages.Cache;

/// <summary>下载缓存页的尺寸/时间/百分比文案（03 篇 §17：界面只展示人读文本，格式化集中在这里）。</summary>
public static class CacheFormat
{
    /// <summary>把字节数格式化成 KB/MB/GB（一位小数，固定不变文化）。</summary>
    public static string FormatSize(long bytes)
    {
        if (bytes < 1024) return bytes.ToString(CultureInfo.InvariantCulture) + " B";
        if (bytes < 1024L * 1024) return (bytes / 1024d).ToString("0.#", CultureInfo.InvariantCulture) + " KB";
        if (bytes < 1024L * 1024 * 1024) return (bytes / (1024d * 1024)).ToString("0.#", CultureInfo.InvariantCulture) + " MB";
        return (bytes / (1024d * 1024 * 1024)).ToString("0.##", CultureInfo.InvariantCulture) + " GB";
    }

    /// <summary>把 UTC 时间格式成本地时间文案；未知时返回「未知」。</summary>
    public static string FormatTime(DateTime utc)
        => utc == default ? "未知" : utc.ToLocalTime().ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);

    /// <summary>容量上限文案；<c>null</c> 或非正数表示「不限制」。</summary>
    public static string FormatLimit(long? bytes)
        => bytes is { } limit && limit > 0 ? FormatSize(limit) : "不限制";

    /// <summary>使用率文案（一位小数）；未设上限时返回「—」。</summary>
    public static string FormatPercent(double percent)
        => percent <= 0d ? "0%" : percent.ToString("0.#", CultureInfo.InvariantCulture) + "%";
}
