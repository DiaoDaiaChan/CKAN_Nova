using System;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using CKAN;

using CkanUI.Services.Abstractions;
using CkanUI.Services.Logging;

namespace CkanUI.Services.Users;

/// <summary>
/// 把 CKAN Core 的同步用户回调封送到桌面服务（02 篇 §4 契约表）。
/// 这是 Core 与 UI 之间唯一的「反向调用」通道：Core 的安装/下载线程会主动要对话框。
/// </summary>
/// <remarks>
/// 三条纪律（05 篇 §2/§3）：
/// 1. 要返回值的同步回调（YesNo / Selection）必须 Invoke 语义，不能用 Post；
/// 2. 但**在 UI 线程上同步等一个模态对话框必然死锁**——窗口的消息循环正是被我们阻塞的那条线程。
///    所以 UI 线程上直接返回安全默认值（YesNo=false、Selection=-1）并记警告，
///    让 Core 走「用户拒绝」分支。这比死锁或静默自动确认都安全。
/// 3. 高频进度（RaiseProgress）必须节流，否则会把 UI 线程淹掉。
/// </remarks>
public sealed class AvaloniaUser : IUser
{
    private const int ProgressIntervalMilliseconds = 100;

    private readonly IDispatcher _dispatcher;
    private readonly IDialogService _dialogs;
    private readonly IErrorReporter _errors;
    private readonly log4net.ILog _log = LoggingSetup.GetLogger(LoggingSetup.UiLoggerName);

    private string? _recentMessage;
    private long _lastProgressTicks;
    private string? _lastProgressMessage;
    private int _lastProgressPercent = -1;

    public AvaloniaUser(IDispatcher dispatcher, IDialogService dialogs, IErrorReporter errors)
    {
        ArgumentNullException.ThrowIfNull(dispatcher);
        ArgumentNullException.ThrowIfNull(dialogs);
        ArgumentNullException.ThrowIfNull(errors);
        _dispatcher = dispatcher;
        _dialogs = dialogs;
        _errors = errors;
    }

    /// <inheritdoc />
    public bool Headless => false;

    /// <summary>最近一条 Core 消息（状态栏展示用）。</summary>
    public string? RecentMessage => Volatile.Read(ref _recentMessage);

    /// <summary>最近一次被接受的进度（节流后的值），供状态栏绑定。</summary>
    public (string Message, int Percent)? LastProgress { get; private set; }

    /// <inheritdoc />
    public bool RaiseYesNoDialog(string question)
    {
        if (_dispatcher.IsOnUiThread)
        {
            _log.Warn($"Core 在 UI 线程上要求确认对话框，已按拒绝处理以避免死锁：{question}");
            return false;
        }

        return _dispatcher.InvokeAsync(() => _dialogs.ConfirmAsync("确认", question))
            .Unwrap()
            .GetAwaiter()
            .GetResult();
    }

    /// <inheritdoc />
    public int RaiseSelectionDialog(string message, params object[] args)
    {
        var options = (args ?? Array.Empty<object>()).Select(x => x?.ToString() ?? string.Empty).ToArray();
        if (_dispatcher.IsOnUiThread)
        {
            _log.Warn($"Core 在 UI 线程上要求选择对话框，已按取消处理以避免死锁：{message}");
            return -1;
        }

        return _dispatcher.InvokeAsync(() => _dialogs.SelectAsync("选择", message, options))
            .Unwrap()
            .GetAwaiter()
            .GetResult();
    }

    /// <inheritdoc />
    public void RaiseError(string message, params object[] args)
    {
        // 只 Post 不 Invoke：错误提示不需要 Core 等结果，Invoke 会在 Core 持锁时把 UI 线程拖进死锁
        try
        {
            var text = Format(message, args);
            _dispatcher.Post(() => _errors.Report("CKAN", text));
        }
        catch (Exception exception)
        {
            _log.Error("处理 Core 错误消息失败", exception);
        }
    }

    /// <inheritdoc />
    public void RaiseProgress(string message, int percent)
    {
        var now = Environment.TickCount64;
        var last = Interlocked.Read(ref _lastProgressTicks);
        if (now - last < ProgressIntervalMilliseconds && percent != 100)
        {
            return;
        }

        Interlocked.Exchange(ref _lastProgressTicks, now);
        _lastProgressMessage = message;
        _lastProgressPercent = percent;
        LastProgress = (message, percent);
        _log.Debug($"Core 进度 {percent}%：{message}");
    }

    /// <inheritdoc />
    public void RaiseProgress(ByteRateCounter rateCounter)
    {
        try
        {
            RaiseProgress(rateCounter.Summary, rateCounter.Percent);
        }
        catch (Exception exception)
        {
            _log.Error("读取 Core 下载进度失败", exception);
        }
    }

    /// <inheritdoc />
    public void RaiseMessage(string message, params object[] args)
    {
        try
        {
            var text = Format(message, args);
            Volatile.Write(ref _recentMessage, text);
            _dispatcher.Post(() => Volatile.Write(ref _recentMessage, text));
            _log.Info(text);
        }
        catch (Exception exception)
        {
            _log.Error("处理 Core 消息失败", exception);
        }
    }

    private static string Format(string message, object[]? args)
    {
        if (args is null || args.Length == 0)
        {
            return message;
        }

        try
        {
            return string.Format(CultureInfo.CurrentCulture, message, args);
        }
        catch (FormatException)
        {
            // Core 的格式串与参数不匹配时不要抛给 Core 线程，原样返回即可
            return message;
        }
    }
}
