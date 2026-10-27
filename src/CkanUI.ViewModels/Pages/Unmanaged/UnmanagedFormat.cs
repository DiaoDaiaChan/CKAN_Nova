using System.Globalization;

namespace CkanUI.ViewModels.Pages.Unmanaged;

/// <summary>未管理文件页的尺寸与时间文案（03 篇 §17：界面只展示人读文本，格式化集中在这里）。</summary>
public static class UnmanagedFormat
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
    public static string FormatTime(DateTime? utc) =>
        utc is { } value ? value.ToLocalTime().ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture) : "未知";
}
