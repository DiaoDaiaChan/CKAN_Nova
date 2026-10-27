namespace CkanUI.Services.Installers;

/// <summary>
/// 后端上报的一次进度（05 篇 §4）：消息 + 0..100 的百分比。
/// <paramref name="BypassThrottle"/> 用于「一个模组装完」这类不能丢的关键事件（OneComplete 不节流）。
/// </summary>
public sealed record InstallerBackendProgress(string Message, double Percent, bool BypassThrottle = false);
