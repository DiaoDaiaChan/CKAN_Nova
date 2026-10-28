using CkanUI.Services.Abstractions.Logs;

namespace CkanUI.ViewModels.Pages.Logs;

/// <summary>
/// 时间轴条的一根柱子（14 篇 §4.2⑤）：右缘细柱状图，错误红点、警告黄点，点击定位。
/// 高度按当前窗口内最大值归一化，避免一条错误把其余柱子压平。
/// </summary>
public sealed class LogTimelineBarViewModel
{
    /// <summary>构造一根柱子；<paramref name="maxTotal"/> 是窗口内的最大条数，用于归一化。</summary>
    public LogTimelineBarViewModel(LogTimelineBucket bucket, int maxTotal)
    {
        Bucket = bucket ?? throw new ArgumentNullException(nameof(bucket));
        MaxTotal = Math.Max(1, maxTotal);
    }

    /// <summary>服务层原始桶。</summary>
    public LogTimelineBucket Bucket { get; }

    /// <summary>时间轴一列的最大像素高度。</summary>
    public const double MaxBarHeight = 72.0;

    /// <summary>归一化用的最大值。</summary>
    public int MaxTotal { get; }

    /// <summary>桶起始时间。</summary>
    public DateTime StartLocal => Bucket.StartLocal;

    /// <summary>错误条数。</summary>
    public int ErrorCount => Bucket.ErrorCount;

    /// <summary>警告条数。</summary>
    public int WarningCount => Bucket.WarningCount;

    /// <summary>其他条数。</summary>
    public int OtherCount => Bucket.OtherCount;

    /// <summary>总条数。</summary>
    public int Total => Bucket.Total;

    /// <summary>柱高占满格的比例（0~1）。</summary>
    public double HeightRatio => (double)Total / MaxTotal;

    /// <summary>柱高像素（XAML 直接绑，省掉一个转换器；最小 2px 保证 0 条时也有基线可见）。</summary>
    public double BarHeight => Math.Max(2.0, HeightRatio * MaxBarHeight);

    /// <summary>有错误（画红点）。</summary>
    public bool HasError => ErrorCount > 0;

    /// <summary>有警告（画黄点）。</summary>
    public bool HasWarning => WarningCount > 0;

    /// <summary>悬停提示：时间 + 各级计数。</summary>
    public string TipText => $"{StartLocal:HH:mm:ss} 共 {Total} 条（错误 {ErrorCount}、警告 {WarningCount}）";
}
