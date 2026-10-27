using CkanUI.Services.Abstractions;
using CkanUI.Services.Logging;

namespace CkanUI.Services.Errors;

/// <summary>汇总日志与最近错误并在 UI 线程广播（ARC-04）。</summary>
public sealed class ErrorReporter : IErrorReporter
{
    private readonly IDispatcher _dispatcher;
    private readonly int _capacity;
    private readonly object _gate = new();
    private readonly Queue<ErrorEntry> _entries = new();
    private readonly log4net.ILog _log = LoggingSetup.GetLogger("CKAN.UI");
    public ErrorReporter(IDispatcher dispatcher, int capacity = 200) { _dispatcher = dispatcher; _capacity = Math.Max(1, capacity); }
    public event Action<ErrorEntry>? Reported;
    /// <summary>返回时间倒序快照，最近记录排在首位。</summary>
    public IReadOnlyList<ErrorEntry> Recent { get { lock (_gate) return _entries.Reverse().ToArray(); } }
    public void Report(Exception exception, string context, string? userMessage = null)
    { try { string detail; try { detail = exception.ToString(); } catch { detail = exception.GetType().FullName ?? "Exception serialization failed"; } var entry = new ErrorEntry(DateTime.UtcNow, context, userMessage ?? "发生错误", detail, exception); try { _log.Error($"{context}: {entry.UserMessage}", exception); } catch { } Add(entry); } catch { } }
    public void Report(string context, string userMessage, string? detail = null)
    { try { var entry = new ErrorEntry(DateTime.UtcNow, context, userMessage, detail, null); try { _log.Error($"{context}: {userMessage}{(detail is null ? string.Empty : " — " + detail)}"); } catch { } Add(entry); } catch { } }
    private void Add(ErrorEntry entry) { lock (_gate) { _entries.Enqueue(entry); while (_entries.Count > _capacity) _entries.Dequeue(); } try { _dispatcher.Post(() => { try { Reported?.Invoke(entry); } catch { } }); } catch { } }
}
