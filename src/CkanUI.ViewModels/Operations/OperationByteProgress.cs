using System.Globalization;
using System.Text.RegularExpressions;

namespace CkanUI.ViewModels.Operations;

/// <summary>
/// 从任务消息里解析"已传 / 总字节"。
/// </summary>
/// <remarks>
/// 为什么用解析而不是直接读字段：<c>OperationProgress</c>（04 篇 §6 / 契约冻结）只有
/// <c>TaskId, Kind, Title, Message, Percent, CanCancel, State</c>，没有字节数。
/// 而 03 篇 §10 的进度文案形态（<c>正在恢复数据 12.4 MB / 37.9 MB</c>）与 UI-08 的"已传/总字节、速率、剩余时间"
/// 都要求这两个数。在契约不扩充的前提下，只能从消息里取；取不到就整行隐藏字节列（<see cref="HasValue"/> 为 false），
/// 绝不用 0 冒充"未知"。契约若要补 <c>BytesTransferred/BytesTotal</c>，只需改本类型的构造入口。
/// </remarks>
public readonly partial record struct OperationByteProgress(bool HasValue, long Transferred, long Total)
{
    /// <summary>"没有字节信息"的取值（<see cref="HasValue"/> 为 false）。</summary>
    public static OperationByteProgress None => new(false, 0, 0);

    /// <summary>尝试从消息文本里解析字节进度。</summary>
    /// <param name="message">队列里的 <c>Message</c>，可能为 null。</param>
    /// <param name="result">解析结果；失败时为 <see cref="None"/>。</param>
    /// <returns>成功解析出"已传 + 总量"时为 true。</returns>
    public static bool TryParse(string? message, out OperationByteProgress result)
    {
        result = None;
        if (string.IsNullOrWhiteSpace(message))
        {
            return false;
        }

        var matches = SizePattern().Matches(message);
        if (matches.Count < 2)
        {
            // 只有单个体积（如"正在安装 3.2 MB"）无法推出总量，按未知处理
            return false;
        }

        var transferred = ToBytes(matches[0]);
        var total = ToBytes(matches[1]);
        if (transferred < 0 || total <= 0 || transferred > total)
        {
            return false;
        }

        result = new OperationByteProgress(true, transferred, total);
        return true;
    }

    private static long ToBytes(Match match)
    {
        var text = match.Groups["value"].Value;
        if (text.Contains('.'))
        {
            // "1.234,5" 之类的混排不做支持，直接判无效
            if (text.Contains(','))
            {
                return -1;
            }
        }
        else
        {
            text = text.Replace(',', '.');
        }

        if (!double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) || value < 0)
        {
            return -1;
        }

        var unit = match.Groups["unit"].Value;
        var multiplier = unit switch
        {
            _ when unit.Equals("B", StringComparison.OrdinalIgnoreCase) => 1L,
            _ when unit.Equals("KB", StringComparison.OrdinalIgnoreCase) || unit.Equals("KiB", StringComparison.OrdinalIgnoreCase) => 1024L,
            _ when unit.Equals("MB", StringComparison.OrdinalIgnoreCase) || unit.Equals("MiB", StringComparison.OrdinalIgnoreCase) => 1024L * 1024L,
            _ when unit.Equals("GB", StringComparison.OrdinalIgnoreCase) || unit.Equals("GiB", StringComparison.OrdinalIgnoreCase) => 1024L * 1024L * 1024L,
            _ when unit.Equals("TB", StringComparison.OrdinalIgnoreCase) || unit.Equals("TiB", StringComparison.OrdinalIgnoreCase) => 1024L * 1024L * 1024L * 1024L,
            _ => 0L,
        };

        if (multiplier == 0)
        {
            return -1;
        }

        var bytes = value * multiplier;
        return bytes > long.MaxValue ? -1 : (long)Math.Round(bytes, MidpointRounding.AwayFromZero);
    }

    // 单位长的排前面：正则分支按书写顺序匹配，先写 KiB/MiB 再写 B 才不会被截断成 "K" + "iB"
    [GeneratedRegex(@"(?<value>\d+(?:[.,]\d+)?)\s*(?<unit>KiB|MiB|GiB|TiB|KB|MB|GB|TB|B)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex SizePattern();
}
