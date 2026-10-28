namespace CkanUI.Services.Abstractions.Logs;

/// <summary>
/// 聚合视图的一组（14 篇 §4.2 ④）：级别 + 族 + 消息骨架相同的行折叠成「×N」。
/// Key 是稳定指纹，UI 用它做同一组的定位与展开。
/// </summary>
public sealed record LogFingerprintGroup(string Key, LogLevel Level, LogFamily Family, string Sample, int Count)
{
    /// <summary>首次出现的序号（点开后跳到第一条证据行）。</summary>
    public long FirstSeq { get; init; }

    /// <summary>最后出现的序号。</summary>
    public long LastSeq { get; init; }
}
