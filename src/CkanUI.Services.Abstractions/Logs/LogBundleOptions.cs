namespace CkanUI.Services.Abstractions.Logs;

/// <summary>
/// 诊断包参数（14 篇 §5.1）：默认最近 5,000 行，可调 500–50,000。
/// 上限 50k 的原因：更多行对排查没有帮助（真正的信息在错误段），却会把 zip 撑到几十 MB。
/// </summary>
public sealed record LogBundleOptions
{
    /// <summary>默认行数。</summary>
    public const int DefaultTailLines = 5_000;

    /// <summary>行数下限。</summary>
    public const int MinTailLines = 500;

    /// <summary>行数上限。</summary>
    public const int MaxTailLines = 50_000;

    /// <summary>期望的尾部行数。</summary>
    public int TailLines { get; init; } = DefaultTailLines;

    /// <summary>是否附上脱敏后的配置（config.redacted.json）。</summary>
    public bool IncludeConfig { get; init; } = true;

    /// <summary>是否附上 UI 自身事件（console.log）。</summary>
    public bool IncludeConsoleLog { get; init; } = true;

    /// <summary>默认参数。</summary>
    public static LogBundleOptions Default { get; } = new();

    /// <summary>把行数裁到合法区间。</summary>
    public int ClampTailLines() => Math.Clamp(TailLines, MinTailLines, MaxTailLines);
}
