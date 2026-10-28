namespace CkanUI.Services.Abstractions.Logs;

/// <summary>
/// 一条分类规则（14 篇 §3.3）：正则 + 族 + 权重。
/// 为什么带权重而不是「按文件顺序取第一命中」：同一行常同时命中多族
/// （例如「下载失败：The underlying connection was closed」既像 Net 又像 Download），
/// 权重让规则表本身表达优先级，调整规则时不用重排整个文件。
/// </summary>
public sealed record LogRule
{
    /// <summary>归属族。</summary>
    public LogFamily Family { get; init; } = LogFamily.Other;

    /// <summary>权重，越大越优先（默认 100）。</summary>
    public int Weight { get; init; } = 100;

    /// <summary>匹配用的正则（大小写不敏感，对「级别 + 来源 + 原文 + Detail」拼接后的文本执行）。</summary>
    public string Pattern { get; init; } = string.Empty;

    /// <summary>
    /// 字面量预筛词（任意一个出现在文本里才跑正则）。
    /// 为什么需要它：把旧日志的 10 万行一次导入时，若每条规则都对每行跑正则，仅分类就要几秒；
    /// 先用 <see cref="string.Contains(string, StringComparison)"/> 做一趟便宜筛选，命中率低的规则几乎零成本。
    /// 为空表示不预筛（纯正则规则）。
    /// </summary>
    public IReadOnlyList<string>? Keywords { get; init; }

    /// <summary>人读说明（规则表 JSON 里写的注释，热更新排查问题时用）。</summary>
    public string? Description { get; init; }
}
