using CkanUI.Localization;
using CkanUI.Services.Abstractions;

namespace CkanUI.Services.Lifecycle;

/// <summary>
/// L1 缓存释放：清空本地化的缺译与告警诊断表（18 篇 §3）。
/// </summary>
/// <remarks>
/// 为什么要释放它：每遇到一个没命中的 key，<see cref="ILocalizer.MissingKeys"/> 就多一行，
/// 而这张表只服务于「缺译清单」这一个诊断视图——长期运行的进程里它是唯一会无界增长的本地化状态。
/// 清空后下一次取译文会重新累计，用户可见的译文一个字都不会变。
/// </remarks>
public sealed class LocalizerCacheParticipant : IAppCacheParticipant
{
    private readonly ILocalizer _localizer;

    /// <summary>构造参与者。</summary>
    /// <param name="localizer">本地化器；它自己保证并发安全，这里直接调即可。</param>
    public LocalizerCacheParticipant(ILocalizer localizer)
    {
        _localizer = localizer ?? throw new ArgumentNullException(nameof(localizer));
    }

    /// <inheritdoc />
    public string Name => "本地化诊断表";

    /// <inheritdoc />
    public int TrimAppCache() => _localizer.ClearDiagnostics();
}
