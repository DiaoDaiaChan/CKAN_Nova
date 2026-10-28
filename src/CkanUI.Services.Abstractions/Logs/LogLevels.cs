namespace CkanUI.Services.Abstractions.Logs;

/// <summary>
/// 级别的序关系与文案。
/// 为什么单独放一个类：<see cref="LogLevel.User"/> 在枚举里排在 <see cref="LogLevel.Fatal"/> 之后（它是「通道」不是「严重度」），
/// 直接比较枚举值会把 User 判成最严重；所有需要「至少 WARN」这类判断的地方都必须走 <see cref="Severity"/>。
/// </summary>
public static class LogLevels
{
    /// <summary>级别在「严重度」维度上的位置（0 = 最不严重），User 按 Info 处理。</summary>
    public static int Severity(LogLevel level) => level switch
    {
        LogLevel.Trace => 0,
        LogLevel.Debug => 1,
        LogLevel.Info => 2,
        LogLevel.User => 2,
        LogLevel.Warn => 3,
        LogLevel.Error => 4,
        LogLevel.Fatal => 5,
        _ => 2,
    };

    /// <summary>是否达到给定最低严重度。</summary>
    public static bool IsAtLeast(LogLevel level, LogLevel minimum) =>
        Severity(level) >= Severity(minimum);

    /// <summary>是否为错误及以上（错误计数、诊断包 log.errors.txt 的判据）。</summary>
    public static bool IsError(LogLevel level) => level is LogLevel.Error or LogLevel.Fatal;

    /// <summary>是否为警告及以上。</summary>
    public static bool IsWarning(LogLevel level) => Severity(level) >= Severity(LogLevel.Warn);

    /// <summary>稳定英文标签（写进日志文件、诊断包、搜索语法 field:value 用，不能随语言变）。</summary>
    public static string Label(LogLevel level) => level switch
    {
        LogLevel.Trace => "TRACE",
        LogLevel.Debug => "DEBUG",
        LogLevel.Info => "INFO",
        LogLevel.Warn => "WARN",
        LogLevel.Error => "ERROR",
        LogLevel.Fatal => "FATAL",
        LogLevel.User => "USER",
        _ => "INFO",
    };

    /// <summary>按英文标签反解（旧 CKAN.log 导入用），大小写不敏感；无法识别返回 <see cref="LogLevel.Info"/>。</summary>
    public static LogLevel Parse(string? label) => label?.Trim().ToUpperInvariant() switch
    {
        "TRACE" or "VERBOSE" => LogLevel.Trace,
        "DEBUG" => LogLevel.Debug,
        "INFO" or "INFORMATION" => LogLevel.Info,
        "WARN" or "WARNING" => LogLevel.Warn,
        "ERROR" => LogLevel.Error,
        "FATAL" or "CRITICAL" => LogLevel.Fatal,
        "USER" => LogLevel.User,
        _ => LogLevel.Info,
    };
}
