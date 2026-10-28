namespace CkanUI.Services.Abstractions.Logs;

/// <summary>
/// 四路采集源（14 篇 §3.2）：log4net / IUser / 未捕获异常 / 子进程，外加我们自己的 UI 事件与旧日志导入。
/// 单独成枚举而不是只靠 Source 字符串前缀，是为了让过滤条与聚合可以按「路」统计（不靠字符串 StartsWith）。
/// </summary>
public enum LogSourceKind
{
    /// <summary>log4net appender 抓到的事件（来自 Core 与新 UI 的 logger）。</summary>
    Log4Net = 0,

    /// <summary>Core 的 <c>IUser</c> 消息（RaiseMessage / RaiseError / RaiseProgress）。</summary>
    User = 1,

    /// <summary>未捕获异常（AppDomain / TaskScheduler / Dispatcher）。</summary>
    Exception = 2,

    /// <summary>子进程（<c>ckan.exe</c> 策略下的 stdout/stderr）。</summary>
    Child = 3,

    /// <summary>我们自己的 UI 事件（导航失败、绑定错误、主题降级）。</summary>
    Ui = 4,

    /// <summary>从旧 <c>CKAN.log</c> 导入的历史行。</summary>
    Legacy = 5,
}
