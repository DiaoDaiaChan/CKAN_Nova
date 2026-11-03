namespace CkanUI.Plugin.Performance;

// 视图行模型：每轮轮询整体重建，因此是不可变 record（不做增量通知）。
// 之所以是"行"而不是"原始 DTO"：界面需要的是已格式化的 N/A 文案与 0–1 的占比，
// 格式化规则集中在这里，视图里就没有一根转换器。

/// <summary>一个实时指标块（值 + 近期走势）。</summary>
/// <param name="Title">指标名。</param>
/// <param name="ValueText">主数值（缺失时是 <c>N/A</c>）。</param>
/// <param name="DetailText">副信息（均值、分位数等）。</param>
/// <param name="Trend">近期序列（可为空）。</param>
/// <param name="HasTrend">是否有可画走势的数据（false 时视图不画空图）。</param>
/// <param name="TrendHint">走势来源说明。</param>
public sealed record PerformanceMetricRow(
    string Title,
    string ValueText,
    string DetailText,
    IReadOnlyList<double> Trend,
    bool HasTrend,
    string TrendHint);

/// <summary>分解条的一行。</summary>
/// <param name="Title">段名。</param>
/// <param name="MsText">毫秒文本。</param>
/// <param name="PctText">占比文本。</param>
/// <param name="Fraction">0–1 的占比（视图按它设条宽）。</param>
public sealed record PerformanceBreakdownRow(string Title, string MsText, string PctText, double Fraction);

/// <summary>GPU 五段的一行。</summary>
/// <param name="Label">段名（depth / opaque / … 的中文标签）。</param>
/// <param name="MsText">毫秒文本；<paramref name="Valid"/> 为 false 时是 <c>N/A</c>。</param>
/// <param name="Fraction">0–1 的条宽。</param>
/// <param name="Valid">该段计时是否被驱动接受。</param>
public sealed record PerformanceGpuStageRow(string Label, string MsText, double Fraction, bool Valid)
{
    /// <summary>有效性文案（视图直接显示，不在 XAML 里做转换）。</summary>
    public string ValidityText => Valid ? "有效" : "N/A";
}

/// <summary>热点榜的一行。</summary>
/// <param name="Name">名称（优先 friendly）。</param>
/// <param name="Assembly">程序集。</param>
/// <param name="MsText">耗时文本。</param>
/// <param name="PctText">占帧百分比文本。</param>
/// <param name="Extra">附加信息（调用次数 / 每帧分配 / 类型）。</param>
public sealed record PerformanceHotspotRow(string Name, string Assembly, string MsText, string PctText, string Extra);

/// <summary>诊断 culprits 的一行。</summary>
public sealed record PerformanceCulpritRow(string Source, string Name, string MsText, string PctText);

/// <summary>诊断 checks 的一行。</summary>
/// <param name="State">ok / warn / critical / unknown（视图据此配色）。</param>
public sealed record PerformanceCheckRow(string Label, string ValueText, string State)
{
    /// <summary>检查通过（视图用 Classes.ok 绑定）。</summary>
    public bool IsOk => State == "ok";

    /// <summary>检查告警。</summary>
    public bool IsWarn => State == "warn";

    /// <summary>检查严重。</summary>
    public bool IsCritical => State == "critical";
}

/// <summary>内存 top allocators 的一行。</summary>
public sealed record PerformanceAllocatorRow(string Name, string Assembly, string BytesText);

/// <summary>通用键值行（页头/内存/桥健康度共用）。</summary>
public sealed record PerformanceKeyValueRow(string Key, string Value);

/// <summary>掉帧尖峰的一行。</summary>
public sealed record PerformanceSpikeRow(string FrameText, string MsText, string Reason, string CulpritText);

/// <summary>热点榜页签。</summary>
/// <param name="Key">plugins / modules / parts / assemblies。</param>
/// <param name="Label">显示名。</param>
/// <param name="Count">该分类的条目数。</param>
/// <param name="IsSelected">是否为当前页签（视图据此高亮）。</param>
public sealed record PerformanceHotspotTab(string Key, string Label, int Count, bool IsSelected);
