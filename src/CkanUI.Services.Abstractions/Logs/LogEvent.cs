namespace CkanUI.Services.Abstractions.Logs;

/// <summary>
/// 一条日志的内部形状（14 篇 §3.1）：四路采集源汇流后的统一事件。
/// 不可变（record）——事件一旦入环形缓冲就不再变，UI 与诊断包共享同一份实例，任何可变字段都会造成「同一行两种内容」。
/// </summary>
public sealed record LogEvent
{
    /// <summary>单调递增序号，作为「证据行」引用（AI 结果按它回指日志行）。由 <see cref="ILogStore.Append"/> 赋值。</summary>
    public long Seq { get; init; }

    /// <summary>本地时间。为 <see langword="default"/> 时由 <see cref="ILogStore.Append"/> 打上当前时间（旧日志导入则自带原始时间）。</summary>
    public DateTime TimeLocal { get; init; }

    /// <summary>级别。</summary>
    public LogLevel Level { get; init; } = LogLevel.Info;

    /// <summary>语义族（入队时由规则表判定）。</summary>
    public LogFamily Family { get; init; } = LogFamily.Other;

    /// <summary>采集源类别。</summary>
    public LogSourceKind Kind { get; init; } = LogSourceKind.Ui;

    /// <summary>来源串，见 <see cref="LogSources"/>。</summary>
    public string Source { get; init; } = "ui";

    /// <summary>原文，不裁剪（展示层再省略；诊断包与 AI 分析要全文）。</summary>
    public string Message { get; init; } = string.Empty;

    /// <summary>异常的 ToString()、HTTP 响应体等附加细节。</summary>
    public string? Detail { get; init; }

    /// <summary>线程名 / 线程 id 串（log4net 与异常钩子能给到，UI 事件可空）。</summary>
    public string? Thread { get; init; }

    /// <summary>一次安装 / 一次刷新 / 一个任务的关联 id。</summary>
    public string? CorrelationId { get; init; }

    /// <summary>结构化标签（mod=xxx / instance=xxx / url=...）。</summary>
    public IReadOnlyDictionary<string, string>? Tags { get; init; }

    /// <summary>按名取标签（不存在返回 null），搜索语法 <c>mod:xxx</c> 走这里。</summary>
    public string? Tag(string name)
    {
        if (Tags is null || string.IsNullOrWhiteSpace(name))
        {
            return null;
        }

        foreach (var pair in Tags)
        {
            if (pair.Key.Equals(name, StringComparison.OrdinalIgnoreCase))
            {
                return pair.Value;
            }
        }

        return null;
    }

    /// <summary>构造一条时间与序号留给存储层填的事件（采集侧最常用）。</summary>
    public static LogEvent Create(
        LogSourceKind kind,
        LogLevel level,
        string source,
        string message,
        string? detail = null,
        string? thread = null,
        string? correlationId = null,
        IReadOnlyDictionary<string, string>? tags = null) => new()
        {
            Kind = kind,
            Level = level,
            Source = string.IsNullOrWhiteSpace(source) ? LogSources.Prefix(kind) : source,
            Message = message ?? string.Empty,
            Detail = detail,
            Thread = thread,
            CorrelationId = correlationId,
            Tags = tags,
        };
}
