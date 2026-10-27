using System.Globalization;

using CkanUI.Services.Abstractions.Dtos;

namespace CkanUI.ViewModels.Operations;

/// <summary>
/// 操作队列的文案格式化（03 篇 §11「数字/体积格式化集中一个 Formatters 类」）。
/// 全部显式使用 <see cref="CultureInfo.InvariantCulture"/>：体积与时长是技术量，不随界面语言变化，
/// 同时避免 CA1305 与"德语界面把小数点变逗号"造成的对拍困难。
/// </summary>
public static class OperationDisplayFormatter
{
    /// <summary>无法计算时统一显示的占位符（03 篇 §10：不显示空白，避免布局跳动）。</summary>
    public const string Placeholder = "—";

    private static readonly string[] ByteUnits = ["KB", "MB", "GB", "TB", "PB"];

    /// <summary>格式化字节数（1024 进制，与 Core 的下载体积口径一致）：<c>1536</c> → <c>1.5 KB</c>。</summary>
    /// <param name="bytes">字节数；负数视为无效。</param>
    /// <returns>可直接显示的体积文本。</returns>
    public static string FormatBytes(long bytes)
    {
        if (bytes < 0)
        {
            return Placeholder;
        }

        if (bytes < 1024)
        {
            return string.Format(CultureInfo.InvariantCulture, "{0} B", bytes);
        }

        double value = bytes;
        var unit = 0;
        do
        {
            value /= 1024;
            unit++;
        }
        while (value >= 1024 && unit < ByteUnits.Length);

        return string.Format(CultureInfo.InvariantCulture, "{0:0.#} {1}", value, ByteUnits[unit - 1]);
    }

    /// <summary>格式化"已传 / 总字节"：<c>12.4 MB / 37.9 MB</c>。</summary>
    /// <param name="transferred">已传字节。</param>
    /// <param name="total">总字节。</param>
    /// <returns>可直接显示的文本。</returns>
    public static string FormatBytePair(long transferred, long total)
    {
        if (transferred < 0 || total <= 0)
        {
            return Placeholder;
        }

        return string.Format(
            CultureInfo.InvariantCulture,
            "{0} / {1}",
            FormatBytes(transferred),
            FormatBytes(total));
    }

    /// <summary>格式化速率：<c>1.2 MB/s</c>；未知时返回 <see cref="Placeholder"/>。</summary>
    /// <param name="bytesPerSecond">每秒字节数；null、NaN 或无穷视为未知。</param>
    /// <returns>可直接显示的文本。</returns>
    public static string FormatSpeed(double? bytesPerSecond)
    {
        if (bytesPerSecond is not { } speed || double.IsNaN(speed) || double.IsInfinity(speed) || speed < 0)
        {
            return Placeholder;
        }

        if (speed < 1)
        {
            return "0 B/s";
        }

        return string.Format(CultureInfo.InvariantCulture, "{0}/s", FormatBytes((long)Math.Round(speed)));
    }

    /// <summary>格式化时长：<c>00:42</c>、<c>1:02:03</c>；null 返回 <see cref="Placeholder"/>。</summary>
    /// <param name="duration">时长。</param>
    /// <returns>可直接显示的文本。</returns>
    public static string FormatDuration(TimeSpan? duration)
    {
        if (duration is not { } value || value < TimeSpan.Zero)
        {
            return Placeholder;
        }

        var totalSeconds = (long)Math.Round(value.TotalSeconds, MidpointRounding.AwayFromZero);
        return totalSeconds >= 3600
            ? string.Format(CultureInfo.InvariantCulture, "{0}:{1:00}:{2:00}", totalSeconds / 3600, totalSeconds % 3600 / 60, totalSeconds % 60)
            : string.Format(CultureInfo.InvariantCulture, "{0:00}:{1:00}", totalSeconds / 60, totalSeconds % 60);
    }

    /// <summary>格式化剩余时间：<c>剩余 00:42</c>；未知时 <c>剩余 —</c>（不隐藏，避免行高跳动）。</summary>
    /// <param name="remaining">剩余时长。</param>
    /// <returns>可直接显示的文本。</returns>
    public static string FormatRemaining(TimeSpan? remaining)
        => string.Format(CultureInfo.InvariantCulture, "剩余 {0}", FormatDuration(remaining));

    /// <summary>格式化百分比：<c>42%</c>（四舍五入到整数，避免 100ms 刷新造成文字抖动）。</summary>
    /// <param name="percent">百分比，超出 0..100 会被夹紧。</param>
    /// <returns>可直接显示的文本。</returns>
    public static string FormatPercent(double percent)
        => string.Format(CultureInfo.InvariantCulture, "{0:0}%", Math.Clamp(percent, 0, 100));

    /// <summary>把任务的 <c>Kind</c> 翻成中文分组名（03 篇 §11：文案集中一处，便于后续迁 .resx）。</summary>
    /// <param name="kind">队列里的 Kind 原始值（服务层写入，可能是英文标识）。</param>
    /// <returns>中文分组名；未知 Kind 原样显示。</returns>
    public static string FormatKind(string? kind)
    {
        if (string.IsNullOrWhiteSpace(kind))
        {
            return "任务";
        }

        var value = kind.Trim();
        if (Matches(value, "download", "下载"))
        {
            return "下载";
        }

        if (Matches(value, "install", "安装"))
        {
            return "安装";
        }

        if (Matches(value, "uninstall", "remove", "卸载"))
        {
            return "卸载";
        }

        if (Matches(value, "update", "upgrade", "更新"))
        {
            return "更新";
        }

        if (Matches(value, "refresh", "reload", "刷新"))
        {
            return "刷新";
        }

        if (Matches(value, "export", "导出"))
        {
            return "导出";
        }

        if (Matches(value, "import", "导入"))
        {
            return "导入";
        }

        if (Matches(value, "verify", "check", "校验"))
        {
            return "校验";
        }

        if (Matches(value, "scan", "扫描"))
        {
            return "扫描";
        }

        if (Matches(value, "backup", "备份"))
        {
            return "备份";
        }

        if (Matches(value, "restore", "还原"))
        {
            return "还原";
        }

        return value;
    }

    /// <summary>把任务状态翻成中文（05 篇 §5：取消是"请求"，界面必须先显示"正在取消"再落到"已取消"）。</summary>
    /// <param name="state">任务状态。</param>
    /// <returns>中文状态文本。</returns>
    public static string FormatState(OperationState state) => state switch
    {
        OperationState.Pending => "等待中",
        OperationState.Running => "进行中",
        OperationState.Succeeded => "已完成",
        OperationState.Failed => "失败",
        OperationState.Cancelled => "已取消",
        _ => "未知",
    };

    private static bool Matches(string value, string english, string chinese)
        => value.Equals(english, StringComparison.OrdinalIgnoreCase) || value == chinese;

    private static bool Matches(string value, string english1, string english2, string chinese)
        => Matches(value, english1, chinese) || Matches(value, english2, chinese);
}
