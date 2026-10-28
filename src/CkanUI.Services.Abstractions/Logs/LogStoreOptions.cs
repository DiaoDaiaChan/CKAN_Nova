namespace CkanUI.Services.Abstractions.Logs;

/// <summary>
/// 环形缓冲配置（14 篇 §8 LOG-02：默认 20,000 行）。
/// 为什么默认 20k 而不是无限：一次依赖求解 + 下载 + 安装的真实会话通常在 2k~8k 行，
/// 20k 足够覆盖「安装开始前的准备工作」，而内存占用约 20k × (几百字节) ≈ 10MB 以内。
/// </summary>
public sealed record LogStoreOptions
{
    /// <summary>默认容量（行）。</summary>
    public const int DefaultCapacity = 20_000;

    /// <summary>容量下限，防止误配成 1 行把日志页做成「只看最后一行」。</summary>
    public const int MinCapacity = 100;

    /// <summary>容量上限，防止误配成千万行导致 OOM。</summary>
    public const int MaxCapacity = 1_000_000;

    /// <summary>缓冲容量（行）。</summary>
    public int Capacity { get; init; } = DefaultCapacity;

    /// <summary>把容量裁到合法区间。</summary>
    public LogStoreOptions Clamp() => this with { Capacity = Math.Clamp(Capacity, MinCapacity, MaxCapacity) };

    /// <summary>默认配置。</summary>
    public static LogStoreOptions Default { get; } = new();
}
