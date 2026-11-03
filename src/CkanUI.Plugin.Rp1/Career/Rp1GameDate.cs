using System.Globalization;

namespace CkanUI.Plugin.Rp1.Career;

/// <summary>
/// RP-1 使用 Real Solar System 的 Earth 日历（RSSTimeFormatter 的 epoch = 1951-01-01，
/// Day=86400 秒、Year=31536000 秒），不是原版的 426 天/6 小时日历。
/// </summary>
public static class Rp1GameDate
{
    private static readonly DateTime Epoch = new(1951, 1, 1, 0, 0, 0, DateTimeKind.Unspecified);

    /// <summary>UT 秒 → 地球日期。</summary>
    public static DateTime FromUt(double ut) => Epoch.AddSeconds(ut);

    /// <summary>格式化成 yyyy-MM-dd HH:mm；NaN/无穷/越界返回「时间未知」。</summary>
    public static string Format(double? ut)
    {
        if (ut is null || double.IsNaN(ut.Value) || double.IsInfinity(ut.Value))
        {
            return "时间未知";
        }

        try
        {
            return FromUt(ut.Value).ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);
        }
        catch (ArgumentOutOfRangeException)
        {
            return "时间未知";
        }
    }
}
