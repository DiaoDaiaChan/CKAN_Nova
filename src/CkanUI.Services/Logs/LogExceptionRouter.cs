using System.Threading.Tasks;

using CkanUI.Services.Abstractions.Logs;

namespace CkanUI.Services.Logs;

/// <summary>
/// 未捕获异常钩子（14 篇 §3.2 ③）：AppDomain 与 TaskScheduler 由服务层挂，
/// UI 线程的 Dispatcher 钩子必须由 App 层调 <see cref="Report"/>（那需要 Avalonia 类型，服务层不许引用）。
/// </summary>
/// <remarks>
/// 为什么三处都挂：Core 的下载/求解在后台线程，异常走 AppDomain；
/// async 里漏 await 的 Task 异常只在 GC 时通过 UnobservedTaskException 出现——那是「上次到底为什么没反应」最真实的证据。
/// </remarks>
public sealed class LogExceptionRouter : IDisposable
{
    private readonly ILogStore _store;
    private int _attached;
    private long _reported;

    /// <summary>构造路由。</summary>
    public LogExceptionRouter(ILogStore store)
    {
        ArgumentNullException.ThrowIfNull(store);
        _store = store;
    }

    /// <summary>已上报条数（单测断言钩子确实生效）。</summary>
    public long ReportedCount => Interlocked.Read(ref _reported);

    /// <summary>是否已挂钩。</summary>
    public bool IsAttached => Volatile.Read(ref _attached) == 1;

    /// <summary>挂 AppDomain.UnhandledException 与 TaskScheduler.UnobservedTaskException（幂等）。</summary>
    public void Attach()
    {
        if (Interlocked.Exchange(ref _attached, 1) == 1)
        {
            return;
        }

        AppDomain.CurrentDomain.UnhandledException += OnAppDomainException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;
    }

    /// <summary>摘钩（测试与优雅退出用）。</summary>
    public void Detach()
    {
        if (Interlocked.Exchange(ref _attached, 0) == 0)
        {
            return;
        }

        AppDomain.CurrentDomain.UnhandledException -= OnAppDomainException;
        TaskScheduler.UnobservedTaskException -= OnUnobservedTaskException;
    }

    /// <inheritdoc />
    public void Dispose() => Detach();

    /// <summary>上报一个异常（App 层 Dispatcher.UnhandledException、我们自己的 catch 兜底都走它）。</summary>
    public void Report(Exception? exception, string origin, string? context = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(origin);
        var message = exception is null
            ? $"未捕获异常（{origin}）"
            : $"{exception.GetType().Name}: {exception.Message}";
        var detail = exception?.ToString();

        var tags = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["origin"] = origin };
        if (!string.IsNullOrWhiteSpace(context))
        {
            tags["context"] = context;
        }

        Write(LogLevel.Error, origin, message, detail, tags, exception);
    }

    /// <summary>上报一条任意级别的事件（UI 事件、导航失败、绑定错误都用它）。</summary>
    public void ReportMessage(string origin, LogLevel level, string message, string? detail = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(origin);
        var tags = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["origin"] = origin };
        Write(level, origin, message, detail, tags, null);
    }

    private void Write(
        LogLevel level,
        string origin,
        string message,
        string? detail,
        IReadOnlyDictionary<string, string> tags,
        Exception? exception)
    {
        try
        {
            _store.Append(LogEvent.Create(
                exception is null ? LogSourceKind.Ui : LogSourceKind.Exception,
                level,
                LogSources.Encode(exception is null ? LogSourceKind.Ui : LogSourceKind.Exception, origin),
                message,
                detail,
                Environment.CurrentManagedThreadId.ToString(System.Globalization.CultureInfo.InvariantCulture),
                null,
                tags));
            Interlocked.Increment(ref _reported);
        }
        catch (Exception)
        {
            // 上报异常本身绝不能抛：那会把「未捕获异常」变成「报告未捕获异常时又炸一次」。
            System.Diagnostics.Debug.WriteLine("LogExceptionRouter 上报失败");
        }
    }

    private void OnAppDomainException(object? sender, UnhandledExceptionEventArgs e)
    {
        var origin = e.IsTerminating ? "appdomain-terminating" : "appdomain";
        Report(e.ExceptionObject as Exception, origin, e.IsTerminating ? "进程即将终止" : null);
    }

    private void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        // 标记已观察，避免默认策略升级为进程终止（.NET 4.5+ 默认不终止，但宿主可改策略）。
        e.SetObserved();
        Report(e.Exception, "task", "未观察的 Task 异常");
    }
}
