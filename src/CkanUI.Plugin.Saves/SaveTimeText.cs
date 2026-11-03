using System.Globalization;

namespace CkanUI.Plugin.Saves;

/// <summary>
/// 存档页的纯格式化函数（03 篇 §14 存档工作台列表列）：KSP 游戏内时间、文件体积、资金。
/// </summary>
/// <remarks>
/// KSP 的时间口径是固定常量：一天 6 小时（21600 秒）、一年 426 天（13 篇 §2.2）。
/// UT 本身保留原始字符串展示（存档里的 <c>5005129.6190400701</c> 是双精度十进制，四舍五入会丢位），
/// 这里只额外算一个"第 N 年第 M 天"的可读提示。
/// </remarks>
public static class SaveTimeText
{
    /// <summary>KSP 一天的游戏内秒数（6 小时）。</summary>
    public const double SecondsPerDay = 21600d;

    /// <summary>KSP 一年的天数。</summary>
    public const int DaysPerYear = 426;

    /// <summary>把 UT（秒）转成"第 N 年 第 M 天 HH:mm"；无法解析时返回 <c>—</c>。</summary>
    public static string UniversalTime(string? universalTime)
    {
        if (string.IsNullOrWhiteSpace(universalTime) ||
            !double.TryParse(universalTime.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var seconds) ||
            !double.IsFinite(seconds) ||
            seconds < 0d)
        {
            return "—";
        }

        var totalDays = (long)Math.Floor(seconds / SecondsPerDay);
        var year = (totalDays / DaysPerYear) + 1L;
        var day = (totalDays % DaysPerYear) + 1L;
        var secondsOfDay = seconds - (totalDays * SecondsPerDay);
        var hours = (int)(secondsOfDay / 3600d);
        var minutes = (int)((secondsOfDay - (hours * 3600d)) / 60d);
        return string.Format(
            CultureInfo.InvariantCulture,
            "第 {0} 年 第 {1} 天 {2:00}:{3:00}",
            year,
            day,
            hours,
            minutes);
    }

    /// <summary>文件体积：字节 / KB / MB，保留一位小数。</summary>
    public static string FileSize(long bytes)
    {
        if (bytes < 0)
        {
            return "—";
        }

        if (bytes < 1024)
        {
            return string.Format(CultureInfo.InvariantCulture, "{0} B", bytes);
        }

        if (bytes < 1024L * 1024L)
        {
            return string.Format(CultureInfo.InvariantCulture, "{0:0.0} KB", bytes / 1024d);
        }

        return string.Format(CultureInfo.InvariantCulture, "{0:0.0} MB", bytes / (1024d * 1024d));
    }

    /// <summary>资金：<c>√ 1,234,567</c>；解析不出来就原样显示（存档可能带小数）。</summary>
    public static string Funds(string? funds)
    {
        if (string.IsNullOrWhiteSpace(funds))
        {
            return "—";
        }

        var text = funds.Trim();
        return decimal.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
            ? "√ " + value.ToString("N0", CultureInfo.InvariantCulture)
            : text;
    }

    /// <summary>修改时间：本地时区 <c>yyyy-MM-dd HH:mm</c>（列表列宽固定）。</summary>
    public static string LocalTimestamp(DateTimeOffset timestamp)
    {
        return timestamp.ToLocalTime().ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);
    }
}
