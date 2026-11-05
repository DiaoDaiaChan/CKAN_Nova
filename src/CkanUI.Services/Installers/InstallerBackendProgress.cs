namespace CkanUI.Services.Installers;

/// <summary>进度所属阶段。用于双段上报（下载 n/N → 安装 m/M）和回滚复用。</summary>
public enum InstallerPhase
{
    /// <summary>下载/取缓存阶段。</summary>
    Download,

    /// <summary>安装/替换阶段。</summary>
    Install,

    /// <summary>卸载阶段。</summary>
    Remove,

    /// <summary>回滚阶段。</summary>
    Rollback,
}

/// <summary>
/// 后端上报的一次进度（05 篇 §4）：消息 + 0..100 的百分比。
/// <paramref name="BypassThrottle"/> 用于「一个模组装完」这类不能丢的关键事件（OneComplete 不节流）。
/// </summary>
public sealed record InstallerBackendProgress(string Message, double Percent, bool BypassThrottle = false)
{
    /// <summary>
    /// 阶段。默认 <see cref="InstallerPhase.Install"/>：既有构造点大多发生在安装段，
    /// 用 init 属性而不是位置参数，避免破坏已有构造点。
    /// </summary>
    public InstallerPhase Phase { get; init; } = InstallerPhase.Install;

    /// <summary>当前条目序号（1 起）；没有条目计数的阶段为 <see langword="null"/>。</summary>
    public int? ItemIndex { get; init; }

    /// <summary>本次阶段的条目总数；没有条目计数的阶段为 <see langword="null"/>。</summary>
    public int? ItemTotal { get; init; }
}
