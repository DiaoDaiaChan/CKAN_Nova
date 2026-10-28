using System.Globalization;

namespace CkanUI.Services.Downloads;

/// <summary>
/// 下载失败的自动重试策略：几次、间隔多久、「这个失败值不值得重试」的唯一判定点。
/// </summary>
/// <remarks>
/// <para>抽成独立类只为一件事：让重试语义可测。真实的等待会拖慢测试，
/// 因此测试传 <see cref="Immediate"/>，生产用 <see cref="Default"/>。</para>
/// <para><b>为什么有上限</b>：断网的机器重试到天荒地老只会让用户以为卡死；
/// 三次足够跨过瞬时限流 / 连接重置，超过就该把错误交给用户（05 篇 §5）。</para>
/// <para><b>为什么递增</b>：固定间隔会在服务器限流时形成整齐的重试风暴，
/// 400ms → 1200ms 的递增让服务器有恢复窗口，也让用户看得见「在重试」而不是「卡住」。</para>
/// </remarks>
public sealed class DownloadRetryPolicy
{
    /// <summary>默认最大尝试次数（含第一次）。</summary>
    public const int DefaultMaxAttempts = 3;

    /// <summary>默认退避：第 1 次失败后等 400ms，第 2 次失败后等 1200ms（毫秒）。</summary>
    private static readonly int[] DefaultDelaysMs = [400, 1200];

    private readonly int[] _delaysMs;

    /// <param name="maxAttempts">最大尝试次数（含第一次）；小于 1 时按 1 处理。</param>
    /// <param name="delaysMs">每次失败后的等待毫秒数；少于尝试次数时，末尾的等待一直复用最后一个值。</param>
    public DownloadRetryPolicy(int maxAttempts = DefaultMaxAttempts, IEnumerable<int>? delaysMs = null)
    {
        MaxAttempts = Math.Max(1, maxAttempts);
        _delaysMs = delaysMs is null ? DefaultDelaysMs : delaysMs.Where(ms => ms >= 0).ToArray();
    }

    /// <summary>生产默认：最多 3 次，400ms / 1200ms 递增退避。</summary>
    public static DownloadRetryPolicy Default { get; } = new();

    /// <summary>测试用：次数不变但不等待。</summary>
    public static DownloadRetryPolicy Immediate { get; } = new(delaysMs: Array.Empty<int>());

    /// <summary>单次下载最多尝试几次（含第一次）。</summary>
    public int MaxAttempts { get; }

    /// <summary>刚失败的那一次之后，是否还该再试一次。</summary>
    /// <param name="retryable">失败是否属于可重试类型（网络 / 限流）；磁盘与校验类一律 false。</param>
    /// <param name="completedAttempt">刚刚失败的这一次是第几次（从 1 开始）。</param>
    public bool ShouldRetry(bool retryable, int completedAttempt)
    {
        if (!retryable)
        {
            return false;
        }

        return completedAttempt < MaxAttempts;
    }

    /// <summary>第 <paramref name="completedAttempt"/> 次失败后要等多久再重试。</summary>
    public TimeSpan DelayBefore(int completedAttempt)
    {
        if (_delaysMs.Length == 0)
        {
            return TimeSpan.Zero;
        }

        var index = Math.Clamp(completedAttempt - 1, 0, _delaysMs.Length - 1);
        return TimeSpan.FromMilliseconds(_delaysMs[index]);
    }

    /// <inheritdoc />
    public override string ToString()
        => string.Create(CultureInfo.InvariantCulture, $"最大 {MaxAttempts} 次，退避 {string.Join("/", _delaysMs)} ms");
}
