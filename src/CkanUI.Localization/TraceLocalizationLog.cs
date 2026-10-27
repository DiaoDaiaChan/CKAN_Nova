namespace CkanUI.Localization;

/// <summary>
/// 默认日志实现：写 <see cref="System.Diagnostics.Trace"/>，随 App 的 LogToTrace 落盘/调试器可见。
/// App 若已配置 log4net，应在 <see cref="LocalizationOptions.Log"/> 里换成 log4net 的 Warn。
/// </summary>
public sealed class TraceLocalizationLog : ILocalizationLog
{
    /// <summary>全局单例。</summary>
    public static TraceLocalizationLog Instance { get; } = new();

    /// <inheritdoc />
    public void Warn(string message) => System.Diagnostics.Trace.TraceWarning(message);
}
